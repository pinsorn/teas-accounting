using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Accounting.Api.Mcp;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Identity;
using Accounting.Application.Master;
using Accounting.Infrastructure.Persistence;
using Microsoft.AspNetCore;               // HttpContext.GetOpenIddictServerRequest()
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;

namespace Accounting.Api.OAuth;

public static class OAuthEndpoints
{
    /// <summary>
    /// RFC 9728 protected-resource metadata — ANONYMOUS. An MCP client that gets a 401 with
    /// <c>WWW-Authenticate: Bearer resource_metadata="…"</c> fetches this to discover the AS + scopes.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthMetadata(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", (IOptions<AppOptions> opt) =>
        {
            var baseUrl = opt.Value.BaseUrl.TrimEnd('/');
            return Results.Json(new
            {
                resource = $"{baseUrl}/mcp",
                authorization_servers = new[] { baseUrl },
                scopes_supported = McpScopes.All,
                bearer_methods_supported = new[] { "header" },
            });
        })
        .AllowAnonymous()
        .WithName("OAuthProtectedResource");

        return app;
    }

    /// <summary>
    /// The interactive authorize + consent bridge (spec §3, Codex blocker #3). OpenIddict validated
    /// client_id + exact registered redirect_uri + PKCE S256 + resource before this runs.
    ///
    /// MECHANISM DEVIATION from blocker #3 (flag for Ham/Codex): OpenIddict 7.5 REMOVED
    /// EnableAuthorizationRequestCaching, so the opaque-handle cache it prescribed is unavailable.
    /// The security GOAL (browser-carried params can't be tampered to escalate) is instead met by
    /// OpenIddict RE-VALIDATING client_id + exact registered redirect_uri + PKCE on the POST, the
    /// server-side scope Normalize (never *.post), and the company-membership check. Tampering the
    /// round-tripped params yields rejection/capping, never escalation.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthAuthorize(this IEndpointRouteBuilder app)
    {
        // GET — login-gate then hand off to the Next consent page (company picker). Anonymous: the
        // handler itself decides login-vs-consent (the fallback auth policy would 401 before that).
        app.MapMethods("/oauth/authorize", ["GET"], (
            HttpContext http, ITenantContext tenant, IOptions<AppOptions> opt) =>
        {
            _ = http.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenIddict authorization request cannot be retrieved.");

            var baseUrl = opt.Value.BaseUrl.TrimEnd('/');
            // Forward the validated authorize params to the consent page; it re-POSTs them to the
            // accept endpoint where OpenIddict re-validates them (see the deviation note above).
            var consent = $"/oauth/consent{http.Request.QueryString.Value}";

            // Not logged in (no session Bearer forwarded by the BFF) → Next login, returning to consent.
            if (tenant.UserId is not { } userId || userId <= 0)
                return Results.Redirect($"{baseUrl}/login?returnTo={Uri.EscapeDataString(consent)}");

            return Results.Redirect($"{baseUrl}{consent}");
        })
        .AllowAnonymous()
        .WithName("OAuthAuthorize");

        // POST — the consent "accept" (BFF-forwarded session Bearer + antiforgery/Origin at the BFF).
        // Posts to the SAME authorize path so GetOpenIddictServerRequest() rehydrates the cached
        // request → SignIn issues the code. The consenting user IS the token subject; the posted
        // company is validated against THAT user's memberships (never trusted blindly).
        app.MapMethods("/oauth/authorize", ["POST"], async (
            HttpContext http, ITenantContext tenant, ICompanyService companies,
            AccountingDbContext db, IActivityRecorder activity, IPermissionLookup permissions,
            IOptions<AppOptions> opt, CancellationToken ct) =>
        {
            var request = http.GetOpenIddictServerRequest()
                ?? throw new InvalidOperationException("The OpenIddict authorization request cannot be retrieved.");

            if (tenant.UserId is not { } userId || userId <= 0)
                return Forbid();

            var form = await http.Request.ReadFormAsync(ct);
            if (!string.Equals(form["approve"], "true", StringComparison.OrdinalIgnoreCase))
                return Forbid();   // deny → never construct a code manually
            if (!int.TryParse(form["company_id"], out var companyId) || companyId <= 0)
                return Results.BadRequest(new { error = "invalid_company" });

            // Membership: a super-admin may grant for any ACTIVE company; a normal user only their own.
            // (The token NEVER carries is_super_admin — even a super-admin's MCP token is tenant-scoped.)
            var allowed = tenant.IsSuperAdmin
                ? (await companies.ListAsync(ct)).Any(c => c.CompanyId == companyId && c.IsActive)
                : tenant.CompanyId == companyId;
            if (!allowed)
                return Forbid();

            // HQ-first active branch of the chosen company (mirror CompanySwitchService). Reject if none.
            // master.branches carries RLS (G2) keyed on the SESSION's pinned app.company_id (the
            // caller's own company, not necessarily the one they're granting for) — same LOCAL-only
            // app.bypass_rls pin as CompanySwitchService, scoped to just this short transaction.
            int branchId;
            await using (var tx = await db.Database.BeginTransactionAsync(ct))
            {
                await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'true', true)", ct);
                branchId = await db.Branches.IgnoreQueryFilters()
                    .Where(b => b.CompanyId == companyId && b.IsActive)
                    .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.BranchId)
                    .Select(b => (int?)b.BranchId).FirstOrDefaultAsync(ct) ?? 0;
                await tx.CommitAsync(ct);
            }
            if (branchId <= 0)
                return Results.BadRequest(new { error = "company_has_no_active_branch" });

            // Authoritative grant: requested ∩ McpScopes (structurally drops unknown + every *.post).
            var granted = McpScopes.Normalize(request.GetScopes());
            if (granted.Count == 0)
                return Results.BadRequest(new { error = "invalid_scope" });

            // H4 — a token may never carry authority the consenting user lacks interactively. A super-admin
            // holds no explicit permission rows (they bypass RBAC everywhere: PermissionHandler.cs:36,
            // CompanySwitchService.cs:49-51), so intersecting would zero their grant — grant them the full
            // valid set instead. A regular user's grant is capped to their effective RBAC for THIS company.
            if (!tenant.IsSuperAdmin)
            {
                var (_, perms) = await permissions.LoadAsync(userId, companyId, ct);
                granted = McpConsentScopes.FilterToRbac(granted, perms.ToHashSet(StringComparer.Ordinal));
                if (granted.Count == 0)
                    return Results.BadRequest(new { error = "invalid_scope" });
            }

            var resource = $"{opt.Value.BaseUrl.TrimEnd('/')}/mcp";
            var actor = $"oauth:{tenant.Username ?? userId.ToString()}";
            var principal = McpPrincipalFactory.Build(userId, actor, companyId, branchId, granted, resource);

            // offline_access → OpenIddict issues a rotating refresh token (long-running agents). Added
            // to the principal's OpenIddict scopes only — NOT the CSV `scopes` claim (it is not a tool
            // scope). company_id stays baked in the principal, so a refresh keeps the ORIGINAL company.
            if (request.HasScope(OpenIddictConstants.Scopes.OfflineAccess))
                principal.SetScopes(principal.GetScopes().Append(OpenIddictConstants.Scopes.OfflineAccess));

            // §4.8 audit — user + client + company (token id assigned by OpenIddict downstream).
            // audit.activity_log carries RLS (G3, since 585) keyed on the caller's own pinned
            // company_id, not necessarily the grant's target company — same LOCAL bypass pin.
            activity.Record(
                entityType: "oauth_grant", entityId: companyId, docNo: request.ClientId,
                companyId: companyId, action: "oauth_authorize",
                note: $"user {userId} granted an MCP token for company {companyId} to client " +
                      $"'{request.ClientId}' — scopes [{string.Join(' ', granted)}]",
                module: "identity");
            await using (var tx2 = await db.Database.BeginTransactionAsync(ct))
            {
                await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.bypass_rls', 'true', true)", ct);
                await db.SaveChangesAsync(ct);
                await tx2.CommitAsync(ct);
            }

            return Results.SignIn(principal, properties: null,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
        .RequireAuthorization()   // must carry the BFF-forwarded session Bearer
        .WithName("OAuthAuthorizeAccept");

        return app;

        static IResult Forbid() => Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }

    /// <summary>
    /// RFC 7591 Dynamic Client Registration — ANONYMOUS POST. OpenIddict 7.5 has no native DCR
    /// (verified: OpenIddict.Server.dll has zero registration handlers), so this is hand-rolled and
    /// hardened: the request's scope/grants are IGNORED, redirect_uris are https/loopback-only, the
    /// created client gets the fixed policy via McpClientDescriptorFactory, the client_id is
    /// deterministic (dedup), and the endpoint is rate-limited (anonymous + world-reachable).
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthRegister(this IEndpointRouteBuilder app)
    {
        app.MapMethods("/oauth/register", ["POST"], async (
            HttpContext http, DcrRequest? body,
            IOpenIddictApplicationManager appMgr, IOptions<AppOptions> opt,
            CancellationToken ct) =>
        {
            var raw = body?.RedirectUris ?? [];
            if (raw.Count == 0)
                return DcrError("invalid_redirect_uri", "redirect_uris is required and must be non-empty.");

            var uris = new List<Uri>(raw.Count);
            foreach (var s in raw)
            {
                if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || !IsAllowedRedirectUri(u))
                    return DcrError("invalid_redirect_uri",
                        $"redirect_uri '{s}' must be an https URI or a loopback http URI.");
                uris.Add(u);
            }

            var clientId    = DeterministicClientId(uris);
            var mcpResource = $"{opt.Value.BaseUrl.TrimEnd('/')}/mcp";
            var displayName = string.IsNullOrWhiteSpace(body?.ClientName) ? "TEAS Connect (DCR)" : body!.ClientName!;
            var descriptor  = McpClientDescriptorFactory.Build(clientId, displayName, mcpResource, uris);

            // Find-or-create, idempotent + concurrency-safe. UpdateAsync-on-hit re-asserts the
            // server-fixed policy (a future McpScopes tightening propagates), mirroring the seeder.
            var existing = await appMgr.FindByClientIdAsync(clientId, ct);
            if (existing is not null)
            {
                await appMgr.UpdateAsync(existing, descriptor, ct);
            }
            else
            {
                try { await appMgr.CreateAsync(descriptor, ct); }
                catch (Exception)   // concurrent create → the unique client_id index rejected us
                {
                    var raced = await appMgr.FindByClientIdAsync(clientId, ct);
                    if (raced is null) throw;                       // a genuine failure, not a race
                    await appMgr.UpdateAsync(raced, descriptor, ct);
                }
            }

            return Results.Json(new
            {
                client_id                  = clientId,
                client_id_issued_at        = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                redirect_uris              = uris.Select(u => u.AbsoluteUri).ToArray(),
                token_endpoint_auth_method = "none",                // public/PKCE, no secret
                grant_types                = new[] { "authorization_code", "refresh_token" },
                response_types             = new[] { "code" },
                scope                      = string.Join(' ', McpScopes.All),
                client_name                = displayName,
            }, statusCode: StatusCodes.Status201Created);           // NOTE: no client_secret (public client)
        })
        .AllowAnonymous()
        .RequireRateLimiting("dcr")
        .WithName("OAuthRegister");

        return app;

        // https OR loopback-http only (Uri.IsLoopback ⇒ localhost / 127.0.0.0/8 / ::1). Rejects custom
        // schemes + non-loopback http. Covers every documented Claude callback (claude.ai https + Claude
        // Code loopback); requirement 4.
        static bool IsAllowedRedirectUri(Uri u) =>
            u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback);

        static string DeterministicClientId(IEnumerable<Uri> uris)
        {
            var joined = string.Join('\n', uris.Select(u => u.AbsoluteUri).OrderBy(s => s, StringComparer.Ordinal));
            var hash   = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
            return "dcr-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();   // disjoint from "teas-mcp" (§6)
        }

        static IResult DcrError(string error, string description) =>
            Results.Json(new { error, error_description = description },   // RFC 7591 §3.2.2
                statusCode: StatusCodes.Status400BadRequest);
    }
}

/// <summary>RFC 7591 DCR request. scope / grant_types / response_types / token_endpoint_auth_method
/// are ACCEPTED but IGNORED — the created client always gets the fixed server policy.</summary>
public sealed record DcrRequest
{
    [JsonPropertyName("redirect_uris")]              public List<string>? RedirectUris { get; init; }
    [JsonPropertyName("client_name")]                public string? ClientName { get; init; }
    [JsonPropertyName("scope")]                      public string? Scope { get; init; }
    [JsonPropertyName("grant_types")]                public List<string>? GrantTypes { get; init; }
    [JsonPropertyName("response_types")]             public List<string>? ResponseTypes { get; init; }
    [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; init; }
}

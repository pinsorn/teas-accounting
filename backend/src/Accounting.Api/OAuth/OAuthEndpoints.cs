using Accounting.Api.Mcp;
using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
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
            AccountingDbContext db, IActivityRecorder activity, IOptions<AppOptions> opt,
            CancellationToken ct) =>
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
            var branchId = await db.Branches.IgnoreQueryFilters()
                .Where(b => b.CompanyId == companyId && b.IsActive)
                .OrderByDescending(b => b.IsHeadOffice).ThenBy(b => b.BranchId)
                .Select(b => (int?)b.BranchId).FirstOrDefaultAsync(ct) ?? 0;
            if (branchId <= 0)
                return Results.BadRequest(new { error = "company_has_no_active_branch" });

            // Authoritative grant: requested ∩ McpScopes (structurally drops unknown + every *.post).
            var granted = McpScopes.Normalize(request.GetScopes());
            if (granted.Count == 0)
                return Results.BadRequest(new { error = "invalid_scope" });

            var resource = $"{opt.Value.BaseUrl.TrimEnd('/')}/mcp";
            var actor = $"oauth:{tenant.Username ?? userId.ToString()}";
            var principal = McpPrincipalFactory.Build(userId, actor, companyId, branchId, granted, resource);

            // §4.8 audit — user + client + company (token id assigned by OpenIddict downstream).
            activity.Record(
                entityType: "oauth_grant", entityId: companyId, docNo: request.ClientId,
                companyId: companyId, action: "oauth_authorize",
                note: $"user {userId} granted an MCP token for company {companyId} to client " +
                      $"'{request.ClientId}' — scopes [{string.Join(' ', granted)}]",
                module: "identity");
            await db.SaveChangesAsync(ct);

            return Results.SignIn(principal, properties: null,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        })
        .RequireAuthorization()   // must carry the BFF-forwarded session Bearer
        .WithName("OAuthAuthorizeAccept");

        return app;

        static IResult Forbid() => Results.Forbid(
            authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
    }
}

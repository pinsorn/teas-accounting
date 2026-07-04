using System.Globalization;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// M11 — the refresh-flow hardening handler referenced by Program.cs. On grant_type=refresh_token
/// ONLY, it reloads the subject user and REJECTS the grant if the user is inactive or no longer a
/// member of the token's baked company_id, then re-derives the scope set against the user's CURRENT
/// RBAC (shares H4's McpConsentScopes primitive). Access tokens live ≤10 min, but a refresh token
/// lives 8h absolute — without this a disabled/off-boarded user keeps MCP access for up to 8h.
/// Untouched: authorization-code sign-in (fresh consent is already RBAC-filtered in OAuthEndpoints)
/// and all non-token sign-ins.
/// </summary>
public sealed class RefreshTokenRevalidationHandler
    : IOpenIddictServerHandler<OpenIddictServerEvents.ProcessSignInContext>
{
    private readonly AccountingDbContext _db;
    private readonly IPermissionLookup _permissions;

    public RefreshTokenRevalidationHandler(AccountingDbContext db, IPermissionLookup permissions)
    {
        _db = db;
        _permissions = permissions;
    }

    public async ValueTask HandleAsync(OpenIddictServerEvents.ProcessSignInContext context)
    {
        // Scope strictly to the token endpoint's refresh grant.
        if (context.EndpointType != OpenIddictServerEndpointType.Token ||
            context.Request is null || !context.Request.IsRefreshTokenGrantType())
            return;

        var principal = context.Principal;
        if (principal is null) { Reject(context); return; }

        if (!long.TryParse(principal.GetClaim(Claims.Subject), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var userId) || userId <= 0 ||
            !int.TryParse(principal.GetClaim(TenantClaims.CompanyId), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var companyId) || companyId <= 0)
        { Reject(context); return; }

        // Reload the user (global table, no company RLS — same read login does anonymously).
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.IsActive, u.IsSuperAdmin })
            .FirstOrDefaultAsync(context.CancellationToken);
        if (user is null || !user.IsActive) { Reject(context); return; }

        // Split offline_access (an OpenIddict scope, never a tool scope) from the tool scopes.
        var current = principal.GetScopes();                       // ImmutableArray<string>
        var hadOffline = current.Contains(Scopes.OfflineAccess);
        var toolScopes = current.Where(s => s != Scopes.OfflineAccess).ToArray();

        IReadOnlyList<string> granted;
        if (user.IsSuperAdmin)
        {
            // Super still member of an ACTIVE company? (mirror the consent membership rule)
            var companyActive = await _db.Companies.IgnoreQueryFilters()
                .AnyAsync(c => c.CompanyId == companyId && c.IsActive, context.CancellationToken);
            if (!companyActive) { Reject(context); return; }
            granted = McpScopes.Normalize(toolScopes);             // no RBAC filter (see spec §0)
        }
        else
        {
            var (roles, perms) = await _permissions.LoadAsync(userId, companyId, context.CancellationToken);
            if (roles.Count == 0) { Reject(context); return; }     // off-boarded: no active role in company
            granted = McpConsentScopes.FilterToRbac(
                McpScopes.Normalize(toolScopes), perms.ToHashSet(StringComparer.Ordinal));
        }

        if (granted.Count == 0) { Reject(context); return; }

        // Re-bake BOTH scope representations + destinations (keep the CSV claim = the tool authority).
        var finalScopes = hadOffline ? granted.Append(Scopes.OfflineAccess) : granted;
        principal.SetScopes(finalScopes);
        principal.SetClaim(TenantClaims.Scopes, string.Join(',', granted));   // CSV PermissionHandler reads
        principal.SetDestinations(static _ => [Destinations.AccessToken]);

        static void Reject(OpenIddictServerEvents.ProcessSignInContext ctx) => ctx.Reject(
            error: Errors.InvalidGrant,
            description: "The user is inactive or no longer authorized for this company.",
            uri: null);
    }
}

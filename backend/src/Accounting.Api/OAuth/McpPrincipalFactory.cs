using System.Globalization;
using System.Security.Claims;
using Accounting.Application.Abstractions;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// Builds the <see cref="ClaimsPrincipal"/> for an issued MCP OAuth token so its access-token claims
/// EQUAL the set the X-Api-Key handler emits (<c>company_id</c>, <c>branch_id</c>,
/// <c>is_api_key=true</c>, <c>scopes</c> CSV, name) — letting HttpTenantContext → RLS →
/// <c>apiperm:*</c> gates → MCP tools all work unchanged.
///
/// Security invariants (spec §6b, gated by tests): company_id/branch_id MUST be &gt; 0;
/// <c>is_super_admin</c> and <c>api_key_id</c> are NEVER emitted; scopes are the server-normalized
/// grant (never *.post). aud = the MCP resource (RFC 8707).
/// </summary>
public static class McpPrincipalFactory
{
    public static ClaimsPrincipal Build(
        long userId, string actorName, int companyId, int branchId,
        IReadOnlyList<string> grantedScopes, string resource)
    {
        // Silent-zero company/branch already caused duplicate doc-number sequences — hard-reject.
        if (userId <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "sub must be a positive TEAS user id.");
        if (companyId <= 0)
            throw new ArgumentOutOfRangeException(nameof(companyId),
                "company_id must be positive — a silent-zero token would break tenant isolation (spec §6b).");
        if (branchId <= 0)
            throw new ArgumentOutOfRangeException(nameof(branchId), "branch_id must be positive (spec §6b).");

        var identity = new ClaimsIdentity(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: ClaimTypes.Name, roleType: ClaimTypes.Role);

        identity.SetClaim(Claims.Subject, userId.ToString(CultureInfo.InvariantCulture)); // sub = numeric user id
        identity.SetClaim(ClaimTypes.Name, actorName);
        identity.SetClaim(TenantClaims.CompanyId, companyId.ToString(CultureInfo.InvariantCulture));
        identity.SetClaim(TenantClaims.BranchId, branchId.ToString(CultureInfo.InvariantCulture));
        identity.SetClaim(TenantClaims.IsApiKey, "true");                    // load-bearing (PermissionHandler)
        identity.SetClaim(TenantClaims.Scopes, string.Join(',', grantedScopes));
        // Actor name = draft provenance (CreatedViaApiKeyName) + the E5 own-drafts filter key, so an
        // OAuth agent is first-class (can poll its own drafts) — NOT an api_key_id.
        identity.SetClaim(TenantClaims.ApiKeyName, actorName);
        // NEVER TenantClaims.IsSuperAdmin, NEVER TenantClaims.ApiKeyId.

        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(grantedScopes);
        principal.SetResources(resource);                                    // RFC 8707 → aud = the MCP resource
        principal.SetDestinations(static _ => [Destinations.AccessToken]);   // every claim on the access token
        return principal;
    }
}

using System.Globalization;
using System.Security.Claims;
using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Validation.AspNetCore;

namespace Accounting.Api.OAuth;

/// <summary>
/// Defense-in-depth (spec §6b) for the /mcp OAuth Bearer path. Our issuer (McpPrincipalFactory)
/// already guarantees these invariants at grant time, but this rejects at VALIDATION too so a
/// malformed/forged Bearer principal can never reach RLS with a zero/super identity:
/// an MCP OAuth principal MUST carry a positive company_id + branch_id and is_api_key=true, and
/// MUST NOT carry is_super_admin. On violation the principal is replaced with an unauthenticated
/// one → RequireAuthenticatedUser fails → 401 (with the WWW-Authenticate the /mcp guard adds).
///
/// Scoped strictly to the OpenIddict validation scheme — a no-op for JWT and X-Api-Key principals.
/// (aud = the MCP resource is enforced separately by OpenIddict's AddAudiences.)
/// </summary>
public sealed class McpBearerClaimsTransform : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = principal.Identities.FirstOrDefault(i =>
            i.AuthenticationType == OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        if (identity is null || !identity.IsAuthenticated)
            return Task.FromResult(principal);   // not an OAuth Bearer principal → untouched

        var companyOk = int.TryParse(identity.FindFirst(TenantClaims.CompanyId)?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var company) && company > 0;
        var branchOk = int.TryParse(identity.FindFirst(TenantClaims.BranchId)?.Value,
            NumberStyles.Integer, CultureInfo.InvariantCulture, out var branch) && branch > 0;
        var isApiKey = string.Equals(identity.FindFirst(TenantClaims.IsApiKey)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);
        var hasSuperAdmin = identity.FindFirst(TenantClaims.IsSuperAdmin) is not null;

        if (!companyOk || !branchOk || !isApiKey || hasSuperAdmin)
            return Task.FromResult(new ClaimsPrincipal(new ClaimsIdentity()));   // reject → unauthenticated

        return Task.FromResult(principal);
    }
}

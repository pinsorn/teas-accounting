using System.Security.Claims;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Tenancy;

/// <summary>
/// Reads tenant + user from the current request's JWT claim set.
/// Registered Scoped so DbContext (also Scoped) gets the same instance.
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    public int   CompanyId       { get; }
    public int   BranchId        { get; }
    public long? UserId          { get; }
    public bool  IsSuperAdmin    { get; }
    public bool  IsAuthenticated { get; }
    public long? ApiKeyId        { get; }

    public HttpTenantContext(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        IsAuthenticated = user?.Identity?.IsAuthenticated == true;
        if (!IsAuthenticated || user is null)
        {
            return;
        }

        CompanyId    = TryInt(user, TenantClaims.CompanyId);
        BranchId     = TryInt(user, TenantClaims.BranchId);
        UserId       = long.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                       ?? user.FindFirst("sub")?.Value, out var u) ? u : null;
        IsSuperAdmin = string.Equals(user.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
                                     StringComparison.OrdinalIgnoreCase);
        ApiKeyId     = long.TryParse(user.FindFirst(TenantClaims.ApiKeyId)?.Value, out var k) ? k : null;
    }

    private static int TryInt(ClaimsPrincipal user, string type) =>
        int.TryParse(user.FindFirst(type)?.Value, out var v) ? v : 0;
}

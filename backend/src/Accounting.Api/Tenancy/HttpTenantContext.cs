using System.Security.Claims;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Tenancy;

/// <summary>
/// Reads tenant + user from the current request's principal (JWT or Sprint-14
/// ApiKey scheme). Registered Scoped so the DbContext (also Scoped) shares it.
///
/// Properties are evaluated LAZILY on each access — NOT cached in the ctor.
/// Reason: the ApiKey auth handler resolves <c>IApiKeyResolver → AccountingDbContext
/// → ITenantContext</c> DURING authentication, i.e. before the principal is
/// established. A ctor-snapshot would freeze the anonymous pre-auth user for
/// the whole request. (Sprint 14 — runtime-gotcha class.)
/// </summary>
public sealed class HttpTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    public HttpTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;

    public int CompanyId => Authed ? TryInt(TenantClaims.CompanyId) : 0;
    public int BranchId  => Authed ? TryInt(TenantClaims.BranchId)  : 0;

    public long? UserId => Authed && long.TryParse(
        User!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User!.FindFirst("sub")?.Value,
        out var u) ? u : null;

    // Sprint 13k §4.8 — JWT name claim (set by JwtTokenIssuer). Audit actor.
    public string? Username => Authed
        ? User!.FindFirst(ClaimTypes.Name)?.Value ?? User!.FindFirst("name")?.Value
        : null;

    public bool IsSuperAdmin => Authed && string.Equals(
        User!.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
        StringComparison.OrdinalIgnoreCase);

    public long? ApiKeyId => Authed
        && long.TryParse(User!.FindFirst(TenantClaims.ApiKeyId)?.Value, out var k) ? k : null;

    public int? ApiKeyDefaultBusinessUnitId => Authed
        && int.TryParse(User!.FindFirst(TenantClaims.DefaultBusinessUnit)?.Value, out var b) ? b : null;

    private bool Authed => User?.Identity?.IsAuthenticated == true;

    private int TryInt(string type) =>
        int.TryParse(User!.FindFirst(type)?.Value, out var v) ? v : 0;
}

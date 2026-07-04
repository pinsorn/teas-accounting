using System.Security.Claims;
using Accounting.Application.Abstractions;

namespace Accounting.Api.Tenancy;

/// <summary>
/// Reads tenant + user from the current request's principal (JWT or Sprint-14 ApiKey scheme)
/// when a request is in flight. Registered Scoped so the DbContext (also Scoped) shares it.
///
/// Properties are evaluated LAZILY on each access — NOT cached in the ctor.
/// Reason: the ApiKey auth handler resolves <c>IApiKeyResolver → AccountingDbContext
/// → ITenantContext</c> DURING authentication, i.e. before the principal is
/// established. A ctor-snapshot would freeze the anonymous pre-auth user for
/// the whole request. (Sprint 14 — runtime-gotcha class.) This lazy-claim behavior is
/// UNCHANGED by the fallback below — it only applies when <c>HttpContext</c> is non-null,
/// which is true for the ENTIRE lifetime of every real request (the framework sets it before
/// any user code, including pre-auth resolvers, runs) — so a request can never observe the
/// fallback branch.
///
/// Move-jobs-to-api (2026-07-04) — renamed from <c>HttpTenantContext</c>. Scheduled jobs
/// (Quartz, now hosted in-process, see <c>Accounting.Api/Scheduling</c>) run in a DI scope
/// with NO HttpContext (a fresh <c>IServiceScopeFactory.CreateScope()</c>, not a request scope).
/// When <c>_accessor.HttpContext</c> is null, every property instead returns a settable
/// per-scope fallback (<see cref="SetCompany"/>) — mirrors the retired
/// <c>Accounting.Workers.Tenancy.WorkerTenantContext</c>'s role: a job resolves this type from
/// its OWN scope, calls <c>SetCompany(companyId)</c> BEFORE resolving the
/// DbContext/report service, and the EF global query filter (<c>AccountingDbContext</c>) plus a
/// LOCAL-tx <c>set_config('app.company_id', …)</c> pin (<c>VatRegisterSnapshotJob.RunSnapshotAsync</c>)
/// both then see that one company. Never super-admin in fallback mode — each scope is pinned to
/// exactly one company, so the EF filter must enforce <c>CompanyId</c> equality, not bypass it.
/// </summary>
public sealed class AmbientTenantContext : ITenantContext
{
    private readonly IHttpContextAccessor _accessor;
    public AmbientTenantContext(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? User => _accessor.HttpContext?.User;
    private bool NoHttpContext => _accessor.HttpContext is null;

    // Fallback state — read ONLY when there is no HttpContext (a background job scope).
    // A real request always has one, so these fields never affect request behaviour.
    private int _fallbackCompanyId;
    private int _fallbackBranchId;

    /// <summary>Pins this scope to one company for a background job. Meaningless (never read)
    /// when a request's HttpContext is present — the request path always derives the tenant
    /// from claims instead.</summary>
    public void SetCompany(int companyId, int branchId = 0)
    {
        _fallbackCompanyId = companyId;
        _fallbackBranchId = branchId;
    }

    public bool IsAuthenticated => NoHttpContext || User?.Identity?.IsAuthenticated == true;

    public int CompanyId => NoHttpContext ? _fallbackCompanyId : (Authed ? TryInt(TenantClaims.CompanyId) : 0);
    public int BranchId  => NoHttpContext ? _fallbackBranchId  : (Authed ? TryInt(TenantClaims.BranchId)  : 0);

    public long? UserId => NoHttpContext ? null : Authed && long.TryParse(
        User!.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User!.FindFirst("sub")?.Value,
        out var u) ? u : null;

    // Sprint 13k §4.8 — JWT name claim (set by JwtTokenIssuer). Audit actor.
    // Fallback mode: "system" (the background-job actor), mirroring WorkerTenantContext.
    public string? Username => NoHttpContext
        ? "system"
        : Authed ? User!.FindFirst(ClaimTypes.Name)?.Value ?? User!.FindFirst("name")?.Value : null;

    public bool IsSuperAdmin => !NoHttpContext && Authed && string.Equals(
        User!.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
        StringComparison.OrdinalIgnoreCase);

    public long? ApiKeyId => NoHttpContext ? null : Authed
        && long.TryParse(User!.FindFirst(TenantClaims.ApiKeyId)?.Value, out var k) ? k : null;

    // M4a — the actor name stamped on agent-created drafts (CreatedViaApiKeyName) + the E5
    // own-drafts filter. Set by the X-Api-Key handler (the key name) AND the OAuth token
    // (actor "oauth:&lt;user&gt;") — NOT gated on ApiKeyId, since an OAuth principal has an actor
    // name but no api_key_id. Human JWT callers carry no such claim → null (unchanged).
    public string? ApiKeyName => NoHttpContext ? null : Authed ? User!.FindFirst(TenantClaims.ApiKeyName)?.Value : null;

    public int? ApiKeyDefaultBusinessUnitId => NoHttpContext ? null : Authed
        && int.TryParse(User!.FindFirst(TenantClaims.DefaultBusinessUnit)?.Value, out var b) ? b : null;

    private bool Authed => User?.Identity?.IsAuthenticated == true;

    private int TryInt(string type) =>
        int.TryParse(User!.FindFirst(type)?.Value, out var v) ? v : 0;
}

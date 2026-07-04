using Accounting.Application.Abstractions;

namespace Accounting.Workers.Tenancy;

/// <summary>
/// H2 (review 2026-07-04) — mutable <see cref="ITenantContext"/> for background workers.
/// There is no HTTP request to read claims from (contrast <c>HttpTenantContext</c>), so a
/// Quartz job that must process every company sets <see cref="CompanyId"/> per iteration,
/// once per fresh DI scope, BEFORE resolving that scope's DbContext/services — the same
/// instance is then read by the EF global query filter (<c>AccountingDbContext.cs</c>) for
/// every query issued in that scope. Registered Scoped in Program.cs, with
/// <c>ITenantContext</c> mapped to resolve the SAME instance, so the DbContext (which
/// requests the interface) and the job loop (which mutates the concrete type) share one
/// object per scope.
///
/// Never super-admin: each scope is pinned to exactly one company, so the EF filter must
/// enforce <c>CompanyId</c> equality (not bypass it) for the isolation to hold.
/// </summary>
public sealed class WorkerTenantContext : ITenantContext
{
    public int CompanyId { get; set; }
    public int BranchId { get; set; }
    public long? UserId => null;
    public string? Username => "system";
    public bool IsSuperAdmin => false;
    public bool IsAuthenticated => true;
    public long? ApiKeyId => null;
    public string? ApiKeyName => null;
    public int? ApiKeyDefaultBusinessUnitId => null;
}

using Accounting.Application.Reports;
using Accounting.Infrastructure.Persistence;
using Accounting.Workers.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Accounting.Workers.Jobs;

/// <summary>
/// H2 (review 2026-07-04) — was tenant-blind: a single captured <see cref="IVatReportService"/>
/// with no <c>ITenantContext</c> registered anywhere in this host meant the EF global query
/// filter no-op'd (bypass-role dev/test → blended every company's VAT; prod NOBYPASSRLS → 0
/// rows). Now loops every active company, one fresh child DI scope each — a fresh
/// <see cref="WorkerTenantContext"/> + <see cref="AccountingDbContext"/> per company, so there
/// is no cross-company bleed on a shared instance. <c>master.companies</c> is not
/// <c>ITenantOwned</c> (no RLS) so the company list itself is a plain, legitimately
/// cross-tenant read.
/// </summary>
[DisallowConcurrentExecution]
public sealed class VatRegisterSnapshotJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VatRegisterSnapshotJob> _log;

    public VatRegisterSnapshotJob(IServiceScopeFactory scopeFactory, ILogger<VatRegisterSnapshotJob> log)
    {
        _scopeFactory = scopeFactory; _log = log;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var ct = context.CancellationToken;
        var bangkok = TimeZoneInfo.FindSystemTimeZoneById(
            OperatingSystem.IsWindows() ? "SE Asia Standard Time" : "Asia/Bangkok");
        var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, bangkok);

        List<int> companyIds;
        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            companyIds = await db.Companies
                .Where(c => c.IsActive)
                .Select(c => c.CompanyId)
                .ToListAsync(ct);
        }

        foreach (var companyId in companyIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var tenant = scope.ServiceProvider.GetRequiredService<WorkerTenantContext>();
            tenant.CompanyId = companyId;

            var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var report = scope.ServiceProvider.GetRequiredService<IVatReportService>();

            var summary = await RunSnapshotAsync(db, report, companyId, now.Year, now.Month, ct);

            _log.LogInformation(
                "VAT snapshot company {CompanyId} {Year}-{Month:D2}: Sales={Sales:N2}, OutputVAT={OutputVat:N2}, Purchase={Purchase:N2}, InputVAT={InputVat:N2}, Net={Net:N2}",
                companyId, summary.Year, summary.Month, summary.Sales, summary.OutputVat,
                summary.Purchase, summary.InputVat,
                summary.NetVatPayable - summary.NetVatRefundable);
        }
    }

    /// <summary>
    /// Runs one company's snapshot inside an explicit transaction that pins
    /// <c>app.company_id</c> LOCAL (mirrors <c>PermissionLookup.cs</c>) so RLS sees the same
    /// value as the EF filter for every query the snapshot issues, on the one connection that
    /// transaction owns. <c>is_local=true</c> auto-reverts at commit — no pooled-connection
    /// poison risk. Public (not private) so the RLS proving test can drive this exact
    /// transaction/pin logic directly against a role-switched connection, without fighting
    /// Quartz job-execution scoping or needing InternalsVisibleTo plumbing.
    /// </summary>
    public static async Task<Pnd30Summary> RunSnapshotAsync(
        AccountingDbContext db, IVatReportService report, int companyId,
        int year, int month, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync(
            "SELECT set_config('app.company_id', {0}, true)",
            [companyId.ToString(System.Globalization.CultureInfo.InvariantCulture)], ct);

        var summary = await report.GetPnd30Async(year, month, ct);

        await tx.CommitAsync(ct);
        return summary;
    }
}

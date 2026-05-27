using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Purchase;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// Sprint 13j-PURCH Phase B — AP Aging report (อายุหนี้เจ้าหนี้).
/// Read-only query; seeds VendorInvoice rows directly (Status=Posted, explicit
/// SettledAmount/SettlementStatus) — no service plumbing/NumberSequence needed.
/// Real Postgres (teas_test); unique seeds via TestIds.* so the suite is
/// re-runnable on the shared DB (CLAUDE.md §8). Each test scopes its assertions
/// to a unique vendor (vendorId filter) so concurrently-seeded rows never leak in.
///
/// Buckets by (asOf.DayNumber − DocDate.DayNumber): ≤30 Current, 31–60, 61–90, >90.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ApAgingTests
{
    private readonly PostgresFixture _fx;
    public ApAgingTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    // Seed a single posted VendorInvoice for the given vendor, aged so that
    // DocDate = asOf − ageDays. SettledAmount/SettlementStatus set directly.
    private static async Task SeedVi(
        ServiceProvider sp, long vendorId, DateOnly asOf, int ageDays,
        decimal total, decimal settled, int companyId = 1)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var status = settled >= total - 0.01m ? "PAID" : settled > 0m ? "PARTIAL" : "UNPAID";
        db.VendorInvoices.Add(new VendorInvoice
        {
            CompanyId = companyId, BranchId = 1,
            DocNo = $"VI-{TestIds.Suffix()}",
            DocDate = asOf.AddDays(-ageDays),
            VendorTaxInvoiceNo = $"VTI-{TestIds.Suffix()[..6]}",
            VendorTaxInvoiceDate = asOf.AddDays(-ageDays),
            VatClaimPeriod = 202605,
            VendorId = vendorId,
            VendorTaxId = TestIds.TaxId(),
            VendorName = "ผู้ขายทดสอบ AP",
            VendorType = CustomerType.Corporate,
            SubtotalAmount = total, VatAmount = 0m, TotalAmount = total, TotalAmountThb = total,
            SettledAmount = settled, SettlementStatus = status,
            Status = DocumentStatus.Posted,
            PostedAt = DateTimeOffset.UtcNow, PostedBy = 1,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<ApAgingRow?> RowFor(ServiceProvider sp, long vendorId, DateOnly asOf)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IApAgingService>();
        var rep = await svc.GetAsync(asOf, vendorId, default);
        return rep.Rows.SingleOrDefault();
    }

    // ── bucket boundaries: 30→Current, 31→31-60, 60→31-60, 61→61-90, 90→61-90, 91→Over90 ──
    [SkippableTheory]
    [InlineData(30, "current")]
    [InlineData(31, "b3160")]
    [InlineData(60, "b3160")]
    [InlineData(61, "b6190")]
    [InlineData(90, "b6190")]
    [InlineData(91, "over90")]
    public async Task Bucket_boundaries(int ageDays, string expected)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = (long)(uint)Guid.NewGuid().GetHashCode();   // unique vendorId to isolate
        var asOf = new DateOnly(2026, 5, 27);
        await SeedVi(sp, vid, asOf, ageDays, total: 1000m, settled: 0m);

        var row = await RowFor(sp, vid, asOf);
        row.Should().NotBeNull();
        row!.Total.Should().Be(1000m);
        switch (expected)
        {
            case "current": row.Current.Should().Be(1000m); break;
            case "b3160":   row.Bucket31To60.Should().Be(1000m); break;
            case "b6190":   row.Bucket61To90.Should().Be(1000m); break;
            case "over90":  row.BucketOver90.Should().Be(1000m); break;
        }
    }

    [SkippableFact]
    public async Task Partial_payment_outstanding_in_correct_bucket()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = (long)(uint)Guid.NewGuid().GetHashCode();
        var asOf = new DateOnly(2026, 5, 27);
        // total 1000, paid 300 → outstanding 700, aged 45 days → 31-60 bucket
        await SeedVi(sp, vid, asOf, ageDays: 45, total: 1000m, settled: 300m);

        var row = await RowFor(sp, vid, asOf);
        row.Should().NotBeNull();
        row!.Bucket31To60.Should().Be(700m);
        row.Total.Should().Be(700m);
        row.Current.Should().Be(0m);
    }

    [SkippableFact]
    public async Task FullyPaid_excluded()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = (long)(uint)Guid.NewGuid().GetHashCode();
        var asOf = new DateOnly(2026, 5, 27);
        await SeedVi(sp, vid, asOf, ageDays: 45, total: 1000m, settled: 1000m);   // PAID

        var row = await RowFor(sp, vid, asOf);
        row.Should().BeNull("fully-paid VIs are excluded from AP aging");
    }

    [SkippableFact]
    public async Task Empty_when_no_posted_vis()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var vid = (long)(uint)Guid.NewGuid().GetHashCode();   // vendor with no VIs
        var asOf = new DateOnly(2026, 5, 27);

        await using var s = sp.CreateAsyncScope();
        var rep = await s.ServiceProvider.GetRequiredService<IApAgingService>()
            .GetAsync(asOf, vid, default);
        rep.Rows.Should().BeEmpty();
        rep.Totals.Current.Should().Be(0m);
        rep.Totals.Bucket31To60.Should().Be(0m);
        rep.Totals.Bucket61To90.Should().Be(0m);
        rep.Totals.BucketOver90.Should().Be(0m);
        rep.Totals.Total.Should().Be(0m);
    }

    // ── §4.7 multi-tenant: Company A's VI must NOT appear in Company B's report ──
    [SkippableFact]
    public async Task MultiTenant_company_a_vi_absent_from_company_b_report()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var asOf = new DateOnly(2026, 5, 27);
        var vid = (long)(uint)Guid.NewGuid().GetHashCode();

        // Seed under Company 1.
        await using var spA = Provider(companyId: 1);
        await SeedVi(spA, vid, asOf, ageDays: 10, total: 500m, settled: 0m, companyId: 1);

        // Company 1 sees it.
        var rowA = await RowFor(spA, vid, asOf);
        rowA.Should().NotBeNull();
        rowA!.Total.Should().Be(500m);

        // Company 2 must NOT see Company 1's VI (filter by same vendorId to be precise).
        await using var spB = Provider(companyId: 2);
        await using var s = spB.CreateAsyncScope();
        var repB = await s.ServiceProvider.GetRequiredService<IApAgingService>()
            .GetAsync(asOf, vid, default);
        repB.Rows.Should().BeEmpty("Company 2 must not see Company 1's vendor invoice (§4.7)");
    }
}

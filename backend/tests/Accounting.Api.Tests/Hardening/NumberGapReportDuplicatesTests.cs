using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// H1 (specs/fix-duplicate-tax-doc-numbers.md) WP-3 — the compliance control that reported clean
/// over the breach (VERDICT line 218: hasGaps:false over the very period holding H1's duplicates).
/// tax.v_number_gaps finds MISSING numbers only; tax.v_duplicate_doc_numbers (635) finds the SAME
/// number appearing more than once. T9/T10.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberGapReportDuplicatesTests
{
    private readonly PostgresFixture _fx;
    public NumberGapReportDuplicatesTests(PostgresFixture fx) => _fx = fx;

    private static async Task SeedDuplicateTiPairAsync(
        AccountingDbContext db, int companyId, long customerId, DateOnly today, string docNo)
    {
        TaxInvoice Build(int branchId) => new()
        {
            CompanyId = companyId, BranchId = branchId,
            DocNo = docNo, DocDate = today, TaxPointDate = today,
            SupplierTaxId = "0000000000000", SupplierBranchCode = "00000", SupplierBranchName = "สำนักงานใหญ่",
            SupplierName = "Test Supplier", SupplierAddress = "Test Address",
            CustomerId = customerId, CustomerName = "Test Customer", CustomerAddress = "Test Address",
            SubtotalAmount = 100m, TaxableAmount = 100m, TaxAmount = 7m, TotalAmount = 107m,
            Status = DocumentStatus.Posted,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        // Two posted rows, SAME doc_no, DIFFERENT branch — exactly the co2 shape (07-2026-RC-LAB-0001,
        // two POSTED receipts). sales.tax_invoices carries the branch-scoped (company,branch,doc_no)
        // unique index (§1.2 hole), so this insert is legally accepted, not a constraint violation.
        db.TaxInvoices.AddRange(Build(0), Build(1));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Duplicate_report_is_tenant_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        DateOnly today;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = s.ServiceProvider.GetRequiredService<Accounting.Application.Abstractions.IClock>().TodayInBangkok();
            await SeedDuplicateTiPairAsync(db, a.CompanyId, a.CustomerId, today, $"{today:MM}-{today:yyyy}-TI-9001");
        }

        // Company A sees its own duplicate.
        await using (var spA = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId))
        await using (var s = spA.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<INumberGapReportService>();
            var report = await svc.GetGapsAsync(null, null, null, default);
            report.Duplicates.Should().ContainSingle(d => d.DocNo == $"{today:MM}-{today:yyyy}-TI-9001");
        }

        // Company B — the view has NO RLS (F14); the service filter is the only wall. Must see nothing.
        await using (var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, b.CompanyId, b.BranchId))
        await using (var s = spB.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<INumberGapReportService>();
            var report = await svc.GetGapsAsync(null, null, null, default);
            report.Duplicates.Should().BeEmpty("company B must never see company A's duplicates (cross-tenant leak)");
        }
    }

    [SkippableFact]
    public async Task Duplicate_report_surfaces_what_number_gaps_missed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        DateOnly today;
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId);
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            today = s.ServiceProvider.GetRequiredService<Accounting.Application.Abstractions.IClock>().TodayInBangkok();
            // The historic two-branch-series shape: ONE number minted twice, no gap anywhere in the
            // union (only sequence 1 exists at all) — exactly what let /reports/number-gaps report
            // hasGaps:false over the period holding H1's real duplicate (VERDICT line 218).
            await SeedDuplicateTiPairAsync(db, t.CompanyId, t.CustomerId, today, $"{today:MM}-{today:yyyy}-TI-0001");
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<INumberGapReportService>();
            var report = await svc.GetGapsAsync(null, null, null, default);
            report.Gaps.Should().BeEmpty("the union has only sequence 1 — v_number_gaps correctly finds nothing missing");
            report.Duplicates.Should().NotBeEmpty(
                "the SAME response must surface the duplicate v_number_gaps cannot see — closing the verdict's exact complaint");
        }
    }
}

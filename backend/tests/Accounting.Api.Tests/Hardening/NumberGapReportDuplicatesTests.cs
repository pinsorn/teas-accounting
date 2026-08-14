using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Reports;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.Infrastructure.Reports;
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
///
/// R3/H1 — WP-4 gave every doc-carrying table a UNIQUE (company_id, doc_no) index, so a real
/// duplicate can no longer be INSERTed. Post-WP-4 this report is defence-in-depth: it still needs
/// to surface a duplicate that predates the index, or one that appears if the index is ever
/// dropped/disabled or data is loaded out of band. Both tests below model exactly that: drop the
/// index, seed the duplicate, and read the report back — ALL inside one transaction that is rolled
/// back at the end (Postgres DDL is transactional, so nothing persists even if the test crashes;
/// closing the connection rolls the whole thing back). The report is constructed directly against
/// the transaction's own <see cref="AccountingDbContext"/> rather than resolved from a fresh DI
/// scope, because a fresh scope gets its own connection and would not see the still-uncommitted
/// duplicate rows.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class NumberGapReportDuplicatesTests
{
    private readonly PostgresFixture _fx;
    public NumberGapReportDuplicatesTests(PostgresFixture fx) => _fx = fx;

    /// <summary>Must run inside a transaction that has already dropped
    /// sales.ix_tax_invoices_company_id_doc_no — WP-4's unique index otherwise refuses this
    /// insert with 23505 (it is now the ONLY way this shape can occur, see class doc).</summary>
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
        // two POSTED receipts).
        db.TaxInvoices.AddRange(Build(0), Build(1));
        await db.SaveChangesAsync();
    }

    [SkippableFact]
    public async Task Duplicate_report_is_tenant_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        NumberGapReport reportA, reportB;
        string docNo;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, a.CompanyId, a.BranchId))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var today = s.ServiceProvider.GetRequiredService<Accounting.Application.Abstractions.IClock>().TodayInBangkok();
            docNo = $"{today:MM}-{today:yyyy}-TI-9001";

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX sales.ix_tax_invoices_company_id_doc_no");
            await SeedDuplicateTiPairAsync(db, a.CompanyId, a.CustomerId, today, docNo);

            // Company A sees its own duplicate.
            reportA = await new NumberGapReportService(db,
                new StubTenant { CompanyId = a.CompanyId, BranchId = a.BranchId, UserId = 1 })
                .GetGapsAsync(null, null, null, default);

            // Company B — the view has NO RLS (F14); the service filter is the only wall. This
            // MUST run on the same connection/transaction as the seed (a separate connection
            // would neither see the uncommitted row nor be able to — DROP INDEX holds an
            // exclusive lock on the table for the life of the transaction). Must see nothing.
            reportB = await new NumberGapReportService(db,
                new StubTenant { CompanyId = b.CompanyId, BranchId = b.BranchId, UserId = 1 })
                .GetGapsAsync(null, null, null, default);

            await tx.RollbackAsync();
        }

        reportA.Duplicates.Should().ContainSingle(d => d.DocNo == docNo);
        reportB.Duplicates.Should().BeEmpty("company B must never see company A's duplicates (cross-tenant leak)");
    }

    [SkippableFact]
    public async Task Duplicate_report_surfaces_what_number_gaps_missed()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var t = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        NumberGapReport report;
        await using (var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, t.CompanyId, t.BranchId))
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var tenant = s.ServiceProvider.GetRequiredService<Accounting.Application.Abstractions.ITenantContext>();
            var today = s.ServiceProvider.GetRequiredService<Accounting.Application.Abstractions.IClock>().TodayInBangkok();

            await using var tx = await db.Database.BeginTransactionAsync();
            await db.Database.ExecuteSqlRawAsync("DROP INDEX sales.ix_tax_invoices_company_id_doc_no");
            // The historic two-branch-series shape: ONE number minted twice, no gap anywhere in the
            // union (only sequence 1 exists at all) — exactly what let /reports/number-gaps report
            // hasGaps:false over the period holding H1's real duplicate (VERDICT line 218).
            await SeedDuplicateTiPairAsync(db, t.CompanyId, t.CustomerId, today, $"{today:MM}-{today:yyyy}-TI-0001");

            report = await new NumberGapReportService(db, tenant).GetGapsAsync(null, null, null, default);

            await tx.RollbackAsync();
        }

        report.Gaps.Should().BeEmpty("the union has only sequence 1 — v_number_gaps correctly finds nothing missing");
        report.Duplicates.Should().NotBeEmpty(
            "the SAME response must surface the duplicate v_number_gaps cannot see — closing the verdict's exact complaint");
    }
}

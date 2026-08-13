using Accounting.Api.Tests.Fixtures;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// R2/WP-3 (H16, T22-T25, I9/I10) — a company with no VAT registration must never produce, let
/// alone finalize, a ภ.พ.30. TaxFilingService.GeneratePnd30Async is the single chokepoint for all
/// four surfaces (JSON preview/finalize, filled PDF, RD batch file) — one guard added right after
/// the auth check blocks all four. T24 pins the sibling invariant (I10): ภ.พ.36 stays available to
/// non-VAT companies (ม.83/6 reverse charge binds them too), so the new guard must never leak onto
/// WhtFilingService.GeneratePnd36Async. T25 pins that a genuinely VAT-registered company is
/// unaffected on every surface.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pnd30VatRegistrantOnlyTests
{
    private readonly PostgresFixture _fx;
    public Pnd30VatRegistrantOnlyTests(PostgresFixture fx) => _fx = fx;

    // Distinct far-future period per test — the shared teas_test fixture persists inserted rows
    // across runs (mirrors Pp30BatchExportServiceTests' RandPeriod).
    private static int RandPeriod()
    {
        var r = Random.Shared;
        return (3000 + r.Next(0, 6000)) * 100 + r.Next(1, 13);
    }
    private static DateOnly PeriodDate(int period) => new(period / 100, period % 100, 10);

    /// <summary>Insert ONE posted Tax Invoice with a single taxable line (mirrors
    /// Pp30BatchExportServiceTests' helper) — direct EF insert so DocDate/period are free to pick;
    /// going through ITaxInvoiceService.CreateDraftAsync server-pins DocDate to today.</summary>
    private static async Task AddPostedTaxableSale(
        ServiceProvider sp, int companyId, DateOnly date, decimal subtotal, decimal vat)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = new TaxInvoice
        {
            CompanyId = companyId, BranchId = 1,
            DocNo = "TI-" + TestIds.Suffix(), DocDate = date, TaxPointDate = date,
            SupplierTaxId = "0105500001234", SupplierBranchCode = "00000",
            SupplierBranchName = "สำนักงานใหญ่", SupplierName = "TEAS Co",
            SupplierAddress = "BKK",
            CustomerName = "ลูกค้าทดสอบ", CustomerAddress = "BKK",
            SubtotalAmount = subtotal, TaxAmount = vat,
            TotalAmount = subtotal + vat, TotalAmountThb = subtotal + vat,
            Status = DocumentStatus.Posted, PostedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.TaxInvoices.Add(ti);
        await db.SaveChangesAsync(default);

        db.TaxInvoiceLines.Add(new TaxInvoiceLine
        {
            TaxInvoiceId = ti.TaxInvoiceId,
            DescriptionTh = "ค่าบริการ", UomText = "งาน",
            TaxCode = "VAT7", TaxRate = 0.07m,
            Quantity = 1, UnitPrice = subtotal,
            LineAmount = subtotal, TaxAmount = vat,
        });
        await db.SaveChangesAsync(default);
    }

    private static async Task SetHouseNo(ServiceProvider sp, int companyId, string houseNo)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var prof = await db.CompanyProfiles.FirstAsync(p => p.CompanyId == companyId);
        prof.RegHouseNo = houseNo;
        await db.SaveChangesAsync(default);
    }

    // ── T22 (RED first) — non-VAT: preview AND finalize both throw pp30.non_vat_blocked ──
    [SkippableFact]
    public async Task NonVat_company_pnd30_preview_and_finalize_are_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        var period = RandPeriod();

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var actPreview = () => svc.GeneratePnd30Async(period, TaxFilingMode.Preview, default);
            (await actPreview.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pp30.non_vat_blocked");
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var actFinalize = () => svc.GeneratePnd30Async(period, TaxFilingMode.Finalize, default);
            (await actFinalize.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pp30.non_vat_blocked");
        }
    }

    // ── T23 — non-VAT: the PDF and RD batch-file surfaces are blocked too (same chokepoint) ──
    [SkippableFact]
    public async Task NonVat_company_pnd30_pdf_and_batch_file_are_blocked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        var period = RandPeriod();

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var actPdf = () => svc.BuildPnd30PdfAsync(period, default);
            (await actPdf.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pp30.non_vat_blocked");
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPp30BatchExportService>();
            var actBatch = () => svc.BuildAsync(period, default);
            (await actBatch.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pp30.non_vat_blocked");
        }
    }

    // ── T24 — I10: ภ.พ.36 is NOT gated for a non-VAT company (ม.83/6 binds them too) ──
    [SkippableFact]
    public async Task NonVat_company_pnd36_still_succeeds()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        var period = RandPeriod();

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IWhtFilingService>();
        var f = await svc.GeneratePnd36Async(period, TaxFilingMode.Preview, default);

        // No throw — WP-3's non-VAT guard must never leak onto GeneratePnd36Async.
        f.Status.Should().Be("Preview");
    }

    // ── T25 — VAT-registered company: every ภ.พ.30 surface is unchanged by the new guard ──
    [SkippableFact]
    public async Task Vat_company_all_pnd30_surfaces_unaffected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        var period = RandPeriod();
        await AddPostedTaxableSale(sp, co.CompanyId, PeriodDate(period), subtotal: 50000m, vat: 3500m);

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var preview = await svc.GeneratePnd30Async(period, TaxFilingMode.Preview, default);
            preview.Status.Should().Be("Preview");
            preview.Lines.SalesTaxable.Amount.Should().Be(50000m);
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var finalized = await svc.GeneratePnd30Async(period, TaxFilingMode.Finalize, default);
            finalized.Status.Should().BeOneOf("Finalized", "Submitted");
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
            var pdf = await svc.BuildPnd30PdfAsync(period, default);
            pdf.Should().NotBeNullOrEmpty();
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        }

        await SetHouseNo(sp, co.CompanyId, "199/4");
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IPp30BatchExportService>();
            var file = await svc.BuildAsync(period, default);
            file.RecordCount.Should().Be(1);
        }
    }
}

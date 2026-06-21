using System.Text;
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
/// DB-backed tests for <see cref="IPp30BatchExportService"/>: it reuses GeneratePnd30Async's figures
/// (so ข้อ1/ข้อ5 reflect the posted sales), pulls the address (NUMBER/POSTAL) from the company profile,
/// and guards loudly on no-sales / missing-address. Pure box layout is covered by
/// <see cref="Pp30BatchFormatTests"/>. Each test runs against its OWN fresh company (TestCompanyFactory)
/// so the shared teas_test DB stays collision-free.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pp30BatchExportServiceTests
{
    private readonly PostgresFixture _fx;
    public Pp30BatchExportServiceTests(PostgresFixture fx) => _fx = fx;

    // Distinct far-future period per test — the shared fixture persists inserted rows.
    private static int RandPeriod()
    {
        var r = Random.Shared;
        return (3000 + r.Next(0, 6000)) * 100 + r.Next(1, 13);
    }
    private static DateOnly PeriodDate(int period) => new(period / 100, period % 100, 10);

    /// <summary>Insert ONE posted Tax Invoice with a single taxable line so SalesCategorizer
    /// (which reads TaxInvoiceLines) reports taxable sales + output VAT for the period.</summary>
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
            TaxCode = "VAT7", TaxRate = 0.07m,          // > 0 → SalesCategorizer treats it as taxable
            Quantity = 1, UnitPrice = subtotal,
            LineAmount = subtotal, TaxAmount = vat,
        });
        await db.SaveChangesAsync(default);
    }

    private static async Task SetHouseNo(ServiceProvider sp, int companyId, string? houseNo)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var prof = await db.CompanyProfiles.FirstAsync(p => p.CompanyId == companyId);
        prof.RegHouseNo = houseNo;
        await db.SaveChangesAsync(default);
    }

    [SkippableFact]
    public async Task Builds_one_branch_row_from_pnd30_figures_and_company_address()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);

        var period = RandPeriod();
        await SetHouseNo(sp, co.CompanyId, "199/4");                  // ม.86/4 registered house no.
        await AddPostedTaxableSale(sp, co.CompanyId, PeriodDate(period), subtotal: 100000m, vat: 7000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPp30BatchExportService>();
        var file = await svc.BuildAsync(period, default);

        file.FileName.Should().StartWith("PP30_").And.EndWith(".txt");
        file.RecordCount.Should().Be(1);                              // one HQ branch row

        var text = Encoding.UTF8.GetString(file.Content);
        text.Take3().Should().NotEqual(new byte[] { 0xEF, 0xBB, 0xBF }, "no BOM");
        var rows = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        rows.Should().HaveCount(1);                                   // DETAIL only (no header record)

        var d = rows[0].Split('|');
        d.Should().HaveCount(16);
        d[1].Should().Be("0");                                        // BRANCH_NO — HQ 00000 → 0
        d[2].Should().Be("199/4");                                    // NUMBER — '/' preserved
        d[3].Should().Be("10110");                                    // POSTAL_CODE (founding address)
        d[4].Should().Be("100000.00");                                // ข้อ1 ยอดขาย (reuses GeneratePnd30Async)
        d[9].Should().Be("100000.00");                                // ข้อ4 = ข้อ1 − ข้อ2 − ข้อ3
        d[10].Should().Be("7000.00");                                 // ข้อ5 ภาษีขาย
        d[11].Should().Be("0.00");                                    // ข้อ6 ยอดซื้อ — present zero (no purchases)
        d[14].Should().Be("0.00");                                    // ข้อ7 ภาษีซื้อ — present zero
        d[15].Should().Be("7000.00");                                 // ข้อ8/9 = ข้อ5 − ข้อ7 (no purchases) → payable
    }

    [SkippableFact]
    public async Task Empty_period_throws_no_data()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        await SetHouseNo(sp, co.CompanyId, "199/4");

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPp30BatchExportService>();
        var act = () => svc.BuildAsync(RandPeriod(), default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pp30_batch.no_data");
    }

    [SkippableFact]
    public async Task Missing_registered_house_no_fails_loudly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);

        var period = RandPeriod();
        await SetHouseNo(sp, co.CompanyId, null);                     // NUMBER (เลขที่) is Mandatory on ภ.พ.30
        await AddPostedTaxableSale(sp, co.CompanyId, PeriodDate(period), subtotal: 100000m, vat: 7000m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPp30BatchExportService>();
        var act = () => svc.BuildAsync(period, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pp30_batch.missing_address");
    }
}

file static class Pp30TestExtensions
{
    // Tiny helper so the BOM assertion reads cleanly above.
    public static IEnumerable<byte> Take3(this string s) => Encoding.UTF8.GetBytes(s).Take(3);
}

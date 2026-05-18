using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Application.TaxFilings;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Sprint 9 Part B — ม.81 exempt seed + R-Q3 derived category + ม.82/6
/// proportional ratio + ภ.พ.30 preview/finalize immutability.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Sprint9VatComplianceTests
{
    private readonly PostgresFixture _fx;
    public Sprint9VatComplianceTests(PostgresFixture fx) => _fx = fx;

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

    private static async Task<long> CustomerId(ServiceProvider sp)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();
    }

    // taxCode/taxRate drive the ภ.พ.30 categorisation join (by Code → tax_codes).
    private static async Task PostTi(
        ServiceProvider sp, long cust, decimal price, string taxCode, decimal rate)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), cust, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "svc", 1m, 1, "ชิ้น", price, 0m, 1, taxCode, rate)],
            null), default);
        await svc.PostAsync(id, default);
    }

    [SkippableFact]
    public async Task Exempt_seed_240_has_derived_category_and_legal_ref()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();

        var live = await db.TaxCodes.FirstAsync(c => c.Code == "EXEMPT-LIVE");
        live.Category.Should().Be("EXEMPT");
        live.LegalRef.Should().Be("ม.81(1)(ข)");

        var exp = await db.TaxCodes.FirstAsync(c => c.Code == "VAT-OUT-0-EXP");
        exp.Category.Should().Be("ZERO_RATED");

        var vat7 = await db.TaxCodes.FirstAsync(c => c.Code == "VAT7");
        vat7.Category.Should().Be("TAXABLE");
    }

    [SkippableFact]
    public async Task Proportional_claim_ratio_is_taxable_over_total()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 6000m, "VAT7", 0.07m);        // taxable
        await PostTi(sp, cust, 4000m, "EXEMPT-LIVE", 0m);    // exempt

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProportionalInputVatService>();
        var r = await svc.ComputeAsync(202605, default);

        r.TaxableSales.Should().BeGreaterThanOrEqualTo(6000m);
        r.ExemptSales.Should().BeGreaterThanOrEqualTo(4000m);
        // taxable / (taxable + exempt) — with 6000 vs 4000 the ratio trends to 0.6.
        r.ClaimRatio.Should().BeInRange(0m, 1m);
        r.ClaimRatio.Should().Be(decimal.Round(r.TaxableSales / r.TotalSales, 6));
    }

    [SkippableFact]
    public async Task Pnd30_preview_categorizes_sales_and_persists_nothing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 5000m, "VAT7", 0.07m);
        await PostTi(sp, cust, 1000m, "EXEMPT-LIVE", 0m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
        var f = await svc.GeneratePnd30Async(202605, TaxFilingMode.Preview, default);

        f.Status.Should().Be("Preview");
        f.Lines.SalesTaxable.Amount.Should().BeGreaterThan(0m);
        f.Lines.SalesExempt.Amount.Should().BeGreaterThan(0m);
        f.Lines.OutputVatTotal.Should().Be(f.Lines.SalesTaxable.Vat);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.TaxFilings.AnyAsync(x => x.FormType == "PND30" && x.Period == 202605))
            .Should().BeFalse("preview must not persist");
    }

    [SkippableFact]
    public async Task Pnd30_finalize_is_immutable()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 2500m, "VAT7", 0.07m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();

        // PostgresFixture persists rows across runs; finalize is immutable per
        // (form, period) — use a unique far-future period so re-runs never clash.
        var period = (3000 + Random.Shared.Next(0, 6000)) * 100 + Random.Shared.Next(1, 13);

        var f = await svc.GeneratePnd30Async(period, TaxFilingMode.Finalize, default);
        f.Status.Should().BeOneOf("Finalized", "Submitted");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var row = await db.TaxFilings.FirstAsync(x => x.FormType == "PND30" && x.Period == period);
        row.FinalizedAt.Should().NotBeNull();
        row.PayloadJson.Should().Contain("salesTaxable");

        var act = () => svc.GeneratePnd30Async(period, TaxFilingMode.Finalize, default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("tax_filing.already_finalized");
    }

    [SkippableFact]
    public async Task Output_vat_register_lists_posted_ti_with_category()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var cust = await CustomerId(sp);
        await PostTi(sp, cust, 3000m, "VAT7", 0.07m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxFilingService>();
        var reg = await svc.OutputVatRegisterAsync(202605, default);

        reg.Rows.Should().Contain(r => r.DocType == "TI" && r.Category == "TAXABLE");
        reg.VatTotal.Should().Be(reg.Rows.Sum(r => r.Vat));
    }
}

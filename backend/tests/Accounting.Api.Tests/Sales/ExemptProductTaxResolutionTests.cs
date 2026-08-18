using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Master;
using Accounting.Application.Sales;
using Accounting.Domain.Entities.Sales;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-review-n-findings-2026-08-17.md — N1 (ม.81 exempt-product clamp) + N3
/// (case-insensitive tax-code lookup). N1: an EXEMPT_GOOD/EXEMPT_SERVICE product line must
/// NEVER store TaxRate &gt; 0, whatever tax code the caller sends — ม.81 exemption is a
/// property of the product MASTER, not of the request. N3: a request tax-code that differs
/// from a master row only by case must resolve to that row (was: silently charged 7%).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ExemptProductTaxResolutionTests
{
    private readonly PostgresFixture _fx;
    public ExemptProductTaxResolutionTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    // Doc date in the CURRENT month so the accounting period is open.
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    // Products are created through IProductService.CreateAsync — the only path that can set
    // DefaultOutputTaxCodeId (N.3/N.4 — the product settings screen always nulls it).
    private static async Task<long> CreateProductAsync(
        ServiceProvider sp, string productType, int? defaultOutputTaxCodeId = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IProductService>();
        return await svc.CreateAsync(new CreateProductRequest(
            TestIds.ProductCode(), "สินค้าทดสอบ", null, productType, null, null,
            defaultOutputTaxCodeId, null, null, null, null), default);
    }

    private static async Task<(TaxInvoiceLine Line, TaxInvoice Ti)> CreateTiLineAsync(
        ServiceProvider sp, long customerId, long? productId, string? taxCode, int? taxCodeId,
        decimal taxRate, decimal price = 1000m, string? productType = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(productId, null, "บริการ", 1m, 1, "ครั้ง", price, 0m,
                taxCodeId, taxCode, taxRate, productType)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().Include(x => x.Lines)
            .FirstAsync(x => x.TaxInvoiceId == id);
        return (ti.Lines.OrderBy(l => l.LineNo).First(), ti);
    }

    private static async Task<int> MasterTaxCodeIdAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxCodes.Where(c => c.CompanyId == companyId && c.Code == code)
            .Select(c => c.TaxCodeId).SingleAsync();
    }

    private static async Task<bool> IsExemptCodeAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxCodes.Where(c => c.CompanyId == companyId && c.Code == code)
            .Select(c => c.IsExempt).SingleAsync();
    }

    // ══════════════════════════ T-N1 — the exempt-product clamp ══════════════════════════

    [SkippableFact]
    public async Task Exempt_product_with_no_tax_code_never_charges_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");

        // The client lies (taxRate: 0.07m) — the server must ignore it (trap §9.1).
        var (line, ti) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: null, taxCodeId: null, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m);
        line.TaxAmount.Should().Be(0m);
        line.TaxCodeId.Should().NotBe(0, "a real master row, not the synthetic sentinel");
        (await IsExemptCodeAsync(sp, c.CompanyId, line.TaxCode)).Should().BeTrue();
        ti.TaxAmount.Should().Be(0m);
        ti.TaxableAmount.Should().Be(0m);
        ti.NonTaxableAmount.Should().Be(line.LineAmount);
    }

    [SkippableFact]
    public async Task Exempt_product_ignores_a_taxable_code_the_caller_supplied()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");

        // taxCodeId is irrelevant here — Resolve looks the code up by string, then rewrites
        // the id from the master row it matches.
        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: "VAT7", taxCodeId: 1, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m, "ladder step 2c — a taxable code on an exempt product is discarded, not applied");
        line.TaxCode.Should().NotBe("VAT7");
        (await IsExemptCodeAsync(sp, c.CompanyId, line.TaxCode)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Exempt_product_ignores_a_zero_rated_code_the_caller_supplied()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: "VAT-OUT-0-EXP", taxCodeId: 1, taxRate: 0m);

        line.TaxRate.Should().Be(0m);
        (await IsExemptCodeAsync(sp, c.CompanyId, line.TaxCode)).Should().BeTrue(
            "the ภ.พ.30 bucket must be EXEMPT, not ZERO_RATED — a rate-only assertion would miss this (ladder step 2c)");
    }

    [SkippableFact]
    public async Task Exempt_product_honours_an_exempt_code_the_caller_supplied()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: "EXEMPT-BOOK", taxCodeId: 1, taxRate: 0m);

        line.TaxRate.Should().Be(0m);
        line.TaxCode.Should().Be("EXEMPT-BOOK");
        line.TaxCodeId.Should().Be(expectedId);
    }

    [SkippableFact]
    public async Task Exempt_product_uses_its_own_default_output_tax_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var exemptBookId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD", defaultOutputTaxCodeId: exemptBookId);

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: null, taxCodeId: null, taxRate: 0.07m);

        line.TaxCode.Should().Be("EXEMPT-BOOK", "ladder step 3 — the tenant curated this default");
        line.TaxCodeId.Should().Be(exemptBookId);
        line.TaxRate.Should().Be(0m);
    }

    [SkippableFact]
    public async Task Exempt_product_ignores_a_non_exempt_product_default()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var vat7Id = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");
        var productId = await CreateProductAsync(sp, "EXEMPT_SERVICE", defaultOutputTaxCodeId: vat7Id);

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: null, taxCodeId: null, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m, "a mis-set master default must never charge VAT (ladder step 3 rejected -> 4)");
        line.TaxCode.Should().NotBe("VAT7");
        (await IsExemptCodeAsync(sp, c.CompanyId, line.TaxCode)).Should().BeTrue();
    }

    [SkippableFact]
    public async Task Taxable_product_is_unaffected_by_the_exempt_ladder()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var zeroRatedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT-OUT-0-EXP");
        var productId = await CreateProductAsync(sp, "GOOD", defaultOutputTaxCodeId: zeroRatedId);

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId, taxCode: null, taxCodeId: null, taxRate: 0m);

        // Rule D is DEFERRED: a taxable product's DefaultOutputTaxCodeId is NOT consulted.
        // If this fails, someone implemented Rule D.
        line.TaxRate.Should().Be(0.07m);
        line.TaxCode.Should().Be("VAT7");
    }

    [SkippableFact]
    public async Task Free_text_line_claiming_exempt_type_still_charges_vat()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        var (line, _) = await CreateTiLineAsync(
            sp, c.CustomerId, productId: null, taxCode: null, taxCodeId: null, taxRate: 0m, productType: "EXEMPT_GOOD");

        // No productId ⇒ productDefaults never resolves ⇒ exemptProduct is false. A caller's
        // claimed type string is not master data — pins the §N1.2 boundary.
        line.TaxRate.Should().Be(0.07m);
        line.TaxCode.Should().Be("VAT7");
    }

    [SkippableFact]
    public async Task Exempt_product_on_a_non_vat_company_stays_on_the_VAT0_sentinel()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // A Tax Invoice cannot be issued by a non-VAT company (ม.86/4) — use a Quotation line.
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");

        await using var s = sp.CreateAsyncScope();
        var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
        var qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
            Today(), Today().AddDays(30), c.CustomerId, null, "THB", 1m, null, null,
            [new ChainLineInput(productId, "สินค้ายกเว้น", 1m, "ชิ้น", 1000m, 0m, null, null, 0.07m)]), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.QuotationLines.AsNoTracking()
            .Where(l => l.QuotationId == qId).OrderBy(l => l.LineNo).FirstAsync();

        // Ladder step 1 still wins, unchanged — non-VAT trumps even the exempt-product clamp.
        line.TaxRate.Should().Be(0m);
        line.TaxCode.Should().Be("VAT0");
        line.TaxCodeId.Should().Be(0);
    }

    [SkippableFact]
    public async Task Exempt_product_line_keeps_the_journal_balanced()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var productId = await CreateProductAsync(sp, "EXEMPT_GOOD");

        long tiId;
        TaxInvoicePostedResult posted;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [
                    new TaxInvoiceLineInput(productId, null, "สินค้ายกเว้น", 1m, 1, "ชิ้น", 1000m, 0m, null, null, 0.07m),
                    new TaxInvoiceLineInput(null, null, "บริการทั่วไป", 1m, 1, "ครั้ง", 500m, 0m, null, "VAT7", 0.07m),
                ],
                null), default);
            posted = await svc.PostAsync(tiId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var ti = await db.TaxInvoices.AsNoTracking().Include(x => x.Lines)
            .FirstAsync(x => x.TaxInvoiceId == tiId);
        var taxableLine = ti.Lines.Single(l => l.TaxRate > 0m);

        (ti.TaxableAmount + ti.NonTaxableAmount).Should().Be(ti.SubtotalAmount);

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleAsync(j => j.Reference == posted.DocNo);
        je.TotalDebit.Should().Be(je.TotalCredit, "M4 — Dr=Cr for every posted document");
        var vatLine = je.Lines.Single(l => l.Description != null && l.Description.StartsWith("Output VAT", StringComparison.Ordinal));
        vatLine.CreditAmount.Should().Be(taxableLine.TaxAmount,
            "the output-VAT credit equals the taxable line's TaxAmount exactly — the exempt line contributes nothing");
    }

    // ══════════════════════════ T-N3 — case-insensitive tax-code lookup ══════════════════════════

    [SkippableFact]
    public async Task Mixed_case_exempt_code_resolves_and_stores_the_master_casing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId: null, taxCode: "exempt-book", taxCodeId: 1, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m);
        line.TaxCode.Should().Be("EXEMPT-BOOK", "the MASTER row's casing, not the caller's — trap §9.2");
        line.TaxCodeId.Should().Be(expectedId);
    }

    [SkippableFact]
    public async Task Mixed_case_zero_rated_code_resolves_and_stores_the_master_casing()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT-OUT-0-EXP");

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId: null, taxCode: "vat-out-0-exp", taxCodeId: 1, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m);
        line.TaxCode.Should().Be("VAT-OUT-0-EXP");
        line.TaxCodeId.Should().Be(expectedId);
    }

    [SkippableFact]
    public async Task Exact_case_code_still_resolves_unchanged()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");

        var (line, _) = await CreateTiLineAsync(sp, c.CustomerId, productId: null, taxCode: "EXEMPT-BOOK", taxCodeId: 1, taxRate: 0.07m);

        line.TaxRate.Should().Be(0m);
        line.TaxCode.Should().Be("EXEMPT-BOOK");
        line.TaxCodeId.Should().Be(expectedId);
    }
}

using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// specs/fix-chain-conversion-integrity.md — F13. <c>tax_code_id</c> is a GLOBAL identity
/// column on a per-company table (tax.tax_codes), yet six frontend forms hardcoded
/// <c>taxCodeId: 1</c>. On any company other than the one that happens to own row 1, every
/// sales line stored another tenant's tax-code id, and an unknown/orphan code string (like the
/// prod "V7" row) was stored verbatim instead of resolving to the company's real code. These
/// prove the new SalesLineBackstop.Resolve ladder (I4): a matched code stores the MASTER row's
/// own (id, code); an unmatched/blank code resolves to the company's OWN standard output code
/// (never a foreign id, never an orphan string); and a tenant with no tax-code master at all
/// never throws (memory seed-cos-bypass-createasync-taxcodes).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxCodePairIntegrityTests
{
    private readonly PostgresFixture _fx;
    public TaxCodePairIntegrityTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    // Doc date in the CURRENT month so the accounting period is open.
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    private static async Task<(decimal Rate, string Code, int TaxCodeId)> CreateLineAsync(
        ServiceProvider sp, long customerId, string taxCode, decimal price = 1000m)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", price, 0m, 1, taxCode, 0m)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == id).OrderBy(l => l.LineNo).FirstAsync();
        return (line.TaxRate, line.TaxCode ?? "", line.TaxCodeId);
    }

    private static async Task<int> MasterTaxCodeIdAsync(ServiceProvider sp, int companyId, string code)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        return await db.TaxCodes.Where(c => c.CompanyId == companyId && c.Code == code)
            .Select(c => c.TaxCodeId).SingleAsync();
    }

    [SkippableFact]
    public async Task Unknown_request_code_resolves_to_the_companys_own_standard_output_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");

        // "V7" — the orphan code that reached prod (F13): not a typo of "VAT7" that a fuzzy
        // match should catch, an unrelated string absent from EVERY company's master.
        var line = await CreateLineAsync(sp, c.CustomerId, "V7");

        line.Code.Should().Be("VAT7", "an orphan/unknown code must resolve to the company's own standard output code");
        line.Rate.Should().Be(0.07m);
        line.TaxCodeId.Should().Be(expectedId);
        line.TaxCodeId.Should().NotBe(1, "the hardcoded frontend taxCodeId:1 must never survive — this company's own row is not id 1");
    }

    [SkippableFact]
    public async Task Null_request_code_resolves_to_the_companys_own_standard_output_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "VAT7");

        // WP-5 (§3.6) — the frontend no longer sends a taxCodeId/taxCode when the user hasn't
        // touched the tax-code picker (was: hardcoded taxCodeId:1, taxCode:'V7'). A null pair
        // must resolve exactly like an unmatched code (ladder step 3), never 422 — trap §9.6.
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), c.CustomerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", 1000m, 0m, null, null, 0m)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == id).OrderBy(l => l.LineNo).FirstAsync();

        line.TaxCode.Should().Be("VAT7");
        line.TaxRate.Should().Be(0.07m);
        line.TaxCodeId.Should().Be(expectedId);
    }

    // WP-5 trap §9.6 — leaving NotEmpty() on CreateTaxInvoiceValidator's TaxCode rule 422s
    // every tax invoice the instant the frontend sends null (no more hardcoded
    // taxCodeId:1/taxCode:'V7'). CreateDraftAsync above is called SERVICE-side, bypassing the
    // endpoint's validator entirely — this is the only test in this file that actually
    // exercises the validator FluentValidation.NotEmpty() would otherwise 422 on.
    [Fact]
    public void Validator_accepts_a_request_with_no_tax_code()
    {
        var v = new CreateTaxInvoiceValidator();
        var req = new CreateTaxInvoiceRequest(
            new DateOnly(2026, 6, 1), 1, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", 1000m, 0m, null, null, 0.07m)],
            null);
        v.Validate(req).IsValid.Should().BeTrue();
    }

    [SkippableFact]
    public async Task Exempt_code_keeps_its_code_and_id_and_zero_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);
        var expectedId = await MasterTaxCodeIdAsync(sp, c.CompanyId, "EXEMPT-BOOK");

        var line = await CreateLineAsync(sp, c.CustomerId, "EXEMPT-BOOK");

        line.Code.Should().Be("EXEMPT-BOOK");
        line.Rate.Should().Be(0m);
        line.TaxCodeId.Should().Be(expectedId);
    }

    [SkippableFact]
    public async Task Company_with_no_tax_code_master_still_creates_a_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        // Simulate a raw-SQL-seeded tenant with zero tax codes (memory
        // seed-cos-bypass-createasync-taxcodes). tax.tax_rates has an FK to tax.tax_codes
        // (TaxRateConfiguration.cs:18) — delete the child rate rows first.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var codeIds = await db.TaxCodes.Where(x => x.CompanyId == c.CompanyId)
                .Select(x => x.TaxCodeId).ToListAsync();
            var rates = await db.TaxRates.Where(r => codeIds.Contains(r.TaxCodeId)).ToListAsync();
            db.TaxRates.RemoveRange(rates);
            await db.SaveChangesAsync();
            var codes = await db.TaxCodes.Where(x => x.CompanyId == c.CompanyId).ToListAsync();
            db.TaxCodes.RemoveRange(codes);
            await db.SaveChangesAsync();
        }

        var line = await CreateLineAsync(sp, c.CustomerId, "VAT7");

        line.Code.Should().Be("VAT7", "byte-for-byte today's legacy fallback code");
        line.Rate.Should().Be(0.07m, "the company's configured VAT rate — the resolver never throws on an empty master");
        line.TaxCodeId.Should().Be(0, "SYNTHETIC_TAX_CODE_ID — no master row backs this line's code");
    }
}

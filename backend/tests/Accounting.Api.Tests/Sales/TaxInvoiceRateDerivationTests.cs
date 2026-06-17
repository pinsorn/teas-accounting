using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Sales;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Sales;

/// <summary>
/// §4.6 / ม.80 — the per-line VAT RATE on a sales document is COMPANY MASTER DATA, never
/// the caller's input. A non-UI client can POST a Tax Invoice line with taxCode "VAT7" and
/// taxRate 0 (or any wrong rate); the backend MUST derive the rate from the line's tax-code
/// classification and the company's configured vat_rate, ignoring the caller's taxRate. This
/// closes the "VAT-coded line that carries 0 VAT" compliance hole.
///
/// The direct Tax-Invoice create path (POST /tax-invoices → TaxInvoiceService.CreateDraftAsync)
/// is the exact path that produced the bug, so it is covered explicitly here. Tests run against
/// their OWN TestCompanyFactory company (a VAT company at 0.07 and a non-VAT company) — never
/// company 1's shared row.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxInvoiceRateDerivationTests
{
    private readonly PostgresFixture _fx;
    public TaxInvoiceRateDerivationTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId, long userId = 1) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId, userId);

    // Doc date in the CURRENT month so the accounting period is open.
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    // Create a single-line draft TI through the request-fed path and return the persisted line.
    private static async Task<(decimal Rate, decimal Vat, string Code, decimal Total)> CreateLineAsync(
        ServiceProvider sp, long customerId, string taxCode, decimal callerRate, decimal price = 1000m)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            Today(), customerId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "บริการ", 1m, 1, "ครั้ง", price, 0m, 1, taxCode, callerRate)],
            null), default);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == id).OrderBy(l => l.LineNo).FirstAsync();
        return (line.TaxRate, line.TaxAmount, line.TaxCode ?? "", line.TotalAmount);
    }

    // ── 1. VAT company, standard line, caller sends taxRate=0 → forced to 0.07, VAT charged ──
    [SkippableFact]
    public async Task DirectTi_vat_company_standard_code_zero_rate_is_forced_to_company_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        // Caller lies: VAT7 but taxRate 0 — the old bug issued a VAT line with 0 VAT.
        var line = await CreateLineAsync(sp, c.CustomerId, "VAT7", callerRate: 0m, price: 1000m);

        line.Rate.Should().Be(0.07m);          // derived from companies.vat_rate, NOT the caller's 0
        line.Vat.Should().Be(70m);             // 1000 × 7%
        line.Code.Should().Be("VAT7");
        line.Total.Should().Be(1070m);
    }

    // ── 2. VAT company, deliberately WRONG caller rate 0.99 → still forced to 0.07 ──
    [SkippableFact]
    public async Task DirectTi_vat_company_wrong_caller_rate_is_ignored()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        var line = await CreateLineAsync(sp, c.CustomerId, "VAT7", callerRate: 0.99m, price: 1000m);

        line.Rate.Should().Be(0.07m);          // caller input ignored entirely
        line.Vat.Should().Be(70m);
        line.Code.Should().Be("VAT7");
    }

    // ── 3. VAT company, zero-rated code (is_zero_rated, ม.80/1) → rate 0, code kept ──
    [SkippableFact]
    public async Task DirectTi_vat_company_zero_rated_code_yields_zero_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        // Caller even lies with rate 0.07 — a zero-rated export must stay 0.
        var line = await CreateLineAsync(sp, c.CustomerId, "VAT-OUT-0-EXP", callerRate: 0.07m, price: 1000m);

        line.Rate.Should().Be(0m);
        line.Vat.Should().Be(0m);
        line.Code.Should().Be("VAT-OUT-0-EXP");   // code preserved
    }

    // ── 4. VAT company, exempt code (is_exempt, ม.81) → rate 0, code kept ──
    [SkippableFact]
    public async Task DirectTi_vat_company_exempt_code_yields_zero_rate()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        var line = await CreateLineAsync(sp, c.CustomerId, "EXEMPT-LIVE", callerRate: 0.07m, price: 1000m);

        line.Rate.Should().Be(0m);
        line.Vat.Should().Be(0m);
        line.Code.Should().Be("EXEMPT-LIVE");
    }

    // ── 5. DO→TI: a Delivery Order's lines carry RAW client taxRate (DO builders do not derive),
    //       so the TI created from it must DERIVE — a "VAT7 + taxRate:0" DO must not mint a posted
    //       VAT7 line carrying 0 VAT (the bug, reachable via POST /delivery-orders/{id}/create-tax-invoice). ──
    [SkippableFact]
    public async Task DoToTi_vat_company_derives_rate_despite_zero_rate_on_delivery_order()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long doId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var dosvc = s0.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            // Caller lies on the DO: VAT7 but taxRate 0.
            doId = await dosvc.CreateDraftAsync(new CreateDeliveryOrderRequest(
                Today(), c.CustomerId, null, IsCombinedWithTi: false, null, null,
                [new DeliveryLineInput(null, null, "ส่งของ", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0m)]),
                default);
            await dosvc.IssueAsync(doId, default);
            await dosvc.MarkDeliveredAsync(doId, default);
        }

        long tiId;
        await using (var s1 = sp.CreateAsyncScope())
        {
            var dosvc = s1.ServiceProvider.GetRequiredService<IDeliveryOrderService>();
            tiId = await dosvc.CreateTaxInvoiceAsync(doId, default);   // creates + posts the TI
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.TaxInvoiceLines.AsNoTracking()
            .Where(l => l.TaxInvoiceId == tiId).OrderBy(l => l.LineNo).FirstAsync();
        line.TaxRate.Should().Be(0.07m);     // derived on the TI, NOT inherited from the DO's bogus 0
        line.TaxAmount.Should().Be(70m);
        line.TaxCode.Should().Be("VAT7");
    }

    // ── 6. CN/DN: the note rate is DERIVED from the original posted TI + master data, not the
    //       caller's req.TaxRate. A VAT company issuing a CN against a VAT-bearing TI with a
    //       deliberately-wrong req.TaxRate (0) must still apply the company rate. ──
    [SkippableFact]
    public async Task CreditNote_vat_company_derives_rate_from_original_ti()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        // A posted VAT-bearing TI to adjust.
        long tiId;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc = s0.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
                Today(), c.CustomerId, false, "THB", 1m, null, null, null,
                [new TaxInvoiceLineInput(null, null, "ขาย", 1m, 1, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)],
                null), default);
            await svc.PostAsync(tiId, default);
        }

        long noteId;
        await using (var s1 = sp.CreateAsyncScope())
        {
            var nsvc = s1.ServiceProvider.GetRequiredService<ITaxAdjustmentNoteService>();
            // Caller lies: req.TaxRate 0 on a CN against a VAT TI.
            noteId = await nsvc.CreateDraftAsync(new CreateTaxAdjustmentNoteRequest(
                TaxAdjustmentNoteType.Credit, Today(), tiId, "RETURN", "คืนสินค้า",
                500m, 0m, "THB", 1m, null), default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var note = await db.TaxAdjustmentNotes.AsNoTracking().FirstAsync(n => n.NoteId == noteId);
        note.TaxRate.Should().Be(0.07m);       // derived from the VAT-bearing original, not the caller's 0
        note.TaxAmount.Should().Be(35m);       // 500 × 7%
        note.TotalAmount.Should().Be(535m);
    }

    // ── 7. Non-VAT company → rate 0 regression guard (ม.86 chokepoint blocks TI, so use
    //       the Quotation origin builder which a non-VAT company CAN create) ──
    [SkippableFact]
    public async Task NonVat_company_quotation_standard_code_is_forced_to_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: false);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long qId;
        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            // Caller lies: VAT7 + 0.07 on a NON-VAT company → must be forced to VAT0 / 0.
            qId = await svc.CreateDraftAsync(new CreateQuotationRequest(
                Today(), Today().AddDays(30), c.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "ขาย", 1m, "ชิ้น", 1000m, 0m, 1, "VAT7", 0.07m)]),
                default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var line = await db.QuotationLines.AsNoTracking()
            .Where(l => l.QuotationId == qId).OrderBy(l => l.LineNo).FirstAsync();
        line.TaxRate.Should().Be(0m);
        line.TaxAmount.Should().Be(0m);
        line.TaxCode.Should().Be("VAT0");
    }
}

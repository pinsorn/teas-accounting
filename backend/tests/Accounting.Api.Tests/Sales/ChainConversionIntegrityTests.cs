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
/// specs/fix-chain-conversion-integrity.md — F8 (SO→DO) + F8b (Quotation→TI). Both browser
/// screens used to rebuild the create-request from a thin line DTO and invented a discount of
/// 0, dropping the SO-line link. These prove the new SERVER-SIDE conversions (lines copied from
/// the tracked source entity, never invented client-side) preserve the source document's
/// discount/amount exactly — I2 and I3.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ChainConversionIntegrityTests
{
    private readonly PostgresFixture _fx;
    public ChainConversionIntegrityTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId, int branchId) =>
        TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);

    // Doc date in the CURRENT month so the accounting period is open.
    private static DateOnly Today()
    {
        var n = DateTime.UtcNow;
        return new DateOnly(n.Year, n.Month, 16);
    }

    // ── T1/T2 shared setup — a posted SO with an undiscounted line + a 15%-discounted line ──
    private async Task<(ServiceProvider Sp, long SoId, long DoId)> CreatePostedSoAndConvertAsync()
    {
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        var sp = Provider(c.CompanyId, c.BranchId);

        long soId;
        await using (var s = sp.CreateAsyncScope())
        {
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            soId = await soSvc.CreateDraftAsync(new CreateSalesOrderRequest(
                Today(), null, c.CustomerId, null, "THB", 1m, null, null,
                [
                    new ChainLineInput(null, "line 1", 1m, "ชิ้น", 500m, 0m, 1, "VAT7", 0.07m),
                    new ChainLineInput(null, "line 2", 2m, "ชิ้น", 1250.00m, 15m, 1, "VAT7", 0.07m),
                ]), default);
            await soSvc.PostAsync(soId, default);
        }

        long doId;
        await using (var s = sp.CreateAsyncScope())
        {
            var soSvc = s.ServiceProvider.GetRequiredService<ISalesOrderService>();
            doId = await soSvc.CreateFullDeliveryOrderAsync(soId, isCombinedWithTi: false, docDate: null, default);
        }

        return (sp, soId, doId);
    }

    [SkippableFact]
    public async Task So_to_do_full_conversion_preserves_line_discount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (sp, soId, doId) = await CreatePostedSoAndConvertAsync();
        await using var _ = sp;

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var so = await db.SalesOrders.AsNoTracking().FirstAsync(x => x.SalesOrderId == soId);
        var dord = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstAsync(x => x.DeliveryOrderId == doId);
        var line2 = dord.Lines.OrderBy(l => l.LineNo).ElementAt(1);

        line2.DiscountPercent.Should().Be(15.00m);
        line2.LineAmount.Should().Be(2125.00m);   // 2 × 1250.00 less 15%
        dord.TotalAmount.Should().Be(so.TotalAmount, "the DO's totals equal the SO's totals, not the undiscounted gross");
    }

    [SkippableFact]
    public async Task So_to_do_links_the_order_line_and_advances_delivered_quantity()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (sp, soId, doId) = await CreatePostedSoAndConvertAsync();
        await using var _ = sp;

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var dord = await db.DeliveryOrders.AsNoTracking().Include(x => x.Lines)
            .FirstAsync(x => x.DeliveryOrderId == doId);
        dord.Lines.Should().OnlyContain(l => l.SalesOrderLineId != null,
            "every DO line converted from a SO must carry the link back — the dead over-delivery guard (F8)");

        var solIds = dord.Lines.Select(l => l.SalesOrderLineId!.Value).ToList();
        var sols = await db.SalesOrderLines.AsNoTracking()
            .Where(l => solIds.Contains(l.LineId)).ToListAsync();
        sols.Should().OnlyContain(l => l.DeliveredQuantity == l.Quantity,
            "a full-quantity conversion must advance DeliveredQuantity to the full ordered qty");
    }

    [SkippableFact]
    public async Task Quotation_to_tax_invoice_preserves_line_discount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long qId;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                Today(), Today().AddDays(30), c.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "line 1", 2m, "ชิ้น", 1250.00m, 15m, 1, "VAT7", 0.07m)]), default);
            await qSvc.SendAsync(qId, default);
            await qSvc.AcceptAsync(qId, default);
        }

        long tiId;
        await using (var s = sp.CreateAsyncScope())
        {
            var tiSvc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
            tiId = await tiSvc.CreateFromQuotationAsync(qId, default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var q = await db.Quotations.AsNoTracking().FirstAsync(x => x.QuotationId == qId);
        var ti = await db.TaxInvoices.AsNoTracking().Include(x => x.Lines)
            .FirstAsync(x => x.TaxInvoiceId == tiId);
        var line = ti.Lines.Single();

        line.DiscountPercent.Should().Be(15.00m);
        ti.TotalAmount.Should().Be(q.TotalAmount);
        ti.QuotationId.Should().Be(q.QuotationId);
    }

    // ── Tier-2 REJECT finding (2026-08-16) — the EDIT path, not conversion. §3.0 Decision 1
    // ("do not widen ChainLineDto") reversed for exactly this: QuotationForm/SalesOrderForm/
    // BillingNoteForm's `toLine` hydrates an edit form from GetAsync's ChainLineDto, and a
    // create-only test suite let a widened-but-unpopulated (or narrow) DTO through green. These
    // two prove the FULL round trip a real edit does: GetAsync → build a request from exactly
    // the DTO's own fields (mirrors `toLine` + the form's payload map byte-for-byte) →
    // UpdateDraftAsync → GetAsync again → nothing moved. ──

    [SkippableFact]
    public async Task Quotation_edit_round_trip_preserves_an_exempt_tax_code()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long qId;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                Today(), Today().AddDays(30), c.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "หนังสือ", 2m, "เล่ม", 500m, 0m, null, "EXEMPT-BOOK", 0m)]),
                default);
        }

        int beforeTaxCodeId;
        string beforeTaxCode;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            var before = await qSvc.GetAsync(qId, default);
            var l = before!.Lines.Single();
            beforeTaxCode = l.TaxCode;
            beforeTaxCodeId = l.TaxCodeId;
            l.TaxAmount.Should().Be(0m, "sanity: the exempt code must already be zero-rated before any edit");

            // The exact shape a correct `toLine` + form payload now builds (Tier-2 fix): the
            // stored code/id/discount come straight off ChainLineDto, never re-invented.
            await qSvc.UpdateDraftAsync(qId, new CreateQuotationRequest(
                before.DocDate, before.ValidUntilDate, before.CustomerId, before.BusinessUnitId,
                before.CurrencyCode, 1m, before.Notes, null,
                [new ChainLineInput(l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                    l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode,
                    l.LineAmount > 0 ? l.TaxAmount / l.LineAmount : 0.07m)]),
                default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var qSvc2 = s2.ServiceProvider.GetRequiredService<IQuotationService>();
        var after = await qSvc2.GetAsync(qId, default);
        var afterLine = after!.Lines.Single();

        afterLine.TaxCode.Should().Be(beforeTaxCode, "an edit round-trip must not lose the stored code — this is F14 through the edit door");
        afterLine.TaxCode.Should().Be("EXEMPT-BOOK");
        afterLine.TaxCodeId.Should().Be(beforeTaxCodeId);
        afterLine.TaxAmount.Should().Be(0m, "an unrelated edit must never turn an exempt line standard-rated");
    }

    [SkippableFact]
    public async Task Quotation_edit_round_trip_preserves_a_line_discount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var c = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true, vatRate: 0.07m);
        await using var sp = Provider(c.CompanyId, c.BranchId);

        long qId;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            qId = await qSvc.CreateDraftAsync(new CreateQuotationRequest(
                Today(), Today().AddDays(30), c.CustomerId, null, "THB", 1m, null, null,
                [new ChainLineInput(null, "line 1", 2m, "ชิ้น", 1250.00m, 15m, null, "VAT7", 0.07m)]),
                default);
        }

        decimal beforeTotal;
        await using (var s = sp.CreateAsyncScope())
        {
            var qSvc = s.ServiceProvider.GetRequiredService<IQuotationService>();
            var before = await qSvc.GetAsync(qId, default);
            beforeTotal = before!.TotalAmount;
            var l = before.Lines.Single();
            l.DiscountPercent.Should().Be(15.00m, "sanity: the 15% discount must already be stored before any edit");

            await qSvc.UpdateDraftAsync(qId, new CreateQuotationRequest(
                before.DocDate, before.ValidUntilDate, before.CustomerId, before.BusinessUnitId,
                before.CurrencyCode, 1m, before.Notes, null,
                [new ChainLineInput(l.ProductId, l.DescriptionTh, l.Quantity, l.UomText,
                    l.UnitPrice, l.DiscountPercent, l.TaxCodeId, l.TaxCode,
                    l.LineAmount > 0 ? l.TaxAmount / l.LineAmount : 0.07m)]),
                default);
        }

        await using var s2 = sp.CreateAsyncScope();
        var qSvc2 = s2.ServiceProvider.GetRequiredService<IQuotationService>();
        var after = await qSvc2.GetAsync(qId, default);
        var afterLine = after!.Lines.Single();

        afterLine.DiscountPercent.Should().Be(15.00m, "an edit round-trip must not lose the stored discount — this is F8 through the edit door");
        afterLine.LineAmount.Should().Be(2125.00m, "2 × 1250.00 less 15%, unchanged by the round trip");
        after.TotalAmount.Should().Be(beforeTotal, "an unrelated edit must never silently change the total");
    }
}

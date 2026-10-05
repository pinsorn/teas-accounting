using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Sales;
using Accounting.Application.Reports;
using Accounting.Application.Sales;
using Accounting.Application.TaxFilings;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// specs/cancel-reissue-sales-docs.md WP-3: T11 (AR tie-out across a closed-month cancel+reissue),
/// T13 (ภ.พ.30 / output register unchanged), T14 (voided rows at 0.00 + remarks + cross-month memo).
/// Everything is posted through the REAL services; far-future 2031 dates via FixedClock (spec 6).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CancelReissueReportTests
{
    private readonly PostgresFixture _fx;
    public CancelReissueReportTests(PostgresFixture fx) => _fx = fx;

    private static readonly DateTimeOffset Mar15 = new(2031, 3, 15, 5, 0, 0, TimeSpan.Zero);   // 12:00 Bangkok
    private static readonly DateTimeOffset Apr06 = new(2031, 4, 6, 5, 0, 0, TimeSpan.Zero);

    private static async Task<T> WithAsync<T>(ServiceProvider sp, Func<IServiceProvider, Task<T>> f)
    {
        await using var s = sp.CreateAsyncScope();
        return await f(s.ServiceProvider);
    }

    /// <summary>Cancel-and-reissue, correct the description (the only edit the lock allows), post the replacement.</summary>
    private static async Task<(long ReplId, string ReplDocNo)> ReissueAsync(ServiceProvider sp, long tiId)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var res = await svc.CancelAndReissueAsync(tiId, "ITEM_DESCRIPTION_ERROR", "คำอธิบายผิด", default);
        var replId = res.ReplacementTaxInvoiceId!.Value;
        var req = (await svc.GetDraftInputAsync(replId, default))!;
        await svc.UpdateDraftAsync(replId, req with { Lines = [req.Lines[0] with { DescriptionTh = "แก้ไขแล้ว" }] }, default);
        var posted = await svc.PostAsync(replId, default);
        return (replId, posted.DocNo);
    }

    private static Task<VatRegisterPeriod> RegAsync(ServiceProvider sp, int y, int m) =>
        WithAsync(sp, p => p.GetRequiredService<IVatReportService>().GetRegisterAsync(y, m, default));

    private static Task<Pnd30Summary> PndAsync(ServiceProvider sp, int y, int m) =>
        WithAsync(sp, p => p.GetRequiredService<IVatReportService>().GetPnd30Async(y, m, default));

    private static Task<OutputVatRegister> OutAsync(ServiceProvider sp, int period) =>
        WithAsync(sp, p => p.GetRequiredService<ITaxFilingService>().OutputVatRegisterAsync(period, default));

    // ── T11 — AR tie-out across a closed-month cancel + reissue (I9) ───────────────────────────

    [SkippableFact]
    public async Task Ar_reconciliation_ties_out_across_cancel()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        long tiA;
        await using (var spMar = CancelKit.Sp(_fx, co, new FixedClock(Mar15)))
        {
            tiA = await CancelKit.PostTiAsync(spMar, co.CustomerId, 1000m, "มีนาคม A");
            await CancelKit.PostTiAsync(spMar, co.CustomerId, 2000m, "มีนาคม B");
        }

        await using var spApr = CancelKit.Sp(_fx, co, new FixedClock(Apr06));
        Task<SubledgerReconciliation> Rec(DateOnly d) => WithAsync(spApr, async p =>
            (await p.GetRequiredService<ISubledgerReportService>().ArAgingAsync(d, null, default)).Reconciliation);
        var mar31 = new DateOnly(2031, 3, 31);
        var apr30 = new DateOnly(2031, 4, 30);

        var before = await Rec(mar31);
        before.Difference.Should().Be(0m);
        before.SubLedgerTotal.Should().Be(3210m);

        // March is closed (implicitly): reversal and replacement JEs are dated Apr 6, the replacement DocDate stays Mar 15.
        var (replId, _) = await ReissueAsync(spApr, tiA);

        var afterMar = await Rec(mar31);
        afterMar.Difference.Should().Be(0m, "the month-end balance must not move when the cancel happens after close");
        afterMar.SubLedgerTotal.Should().Be(3210m);
        afterMar.ControlAccountBalance.Should().Be(3210m);

        var afterApr = await Rec(apr30);
        afterApr.Difference.Should().Be(0m);
        afterApr.SubLedgerTotal.Should().Be(3210m, "orig - reversal + replacement == original");

        // the statement shows the mirror row under its own DocType, dated by the reversal JE
        var st = await WithAsync(spApr, p => p.GetRequiredService<ISubledgerReportService>()
            .CustomerStatementAsync(co.CustomerId, new DateOnly(2031, 3, 1), apr30, default));
        st.Lines.Where(l => l.DocType == "TaxInvoiceCancel").Should().ContainSingle()
            .Which.DocDate.Should().Be(new DateOnly(2031, 4, 6));
        st.Lines.Where(l => l.DocType == "TaxInvoice" && l.DocDate == new DateOnly(2031, 4, 6)).Should().ContainSingle(
            "the replacement is dated by its JE (Apr 6), not its March DocDate");
        st.Reconciliation.Difference.Should().Be(0m);
        replId.Should().BeGreaterThan(0);
    }

    // ── T13 — ภ.พ.30 + output register unchanged (I7) ─────────────────────────────────────────

    [SkippableFact]
    public async Task Pnd30_unchanged_after_cancel_reissue_same_month()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co, new FixedClock(Mar15));
        var tiA = await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "A");
        await CancelKit.PostTiAsync(sp, co.CustomerId, 2000m, "B");

        var pndBefore = await PndAsync(sp, 2031, 3);
        var outBefore = await OutAsync(sp, 203103);
        var filingBefore = await WithAsync(sp, p => p.GetRequiredService<ITaxFilingService>()
            .GeneratePnd30Async(203103, TaxFilingMode.Preview, default));
        pndBefore.OutputVat.Should().Be(210m);

        await ReissueAsync(sp, tiA);

        (await PndAsync(sp, 2031, 3)).Should().Be(pndBefore);
        var outAfter = await OutAsync(sp, 203103);
        outAfter.SubtotalTotal.Should().Be(outBefore.SubtotalTotal);
        outAfter.VatTotal.Should().Be(outBefore.VatTotal);
        outAfter.Rows.Should().HaveCount(outBefore.Rows.Count + 1, "the voided original stays listed next to the replacement");
        (await WithAsync(sp, p => p.GetRequiredService<ITaxFilingService>()
            .GeneratePnd30Async(203103, TaxFilingMode.Preview, default))).Lines.Should().BeEquivalentTo(filingBefore.Lines);
    }

    [SkippableFact]
    public async Task Pnd30_unchanged_after_cancel_reissue_cross_month_closed_and_filed_payload_byte_equal()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        long tiA;
        await using (var spMar = CancelKit.Sp(_fx, co, new FixedClock(Mar15)))
        {
            tiA = await CancelKit.PostTiAsync(spMar, co.CustomerId, 1000m, "A");
            await CancelKit.PostTiAsync(spMar, co.CustomerId, 2000m, "B");
        }

        await using var spApr = CancelKit.Sp(_fx, co, new FixedClock(Apr06));
        var pndBefore = await PndAsync(spApr, 2031, 3);
        var outBefore = await OutAsync(spApr, 203103);
        await WithAsync(spApr, async p =>
            await p.GetRequiredService<ITaxFilingService>().GeneratePnd30Async(203103, TaxFilingMode.Finalize, default));
        async Task<string> PayloadAsync() => await CancelKit.WithDbAsync(spApr, db => db.TaxFilings.AsNoTracking()
            .Where(f => f.FormType == "PND30" && f.Period == 203103).Select(f => f.PayloadJson).SingleAsync());
        var payloadBefore = await PayloadAsync();

        await ReissueAsync(spApr, tiA);

        (await PndAsync(spApr, 2031, 3)).Should().Be(pndBefore);
        var outAfter = await OutAsync(spApr, 203103);
        outAfter.SubtotalTotal.Should().Be(outBefore.SubtotalTotal);
        outAfter.VatTotal.Should().Be(outBefore.VatTotal);
        (await PayloadAsync()).Should().Be(payloadBefore, "a finalized filing is never rewritten");
        // the live recompute equals the filed snapshot, so nothing needs amending
        var live = await WithAsync(spApr, p => p.GetRequiredService<ITaxFilingService>()
            .GeneratePnd30Async(203103, TaxFilingMode.Preview, default));
        live.Lines.OutputVatTotal.Should().Be(pndBefore.OutputVat);
    }

    [SkippableFact]
    public async Task Standalone_cancel_lowers_month_vat_by_exactly_the_tax_amount()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co, new FixedClock(Mar15));
        await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "A");
        var tiB = await CancelKit.PostTiAsync(sp, co.CustomerId, 2000m, "B");   // VAT 140

        var pndBefore = await PndAsync(sp, 2031, 3);
        var outBefore = await OutAsync(sp, 203103);
        await WithAsync(sp, p => p.GetRequiredService<ITaxInvoiceService>()
            .CancelAsync(tiB, "ISSUED_IN_ERROR", "ออกผิด", default));

        var pndAfter = await PndAsync(sp, 2031, 3);
        (pndBefore.OutputVat - pndAfter.OutputVat).Should().Be(140m);
        (pndBefore.Sales - pndAfter.Sales).Should().Be(2000m);
        var outAfter = await OutAsync(sp, 203103);
        (outBefore.VatTotal - outAfter.VatTotal).Should().Be(140m);
        outAfter.Rows.Should().HaveCount(outBefore.Rows.Count, "the voided row is never omitted");
    }

    // ── T14 — voided rows listed at 0.00 with remarks; cross-month memo row (I7, 3.5.1) ────────

    [SkippableFact]
    public async Task Sales_register_lists_voided_at_zero_with_remarks()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        await using var sp = CancelKit.Sp(_fx, co, new FixedClock(Mar15));
        var tiA = await CancelKit.PostTiAsync(sp, co.CustomerId, 1000m, "A");
        var tiB = await CancelKit.PostTiAsync(sp, co.CustomerId, 2000m, "B");
        var docA = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.Where(t => t.TaxInvoiceId == tiA).Select(t => t.DocNo!).SingleAsync());
        var docB = await CancelKit.WithDbAsync(sp, db => db.TaxInvoices.Where(t => t.TaxInvoiceId == tiB).Select(t => t.DocNo!).SingleAsync());

        var (_, replNo) = await ReissueAsync(sp, tiA);
        await WithAsync(sp, p => p.GetRequiredService<ITaxInvoiceService>().CancelAsync(tiB, "DUPLICATE", "ซ้ำ", default));

        var reg = await RegAsync(sp, 2031, 3);
        var a = reg.Sales.Single(r => r.DocNo == docA);
        a.Status.Should().Be("Voided");
        (a.SubtotalAmount, a.TaxAmount, a.TotalAmount).Should().Be((0m, 0m, 0m));
        a.Remark.Should().Be($"ยกเลิก — ออกแทนโดย {replNo}");
        var repl = reg.Sales.Single(r => r.DocNo == replNo);
        repl.Status.Should().Be("Posted");
        repl.TotalAmount.Should().Be(1070m);
        repl.Remark.Should().Be($"ออกแทนเลขที่ {docA} ลงวันที่ 15/03/2574");
        var b = reg.Sales.Single(r => r.DocNo == docB);
        b.Status.Should().Be("Voided");
        b.Remark.Should().Be("ยกเลิก");
        b.TotalAmount.Should().Be(0m);
        reg.OutputVatTotal.Should().Be(70m, "only the replacement counts");

        var outReg = await OutAsync(sp, 203103);
        outReg.Rows.Where(r => r.Status == "Voided").Should().HaveCount(2)
            .And.OnlyContain(r => r.Category == "CANCELLED" && r.Subtotal == 0m && r.Vat == 0m && r.Total == 0m);
        outReg.Rows.Single(r => r.DocNo == replNo).Remark.Should().Be(repl.Remark);
        outReg.VatTotal.Should().Be(70m);
    }

    [SkippableFact]
    public async Task Sales_register_cross_month_cancel_adds_zero_memo_row_in_cancel_month()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var co = await CancelKit.VatCoAsync(_fx);
        long tiA;
        await using (var spMar = CancelKit.Sp(_fx, co, new FixedClock(Mar15)))
            tiA = await CancelKit.PostTiAsync(spMar, co.CustomerId, 1000m, "A");
        await using var spApr = CancelKit.Sp(_fx, co, new FixedClock(Apr06));
        var docA = await CancelKit.WithDbAsync(spApr, db => db.TaxInvoices.Where(t => t.TaxInvoiceId == tiA).Select(t => t.DocNo!).SingleAsync());

        var (_, replNo) = await ReissueAsync(spApr, tiA);

        // April: nothing dated April, but the cancel happened in April -> one zero memo row, never any amount.
        var apr = await RegAsync(spApr, 2031, 4);
        var memo = apr.Sales.Should().ContainSingle().Which;
        memo.DocNo.Should().Be(docA);
        memo.DocDate.Should().Be(new DateOnly(2031, 3, 15));
        memo.Status.Should().Be("Voided");
        (memo.SubtotalAmount, memo.TaxAmount, memo.TotalAmount).Should().Be((0m, 0m, 0m));
        memo.Remark.Should().Be("หมายเหตุ: ยกเลิกใบลงวันที่ 15/03/2574 — ไม่นำมารวมยอดเดือนนี้");
        apr.OutputVatTotal.Should().Be(0m);
        var aprOut = await OutAsync(spApr, 203104);
        aprOut.Rows.Should().ContainSingle().Which.Category.Should().Be("CANCELLED");
        aprOut.VatTotal.Should().Be(0m);

        // March (closed): the voided original AND the replacement (same DocDate) are both listed.
        var mar = await RegAsync(spApr, 2031, 3);
        mar.Sales.Select(r => r.DocNo).Should().BeEquivalentTo(new[] { docA, replNo });
        mar.Sales.Single(r => r.DocNo == docA).Status.Should().Be("Voided");
        mar.OutputVatTotal.Should().Be(70m);
        // the memo wording is only for the cancel month: March's own voided row has the ordinary remark
        mar.Sales.Single(r => r.DocNo == docA).Remark.Should().Be($"ยกเลิก — ออกแทนโดย {replNo}");
    }
}

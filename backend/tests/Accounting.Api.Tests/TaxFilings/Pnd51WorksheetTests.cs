using System;
using System.IO;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Tax;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Pure unit tests for the ภ.ง.ด.51 page-2 Method-A worksheet compute + attestation guard
/// (<see cref="Pnd51FilingService.BuildWorksheet"/>). No Postgres → these ALWAYS run, so the
/// compliance guard (ภ.ง.ด.51 §4) is never silently skipped.
/// </summary>
public sealed class Pnd51WorksheetTests
{
    private static CitRateSchedule General => CitRateSchedule.General();
    private static Pnd51Attestation Clean => new(
        FirstFiling: true, NoLossCarryForward: true, NoExemption: true,
        NoRateReduction: true, NoSurcharge: true);

    // ── guard: refuses anything it can't stand behind (ภ.ง.ด.51 §4 — omission asserts zero) ──

    [Fact] // ภ.ง.ด.51 §4 attestation
    public void Not_requested_returns_null()
        => Pnd51FilingService.BuildWorksheet(
            fillWorksheet: false, attest: null, isSme: false,
            estimate: 200_000m, revenueFullYear: 400_000m, expenseFullYear: 200_000m,
            whtSufferedH1: 0m, schedule: General).Should().BeNull();

    [Fact] // ภ.ง.ด.51 §4 attestation
    public void Requested_without_attestation_throws()
        => ThrowsNotAttestable(() => Pnd51FilingService.BuildWorksheet(
            true, attest: null, isSme: false, 200_000m, 400_000m, 200_000m, 0m, General));

    [Theory] // any single unclean flag ⇒ refuse
    [InlineData(false, true, true, true, true)]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, false, true, true)]
    [InlineData(true, true, true, false, true)]
    [InlineData(true, true, true, true, false)]
    public void Requested_with_any_unclean_flag_throws(bool first, bool noCf, bool noEx, bool noRr, bool noSur)
        => ThrowsNotAttestable(() => Pnd51FilingService.BuildWorksheet(
            true, new Pnd51Attestation(first, noCf, noEx, noRr, noSur), isSme: false,
            200_000m, 400_000m, 200_000m, 0m, General));

    [Fact] // v1 ships general-rate only (SME rate radio not yet confirmed)
    public void Requested_for_sme_throws_even_when_clean()
        => ThrowsNotAttestable(() => Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: true, 200_000m, 400_000m, 200_000m, 0m, General));

    [Fact] // footing rule A: non-positive estimate ⇒ no tax base ⇒ refuse
    public void Requested_with_non_positive_estimate_throws()
        => ThrowsNotAttestable(() => Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: false, estimate: 0m, revenueFullYear: 0m, expenseFullYear: 0m,
            whtSufferedH1: 0m, schedule: General));

    [Fact] // footing rule A: WHT exceeding the computed tax would render a ชำระไว้เกิน (non-footing) → refuse
    public void Requested_when_wht_exceeds_tax_throws()
        // estimate 200k general ⇒ tax = 40k × 0.5 = 20k; wht 25k > 20k
        => ThrowsNotAttestable(() => Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: false, 200_000m, 400_000m, 200_000m, whtSufferedH1: 25_000m, schedule: General));

    // ── happy path: a clean, footing worksheet ──

    [Fact] // default path: 51/52/53-54 present and foot
    public void Clean_default_path_fills_a_footing_worksheet()
    {
        var w = Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: false,
            estimate: 200_000m, revenueFullYear: 400_000m, expenseFullYear: 200_000m,
            whtSufferedH1: 5_000m, schedule: General)!;

        w.RevenueFullYear.Should().Be(400_000m);
        w.ExpenseFullYear.Should().Be(200_000m);
        // foots: รายรับ − รายจ่าย = กำไรสุทธิประมาณการ
        (w.RevenueFullYear!.Value - w.ExpenseFullYear!.Value).Should().Be(w.EstimatedNetProfit);
        w.EstimatedNetProfit.Should().Be(200_000m);
        w.HalfEstimatedProfit.Should().Be(100_000m);                 // = estimate / 2
        w.TaxComputed.Should().Be(20_000m);                          // 200k × 20% × ½
        w.WhtH1.Should().Be(5_000m);
        w.NetPayable.Should().Be(15_000m);                           // 20,000 − 5,000 (foots, positive)
        (w.TaxComputed - w.WhtH1).Should().Be(w.NetPayable);
        w.IsSme.Should().BeFalse();
    }

    [Fact] // override path: only the net estimate exists → 51/52 stay null, worksheet starts at 57-58
    public void Clean_override_path_leaves_revenue_expense_null_and_foots()
    {
        var w = Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: false,
            estimate: 1_000_000m, revenueFullYear: null, expenseFullYear: null,
            whtSufferedH1: 30_000m, schedule: General)!;

        w.RevenueFullYear.Should().BeNull();
        w.ExpenseFullYear.Should().BeNull();
        w.EstimatedNetProfit.Should().Be(1_000_000m);
        w.HalfEstimatedProfit.Should().Be(500_000m);
        w.TaxComputed.Should().Be(100_000m);                         // 1,000,000 × 20% × ½
        w.NetPayable.Should().Be(70_000m);                           // 100,000 − 30,000 (foots, positive)
        (w.TaxComputed - w.WhtH1).Should().Be(w.NetPayable);
    }

    // ── Task 5 wiring: Pnd51FormFiller.Fill must actually render the worksheet onto page 2 ──

    [Fact] // guards against the page-2 fill block being dropped (existing tests wouldn't catch it)
    public void Fill_with_worksheet_draws_more_on_page2_than_without()
    {
        var w = Pnd51FilingService.BuildWorksheet(
            true, Clean, isSme: false,
            estimate: 2_500_000m, revenueFullYear: 12_000_000m, expenseFullYear: 9_500_000m,
            whtSufferedH1: 30_000m, schedule: General)!;

        var with = Render(w);
        var without = Render(null);

        PageCount(with).Should().Be(2);
        Page2Len(with).Should().BeGreaterThan(Page2Len(without) + 50,
            "the attested worksheet must draw onto page 2");
    }

    private static byte[] Render(Pnd51Worksheet? w) => Pnd51FormFiller.Fill(new Pnd51Model(
        EmployerTaxId: "0105556789012", EmployerName: "บริษัท ทดสอบ จำกัด",
        PeriodStart: new DateOnly(2026, 1, 1), PeriodEnd: new DateOnly(2026, 12, 31),
        Building: null, RoomNo: null, Floor: null, Village: null,
        HouseNo: "1", Moo: null, Soi: null, Road: "ถนนทดสอบ",
        SubDistrict: null, District: null, Province: "กรุงเทพมหานคร", PostalCode: "10110",
        HalfYearTax: w?.NetPayable ?? 0m, FilingDate: new DateOnly(2026, 8, 31), Worksheet: w));

    private static int PageCount(byte[] pdf)
    { using var ms = new MemoryStream(pdf); return PdfReader.Open(ms, PdfDocumentOpenMode.Import).PageCount; }

    private static int Page2Len(byte[] pdf)
    {
        using var ms = new MemoryStream(pdf);
        var doc = PdfReader.Open(ms, PdfDocumentOpenMode.Modify);
        var pg = doc.Pages[1];
        var total = 0;
        for (var i = 0; i < pg.Contents.Elements.Count; i++)
            if (pg.Contents.Elements.GetObject(i) is PdfDictionary d && d.Stream != null) total += d.Stream.Value.Length;
        return total;
    }

    private static void ThrowsNotAttestable(Action act)
        => act.Should().Throw<DomainException>()
            .Where(e => e.Code == "pnd51.worksheet_not_attestable");
}

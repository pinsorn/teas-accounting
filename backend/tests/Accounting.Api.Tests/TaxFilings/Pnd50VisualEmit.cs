using Accounting.Application.Tax;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Tax;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Visual-gate emitter (not an assertion test): writes the worked-case ภ.ง.ด.50 PDFs for the
/// raster review (docs/RD-Forms/pnd50/fieldmap/render_check.py). Runs only when PND50_EMIT_DIR
/// is set, so the normal suite never touches the filesystem. v2 cases are built through the REAL
/// BuildSheet/BuildLadder so every printed figure foots (the raster reviewer re-adds the maths).
/// </summary>
public sealed class Pnd50VisualEmit
{
    private static readonly Pnd50Attestation Ok = new(FirstFiling: true, AcceptBlankSchedules: true);

    [SkippableFact]
    public void Emit_worked_cases_for_raster_review()
    {
        var dir = Environment.GetEnvironmentVariable("PND50_EMIT_DIR");
        Skip.If(string.IsNullOrEmpty(dir), "PND50_EMIT_DIR not set — visual-gate emit only.");
        Directory.CreateDirectory(dir!);

        // ── Profit case: books 5,000,000 − 4,900,000 = 100,000 · adjustments +50,000/−20,000 ·
        //    loss c/f 40,000 → taxable 90,000 → tax 18,000 − credits 15,003.25 → pay 2,996.75
        //    (+ ม.67ตรี 1,234.56). Distinct digits per box for cell-alignment reading.
        var citP = CitCalculator.Compute(100_000m, 30_000m, 40_000m, 10_002m, 5_001.25m,
            CitRateSchedule.General());
        var ladderP = Pnd50FilingService.BuildLadder(5_000_000m, 4_900_000m, 50_000m, -20_000m, citP);
        var sheetP  = Pnd50FilingService.BuildSheet(citP, 5_001.25m, 10_002m, 1_234.56m, false, Ok);
        var boxesP = new Pnd50BalanceSheetBoxes(
            CashAndEquivalents: 111_111.11m, TradeReceivables: 222_222.22m, Inventory: 0m,
            OtherCurrentAssets: 33_333.33m, OtherNonCurrentAssets: 444_444.44m,
            TotalAssets: 811_111.10m,
            TradePayables: 123_456.78m, OtherCurrentLiabilities: 56_789.01m,
            OtherNonCurrentLiabilities: 9_766.55m, TotalLiabilities: 190_012.34m,
            PaidUpShareCapital: 300_000m, OtherEquity: 0m, RetainedEarnings: 321_098.76m,
            TotalEquity: 621_098.76m, TotalLiabilitiesAndEquity: 811_111.10m);

        // ── Loss case: books 1,000,000 − 1,987,654.32 = −987,654.32 → ขาดทุนสุทธิ ·
        //    WHT 7,654.32 unrefunded → ชำระไว้เกิน · retained earnings negative (Group91 C2).
        var citL = CitCalculator.Compute(-987_654.32m, 0m, 0m, 0m, 7_654.32m,
            CitRateSchedule.General());
        var ladderL = Pnd50FilingService.BuildLadder(1_000_000m, 1_987_654.32m, 0m, 0m, citL);
        var sheetL  = Pnd50FilingService.BuildSheet(citL, 7_654.32m, 0m, 0m, false, Ok);
        var boxesL = boxesP with
        {
            RetainedEarnings = -321_098.76m, TotalEquity = -21_098.76m,
            TotalAssets = 168_913.58m, OtherNonCurrentAssets = -197_753.08m,
            TotalLiabilitiesAndEquity = 168_913.58m,
        };

        Pnd50Model M(Pnd50Sheet s, Pnd50Ladder l, Pnd50BalanceSheetBoxes b,
            bool relatedParty = false) =>
            Pnd50FormFillerTests.Model(s) with
            { Ladder = l, BalanceSheet = b, HasRelatedPartyOver200M = relatedParty };

        File.WriteAllBytes(Path.Combine(dir!, "pnd50_profit.pdf"),
            Pnd50FormFiller.Fill(M(sheetP, ladderP, boxesP)));
        File.WriteAllBytes(Path.Combine(dir!, "pnd50_loss.pdf"),
            Pnd50FormFiller.Fill(M(sheetL, ladderL, boxesL, relatedParty: true)));
        File.WriteAllBytes(Path.Combine(dir!, "pnd50_sme.pdf"),
            Pnd50FormFiller.Fill(M(
                Pnd50FilingService.BuildSheet(
                    CitCalculator.Compute(100_000m, 30_000m, 40_000m, 10_002m, 5_001.25m,
                        CitRateSchedule.Sme()),
                    5_001.25m, 10_002m, 0m, true, Ok),
                ladderP, boxesP)));
    }
}

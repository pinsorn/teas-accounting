using Accounting.Infrastructure.Pdf;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Visual-gate emitter (not an assertion test): writes the worked-case ภ.ง.ด.50 PDFs for the
/// raster review (docs/RD-Forms/pnd50/fieldmap/render_check.py). Runs only when PND50_EMIT_DIR
/// is set, so the normal suite never touches the filesystem.
/// </summary>
public sealed class Pnd50VisualEmit
{
    [SkippableFact]
    public void Emit_worked_cases_for_raster_review()
    {
        var dir = Environment.GetEnvironmentVariable("PND50_EMIT_DIR");
        Skip.If(string.IsNullOrEmpty(dir), "PND50_EMIT_DIR not set — visual-gate emit only.");
        Directory.CreateDirectory(dir!);

        // Distinct digits per box so cell-alignment errors are visible on the raster.
        var profit = new Pnd50Sheet(
            BaseAmount: 1_234_567.89m, IsLoss: false,
            TaxComputed: 246_913.58m, WhtCredit: 5_001.25m, Pnd51Prepaid: 10_002m,
            CreditsTotal: 15_003.25m, NetAmount: 231_910.33m, PayMore: true,
            Surcharge: 1_234.56m, TotalAmount: 233_144.89m, IsSme: false);
        var loss = new Pnd50Sheet(
            BaseAmount: 987_654.32m, IsLoss: true,
            TaxComputed: 0m, WhtCredit: 7_654.32m, Pnd51Prepaid: 0m,
            CreditsTotal: 7_654.32m, NetAmount: 7_654.32m, PayMore: false,
            Surcharge: 0m, TotalAmount: 7_654.32m, IsSme: false);
        var sme = profit with { IsSme = true };

        File.WriteAllBytes(Path.Combine(dir!, "pnd50_profit.pdf"),
            Pnd50FormFiller.Fill(Pnd50FormFillerTests.Model(profit)));
        File.WriteAllBytes(Path.Combine(dir!, "pnd50_loss.pdf"),
            Pnd50FormFiller.Fill(Pnd50FormFillerTests.Model(loss) with { HasRelatedPartyOver200M = true }));
        File.WriteAllBytes(Path.Combine(dir!, "pnd50_sme.pdf"),
            Pnd50FormFiller.Fill(Pnd50FormFillerTests.Model(sme)));
    }
}

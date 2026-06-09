namespace Accounting.Application.Tax;

/// <summary>
/// ภ.ง.ด.51 (ม.67ทวิ) — mid-year CIT prepayment PDF generator.
/// Method A only (estimated annual taxable profit × 50% rate − H1 WHT = amount due).
/// </summary>
public interface IPnd51FilingService
{
    /// <summary>
    /// Build the filled ภ.ง.ด.51 PDF for the given CE <paramref name="year"/>.
    /// <paramref name="estimatedAnnualProfit"/> is the taxpayer's own estimate of full-year
    /// taxable profit; if null, the service defaults to H1 net profit × 2 from P&amp;L.
    /// <paramref name="whtSufferedH1"/> is withholding tax already suffered in the first half;
    /// defaults to zero. <paramref name="isSme"/> selects SME 0/15/20% vs general 20% flat.
    /// <paramref name="fillWorksheet"/> additionally fills page 2 (Method-A computation worksheet);
    /// when set, <paramref name="attest"/> is REQUIRED and the service throws unless every flag holds
    /// (ภ.ง.ด.51 §4 — a blank amount box asserts zero, so page 2 is filled only for a clean case).
    /// </summary>
    Task<byte[]> BuildPnd51Async(
        int year,
        decimal? estimatedAnnualProfit,
        decimal whtSufferedH1,
        bool isSme,
        bool fillWorksheet,
        Pnd51Attestation? attest,
        CancellationToken ct);
}

/// <summary>
/// User attestation that the simple Method-A page-2 worksheet is valid for this filing. Page 2 is
/// filled ONLY when every flag holds (and the figures foot); otherwise the service throws
/// (ภ.ง.ด.51 §4 — omission on this form asserts zero, so we never emit an unverifiable worksheet).
/// </summary>
public sealed record Pnd51Attestation(
    bool FirstFiling,          // not ยื่นเพิ่มเติม (box 35 prior-paid = 0)
    bool NoLossCarryForward,   // box 55 = 0
    bool NoExemption,          // box 56 = 0
    bool NoRateReduction,      // box 34 = 0
    bool NoSurcharge);         // ม.27 / ม.67ตรี (box 38) = 0

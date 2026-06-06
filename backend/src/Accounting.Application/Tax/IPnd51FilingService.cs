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
    /// </summary>
    Task<byte[]> BuildPnd51Async(
        int year,
        decimal? estimatedAnnualProfit,
        decimal whtSufferedH1,
        bool isSme,
        CancellationToken ct);
}

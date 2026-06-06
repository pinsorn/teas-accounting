namespace Accounting.Domain.Tax;

/// <summary>
/// One CIT rate band: net taxable profit up to <see cref="UpTo"/> (THB, the band's upper edge) is
/// taxed at <see cref="Rate"/> (fraction, e.g. 0.15). The top band uses <see cref="decimal.MaxValue"/>.
/// Mirrors <c>PitBand</c> so the band-walk in <see cref="CitCalculator.TaxOnProfit"/> is identical.
/// </summary>
public sealed record CitBand(decimal UpTo, decimal Rate);

/// <summary>
/// The CIT rate schedule for a company class + tax year. This is SEEDED REFERENCE DATA carrying the
/// legal cite — a rate change is a code/seed edit (git-audited), NEVER a UI setting (CLAUDE.md §4.6).
/// Pass an instance into <see cref="CitCalculator"/> so the engine itself is rate-agnostic and fully
/// golden-testable, exactly like <c>PitSchedule</c> → <c>ThaiPitCalculator</c>.
///
/// SME-vs-general is purely a SCHEDULE-SELECTION decision: both classes are expressed as a
/// <see cref="CitRateSchedule"/>. The SME *predicate* (paid-up ≤5M ∧ revenue ≤30M) is NOT here — it
/// belongs in the filing service (it needs <c>Company.PaidUpCapital</c> + the P&L revenue figure),
/// which then picks <see cref="General"/> or <see cref="Sme"/>.
/// </summary>
public sealed record CitRateSchedule(IReadOnlyList<CitBand> Bands, string LegalRef)
{
    /// <summary>
    /// บริษัท/ห้างฯ ทั่วไป — 20% flat on net taxable profit (ม.65 + พรฎ.). A single band with no exempt
    /// floor: every baht of positive taxable profit is taxed at 20%.
    /// </summary>
    public static CitRateSchedule General() => new(
        Bands: [new(decimal.MaxValue, 0.20m)],
        LegalRef: "ม.65 / พรฎ. (CIT 20% flat)");

    /// <summary>
    /// SME (วิสาหกิจขนาดกลางและขนาดย่อม) progressive schedule — applies only when the company qualifies
    /// (paid-up capital ≤ ฿5M AND revenue ≤ ฿30M; that test is enforced by the caller, see class remarks):
    /// 0–300,000 = 0% · 300,000–3,000,000 = 15% · &gt;3,000,000 = 20%. Per the SME พรฎ. series.
    /// NOTE (v1 simplification): the statutory SME relief has further conditions/year qualifiers that a
    /// later version may model; v1 takes the headline 0/15/20 table once the caller has classified SME.
    /// </summary>
    public static CitRateSchedule Sme() => new(
        Bands:
        [
            new(300_000m,        0.00m),
            new(3_000_000m,      0.15m),
            new(decimal.MaxValue, 0.20m),
        ],
        LegalRef: "พรฎ. SME (CIT 0/15/20)");
}

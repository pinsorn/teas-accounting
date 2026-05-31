namespace Accounting.Domain.Payroll;

/// <summary>
/// One progressive PIT band: income up to <see cref="UpTo"/> (THB, the band's upper edge) is
/// taxed at <see cref="Rate"/> (fraction, e.g. 0.05). The top band uses <see cref="decimal.MaxValue"/>.
/// </summary>
public sealed record PitBand(decimal UpTo, decimal Rate);

/// <summary>
/// The PIT rate schedule + statutory expense rule for a tax year. This is SEEDED REFERENCE DATA
/// carrying the legal cite — a tax-year change is a code/seed edit (git-audited), never a UI
/// setting (CLAUDE.md §4.6). Pass an instance into <see cref="ThaiPitCalculator"/> so the engine
/// itself is rate-agnostic and fully unit-testable.
/// </summary>
public sealed record PitSchedule(
    IReadOnlyList<PitBand> Bands,
    decimal StandardExpenseRate,   // ม.42 ทวิ — 0.50 of ม.40(1)(2) income
    decimal StandardExpenseCap)    // ม.42 ทวิ — capped 100,000
{
    /// <summary>
    /// อัตราภาษีเงินได้บุคคลธรรมดา (ขั้นบันได) — current law, verified against the RD Pit_63
    /// infographic (rd.go.th). 0–150,000 exempt; then 5/10/15/20/25/30/35%. ม.42 ทวิ expense
    /// = 50% capped 100,000. Valid for the 2560+ tax years (unchanged through 2567/2568).
    /// </summary>
    public static PitSchedule Current() => new(
        Bands:
        [
            new(150_000m,    0.00m),
            new(300_000m,    0.05m),
            new(500_000m,    0.10m),
            new(750_000m,    0.15m),
            new(1_000_000m,  0.20m),
            new(2_000_000m,  0.25m),
            new(5_000_000m,  0.30m),
            new(decimal.MaxValue, 0.35m),
        ],
        StandardExpenseRate: 0.50m,
        StandardExpenseCap:  100_000m);
}

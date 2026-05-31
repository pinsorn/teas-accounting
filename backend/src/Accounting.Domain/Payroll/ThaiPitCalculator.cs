namespace Accounting.Domain.Payroll;

/// <summary>
/// Pure Thai personal-income-tax engine for employment income (ม.40(1)). No DB, no I/O — the
/// rate schedule is injected (see <see cref="PitSchedule"/>) so this is fully golden-testable.
/// Implements the ม.50(1) monthly-withholding method (projected-annual): each month, project the
/// full-year income, compute the annual tax, and spread the as-yet-unwithheld balance over the
/// remaining pay periods. All money is decimal; rounding is half-up to satang.
/// </summary>
public static class ThaiPitCalculator
{
    /// <summary>ม.42 ทวิ standard expense for employment income = min(income × rate, cap).</summary>
    public static decimal StandardExpense(decimal employmentIncome, PitSchedule s) =>
        Math.Min(Math.Max(employmentIncome, 0m) * s.StandardExpenseRate, s.StandardExpenseCap);

    /// <summary>Progressive tax on net income (เงินได้สุทธิ) — walk the bands cumulatively.</summary>
    public static decimal AnnualTax(decimal netIncome, PitSchedule s)
    {
        if (netIncome <= 0m) return 0m;
        decimal tax = 0m, lower = 0m;
        foreach (var band in s.Bands)
        {
            if (netIncome <= lower) break;
            var slice = Math.Min(netIncome, band.UpTo) - lower;
            tax += slice * band.Rate;
            lower = band.UpTo;
        }
        return decimal.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Annual net income = projected employment income − ม.42ทวิ expense − allowances.</summary>
    public static decimal AnnualNetIncome(
        decimal projectedAnnualIncome, decimal annualAllowances, PitSchedule s) =>
        Math.Max(0m,
            projectedAnnualIncome - StandardExpense(projectedAnnualIncome, s) - Math.Max(annualAllowances, 0m));

    /// <summary>
    /// ม.50(1) projected-annual full-year income from the running month:
    /// YTD income already paid + (this month's regular income × months remaining incl. this one).
    /// </summary>
    public static decimal ProjectAnnualIncome(
        decimal ytdIncome, decimal thisMonthRegularIncome, int monthsRemainingInclusive) =>
        ytdIncome + thisMonthRegularIncome * monthsRemainingInclusive;

    /// <summary>
    /// The month's PIT to withhold (ม.50(1)): (full-year tax − tax already withheld YTD) spread
    /// over the remaining pay periods. Never negative. <paramref name="monthsRemainingInclusive"/>
    /// counts this month (12 for a Jan start, 1 for December, fewer for a mid-year joiner).
    /// </summary>
    public static decimal MonthlyWithholding(
        decimal projectedAnnualIncome, decimal annualAllowances,
        decimal ytdPitWithheld, int monthsRemainingInclusive, PitSchedule s)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(monthsRemainingInclusive);
        var annualTax = AnnualTax(AnnualNetIncome(projectedAnnualIncome, annualAllowances, s), s);
        var remaining = annualTax - ytdPitWithheld;
        if (remaining <= 0m) return 0m;
        return decimal.Round(remaining / monthsRemainingInclusive, 2, MidpointRounding.AwayFromZero);
    }
}

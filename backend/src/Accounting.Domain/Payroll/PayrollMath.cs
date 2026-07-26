using Accounting.Domain.Enums;

namespace Accounting.Domain.Payroll;

/// <summary>
/// Statutory deduction amounts (ค่าลดหย่อน) for the v1 minimal allowance set — verified vs the
/// RD Pit_63 infographic: ผู้มีเงินได้ 60,000 · คู่สมรสไม่มีเงินได้ 60,000 · บุตร 30,000/คน.
/// SEEDED config (CLAUDE.md §4.6) — bound from <c>Payroll:Allowances</c>, passed in so the math
/// stays pure + golden-testable.
/// </summary>
public sealed record PayrollAllowanceRates(decimal Personal, decimal Spouse, decimal Child)
{
    public static PayrollAllowanceRates Default() => new(Personal: 60_000m, Spouse: 60_000m, Child: 30_000m);

    /// <summary>
    /// Annual ค่าลดหย่อน = personal + (spouse, only if married AND the spouse has NO income) +
    /// children×child + the SSO allowance (already capped by the caller, ม.47(1)(ช)).
    /// </summary>
    public decimal Annual(MaritalStatus marital, bool spouseHasIncome, int children, decimal ssoAllowance)
    {
        var spouse = marital == MaritalStatus.Married && !spouseHasIncome ? Spouse : 0m;
        var kids   = Math.Max(0, children) * Child;
        return Personal + spouse + kids + Math.Max(0m, ssoAllowance);
    }
}

/// <summary>Pure Social-Security (ม.33) contribution math. Rate/floor/ceiling are config-driven
/// (<c>Payroll:Sso</c>) so the 2569 ceiling change is a config edit, never code (§4.6).</summary>
public static class SsoContribution
{
    /// <summary>
    /// The employee (= employer) monthly contribution = round(clamp(salary, floor, ceiling) × rate, 2).
    /// Below the floor the base is the floor; above the ceiling it is capped (฿15,000 → ฿750 @ 5%).
    /// </summary>
    public static decimal Monthly(decimal monthlySalary, decimal rate, decimal floor, decimal ceiling)
    {
        if (monthlySalary <= 0m) return 0m;
        var contributoryWage = Math.Clamp(monthlySalary, floor, ceiling);
        return decimal.Round(contributoryWage * rate, 2, MidpointRounding.AwayFromZero);
    }
}

/// <summary>
/// Pure calendar-day salary proration (O8, Ham 2026-07-25): a partial month pays
/// BaseSalary × daysEmployed ÷ daysInMonth. PIT (ม.50(1)) and SSO (ม.33) are then computed from the
/// prorated gross by their existing callers — there is no separate proration of tax.
/// </summary>
public static class SalaryProration
{
    /// <summary>Calendar days the employee was employed inside the period, BOTH endpoints
    /// inclusive (a hire on the 1st or a termination on the last day = a full month). Returns
    /// &lt;= 0 only for a window that does not overlap the period at all — the caller's
    /// eligibility filter already excludes those.</summary>
    public static int DaysEmployed(
        DateOnly periodStart, DateOnly periodEnd, DateOnly hireDate, DateOnly? terminationDate)
    {
        var start = hireDate > periodStart ? hireDate : periodStart;
        var end   = terminationDate is { } t && t < periodEnd ? t : periodEnd;
        return end.DayNumber - start.DayNumber + 1;
    }

    /// <summary>
    /// The month's taxable gross. A FULL month returns <paramref name="baseSalary"/> untouched
    /// (no multiply/divide, no rounding — keeps a full-month payslip bit-identical to pre-O8);
    /// a partial month is rounded to satang half-up, the repo money convention.
    /// </summary>
    public static decimal MonthlyGross(decimal baseSalary, int daysEmployed, int daysInMonth)
    {
        if (baseSalary <= 0m || daysEmployed <= 0) return 0m;
        if (daysEmployed >= daysInMonth) return baseSalary;
        // multiply FIRST — `daysEmployed / daysInMonth` alone is integer division (= 0).
        return decimal.Round(baseSalary * daysEmployed / daysInMonth, 2, MidpointRounding.AwayFromZero);
    }
}

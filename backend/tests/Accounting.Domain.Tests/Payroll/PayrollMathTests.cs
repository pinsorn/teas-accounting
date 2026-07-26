using Accounting.Domain.Enums;
using Accounting.Domain.Payroll;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests.Payroll;

/// <summary>Golden tests for the pure payroll math: ค่าลดหย่อน (RD Pit_63 minimal set) + SSO ม.33.</summary>
public class PayrollMathTests
{
    private static readonly PayrollAllowanceRates R = PayrollAllowanceRates.Default();

    [Fact]
    public void Single_employee_allowance_is_personal_plus_sso()
        => R.Annual(MaritalStatus.Single, spouseHasIncome: false, children: 0, ssoAllowance: 9_000m)
            .Should().Be(69_000m);

    [Fact]
    public void Married_no_spouse_income_two_children_gets_spouse_and_child_allowances()
        // 60k personal + 60k spouse + 2×30k children + 9k SSO
        => R.Annual(MaritalStatus.Married, spouseHasIncome: false, children: 2, ssoAllowance: 9_000m)
            .Should().Be(189_000m);

    [Fact]
    public void Married_spouse_has_income_drops_the_spouse_allowance()
        // spouse with income → no spouse allowance: 60k + 2×30k + 9k
        => R.Annual(MaritalStatus.Married, spouseHasIncome: true, children: 2, ssoAllowance: 9_000m)
            .Should().Be(129_000m);

    [Theory]
    [InlineData(50000, 750)]     // above ceiling 15,000 → capped at 750
    [InlineData(15000, 750)]     // exactly at the ceiling
    [InlineData(10000, 500)]     // 10,000 × 5%
    [InlineData(1000, 82.5)]     // below the floor 1,650 → base = floor → 82.50
    [InlineData(0, 0)]           // no salary → no contribution
    public void Sso_monthly_clamps_to_floor_and_ceiling(double salary, double expected)
        => SsoContribution.Monthly((decimal)salary, rate: 0.05m, floor: 1_650m, ceiling: 15_000m)
            .Should().Be((decimal)expected);
}

/// <summary>O8 golden tests — calendar-day salary proration. Fixed dates only, no DB, no random years.</summary>
public class SalaryProrationTests
{
    private static readonly DateOnly JulStart = new(2026, 7, 1);
    private static readonly DateOnly JulEnd   = new(2026, 7, 31);

    // ---- A. DaysEmployed (period 2026-07-01..07-31 unless stated) ----

    [Fact] // A1
    public void Hire_long_before_period_no_term_is_full_month()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), null).Should().Be(31);

    [Fact] // A2
    public void Hire_on_period_start_no_term_is_full_month()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2026, 7, 1), null).Should().Be(31);

    [Fact] // A3
    public void Term_on_period_end_is_full_month()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 31)).Should().Be(31);

    [Fact] // A4
    public void Term_after_period_end_is_full_month()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), new DateOnly(2026, 8, 15)).Should().Be(31);

    [Fact] // A5
    public void Mid_month_hire_counts_hire_date_inclusive()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2026, 7, 15), null).Should().Be(17);

    [Fact] // A6
    public void Mid_month_termination_counts_termination_date_inclusive()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 10)).Should().Be(10);

    [Fact] // A7
    public void Hire_and_terminate_same_month()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 20)).Should().Be(11);

    [Fact] // A8
    public void Hire_on_last_day_of_month_is_one_day()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2026, 7, 31), null).Should().Be(1);

    [Fact] // A9
    public void Termination_in_past_month_yields_non_positive()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), new DateOnly(2026, 6, 30)).Should().BeLessThanOrEqualTo(0);

    [Fact] // Codex Tier-2 nit: termination on the period's FIRST day (hire before the period) — exactly 1 day
    public void Termination_on_first_day_of_period_is_one_day()
        => SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2020, 1, 1), new DateOnly(2026, 7, 1)).Should().Be(1);

    [Fact] // Codex Tier-2 nit: hire AND termination on the SAME day — exactly 1 day; money pinned too
    public void Hire_and_termination_on_same_day_is_one_day_and_prorates_correctly()
    {
        SalaryProration.DaysEmployed(JulStart, JulEnd, new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 20)).Should().Be(1);
        SalaryProration.MonthlyGross(31_000m, 1, 31).Should().Be(1_000.00m);   // 31,000 ÷ 31 = exactly 1,000
    }

    [Fact] // A10 — Feb 2026 (28d)
    public void Feb_28_day_month_mid_hire()
        => SalaryProration.DaysEmployed(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 15), null).Should().Be(14);

    [Fact] // A11 — Feb 2028 (29d, leap)
    public void Feb_leap_29_day_month_mid_hire()
        => SalaryProration.DaysEmployed(new DateOnly(2028, 2, 1), new DateOnly(2028, 2, 29), new DateOnly(2028, 2, 15), null).Should().Be(15);

    [Fact] // A12 — Apr 2026 (30d)
    public void Apr_30_day_month_mid_hire()
        => SalaryProration.DaysEmployed(new DateOnly(2026, 4, 1), new DateOnly(2026, 4, 30), new DateOnly(2026, 4, 16), null).Should().Be(15);

    // ---- A. MonthlyGross ----

    [Fact] // A13
    public void Full_month_short_circuits_to_base_salary_exactly()
        => SalaryProration.MonthlyGross(60_000m, 31, 31).Should().Be(60_000m);

    [Fact] // A14 — 4dp salary must NOT be rounded on the full-month path
    public void Full_month_preserves_four_decimal_salary_bit_identical()
        => SalaryProration.MonthlyGross(12_345.6789m, 30, 30).Should().Be(12_345.6789m);

    [Fact] // A15 — army hand-calc
    public void Prorated_17_of_31_days_on_60000()
        => SalaryProration.MonthlyGross(60_000m, 17, 31).Should().Be(32_903.23m);

    [Fact] // A16 — army hand-calc
    public void Prorated_10_of_31_days_on_60000()
        => SalaryProration.MonthlyGross(60_000m, 10, 31).Should().Be(19_354.84m);

    [Fact] // A17 — hire+leave same month
    public void Prorated_11_of_31_days_on_60000()
        => SalaryProration.MonthlyGross(60_000m, 11, 31).Should().Be(21_290.32m);

    [Fact] // A18 — drives the SSO change (A23 equivalent B6)
    public void Prorated_17_of_31_days_on_20000()
        => SalaryProration.MonthlyGross(20_000m, 17, 31).Should().Be(10_967.74m);

    [Fact] // A19 — floor case (Q3)
    public void Prorated_1_of_31_days_on_3000()
        => SalaryProration.MonthlyGross(3_000m, 1, 31).Should().Be(96.77m);

    [Fact] // A20 — 28-day month, exact
    public void Prorated_14_of_28_days_on_28000_is_exact()
        => SalaryProration.MonthlyGross(28_000m, 14, 28).Should().Be(14_000.00m);

    [Fact] // A21 — 29-day month, exact
    public void Prorated_15_of_29_days_on_29000_is_exact()
        => SalaryProration.MonthlyGross(29_000m, 15, 29).Should().Be(15_000.00m);

    [Fact] // A22 — 30-day month, exact
    public void Prorated_15_of_30_days_on_30000_is_exact()
        => SalaryProration.MonthlyGross(30_000m, 15, 30).Should().Be(15_000.00m);

    [Fact] // A23 — exact .005 midpoint → half-up (AwayFromZero) is pinned
    public void Midpoint_rounds_half_up()
        => SalaryProration.MonthlyGross(10_000.03m, 15, 30).Should().Be(5_000.02m);

    [Theory] // A24 — no-overlap / zero-salary guards
    [InlineData(60_000, 0, 31)]
    [InlineData(60_000, -5, 31)]
    [InlineData(0, 17, 31)]
    public void No_overlap_or_zero_salary_yields_zero(double salary, int days, int daysInMonth)
        => SalaryProration.MonthlyGross((decimal)salary, days, daysInMonth).Should().Be(0m);

    [Fact] // A25 — defensive `>=`, days beyond the month clip still short-circuits to base salary
    public void Days_beyond_month_length_still_short_circuits()
        => SalaryProration.MonthlyGross(60_000m, 35, 31).Should().Be(60_000m);

    // ---- SSO clamp composed on the prorated wage ----

    [Fact] // A26 — ceiling still binds after proration
    public void Sso_ceiling_still_binds_after_proration()
        => SsoContribution.Monthly(32_903.23m, 0.05m, 1_650m, 17_500m).Should().Be(875.00m);

    [Fact] // A27 — ceiling stops binding — the number that MOVES
    public void Sso_ceiling_stops_binding_after_proration()
        => SsoContribution.Monthly(9_677.42m, 0.05m, 1_650m, 17_500m).Should().Be(483.87m);

    [Fact] // A28 — matches integration B6 (test-config ceiling 15,000)
    public void Sso_under_test_ceiling_after_proration()
        => SsoContribution.Monthly(10_967.74m, 0.05m, 1_650m, 15_000m).Should().Be(548.39m);

    [Fact] // A29 — ฿1,650 floor binds (Q3 documented)
    public void Sso_floor_binds_on_tiny_prorated_wage()
        => SsoContribution.Monthly(96.77m, 0.05m, 1_650m, 17_500m).Should().Be(82.50m);
}

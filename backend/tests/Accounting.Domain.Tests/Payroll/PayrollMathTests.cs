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

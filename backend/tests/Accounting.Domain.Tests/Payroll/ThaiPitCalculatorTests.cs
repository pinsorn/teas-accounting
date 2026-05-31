using Accounting.Domain.Payroll;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests.Payroll;

/// <summary>
/// Golden tests for the pure Thai PIT engine. Figures cross-checked against the RD progressive
/// schedule (Pit_63, rd.go.th): 0–150k exempt, then 5/10/15/20/25/30/35%; ม.42ทวิ expense 50%/cap 100k.
/// </summary>
public class ThaiPitCalculatorTests
{
    private static readonly PitSchedule S = PitSchedule.Current();

    [Theory]
    [InlineData(0, 0)]
    [InlineData(150000, 0)]            // top of the exempt band
    [InlineData(300000, 7500)]         // 150k @ 5%
    [InlineData(500000, 27500)]        // + 200k @ 10% = 27,500 (the textbook figure)
    [InlineData(1000000, 115000)]      // 7500 + 20000 + 37500 + 50000
    [InlineData(2000000, 365000)]      // + 1,000,000 @ 25% = 250,000 over the 1M figure
    public void AnnualTax_walks_the_progressive_bands(double net, double expected)
        => ThaiPitCalculator.AnnualTax((decimal)net, S).Should().Be((decimal)expected);

    [Theory]
    [InlineData(100000, 50000)]        // 50%
    [InlineData(600000, 100000)]       // capped at 100k
    public void StandardExpense_is_50pct_capped_100k(double income, double expected)
        => ThaiPitCalculator.StandardExpense((decimal)income, S).Should().Be((decimal)expected);

    [Fact]
    public void Monthly_withholding_spreads_projected_annual_tax_ma50_1()
    {
        // Salary 50,000/mo, January (12 months remaining), no YTD, allowances = personal 60,000
        // + SSO 9,000 (750×12, capped). Projected 600,000 − exp 100,000 − allow 69,000 = net 431,000.
        // Annual tax = 7,500 + (431,000−300,000)×10% = 20,600 → /12 = 1,716.67.
        var projected = ThaiPitCalculator.ProjectAnnualIncome(0m, 50_000m, 12);
        projected.Should().Be(600_000m);

        var monthly = ThaiPitCalculator.MonthlyWithholding(
            projectedAnnualIncome: projected, annualAllowances: 69_000m,
            ytdPitWithheld: 0m, monthsRemainingInclusive: 12, S);
        monthly.Should().Be(1716.67m);
    }

    [Fact]
    public void Low_earner_below_threshold_withholds_nothing()
    {
        // 20,000/mo → projected 240,000 − exp 100,000 − allow 69,000 = net 71,000 (< 150k) → tax 0.
        var projected = ThaiPitCalculator.ProjectAnnualIncome(0m, 20_000m, 12);
        ThaiPitCalculator.MonthlyWithholding(projected, 69_000m, 0m, 12, S).Should().Be(0m);
    }

    [Fact]
    public void Already_fully_withheld_returns_zero_not_negative()
    {
        // December (1 month left): annual tax 20,600 already withheld in full → 0, never negative.
        ThaiPitCalculator.MonthlyWithholding(600_000m, 69_000m, 20_600m, 1, S).Should().Be(0m);
    }

    [Fact]
    public void Mid_year_joiner_uses_fewer_remaining_months()
    {
        // Joins in July: 6 months remaining, projected = 50,000 × 6 = 300,000.
        // net = 300,000 − exp 100,000 − allow 69,000 = 131,000 (< 150k) → annual tax 0 → withhold 0.
        var projected = ThaiPitCalculator.ProjectAnnualIncome(0m, 50_000m, 6);
        projected.Should().Be(300_000m);
        ThaiPitCalculator.MonthlyWithholding(projected, 69_000m, 0m, 6, S).Should().Be(0m);
    }
}

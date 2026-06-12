using Accounting.Domain.Tax;
using FluentAssertions;

namespace Accounting.Domain.Tests.Tax;

// 2026-06-12 wht-grossup spec — RD gross-up when the payer bears the tax
// (ผู้จ่ายออกภาษีให้): the absorbed tax is the payee's assessable income.
public class WhtPayerModesTests
{
    [Fact]
    public void Deduct_is_plain_rate_times_net()
    {
        var (wht, income) = WhtPayerModes.Compute(10_000m, 0.03m, WhtPayerModes.Deduct);
        wht.Should().Be(300m);
        income.Should().Be(10_000m);
    }

    [Fact]
    public void GrossUpForever_3pct_uses_r_over_one_minus_r()
    {
        // income = 10,000/0.97 = 10,309.28 ; tax = 3% = 309.28 (effective 3.0928%)
        var (wht, income) = WhtPayerModes.Compute(10_000m, 0.03m, WhtPayerModes.GrossUpForever);
        income.Should().Be(10_309.28m);
        wht.Should().Be(309.28m);
        (income - wht).Should().BeApproximately(10_000m, 0.01m); // payee nets the contract price
    }

    [Fact]
    public void GrossUpForever_15pct_foreign_service()
    {
        // income = 100,000/0.85 = 117,647.06 ; tax = 17,647.06 (effective 17.6471%)
        var (wht, income) = WhtPayerModes.Compute(100_000m, 0.15m, WhtPayerModes.GrossUpForever);
        income.Should().Be(117_647.06m);
        wht.Should().Be(17_647.06m);
    }

    [Fact]
    public void GrossUpOnce_adds_the_first_tax_to_income_exactly_once()
    {
        // income = 10,000·1.03 = 10,300 ; tax = 3% = 309 (effective 3.09%)
        var (wht, income) = WhtPayerModes.Compute(10_000m, 0.03m, WhtPayerModes.GrossUpOnce);
        income.Should().Be(10_300m);
        wht.Should().Be(309m);
    }

    [Fact]
    public void Zero_rate_lines_are_untouched_in_every_mode()
    {
        foreach (var mode in WhtPayerModes.All)
        {
            var (wht, income) = WhtPayerModes.Compute(5_000m, 0m, mode);
            wht.Should().Be(0m);
            income.Should().Be(5_000m);
        }
    }

    [Theory]
    [InlineData(WhtPayerModes.Deduct, 1, false)]
    [InlineData(WhtPayerModes.GrossUpForever, 2, true)]
    [InlineData(WhtPayerModes.GrossUpOnce, 3, true)]
    public void Condition_and_selfwithhold_map_per_50tawi_box(string mode, int condition, bool self)
    {
        WhtPayerModes.Condition(mode).Should().Be(condition);
        WhtPayerModes.IsSelfWithhold(mode).Should().Be(self);
        WhtPayerModes.IsValid(mode).Should().BeTrue();
    }

    [Fact]
    public void Unknown_mode_is_invalid()
    {
        WhtPayerModes.IsValid("GROSS_UP").Should().BeFalse();
        WhtPayerModes.IsValid(null).Should().BeFalse();
    }
}

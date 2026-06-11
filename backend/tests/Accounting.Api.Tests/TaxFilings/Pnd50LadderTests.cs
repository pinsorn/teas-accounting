using Accounting.Domain.Common;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// ภ.ง.ด.50 v2 — pure p3 รายการที่ 2 ladder builder (spec pnd50-v2-dashboard.md §3, ม.65ทวิ/ตรี).
/// Boxes on the form are unsigned (sign lives only in the row-3/9/21 radios), so a chain whose
/// running total flips sign mid-ladder is REFUSED (<c>pnd50.ladder_sign_flip</c>) — never printed
/// dishonestly. Inputs always come from <see cref="CitCalculator.Compute"/>.
/// </summary>
public sealed class Pnd50LadderTests
{
    private static readonly CitRateSchedule General = CitRateSchedule.General();

    private static CitComputation Cit(
        decimal profit, decimal adjustments = 0m, decimal lossCarryIn = 0m) =>
        CitCalculator.Compute(profit, adjustments, lossCarryIn, 0m, 0m, General);

    [Fact]
    public void Zero_adjustments_prints_pass_through()
    {
        var cit = Cit(100_000m);
        var l = Pnd50FilingService.BuildLadder(500_000m, 400_000m, 0m, 0m, cit);

        l.DirectRevenue.Should().Be(500_000m);
        l.CostOfSales.Should().Be(0m);
        l.GrossProfit.Should().Be(500_000m);
        l.OtherIncome.Should().Be(0m);
        l.Total5.Should().Be(500_000m);
        l.OtherExpenses.Should().Be(0m);
        l.Total7.Should().Be(500_000m);
        l.SellingAdminExpenses.Should().Be(400_000m);
        l.AccountingNetProfit.Should().Be(100_000m);
        l.DisallowedExpenses.Should().Be(0m);
        l.ExemptDeductions.Should().Be(0m);
        l.Total12.Should().Be(100_000m);
        l.Total14.Should().Be(cit.TaxableBeforeLoss);
        l.LossCarryForward.Should().Be(0m);
        l.Total16.Should().Be(100_000m);
        l.Total20.Should().Be(100_000m);
        l.TaxableNetProfit.Should().Be(cit.TaxableProfit);
    }

    [Fact]
    public void Signed_adjustments_route_to_rows_11_and_13()
    {
        var cit = Cit(100_000m, adjustments: 30_000m); // +50k −20k
        var l = Pnd50FilingService.BuildLadder(500_000m, 400_000m, 50_000m, -20_000m, cit);

        l.DisallowedExpenses.Should().Be(50_000m);   // row 11 (73)
        l.ExemptDeductions.Should().Be(20_000m);     // row 13 (74) — printed abs
        l.Total12.Should().Be(150_000m);
        l.Total14.Should().Be(130_000m);
        l.Total14.Should().Be(cit.TaxableBeforeLoss);
        l.TaxableNetProfit.Should().Be(cit.TaxableProfit);
    }

    [Fact]
    public void Loss_carry_forward_applies_on_row_15()
    {
        var cit = Cit(100_000m, lossCarryIn: 40_000m);
        var l = Pnd50FilingService.BuildLadder(500_000m, 400_000m, 0m, 0m, cit);

        l.LossCarryForward.Should().Be(40_000m);     // row 15 (75) = LossApplied
        l.Total16.Should().Be(60_000m);
        l.Total20.Should().Be(60_000m);
        l.TaxableNetProfit.Should().Be(cit.TaxableProfit);
    }

    [Fact]
    public void Loss_year_keeps_negative_chain_and_skips_loss_cf()
    {
        var cit = Cit(-80_000m, lossCarryIn: 99_000m); // base < 0 → LossApplied 0
        var l = Pnd50FilingService.BuildLadder(300_000m, 380_000m, 0m, 0m, cit);

        l.AccountingNetProfit.Should().Be(-80_000m);
        l.Total12.Should().Be(-80_000m);
        l.Total14.Should().Be(-80_000m);
        l.LossCarryForward.Should().Be(0m);
        l.Total16.Should().Be(-80_000m);
        l.TaxableNetProfit.Should().Be(-80_000m);    // → Group9 Choice2 ขาดทุนสุทธิ
    }

    [Fact]
    public void Sign_flip_mid_chain_is_refused()
    {
        // Profit books + a deduction larger than the profit → s14 < 0 while s9 ≥ 0:
        // row 14 has no sign radio, the form cannot honestly render it.
        var cit = Cit(100_000m, adjustments: -150_000m);
        var act = () => Pnd50FilingService.BuildLadder(500_000m, 400_000m, 0m, -150_000m, cit);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.ladder_sign_flip");
    }

    [Fact]
    public void Loss_books_flipped_positive_by_additions_is_refused()
    {
        var cit = Cit(-50_000m, adjustments: 120_000m);
        var act = () => Pnd50FilingService.BuildLadder(300_000m, 350_000m, 120_000m, 0m, cit);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.ladder_sign_flip");
    }

    [Fact]
    public void Pl_mismatch_with_cit_is_a_caller_bug()
    {
        var cit = Cit(100_000m);
        var act = () => Pnd50FilingService.BuildLadder(500_000m, 350_000m, 0m, 0m, cit); // 150k ≠ 100k
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Adjustment_split_mismatch_is_a_caller_bug()
    {
        var cit = Cit(100_000m, adjustments: 30_000m);
        var act = () => Pnd50FilingService.BuildLadder(500_000m, 400_000m, 10_000m, 0m, cit);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Pre_split_sums_must_carry_their_sign()
    {
        var cit = Cit(100_000m);
        var act = () => Pnd50FilingService.BuildLadder(500_000m, 400_000m, -1m, 0m, cit);
        act.Should().Throw<InvalidOperationException>();
    }
}

using Accounting.Domain.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests.Tax;

/// <summary>
/// Golden tests for the pure Thai CIT engine. Rates cross-checked against the RD CIT schedule
/// (docs/RD-Forms/pnd50/_meta.md): general 20% flat (ม.65); SME 0/15/20 (พรฎ.) with breaks at 300k
/// and 3M. The computation ORDER (ม.65ตรี loss → rate → ม.67ทวิ credits) is the real correctness risk,
/// so it is pinned explicitly. Arithmetic here proves code ≡ intent; a worked-example anchor from
/// pnd50_instructions.pdf is added at the form-fill stage to prove code ≡ law.
/// </summary>
public class CitCalculatorTests
{
    private static readonly CitRateSchedule General = CitRateSchedule.General();
    private static readonly CitRateSchedule Sme = CitRateSchedule.Sme();

    // ── ม.65: general 20% flat ────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1_000_000, 200_000)]     // 20% flat, no exempt floor
    [InlineData(12_345_678, 2_469_135.60)]
    public void General_is_20pct_flat(double profit, double expected)
        => CitCalculator.TaxOnProfit((decimal)profit, General).Should().Be((decimal)expected);

    [Fact]
    public void Negative_or_zero_profit_pays_no_tax()
        => CitCalculator.TaxOnProfit(-500_000m, General).Should().Be(0m);

    // ── พรฎ. SME: 0/15/20 progressive ─────────────────────────────────────────────────────────
    [Theory]
    [InlineData(300_000, 0)]             // all inside the 0% band
    [InlineData(1_000_000, 105_000)]     // (1,000,000−300,000) × 15%
    [InlineData(3_000_000, 405_000)]     // 2,700,000 × 15%
    [InlineData(5_000_000, 805_000)]     // 405,000 + (5,000,000−3,000,000) × 20%
    public void Sme_walks_the_0_15_20_bands(double profit, double expected)
        => CitCalculator.TaxOnProfit((decimal)profit, Sme).Should().Be((decimal)expected);

    // ── ม.65/65ตรี/67ทวิ: the full ladder + the ORDER (loss reduces base, credits reduce tax) ──
    [Fact]
    public void Compute_ladder_applies_loss_to_base_then_credits_to_tax()
    {
        // SME. accounting 4,000,000 + adjustments(add-backs) 500,000 = base 4,500,000.
        // loss c/f 1,000,000 → taxable 3,500,000. SME tax = 405,000 + 500,000×20% = 505,000.
        // credits = ภ.ง.ด.51 prepay 200,000 + WHT 50,000 = 250,000 → payable 255,000.
        var c = CitCalculator.Compute(
            accountingProfit: 4_000_000m, adjustmentsTotal: 500_000m, lossCarryIn: 1_000_000m,
            pnd51Prepaid: 200_000m, whtSuffered: 50_000m, Sme);

        c.TaxableBeforeLoss.Should().Be(4_500_000m);
        c.LossApplied.Should().Be(1_000_000m);
        c.TaxableProfit.Should().Be(3_500_000m);
        c.TaxBeforeCredits.Should().Be(505_000m);
        c.CreditsTotal.Should().Be(250_000m);
        c.CitPayable.Should().Be(255_000m);
        c.RefundDue.Should().Be(0m);
    }

    [Fact]
    public void Loss_is_capped_at_the_base_so_the_remainder_can_roll_forward()
    {
        // ม.65ตรี: base 100,000, loss c/f 500,000. Only 100,000 is usable; the 400,000 remainder
        // is the caller's to carry forward (5-yr). LossApplied MUST be 100,000, not 500,000.
        var c = CitCalculator.Compute(
            accountingProfit: 100_000m, adjustmentsTotal: 0m, lossCarryIn: 500_000m,
            pnd51Prepaid: 0m, whtSuffered: 0m, General);

        c.LossApplied.Should().Be(100_000m);
        c.TaxableProfit.Should().Be(0m);
        c.TaxBeforeCredits.Should().Be(0m);
        c.CitPayable.Should().Be(0m);
    }

    [Fact]
    public void Credits_exceeding_tax_yield_a_refund_not_a_negative_payable()
    {
        // tax 200,000 (1M @ 20%), credits 250,000 → payable floors at 0, refund 50,000 derivable.
        var c = CitCalculator.Compute(
            accountingProfit: 1_000_000m, adjustmentsTotal: 0m, lossCarryIn: 0m,
            pnd51Prepaid: 250_000m, whtSuffered: 0m, General);

        c.TaxBeforeCredits.Should().Be(200_000m);
        c.CitPayable.Should().Be(0m);
        c.RefundDue.Should().Be(50_000m);
    }

    [Fact]
    public void Negative_adjustments_reduce_the_base()
    {
        // Exempt income of 300,000 nets the base down: 1,000,000 − 300,000 = 700,000 @ 20% = 140,000.
        var c = CitCalculator.Compute(
            accountingProfit: 1_000_000m, adjustmentsTotal: -300_000m, lossCarryIn: 0m,
            pnd51Prepaid: 0m, whtSuffered: 0m, General);

        c.TaxableProfit.Should().Be(700_000m);
        c.TaxBeforeCredits.Should().Be(140_000m);
    }

    // ── ม.67ทวิ: ภ.ง.ด.51 half-year prepayment (method A) ──────────────────────────────────────
    [Fact]
    public void HalfYearPrepayment_is_half_the_tax_on_the_estimate_less_h1_wht()
    {
        // Estimated full-year taxable 1,000,000 (general) → tax 200,000 × 50% = 100,000 − WHT 20,000.
        CitCalculator.HalfYearPrepayment(1_000_000m, 20_000m, General).Should().Be(80_000m);
    }

    [Fact]
    public void HalfYearPrepayment_never_goes_negative()
        => CitCalculator.HalfYearPrepayment(1_000_000m, 250_000m, General).Should().Be(0m);

    // ── ม.67ตรี: under-estimate penalty (20% of shortfall when est < actual by >25%) ────────────
    [Fact]
    public void UnderEstimatePenalty_charges_20pct_of_the_shortfall_beyond_tolerance()
    {
        // Estimate 1,000,000 vs actual 2,000,000 → 50% short (>25%). Half-tax on actual = 400,000×50%
        // = 200,000; prepaid (= half-year on estimate) 100,000 → shortfall 100,000 → penalty 20,000.
        CitCalculator.UnderEstimatePenalty(1_000_000m, 2_000_000m, 100_000m, General)
            .Should().Be(20_000m);
    }

    [Fact]
    public void UnderEstimatePenalty_is_zero_within_the_25pct_tolerance()
    {
        // Estimate 1,800,000 vs actual 2,000,000 → 10% short (≤25%) → no penalty.
        CitCalculator.UnderEstimatePenalty(1_800_000m, 2_000_000m, 180_000m, General)
            .Should().Be(0m);
    }
}

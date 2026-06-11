using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// ภ.ง.ด.50 §4 — pure sheet builder + refuse-on-unrenderable guard. A blank box on the form
/// asserts zero; v2 renders the p3 ladder + p6 balance sheet, so only pages 4–5/7 + ใบแนบ remain
/// blank and need the attestation. Adjustments/loss years are now RENDERABLE (the v1 refusals are
/// gone — see Pnd50LadderTests for the ladder rules). Inputs always come from
/// <see cref="CitCalculator.Compute"/> — never a hand-rolled <see cref="CitComputation"/>.
/// </summary>
public sealed class Pnd50SheetTests
{
    private static readonly Pnd50Attestation Ok = new(FirstFiling: true, AcceptBlankSchedules: true);
    private static readonly CitRateSchedule General = CitRateSchedule.General();

    private static CitComputation Cit(
        decimal profit, decimal adjustments = 0m, decimal lossCarryIn = 0m,
        decimal prepaid = 0m, decimal wht = 0m) =>
        CitCalculator.Compute(profit, adjustments, lossCarryIn, prepaid, wht, General);

    [Fact]
    public void No_attestation_throws()
    {
        var act = () => Pnd50FilingService.BuildSheet(Cit(1_000_000m), 0m, 0m, 0m, false, null);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.not_attestable");
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void Partial_attestation_throws(bool first, bool acceptBlank)
    {
        var act = () => Pnd50FilingService.BuildSheet(
            Cit(1_000_000m), 0m, 0m, 0m, false, new Pnd50Attestation(first, acceptBlank));
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.not_attestable");
    }

    [Fact]
    public void Nonzero_adjustments_render_in_v2()
    {
        var cit = Cit(1_000_000m, adjustments: 1_000m);
        var s = Pnd50FilingService.BuildSheet(cit, 0m, 0m, 0m, false, Ok);
        s.BaseAmount.Should().Be(1_001_000m);   // box 48-49 = TaxableProfit incl. adjustments
        s.TaxComputed.Should().Be(cit.TaxBeforeCredits);
    }

    [Fact]
    public void Applied_loss_carry_forward_renders_in_v2()
    {
        var cit = Cit(1_000_000m, lossCarryIn: 500m);
        var s = Pnd50FilingService.BuildSheet(cit, 0m, 0m, 0m, false, Ok);
        s.BaseAmount.Should().Be(999_500m);     // base after ม.65ตรี(12) loss applied
    }

    [Fact]
    public void Clean_profit_pay_more_foots()
    {
        // profit 1,000,000 general 20% → tax 200,000; credits 5,000 WHT + 10,000 prepaid
        var cit = Cit(1_000_000m, prepaid: 10_000m, wht: 5_000m);
        var s = Pnd50FilingService.BuildSheet(cit, 5_000m, 10_000m, 1_234m, false, Ok);

        s.IsLoss.Should().BeFalse();
        s.BaseAmount.Should().Be(1_000_000m);
        s.TaxComputed.Should().Be(cit.TaxBeforeCredits);
        s.WhtCredit.Should().Be(5_000m);
        s.Pnd51Prepaid.Should().Be(10_000m);
        s.CreditsTotal.Should().Be(15_000m);
        s.NetAmount.Should().Be(cit.TaxBeforeCredits - 15_000m);
        s.PayMore.Should().BeTrue();
        s.Surcharge.Should().Be(1_234m);
        s.TotalAmount.Should().Be(s.NetAmount + 1_234m);
        // foot: 669 = 665 + 666 · 672 = 670 + 671
        (s.WhtCredit + s.Pnd51Prepaid).Should().Be(s.CreditsTotal);
        (s.NetAmount + s.Surcharge).Should().Be(s.TotalAmount);
    }

    [Fact]
    public void Clean_loss_year_renders_overpaid()
    {
        var cit = Cit(-200_000m, wht: 3_000m);
        var s = Pnd50FilingService.BuildSheet(cit, 3_000m, 0m, 0m, false, Ok);

        s.IsLoss.Should().BeTrue();
        s.BaseAmount.Should().Be(200_000m);     // |TaxableBeforeLoss| — the ขาดทุนสุทธิ base
        s.TaxComputed.Should().Be(0m);
        s.NetAmount.Should().Be(3_000m);        // credits unrefunded → ชำระไว้เกิน
        s.PayMore.Should().BeFalse();
        s.Surcharge.Should().Be(0m);
        s.TotalAmount.Should().Be(3_000m);
    }

    [Fact]
    public void Overpaid_with_surcharge_throws()
    {
        var act = () => Pnd50FilingService.BuildSheet(
            Cit(-200_000m, wht: 3_000m), 3_000m, 0m, surcharge: 100m, isSme: false, Ok);
        act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.not_attestable");
    }

    [Fact]
    public void Credit_component_mismatch_is_a_caller_bug()
    {
        var act = () => Pnd50FilingService.BuildSheet(
            Cit(1_000_000m, wht: 5_000m), 4_000m, 0m, 0m, false, Ok);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sme_flag_flows_through()
    {
        var cit = CitCalculator.Compute(1_000_000m, 0m, 0m, 0m, 0m, CitRateSchedule.Sme());
        Pnd50FilingService.BuildSheet(cit, 0m, 0m, 0m, isSme: true, Ok).IsSme.Should().BeTrue();
    }
}

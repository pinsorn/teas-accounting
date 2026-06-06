namespace Accounting.Domain.Tax;

/// <summary>
/// The full CIT computation ladder — ONE figure per ภ.ง.ด.50 line, not just the final payable.
/// The order is statutory and load-bearing (ม.65 → 65ตรี loss → rate → credits):
///   accounting profit (+/−) tax adjustments  = <see cref="TaxableBeforeLoss"/>
///   − loss carried forward (capped at the base) = <see cref="TaxableProfit"/>   ← loss reduces the BASE (pre-rate)
///   × rate schedule                               = <see cref="TaxBeforeCredits"/>
///   − credits (ภ.ง.ด.51 prepay + WHT suffered)    = <see cref="CitPayable"/>     ← credits reduce the TAX (post-rate)
///
/// <see cref="LossApplied"/> is exposed deliberately: it is min(lossCarryIn, taxableBeforeLoss), so when
/// the base floors at 0 the caller can roll the UNUSED remainder forward (ม.65ตรี, 5-year). A scalar
/// return would lose that. <see cref="CitPayable"/> floors at 0; a refund (credits &gt; tax) is derivable
/// by the caller as max(0, <see cref="CreditsTotal"/> − <see cref="TaxBeforeCredits"/>).
/// </summary>
public sealed record CitComputation(
    decimal AccountingProfit,
    decimal AdjustmentsTotal,
    decimal TaxableBeforeLoss,
    decimal LossApplied,
    decimal TaxableProfit,
    decimal TaxBeforeCredits,
    decimal CreditsTotal,
    decimal CitPayable)
{
    /// <summary>Refund due when credits exceed the tax (ภ.ง.ด.51 over-prepaid + WHT). Never negative.</summary>
    public decimal RefundDue => Math.Max(0m, CreditsTotal - TaxBeforeCredits);
}

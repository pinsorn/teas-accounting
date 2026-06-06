namespace Accounting.Domain.Tax;

/// <summary>
/// Pure Thai corporate-income-tax engine (ม.65 / 65ทวิ / 65ตรี / 67ทวิ). No DB, no I/O — the rate
/// schedule is injected (see <see cref="CitRateSchedule"/>) so the engine is rate-agnostic and fully
/// golden-testable, mirroring <c>ThaiPitCalculator</c>. The COMPUTATION ORDER in <see cref="Compute"/>
/// is statutory and load-bearing (loss reduces the base, credits reduce the tax) — see
/// <see cref="CitComputation"/>. All money is decimal; rounding is half-up to satang.
/// </summary>
public static class CitCalculator
{
    /// <summary>
    /// Tax on a net taxable profit — walk the rate bands cumulatively (identical mechanism to PIT's
    /// progressive walk). For the general schedule this is just profit × 20%; for SME it steps the
    /// 0/15/20 bands. Non-positive profit → 0.
    /// </summary>
    public static decimal TaxOnProfit(decimal taxableProfit, CitRateSchedule s)
    {
        if (taxableProfit <= 0m) return 0m;
        decimal tax = 0m, lower = 0m;
        foreach (var band in s.Bands)
        {
            if (taxableProfit <= lower) break;
            var slice = Math.Min(taxableProfit, band.UpTo) - lower;
            tax += slice * band.Rate;
            lower = band.UpTo;
        }
        return decimal.Round(tax, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The full ภ.ง.ด.50 CIT ladder. <paramref name="adjustmentsTotal"/> is SIGNED — ม.65ตรี add-backs
    /// (non-deductibles) are positive, exempt income / extra deductions are negative — and is layered
    /// onto the auto P&amp;L net accounting profit. The order is enforced here and pinned by tests:
    /// loss carry-forward reduces the BASE (capped at it, so the remainder rolls forward); ภ.ง.ด.51
    /// prepay + WHT suffered reduce the TAX. <see cref="CitComputation.CitPayable"/> floors at 0.
    /// </summary>
    public static CitComputation Compute(
        decimal accountingProfit,
        decimal adjustmentsTotal,
        decimal lossCarryIn,
        decimal pnd51Prepaid,
        decimal whtSuffered,
        CitRateSchedule s)
    {
        var taxableBeforeLoss = accountingProfit + adjustmentsTotal;
        // Loss is usable only up to the positive base; the unused part is the caller's to roll forward.
        var lossApplied = Math.Min(Math.Max(lossCarryIn, 0m), Math.Max(taxableBeforeLoss, 0m));
        var taxableProfit = Math.Max(0m, taxableBeforeLoss - lossApplied);
        var taxBeforeCredits = TaxOnProfit(taxableProfit, s);
        var creditsTotal = Math.Max(0m, pnd51Prepaid) + Math.Max(0m, whtSuffered);
        var citPayable = Math.Max(0m, taxBeforeCredits - creditsTotal);
        return new CitComputation(
            AccountingProfit: accountingProfit,
            AdjustmentsTotal: adjustmentsTotal,
            TaxableBeforeLoss: taxableBeforeLoss,
            LossApplied: lossApplied,
            TaxableProfit: taxableProfit,
            TaxBeforeCredits: taxBeforeCredits,
            CreditsTotal: creditsTotal,
            CitPayable: citPayable);
    }

    /// <summary>
    /// ภ.ง.ด.51 (ม.67ทวิ) method A: HALF the CIT computed on the company's ESTIMATED full-year taxable
    /// profit, less any WHT already suffered in the half-year. Never negative.
    /// </summary>
    public static decimal HalfYearPrepayment(
        decimal estimatedAnnualTaxableProfit, decimal whtSufferedH1, CitRateSchedule s)
    {
        var halfTax = decimal.Round(
            TaxOnProfit(estimatedAnnualTaxableProfit, s) * 0.50m, 2, MidpointRounding.AwayFromZero);
        return Math.Max(0m, halfTax - Math.Max(0m, whtSufferedH1));
    }

    /// <summary>
    /// ม.67ตรี under-estimate penalty: if the ภ.ง.ด.51 estimated full-year profit understates the actual
    /// by MORE than 25%, the penalty is 20% of the shortfall — (half the CIT on the actual profit) minus
    /// the amount already prepaid. Within the 25% tolerance, or when the actual is non-positive, returns 0.
    /// </summary>
    public static decimal UnderEstimatePenalty(
        decimal estimatedAnnualTaxableProfit, decimal actualAnnualTaxableProfit,
        decimal pnd51Prepaid, CitRateSchedule s)
    {
        if (actualAnnualTaxableProfit <= 0m) return 0m;
        var shortfallRatio = (actualAnnualTaxableProfit - estimatedAnnualTaxableProfit) / actualAnnualTaxableProfit;
        if (shortfallRatio <= 0.25m) return 0m;
        var halfTaxOnActual = decimal.Round(
            TaxOnProfit(actualAnnualTaxableProfit, s) * 0.50m, 2, MidpointRounding.AwayFromZero);
        var shortfall = Math.Max(0m, halfTaxOnActual - Math.Max(0m, pnd51Prepaid));
        return decimal.Round(shortfall * 0.20m, 2, MidpointRounding.AwayFromZero);
    }
}

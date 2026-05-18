namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Effective-dated rate for a TaxCode. A single TaxCode owns multiple rows
/// (so historical postings keep using the rate that applied at their tax point).
/// </summary>
public class TaxRate
{
    public long TaxRateId { get; set; }
    public int TaxCodeId { get; set; }
    public TaxCode? TaxCode { get; set; }

    public decimal Rate { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }

    public bool IsEffectiveOn(DateOnly date) =>
        date >= EffectiveFrom && (EffectiveTo is null || date <= EffectiveTo);
}

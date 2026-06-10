using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Phase C-C — per-fiscal-year CIT summary (locked decision #5): the loss-carry-forward store
/// (computed at year-end, override-able) + the ภ.ง.ด.51 estimate (ม.67ตรี under-estimate check).
/// FiscalYear = the CE year the FY STARTS in (matches Pnd51FilingService's `year`).
/// NetProfit figures are TAXABLE (accounting P&amp;L net + ม.65ทวิ/65ตรี adjustments), signed; loss &lt; 0.
/// </summary>
public class CitYearSummary : ITenantOwned, IAuditable
{
    public long CitYearSummaryId { get; set; }
    public int CompanyId { get; set; }
    public int FiscalYear { get; set; }

    /// <summary>Auto snapshot: P&amp;L FY net profit + Σ adjustments (POST …/compute).</summary>
    public decimal? ComputedNetProfit { get; set; }

    /// <summary>Manual override (locked decision #5) — wins over Computed when set.</summary>
    public decimal? OverrideNetProfit { get; set; }

    /// <summary>ภ.ง.ด.51 method-A full-year estimate as filed (ม.67ตรี penalty input).</summary>
    public decimal? Pnd51EstimatedProfit { get; set; }

    /// <summary>ภ.ง.ด.51 amount prepaid (ม.67ทวิ) — the ภ.ง.ด.50 credit line.</summary>
    public decimal? Pnd51Prepaid { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }

    public decimal? EffectiveNetProfit => OverrideNetProfit ?? ComputedNetProfit;
}

using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Phase C-C — one manual CIT tax-adjustment line (locked decision #1): ม.65ทวิ/65ตรี differences
/// between accounting and taxable profit. SIGNED: add-backs (non-deductibles) &gt; 0,
/// exempt income / extra deductions &lt; 0. Layered onto the auto P&amp;L net profit.
/// </summary>
public class CitAdjustment : ITenantOwned, IAuditable
{
    public long CitAdjustmentId { get; set; }
    public int CompanyId { get; set; }
    public int FiscalYear { get; set; }

    /// <summary>Legal reference, e.g. "ม.65ตรี(3)" / "ม.65ทวิ(2)".</summary>
    public required string LegalRefCode { get; set; }
    public required string Label { get; set; }
    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}

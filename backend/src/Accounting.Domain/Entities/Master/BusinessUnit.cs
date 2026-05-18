using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Master;

/// <summary>
/// A user-definable revenue stream / sub-business inside one legal entity
/// (e.g. ECOM, LAB, REPT). The first additional GL dimension actually wired
/// through (Sprint 8): snapshot onto TI/RC/CN/DN headers + every journal_line at
/// POST. Distinct from cost_centers / expense_categories. Flat list (no hierarchy
/// this sprint), revenue-side only.
/// </summary>
public class BusinessUnit : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public int BusinessUnitId { get; set; }
    public int CompanyId { get; set; }

    public required string Code { get; set; }      // 'ECOM' etc., unique per company
    public required string NameTh { get; set; }
    public string? NameEn { get; set; }

    /// <summary>Optional UX nicety — pre-fills the TI revenue line account.</summary>
    public long? DefaultRevenueAccountId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public long   Version   { get; set; }
}

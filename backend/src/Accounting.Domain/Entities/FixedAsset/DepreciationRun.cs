using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md §3) — one aggregate depreciation JE per company-month.
/// UNIQUE (company_id, period_year, period_month) is the double-post backstop (FA-E) — both the
/// idempotent early-return AND the concurrent-race backstop key off this constraint.
/// </summary>
public class DepreciationRun : ITenantOwned, IConcurrencyVersioned
{
    public long DepreciationRunId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public int PeriodYear  { get; set; }
    public int PeriodMonth { get; set; }
    /// <summary>Last day of (PeriodYear, PeriodMonth) — the JE DocDate.</summary>
    public DateOnly RunDate { get; set; }

    public DepreciationRunStatus Status { get; set; } = DepreciationRunStatus.Posted;
    public decimal TotalAmount { get; set; }
    public int     AssetCount  { get; set; }
    public long?   JournalEntryId { get; set; }

    public long Version { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }

    public ICollection<DepreciationRunLine> Lines { get; set; } = new List<DepreciationRunLine>();
}

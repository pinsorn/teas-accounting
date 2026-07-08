using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Ledger;

/// <summary>
/// Year-lock + audit source of truth (specs/year-end-closing.md D7). "FY N is closed" ⇔ an
/// ACTIVE (non-reversed) row exists — enforced by a filtered unique index on
/// (CompanyId, FiscalYear) WHERE reversed_at IS NULL, which allows one active close per year
/// while keeping reversed rows around for audit and permitting a clean re-close after reopen.
/// </summary>
public class FiscalYearClose : ITenantOwned
{
    public int FiscalYearCloseId { get; set; }   // identity PK
    public int CompanyId { get; set; }

    public int FiscalYear { get; set; }           // = start calendar year N
    public DateOnly FiscalStartDate { get; set; }
    public DateOnly FiscalEndDate { get; set; }

    public decimal NetProfit { get; set; }        // swept amount (Rev − Exp); for display
    public long? ClosingJournalId { get; set; }    // null iff zero activity (no JE posted)

    public DateTimeOffset ClosedAt { get; set; }
    public long? ClosedBy { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset? ReversedAt { get; set; }
    public long? ReversedBy { get; set; }
    public long? ReversingJournalId { get; set; }
}

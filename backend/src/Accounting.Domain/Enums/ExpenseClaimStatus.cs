namespace Accounting.Domain.Enums;

/// <summary>Cycle C (specs/expense-claims.md §4) — employee expense reimbursement lifecycle.
/// Draft/Rejected are both editable (Submit re-enters the same flow); Paid/Cancelled are
/// terminal.</summary>
public enum ExpenseClaimStatus
{
    Draft,
    Submitted,
    Approved,
    Paid,
    Rejected,
    Cancelled,
}

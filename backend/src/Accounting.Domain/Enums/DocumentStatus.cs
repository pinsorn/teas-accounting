namespace Accounting.Domain.Enums;

/// <summary>
/// Common workflow status for posting documents (Journal Voucher, Tax Invoice, …).
/// Transitions are one-way: DRAFT → POSTED → (VOIDED only via Credit Note flow, never edited).
/// Payment Voucher additionally uses DRAFT → APPROVED → POSTED (SoD, CLAUDE.md §12.1);
/// other documents skip Approved.
/// </summary>
public enum DocumentStatus
{
    Draft,
    Approved,
    Posted,
    Voided,
}

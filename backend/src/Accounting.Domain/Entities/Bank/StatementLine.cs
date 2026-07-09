using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md D1/B2-B4) — one row per statement
/// transaction. Match state + links live as columns here (one-to-one matching, D4) — no
/// separate join/reconciliation table. NO FK navigation to Receipt/PaymentVoucher/JournalEntry
/// (id-only — avoids cascade paths, mirrors FiscalYearCloseConfiguration).
/// </summary>
public class StatementLine : ITenantOwned
{
    public long StatementLineId { get; set; }
    public int CompanyId { get; set; }

    public long StatementImportId { get; set; }
    public int BankAccountId { get; set; }
    public int LineNo { get; set; }

    public DateOnly TxnDate { get; set; }
    public TimeOnly? TxnTime { get; set; }
    public DateOnly? ValueDate { get; set; }
    public StatementDirection Direction { get; set; }
    public decimal Amount { get; set; }
    public decimal RunningBalance { get; set; }
    public required string Channel { get; set; }
    public required string TxnType { get; set; }
    public required string Description { get; set; }
    public string? RawRef { get; set; }

    public MatchStatus MatchStatus { get; set; }
    public long? MatchedReceiptId { get; set; }
    public long? MatchedPaymentVoucherId { get; set; }
    public long? PostedJournalId { get; set; }
    public DateTimeOffset? MatchedAt { get; set; }
    public long? MatchedBy { get; set; }
}

namespace Accounting.Application.Bank;

// Bank reconciliation (specs/bank-reconciliation.md B5, §Reconciliation report math) — the
// tie-out report. Computed, not stored.

/// <summary>One reconciling-item row (an unmatched statement line, a deposit-in-transit, or an
/// outstanding payment). <paramref name="Amount"/> carries the SAME sign it contributes with in
/// the tie-out (an unmatched MoneyOut line is negative).</summary>
public sealed record ReconciliationReportItem(string Description, DateOnly Date, decimal Amount);

public sealed record BankReconciliationReport(
    int BankAccountId, DateOnly From, DateOnly To,
    decimal StatementClosingBalance, decimal GlBalance,
    decimal DepositsInTransitTotal, decimal OutstandingPaymentsTotal, decimal UnmatchedLinesNet,
    decimal Difference,
    IReadOnlyList<ReconciliationReportItem> UnmatchedLines,
    IReadOnlyList<ReconciliationReportItem> DepositsInTransit,
    IReadOnlyList<ReconciliationReportItem> OutstandingPayments);

public interface IBankReconciliationReportService
{
    /// <summary>Tie-out: GL balance + deposits-in-transit − outstanding-payments ±
    /// unmatched-lines == statement closing balance; <c>Difference</c> is 0 when fully
    /// reconciled. GL balance = Dr−Cr over Posted journal lines on the bank account's
    /// gl_cash_account_id, DocDate ≤ <paramref name="to"/> (same shape as TrialBalanceAsync).</summary>
    Task<BankReconciliationReport> GetAsync(int bankAccountId, DateOnly from, DateOnly to, CancellationToken ct);
}

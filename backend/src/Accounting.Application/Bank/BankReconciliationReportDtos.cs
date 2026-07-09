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
    /// <summary>Tie-out (corrected 2026-07-09): GL balance − deposits-in-transit +
    /// outstanding-payments ± unmatched-lines == statement closing balance; <c>Difference</c>
    /// is 0 when fully reconciled. GL balance = Dr−Cr over Posted journal lines on the bank
    /// account's gl_cash_account_id, DocDate ≤ <paramref name="to"/> (same shape as
    /// TrialBalanceAsync) — CUMULATIVE, not bounded by <paramref name="from"/>. The three
    /// reconciling-item queries (unmatched lines, deposits-in-transit, outstanding payments)
    /// are likewise CUMULATIVE (≤ <paramref name="to"/> only, no lower bound) so an unresolved
    /// item dated before <paramref name="from"/> still appears in both the item list and the
    /// Difference math — the tie-out identity itself is cumulative-as-of-period-end, so a
    /// lower-bounded item query would silently drop items already baked into the two balances.
    /// <paramref name="from"/> is carried through to the DTO for display/filtering only.</summary>
    Task<BankReconciliationReport> GetAsync(int bankAccountId, DateOnly from, DateOnly to, CancellationToken ct);
}

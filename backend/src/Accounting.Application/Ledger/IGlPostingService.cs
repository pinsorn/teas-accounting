namespace Accounting.Application.Ledger;

/// <summary>
/// Posts a GL JournalEntry derived from a fiscal document. Invoked by the originating
/// fiscal service inside its existing transaction — never standalone — so atomic rollback
/// covers both fiscal and GL state. Returns the JournalId.
/// </summary>
public interface IGlPostingService
{
    Task<long> PostTaxInvoiceAsync(long taxInvoiceId, CancellationToken ct);
    Task<long> PostReceiptAsync(long receiptId, CancellationToken ct);
    Task<long> PostPaymentVoucherAsync(long paymentVoucherId, CancellationToken ct);
    Task<long> PostVendorInvoiceAsync(long vendorInvoiceId, CancellationToken ct);
    Task<long> PostTaxAdjustmentNoteAsync(long noteId, CancellationToken ct);
    Task<long> PostPayrollRunAsync(long payrollRunId, CancellationToken ct);

    /// <summary>
    /// Year-end closing (specs/year-end-closing.md D3/B4) — posts a closing or reversing JV
    /// from ALREADY-RESOLVED AccountIds. Unlike the source-document posters above, this does
    /// NOT call <c>IPeriodCloseService.EnsureOpenAsync</c> — posting into an already-closed
    /// fiscal year is the intentional, system-driven point of this method. Used by
    /// <c>IYearCloseService</c> only.
    /// </summary>
    Task<long> PostClosingEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description,
        bool isClosingEntry, long? reversalOfId,
        IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines,
        CancellationToken ct);

    /// <summary>
    /// Bank reconciliation (specs/bank-reconciliation.md D7) — posts a manual balanced JE (e.g.
    /// bank interest/fees discovered during reconciliation) from ALREADY-RESOLVED AccountIds.
    /// Deliberately NOT <see cref="PostClosingEntryAsync"/> — <c>IsClosingEntry</c> stays false so
    /// the entry appears in P&amp;L/CIT/tax reports normally (a bank interest/fee JE must NOT be
    /// hidden the way a year-end sweep is). Like every other poster here, this does NOT call
    /// <c>IPeriodCloseService.EnsureOpenAsync</c> — the CALLER (the bank reconciliation service)
    /// must call it first, exactly as ReceiptService/PaymentVoucherService do.
    /// </summary>
    Task<long> PostManualEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description, string? reference,
        IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines,
        CancellationToken ct);
}

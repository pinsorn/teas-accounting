namespace Accounting.Application.Ledger;

/// <summary>A manual-JV line (specs/manual-jv-and-coa-management.md §B2). Richer than the
/// 3-tuple overload (per-line description + BU), which bank reconciliation keeps using
/// unchanged.</summary>
public sealed record ManualJvLine(
    long AccountId, decimal Debit, decimal Credit, string? Description, int? BusinessUnitId);

/// <summary>
/// Posts a GL JournalEntry derived from a fiscal document. Invoked by the originating
/// fiscal service inside its existing transaction — never standalone — so atomic rollback
/// covers both fiscal and GL state. Returns the JournalId.
/// </summary>
public interface IGlPostingService
{
    /// <param name="glDate">cancel-reissue (spec 3.3.3) - overrides the JE date (a replacement for a
    /// closed-month original is dated in the open month). Null = the document's DocDate. The CALLER owns
    /// the period check.</param>
    Task<long> PostTaxInvoiceAsync(long taxInvoiceId, CancellationToken ct, DateOnly? glDate = null);

    /// <summary>
    /// R1/C6 (WP-1) — non-VAT revenue+AR accrual at Invoice issue (BillingNoteService
    /// .IssueAsync, non-VAT branch only). Mirrors <see cref="PostTaxInvoiceAsync"/>'s
    /// structure: Dr 1130 AR / Cr 4000 Sales for the invoice total. Throws
    /// <c>gl.bn_vat_unexpected</c> if the BN carries VAT — a VAT company's BN groups
    /// already-accrued Tax Invoices and must NEVER post here (double-counts AR+revenue).
    /// </summary>
    Task<long> PostBillingNoteAsync(long billingNoteId, CancellationToken ct);

    Task<long> PostReceiptAsync(long receiptId, CancellationToken ct, DateOnly? glDate = null);

    /// <summary>cancel-reissue (spec 3.3.3) - mirrors a POSTED JE line-for-line (Dr/Cr swapped, same
    /// account and BU) dated <paramref name="glDate"/>, linked via ReversalOfId. Caller owns the period
    /// check (F5 convention). Refuses a non-Posted source or a closing entry.</summary>
    Task<long> PostReversalAsync(long originalJournalId, DateOnly glDate, string description, CancellationToken ct);
    Task<long> PostPaymentVoucherAsync(long paymentVoucherId, CancellationToken ct);
    Task<long> PostVendorInvoiceAsync(long vendorInvoiceId, CancellationToken ct);

    /// <summary>
    /// Cycle C (specs/expense-claims.md §3, Option A) — self-contained cash disbursement for an
    /// employee expense claim. NEW additive method; calls the same private JE-balance/number/
    /// MarkPosted machinery as every other poster here but never touches PaymentVoucher code.
    /// WHT: NONE — reimbursing an employee's out-of-pocket spend is not a withholding event
    /// (any WHT was already handled by the employee at the original purchase).
    /// </summary>
    Task<long> PostExpenseClaimAsync(long expenseClaimId, CancellationToken ct);
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

    /// <summary>
    /// Manual JV overload (specs/manual-jv-and-coa-management.md §B2) — same private
    /// BuildAndPostAsync engine as every other poster (balance check, JV numbering,
    /// MarkPosted). NOT a second posting path: the only difference from the 3-tuple overload
    /// above is that the caller may set a per-line description and BU. The CALLER (JournalService)
    /// is responsible for the period gate and postable-account validation, exactly like the
    /// 3-tuple overload's caller.
    /// </summary>
    Task<long> PostManualEntryAsync(
        int companyId, int branchId, DateOnly docDate, string description, string? reference,
        IReadOnlyList<ManualJvLine> lines, CancellationToken ct);
}

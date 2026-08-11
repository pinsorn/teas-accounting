namespace Accounting.Application.Ledger;

/// <summary>
/// R1/C6 (WP-2, specs/fix-breakit-r1-ledger-integrity.md §3.2.5) — one-time correction for a
/// non-VAT company's invoices issued before WP-1 shipped (no AR/revenue accrual existed then).
/// One entry per OUTSTANDING invoice, nothing else: a fully-settled sale is untouched (its
/// present state is already correct — revenue was recognised at the receipt). For an unpaid
/// invoice, the credit side depends on whether its fiscal year is closed:
///   - issued in the CURRENT open fiscal year → Dr AR / Cr Revenue, dated at the invoice's
///     own issue date (normal recognition — that year's P&amp;L isn't final yet).
///   - issued in a CLOSED fiscal year → Dr AR / Cr Retained Earnings (กำไรสะสม), dated in the
///     current open period (never inside the closed year — that year's statements are final).
/// </summary>
public interface INonVatArBackfillService
{
    /// <summary>Builds the full correction plan with ZERO writes (I13). Safe to call any time —
    /// this is the report handed to the company's accountant.</summary>
    Task<NonVatArBackfillResult> PreviewAsync(CancellationToken ct);

    /// <summary>Posts one correcting entry per outstanding invoice, ONE TRANSACTION PER
    /// INVOICE (a mid-run failure neither loses nor duplicates already-committed corrections).
    /// Idempotent/resumable by construction: an invoice is a candidate only while its
    /// <c>JournalEntryId</c> is still null, so re-running after a partial or full prior run
    /// naturally skips everything already done and only processes what remains (I12).</summary>
    Task<NonVatArBackfillResult> ApplyAsync(CancellationToken ct);
}

/// <summary>One corrected (or to-be-corrected) invoice line in the plan.</summary>
public sealed record NonVatArBackfillInvoiceLine(
    long BillingNoteId, string? DocNo, DateOnly DocDate, decimal Outstanding);

/// <summary>One fiscal-year bucket of the plan — exactly what the accountant needs per year:
/// how much, which side it credits, and the invoices behind the total.</summary>
public sealed record NonVatArBackfillYearGroup(
    int FiscalYear,
    string CreditSide,   // "Revenue" (current open year) | "RetainedEarnings" (closed year)
    decimal OutstandingTotal,
    int InvoiceCount,
    IReadOnlyList<NonVatArBackfillInvoiceLine> Invoices);

/// <summary>
/// <paramref name="AlreadyDone"/> / <paramref name="ResumedFrom"/> — count of invoices this
/// backfill mechanism has ALREADY corrected for this company as of the moment this call ran
/// (identified by the correcting JE's own description tag — see
/// NonVatArBackfillService.DescriptionPrefix), same number viewed two ways: "already done"
/// before this call, and "resumed from" (skipped past) by this call. Always 0 for
/// <see cref="PreviewAsync"/>'s Posted; always reflects prior <c>apply</c> runs for either mode.
/// <paramref name="Blockers"/> (Opus review finding, 2026-08-12) — problems that would make
/// <c>apply</c> refuse, surfaced in <c>preview</c> too rather than only exploding when
/// someone actually runs it: the current period being closed, or a configured GL account
/// (AR/Sales/RetainedEarnings) missing from the chart of accounts. Always empty for a
/// successful <c>apply</c> result (a blocker there means the whole call threw instead).
/// </summary>
public sealed record NonVatArBackfillResult(
    string Mode,   // "preview" | "apply"
    decimal GrandTotalOutstanding,
    IReadOnlyList<NonVatArBackfillYearGroup> ByFiscalYear,
    int AlreadyDone,
    int Posted,
    int ResumedFrom,
    IReadOnlyList<string> Blockers);

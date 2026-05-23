namespace Accounting.Application.Sales;

// Sprint 13j-FE — original/copy print tracking for fiscal documents
// (TaxInvoice / Receipt / Credit Note / Debit Note). The first ORIGINAL print
// stamps OriginalPrintedAt; any later original print is a reprint and the UI
// must downgrade it to a copy (สำเนา) per ม.86/4 / ม.86/12. Every print also
// appends to audit.activity_log (append-only).
// cont.69 Phase 4 (D8) — extended to the full sales chain so EVERY document
// (not just fiscal docs) tracks original/copy printing identically.
public enum PrintDocType { TaxInvoice, Receipt, CreditNote, DebitNote, Quotation, SalesOrder, DeliveryOrder, BillingNote }

public sealed record PrintMarkResult(DateTimeOffset? OriginalPrintedAt, int PrintCount, bool WasReprint);

public interface IPrintTrackingService
{
    /// <summary>Records a print. Returns null if the document doesn't exist /
    /// isn't visible to the caller. `isCopy=false` requests an original.</summary>
    Task<PrintMarkResult?> MarkPrintedAsync(PrintDocType docType, long id, bool isCopy, CancellationToken ct);
}

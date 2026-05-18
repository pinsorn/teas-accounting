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
}

namespace Accounting.Application.Sales;

public interface ITaxInvoiceService
{
    /// <summary>Create a draft TI. Snapshots supplier + customer fields. Number not allocated yet.</summary>
    Task<long> CreateDraftAsync(CreateTaxInvoiceRequest req, CancellationToken ct);

    /// <summary>cont.69 Phase 1 — Invoice (BillingNote) → Tax Invoice, manual, VAT only.
    /// Funnels through <c>EnsureVatRegistered()</c> (throws <c>ti.non_vat_blocked</c> 422
    /// for non-VAT). Copies the Invoice lines into a new Draft TI with
    /// <c>BillingNoteId</c> set. Returns the new tax_invoice_id.</summary>
    Task<long> CreateFromBillingNoteAsync(long billingNoteId, CancellationToken ct);

    /// <summary>Post the draft: allocate TI-NNNN, freeze status, write posted_at. Throws on validation failure.</summary>
    Task<TaxInvoicePostedResult> PostAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Cursor-paginated list (desc by id) with date / customer / status filters.</summary>
    Task<CursorPage<TaxInvoiceListItem>> ListAsync(TaxInvoiceListQuery query, CancellationToken ct);

    /// <summary>Full detail incl. lines. Returns null if not found in the tenant.</summary>
    Task<TaxInvoiceDetail?> GetDetailAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Canonical e-Tax XML (unsigned skeleton) for download.</summary>
    Task<string> BuildXmlAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>A4 PDF rendition (ม.86/4 layout) for download / print.
    /// copy=true stamps the "สำเนา" watermark (reprint / copy — ม.86/4).</summary>
    Task<byte[]> BuildPdfAsync(long taxInvoiceId, CancellationToken ct, bool copy = false);

    /// <summary>Re-send the e-Tax email. No-op while the e-Tax pipeline is inert.</summary>
    Task<TaxInvoiceResendResult> ResendAsync(long taxInvoiceId, CancellationToken ct);
}

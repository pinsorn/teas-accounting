namespace Accounting.Application.Sales;

public interface ITaxInvoiceService
{
    /// <summary>Create a draft TI from a client request (incl. DO→TI, whose DO lines carry raw
    /// client taxRate). Snapshots supplier + customer fields. DERIVES each line's VAT rate from
    /// company master data (§4.6 / ม.80) — the caller's per-line taxRate is ignored. Number not
    /// allocated yet.</summary>
    Task<long> CreateDraftAsync(CreateTaxInvoiceRequest req, CancellationToken ct);

    /// <summary>cont.69 Phase 1 — Invoice (BillingNote) → Tax Invoice, manual, VAT only.
    /// Funnels through <c>EnsureVatRegistered()</c> (throws <c>ti.non_vat_blocked</c> 422
    /// for non-VAT). Copies the Invoice lines into a new Draft TI with
    /// <c>BillingNoteId</c> set. Returns the new tax_invoice_id.</summary>
    Task<long> CreateFromBillingNoteAsync(long billingNoteId, CancellationToken ct);

    /// <summary>D3 (spec mcp-expansion.md) — draft-only full edit: replaces header + delete-and-
    /// recreates ALL lines, recomputing totals server-side (client Lines only; never trust a
    /// client total). Throws <c>ti.cannot_edit_after_post</c> once the TI has left Draft.
    /// DocNo/Status/PostedAt/DocDate/TaxPointDate are server-controlled and untouched here.</summary>
    Task UpdateDraftAsync(long taxInvoiceId, CreateTaxInvoiceRequest req, CancellationToken ct);

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

    /// <summary>cont.121 — the canonical paper composition (posted snapshot, ม.86/4)
    /// the PDF renders, exposed as JSON for the FE PaperDocument.</summary>
    Task<Pdf.PaperDocModel> BuildPaperAsync(long taxInvoiceId, CancellationToken ct, bool copy = false);

    /// <summary>Re-send the e-Tax email. No-op while the e-Tax pipeline is inert.</summary>
    Task<TaxInvoiceResendResult> ResendAsync(long taxInvoiceId, CancellationToken ct);
}

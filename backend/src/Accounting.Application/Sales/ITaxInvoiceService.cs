namespace Accounting.Application.Sales;

public interface ITaxInvoiceService
{
    /// <summary>Create a draft TI. Snapshots supplier + customer fields. Number not allocated yet.</summary>
    Task<long> CreateDraftAsync(CreateTaxInvoiceRequest req, CancellationToken ct);

    /// <summary>Post the draft: allocate TI-NNNN, freeze status, write posted_at. Throws on validation failure.</summary>
    Task<TaxInvoicePostedResult> PostAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Cursor-paginated list (desc by id) with date / customer / status filters.</summary>
    Task<CursorPage<TaxInvoiceListItem>> ListAsync(TaxInvoiceListQuery query, CancellationToken ct);

    /// <summary>Full detail incl. lines. Returns null if not found in the tenant.</summary>
    Task<TaxInvoiceDetail?> GetDetailAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Canonical e-Tax XML (unsigned skeleton) for download.</summary>
    Task<string> BuildXmlAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>A4 PDF rendition (ม.86/4 layout) for download / print.</summary>
    Task<byte[]> BuildPdfAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>Re-send the e-Tax email. No-op while the e-Tax pipeline is inert.</summary>
    Task<TaxInvoiceResendResult> ResendAsync(long taxInvoiceId, CancellationToken ct);
}

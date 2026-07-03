using Accounting.Application.Pdf;

namespace Accounting.Application.Sales;

/// <summary>Sprint 10 C3 — PDF for the Q/SO/DO chain. Quotation PDF carries
/// the optional WHT informational note (B4); DO PDF shows both
/// ใบส่งของ + ใบกำกับภาษี labels when combined.</summary>
public interface ISalesChainPdfService
{
    // cont.69 Phase 4 (D8) — copy=true renders the สำเนา watermark (universal print).
    Task<byte[]> QuotationPdfAsync(long id, CancellationToken ct, bool copy = false);
    Task<byte[]> SalesOrderPdfAsync(long id, CancellationToken ct, bool copy = false);
    Task<byte[]> DeliveryOrderPdfAsync(long id, CancellationToken ct, bool copy = false);

    // cont.121 canonical paper DTO — the same composition the PDF renders,
    // exposed as JSON for the FE PaperDocument (GET /{doc}/{id}/paper).
    Task<PaperDocModel> QuotationPaperAsync(long id, CancellationToken ct, bool copy = false);
    Task<PaperDocModel> SalesOrderPaperAsync(long id, CancellationToken ct, bool copy = false);
    Task<PaperDocModel> DeliveryOrderPaperAsync(long id, CancellationToken ct, bool copy = false);
}

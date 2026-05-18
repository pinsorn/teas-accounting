namespace Accounting.Application.Sales;

/// <summary>Sprint 10 C3 — PDF for the Q/SO/DO chain. Quotation PDF carries
/// the optional WHT informational note (B4); DO PDF shows both
/// ใบส่งของ + ใบกำกับภาษี labels when combined.</summary>
public interface ISalesChainPdfService
{
    Task<byte[]> QuotationPdfAsync(long id, CancellationToken ct);
    Task<byte[]> SalesOrderPdfAsync(long id, CancellationToken ct);
    Task<byte[]> DeliveryOrderPdfAsync(long id, CancellationToken ct);
}

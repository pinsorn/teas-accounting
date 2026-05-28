namespace Accounting.Application.Purchase;

/// <summary>
/// F (Question-Backend36) — server-resolved Purchase document chain. Mirrors the Sales
/// <c>IDocumentCrossRefService.GetChainAsync</c> idea but lives in its own service so the
/// fixed 7-slot Sales DTO (<c>Quotation, SalesOrder, DeliveryOrders[], …</c>) is left
/// untouched. Purchase has a different topology: PO → VI* → PV* (each PV optionally
/// settles one VI) → WhtCert* (each PV may emit ≥1 50ทวิ).
///
/// The DTO is intentionally flat: one nullable PO + three lists. The FE
/// PurchaseDocumentChain only renders the linear PO→VI→PV→WHT chain today, so a single-PO
/// root is enough; fan-out shows as longer lists. If a future use case needs branching
/// across PVs+VIs richly, this DTO can grow without breaking the Sales-side DTO.
/// </summary>
public sealed record PurchaseChainNode(
    long Id,
    string? DocNo,        // nullable on Draft (PO/VI/PV); WhtCertificate is always non-null
    DateOnly DocDate,
    string Status,
    decimal Amount);

public sealed record PurchaseChainDto(
    PurchaseChainNode? PurchaseOrder,
    IReadOnlyList<PurchaseChainNode> VendorInvoices,
    IReadOnlyList<PurchaseChainNode> PaymentVouchers,
    IReadOnlyList<PurchaseChainNode> WhtCertificates);

public interface IPurchaseChainService
{
    /// <summary>
    /// Resolve the full Purchase chain anchored at any of the four Purchase doc types.
    /// Returns null if the anchor row doesn't exist in the caller's tenant.
    /// Valid anchorType: <c>purchase-order</c>, <c>vendor-invoice</c>, <c>payment-voucher</c>,
    /// <c>wht-certificate</c>.
    /// </summary>
    Task<PurchaseChainDto?> GetAsync(string anchorType, long id, CancellationToken ct);
}

namespace Accounting.Application.Sales;

/// <summary>
/// Sprint 13h P8 — central cross-reference service. Resolves the relationship
/// graph between sales documents (Q ↔ SO ↔ DO ↔ TI ↔ RC / CN / DN / BN) so
/// the UI can render clickable chips on every document detail page.
///
/// Tenant isolation: every query carries <c>x.CompanyId == tenant.CompanyId</c>
/// on top of the EF global query filter (gotcha §26 belt-and-braces).
/// </summary>
public interface IDocumentCrossRefService
{
    /// <summary>
    /// Resolve everything related to a Tax Invoice. Used by the TI detail
    /// page to render RC / CN / DN / BN chips. Returns <c>null</c> when the
    /// TI does not belong to the current tenant.
    /// </summary>
    Task<DocumentCrossRefDto?> GetForTaxInvoiceAsync(long taxInvoiceId, CancellationToken ct);

    /// <summary>
    /// Resolve everything related to a Receipt. Used by the RC detail page
    /// to render TI chips (already in <c>appliedTo</c>) plus any back-links.
    /// </summary>
    Task<DocumentCrossRefDto?> GetForReceiptAsync(long receiptId, CancellationToken ct);

    /// <summary>
    /// Resolve everything related to a Credit Note or Debit Note. Used by
    /// the AN detail page to render the original TI chip + any sibling AN.
    /// </summary>
    Task<DocumentCrossRefDto?> GetForAdjustmentNoteAsync(long noteId, CancellationToken ct);

    /// <summary>
    /// cont.69 Phase 3 (D7) — resolve the FULL document chain
    /// (Q → SO → DO → Invoice → TI → RC + CN/DN) from any anchor node. Walks UP the
    /// FK graph to the originating Quotation, then DOWN from it collecting every
    /// descendant (a Q may fan out to multiple SO/DO/Invoice/TI/RC — all returned).
    /// Tenant-scoped. Returns <c>null</c> when the anchor is not found for the tenant.
    /// </summary>
    /// <param name="anchorType">
    /// quotation | sales-order | delivery-order | billing-note | tax-invoice |
    /// receipt | adjustment-note
    /// </param>
    Task<DocumentChainDto?> GetChainAsync(string anchorType, long id, CancellationToken ct);
}

/// <summary>cont.69 Phase 3 — one node in the unified document chain.</summary>
public sealed record ChainNode(long Id, string? DocNo, DateOnly DocDate, string Status, decimal Total);

/// <summary>
/// cont.69 Phase 3 — normalized full chain. Each property holds the present node(s)
/// in document order. Singular slots (Quotation/SalesOrder) are the unique upstream
/// roots; the rest are collections to support fan-out.
/// </summary>
public sealed record DocumentChainDto(
    ChainNode? Quotation,
    ChainNode? SalesOrder,
    IReadOnlyList<ChainNode> DeliveryOrders,
    IReadOnlyList<ChainNode> Invoices,
    IReadOnlyList<ChainNode> TaxInvoices,
    IReadOnlyList<ChainNode> Receipts,
    IReadOnlyList<ChainNode> AdjustmentNotes);

public sealed record DocumentCrossRefDto(
    DocumentRef? Quotation,
    DocumentRef? SalesOrder,
    DocumentRef? DeliveryOrder,
    IReadOnlyList<DocumentRef> TaxInvoices,
    IReadOnlyList<ReceiptRef> Receipts,
    IReadOnlyList<DocumentRef> CreditNotes,
    IReadOnlyList<DocumentRef> DebitNotes,
    IReadOnlyList<DocumentRef> BillingNotes);

public sealed record DocumentRef(long Id, string? DocNo, string Status);

public sealed record ReceiptRef(long Id, string? DocNo, string Status, decimal AppliedAmount);

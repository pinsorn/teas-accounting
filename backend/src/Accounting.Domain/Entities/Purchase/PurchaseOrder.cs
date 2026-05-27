using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Purchase;

/// <summary>Sprint 12 — internal Purchase Order. Approval + spend traceability,
/// NOT an external vendor commitment. Draft → Approved (doc_no allocated, SoD)
/// → Closed (auto when linked VI total ≥ 95%, or manual) | Cancelled.</summary>
public class PurchaseOrder : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long PurchaseOrderId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }                    // PO-NNNN, on Approve
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateOnly DocDate { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }

    public long VendorId { get; set; }
    public required string VendorName { get; set; }
    public string? VendorAddress { get; set; }
    public string? VendorTaxId { get; set; }
    public CustomerType VendorType { get; set; }

    public int? BusinessUnitId { get; set; }

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal TotalAmountThb { get; set; }

    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long? ApprovedBy { get; set; }
    public DateTimeOffset? SentToVendorAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    // Sprint 13j-PURCH — original/copy print tracking (parity with Sales TaxInvoice).
    // OriginalPrintedAt stamped on the first original print; reprints are marked สำเนา.
    public DateTimeOffset? OriginalPrintedAt { get; set; }
    public int PrintCount { get; set; }

    public ICollection<PurchaseOrderLine> Lines { get; set; } = new List<PurchaseOrderLine>();

    /// <summary>Draft → Approved with SoD (approver ≠ creator). Belt-and-braces
    /// with DB CHECK ck_po_sod. doc_no allocated by the service.</summary>
    public void MarkApproved(long approverUserId, string docNo, DateTimeOffset at)
    {
        if (Status != PurchaseOrderStatus.Draft)
            throw new DomainException("po.not_draft", $"Cannot approve PO in status {Status}.");
        if (CreatedBy is { } creator && creator == approverUserId)
            throw new DomainException("po.sod_violation",
                "Approver must differ from the creator (segregation of duties).");
        DocNo = docNo;
        ApprovedBy = approverUserId;
        ApprovedAt = at;
        Status = PurchaseOrderStatus.Approved;
    }

    public void MarkClosed(DateTimeOffset at)
    {
        if (Status != PurchaseOrderStatus.Approved)
            throw new DomainException("po.not_approved",
                $"Only an Approved PO can be closed (status {Status}).");
        Status = PurchaseOrderStatus.Closed;
        ClosedAt = at;
    }

    public void MarkCancelled(string reason, DateTimeOffset at)
    {
        if (Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
            throw new DomainException("po.terminal",
                $"Cannot cancel a {Status} PO.");
        Status = PurchaseOrderStatus.Cancelled;
        CancellationReason = reason;
        CancelledAt = at;
    }
}

public class PurchaseOrderLine
{
    public long PurchaseOrderId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

    public long?  ProductId { get; set; }
    public string? ProductCode { get; set; }
    public required string DescriptionTh { get; set; }

    public decimal Quantity { get; set; }
    public string? UomText { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineAmount { get; set; }

    public int?    TaxCodeId { get; set; }
    public string? TaxCode { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
}

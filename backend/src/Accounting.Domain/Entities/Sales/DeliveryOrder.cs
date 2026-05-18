using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>Sprint 10 — ใบส่งของ. Pattern X: is_combined_with_ti → on POST a
/// linked TaxInvoice is auto-created. Pattern Y: TI created later from DO.</summary>
public class DeliveryOrder : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long DeliveryOrderId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }                 // DO-NNNN, on POST
    public DeliveryOrderStatus Status { get; set; } = DeliveryOrderStatus.Draft;
    public DateOnly DocDate { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }
    public CustomerType CustomerType { get; set; }

    public int? BusinessUnitId { get; set; }
    public long? SalesOrderId { get; set; }

    public bool IsCombinedWithTi { get; set; }
    public long? TaxInvoiceId { get; set; }             // linked TI (Pattern X auto / Pattern Y manual)

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }
    public string? CancelledReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<DeliveryOrderLine> Lines { get; set; } = new List<DeliveryOrderLine>();
}

public class DeliveryOrderLine
{
    public long DeliveryOrderId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

    public long? SalesOrderLineId { get; set; }         // source SO line (partial delivery)
    public long?  ProductId { get; set; }
    public string? ProductCode { get; set; }
    public required string DescriptionTh { get; set; }

    public decimal Quantity { get; set; }
    public required string UomText { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal LineAmount { get; set; }

    public int     TaxCodeId { get; set; }
    public required string TaxCode { get; set; }
    public decimal TaxRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
}

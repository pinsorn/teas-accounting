using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>Sprint 10 — ใบสั่งขาย (internal commitment). Posted → number
/// allocated. Closed when every line fully delivered.</summary>
public class SalesOrder : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long SalesOrderId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }                 // SO-NNNN, on POST
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public DateOnly DocDate { get; set; }
    public DateOnly? ExpectedDeliveryDate { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }
    public CustomerType CustomerType { get; set; }

    public int? BusinessUnitId { get; set; }
    public long? QuotationId { get; set; }              // optional source

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CancelledReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<SalesOrderLine> Lines { get; set; } = new List<SalesOrderLine>();
}

public class SalesOrderLine
{
    public long SalesOrderId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

    public long?  ProductId { get; set; }
    public string? ProductCode { get; set; }
    public required string DescriptionTh { get; set; }

    public decimal Quantity { get; set; }
    public decimal DeliveredQuantity { get; set; }      // running total across DOs
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

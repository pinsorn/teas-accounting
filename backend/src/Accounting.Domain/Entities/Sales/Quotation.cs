using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// Sprint 10 — ใบเสนอราคา (non-fiscal). Doc number allocated on Send (the
/// POST-equivalent for a quotation). Converts to a SalesOrder when Accepted.
/// </summary>
public class Quotation : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long QuotationId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }                 // Q-NNNN, on Send
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;
    public DateOnly DocDate { get; set; }
    public DateOnly ValidUntilDate { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }
    public CustomerType CustomerType { get; set; }

    public int? BusinessUnitId { get; set; }

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }

    /// <summary>Auto from CustomerType (CORPORATE=true). WHT note computed at
    /// PDF time, never stored.</summary>
    public bool ShowWhtNote { get; set; }

    public long? ConvertedToSoId { get; set; }
    public string? RejectedReason { get; set; }
    public string? CancelledReason { get; set; }

    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? ExpiredAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public ICollection<QuotationLine> Lines { get; set; } = new List<QuotationLine>();
}

public class QuotationLine
{
    public long QuotationId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

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

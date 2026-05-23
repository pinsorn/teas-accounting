using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// Sprint 13h P6.2 — ใบแจ้งหนี้/ใบวางบิล (Billing Note). Non-fiscal: a customer-facing
/// statement that rolls up one or more posted TaxInvoices (and optionally a Quotation)
/// for collection. Doc number allocated on Issue (Draft → Issued). Status moves to
/// Settled when receipt(s) fully cover the BN total. Cancellation is soft (status only)
/// because Issued has a doc_no allocated (gap rule per Plan §17.6).
/// Numbering format: MM-YYYY-BL-{BU}-NNNN per Answer-Sana-Backend27 P6.2.
/// </summary>
public class BillingNote : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long BillingNoteId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }                 // BL-NNNN, on Issue
    public BillingNoteStatus Status { get; set; } = BillingNoteStatus.Draft;
    public DateOnly DocDate { get; set; }
    public DateOnly DueDate { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName { get; set; }
    public string? CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }
    public CustomerType CustomerType { get; set; }

    public int? BusinessUnitId { get; set; }
    public long? QuotationId { get; set; }

    /// <summary>cont.69 Phase 1 — source Delivery Order this Invoice was created from
    /// (DO → Invoice, manual). Nullable: Invoices may be created standalone.</summary>
    public long? DeliveryOrderId { get; set; }

    public string CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public string? Notes { get; set; }
    public string? InternalNotes { get; set; }
    public string? CancelledReason { get; set; }

    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    // cont.69 Phase 4 — universal original/copy print tracking (D8). Surfaces as
    // "ใบแจ้งหนี้/Invoice" in the UI; entity stays BillingNote internally (D5).
    public DateTimeOffset? OriginalPrintedAt { get; set; }
    public int PrintCount { get; set; }

    public ICollection<BillingNoteLine> Lines { get; set; } = new List<BillingNoteLine>();

    /// <summary>
    /// Sprint 13i C7 — TaxInvoices grouped by this BN, via the dedicated
    /// sales.billing_note_tax_invoices join table (replaces the old bigint[] column).
    /// </summary>
    public ICollection<BillingNoteTaxInvoice> TaxInvoiceLinks { get; set; } = new List<BillingNoteTaxInvoice>();
}

/// <summary>
/// Sprint 13i C7 — join row between a BillingNote and a posted TaxInvoice it groups.
/// applied_amount is persisted at link time (defaults to the TaxInvoice total) so the
/// BN can record how much of each TI it covers for collection. Composite PK
/// (billing_note_id, tax_invoice_id). Tenant-isolated via CompanyId (ITenantOwned) +
/// RLS mirroring sales.billing_notes.
/// </summary>
public class BillingNoteTaxInvoice : ITenantOwned
{
    public long BillingNoteId { get; set; }
    public long TaxInvoiceId  { get; set; }
    public int  CompanyId     { get; set; }
    public decimal AppliedAmount { get; set; }
}

public class BillingNoteLine
{
    public long BillingNoteId { get; set; }
    public long LineId { get; set; }
    public int  LineNo { get; set; }

    public long?  ProductId { get; set; }
    public string? ProductCode { get; set; }
    /// <summary>Sprint 13h P7 — Product master ProductType snapshot. Sprint 13i C5 — NOT NULL, defaults GOOD.</summary>
    public string ProductType { get; set; } = "GOOD";
    /// <summary>Optional FK to the source TaxInvoice rolled up into this BN line.</summary>
    public long? TaxInvoiceId { get; set; }
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

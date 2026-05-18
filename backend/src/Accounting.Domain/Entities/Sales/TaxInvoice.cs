using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// Full Tax Invoice (ใบกำกับภาษีเต็มรูป) — ม.86/4. Snapshot pattern: supplier and customer
/// identifying fields are frozen at issue time so later master-data edits cannot retro-mutate
/// the legal document.
/// </summary>
public class TaxInvoice : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long TaxInvoiceId { get; set; }
    public int  CompanyId    { get; set; }
    public int  BranchId     { get; set; }

    /// <summary>NULL until posted. Allocated from NumberSequence(TI) on POST.</summary>
    public string? DocNo  { get; set; }
    public string? BookNo { get; set; }

    public DateOnly DocDate       { get; set; }
    public DateOnly TaxPointDate  { get; set; }
    public string?  TaxPointReason { get; set; }

    /// <summary>Sprint 8 — revenue stream tag. Snapshot at draft; spliced into the
    /// doc number at POST (MM-YYYY-TI[-{BU}]-NNNN). NULL allowed unless the company
    /// has requires_business_unit = true.</summary>
    public int? BusinessUnitId { get; set; }

    /// <summary>"FULL" only — Simplified is out of scope (CLAUDE.md §15.4 / §10).</summary>
    public string InvoiceType { get; set; } = "FULL";

    /// <summary>True when this row is a replacement (ใบแทน) for a damaged original (ม.86/12).</summary>
    public bool IsSubstitute { get; set; }
    public long? OriginalInvoiceId { get; set; }

    // ---- Supplier snapshot (frozen) ----
    public required string SupplierTaxId      { get; set; }
    public required string SupplierBranchCode { get; set; }
    public required string SupplierBranchName { get; set; }
    public required string SupplierName       { get; set; }
    public required string SupplierAddress    { get; set; }

    // ---- Customer snapshot (frozen) ----
    public long    CustomerId             { get; set; }
    public string? CustomerTaxId          { get; set; }
    public string? CustomerBranchCode     { get; set; }
    public string? CustomerBranchName     { get; set; }
    public required string CustomerName    { get; set; }
    public required string CustomerAddress { get; set; }
    public bool    CustomerVatRegistered  { get; set; }

    // ---- Amounts (NUMERIC(19,4)) ----
    public string  CurrencyCode      { get; set; } = "THB";
    public decimal ExchangeRate      { get; set; } = 1m;
    public decimal SubtotalAmount    { get; set; }
    public decimal DiscountAmount    { get; set; }
    public decimal TaxableAmount     { get; set; }
    public decimal NonTaxableAmount  { get; set; }
    public decimal TaxAmount         { get; set; }
    public decimal TotalAmount       { get; set; }
    public decimal TotalAmountThb    { get; set; }
    public string? AmountInWordsTh   { get; set; }
    public bool    IsTaxInclusive    { get; set; }

    // ---- Status ----
    public DocumentStatus Status   { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long?  PostedBy { get; set; }

    public string  PaymentStatus { get; set; } = "UNPAID";
    public decimal AmountPaid   { get; set; }
    public DateOnly? DueDate    { get; set; }

    // ---- e-Tax ----
    public bool             IsETax           { get; set; }
    public string?          ETaxXmlUrl       { get; set; }
    public string?          ETaxPdfUrl       { get; set; }
    public DateTimeOffset?  ETaxSignedAt     { get; set; }
    public DateTimeOffset?  ETaxSubmittedAt  { get; set; }
    public string?          ETaxAckId        { get; set; }
    public string?          ETaxStatus       { get; set; }

    // ---- Delivery tracking ----
    public bool             DeliveredToCustomer    { get; set; }
    public DateTimeOffset?  DeliveredToCustomerAt  { get; set; }
    public string?          DeliveryMethod         { get; set; }

    public string? PaymentTerms { get; set; }
    public string? Notes        { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public long   Version   { get; set; }

    public ICollection<TaxInvoiceLine> Lines { get; set; } = new List<TaxInvoiceLine>();

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("ti.not_draft", $"Cannot post tax invoice in status {Status}.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("ti.no_docno", "DocNo is required when posting.");
        if (DocDate != TaxPointDate)
            throw new DomainException("ti.tax_point_mismatch",
                "doc_date must equal tax_point_date (ม.86/4 #7).");
        if (TaxAmount < 0 || TotalAmount <= 0)
            throw new DomainException("ti.invalid_amounts", "Amounts are invalid.");

        DocNo    = docNo;
        Status   = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

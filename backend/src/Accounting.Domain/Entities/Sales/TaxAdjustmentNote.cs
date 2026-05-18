using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// ใบลดหนี้ (CN, ม.86/10) / ใบเพิ่มหนี้ (DN, ม.86/9). Always references an original posted TI.
/// Reason is mandatory because RD requires the rationale on the document.
/// </summary>
public class TaxAdjustmentNote : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long NoteId    { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string?  DocNo { get; set; }
    public required string PrefixCode { get; set; }   // "CN" or "DN"
    public TaxAdjustmentNoteType NoteType { get; set; }

    public DateOnly DocDate { get; set; }
    public DateOnly TaxPointDate { get; set; }

    /// <summary>Sprint 8 — BU snapshot at draft (typically inherited from the
    /// original TI). NULL allowed unless company.requires_business_unit.</summary>
    public int? BusinessUnitId { get; set; }

    public long OriginalTaxInvoiceId { get; set; }
    /// <summary>Structured reason code (CreditNoteReasonCode/DebitNoteReasonCode name). UX/reporting.</summary>
    public string? ReasonCode { get; set; }
    public required string Reason { get; set; }   // ม.86/10 #5 / ม.86/9 #5 — legal free text

    // Customer snapshot
    public long CustomerId { get; set; }
    public string?  CustomerTaxId      { get; set; }
    public string?  CustomerBranchCode { get; set; }
    public required string CustomerName { get; set; }
    public required string CustomerAddress { get; set; }
    public bool CustomerVatRegistered { get; set; }

    // Amounts (always positive; NoteType determines DR/CR effect)
    public string  CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount { get; set; }
    public decimal TaxAmount      { get; set; }
    public decimal TotalAmount    { get; set; }
    public decimal TotalAmountThb { get; set; }
    public decimal TaxRate        { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("note.not_draft", $"Cannot post in status {Status}.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("note.no_docno", "DocNo is required.");
        if (string.IsNullOrWhiteSpace(Reason))
            throw new DomainException("note.reason_required", "Reason is mandatory (ม.86/9, ม.86/10).");
        if (TotalAmount <= 0)
            throw new DomainException("note.invalid_amount", "TotalAmount must be > 0.");
        if (DocDate != TaxPointDate)
            throw new DomainException("note.tax_point_mismatch", "doc_date must equal tax_point_date.");

        DocNo = docNo;
        Status = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

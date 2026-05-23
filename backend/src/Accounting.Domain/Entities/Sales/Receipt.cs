using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Sales;

/// <summary>
/// ใบเสร็จรับเงิน (Receipt). Records cash-in from a customer and applies the proceeds
/// across one or more posted Tax Invoices via <see cref="ReceiptApplication"/> rows.
/// </summary>
public class Receipt : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long ReceiptId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    public string? DocNo { get; set; }
    public DateOnly DocDate { get; set; }

    /// <summary>Sprint 8 — BU. NULL when the receipt crosses multiple BUs
    /// (cross-BU apply); set when all applied TIs share one BU.</summary>
    public int? BusinessUnitId { get; set; }

    public long CustomerId { get; set; }
    public required string CustomerName    { get; set; }
    public required string CustomerAddress { get; set; }
    public string? CustomerTaxId { get; set; }

    public PaymentMethod PaymentMethod { get; set; }
    public string?  ChequeNo   { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public long?    BankAccountId { get; set; }

    public string  CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;

    public decimal Amount       { get; set; }    // = sum of applications
    public decimal TotalAmount  { get; set; }    // = Amount (Receipt is non-VATable — Receipt for already-VATed TI)
    public decimal TotalAmountThb { get; set; }

    // Sprint 8.6 — AR-side WHT (customer withheld from us). Amount stays = sum(apps);
    // CashReceived = Amount - WhtAmount, computed at service POST.
    // Sprint (multi-category WHT, 2026-05-22) — WhtAmount is now the SUM of the
    // per-income-type WhtLines. WhtTypeId is the legacy single-category pointer:
    // set to the one line's type when exactly one category, NULL when the bill
    // spans multiple service categories (rent + service + ads). The breakdown
    // lives in WhtLines; the customer issues one 50ทวิ (CustomerWhtCertNo).
    public decimal  WhtAmount { get; set; }              // 0 when no WHT; else Σ WhtLines
    public int?     WhtTypeId { get; set; }              // legacy single-cat; NULL when multi
    public string?  CustomerWhtCertNo   { get; set; }    // เลขที่ใบ 50ทวิ ที่ลูกค้าออกให้
    public DateOnly? CustomerWhtCertDate { get; set; }
    public decimal  CashReceived { get; set; }           // = Amount - WhtAmount

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long? PostedBy { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
    public long Version { get; set; }

    // Sprint 13j-FE — original/copy print tracking (reprints marked สำเนา/COPY).
    public DateTimeOffset? OriginalPrintedAt { get; set; }
    public int PrintCount { get; set; }

    public ICollection<ReceiptApplication> Applications { get; set; } = new List<ReceiptApplication>();

    /// <summary>Non-VAT billing path — own line items for a standalone receipt
    /// (cash bill / ใบเสร็จรับเงิน). A VAT-registered company never uses these: its
    /// receipt derives line items from the applied Tax Invoices. A non-VAT company
    /// cannot issue a Tax Invoice (ม.86/4) so its receipt carries its own lines
    /// (or derives them from an applied Delivery Order).</summary>
    public ICollection<ReceiptLine> Lines { get; set; } = new List<ReceiptLine>();

    /// <summary>Sprint (multi-category WHT) — per-income-type withholding breakdown.
    /// Empty when no WHT. One <see cref="WhtCertificate"/> Direction='R' is issued per
    /// line on POST (all sharing the customer's single 50ทวิ number).</summary>
    public ICollection<ReceiptWhtLine> WhtLines { get; set; } = new List<ReceiptWhtLine>();

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("rc.not_draft", $"Cannot post RC in status {Status}.");
        if (Amount <= 0)
            throw new DomainException("rc.invalid_amount", "Receipt amount must be > 0.");
        // A receipt draws from one of two sources: applications (apply the cash across
        // posted Tax Invoices [VAT] or Delivery Orders [non-VAT]) OR its own line items
        // (standalone non-VAT cash bill). At least one source is required.
        if (Applications.Count == 0 && Lines.Count == 0)
            throw new DomainException("rc.no_source",
                "Receipt must apply to a document or carry its own line items.");
        if (Applications.Count > 0 && Applications.Sum(a => a.AppliedAmount) != Amount)
            throw new DomainException("rc.application_mismatch",
                "Sum of applied amounts must equal receipt amount.");
        if (Applications.Count == 0 && Lines.Sum(l => l.Amount) != Amount)
            throw new DomainException("rc.line_mismatch",
                "Sum of standalone line amounts must equal receipt amount.");

        DocNo = docNo;
        Status = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

/// <summary>One settled-against document for a receipt. Exactly one of
/// <see cref="TaxInvoiceId"/> (VAT path — settles AR) or <see cref="DeliveryOrderId"/>
/// (non-VAT path — recognizes revenue at receipt) is set. A standalone non-VAT cash
/// receipt has NO application rows (it carries its own <see cref="ReceiptLine"/>s).</summary>
public class ReceiptApplication
{
    public long ApplicationId { get; set; }
    public long ReceiptId     { get; set; }
    public long? TaxInvoiceId { get; set; }   // VAT path; NULL otherwise
    public long? DeliveryOrderId { get; set; } // non-VAT legacy path (cont.68); NULL otherwise
    // cont.69 Phase 1 — non-VAT Invoice (BillingNote) path; settles a receipt against an
    // issued Invoice, recognizing revenue at receipt (Cr Sales 4000, cash basis). NULL
    // for the TI / DO paths. Exactly one of the three is set on an application row.
    public long? BillingNoteId { get; set; }
    public decimal AppliedAmount { get; set; }
}

/// <summary>Non-VAT standalone receipt line (ใบเสร็จรับเงิน / บิลเงินสด). No VAT
/// fields — a non-VAT entity has no VAT concept on its documents (ม.86 — not a
/// taxRate=0; the field simply does not exist for it). Mirrors the
/// <see cref="ReceiptApplication"/> tenancy model: scoped via ReceiptId, no own
/// company_id / RLS.</summary>
public class ReceiptLine
{
    public long ReceiptLineId { get; set; }
    public long ReceiptId     { get; set; }
    public int  LineNo        { get; set; }
    public long?  ProductId   { get; set; }
    public string? ProductCode { get; set; }
    public string ProductType { get; set; } = "GOOD";  // GOOD/SERVICE — drives WHT eligibility
    public required string DescriptionTh { get; set; }
    public decimal Quantity   { get; set; }
    public string? UomText    { get; set; }
    public decimal UnitPrice  { get; set; }
    public decimal Amount     { get; set; }   // = Quantity * UnitPrice (no VAT)
}

/// <summary>
/// Sprint (multi-category WHT, 2026-05-22) — one income-type slice of a receipt's
/// withholding. A bill mixing rent (5%) + service (3%) + ads (2%) produces one row
/// per category. Base is the ex-VAT service amount pro-rated to the paid portion;
/// WhtAmount = round(Base * WhtRate, 2). Snapshots the WhtType code/income-code/rate
/// so a later rate change does not rewrite history. Tenant-scoped transitively via
/// ReceiptId (mirrors ReceiptApplication — no own company_id / RLS).
/// </summary>
public class ReceiptWhtLine
{
    public long ReceiptWhtLineId { get; set; }
    public long ReceiptId        { get; set; }
    public int  WhtTypeId        { get; set; }
    public required string IncomeTypeCode { get; set; }   // RD income type (ม.40)
    public required string WhtTypeCode    { get; set; }   // e.g. "SVC", "RENT"
    public decimal WhtRate    { get; set; }               // 0.03 = 3%
    public decimal BaseAmount { get; set; }               // ex-VAT base for this category
    public decimal WhtAmount  { get; set; }               // = round(BaseAmount * WhtRate, 2)
}

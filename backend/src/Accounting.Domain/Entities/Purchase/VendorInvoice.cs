using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Purchase;

/// <summary>
/// ใบรับวางบิล / บันทึกใบกำกับภาษีซื้อ — the AP accrual for a vendor's tax invoice
/// (ใบกำกับภาษีซื้อ). Mirrors <c>sales.tax_invoices</c>: snapshot pattern, POST-once,
/// immutable after post. Input VAT lands in ภ.พ.30 by <see cref="VatClaimPeriod"/>
/// (ม.82/4 — claimable in the vendor-TI month or any of the following 6 months).
/// 3-way match (PR→PO→GR) is intentionally out of scope (Answer-Sana-Question-Backend5 §B1.3).
/// </summary>
public class VendorInvoice : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long VendorInvoiceId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    /// <summary>cont.79 — GL dimension: which Business Unit this purchase belongs to. Required
    /// when Company.RequiresBusinessUnit; embedded in the doc number; stamped onto journal lines.</summary>
    public int? BusinessUnitId { get; set; }

    /// <summary>NULL until posted. Allocated from NumberSequence(VI) on POST.</summary>
    public string? DocNo { get; set; }

    /// <summary>Date we record it (Asia/Bangkok today — never user input, CLAUDE.md §10).</summary>
    public DateOnly DocDate { get; set; }

    // ---- Vendor's legal source doc (ใบกำกับภาษีซื้อ) — the ม.82/4 legal refs ----
    public required string VendorTaxInvoiceNo  { get; set; }
    public DateOnly        VendorTaxInvoiceDate { get; set; }

    /// <summary>
    /// ม.82/4 input-VAT claim period as yyyymm (e.g. 202604). Defaults to the period of
    /// <see cref="VendorTaxInvoiceDate"/>; may be moved forward up to +6 months from it.
    /// Drives which ภ.พ.30 the input VAT is reported in.
    /// </summary>
    public int VatClaimPeriod { get; set; }

    // ---- Vendor snapshot (frozen — vendors editable later) ----
    public long    VendorId          { get; set; }
    public string? VendorTaxId       { get; set; }
    public string? VendorBranchCode  { get; set; }
    public required string VendorName { get; set; }
    public string? VendorAddress     { get; set; }
    public CustomerType VendorType   { get; set; }

    // ---- Amounts (NUMERIC(19,4)) ----
    public string  CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;
    public decimal SubtotalAmount          { get; set; }
    public decimal VatAmount               { get; set; }  // recoverable input VAT only
    public decimal NonRecoverableVatAmount { get; set; }  // lumped into expense (ม.82/5)
    public decimal TotalAmount             { get; set; }  // subtotal + all VAT
    public decimal TotalAmountThb          { get; set; }

    // Sprint 8.7 — false (receipt-only / non-VAT or foreign-no-VAT-D vendor):
    // VAT can't be claimed → lumped into expense (Dr Expense gross / Cr AP gross),
    // matching the ม.82/5 non-recoverable pattern.
    public bool HasInputVat                 { get; set; } = true;
    /// <summary>Auto-set when vendor is foreign without Thai VAT-D — Sprint 9 ภ.พ.36 generator scans this.</summary>
    public bool RequiresPnd36ReverseCharge  { get; set; }

    /// <summary>Sprint 12 — optional retroactive link to an internal PO (loose
    /// matching, ≤105% tolerance). Most VIs arrive without a PO reference.</summary>
    public long? PurchaseOrderId { get; set; }

    /// <summary>Stored, never SUM-computed (Answer-Sana-Question-Backend5 §3).</summary>
    public decimal SettledAmount    { get; set; }
    public string  SettlementStatus { get; set; } = "UNPAID";  // UNPAID | PARTIAL | PAID

    public string? Notes { get; set; }

    public DocumentStatus Status   { get; set; } = DocumentStatus.Draft;
    public DateTimeOffset? PostedAt { get; set; }
    public long?  PostedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public long   Version   { get; set; }

    public ICollection<VendorInvoiceLine> Lines { get; set; } = new List<VendorInvoiceLine>();

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("vi.not_draft", $"Cannot post Vendor Invoice in status {Status}.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("vi.no_docno", "DocNo is required when posting.");
        if (TotalAmount <= 0m)
            throw new DomainException("vi.invalid_amount", "TotalAmount must be > 0.");

        DocNo    = docNo;
        Status   = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

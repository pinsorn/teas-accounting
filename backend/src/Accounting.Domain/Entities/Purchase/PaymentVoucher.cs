using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Purchase;

/// <summary>
/// ใบสำคัญจ่าย (Payment Voucher). Carries expense lines + auto-calculated VAT input +
/// WHT (50 ทวิ). Sub-prefix = ExpenseCategory.CategoryCode → PV-RENT-NNNN.
/// </summary>
public class PaymentVoucher : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long PaymentVoucherId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }

    /// <summary>cont.79 — GL dimension: which Business Unit this spend belongs to. Required
    /// when Company.RequiresBusinessUnit; embedded in the doc number; stamped onto journal lines.</summary>
    public int? BusinessUnitId { get; set; }

    public string? DocNo { get; set; }
    public string  PrefixCode { get; set; } = "PV";
    public required string SubPrefix { get; set; }   // ExpenseCategory.CategoryCode

    public DateOnly DocDate { get; set; }
    public DateOnly PostingDate { get; set; }

    public long  VendorId { get; set; }
    public int   ExpenseCategoryId { get; set; }

    // Vendor snapshot (frozen)
    public string?  VendorTaxId         { get; set; }
    public string?  VendorBranchCode    { get; set; }
    public required string VendorName   { get; set; }
    public string?  VendorAddress       { get; set; }
    public CustomerType VendorType      { get; set; }   // INDIVIDUAL drives PND3 vs PND53

    // Payment
    public PaymentMethod PaymentMethod { get; set; }
    public string? ChequeNo { get; set; }
    public DateOnly? ChequeDate { get; set; }
    public long?  BankAccountId { get; set; }

    // Currency
    public string  CurrencyCode { get; set; } = "THB";
    public decimal ExchangeRate { get; set; } = 1m;

    // Amounts
    public decimal SubtotalAmount  { get; set; }
    public decimal VatAmount       { get; set; }
    public decimal WhtAmount       { get; set; }
    public decimal TotalPaid       { get; set; }   // = subtotal + vat - wht
    public decimal TotalAmountThb  { get; set; }

    /// <summary>Optional link — PV settles this Vendor Invoice (NULL = standalone PV).</summary>
    public long? VendorInvoiceId { get; set; }

    // Sprint 8.7 — Scenario A/B. When true (auto for foreign-no-VAT-D, or manual
    // for domestic auto-charge): gross-up — expense = subtotal + vat + wht,
    // cash = subtotal + vat (full), we owe WHT to RD separately.
    public bool SelfWithholdMode { get; set; }
    /// <summary>Auto-set when vendor is foreign without Thai VAT-D — Sprint 9 ภ.พ.36 generator scans this.</summary>
    public bool RequiresPnd36ReverseCharge { get; set; }

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    // ---- B2 SoD: Draft → Approved → Posted (CLAUDE.md §12.1) ----
    public long?  ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? PostedAt { get; set; }
    public long?  PostedBy { get; set; }

    public string? Description { get; set; }
    public string? Notes       { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public long   Version   { get; set; }

    // Sprint 13j-PURCH — original/copy print tracking (parity with Sales TaxInvoice).
    // OriginalPrintedAt stamped on the first original print; reprints are marked สำเนา.
    public DateTimeOffset? OriginalPrintedAt { get; set; }
    public int PrintCount { get; set; }

    public ICollection<PaymentVoucherLine> Lines { get; set; } = new List<PaymentVoucherLine>();

    /// <summary>
    /// Approval gate. cont.77 (Ham 2026-05-30) — approval is now **permission-based only**:
    /// any user holding <c>purchase.payment_voucher.approve</c> may approve, INCLUDING the
    /// creator (single-operator SME). The previous creator≠approver SoD rule (app check +
    /// DB CHECK ck_pv_sod) is removed; <c>ApprovedBy</c> is still recorded for the audit trail.
    /// </summary>
    public void MarkApproved(long approverUserId, DateTimeOffset approvedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("pv.not_draft", $"Cannot approve PV in status {Status}.");

        Status     = DocumentStatus.Approved;
        ApprovedBy = approverUserId;
        ApprovedAt = approvedAt;
    }

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Approved)
            throw new DomainException("pv.not_approved",
                $"PV must be Approved before Post (current: {Status}). B2 workflow: Draft → Approved → Posted.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("pv.no_docno", "DocNo is required when posting.");
        if (TotalPaid <= 0)
            throw new DomainException("pv.invalid_amount", "TotalPaid must be > 0.");

        DocNo = docNo;
        Status = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }
}

using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Domain.Tax;

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

    // 2026-06-12 (wht-grossup spec) — WHT base method when the payee won't be withheld.
    // RD treats tax paid on the payee's behalf as the payee's income → gross-up:
    //   DEDUCT           (เงื่อนไข 1 หัก ณ ที่จ่าย)   tax = r·net, netted off payment
    //   GROSS_UP_FOREVER (เงื่อนไข 2 ออกให้ตลอดไป)  income = net/(1−r), tax = r·income
    //   GROSS_UP_ONCE    (เงื่อนไข 3 ออกให้ครั้งเดียว) income = net·(1+r), tax = r·income
    // Kept in sync with SelfWithholdMode (true ⟺ mode ≠ DEDUCT).
    public string WhtPayerMode { get; set; } = WhtPayerModes.Deduct;

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

    /// <summary>M4 (MCP) — name of the API key that created this draft (TenantClaims.ApiKeyName).
    /// Value = the key name. Null for JWT/human creates. Lets the agent-approval dashboard
    /// count purchase drafts awaiting a human review + post.</summary>
    public string? CreatedViaApiKeyName { get; set; }

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
        Version++;
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
        Version++;
    }

    /// <summary>
    /// B1(b) (specs/fix-army-findings-2026-07-22.md) — escape hatch for a PV that can never
    /// Post (e.g. pv.wht_type_missing on an already-approved draft, or a legacy bad Draft that
    /// predates that guard). Draft OR Approved -&gt; Voided, terminal. Posted throws — immutable
    /// after Post stays absolute (Opus Tier-2 F2, 2026-07-25: Draft admitted too — Cancel() was
    /// Approved-only, but the new ApproveAsync re-assert (B1(a)) welds a legacy bad Draft shut
    /// with NO way to leave Draft — no PV update/delete endpoint exists — which would block
    /// PeriodCloseService forever; a Draft PV has no JE/DocNo either, so cancelling it is exactly
    /// as safe as cancelling an Approved one). Mirrors ExpenseClaim.Cancel()'s guard shape.
    /// DocNo is allocated at Post, so a cancelled Draft/Approved PV never wastes a number, and VI
    /// release is automatic (settlement is POST-only, so a Draft/Approved PV never touched its
    /// linked VI — the status flip alone lets CreateFromVendorInvoiceAsync's `Status != Voided`
    /// guard admit a fresh PV).
    /// </summary>
    public void Cancel()
    {
        if (Status != DocumentStatus.Draft && Status != DocumentStatus.Approved)
            throw new DomainException("pv.cannot_cancel",
                $"Cannot cancel a Payment Voucher in status {Status}; only a Draft or Approved PV may be cancelled.");
        Status = DocumentStatus.Voided;
        Version++;
    }
}

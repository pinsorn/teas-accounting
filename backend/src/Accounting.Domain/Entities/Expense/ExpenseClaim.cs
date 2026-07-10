using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Expense;

/// <summary>
/// Cycle C (specs/expense-claims.md) — employee expense reimbursement. Submitters key claims
/// in on behalf of an <see cref="Master.Employee"/> (no employee login). Draft -&gt; Submitted -&gt;
/// Approved -&gt; Paid, with Reject/Cancel side branches. Pay is self-contained cash disbursement
/// (Option A, ruled 2026-07-09): posts its own Journal Entry via the additive
/// <c>GlPostingService.PostExpenseClaimAsync</c> — never touches PaymentVoucher/Vendor/WHT
/// machinery (reimbursing an employee's out-of-pocket spend is not a withholding event).
/// </summary>
public class ExpenseClaim : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long ExpenseClaimId { get; set; }
    public int  CompanyId { get; set; }
    public int  BranchId  { get; set; }
    public int? BusinessUnitId { get; set; }

    public long EmployeeId { get; set; }

    public string? DocNo { get; set; }              // assigned at PAY only
    public string  PrefixCode { get; set; } = "EX";
    public string? SubPrefix  { get; set; }

    public DateOnly ClaimDate { get; set; }
    public string?  Title { get; set; }

    public ExpenseClaimStatus Status { get; set; } = ExpenseClaimStatus.Draft;

    public string? PaymentMethod  { get; set; }      // CASH | TRANSFER, set at pay
    public int?    BankAccountId  { get; set; }       // Cr source when TRANSFER (BankAccount.BankAccountId is int)

    public decimal SubtotalAmount { get; set; }
    public decimal VatAmount      { get; set; }
    public decimal TotalAmount    { get; set; }

    public long? JournalEntryId { get; set; }        // set at pay

    public string? RejectReason { get; set; }
    public string? Notes        { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?  CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?  UpdatedBy { get; set; }
    public long?  ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long?  PaidBy { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public long   Version   { get; set; }

    public ICollection<ExpenseClaimLine> Lines { get; set; } = new List<ExpenseClaimLine>();

    /// <summary>Draft/Rejected -&gt; Submitted. Locks editing.</summary>
    public void Submit()
    {
        if (Status != ExpenseClaimStatus.Draft && Status != ExpenseClaimStatus.Rejected)
            throw new DomainException("expense_claim.not_draft",
                $"Cannot submit an expense claim in status {Status}.");
        Status = ExpenseClaimStatus.Submitted;
        Version++;
    }

    /// <summary>Submitted -&gt; Approved. Permission-only (creator MAY self-approve — Ham
    /// ruling, mirrors PaymentVoucher's dropped SoD).</summary>
    public void Approve(long approverUserId, DateTimeOffset approvedAt)
    {
        if (Status != ExpenseClaimStatus.Submitted)
            throw new DomainException("expense_claim.not_submitted",
                $"Cannot approve an expense claim in status {Status}.");
        Status = ExpenseClaimStatus.Approved;
        ApprovedBy = approverUserId;
        ApprovedAt = approvedAt;
        Version++;
    }

    /// <summary>Submitted -&gt; Rejected. Returns to editable (re-submittable).</summary>
    public void Reject(string reason)
    {
        if (Status != ExpenseClaimStatus.Submitted)
            throw new DomainException("expense_claim.not_submitted",
                $"Cannot reject an expense claim in status {Status}.");
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("expense_claim.reject_reason_required",
                "A reject reason is required.");
        Status = ExpenseClaimStatus.Rejected;
        RejectReason = reason;
        Version++;
    }

    /// <summary>Approved -&gt; Paid. Terminal — allocates DocNo/PaymentMethod up front so
    /// GlPostingService.PostExpenseClaimAsync (re-queried on the SAME DbContext, same tx) sees
    /// them. JournalEntryId is stamped by the caller AFTER the GL post returns the JournalId
    /// (mirrors PaymentVoucherService.PostAsync: MarkPosted first, GL post second, one tx).</summary>
    public void MarkPaid(string docNo, string paymentMethod, int? bankAccountId, long userId, DateTimeOffset paidAt)
    {
        if (Status != ExpenseClaimStatus.Approved)
            throw new DomainException("expense_claim.not_approved",
                $"Expense claim must be Approved before Pay (current: {Status}).");
        DocNo = docNo;
        PaymentMethod = paymentMethod;
        BankAccountId = bankAccountId;
        Status = ExpenseClaimStatus.Paid;
        PaidAt = paidAt;
        PaidBy = userId;
        Version++;
    }

    /// <summary>Draft/Rejected -&gt; Cancelled. Only legal pre-GL (no JE was ever posted).</summary>
    public void Cancel()
    {
        if (Status != ExpenseClaimStatus.Draft && Status != ExpenseClaimStatus.Rejected)
            throw new DomainException("expense_claim.cannot_cancel",
                $"Cannot cancel an expense claim in status {Status}.");
        Status = ExpenseClaimStatus.Cancelled;
        Version++;
    }
}

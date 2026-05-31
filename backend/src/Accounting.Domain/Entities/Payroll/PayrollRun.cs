using Accounting.Domain.Common;
using Accounting.Domain.Enums;

namespace Accounting.Domain.Entities.Payroll;

/// <summary>
/// A monthly payroll run (one per company per period). Aggregates the per-employee
/// <see cref="Payslip"/> rows and carries the run totals + lifecycle. Lifecycle mirrors the
/// Payment-Voucher SoD chain (Draft → Approved → Posted) plus a final Paid stamp once the
/// salary transfer has happened. The doc number (PR-prefix) is assigned ONLY on Post (§4.3) and
/// the run is IMMUTABLE after Post — corrections are a separate reversing run, never an edit
/// (§4.2 immutability discipline, applied to payroll by analogy).
/// </summary>
public class PayrollRun : ITenantOwned, IAuditable, IConcurrencyVersioned
{
    public long PayrollRunId { get; set; }
    public int  CompanyId    { get; set; }
    public int  BranchId     { get; set; }

    /// <summary>Pay period in <c>yyyymm</c> (CE year, e.g. <c>202605</c>). The Thai PIT year is
    /// the calendar year — YTD aggregation keys on the <c>yyyy</c> prefix.</summary>
    public required string PeriodYearMonth { get; set; }

    /// <summary>Date the salaries are/were paid (drives the payment-evidence document).</summary>
    public DateOnly PayDate { get; set; }

    public string? DocNo      { get; set; }
    public string  PrefixCode { get; set; } = "PR";

    public DocumentStatus Status { get; set; } = DocumentStatus.Draft;

    // ---- Run totals (sum of payslips; money = decimal 4dp) ----
    public decimal TotalGrossTaxable    { get; set; }
    public decimal TotalGrossNonTaxable { get; set; }
    public decimal TotalPit             { get; set; }
    public decimal TotalSsoEmployee     { get; set; }
    public decimal TotalSsoEmployer     { get; set; }
    public decimal TotalOtherDeductions { get; set; }
    public decimal TotalNet             { get; set; }

    /// <summary>The posted GL JournalEntry (set on Post).</summary>
    public long? JournalId { get; set; }

    // ---- Lifecycle stamps ----
    public long?           ApprovedBy { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public long?           PostedBy   { get; set; }
    public DateTimeOffset? PostedAt   { get; set; }
    public long?           PaidBy     { get; set; }
    public DateTimeOffset? PaidAt     { get; set; }

    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long?          CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long?          UpdatedBy { get; set; }
    public long           Version   { get; set; }

    public ICollection<Payslip> Payslips { get; set; } = new List<Payslip>();

    /// <summary>True once the salary transfer has been stamped (sub-state of Posted).</summary>
    public bool IsPaid => PaidAt is not null;

    /// <summary>Recompute the header totals from the payslip rows.</summary>
    public void RecalculateTotals()
    {
        TotalGrossTaxable    = Payslips.Sum(p => p.GrossTaxable);
        TotalGrossNonTaxable = Payslips.Sum(p => p.GrossNonTaxable);
        TotalPit             = Payslips.Sum(p => p.PitWithheld);
        TotalSsoEmployee     = Payslips.Sum(p => p.SsoEmployee);
        TotalSsoEmployer     = Payslips.Sum(p => p.SsoEmployer);
        TotalOtherDeductions = Payslips.Sum(p => p.OtherDeductions);
        TotalNet             = Payslips.Sum(p => p.NetPay);
    }

    public void EnsureValid()
    {
        if (!IsValidPeriod(PeriodYearMonth))
            throw new DomainException("payroll.period_invalid",
                "Pay period must be yyyymm with month 01–12 (CE year).");
        if (Payslips.Count == 0)
            throw new DomainException("payroll.no_employees",
                "A payroll run must have at least one employee payslip.");
    }

    public static bool IsValidPeriod(string? period)
    {
        if (period is null || period.Length != 6 || !period.All(char.IsDigit)) return false;
        var month = int.Parse(period[4..]);
        return month is >= 1 and <= 12;
    }

    public void MarkApproved(long approverUserId, DateTimeOffset approvedAt)
    {
        if (Status != DocumentStatus.Draft)
            throw new DomainException("payroll.not_draft", $"Cannot approve run in status {Status}.");
        Status     = DocumentStatus.Approved;
        ApprovedBy = approverUserId;
        ApprovedAt = approvedAt;
    }

    public void MarkPosted(string docNo, long userId, DateTimeOffset postedAt)
    {
        if (Status != DocumentStatus.Approved)
            throw new DomainException("payroll.not_approved",
                $"Run must be Approved before Post (current: {Status}). Workflow: Draft → Approved → Posted.");
        if (string.IsNullOrEmpty(docNo))
            throw new DomainException("payroll.no_docno", "DocNo is required when posting.");

        DocNo    = docNo;
        Status   = DocumentStatus.Posted;
        PostedAt = postedAt;
        PostedBy = userId;
    }

    public void MarkPaid(long userId, DateTimeOffset paidAt)
    {
        if (Status != DocumentStatus.Posted)
            throw new DomainException("payroll.not_posted", $"Run must be Posted before Pay (current: {Status}).");
        if (PaidAt is not null)
            throw new DomainException("payroll.already_paid", "Run is already marked paid.");
        PaidBy = userId;
        PaidAt = paidAt;
    }
}

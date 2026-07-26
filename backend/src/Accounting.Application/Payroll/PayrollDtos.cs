using Accounting.Domain.Entities.Payroll;
using Accounting.Domain.Enums;
using FluentValidation;
using System.Text.Json.Serialization;

namespace Accounting.Application.Payroll;

// Payroll P-C — PayrollRun → Payslip contract. The run auto-builds a payslip for every employee
// active in the period; salary is prorated by calendar days employed within the period
// (SalaryProration, O8) — a mid-month hire/termination pays only for days worked, full months are
// untouched. PIT = ม.50(1) projected-annual via ThaiPitCalculator; SSO/allowances from config — both
// computed from the prorated gross.

public sealed record CreatePayrollRunRequest(
    string PeriodYearMonth,   // yyyymm (CE year)
    DateOnly PayDate,
    string? Notes);
public sealed record PayPayrollRunRequest(int? BankAccountId = null);
public sealed record PayrollDeductionLine(long EmployeeId, decimal Amount, string Reason);
public sealed record UpdatePayrollDeductionsRequest(IReadOnlyList<PayrollDeductionLine> Deductions)
{
    [JsonIgnore]
    public long PayrollRunId { get; init; }
}


public sealed record PayslipDto(
    long PayslipId, long EmployeeId, string EmployeeCode, string EmployeeName, string NationalId,
    decimal GrossTaxable, decimal GrossNonTaxable, decimal PitWithheld,
    decimal SsoEmployee, decimal SsoEmployer, decimal OtherDeductions, decimal NetPay,
    decimal YtdIncome, decimal YtdPit);

public sealed record PayrollRunListItem(
    long PayrollRunId, string PeriodYearMonth, DateOnly PayDate, string Status, string? DocNo,
    int EmployeeCount, decimal TotalNet, bool IsPaid);

public sealed record PayrollRunDetail(
    long PayrollRunId, string PeriodYearMonth, DateOnly PayDate, string Status, string? DocNo,
    decimal TotalGrossTaxable, decimal TotalGrossNonTaxable, decimal TotalPit,
    decimal TotalSsoEmployee, decimal TotalSsoEmployer, decimal TotalOtherDeductions, decimal TotalNet,
    long? JournalId,
    DateTimeOffset? ApprovedAt, DateTimeOffset? PostedAt, DateTimeOffset? PaidAt,
    string? Notes,
    IReadOnlyList<PayslipDto> Payslips);

public interface IPayrollRunService
{
    /// <summary>Build a DRAFT run + a payslip per employee active in the period. No doc number yet.</summary>
    Task<long> CreateDraftAsync(CreatePayrollRunRequest req, CancellationToken ct);
    Task UpdateDeductionsAsync(long id, UpdatePayrollDeductionsRequest req, CancellationToken ct);
    Task ApproveAsync(long id, CancellationToken ct);
    /// <summary>Approved → Posted: assign the PR doc number + post the balanced GL JV. Immutable after.</summary>
    Task PostAsync(long id, CancellationToken ct);
    Task PayAsync(long id, PayPayrollRunRequest req, CancellationToken ct);
    /// <summary>Delete a DRAFT run (never a posted one).</summary>
    Task DeleteDraftAsync(long id, CancellationToken ct);
    Task<IReadOnlyList<PayrollRunListItem>> ListAsync(CancellationToken ct);
    Task<PayrollRunDetail?> GetAsync(long id, CancellationToken ct);
}

/// <summary>P-D — per-employee payment-evidence / payslip PDF (QuestPDF).</summary>
public interface IPayslipPdfService
{
    /// <summary>One payslip PDF for the given employee in the run.</summary>
    Task<byte[]> BuildAsync(long runId, long employeeId, CancellationToken ct);
    /// <summary>All payslips in the run, zipped (one PDF per employee). Returns the zip + a filename.</summary>
    Task<(byte[] Content, string FileName)> BuildRunZipAsync(long runId, CancellationToken ct);
}

public sealed class CreatePayrollRunValidator : AbstractValidator<CreatePayrollRunRequest>
{
    public CreatePayrollRunValidator()
    {
        RuleFor(x => x.PeriodYearMonth)
            .Must(PayrollRun.IsValidPeriod).WithMessage("validation.period");
    }
}

public sealed class UpdatePayrollDeductionsValidator : AbstractValidator<UpdatePayrollDeductionsRequest>
{
    public UpdatePayrollDeductionsValidator(IPayrollRunService payroll)
    {
        RuleFor(x => x.Deductions).NotNull().WithMessage("กรุณาระบุรายการหักเงินเดือน");
        RuleForEach(x => x.Deductions).ChildRules(line =>
        {
            line.RuleFor(x => x.EmployeeId).GreaterThan(0).WithMessage("รหัสพนักงานไม่ถูกต้อง");
            line.RuleFor(x => x.Amount).GreaterThan(0m).WithMessage("จำนวนเงินหักต้องมากกว่า 0");
            line.RuleFor(x => x.Reason).NotEmpty().MaximumLength(500)
                .WithMessage("กรุณาระบุเหตุผลการหักเงินไม่เกิน 500 ตัวอักษร");
        });

        RuleFor(x => x).CustomAsync(async (req, context, ct) =>
        {
            if (req.Deductions is null) return;
            if (req.Deductions.GroupBy(x => x.EmployeeId).Any(g => g.Count() > 1))
            {
                context.AddFailure(nameof(req.Deductions), "พนักงานแต่ละคนต้องมีรายการหักเพียงรายการเดียว");
                return;
            }

            var run = await payroll.GetAsync(req.PayrollRunId, ct);
            if (run is null)
            {
                context.AddFailure(nameof(req.PayrollRunId), "ไม่พบรายการเงินเดือน");
                return;
            }
            if (!string.Equals(run.Status, DocumentStatus.Draft.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                context.AddFailure(nameof(req.Deductions), "แก้ไขรายการหักได้เฉพาะเงินเดือนสถานะร่างเท่านั้น");
                return;
            }

            foreach (var line in req.Deductions.Where(x => x.EmployeeId > 0 && x.Amount > 0m))
            {
                var slip = run.Payslips.SingleOrDefault(x => x.EmployeeId == line.EmployeeId);
                if (slip is null)
                {
                    context.AddFailure(nameof(req.Deductions), $"ไม่พบพนักงานรหัส {line.EmployeeId} ในรายการเงินเดือนนี้");
                    continue;
                }

                var maximum = slip.GrossTaxable + slip.GrossNonTaxable - slip.PitWithheld - slip.SsoEmployee;
                if (line.Amount > maximum)
                    context.AddFailure(nameof(req.Deductions),
                        $"จำนวนเงินหักของพนักงาน {slip.EmployeeCode} ต้องไม่เกินเงินได้สุทธิหลังภาษีและประกันสังคม {maximum:N2} บาท");
            }
        });
    }
}

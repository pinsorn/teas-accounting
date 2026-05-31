using Accounting.Domain.Entities.Payroll;
using FluentValidation;

namespace Accounting.Application.Payroll;

// Payroll P-C — PayrollRun → Payslip contract. The run auto-builds a payslip for every employee
// active in the period; v1 takes no per-employee input (regular salary only). PIT = ม.50(1)
// projected-annual via ThaiPitCalculator; SSO/allowances from config.

public sealed record CreatePayrollRunRequest(
    string PeriodYearMonth,   // yyyymm (CE year)
    DateOnly PayDate,
    string? Notes);

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
    Task ApproveAsync(long id, CancellationToken ct);
    /// <summary>Approved → Posted: assign the PR doc number + post the balanced GL JV. Immutable after.</summary>
    Task PostAsync(long id, CancellationToken ct);
    Task PayAsync(long id, CancellationToken ct);
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

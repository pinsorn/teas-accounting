using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Payroll;
using Accounting.Domain.Enums;
using Accounting.Domain.Payroll;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Payroll;

/// <summary>
/// Payroll P-C — builds + posts the monthly payroll run. PIT uses the pure ม.50(1) projected-annual
/// engine (<see cref="ThaiPitCalculator"/>): months-remaining = 13 − period-month (NOT hire-date
/// arithmetic — a mid-year joiner is handled by YTD=0). YTD aggregates PRIOR POSTED runs in the same
/// Thai-PIT CALENDAR year. SSO + ค่าลดหย่อน come from config (§4.6). The run is immutable after Post
/// (corrections = a reversing run, never an edit — §4.2 by analogy).
/// </summary>
public sealed class PayrollRunService(
    AccountingDbContext db,
    ITenantContext tenant,
    IClock clock,
    INumberSequenceService numbers,
    IGlPostingService gl,
    IActivityRecorder activity,
    IOptions<SsoOptions> ssoOptions,
    IOptions<PayrollAllowanceOptions> allowanceOptions) : IPayrollRunService
{
    private const string PrefixCode = "PR";
    private const string EntityType = "PayrollRun";
    private const string Module = "payroll";
    private readonly SsoOptions _sso = ssoOptions.Value;
    private readonly PayrollAllowanceRates _allowances = new(
        allowanceOptions.Value.Personal, allowanceOptions.Value.Spouse, allowanceOptions.Value.Child);

    public async Task<long> CreateDraftAsync(CreatePayrollRunRequest req, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
        if (!PayrollRun.IsValidPeriod(req.PeriodYearMonth))
            throw new DomainException("payroll.period_invalid", "Pay period must be yyyymm with month 01–12.");
        if (await db.PayrollRuns.AnyAsync(r => r.PeriodYearMonth == req.PeriodYearMonth, ct))
            throw new DomainException("payroll.duplicate_period",
                $"A payroll run already exists for period {req.PeriodYearMonth}.");

        var year  = int.Parse(req.PeriodYearMonth[..4]);
        var month = int.Parse(req.PeriodYearMonth[4..]);
        var monthsRemaining = 13 - month;               // Jan→12 … Dec→1 (ม.50(1) spread)
        var periodStart = new DateOnly(year, month, 1);
        var periodEnd   = periodStart.AddMonths(1).AddDays(-1);

        // Paid this period = ACTIVE master record AND employment overlaps the period (hired on/before
        // period end AND not terminated before it starts). IsActive is required because soft-deactivate
        // (EmployeeService.DeactivateAsync) only flips IsActive — it does NOT set a TerminationDate, so
        // a date-only filter would keep paying a deactivated employee. Convention: to off-board, set
        // TerminationDate (keeps them on the final overlapping run) THEN deactivate after that run.
        var employees = await db.Employees
            .Where(e => e.IsActive
                     && e.HireDate <= periodEnd
                     && (e.TerminationDate == null || e.TerminationDate >= periodStart))
            .OrderBy(e => e.EmployeeCode)
            .ToListAsync(ct);
        if (employees.Count == 0)
            throw new DomainException("payroll.no_employees", "No employees are active in this period.");

        var ytd = await LoadYtdAsync(req.PeriodYearMonth, ct);
        var schedule = PitSchedule.Current();

        var run = new PayrollRun
        {
            CompanyId       = tenant.CompanyId,
            BranchId        = tenant.BranchId,
            PeriodYearMonth = req.PeriodYearMonth,
            PayDate         = req.PayDate,
            PrefixCode      = PrefixCode,
            Status          = DocumentStatus.Draft,
            Notes           = req.Notes,
            CreatedAt       = clock.UtcNow,
        };

        foreach (var e in employees)
        {
            var (priorIncome, priorPit) = ytd.GetValueOrDefault(e.EmployeeId);

            var ssoEmp = e.SsoApplicable
                ? SsoContribution.Monthly(e.BaseSalary, _sso.Rate, _sso.WageFloor, _sso.WageCeiling)
                : 0m;
            var ssoAllowance = e.SsoApplicable
                ? Math.Min(ssoEmp * 12m, _sso.MaxAllowanceForPit)
                : 0m;

            var annualAllowances = _allowances.Annual(
                e.MaritalStatus, e.SpouseHasIncome, e.ChildrenCount, ssoAllowance);

            var thisMonthTaxable = e.BaseSalary;
            var projected = ThaiPitCalculator.ProjectAnnualIncome(priorIncome, thisMonthTaxable, monthsRemaining);
            var pit = ThaiPitCalculator.MonthlyWithholding(
                projected, annualAllowances, priorPit, monthsRemaining, schedule);

            var slip = new Payslip
            {
                CompanyId       = tenant.CompanyId,
                EmployeeId      = e.EmployeeId,
                EmployeeCode    = e.EmployeeCode,
                EmployeeName    = ComposeName(e),
                NationalId      = e.NationalId,
                AddressText     = ComposeAddress(e),
                BankName        = e.BankName,
                BankAccountNo   = e.BankAccountNo,
                BankAccountName = e.BankAccountName,
                GrossTaxable    = thisMonthTaxable,
                GrossNonTaxable = 0m,
                PitWithheld     = pit,
                SsoEmployee     = ssoEmp,
                SsoEmployer     = ssoEmp,            // employer matches the employee (ม.46)
                OtherDeductions = 0m,
                YtdIncome       = priorIncome + thisMonthTaxable,
                YtdPit          = priorPit + pit,
            };
            slip.ComputeNet();
            run.Payslips.Add(slip);
        }

        run.RecalculateTotals();
        run.EnsureValid();
        db.PayrollRuns.Add(run);
        await db.SaveChangesAsync(ct);              // assign identity for the audit row

        activity.Record(EntityType, run.PayrollRunId, null, run.CompanyId,
            "Created", toStatus: "Draft", note: $"period:{run.PeriodYearMonth}", module: Module);
        await db.SaveChangesAsync(ct);
        return run.PayrollRunId;
    }

    public async Task ApproveAsync(long id, CancellationToken ct)
    {
        var run = await LoadAsync(id, ct);
        run.MarkApproved(tenant.UserId ?? 0, clock.UtcNow);
        activity.Record(EntityType, run.PayrollRunId, run.DocNo, run.CompanyId,
            "Approved", fromStatus: "Draft", toStatus: "Approved", module: Module);
        await db.SaveChangesAsync(ct);
    }

    public async Task PostAsync(long id, CancellationToken ct)
    {
        var run = await LoadAsync(id, ct);
        if (run.Status != DocumentStatus.Approved)
            throw new DomainException("payroll.not_approved",
                $"Run must be Approved before Post (current: {run.Status}).");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var docNo = await numbers.NextAsync(run.CompanyId, run.BranchId, PrefixCode, subPrefix: null, run.PayDate, ct);
        run.MarkPosted(docNo.Value, tenant.UserId ?? 0, clock.UtcNow);
        activity.Record(EntityType, run.PayrollRunId, run.DocNo, run.CompanyId,
            "Posted", fromStatus: "Approved", toStatus: "Posted", module: Module);
        await db.SaveChangesAsync(ct);              // persist docNo + status + audit before GL reads it

        run.JournalId = await gl.PostPayrollRunAsync(run.PayrollRunId, ct);
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);
    }

    public async Task PayAsync(long id, CancellationToken ct)
    {
        var run = await LoadAsync(id, ct);
        run.MarkPaid(tenant.UserId ?? 0, clock.UtcNow);
        activity.Record(EntityType, run.PayrollRunId, run.DocNo, run.CompanyId,
            "Paid", fromStatus: "Posted", toStatus: "Paid", module: Module);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteDraftAsync(long id, CancellationToken ct)
    {
        var run = await LoadAsync(id, ct);
        if (run.Status != DocumentStatus.Draft)
            throw new DomainException("payroll.not_draft", "Only a draft run can be deleted.");
        activity.Record(EntityType, run.PayrollRunId, run.DocNo, run.CompanyId,
            "Deleted", fromStatus: "Draft", module: Module);
        db.PayrollRuns.Remove(run);                 // payslips cascade; audit row persists (no FK)
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PayrollRunListItem>> ListAsync(CancellationToken ct) =>
        await db.PayrollRuns.AsNoTracking()
            .OrderByDescending(r => r.PeriodYearMonth)
            .Select(r => new PayrollRunListItem(
                r.PayrollRunId, r.PeriodYearMonth, r.PayDate,
                r.Status.ToString().ToUpperInvariant(), r.DocNo,
                r.Payslips.Count, r.TotalNet, r.PaidAt != null))
            .ToListAsync(ct);

    public async Task<PayrollRunDetail?> GetAsync(long id, CancellationToken ct) =>
        await db.PayrollRuns.AsNoTracking()
            .Where(r => r.PayrollRunId == id)
            .Select(r => new PayrollRunDetail(
                r.PayrollRunId, r.PeriodYearMonth, r.PayDate,
                r.Status.ToString().ToUpperInvariant(), r.DocNo,
                r.TotalGrossTaxable, r.TotalGrossNonTaxable, r.TotalPit,
                r.TotalSsoEmployee, r.TotalSsoEmployer, r.TotalOtherDeductions, r.TotalNet,
                r.JournalId, r.ApprovedAt, r.PostedAt, r.PaidAt, r.Notes,
                r.Payslips.OrderBy(p => p.EmployeeCode).Select(p => new PayslipDto(
                    p.PayslipId, p.EmployeeId, p.EmployeeCode, p.EmployeeName, p.NationalId,
                    p.GrossTaxable, p.GrossNonTaxable, p.PitWithheld,
                    p.SsoEmployee, p.SsoEmployer, p.OtherDeductions, p.NetPay,
                    p.YtdIncome, p.YtdPit)).ToList()))
            .FirstOrDefaultAsync(ct);

    // ---- helpers ----

    private async Task<PayrollRun> LoadAsync(long id, CancellationToken ct) =>
        await db.PayrollRuns.Include(r => r.Payslips).FirstOrDefaultAsync(r => r.PayrollRunId == id, ct)
            ?? throw new DomainException("payroll.not_found", $"Payroll run {id} not found.");

    /// <summary>YTD income/PIT per employee from PRIOR POSTED runs in the same calendar year.</summary>
    private async Task<Dictionary<long, (decimal Income, decimal Pit)>> LoadYtdAsync(
        string period, CancellationToken ct)
    {
        var year = period[..4];
        var currentPeriod = int.Parse(period);
        var rows = await db.Payslips.AsNoTracking()
            .Where(p => p.Run!.Status == DocumentStatus.Posted && p.Run.PeriodYearMonth.StartsWith(year))
            .Select(p => new { p.EmployeeId, p.Run!.PeriodYearMonth, p.GrossTaxable, p.PitWithheld })
            .ToListAsync(ct);
        return rows
            .Where(x => int.Parse(x.PeriodYearMonth) < currentPeriod)   // prior months this year
            .GroupBy(x => x.EmployeeId)
            .ToDictionary(
                g => g.Key,
                g => (g.Sum(x => x.GrossTaxable), g.Sum(x => x.PitWithheld)));
    }

    private static string ComposeName(Domain.Entities.Master.Employee e) =>
        ((e.TitleTh ?? "") + e.FirstNameTh + " " + e.LastNameTh).Trim();

    private static string? ComposeAddress(Domain.Entities.Master.Employee e)
    {
        var parts = new[]
        {
            e.AddressNo, e.Moo is { Length: > 0 } m ? $"หมู่ {m}" : null,
            e.Soi is { Length: > 0 } s ? $"ซ.{s}" : null,
            e.Street is { Length: > 0 } st ? $"ถ.{st}" : null,
            e.SubDistrict, e.District, e.Province, e.PostalCode,
        }.Where(p => !string.IsNullOrWhiteSpace(p));
        var text = string.Join(" ", parts).Trim();
        return text.Length == 0 ? null : text;
    }
}

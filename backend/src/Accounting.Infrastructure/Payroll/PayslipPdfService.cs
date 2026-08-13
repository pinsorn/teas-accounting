using System.IO;
using System.IO.Compression;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Payroll;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Payroll;

/// <summary>Payroll P-D — renders the per-employee payment-evidence/payslip PDF from a run's
/// payslips (immutable snapshot) + the employer (Company) header. Tenant-scoped via the EF filter.</summary>
public sealed class PayslipPdfService(AccountingDbContext db) : IPayslipPdfService
{
    private static readonly string[] ThaiMonths =
    {
        "", "มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
        "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม",
    };

    public async Task<byte[]> BuildAsync(long runId, long employeeId, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, ct);
        var slip = run.Payslips.FirstOrDefault(p => p.EmployeeId == employeeId)
            ?? throw new DomainException("payroll.payslip_not_found",
                $"No payslip for employee {employeeId} in run {runId}.");
        return PayslipPdf.Render(await BuildModelAsync(run, slip, ct));
    }

    public async Task<(byte[] Content, string FileName)> BuildRunZipAsync(long runId, CancellationToken ct)
    {
        var run = await LoadRunAsync(runId, ct);
        if (run.Payslips.Count == 0)
            throw new DomainException("payroll.no_employees", "Run has no payslips to export.");

        var employer = await EmployerAsync(run.CompanyId, ct);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var slip in run.Payslips.OrderBy(p => p.EmployeeCode))
            {
                var pdf = PayslipPdf.Render(BuildModel(run, slip, employer));
                var entry = zip.CreateEntry($"payslip-{slip.EmployeeCode}.pdf", CompressionLevel.Optimal);
                await using var s = entry.Open();
                await s.WriteAsync(pdf, ct);
            }
        }
        return (ms.ToArray(), $"payslips-{run.PeriodYearMonth}.zip");
    }

    // R2/WP-4 §10 E3 (Ham, PRODUCT) — SEPARATE, INDEPENDENTLY-REVERTABLE from the H13 filing guard in
    // Pnd1FilingService/SsoFilingService: a payslip is an internal document, not an RD/SSO filing, so
    // previewing one after Approval is normal practice — only an unapproved DRAFT is refused. Note an
    // Approved (not yet Posted) run's DocNo is still null (BuildModel below prints run.DocNo) — flagged
    // per the spec, not fixed here.
    private async Task<PayrollRun> LoadRunAsync(long runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().Include(r => r.Payslips)
                .FirstOrDefaultAsync(r => r.PayrollRunId == runId, ct)
            ?? throw new DomainException("payroll.not_found", $"Payroll run {runId} not found.");
        if (run.Status == DocumentStatus.Draft)
            throw new DomainException("payroll.not_approved_for_payslip",
                $"งวดเงินเดือน {run.PeriodYearMonth} ยังเป็นฉบับร่าง (สถานะ {run.Status}) — " +
                $"ต้องอนุมัติก่อนจึงจะพิมพ์สลิปเงินเดือนได้ " +
                $"[Payroll run {run.PeriodYearMonth} is still Draft; a payslip requires at least an " +
                "Approved run.]");
        return run;
    }

    private async Task<(string Name, string TaxId, string? Address)> EmployerAsync(int companyId, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId, ct);
        var addr = string.Join(" ", new[] { c?.AddressTh, c?.SubDistrict, c?.District, c?.Province, c?.PostalCode }
            .Where(p => !string.IsNullOrWhiteSpace(p)));
        return (c?.NameTh ?? "-", c?.TaxId ?? "-", addr.Length == 0 ? null : addr);
    }

    private async Task<PayslipPdfModel> BuildModelAsync(PayrollRun run, Payslip slip, CancellationToken ct)
        => BuildModel(run, slip, await EmployerAsync(run.CompanyId, ct));

    private static PayslipPdfModel BuildModel(
        PayrollRun run, Payslip slip, (string Name, string TaxId, string? Address) employer)
    {
        var month = int.Parse(run.PeriodYearMonth[4..]);
        var year = int.Parse(run.PeriodYearMonth[..4]) + 543;       // CE → พ.ศ. at the print boundary
        var periodThai = $"{ThaiMonths[month]} {year}";
        var payDateThai = $"{run.PayDate.Day:00}/{run.PayDate.Month:00}/{run.PayDate.Year + 543}";

        return new PayslipPdfModel(
            employer.Name, employer.TaxId, employer.Address,
            periodThai, payDateThai, run.DocNo,
            slip.EmployeeCode, slip.EmployeeName, slip.NationalId, slip.AddressText,
            slip.GrossTaxable, slip.GrossNonTaxable, slip.PitWithheld, slip.SsoEmployee,
            slip.SsoEmployer, slip.OtherDeductions, slip.OtherDeductionsReason, slip.NetPay,
            slip.YtdIncome, slip.YtdPit,
            slip.BankName, slip.BankAccountNo, slip.BankAccountName,
            BahtText.Of(slip.NetPay));
    }
}

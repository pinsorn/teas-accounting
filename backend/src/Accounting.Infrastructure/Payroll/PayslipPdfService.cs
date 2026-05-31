using System.IO;
using System.IO.Compression;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Payroll;
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

    private async Task<PayrollRun> LoadRunAsync(long runId, CancellationToken ct) =>
        await db.PayrollRuns.AsNoTracking().Include(r => r.Payslips)
            .FirstOrDefaultAsync(r => r.PayrollRunId == runId, ct)
        ?? throw new DomainException("payroll.not_found", $"Payroll run {runId} not found.");

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
            slip.SsoEmployer, slip.OtherDeductions, slip.NetPay,
            slip.YtdIncome, slip.YtdPit,
            slip.BankName, slip.BankAccountNo, slip.BankAccountName,
            BahtText.Of(slip.NetPay));
    }
}

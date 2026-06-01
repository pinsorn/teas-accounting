using Accounting.Application.Abstractions;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Payroll;

/// <summary>Builds the สปส.1-10 (Social-Security ม.33 monthly contribution) model from a posted
/// <see cref="Domain.Entities.Payroll.PayrollRun"/> — channel-independent aggregation. The employer
/// header comes from <c>CompanyProfile</c> (registered identity/address), same as ภ.ง.ด.1. The
/// contributory wage per insured person is derived from the stored employee contribution so it is
/// audit-consistent with the posted payslip (already clamped to the SSO floor/ceiling at draft).
/// </summary>
public sealed class SsoFilingService(AccountingDbContext db, IOptions<SsoOptions> ssoOptions) : ISsoFilingService
{
    private readonly SsoOptions _sso = ssoOptions.Value;

    public async Task<SsoMonthlyModel> BuildMonthlyAsync(long runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().Include(r => r.Payslips)
                .FirstOrDefaultAsync(r => r.PayrollRunId == runId, ct)
            ?? throw new DomainException("payroll.not_found", $"Payroll run {runId} not found.");
        if (run.Payslips.Count == 0)
            throw new DomainException("payroll.no_employees", "Run has no payslips.");

        // The registered identity/address lives on master.company_profile, NOT master.companies.
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == run.CompanyId, ct);
        var prof = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == run.CompanyId, ct);

        var month = int.Parse(run.PeriodYearMonth[4..]);
        var yearCe = int.Parse(run.PeriodYearMonth[..4]);

        // Only insured persons (a contribution was withheld) appear on สปส.1-10; an employee whose
        // SsoApplicable=false carries a zero contribution and is correctly excluded.
        var insured = run.Payslips.Where(p => p.SsoEmployee > 0m).Select(p => p.EmployeeId).ToList();
        var info = await EmployeeInfoAsync(insured, ct);

        var lines = run.Payslips
            .Where(p => p.SsoEmployee > 0m)
            .OrderBy(p => p.EmployeeCode)
            .Select(p =>
            {
                var (title, first, last, ssoNo) = ResolveInfo(info, p.EmployeeId, p.EmployeeName, p.NationalId);
                // ค่าจ้างที่จ่ายจริง — the ACTUAL (un-capped) wage goes in the wage column; the ฿1,650/฿15,000
                // clamp lives only in the contribution (already posted as SsoEmployee). v1 is salary-only so
                // the actual wage = the payslip's gross taxable.
                return new SsoLine(ssoNo, p.NationalId, title, first, last,
                    p.GrossTaxable, p.SsoEmployee, p.SsoEmployer);
            })
            .ToList();

        return new SsoMonthlyModel(
            EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? "",
            BranchCode: prof?.BranchCode ?? "00000",
            EmployerName: prof?.LegalName ?? c?.NameTh ?? "",
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor, Village: prof?.RegVillage,
            HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo, Soi: prof?.RegSoi, Street: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            PeriodMonth: month, PeriodYearBE: yearCe + 543, PeriodYearCE: yearCe,
            PayDate: run.PayDate,
            // Per-tenant SSO employer account lives on CompanyProfile; config is a legacy fallback.
            EmployerAccountNo: string.IsNullOrWhiteSpace(prof?.SsoEmployerAccountNo)
                ? _sso.EmployerAccountNo : prof!.SsoEmployerAccountNo,
            Lines: lines);
    }

    public async Task<(byte[] Content, string FileName)> BuildMonthlyFileAsync(long runId, CancellationToken ct)
    {
        var model = await BuildMonthlyAsync(runId, ct);
        return (SpsBatchFormat.BuildBytes(model), SpsBatchFormat.FileName(model));
    }

    // EmployeeId → (คำนำหน้า, ชื่อ, ชื่อสกุล, เลขประกันสังคม) from the master — kept split because
    // the สปส.1-10 detail record holds title/first/last in separate fields.
    private async Task<Dictionary<long, (string Title, string First, string Last, string? Sso)>> EmployeeInfoAsync(
        IEnumerable<long> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        return await db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId))
            .ToDictionaryAsync(
                e => e.EmployeeId,
                e => (e.TitleTh ?? "", e.FirstNameTh, e.LastNameTh, e.SsoNumber),
                ct);
    }

    // Master split + SSO number when available; else split the snapshot name and fall back to the
    // national id for the SSO number (เลขประกันสังคม commonly equals the 13-digit PIN).
    private static (string Title, string First, string Last, string Sso) ResolveInfo(
        Dictionary<long, (string Title, string First, string Last, string? Sso)> map,
        long employeeId, string snapshotFullName, string nationalId)
    {
        if (map.TryGetValue(employeeId, out var n))
            return (n.Title, n.First, n.Last, string.IsNullOrWhiteSpace(n.Sso) ? nationalId : n.Sso!);
        var full = (snapshotFullName ?? "").Trim();
        var i = full.LastIndexOf(' ');
        var (first, last) = i <= 0 ? (full, "") : (full[..i].Trim(), full[(i + 1)..].Trim());
        return ("", first, last, nationalId);
    }
}

using Accounting.Application.Abstractions;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
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
        // R2/H13 — this is the SHARED loader for all three สปส.1-10 surfaces: BuildMonthlyFileAsync,
        // BuildMonthlyPdfAsync, and the sso-schedule on-screen JSON (PayrollEndpoints.cs:131-134, which
        // calls BuildMonthlyAsync directly with no other filter) — guarding here covers all three in one
        // place, since the on-screen schedule is transcribed onto the paper form and is the same filing
        // hole as the other two. Runs FIRST, before the payslip-count check and before WP-5's
        // EnsureEmployerAccount/EnsureNamesFilable (called from the two artifact builders, AFTER this
        // method returns) — a Draft run is refused before we start complaining about its data quality.
        // See Pnd1FilingService.BuildPnd1MonthlyAsync for the full VERDICT H13 citation.
        if (run.Status != DocumentStatus.Posted)
            throw new DomainException("payroll.not_posted_for_filing",
                $"งวดเงินเดือน {run.PeriodYearMonth} ยังไม่ได้ลงบัญชี (สถานะ {run.Status}) — " +
                $"ต้องอนุมัติและลงบัญชีก่อนจึงจะออกแบบยื่นได้ " +
                $"[Payroll run {run.PeriodYearMonth} is {run.Status}; a filing artifact requires a Posted run.]");
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

    public async Task<byte[]> BuildMonthlyPdfAsync(long runId, CancellationToken ct)
    {
        var model = await BuildMonthlyAsync(runId, ct);
        EnsurePayerTaxId(model);
        EnsureEmployerAccount(model);
        return Pdf.Sps110FormFiller.Fill(model);
    }

    public async Task<(byte[] Content, string FileName)> BuildMonthlyFileAsync(long runId, CancellationToken ct)
    {
        var model = await BuildMonthlyAsync(runId, ct);
        EnsurePayerTaxId(model);
        EnsureEmployerAccount(model);
        EnsureNamesFilable(model);
        return (SpsBatchFormat.BuildBytes(model), SpsBatchFormat.FileName(model));
    }

    // R2/L1-1 — mirrors EnsureEmployerAccount's placement exactly: guards only the two
    // artifacts (file + PDF), NOT BuildMonthlyAsync — the on-screen สปส.1-10 ส่วนที่ 2 schedule
    // must keep rendering (pinned by T15) so the user can see what's wrong before fixing the
    // company profile, rather than being shut out entirely.
    private static void EnsurePayerTaxId(SsoMonthlyModel model) => PayerTaxIdRules.EnsureUsable(model.EmployerTaxId);

    // R2/H8 — เลขที่บัญชีนายจ้าง is mandatory on สปส.1-10. Today a blank one is emitted as
    // "0000000000" by SpsBatchFormat.Digits (:67/:122-127) and the user discovers it at the SSO
    // portal. Mirrors the ภ.พ.30 exporter's refusal (Pp30BatchExportService.cs:39-52,
    // pp30_batch.missing_address) — the sibling that already does this right. Guards ONLY the two
    // artifacts (file + PDF), NOT BuildMonthlyAsync — the on-screen สปส.1-10 ส่วนที่ 2 schedule
    // (sso-schedule) must keep rendering so the user can SEE what is missing.
    private static void EnsureEmployerAccount(SsoMonthlyModel model)
    {
        if (string.IsNullOrWhiteSpace(model.EmployerAccountNo))
            throw new DomainException("sso_batch.missing_employer_account",
                "ยังไม่ได้ตั้งค่าเลขที่บัญชีนายจ้าง (10 หลัก) — กรอกในข้อมูลบริษัทก่อนจึงจะออกไฟล์ สปส.1-10 ได้ " +
                "[SSO employer account number is required for สปส.1-10. Set it on the company profile " +
                "(CompanyProfile.SsoEmployerAccountNo) first.]");
    }

    // R2/H9 — every name that becomes literal TEXT in the สปส.1-10 upload file must be filable
    // (FilingNameRules.EnsureFilable). File-channel ONLY (§2.3 of the spec): Sps110FormFiller.Fill
    // (the PDF channel) does shrink-to-fit with no cp874 encoding step, so it cannot suffer the
    // same silent-'?'-substitution defect and needs no guard. คำนำหน้า (Title) is deliberately NOT
    // checked here — the file never carries it as text, only as a numeric code (PrefixCode), and a
    // blank Title is a legitimate, already-tested value (SpsBatchFormatTests: PrefixCode("")=="099").
    private static void EnsureNamesFilable(SsoMonthlyModel model)
    {
        FilingNameRules.EnsureFilable(model.EmployerName, "ชื่อสถานประกอบการ (employer name)", "นายจ้าง (employer)");
        foreach (var l in model.Lines)
        {
            var who = $"ผู้ประกันตน {l.NationalId}";
            FilingNameRules.EnsureFilable(l.FirstName, "ชื่อ (first name)", who);
            FilingNameRules.EnsureFilable(l.LastName, "ชื่อสกุล (last name)", who);
        }
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

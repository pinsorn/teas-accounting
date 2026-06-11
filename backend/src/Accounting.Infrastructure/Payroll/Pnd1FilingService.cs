using Accounting.Application.Abstractions;
using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Payroll;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Payroll;

/// <summary>Builds ภ.ง.ด.1 (monthly, from a run) + ภ.ง.ด.1ก (annual, aggregating posted runs in a tax
/// year) + their ใบแนบ from payslips + the employer profile header. Salary = ม.40(1) กรณีทั่วไป.</summary>
public sealed class Pnd1FilingService(AccountingDbContext db, ITenantContext tenant) : IPnd1FilingService
{
    public async Task<byte[]> BuildPnd1MonthlyAsync(long runId, CancellationToken ct)
    {
        var run = await db.PayrollRuns.AsNoTracking().Include(r => r.Payslips)
                .FirstOrDefaultAsync(r => r.PayrollRunId == runId, ct)
            ?? throw new DomainException("payroll.not_found", $"Payroll run {runId} not found.");
        if (run.Payslips.Count == 0)
            throw new DomainException("payroll.no_employees", "Run has no payslips.");

        // The registered address lives on master.company_profile (CompanyProfile), NOT master.companies.
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == run.CompanyId, ct);
        var prof = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == run.CompanyId, ct);

        // ม.52 + ม.59: ภ.ง.ด.1 is filed for the month the income was PAID (เดือนที่จ่ายเงินได้
        // พึงประเมิน — remit within 7 days of that month's end), NOT the payroll period month.
        // Identical for normal in-month runs; a cross-month PayDate follows the payment month.
        // (สปส.1-10 correctly stays on the wage-period month — different law, different basis.)
        var month = run.PayDate.Month;
        var yearBe = run.PayDate.Year + 543;
        var payDate = $"{run.PayDate.Day:00}/{run.PayDate.Month:00}/{(run.PayDate.Year + 543) % 100:00}";

        var names = await NameMapAsync(run.Payslips.Select(p => p.EmployeeId), ct);
        var lines = run.Payslips
            .OrderBy(p => p.EmployeeCode)
            .Select(p =>
            {
                var (first, last) = ResolveName(names, p.EmployeeId, p.EmployeeName);
                return new Pnd1Line(p.NationalId, first, last, payDate, p.GrossTaxable, p.PitWithheld);
            })
            .ToList();

        // Prefer the structured registered-address fields; if a tenant has only the legacy
        // free-text Line1, fall back to dropping it in เลขที่ so the form isn't blank.
        var hasStructured = prof is not null && new[]
        {
            prof.RegBuilding, prof.RegRoomNo, prof.RegFloor, prof.RegVillage,
            prof.RegHouseNo, prof.RegMoo, prof.RegSoi, prof.RegStreet,
        }.Any(s => !string.IsNullOrWhiteSpace(s));
        var houseNo = hasStructured ? prof!.RegHouseNo
            : string.Join(" ", new[] { prof?.RegisteredAddressLine1, prof?.RegisteredAddressLine2 }
                .Where(s => !string.IsNullOrWhiteSpace(s)));

        var model = new Pnd1MonthlyModel(
            EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? "",
            BranchCode: prof?.BranchCode ?? "00000",
            EmployerName: prof?.LegalName ?? c?.NameTh ?? "",
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor, Village: prof?.RegVillage,
            HouseNo: string.IsNullOrWhiteSpace(houseNo) ? null : houseNo,
            Moo: prof?.RegMoo, Soi: prof?.RegSoi, Street: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            PeriodMonth: month, PeriodYearBE: yearBe,
            Lines: lines);

        return Pnd1FormFiller.FillMonthly(model);
    }

    public async Task<byte[]> BuildPnd1aAnnualAsync(int year, CancellationToken ct)
    {
        var prof = await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);
        var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

        // ม.58(1): ภ.ง.ด.1ก covers income PAID within the CE year (จ่ายในปีที่ล่วงมาแล้ว) —
        // aggregate every POSTED run by PAYMENT year, per employee (whole-year income + tax).
        var slips = await db.Payslips.AsNoTracking()
            .Where(p => p.Run!.Status == DocumentStatus.Posted && p.Run.PayDate.Year == year)
            .Select(p => new { p.EmployeeId, p.NationalId, p.EmployeeName, p.AddressText, p.GrossTaxable, p.PitWithheld })
            .ToListAsync(ct);
        if (slips.Count == 0)
            throw new DomainException("payroll.no_data", $"No posted payroll runs in tax year {year}.");

        var names = await NameMapAsync(slips.Select(s => s.EmployeeId), ct);
        var lines = slips
            .GroupBy(s => s.EmployeeId)
            .Select(g =>
            {
                var s0 = g.First();
                var (first, last) = ResolveName(names, g.Key, s0.EmployeeName);
                return new Pnd1aLine(s0.NationalId, first, last, s0.AddressText,
                    g.Sum(x => x.GrossTaxable), g.Sum(x => x.PitWithheld));
            })
            .OrderBy(l => l.LastName).ThenBy(l => l.FirstName)
            .ToList();

        var model = new Pnd1aModel(
            EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? "",
            BranchCode: prof?.BranchCode ?? "00000",
            EmployerName: prof?.LegalName ?? c?.NameTh ?? "",
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor, Village: prof?.RegVillage,
            HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo, Soi: prof?.RegSoi, Street: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            YearBE: year + 543, Lines: lines);

        return Pnd1aFormFiller.FillAnnual(model);
    }

    // EmployeeId → (ชื่อ = title + first name, ชื่อสกุล = last name) for the form's split name boxes.
    private async Task<Dictionary<long, (string First, string Last)>> NameMapAsync(
        IEnumerable<long> employeeIds, CancellationToken ct)
    {
        var ids = employeeIds.Distinct().ToList();
        return await db.Employees.AsNoTracking()
            .Where(e => ids.Contains(e.EmployeeId))
            .ToDictionaryAsync(
                e => e.EmployeeId,
                e => (((e.TitleTh ?? "") + " " + e.FirstNameTh).Trim(), e.LastNameTh),
                ct);
    }

    // Use the master split when available; else split the frozen snapshot name on its last space.
    private static (string First, string Last) ResolveName(
        Dictionary<long, (string First, string Last)> map, long employeeId, string snapshotFullName)
    {
        if (map.TryGetValue(employeeId, out var n)) return n;
        var full = (snapshotFullName ?? "").Trim();
        var i = full.LastIndexOf(' ');
        return i <= 0 ? (full, "") : (full[..i].Trim(), full[(i + 1)..].Trim());
    }
}

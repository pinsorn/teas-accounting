using Accounting.Application.Payroll;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Payroll;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Payroll;

/// <summary>Builds the monthly ภ.ง.ด.1 (return + ใบแนบ) from a posted payroll run's payslips +
/// the employer (Company) header. Salary = ม.40(1) กรณีทั่วไป. Tenant-scoped via the EF filter.</summary>
public sealed class Pnd1FilingService(AccountingDbContext db) : IPnd1FilingService
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

        var month = int.Parse(run.PeriodYearMonth[4..]);
        var yearBe = int.Parse(run.PeriodYearMonth[..4]) + 543;
        var payDate = $"{run.PayDate.Day:00}/{run.PayDate.Month:00}/{(run.PayDate.Year + 543) % 100:00}";

        var lines = run.Payslips
            .OrderBy(p => p.EmployeeCode)
            .Select(p => new Pnd1Line(p.NationalId, p.EmployeeName, "", payDate, p.GrossTaxable, p.PitWithheld))
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
}

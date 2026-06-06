using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Pdf;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Tax;

/// <summary>
/// Implements ภ.ง.ด.51 (ม.67ทวิ method A) PDF generation.
/// Pulls company header from CompanyProfile; optionally queries H1 P&amp;L as the default
/// estimated profit (caller may override). Rate schedule: General 20% flat by default;
/// SME 0/15/20 via <c>isSme=true</c> (auto-detect deferred to Phase C-C when PaidUpCapital lands).
/// </summary>
public sealed class Pnd51FilingService(
    AccountingDbContext db,
    ITenantContext tenant,
    IFinancialReportService financialReport) : IPnd51FilingService
{
    private static readonly TimeSpan Bkk = TimeSpan.FromHours(7);

    public async Task<byte[]> BuildPnd51Async(
        int year,
        decimal? estimatedAnnualProfit,
        decimal whtSufferedH1,
        bool isSme,
        CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");

        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

        // Fiscal-year bounds
        var startMonth  = (int)c.FiscalYearStartMonth;
        var periodStart = new DateOnly(year, startMonth, 1);
        var periodEnd   = periodStart.AddMonths(12).AddDays(-1);
        var h1End       = periodStart.AddMonths(6).AddDays(-1);

        // Estimated full-year taxable profit: caller's value takes precedence.
        decimal estimate;
        if (estimatedAnnualProfit.HasValue)
        {
            estimate = estimatedAnnualProfit.Value;
        }
        else
        {
            // Default: H1 accounting net profit × 2 (method A proxy; taxpayer should review).
            var plH1 = await financialReport.ProfitLossAsync(
                periodStart, h1End, businessUnitId: null, includeUnspecified: true, ct);
            estimate = Math.Max(0m, plH1.Totals.NetProfit) * 2m;
        }

        var schedule  = isSme ? CitRateSchedule.Sme() : CitRateSchedule.General();
        var taxAmount = CitCalculator.HalfYearPrepayment(estimate, whtSufferedH1, schedule);

        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(Bkk).Date);

        var model = new Pnd51Model(
            EmployerTaxId: prof?.TaxId ?? c.TaxId,
            EmployerName:  prof?.LegalName ?? c.NameTh,
            PeriodStart:   periodStart,
            PeriodEnd:     periodEnd,
            Building:      prof?.RegBuilding,
            RoomNo:        prof?.RegRoomNo,
            Floor:         prof?.RegFloor,
            Village:       prof?.RegVillage,
            HouseNo:       prof?.RegHouseNo,
            Moo:           prof?.RegMoo,
            Soi:           prof?.RegSoi,
            Road:          prof?.RegStreet,
            SubDistrict:   prof?.RegisteredSubdistrict,
            District:      prof?.RegisteredDistrict,
            Province:      prof?.RegisteredProvince,
            PostalCode:    prof?.RegisteredPostalCode,
            HalfYearTax:   taxAmount,
            FilingDate:    today);

        return Pnd51FormFiller.Fill(model);
    }
}

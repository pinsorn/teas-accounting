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
        bool fillWorksheet,
        Pnd51Attestation? attest,
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

        // Estimated full-year taxable profit: caller's value takes precedence. On the default path we also
        // keep the full-year revenue/expense (× H1) so the page-2 worksheet can fill boxes 51/52/53-54;
        // on the override path only the net estimate exists → those stay null (worksheet starts at 57-58).
        decimal estimate;
        decimal? revenueFullYear = null, expenseFullYear = null;
        if (estimatedAnnualProfit.HasValue)
        {
            estimate = estimatedAnnualProfit.Value;
        }
        else
        {
            // Default: H1 accounting net profit × 2 (method A proxy; taxpayer should review).
            var plH1 = await financialReport.ProfitLossAsync(
                periodStart, h1End, businessUnitId: null, includeUnspecified: true, ct);
            revenueFullYear = plH1.Totals.Revenue * 2m;
            expenseFullYear = plH1.Totals.Expense * 2m;
            estimate = Math.Max(0m, plH1.Totals.NetProfit) * 2m;
        }

        var schedule  = isSme ? CitRateSchedule.Sme() : CitRateSchedule.General();
        var taxAmount = CitCalculator.HalfYearPrepayment(estimate, whtSufferedH1, schedule);

        var worksheet = BuildWorksheet(
            fillWorksheet, attest, isSme, estimate, revenueFullYear, expenseFullYear, whtSufferedH1, schedule);

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
            FilingDate:    today,
            Worksheet:     worksheet);

        return Pnd51FormFiller.Fill(model);
    }

    /// <summary>
    /// Pure compute + guard for the page-2 Method-A worksheet. Returns null when page 2 is not requested.
    /// Throws (ภ.ง.ด.51 §4 attestation) unless the filer attests a clean case AND the figures FOOT as a
    /// positive ชำระเพิ่มเติม: general rate only (SME rate radio not yet confirmed), first filing, no loss
    /// carry-forward / exemption / rate-reduction / surcharge, a positive estimate, and tax ≥ H1 WHT (so the
    /// worksheet never shows a non-footing or ชำระไว้เกิน/ขาดทุน case the clamped engine can't honestly render).
    /// On the override path <paramref name="revenueFullYear"/>/<paramref name="expenseFullYear"/> are null →
    /// boxes 51/52/53-54 stay blank. Inputs are full-year (already ×2 on the default path).
    /// </summary>
    public static Pnd51Worksheet? BuildWorksheet(
        bool fillWorksheet, Pnd51Attestation? attest, bool isSme,
        decimal estimate, decimal? revenueFullYear, decimal? expenseFullYear,
        decimal whtSufferedH1, CitRateSchedule schedule)
    {
        if (!fillWorksheet) return null;

        // box 32 — half the CIT computed on the estimated full-year profit (flat or SME bracket via schedule).
        var taxComputed = decimal.Round(
            CitCalculator.TaxOnProfit(estimate, schedule) * 0.50m, 2, MidpointRounding.AwayFromZero);
        var wht = Math.Max(0m, whtSufferedH1);

        // ภ.ง.ด.51 §4 attestation: page 2 may be filled ONLY for a clean, general-rate, footing Method-A
        // case. A blank box on this form asserts zero, so we refuse (throw) — never silently default —
        // unless the filer attests all four adjustment inputs are 0, it is a first (not amended) filing,
        // and the figures foot as a POSITIVE ชำระเพิ่มเติม (estimate > 0 and tax ≥ H1 WHT). The
        // ชำระไว้เกิน / ขาดทุน cases (which the clamped engine can't honestly render and whose radios are
        // unconfirmed) fall back to a legal blank page 2.
        if (attest is null || isSme
            || !(attest.FirstFiling && attest.NoLossCarryForward && attest.NoExemption
                 && attest.NoRateReduction && attest.NoSurcharge)
            || estimate <= 0m || taxComputed < wht)
        {
            throw new DomainException("pnd51.worksheet_not_attestable",
                "Page-2 worksheet needs a clean, footing, general-rate Method-A attestation "
              + "(first filing; not SME-rate; no loss carry-forward, exemption, rate-reduction, or surcharge; "
              + "positive estimate; computed tax ≥ H1 WHT). Use Phase C-C for the full worksheet.");
        }

        var netPayable = decimal.Round(taxComputed - wht, 2, MidpointRounding.AwayFromZero);
        return new Pnd51Worksheet(
            RevenueFullYear:     revenueFullYear,
            ExpenseFullYear:     expenseFullYear,
            EstimatedNetProfit:  estimate,
            HalfEstimatedProfit: estimate / 2m,
            TaxComputed:         taxComputed,
            WhtH1:               wht,
            NetPayable:          netPayable,
            IsSme:               isSme);
    }
}

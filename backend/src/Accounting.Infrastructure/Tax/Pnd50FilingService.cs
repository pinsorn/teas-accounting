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
/// ภ.ง.ด.50 v1 (Phase C-C): page 1 header + page 2 รายการที่ 1 from the CIT data layer
/// (CitProfile for SME/adjustments/loss, cit_year_summaries for the ภ.ง.ด.51 estimate+prepaid,
/// WhtReceivableRegister for the FY WHT credit, CitCalculator for the ladder + ม.67ตรี penalty).
/// <see cref="BuildSheet"/> is the pure §4 guard + figure derivation (unit-tested without a DB).
/// </summary>
public sealed class Pnd50FilingService(
    AccountingDbContext db,
    ITenantContext tenant,
    ICitYearDataService citData,
    IWhtReceivableReportService whtReport) : IPnd50FilingService
{
    public async Task<byte[]> BuildPnd50Async(
        int year, bool? isSme, bool hasRelatedPartyOver200M,
        Pnd50Attestation? attest, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");
        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);

        var startMonth  = (int)c.FiscalYearStartMonth;
        var periodStart = new DateOnly(year, startMonth, 1);
        var periodEnd   = periodStart.AddMonths(12).AddDays(-1);

        var profile = await citData.ProfileAsync(year, ct);
        var summary = (await citData.ListYearsAsync(ct)).FirstOrDefault(y => y.FiscalYear == year);
        var prepaid  = summary?.Pnd51Prepaid ?? 0m;
        var estimate = summary?.Pnd51EstimatedProfit;

        // FY WHT suffered (AR-side 50ทวิ credit — box 54).
        var whtFy = (await whtReport.GetRegisterAsync(periodStart, periodEnd, ct)).TotalWht;

        var sme      = isSme ?? profile.IsSme;
        var schedule = sme ? CitRateSchedule.Sme() : CitRateSchedule.General();
        var accountingNp = summary?.EffectiveNetProfit ?? profile.AccountingNetProfit;

        var cit = CitCalculator.Compute(
            accountingNp, profile.AdjustmentsTotal, profile.LossCarryIn, prepaid, whtFy, schedule);
        var surcharge = estimate is { } est
            ? CitCalculator.UnderEstimatePenalty(est, cit.TaxableProfit, prepaid, schedule)
            : 0m;

        var sheet = BuildSheet(cit, whtFy, prepaid, surcharge, sme, attest);

        var model = new Pnd50Model(
            TaxId: prof?.TaxId ?? c.TaxId, CompanyName: prof?.LegalName ?? c.NameTh,
            PeriodStart: periodStart, PeriodEnd: periodEnd,
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
            Village: prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
            Soi: prof?.RegSoi, Road: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            Website: prof?.Website, Email: prof?.Email,
            HasRelatedPartyOver200M: hasRelatedPartyOver200M,
            Sheet: sheet);
        return Pnd50FormFiller.Fill(model);
    }

    /// <summary>
    /// Derive the page-2 รายการที่ 1 figures from a <see cref="CitComputation"/>, enforcing the
    /// ภ.ง.ด.50 §4 posture: a blank box on this form asserts zero, so any year the v1 layout cannot
    /// honestly render is REFUSED (<c>pnd50.not_attestable</c>) — never silently defaulted.
    /// <paramref name="whtSuffered"/>/<paramref name="pnd51Prepaid"/> are the two credit components
    /// (boxes 54/55); they must reproduce <see cref="CitComputation.CreditsTotal"/> exactly —
    /// a mismatch is a caller bug, not a tax condition.
    /// </summary>
    public static Pnd50Sheet BuildSheet(
        CitComputation cit, decimal whtSuffered, decimal pnd51Prepaid,
        decimal surcharge, bool isSme, Pnd50Attestation? attest)
    {
        if (Math.Max(0m, whtSuffered) + Math.Max(0m, pnd51Prepaid) != cit.CreditsTotal)
            throw new InvalidOperationException(
                "BuildSheet credit components must reproduce CitComputation.CreditsTotal.");

        if (attest is not { FirstFiling: true, AcceptBlankSchedules: true })
            throw new DomainException("pnd50.not_attestable",
                "ภ.ง.ด.50 v1 prints รายการที่ 2–9 blank (a blank box asserts zero) — the filer must "
              + "attest firstFiling + acceptBlankSchedules, or complete the full form manually.");

        // Non-zero ม.65ทวิ/ตรี adjustments or applied loss carry-forward require the รายการที่ 2
        // ladder (page 3), which v1 does not render — leaving it blank would assert a false zero.
        if (cit.AdjustmentsTotal != 0m || cit.LossApplied != 0m)
            throw new DomainException("pnd50.not_attestable",
                "Non-zero ม.65ทวิ/ตรี adjustments or loss carry-forward require the รายการที่ 2 "
              + "ladder (page 3), which ภ.ง.ด.50 v1 does not render.");

        var net     = cit.TaxBeforeCredits - cit.CreditsTotal;
        var payMore = net >= 0m;

        // บวกเงินเพิ่ม (box 60) belongs to the ชำระเพิ่มเติม branch of the form's bottom line; an
        // overpaid year that still owes a ม.67ตรี penalty is not renderable in the v1 layout.
        if (!payMore && surcharge > 0m)
            throw new DomainException("pnd50.not_attestable",
                "เงินเพิ่ม (ม.67ตรี) combined with an overpaid (ชำระไว้เกิน) bottom line is not "
              + "renderable in the ภ.ง.ด.50 v1 layout.");

        var isLoss = cit.TaxableBeforeLoss < 0m;
        return new Pnd50Sheet(
            BaseAmount:   isLoss ? Math.Abs(cit.TaxableBeforeLoss) : cit.TaxableProfit,
            IsLoss:       isLoss,
            TaxComputed:  cit.TaxBeforeCredits,
            WhtCredit:    Math.Max(0m, whtSuffered),
            Pnd51Prepaid: Math.Max(0m, pnd51Prepaid),
            CreditsTotal: cit.CreditsTotal,
            NetAmount:    Math.Abs(net),
            PayMore:      payMore,
            Surcharge:    payMore ? surcharge : 0m,
            TotalAmount:  Math.Abs(net) + (payMore ? surcharge : 0m),
            IsSme:        isSme);
    }
}

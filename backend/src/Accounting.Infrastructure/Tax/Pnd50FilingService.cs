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
/// ภ.ง.ด.50 v2 (the only behaviour — spec pnd50-v2-dashboard.md): p1 header + p2 รายการที่ 1 +
/// p3 รายการที่ 2/3 ladder + p6 งบฐานะ, all from the CIT data layer (CitProfile for
/// SME/adjustments/loss, cit_year_summaries for the ภ.ง.ด.51 estimate+prepaid,
/// WhtReceivableRegister for the FY WHT credit, BalanceSheetAsync for p6, CitCalculator for the
/// computation + ม.67ตรี penalty). <see cref="ComposeAsync"/> builds ONE composition consumed by
/// BOTH the PDF and the preview endpoint (single source — the dashboard shows exactly what the
/// filler prints). Pure pieces (<see cref="BuildSheet"/>, <see cref="BuildLadder"/>,
/// <see cref="MapBalanceSheet"/>) are unit-tested without a DB.
/// </summary>
public sealed class Pnd50FilingService(
    AccountingDbContext db,
    ITenantContext tenant,
    ICitYearDataService citData,
    IWhtReceivableReportService whtReport,
    IFinancialReportService financialReport) : IPnd50FilingService
{
    /// <summary>Everything the PDF/preview need, derived once. Refusals are COLLECTED, not thrown.</summary>
    internal sealed record Pnd50Composition(
        int Year, DateOnly PeriodStart, DateOnly PeriodEnd,
        bool IsSme, decimal? PaidUpCapital, decimal Revenue, decimal Expenses,
        CitComputation Cit, Pnd50Ladder? Ladder, Pnd50BalanceSheetBoxes BalanceSheet,
        bool BalanceSheetBalanced,
        decimal WhtCredit, decimal Pnd51Prepaid, decimal? Pnd51Estimate, decimal Surcharge,
        IReadOnlyList<WhtReceivableRegisterRow> WhtRows,
        IReadOnlyList<CitAdjustmentDto> Adjustments,
        IReadOnlyList<string> Refusals);

    private async Task<Pnd50Composition> ComposeAsync(int year, bool? isSme, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");

        var startMonth  = (int)c.FiscalYearStartMonth;
        var periodStart = new DateOnly(year, startMonth, 1);
        var periodEnd   = periodStart.AddMonths(12).AddDays(-1);

        var profile = await citData.ProfileAsync(year, ct);
        var summary = (await citData.ListYearsAsync(ct)).FirstOrDefault(y => y.FiscalYear == year);
        var prepaid  = summary?.Pnd51Prepaid ?? 0m;
        var estimate = summary?.Pnd51EstimatedProfit;

        // FY WHT suffered (AR-side 50ทวิ credit — box 54), per-certificate for the dashboard.
        var whtReg = await whtReport.GetRegisterAsync(periodStart, periodEnd, ct);

        var sme      = isSme ?? profile.IsSme;
        var schedule = sme ? CitRateSchedule.Sme() : CitRateSchedule.General();

        // v2 fix: feed the PURE accounting net profit. (v1 fed EffectiveNetProfit, which
        // ComputeYearAsync stores as P&L + adjustments — a latent double-count masked by the
        // v1 adjustments==0 refusal.) An override that disagrees with the books cannot be
        // rendered in a ladder whose rows must foot → refusal, not a silent substitution.
        var cit = CitCalculator.Compute(
            profile.AccountingNetProfit, profile.AdjustmentsTotal, profile.LossCarryIn,
            prepaid, whtReg.TotalWht, schedule);
        var surcharge = estimate is { } est
            ? CitCalculator.UnderEstimatePenalty(est, cit.TaxableProfit, prepaid, schedule)
            : 0m;

        var refusals = new List<string>();
        if (summary?.OverrideNetProfit is { } o && o != cit.TaxableBeforeLoss)
            refusals.Add("pnd50.override_breaks_ladder");

        var adjustments = await citData.ListAdjustmentsAsync(year, ct);
        var posAdj = adjustments.Where(a => a.Amount > 0m).Sum(a => a.Amount);
        var negAdj = adjustments.Where(a => a.Amount < 0m).Sum(a => a.Amount);
        var expenses = profile.RevenueFullYear - profile.AccountingNetProfit;

        Pnd50Ladder? ladder = null;
        try
        {
            ladder = BuildLadder(profile.RevenueFullYear, expenses, posAdj, negAdj, cit);
        }
        catch (DomainException ex)
        {
            refusals.Add(ex.Code);
        }

        // เงินเพิ่ม (ม.67ตรี) sits on the ชำระเพิ่มเติม branch — an overpaid bottom line that still
        // owes the penalty has no honest box (same rule BuildSheet enforces on the PDF path).
        if (cit.TaxBeforeCredits - cit.CreditsTotal < 0m && surcharge > 0m)
            refusals.Add("pnd50.surcharge_with_overpaid");

        var bs = await financialReport.BalanceSheetAsync(periodEnd, ct);

        return new Pnd50Composition(
            year, periodStart, periodEnd, sme, profile.PaidUpCapital,
            profile.RevenueFullYear, expenses,
            cit, ladder, MapBalanceSheet(bs), bs.Balanced,
            whtReg.TotalWht, prepaid, estimate, surcharge,
            whtReg.Rows, adjustments, refusals);
    }

    public async Task<byte[]> BuildPnd50Async(
        int year, bool? isSme, bool hasRelatedPartyOver200M,
        Pnd50Attestation? attest, CancellationToken ct)
    {
        var comp = await ComposeAsync(year, isSme, ct);
        if (comp.Refusals.Count > 0)
            throw new DomainException("pnd50.not_renderable",
                "ภ.ง.ด.50 cannot honestly render this year: " + string.Join("; ", comp.Refusals));

        var prof = await db.CompanyProfiles.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct);
        var c = await db.Companies.AsNoTracking()
            .FirstAsync(x => x.CompanyId == tenant.CompanyId, ct);

        var sheet = BuildSheet(
            comp.Cit, comp.WhtCredit, comp.Pnd51Prepaid, comp.Surcharge, comp.IsSme, attest);

        var model = new Pnd50Model(
            TaxId: prof?.TaxId ?? c.TaxId, CompanyName: prof?.LegalName ?? c.NameTh,
            PeriodStart: comp.PeriodStart, PeriodEnd: comp.PeriodEnd,
            Building: prof?.RegBuilding, RoomNo: prof?.RegRoomNo, Floor: prof?.RegFloor,
            Village: prof?.RegVillage, HouseNo: prof?.RegHouseNo, Moo: prof?.RegMoo,
            Soi: prof?.RegSoi, Road: prof?.RegStreet,
            SubDistrict: prof?.RegisteredSubdistrict, District: prof?.RegisteredDistrict,
            Province: prof?.RegisteredProvince, PostalCode: prof?.RegisteredPostalCode,
            Website: prof?.Website, Email: prof?.Email,
            HasRelatedPartyOver200M: hasRelatedPartyOver200M,
            Sheet: sheet,
            Ladder: comp.Ladder!,
            BalanceSheet: comp.BalanceSheet);
        return Pnd50FormFiller.Fill(model);
    }

    public async Task<Pnd50PreviewDto> PreviewAsync(int year, bool? isSme, CancellationToken ct)
    {
        var comp = await ComposeAsync(year, isSme, ct);
        var cit = comp.Cit;
        var net = cit.TaxBeforeCredits - cit.CreditsTotal;
        var payMore = net >= 0m;
        var b = comp.BalanceSheet;

        return new Pnd50PreviewDto(
            comp.Year, comp.PeriodStart, comp.PeriodEnd, comp.IsSme, comp.PaidUpCapital,
            comp.Revenue, comp.Expenses,
            comp.Pnd51Estimate, comp.Pnd51Prepaid,
            comp.WhtCredit,
            comp.WhtRows.Select(r => new Pnd50WhtCertDto(
                r.DocNo, r.DocDate, r.CustomerName, r.CustomerTaxId,
                r.WhtAmount, r.CustomerWhtCertNo)).ToList(),
            comp.Ladder is { } l
                ? new Pnd50LadderDto(
                    l.DirectRevenue, l.CostOfSales, l.GrossProfit, l.OtherIncome, l.Total5,
                    l.OtherExpenses, l.Total7, l.SellingAdminExpenses, l.AccountingNetProfit,
                    l.IncomeAdditions, l.DisallowedExpenses, l.Total12, l.ExemptDeductions,
                    l.Total14, l.LossCarryForward, l.Total16, l.Total20, l.TaxableNetProfit)
                : null,
            comp.Adjustments,
            cit.TaxBeforeCredits, cit.CreditsTotal, Math.Abs(net), payMore,
            payMore ? comp.Surcharge : 0m,
            Math.Abs(net) + (payMore ? comp.Surcharge : 0m),
            new Pnd50BalanceSheetDto(
                b.CashAndEquivalents, b.TradeReceivables, b.Inventory,
                b.OtherCurrentAssets, b.OtherNonCurrentAssets, b.TotalAssets,
                b.TradePayables, b.OtherCurrentLiabilities, b.OtherNonCurrentLiabilities,
                b.TotalLiabilities, b.PaidUpShareCapital, b.OtherEquity,
                b.RetainedEarnings, b.TotalEquity, b.TotalLiabilitiesAndEquity,
                comp.BalanceSheetBalanced),
            comp.Refusals);
    }

    /// <summary>
    /// Derive the page-2 รายการที่ 1 figures from a <see cref="CitComputation"/>, enforcing the
    /// ภ.ง.ด.50 §4 posture: a blank box on this form asserts zero, so any year the layout cannot
    /// honestly render is REFUSED (<c>pnd50.not_attestable</c>) — never silently defaulted.
    /// (v2 renders the p3 ladder + p6 balance sheet, so the v1 adjustments/loss refusals are gone;
    /// pages 4–5 + 7 + ใบแนบ still print blank, hence the attestation.)
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
                "ภ.ง.ด.50 prints pages 4–5 (ต้นทุน/ขายบริหาร detail), page 7 (แบบแจ้งกรรมการ) and "
              + "the ใบแนบ blank (a blank box asserts zero) — the filer must attest firstFiling + "
              + "acceptBlankSchedules, or complete those schedules manually.");

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

    /// <summary>
    /// p3 รายการที่ 2 ladder from the FY P&amp;L + sign-split adjustment sums + the CitComputation.
    /// Pure; throws DomainException("pnd50.ladder_sign_flip") when the running chain flips sign
    /// mid-ladder (boxes are unsigned — only rows 3/9/21 carry sign radios, so such a year cannot
    /// be honestly rendered), and InvalidOperationException when the caller's figures do not
    /// reproduce the CitComputation (caller bug, not a tax condition — same posture as BuildSheet's
    /// credits check). Ladder mapping per spec pnd50-v2-dashboard.md §3: positives → row 11
    /// (รายจ่ายที่ไม่ให้ถือเป็นรายจ่าย), negatives → row 13 (รายได้ยกเว้น/รายจ่ายหักเพิ่ม);
    /// rows 2/4/6/10/17/18/19 are structurally 0 in v2 (flat P&amp;L, no per-code classification).
    /// </summary>
    public static Pnd50Ladder BuildLadder(
        decimal revenue, decimal expenses,
        decimal positiveAdjustments, decimal negativeAdjustments, CitComputation cit)
    {
        if (positiveAdjustments < 0m || negativeAdjustments > 0m)
            throw new InvalidOperationException(
                "BuildLadder adjustment sums must be pre-split by sign.");
        if (revenue - expenses != cit.AccountingProfit)
            throw new InvalidOperationException(
                "BuildLadder P&L must reproduce CitComputation.AccountingProfit.");
        if (positiveAdjustments + negativeAdjustments != cit.AdjustmentsTotal)
            throw new InvalidOperationException(
                "BuildLadder adjustments must reproduce CitComputation.AdjustmentsTotal.");

        var gross = revenue;                    // row 2 ต้นทุน = 0 (no COGS/inventory in TEAS books)
        var s9  = cit.AccountingProfit;
        var s12 = s9 + positiveAdjustments;     // rows 10 (0) + 11
        var s14 = s12 + negativeAdjustments;    // − row 13
        if (s14 != cit.TaxableBeforeLoss)
            throw new InvalidOperationException(
                "Ladder row 14 must equal CitComputation.TaxableBeforeLoss.");
        if ((s9 >= 0m && s14 < 0m) || (s9 < 0m && (s12 > 0m || s14 > 0m)))
            throw new DomainException("pnd50.ladder_sign_flip",
                "Adjustments flip the sign of the รายการที่ 2 running total mid-ladder; the form's "
              + "unsigned boxes cannot honestly render that — file this year manually.");

        var s16 = s14 - cit.LossApplied;
        return new Pnd50Ladder(
            DirectRevenue: revenue, CostOfSales: 0m, GrossProfit: gross,
            OtherIncome: 0m, Total5: gross, OtherExpenses: 0m, Total7: gross,
            SellingAdminExpenses: expenses, AccountingNetProfit: s9,
            IncomeAdditions: 0m, DisallowedExpenses: positiveAdjustments, Total12: s12,
            ExemptDeductions: Math.Abs(negativeAdjustments), Total14: s14,
            LossCarryForward: cit.LossApplied, Total16: s16,
            Excess10Pct: 0m, CharityExcess: 0m, EducationExcess: 0m,
            Total20: s16, TaxableNetProfit: s16);
    }

    /// <summary>
    /// p5 รายการที่ 7 schedule — PARTITIONS the per-account FY expense rows by the account-code
    /// convention documented on <see cref="Pnd50ExpenseSchedule"/>; every row lands in exactly one
    /// line (unmapped → ข้อ 22), so Total ≡ Σ rows by construction. Throws
    /// InvalidOperationException when that total does not reproduce the ladder's row 8
    /// (<paramref name="sellingAdminExpenses"/>) — a data-source mismatch between
    /// ExpenseByAccountAsync and the P&amp;L is a caller bug, not a tax condition.
    /// </summary>
    public static Pnd50ExpenseSchedule BuildExpenseSchedule(
        IReadOnlyList<ExpenseAccountRow> rows, decimal sellingAdminExpenses)
    {
        decimal emp = 0, rent = 0, mkt = 0, otherTax = 0, fees = 0, other = 0;
        foreach (var r in rows)
        {
            var code = int.TryParse(r.AccountCode, out var n) && n is >= 1000 and <= 9999 ? n : -1;
            switch (code)
            {
                case >= 5400 and <= 5499: emp      += r.Amount; break;
                case >= 5100 and <= 5199: rent     += r.Amount; break;
                case >= 5300 and <= 5349: mkt      += r.Amount; break;
                case >= 5350 and <= 5399: otherTax += r.Amount; break;
                case >= 5200 and <= 5299: fees     += r.Amount; break;
                default:                  other    += r.Amount; break;  // incl. unparseable
            }
        }

        var total = emp + rent + mkt + otherTax + fees + other;
        if (total != sellingAdminExpenses)
            throw new InvalidOperationException(
                "BuildExpenseSchedule rows must reproduce the ladder's SellingAdminExpenses.");

        return new Pnd50ExpenseSchedule(
            Employee: emp, DirectorComp: 0m, Utilities: 0m, Travel: 0m, Freight: 0m,
            Rent: rent, Repairs: 0m, Entertainment: 0m, Marketing: mkt, SbtTax: 0m,
            OtherTaxes: otherTax, FinanceCost: 0m, Bookkeeping: 0m, AuditFee: 0m,
            PoliticalDonation: 0m, CharityDonation: 0m, EducationSport: 0m, Consulting: 0m,
            OtherFees: fees, BadDebt: 0m, Depreciation: 0m, Other: other,
            DoubleDeduct: 0m, Total: total);
    }

    /// <summary>
    /// p5 รายการที่ 8 schedule from the POSITIVE adjustment lines (the same set that feeds ladder
    /// row 11). LegalRefCode matches are EXACT after whitespace removal (so ม.65ตรี(1) never
    /// swallows ม.65ตรี(13)); Label matching is contains-based; remainder → ข้อ 6 อื่นๆ.
    /// Throws InvalidOperationException when Total ≠ <paramref name="disallowedExpenses"/>
    /// (ladder row 11) — caller bug.
    /// </summary>
    public static Pnd50DisallowedSchedule BuildDisallowedSchedule(
        IReadOnlyList<CitAdjustmentDto> adjustments, decimal disallowedExpenses)
    {
        decimal tax = 0, ent = 0, bad = 0, prov = 0, other = 0;
        foreach (var a in adjustments.Where(a => a.Amount > 0m))
        {
            var code  = a.LegalRefCode.Replace(" ", "");
            var label = a.Label;
            if (code == "ม.65ตรี(6)" || label.Contains("ภาษีเงินได้"))      tax   += a.Amount;
            else if (code == "ม.65ตรี(4)" || label.Contains("ค่ารับรอง"))   ent   += a.Amount;
            else if (label.Contains("หนี้สูญ"))                              bad   += a.Amount;
            else if (code == "ม.65ตรี(1)" || label.Contains("เงินสำรอง"))   prov  += a.Amount;
            else                                                             other += a.Amount;
        }

        var total = tax + ent + bad + prov + other;
        if (total != disallowedExpenses)
            throw new InvalidOperationException(
                "BuildDisallowedSchedule positive adjustments must reproduce the ladder's DisallowedExpenses.");

        return new Pnd50DisallowedSchedule(
            IncomeTax: tax, Entertainment: ent, BadDebt: bad, Provisions: prov,
            FromItem7Line23: 0m, Other: other, Total: total);
    }

    /// <summary>
    /// p6 งบแสดงฐานะการเงิน classifier — routes BalanceSheetReport rows to the form's named lines
    /// by the TEAS 4-digit account-code convention (see <see cref="Pnd50BalanceSheetBoxes"/> doc).
    /// Pure; throws InvalidOperationException when the mapped boxes fail to reproduce the report
    /// totals (every account must land exactly once — a classifier bug, not a data condition).
    /// </summary>
    public static Pnd50BalanceSheetBoxes MapBalanceSheet(BalanceSheetReport bs)
    {
        static int CodeOf(BalanceSheetRow r) =>
            int.TryParse(r.AccountCode, out var n) && n is >= 1000 and <= 9999 ? n : -1;

        decimal cash = 0, ar = 0, inv = 0, curA = 0, nonA = 0;
        foreach (var r in bs.Assets.Rows)
            switch (CodeOf(r))
            {
                case >= 1110 and <= 1129: cash += r.Balance; break;
                case >= 1130 and <= 1139: ar   += r.Balance; break;
                case >= 1140 and <= 1149: inv  += r.Balance; break;
                case >= 1500 and <= 1999: nonA += r.Balance; break;
                default:                  curA += r.Balance; break;   // incl. unparseable codes
            }

        decimal ap = 0, curL = 0, nonL = 0;
        foreach (var r in bs.Liabilities.Rows)
            switch (CodeOf(r))
            {
                case 2110:                ap   += r.Balance; break;
                case >= 2500 and <= 2999: nonL += r.Balance; break;
                default:                  curL += r.Balance; break;
            }

        decimal capital = 0, otherEq = 0;
        var retained = bs.CurrentPeriodEarnings;
        foreach (var r in bs.Equity.Rows)
            switch (CodeOf(r))
            {
                case >= 3100 and <= 3199: capital  += r.Balance; break;
                case >= 3200 and <= 3299: retained += r.Balance; break;
                default:                  otherEq  += r.Balance; break;
            }

        var totalEq = capital + otherEq + retained;
        if (cash + ar + inv + curA + nonA != bs.Assets.Total
            || ap + curL + nonL != bs.Liabilities.Total
            || totalEq != bs.Equity.Total + bs.CurrentPeriodEarnings)
            throw new InvalidOperationException("MapBalanceSheet must reproduce the report totals.");

        return new Pnd50BalanceSheetBoxes(
            cash, ar, inv, curA, nonA, bs.Assets.Total,
            ap, curL, nonL, bs.Liabilities.Total,
            capital, otherEq, retained, totalEq, bs.LiabilitiesAndEquityTotal);
    }
}

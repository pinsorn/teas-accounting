using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Tax;
using Accounting.Domain.Tax;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Tax;

/// <summary>
/// Phase C-C — CIT year-data store: per-FY taxable net profit (computed + manual override,
/// locked decision #5), manual ม.65ทวิ/65ตรี adjustment lines (locked decision #1), the
/// persisted ภ.ง.ด.51 estimate (ม.67ตรี under-estimate check), and the ภ.ง.ด.50 profile
/// (auto-SME §4.6 + ม.65ตรี(12) loss carry-in). Tenant scoping = DbContext global query
/// filter on reads; inserts ALWAYS set CompanyId from the tenant context.
/// </summary>
public sealed class CitYearDataService(
    AccountingDbContext db,
    ITenantContext tenant,
    IFinancialReportService financialReport) : ICitYearDataService
{
    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    private static CitYearSummaryDto ToDto(CitYearSummary s) => new(
        s.FiscalYear, s.ComputedNetProfit, s.OverrideNetProfit, s.EffectiveNetProfit,
        s.Pnd51EstimatedProfit, s.Pnd51Prepaid, s.Note);

    private static CitAdjustmentDto ToDto(CitAdjustment a) => new(
        a.CitAdjustmentId, a.FiscalYear, a.LegalRefCode, a.Label, a.Amount);

    /// <summary>Fiscal-year bounds from Company.FiscalYearStartMonth (mirrors Pnd51FilingService).</summary>
    private async Task<(DateOnly Start, DateOnly End)> FiscalBoundsAsync(int fiscalYear, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");
        var start = new DateOnly(fiscalYear, (int)c.FiscalYearStartMonth, 1);
        return (start, start.AddMonths(12).AddDays(-1));
    }

    private async Task<CitYearSummary> FindOrCreateYearAsync(int fiscalYear, CancellationToken ct)
    {
        var row = await db.CitYearSummaries
            .FirstOrDefaultAsync(s => s.FiscalYear == fiscalYear, ct);
        if (row is null)
        {
            row = new CitYearSummary { CompanyId = tenant.CompanyId, FiscalYear = fiscalYear };
            db.CitYearSummaries.Add(row);
        }
        return row;
    }

    public async Task<IReadOnlyList<CitYearSummaryDto>> ListYearsAsync(CancellationToken ct)
    {
        EnsureAuth();
        var rows = await db.CitYearSummaries.AsNoTracking()
            .OrderByDescending(s => s.FiscalYear)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<CitYearSummaryDto> UpsertYearAsync(
        int fiscalYear, UpsertCitYearRequest req, CancellationToken ct)
    {
        EnsureAuth();
        var row = await FindOrCreateYearAsync(fiscalYear, ct);
        row.OverrideNetProfit = req.OverrideNetProfit;
        row.Note = req.Note;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<CitYearSummaryDto> ComputeYearAsync(int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();
        var (start, end) = await FiscalBoundsAsync(fiscalYear, ct);
        var pl = await financialReport.ProfitLossAsync(
            start, end, businessUnitId: null, includeUnspecified: true, ct);
        var adjustments = await db.CitAdjustments.AsNoTracking()
            .Where(a => a.FiscalYear == fiscalYear)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        var row = await FindOrCreateYearAsync(fiscalYear, ct);
        row.ComputedNetProfit = pl.Totals.NetProfit + adjustments;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<CitYearSummaryDto> RecordPnd51EstimateAsync(
        int fiscalYear, decimal estimatedProfit, decimal whtH1, bool isSme, CancellationToken ct)
    {
        EnsureAuth();
        // ม.67ตรี — store the estimate exactly as filed; the ภ.ง.ด.50 year-end check
        // compares it against actual taxable profit (>25% shortfall ⇒ surcharge).
        var row = await FindOrCreateYearAsync(fiscalYear, ct);
        row.Pnd51EstimatedProfit = estimatedProfit;
        row.Pnd51Prepaid = CitCalculator.HalfYearPrepayment(
            estimatedProfit, whtH1, isSme ? CitRateSchedule.Sme() : CitRateSchedule.General());
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<IReadOnlyList<CitAdjustmentDto>> ListAdjustmentsAsync(
        int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();
        var rows = await db.CitAdjustments.AsNoTracking()
            .Where(a => a.FiscalYear == fiscalYear)
            .OrderBy(a => a.CitAdjustmentId)
            .ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    private static void ValidateAdjustment(UpsertCitAdjustmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.LegalRefCode) || string.IsNullOrWhiteSpace(req.Label))
            throw new DomainException("cit.adjustment_invalid",
                "LegalRefCode and Label are required for a CIT adjustment line.");
    }

    public async Task<CitAdjustmentDto> CreateAdjustmentAsync(
        int fiscalYear, UpsertCitAdjustmentRequest req, CancellationToken ct)
    {
        EnsureAuth();
        ValidateAdjustment(req);
        var row = new CitAdjustment
        {
            CompanyId = tenant.CompanyId,
            FiscalYear = fiscalYear,
            LegalRefCode = req.LegalRefCode.Trim(),
            Label = req.Label.Trim(),
            Amount = req.Amount,
        };
        db.CitAdjustments.Add(row);
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task<CitAdjustmentDto> UpdateAdjustmentAsync(
        long id, UpsertCitAdjustmentRequest req, CancellationToken ct)
    {
        EnsureAuth();
        ValidateAdjustment(req);
        var row = await db.CitAdjustments.FirstOrDefaultAsync(a => a.CitAdjustmentId == id, ct)
            ?? throw new DomainException("cit.adjustment_not_found", "CIT adjustment not found.");
        row.LegalRefCode = req.LegalRefCode.Trim();
        row.Label = req.Label.Trim();
        row.Amount = req.Amount;
        await db.SaveChangesAsync(ct);
        return ToDto(row);
    }

    public async Task DeleteAdjustmentAsync(long id, CancellationToken ct)
    {
        EnsureAuth();
        var row = await db.CitAdjustments.FirstOrDefaultAsync(a => a.CitAdjustmentId == id, ct)
            ?? throw new DomainException("cit.adjustment_not_found", "CIT adjustment not found.");
        db.CitAdjustments.Remove(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseAccountRow>> ExpenseByAccountAsync(
        int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();
        var (start, end) = await FiscalBoundsAsync(fiscalYear, ct);

        // Same posted-entry/date-window basis as FinancialReportService.ProfitLossAsync (which
        // ProfileAsync uses), restricted to Expense accounts and grouped per account — so
        // Σ Amount reproduces the P&L expense total and the รายการที่ 7 foot guard holds.
        var rows = await db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.AsNoTracking(), l => l.JournalId, j => j.JournalId,
                  (l, j) => new { l, j })
            .Join(db.ChartOfAccounts.AsNoTracking(), x => x.l.AccountId, a => a.AccountId,
                  (x, a) => new { x.l, x.j, a })
            .Where(x => x.j.Status == Domain.Enums.DocumentStatus.Posted
                        && x.j.DocDate >= start && x.j.DocDate <= end
                        && x.a.AccountType == Domain.Enums.AccountType.Expense)
            .GroupBy(x => new { x.a.AccountCode, x.a.AccountNameTh })
            .Select(g => new ExpenseAccountRow(
                g.Key.AccountCode, g.Key.AccountNameTh,
                g.Sum(x => x.l.DebitAmount) - g.Sum(x => x.l.CreditAmount)))
            .ToListAsync(ct);
        return rows.OrderBy(r => r.AccountCode).ToList();
    }

    public async Task<CitProfileDto> ProfileAsync(int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");

        var start = new DateOnly(fiscalYear, (int)c.FiscalYearStartMonth, 1);
        var end = start.AddMonths(12).AddDays(-1);
        var pl = await financialReport.ProfitLossAsync(
            start, end, businessUnitId: null, includeUnspecified: true, ct);

        var adjustmentsTotal = await db.CitAdjustments.AsNoTracking()
            .Where(a => a.FiscalYear == fiscalYear)
            .SumAsync(a => (decimal?)a.Amount, ct) ?? 0m;

        // ม.65ตรี(12) — loss carry-in from effective (override ?? computed) taxable P/L history.
        var history = (await db.CitYearSummaries.AsNoTracking().ToListAsync(ct))
            .Where(s => s.EffectiveNetProfit is not null)
            .Select(s => (s.FiscalYear, s.EffectiveNetProfit!.Value))
            .ToList();
        var lossCarryIn = CitLossCarryForward.CarryInFor(fiscalYear, history);

        // §4.6 — SME = paid-up ≤ ฿5M AND revenue ≤ ฿30M; null capital ⇒ General (never silently SME).
        var isSme = c.PaidUpCapital is { } cap
            && cap <= 5_000_000m && pl.Totals.Revenue <= 30_000_000m;

        return new CitProfileDto(
            fiscalYear, c.PaidUpCapital, pl.Totals.Revenue, isSme,
            adjustmentsTotal, lossCarryIn, pl.Totals.NetProfit);
    }
}

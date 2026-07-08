using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Ledger;

/// <summary>
/// Year-end closing (specs/year-end-closing.md). Sweeps posted, non-closing Revenue/Expense
/// balances (RANGE-bounded to THIS fiscal year only, fiscalStart &lt;= DocDate &lt;= fiscalEnd —
/// §C sweep math, Tier-2 corrected 2026-07-08; NOT cumulative — a prior year's own close already
/// zeroed anything before this one) into the 3300 Retained Earnings account via a single JV
/// posted through
/// <see cref="IGlPostingService.PostClosingEntryAsync"/> (D3 — bypasses EnsureOpenAsync by
/// design: posting into an already-closed fiscal year is the point). "FY N is closed" ⇔ an
/// active (non-reversed) <see cref="FiscalYearClose"/> row exists (D7); mistake-recovery is a
/// Dr/Cr-swapped reversing JE (D4), never an edit of the original.
/// </summary>
public sealed class YearCloseService(
    AccountingDbContext db, ITenantContext tenant, IClock clock, IGlPostingService glPosting)
    : IYearCloseService
{
    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    /// <summary>Fiscal-year bounds + the 12 (year,month) pairs in fiscal order (D5 — mirrors
    /// CitYearDataService.FiscalBoundsAsync / FinancialStatementPdfService).</summary>
    private async Task<(short StartMonth, DateOnly Start, DateOnly End, List<(int Year, int Month)> Months)>
        FiscalBoundsAsync(int fiscalYear, CancellationToken ct)
    {
        var c = await db.Companies.AsNoTracking()
            .FirstOrDefaultAsync(x => x.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("company.not_found", "Company not found.");
        var start = new DateOnly(fiscalYear, c.FiscalYearStartMonth, 1);
        var end = start.AddYears(1).AddDays(-1);
        var months = Enumerable.Range(0, 12)
            .Select(i => start.AddMonths(i))
            .Select(d => (d.Year, d.Month))
            .ToList();
        return (c.FiscalYearStartMonth, start, end, months);
    }

    /// <summary>Loads the explicit AccountingPeriod rows for the given fiscal months, keyed by
    /// (year, month). D6 — a MISSING row is treated as not-closed here (unlike
    /// IPeriodCloseService.IsOpenAsync's implicit "past month = closed" rule); year close is a
    /// deliberate act on deliberately-closed periods only.</summary>
    private async Task<Dictionary<(int Year, int Month), AccountingPeriod>> LoadPeriodsAsync(
        List<(int Year, int Month)> months, CancellationToken ct)
    {
        var years = months.Select(m => m.Year).Distinct().ToList();
        var rows = await db.AccountingPeriods.AsNoTracking()
            .Where(p => years.Contains(p.Year))
            .ToListAsync(ct);
        return rows.ToDictionary(p => (p.Year, (int)p.Month), p => p);
    }

    public async Task<FiscalYearStatus> GetStatusAsync(int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();
        var (startMonth, start, end, months) = await FiscalBoundsAsync(fiscalYear, ct);
        var periodByYm = await LoadPeriodsAsync(months, ct);

        var periods = months.Select(m =>
        {
            periodByYm.TryGetValue(m, out var p);
            return new FiscalYearStatusPeriod(m.Year, m.Month, p?.Status.ToString() ?? "Open", p?.ClosedAt);
        }).ToList();
        var allClosed = months.All(m =>
            periodByYm.TryGetValue(m, out var p) && p.Status == PeriodStatus.Closed);

        var active = await db.FiscalYearCloses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FiscalYear == fiscalYear && x.ReversedAt == null, ct);

        return new FiscalYearStatus(
            fiscalYear, startMonth, start, end,
            active is not null, active?.ClosedAt, active?.ClosedBy, active?.Notes,
            active?.ClosingJournalId, active?.NetProfit,
            periods, allClosed);
    }

    public async Task<FiscalYearCloseResult> CloseAsync(int fiscalYear, string? notes, CancellationToken ct)
    {
        EnsureAuth();

        var already = await db.FiscalYearCloses
            .AnyAsync(x => x.FiscalYear == fiscalYear && x.ReversedAt == null, ct);
        if (already)
            throw new DomainException("year.already_closed", $"Fiscal year {fiscalYear} is already closed.");

        var (_, start, end, months) = await FiscalBoundsAsync(fiscalYear, ct);
        var periodByYm = await LoadPeriodsAsync(months, ct);
        var openMonths = months
            .Where(m => !(periodByYm.TryGetValue(m, out var p) && p.Status == PeriodStatus.Closed))
            .Select(m => $"{m.Year}-{m.Month:D2}")
            .ToList();
        if (openMonths.Count > 0)
            throw new DomainException("year.periods_not_closed",
                $"Months still open: {string.Join(", ", openMonths)}");

        // Sweep math (specs/year-end-closing.md §C, Tier-2 CORRECTION) — per revenue+expense
        // account, over posted NON-closing lines with DocDate IN [fiscalStart, fiscalEnd]. This
        // is a RANGE aggregation over THIS fiscal year only (mirrors ProfitLossAsync) — NOT
        // cumulative: without the lower bound, closing FY N+1 would re-sweep everything from FY
        // N too (the sweep excludes closing entries, so it can't see that FY N's close already
        // zeroed those accounts), storing a combined NetProfit and overstating RE.
        var sums = await (
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            join a in db.ChartOfAccounts.AsNoTracking() on l.AccountId equals a.AccountId
            where j.Status == DocumentStatus.Posted && !j.IsClosingEntry
                  && j.DocDate >= start && j.DocDate <= end
                  && (a.AccountType == AccountType.Revenue || a.AccountType == AccountType.Expense)
            group l by l.AccountId into g
            select new { AccountId = g.Key,
                         Debit = g.Sum(x => x.DebitAmount),
                         Credit = g.Sum(x => x.CreditAmount) })
            .ToListAsync(ct);

        var lines = new List<(long AccountId, decimal Debit, decimal Credit)>();
        decimal totalRawNet = 0m;
        foreach (var s in sums)
        {
            var rawNet = s.Debit - s.Credit;
            totalRawNet += rawNet;
            if (rawNet == 0m) continue;
            lines.Add(rawNet > 0 ? (s.AccountId, 0m, rawNet) : (s.AccountId, -rawNet, 0m));
        }

        var netProfit = -totalRawNet;
        if (totalRawNet != 0m)
        {
            var reAccountId = await db.ChartOfAccounts.AsNoTracking()
                .Where(a => a.AccountCode == "3300")
                .Select(a => (long?)a.AccountId)
                .FirstOrDefaultAsync(ct)
                ?? throw new DomainException("gl.account_missing",
                    "Retained earnings account 3300 is missing from chart_of_accounts. " +
                    "Seed it (see 611_seed_retained_earnings_account.sql / DefaultChartOfAccounts).");
            lines.Add(totalRawNet < 0 ? (reAccountId, 0m, -totalRawNet) : (reAccountId, totalRawNet, 0m));
        }

        var now = clock.UtcNow;
        long? closingJournalId = null;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (lines.Count >= 2 && lines.Sum(l => l.Debit) > 0m)
        {
            closingJournalId = await glPosting.PostClosingEntryAsync(
                tenant.CompanyId, tenant.BranchId, end,
                $"ปิดบัญชีสิ้นปี {fiscalYear} / Year-end closing FY {fiscalYear}",
                isClosingEntry: true, reversalOfId: null, lines, ct);
        }

        db.FiscalYearCloses.Add(new FiscalYearClose
        {
            CompanyId = tenant.CompanyId, FiscalYear = fiscalYear,
            FiscalStartDate = start, FiscalEndDate = end,
            NetProfit = netProfit, ClosingJournalId = closingJournalId,
            ClosedAt = now, ClosedBy = tenant.UserId, Notes = notes,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new FiscalYearCloseResult(fiscalYear, end, netProfit, closingJournalId, now);
    }

    public async Task ReopenAsync(int fiscalYear, CancellationToken ct)
    {
        EnsureAuth();

        var row = await db.FiscalYearCloses.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FiscalYear == fiscalYear && x.ReversedAt == null, ct)
            ?? throw new DomainException("year.not_closed", $"Fiscal year {fiscalYear} is not closed.");

        var now = clock.UtcNow;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Tier-2 (non-blocking, fixed together) — double-reopen guard. Win the reversed_at slot
        // FIRST via an affected-rows-checked conditional update: a concurrent ReopenAsync racing
        // on the same row loses this (0 rows affected) and fails cleanly with year.not_closed,
        // instead of both posting a reversing JE and swinging RE to -NetProfit.
        var claimed = await db.FiscalYearCloses
            .Where(x => x.FiscalYearCloseId == row.FiscalYearCloseId && x.ReversedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ReversedAt, now)
                .SetProperty(x => x.ReversedBy, tenant.UserId), ct);
        if (claimed == 0)
            throw new DomainException("year.not_closed", $"Fiscal year {fiscalYear} is not closed.");

        long? reversingJournalId = null;
        if (row.ClosingJournalId is { } closingId)
        {
            var original = await db.JournalEntries.AsNoTracking()
                .FirstOrDefaultAsync(j => j.JournalId == closingId, ct)
                ?? throw new DomainException("je.not_found", $"Closing journal {closingId} not found.");
            var originalLines = await db.JournalLines.AsNoTracking()
                .Where(l => l.JournalId == closingId)
                .ToListAsync(ct);
            var reversedLines = originalLines
                .Select(l => (l.AccountId, Debit: l.CreditAmount, Credit: l.DebitAmount))
                .ToList();

            reversingJournalId = await glPosting.PostClosingEntryAsync(
                original.CompanyId, original.BranchId, row.FiscalEndDate,
                $"กลับรายการปิดบัญชี {fiscalYear} / Reopen FY {fiscalYear}",
                isClosingEntry: true, reversalOfId: closingId, reversedLines, ct);

            await db.FiscalYearCloses
                .Where(x => x.FiscalYearCloseId == row.FiscalYearCloseId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.ReversingJournalId, reversingJournalId), ct);
        }

        await tx.CommitAsync(ct);
    }
}

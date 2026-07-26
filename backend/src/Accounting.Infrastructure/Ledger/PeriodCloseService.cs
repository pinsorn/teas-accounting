using Accounting.Application.Abstractions;
using Accounting.Application.Audit;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Ledger;

public sealed class PeriodCloseService : IPeriodCloseService
{
    private readonly AccountingDbContext _db;
    private readonly ITenantContext      _tenant;
    private readonly IClock              _clock;
    private readonly IActivityRecorder   _activity;

    public PeriodCloseService(
        AccountingDbContext db, ITenantContext tenant, IClock clock, IActivityRecorder activity)
    {
        _db = db; _tenant = tenant; _clock = clock; _activity = activity;
    }

    public async Task<bool> IsOpenAsync(int year, int month, CancellationToken ct)
    {
        var period = await _db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Year == year && p.Month == (short)month, ct);

        // §10 / ม.86/4(7) amplifier — an EXPLICIT period row is authoritative.
        if (period is not null)
            return period.Status == PeriodStatus.Open;

        // No row exists. Previously this was treated as OPEN-forever, which left every
        // never-opened past or future month unbounded-postable (arbitrary back/forward-dating).
        // A missing row is now OPEN only for the CURRENT Asia/Bangkok month — the legitimate
        // "this month, not yet closed" case. Every other missing month (a never-opened past
        // month, or any future month) is CLOSED. The 400-seed closes the previous month
        // explicitly, so a real row still governs that one; this only bounds the gaps.
        var today = _clock.TodayInBangkok();
        return year == today.Year && month == today.Month;
    }

    public async Task EnsureOpenAsync(DateOnly docDate, CancellationToken ct)
    {
        if (!await IsOpenAsync(docDate.Year, docDate.Month, ct))
            throw new DomainException("period.closed",
                $"Period {docDate.Year}-{docDate.Month:D2} is CLOSED. Reopen the period or correct doc_date.");
    }

    public async Task<PeriodCloseResult> CloseAsync(int year, int month, string? notes, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var (from, to) = (new DateOnly(year, month, 1),
                          new DateOnly(year, month, DateTime.DaysInMonth(year, month)));

        // Refuse close if any draft fiscal doc exists in the period.
        var draftTi = await _db.TaxInvoices
            .AnyAsync(t => t.DocDate >= from && t.DocDate <= to && t.Status == DocumentStatus.Draft, ct);
        var draftPv = await _db.PaymentVouchers
            .AnyAsync(p => p.DocDate >= from && p.DocDate <= to && p.Status == DocumentStatus.Draft, ct);
        var draftJe = await _db.JournalEntries
            .AnyAsync(j => j.DocDate >= from && j.DocDate <= to && j.Status == DocumentStatus.Draft, ct);
        if (draftTi || draftPv || draftJe)
            throw new DomainException("period.draft_present",
                "Cannot close period — draft fiscal documents still exist. Post or void them first.");

        // Fixed-assets hook (specs/fixed-assets.md §4): a month with assets due depreciation
        // cannot close until that month's depreciation run is posted. Minimal add — two reads
        // + one throw; no change to the close transaction or any other service.
        var depreciationDue = await _db.FixedAssets.AnyAsync(a =>
            a.Status == FixedAssetStatus.Active
            && a.DepreciationStartDate <= to
            && a.AccumulatedDepreciation < a.DepreciableBase, ct);
        if (depreciationDue)
        {
            var runPosted = await _db.DepreciationRuns.AnyAsync(r =>
                r.PeriodYear == year && r.PeriodMonth == month
                && r.Status == DepreciationRunStatus.Posted, ct);
            if (!runPosted)
                throw new DomainException("period.depreciation_required",
                    $"Depreciation for {year}-{month:D2} must be generated before closing this period.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var period = await _db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Year == year && p.Month == (short)month, ct);

        var now = _clock.UtcNow;
        if (period is null)
        {
            period = new AccountingPeriod
            {
                CompanyId = _tenant.CompanyId,
                Year = year, Month = (short)month,
                Status = PeriodStatus.Closed,
                ClosedAt = now,
                ClosedBy = _tenant.UserId,
                CloseNotes = notes,
            };
            _db.AccountingPeriods.Add(period);
        }
        else
        {
            if (period.Status == PeriodStatus.Closed)
                throw new DomainException("period.already_closed",
                    $"Period {year}-{month:D2} is already closed.");
            period.Status = PeriodStatus.Closed;
            period.ClosedAt = now;
            period.ClosedBy = _tenant.UserId;
            period.CloseNotes = notes;
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new PeriodCloseResult(year, month, now);
    }

    public async Task ReopenAsync(int year, int month, string? reason, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var periodStart = new DateOnly(year, month, 1);
        var periodEnd = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
        var fiscalYearClosed = await _db.FiscalYearCloses.AsNoTracking()
            .AnyAsync(x => x.ReversedAt == null
                           && x.FiscalStartDate <= periodStart
                           && x.FiscalEndDate >= periodEnd, ct);
        if (fiscalYearClosed)
            throw new DomainException("period.year_closed",
                $"Period {year}-{month:D2} is inside a closed fiscal year. Reopen the fiscal year first.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var periodId = await _db.AccountingPeriods.AsNoTracking()
            .Where(p => p.Year == year && p.Month == (short)month && p.Status == PeriodStatus.Closed)
            .Select(p => (long?)p.PeriodId)
            .FirstOrDefaultAsync(ct);

        var claimed = await _db.AccountingPeriods
            .Where(p => p.Year == year && p.Month == (short)month && p.Status == PeriodStatus.Closed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PeriodStatus.Open)
                .SetProperty(p => p.ClosedAt, (DateTimeOffset?)null)
                .SetProperty(p => p.ClosedBy, (long?)null)
                .SetProperty(p => p.CloseNotes, (string?)null), ct);
        if (claimed == 0 || periodId is null)
            throw new DomainException("period.not_closed",
                $"Period {year}-{month:D2} is not closed.");

        // Monthly close posts no journal entry, so monthly reopen has nothing to reverse.
        _activity.Record(
            "AccountingPeriod", periodId.Value, $"{year}-{month:D2}", _tenant.CompanyId,
            "Reopened", "Closed", "Open", reason, "gl");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

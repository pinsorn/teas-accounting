using Accounting.Application.Abstractions;
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

    public PeriodCloseService(AccountingDbContext db, ITenantContext tenant, IClock clock)
    {
        _db = db; _tenant = tenant; _clock = clock;
    }

    public async Task<bool> IsOpenAsync(int year, int month, CancellationToken ct)
    {
        var period = await _db.AccountingPeriods
            .FirstOrDefaultAsync(p => p.Year == year && p.Month == (short)month, ct);
        return period is null || period.Status == PeriodStatus.Open;
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
}

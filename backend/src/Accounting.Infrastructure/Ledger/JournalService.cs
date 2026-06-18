using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Ledger;

public sealed class JournalService : IJournalService
{
    private const string JvPrefix = "JV";

    private readonly AccountingDbContext      _db;
    private readonly ITenantContext           _tenant;
    private readonly IClock                   _clock;
    private readonly INumberSequenceService   _numbers;

    public JournalService(
        AccountingDbContext db,
        ITenantContext tenant,
        IClock clock,
        INumberSequenceService numbers)
    {
        _db      = db;
        _tenant  = tenant;
        _clock   = clock;
        _numbers = numbers;
    }

    public async Task<long> CreateDraftAsync(CreateJournalRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var totalDr = req.Lines.Sum(l => l.DebitAmount);
        var totalCr = req.Lines.Sum(l => l.CreditAmount);

        // §10 — a manual journal entry's DocDate / PostingDate are ALWAYS today in
        // Asia/Bangkok, never trusted from the request.
        var docDate = _clock.TodayInBangkok();

        var entity = new JournalEntry
        {
            CompanyId    = _tenant.CompanyId,
            BranchId     = _tenant.BranchId,
            PrefixCode   = JvPrefix,
            DocDate      = docDate,   // §10 — pinned to Asia/Bangkok today
            PostingDate  = docDate,   // §10 — posting date = doc date
            Description  = req.Description,
            Reference    = req.Reference,
            CurrencyCode = req.CurrencyCode,
            ExchangeRate = req.ExchangeRate,
            TotalDebit   = totalDr,
            TotalCredit  = totalCr,
            Lines = req.Lines.Select((l, i) => new JournalLine
            {
                LineNo         = i + 1,
                AccountId      = l.AccountId,
                DebitAmount    = l.DebitAmount,
                CreditAmount   = l.CreditAmount,
                Description    = l.Description,
                Reference      = l.Reference,
                DimensionsJson = l.DimensionsJson,
            }).ToList(),
        };

        _db.JournalEntries.Add(entity);
        await _db.SaveChangesAsync(ct);
        return entity.JournalId;
    }

    public async Task<JournalPostedResult> PostAsync(long journalId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var entry = await _db.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.JournalId == journalId, ct)
            ?? throw new DomainException("je.not_found", $"Journal {journalId} not found.");

        var docNo = await _numbers.NextAsync(
            entry.CompanyId, entry.BranchId, JvPrefix, subPrefix: null, entry.DocDate, ct);

        var now = _clock.UtcNow;
        entry.MarkPosted(docNo, _tenant.UserId ?? 0, now);

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return new JournalPostedResult(entry.JournalId, docNo, now);
    }
}

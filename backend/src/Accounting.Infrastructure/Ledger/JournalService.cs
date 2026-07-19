using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
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

        var now = _clock.UtcNow;

        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        var docNo = (await NumberedDocumentWriter.AllocateAndSaveAsync(
            _db,
            c => _numbers.NextAsync(entry.CompanyId, entry.BranchId, JvPrefix, subPrefix: null, entry.DocDate, c),
            (v, first) => { if (first) entry.MarkPosted(v.Value, _tenant.UserId ?? 0, now); else entry.DocNo = v.Value; },
            ct)).Value;
        await tx.CommitAsync(ct);

        return new JournalPostedResult(entry.JournalId, docNo, now);
    }

    public async Task<JournalDetail> GetDetailAsync(long journalId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var entry = await _db.JournalEntries.AsNoTracking()
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.JournalId == journalId, ct)
            ?? throw new DomainException("je.not_found", $"Journal {journalId} not found.");

        var accountIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.AsNoTracking()
            .Where(a => accountIds.Contains(a.AccountId))
            .ToDictionaryAsync(a => a.AccountId, ct);

        var lines = entry.Lines
            .OrderBy(l => l.LineNo)
            .Select(l =>
            {
                var a = accounts.GetValueOrDefault(l.AccountId);
                return new JournalDetailLine(
                    l.LineNo, l.AccountId, a?.AccountCode ?? "", a?.AccountNameTh ?? "",
                    l.Description, l.Reference, l.DebitAmount, l.CreditAmount, l.BusinessUnitId);
            })
            .ToList();

        return new JournalDetail(
            entry.JournalId, entry.DocNo, entry.DocDate, entry.PostingDate,
            entry.Description, entry.Reference, entry.Status.ToString(), entry.PostedAt,
            entry.ReversalOfId, lines, entry.TotalDebit, entry.TotalCredit);
    }
}

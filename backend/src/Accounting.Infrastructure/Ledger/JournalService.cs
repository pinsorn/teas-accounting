using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Accounting.Infrastructure.Ledger;

public sealed class JournalService : IJournalService
{
    private const string JvPrefix = "JV";

    private readonly AccountingDbContext      _db;
    private readonly ITenantContext           _tenant;
    private readonly IClock                   _clock;
    private readonly INumberSequenceService   _numbers;
    private readonly IGlPostingService        _gl;
    private readonly IPeriodCloseService      _period;
    private readonly IOptions<GlAccountsOptions> _glAccounts;

    public JournalService(
        AccountingDbContext db,
        ITenantContext tenant,
        IClock clock,
        INumberSequenceService numbers,
        IGlPostingService gl,
        IPeriodCloseService period,
        IOptions<GlAccountsOptions> glAccounts)
    {
        _db      = db;
        _tenant  = tenant;
        _clock   = clock;
        _numbers = numbers;
        _gl      = gl;
        _period  = period;
        _glAccounts = glAccounts;
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

    /// <summary>
    /// R3/H2 — thin wrapper so a concurrent double-post race maps to a clean 409 instead of a
    /// raw 500. JournalEntry.Version is never incremented by MarkPosted (unlike PaymentVoucher
    /// post-WP-B), so EF's own optimistic-concurrency check never actually diverges between two
    /// racing writers — what stops the loser from corrupting the winner's already-committed row
    /// is gl.trg_je_immutable (020_journal_immutability.sql), which raises a raw 23514
    /// check_violation when the loser's UPDATE tries to change doc_no/status on a row that is
    /// now POSTED. Also covers the (currently unreachable, since Version is inert here) case of
    /// a genuine DbUpdateConcurrencyException, mirroring the PV pattern.
    /// </summary>
    public async Task<JournalPostedResult> PostAsync(long journalId, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        try
        {
            return await PostCoreAsync(journalId, ct);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsPostedRaceViolation(ex))
        {
            throw new DomainException("je.locked_mismatch",
                "This journal entry was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>gl.trg_je_immutable / sales.fn_enforce_ti_immutability / .._receipt_immutability
    /// all RAISE EXCEPTION with ERRCODE 'check_violation' (23514) when a second writer's UPDATE
    /// tries to touch a critical field on a row another writer already flipped to Posted.</summary>
    private static bool IsPostedRaceViolation(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: "23514" } };

    private async Task<JournalPostedResult> PostCoreAsync(long journalId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var entry = await _db.JournalEntries
            .Include(j => j.Lines)
            .FirstOrDefaultAsync(j => j.JournalId == journalId, ct)
            ?? throw new DomainException("je.not_found", $"Journal {journalId} not found.");

        // Tier-2 R2b (mcp-manual-journal.md §10) — CreateDraftAsync/the REST /journals/ POST
        // route perform NO account or period validation at all (§1), so a bad draft (forged/
        // foreign/header/inactive account, or a date that later fell inside a closed period)
        // could otherwise sail straight to Posted. These are the SAME gates
        // CreateAndPostManualAsync already runs, extracted so both paths share one
        // implementation instead of duplicating it.
        await ValidatePostableAccountsAsync(entry.Lines.Select(l => l.AccountId), ct);
        await EnsurePostableDateAsync(entry.DocDate, ct);

        var now = _clock.UtcNow;

        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500.
        var docNo = (await NumberedDocumentWriter.AllocateAndSaveAsync(
            _db,
            c => _numbers.NextAsync(entry.CompanyId, JvPrefix, subPrefix: null, entry.DocDate, c),
            (v, first) => { if (first) entry.MarkPosted(v.Value, _tenant.UserId ?? 0, now); else entry.DocNo = v.Value; },
            ct)).Value;
        await tx.CommitAsync(ct);

        // F1 (specs/fix-pnd36-payment-detection.md §3.7/§2 C8) — this is the path an MCP-drafted
        // entry reaches when a human posts it; the advisory must fire here too, not only on
        // CreateAndPostManualAsync.
        var advisory = await Pnd36AdvisoryAsync(
            entry.Lines.Select(l => (l.AccountId, l.DebitAmount)), ct);
        return new JournalPostedResult(entry.JournalId, docNo, now, advisory);
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

    /// <summary>Manual JV (specs/manual-jv-and-coa-management.md §B0/B1/B4) — create AND post in
    /// one call. See §B0 for why there is no draft state.</summary>
    public async Task<JournalPostedResult> CreateAndPostManualAsync(
        CreateManualJournalRequest req, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        // --- date bounds (spec §B1) ---------------------------------------------------------
        if (req.DocDate > _clock.TodayInBangkok())
            throw new DomainException("je.future_date", "A journal cannot be dated in the future.");
        await EnsurePostableDateAsync(req.DocDate, ct);

        // --- postable-account gate (spec §0.3; mirrors BankReconciliationService.cs:224-233) ---
        var ids = req.Lines.Select(l => l.AccountId);
        var accounts = await ValidatePostableAccountsAsync(ids, ct);

        // --- business-unit gate (Tier-2 F1/F1b) -----------------------------------------------
        // gl.journal_lines.business_unit_id carries a real FK to master.business_units, but a
        // Postgres FK check runs as the table owner and bypasses RLS (same reasoning as the
        // account gate above / spec §0.3/§A1b) — so a forged/foreign BU id is never caught by
        // the DB. ONE tenant-scoped batched query covers every line's BU (F1).
        var buIds = req.Lines.Where(l => l.BusinessUnitId is not null)
            .Select(l => l.BusinessUnitId!.Value).Distinct().ToList();
        var validBuIds = buIds.Count == 0
            ? new HashSet<int>()
            : (await _db.BusinessUnits.AsNoTracking()
                .Where(b => buIds.Contains(b.BusinessUnitId) && b.CompanyId == _tenant.CompanyId && b.IsActive)
                .Select(b => b.BusinessUnitId)
                .ToListAsync(ct)).ToHashSet();

        // F1b — Company.RequiresBusinessUnit: a Revenue/Expense line must carry a BU; a
        // balance-sheet line (asset/liability/equity) stays exempt (BU only concerns P&L
        // reporting — a director-loan entry touches bank + a liability, where BU is meaningless).
        var requiresBu = await _db.Companies.AsNoTracking()
            .Where(c => c.CompanyId == _tenant.CompanyId)
            .Select(c => c.RequiresBusinessUnit).FirstAsync(ct);

        foreach (var l in req.Lines)
        {
            if (l.BusinessUnitId is { } buId && !validBuIds.Contains(buId))
                throw new DomainException("bu.invalid", $"Business Unit {buId} not found or inactive.");

            if (requiresBu && l.BusinessUnitId is null)
            {
                var acct = accounts[l.AccountId];   // already confirmed present above
                if (acct.AccountType is AccountType.Revenue or AccountType.Expense)
                    throw new DomainException("bu.required",
                        $"Account {acct.AccountCode} is a revenue/expense account — " +
                        "Business Unit is required for this company.");
            }
        }

        // --- post through the SAME seam every other document uses ----------------------------
        var journalId = await _gl.PostManualEntryAsync(
            _tenant.CompanyId, _tenant.BranchId, req.DocDate, req.Description, req.Reference,
            req.Lines.Select(l => new ManualJvLine(
                l.AccountId, l.DebitAmount, l.CreditAmount, l.Description, l.BusinessUnitId)).ToList(),
            ct);

        var entry = await _db.JournalEntries.AsNoTracking()
            .FirstAsync(j => j.JournalId == journalId, ct);

        // F1 (specs/fix-pnd36-payment-detection.md §3.7) — at-source completeness advisory.
        var advisory = await Pnd36AdvisoryAsync(
            req.Lines.Select(l => (l.AccountId, l.DebitAmount)), ct);
        return new JournalPostedResult(journalId, entry.DocNo!, entry.PostedAt!.Value, advisory);
    }

    // F1 (specs/fix-pnd36-payment-detection.md §3.7) — shared by CreateAndPostManualAsync and
    // PostAsync (§2 C8: an MCP-drafted entry posted by a human takes PostAsync). Gate 1 (any
    // posted line debits AP) is cheap and the overwhelmingly common answer is "no" — skip gate 2
    // entirely in that case, no cost on the common path. Gate 2 is what keeps this from becoming
    // noise: a company with no outstanding foreign-service invoices never sees it.
    //
    // R3/F1 Tier-2 remediation (FIX 2) — both call sites invoke this AFTER their post already
    // committed (PostAsync: after tx.CommitAsync; CreateAndPostManualAsync: after PostManualEntryAsync
    // returns), so there is nothing left to roll back if this throws — a connection blip or a
    // cancelled request here would turn a SUCCESSFUL post into a 500, and the caller's retry would
    // create a SECOND posted JV, manufacturing the very duplicate-clearing state this advisory
    // exists to warn about (journals/manual has no idempotency key). An advisory must never change
    // the outcome of an already-committed post — swallow ANY exception (including
    // OperationCanceledException) and degrade to "no advisory" rather than fail the request.
    // DELIBERATE — do not "improve" this into a rethrow.
    private async Task<string?> Pnd36AdvisoryAsync(
        IEnumerable<(long AccountId, decimal DebitAmount)> lines, CancellationToken ct)
    {
        try
        {
            var apCode = _glAccounts.Value.ApAccount;
            var apAccountId = await _db.ChartOfAccounts.AsNoTracking()
                .Where(a => a.AccountCode == apCode)
                .Select(a => (long?)a.AccountId)
                .FirstOrDefaultAsync(ct);
            if (apAccountId is null || !lines.Any(l => l.AccountId == apAccountId && l.DebitAmount > 0m))
                return null;

            var hasOutstanding = await _db.VendorInvoices.AsNoTracking()
                .AnyAsync(v => v.RequiresPnd36ReverseCharge
                            && v.Status == DocumentStatus.Posted
                            && v.SettledAmount < v.TotalAmount - 0.01m, ct);
            return hasOutstanding ? "pnd36.ap_cleared_outside_pv" : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<JournalListItem>> ListAsync(
        DateOnly? from, DateOnly? to, string? search, int page, int pageSize, CancellationToken ct)
    {
        if (!_tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 200);

        var q = _db.JournalEntries.AsNoTracking().AsQueryable();
        if (from is { } f) q = q.Where(j => j.DocDate >= f);
        if (to   is { } t) q = q.Where(j => j.DocDate <= t);
        if (!string.IsNullOrWhiteSpace(search))
            q = q.Where(j => (j.DocNo != null && EF.Functions.ILike(j.DocNo, $"%{search}%"))
                          || EF.Functions.ILike(j.Description, $"%{search}%"));
        return await q.OrderByDescending(j => j.DocDate).ThenByDescending(j => j.JournalId)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(j => new JournalListItem(j.JournalId, j.DocNo, j.DocDate, j.Description, j.Reference,
                j.Status.ToString(), j.TotalDebit, j.TotalCredit, j.IsClosingEntry))
            .ToListAsync(ct);
    }

    /// <summary>Postable-account gate (spec §0.3; Tier-2 R2b) — shared by
    /// <see cref="CreateAndPostManualAsync"/> and <see cref="PostAsync"/>. journal_lines has no
    /// company_id and no FK to chart_of_accounts, so RLS cannot stop a forged/foreign/header/
    /// inactive account id. ONE tenant-scoped read covers all lines. Returns the account
    /// dictionary so callers needing account metadata (e.g. the BU-required check) don't
    /// re-query.</summary>
    private async Task<Dictionary<long, ChartOfAccount>> ValidatePostableAccountsAsync(
        IEnumerable<long> accountIds, CancellationToken ct)
    {
        var ids = accountIds.Distinct().ToList();
        var accounts = await _db.ChartOfAccounts.AsNoTracking()
            .Where(a => ids.Contains(a.AccountId) && a.CompanyId == _tenant.CompanyId)
            .ToDictionaryAsync(a => a.AccountId, ct);
        foreach (var id in ids)
        {
            if (!accounts.TryGetValue(id, out var a))          // missing OR another tenant's — same 404-ish answer
                throw new DomainException("je.account_not_found", $"Account {id} not found.");
            if (!a.IsActive)
                throw new DomainException("je.account_inactive", $"Account {a.AccountCode} is not active.");
            if (a.IsHeader)
                throw new DomainException("je.account_is_header",
                    $"Account {a.AccountCode} is a header account — pick a postable (non-header) account.");
        }
        return accounts;
    }

    /// <summary>Period/fiscal-year gate for posting (Tier-2 R2b) — shared by
    /// <see cref="CreateAndPostManualAsync"/> and <see cref="PostAsync"/>.</summary>
    private async Task EnsurePostableDateAsync(DateOnly docDate, CancellationToken ct)
    {
        await _period.EnsureOpenAsync(docDate, ct);            // throws period.closed
        var fyClosed = await _db.FiscalYearCloses.AsNoTracking()
            .AnyAsync(x => x.ReversedAt == null
                        && x.FiscalStartDate <= docDate && x.FiscalEndDate >= docDate, ct);
        if (fyClosed)
            throw new DomainException("je.year_closed",
                "This date is inside a closed fiscal year. Reopen the fiscal year first.");
    }
}

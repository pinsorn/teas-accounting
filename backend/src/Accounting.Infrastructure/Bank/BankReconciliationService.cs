using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Bank;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Bank;

/// <summary>
/// Bank reconciliation (specs/bank-reconciliation.md B4.2) — suggest / confirm / unmatch /
/// create-inline-JE / ignore. Enforces D4 (matching), D8 (lifecycle + immutability), and
/// period-close before posting.
///
/// Concurrency: every state-transition (confirm/unmatch/journal/ignore/unignore) claims the row
/// via a conditional <c>ExecuteUpdateAsync</c> keyed on the CURRENT match_status — mirrors
/// <c>YearCloseService.ReopenAsync</c>'s double-reopen guard (win the transition first via an
/// affected-rows-checked update; a racing request loses cleanly with a domain error instead of
/// silently clobbering the other's write). <see cref="CreateJournalAsync"/> additionally wraps
/// the JE post + the claim in ONE DB transaction: if the claim loses the race AFTER the JE was
/// inserted, the whole transaction rolls back (undoing the JE insert too — nothing was committed,
/// so JE immutability is never actually violated) rather than leaving an orphaned posted JE.
/// KNOWN RESIDUAL GAP (documented, not silently ignored): the "not already matched to another
/// line" pre-check in <see cref="ConfirmMatchAsync"/> is NOT itself claimed atomically against a
/// concurrent confirm of a DIFFERENT line to the SAME document — only the named race (two
/// confirms racing on the SAME line) is closed. A DB-level partial-unique constraint would close
/// it fully but needs a migration, out of this stage's blast radius.
/// </summary>
public sealed class BankReconciliationService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    IPeriodCloseService period, IGlPostingService gl)
    : IBankReconciliationService
{
    private const int MatchWindowDays = 7;   // D4 — ±7 days; amount matching is EXACT (==), no tolerance const needed

    private void EnsureAuth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    private async Task<StatementLine> GetLineAsync(long lineId, CancellationToken ct) =>
        await db.StatementLines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId, ct)
        ?? throw new DomainException("bank.line_not_found", $"Statement line {lineId} not found.");

    public async Task<IReadOnlyList<MatchSuggestion>> SuggestAsync(long lineId, CancellationToken ct)
    {
        EnsureAuth();
        var line = await GetLineAsync(lineId, ct);
        var from = line.TxnDate.AddDays(-MatchWindowDays);
        var to = line.TxnDate.AddDays(MatchWindowDays);

        if (line.Direction == StatementDirection.MoneyIn)
        {
            var matchedIds = await db.StatementLines.AsNoTracking()
                .Where(x => x.CompanyId == tenant.CompanyId && x.MatchedReceiptId != null)
                .Select(x => x.MatchedReceiptId!.Value)
                .ToListAsync(ct);

            var candidates = await db.Receipts.AsNoTracking()
                .Where(r => r.CompanyId == tenant.CompanyId && r.Status == DocumentStatus.Posted
                    && r.CashReceived == line.Amount
                    && r.DocDate >= from && r.DocDate <= to
                    && !matchedIds.Contains(r.ReceiptId))
                .ToListAsync(ct);

            return Rank(candidates, r => r.BankAccountId, r => r.DocDate, line)
                .Select(r => new MatchSuggestion("Receipt", r.ReceiptId, r.DocNo, r.DocDate, r.CashReceived, r.CustomerName))
                .ToList();
        }
        else
        {
            var matchedIds = await db.StatementLines.AsNoTracking()
                .Where(x => x.CompanyId == tenant.CompanyId && x.MatchedPaymentVoucherId != null)
                .Select(x => x.MatchedPaymentVoucherId!.Value)
                .ToListAsync(ct);

            var candidates = await db.PaymentVouchers.AsNoTracking()
                .Where(p => p.CompanyId == tenant.CompanyId && p.Status == DocumentStatus.Posted
                    && p.TotalPaid == line.Amount
                    && p.DocDate >= from && p.DocDate <= to
                    && !matchedIds.Contains(p.PaymentVoucherId))
                .ToListAsync(ct);

            return Rank(candidates, p => p.BankAccountId, p => p.DocDate, line)
                .Select(p => new MatchSuggestion("PaymentVoucher", p.PaymentVoucherId, p.DocNo, p.DocDate, p.TotalPaid, p.VendorName))
                .ToList();
        }
    }

    /// <summary>D4 ranking: a candidate whose OWN BankAccountId is populated AND matches the
    /// line's bank account is preferred (never hard-filtered — BankAccountId is often null on
    /// the source doc); then exact-date first, nearest date next (a single ascending sort on the
    /// absolute day difference already puts a 0-day/exact match first).</summary>
    private static List<T> Rank<T>(
        List<T> candidates, Func<T, long?> bankAccountIdOf, Func<T, DateOnly> docDateOf, StatementLine line) =>
        candidates
            .OrderByDescending(c => bankAccountIdOf(c) is { } id && id == line.BankAccountId)
            .ThenBy(c => Math.Abs(docDateOf(c).DayNumber - line.TxnDate.DayNumber))
            .ToList();

    public async Task ConfirmMatchAsync(long lineId, ConfirmMatchRequest req, CancellationToken ct)
    {
        EnsureAuth();
        if ((req.ReceiptId is null) == (req.PaymentVoucherId is null))
            throw new DomainException("bank.match_invalid",
                "Exactly one of receiptId/paymentVoucherId is required.");

        var line = await GetLineAsync(lineId, ct);

        if (req.ReceiptId is { } rid)
        {
            if (line.Direction != StatementDirection.MoneyIn)
                throw new DomainException("bank.match_wrong_doc_type", "A MoneyOut line cannot match a Receipt.");
            var rc = await db.Receipts.AsNoTracking()
                    .FirstOrDefaultAsync(r => r.ReceiptId == rid && r.CompanyId == tenant.CompanyId, ct)
                ?? throw new DomainException("bank.doc_not_found", $"Receipt {rid} not found.");
            if (rc.Status != DocumentStatus.Posted)
                throw new DomainException("bank.doc_not_posted", $"Receipt {rid} is not Posted.");
            if (rc.CashReceived != line.Amount)
                throw new DomainException("bank.amount_mismatch",
                    $"Receipt cash received {rc.CashReceived} does not equal the line amount {line.Amount}.");
            if (await db.StatementLines.AsNoTracking()
                    .AnyAsync(x => x.CompanyId == tenant.CompanyId && x.MatchedReceiptId == rid
                        && x.StatementLineId != lineId, ct))
                throw new DomainException("bank.doc_already_matched", $"Receipt {rid} is already matched to another line.");
        }
        else
        {
            var pid = req.PaymentVoucherId!.Value;
            if (line.Direction != StatementDirection.MoneyOut)
                throw new DomainException("bank.match_wrong_doc_type", "A MoneyIn line cannot match a Payment Voucher.");
            var pv = await db.PaymentVouchers.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.PaymentVoucherId == pid && p.CompanyId == tenant.CompanyId, ct)
                ?? throw new DomainException("bank.doc_not_found", $"Payment Voucher {pid} not found.");
            if (pv.Status != DocumentStatus.Posted)
                throw new DomainException("bank.doc_not_posted", $"Payment Voucher {pid} is not Posted.");
            if (pv.TotalPaid != line.Amount)
                throw new DomainException("bank.amount_mismatch",
                    $"Payment Voucher total paid {pv.TotalPaid} does not equal the line amount {line.Amount}.");
            if (await db.StatementLines.AsNoTracking()
                    .AnyAsync(x => x.CompanyId == tenant.CompanyId && x.MatchedPaymentVoucherId == pid
                        && x.StatementLineId != lineId, ct))
                throw new DomainException("bank.doc_already_matched", $"Payment Voucher {pid} is already matched to another line.");
        }

        var now = clock.UtcNow;
        int claimed;
        try
        {
            claimed = await db.StatementLines
                .Where(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId
                    && x.MatchStatus == MatchStatus.Unmatched)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.MatchStatus, MatchStatus.Matched)
                    .SetProperty(x => x.MatchedReceiptId, req.ReceiptId)
                    .SetProperty(x => x.MatchedPaymentVoucherId, req.PaymentVoucherId)
                    .SetProperty(x => x.MatchedAt, now)
                    .SetProperty(x => x.MatchedBy, tenant.UserId), ct);
        }
        // Codex review finding #4 (2026-07-10) — the pre-checks above are a read-then-write race;
        // the partial unique indexes on matched_receipt_id/matched_payment_voucher_id are the hard
        // backstop. ExecuteUpdateAsync bypasses the change-tracker SaveChanges pipeline, so a
        // constraint violation surfaces as a RAW provider exception, not a wrapped
        // DbUpdateException — catch both shapes defensively.
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
        {
            throw new DomainException("bank.doc_already_matched",
                "This document was just matched to another line by a concurrent request.");
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            throw new DomainException("bank.doc_already_matched",
                "This document was just matched to another line by a concurrent request.");
        }
        if (claimed == 0)
            throw new DomainException("bank.line_not_unmatched",
                "This line is no longer Unmatched (concurrent update) — confirm was not applied.");
    }

    public async Task UnmatchAsync(long lineId, CancellationToken ct)
    {
        EnsureAuth();
        var line = await GetLineAsync(lineId, ct);

        // D8 — Posted (JE-backed) is a real, immutable GL entry; unmatch is blocked outright,
        // before even attempting the conditional claim (a clearer error than a generic race message).
        if (line.MatchStatus == MatchStatus.Posted)
            throw new DomainException("bank.line_posted",
                $"Line posted as JE #{line.PostedJournalId}; post a manual adjusting JE to correct.");

        var claimed = await db.StatementLines
            .Where(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId
                && x.MatchStatus == MatchStatus.Matched)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MatchStatus, MatchStatus.Unmatched)
                .SetProperty(x => x.MatchedReceiptId, (long?)null)
                .SetProperty(x => x.MatchedPaymentVoucherId, (long?)null)
                .SetProperty(x => x.MatchedAt, (DateTimeOffset?)null)
                .SetProperty(x => x.MatchedBy, (long?)null), ct);
        if (claimed == 0)
            throw new DomainException("bank.line_not_matched", "This line is not currently Matched.");
    }

    public async Task<CreateInlineJournalResult> CreateJournalAsync(
        long lineId, CreateInlineJournalRequest req, CancellationToken ct)
    {
        EnsureAuth();
        var line = await GetLineAsync(lineId, ct);
        if (line.MatchStatus != MatchStatus.Unmatched)
            throw new DomainException("bank.line_not_unmatched", "Only an Unmatched line can get an inline JE.");

        var bankAccount = await db.BankAccounts.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BankAccountId == line.BankAccountId && b.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank_account.not_found", $"Bank account {line.BankAccountId} not found.");

        // Opus Tier-2 fix (2026-07-09) — journal_lines.account_id has NO DB-level FK (mirrors
        // every other poster's ResolveAccountIdAsync-style trust boundary), so a forged/foreign/
        // header account id would otherwise post SILENTLY and mis-roll the trial balance. Load
        // the contra account TENANT-SCOPED and reject before building glLines.
        var contraAccount = await db.ChartOfAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == req.ContraAccountId && a.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank.contra_account_not_found",
                $"Contra account {req.ContraAccountId} not found.");
        if (!contraAccount.IsActive)
            throw new DomainException("bank.contra_account_inactive",
                $"Contra account {req.ContraAccountId} is not active.");
        if (contraAccount.IsHeader)
            throw new DomainException("bank.contra_account_is_header",
                $"Contra account {req.ContraAccountId} is a header account — pick a postable (non-header) account.");

        // D7 — the RECON SERVICE enforces period-close; PostManualEntryAsync itself does not.
        await period.EnsureOpenAsync(line.TxnDate, ct);

        var glLines = line.Direction == StatementDirection.MoneyIn
            ? new List<(long AccountId, decimal Debit, decimal Credit)>
              {
                  (bankAccount.GlCashAccountId, line.Amount, 0m),
                  (req.ContraAccountId, 0m, line.Amount),
              }
            : new List<(long AccountId, decimal Debit, decimal Credit)>
              {
                  (req.ContraAccountId, line.Amount, 0m),
                  (bankAccount.GlCashAccountId, 0m, line.Amount),
              };
        var description = string.IsNullOrWhiteSpace(req.Description)
            ? $"{line.TxnType} — {line.Description}".Trim(' ', '—')
            : req.Description;

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var journalId = await gl.PostManualEntryAsync(
            tenant.CompanyId, tenant.BranchId, line.TxnDate, description, line.RawRef, glLines, ct);

        var now = clock.UtcNow;
        var claimed = await db.StatementLines
            .Where(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId
                && x.MatchStatus == MatchStatus.Unmatched)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.MatchStatus, MatchStatus.Posted)
                .SetProperty(x => x.PostedJournalId, journalId)
                .SetProperty(x => x.MatchedAt, now)
                .SetProperty(x => x.MatchedBy, tenant.UserId), ct);
        if (claimed == 0)
        {
            // Concurrent transition won the line first — roll back the WHOLE transaction so the
            // JE insert above is undone too (never committed ⇒ JE immutability is not violated;
            // the consumed JV number is a legitimate, tracked gap, same class as any other
            // rolled-back post).
            await tx.RollbackAsync(ct);
            throw new DomainException("bank.line_not_unmatched",
                "This line is no longer Unmatched (concurrent update) — the journal entry was not committed.");
        }

        await tx.CommitAsync(ct);
        return new CreateInlineJournalResult(journalId);
    }

    public async Task IgnoreAsync(long lineId, CancellationToken ct)
    {
        EnsureAuth();
        var claimed = await db.StatementLines
            .Where(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId
                && x.MatchStatus == MatchStatus.Unmatched)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MatchStatus, MatchStatus.Ignored), ct);
        if (claimed == 0)
            throw new DomainException("bank.line_not_unmatched", "Only an Unmatched line can be ignored.");
    }

    public async Task UnignoreAsync(long lineId, CancellationToken ct)
    {
        EnsureAuth();
        var claimed = await db.StatementLines
            .Where(x => x.StatementLineId == lineId && x.CompanyId == tenant.CompanyId
                && x.MatchStatus == MatchStatus.Ignored)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MatchStatus, MatchStatus.Unmatched), ct);
        if (claimed == 0)
            throw new DomainException("bank.line_not_ignored", "This line is not currently Ignored.");
    }
}

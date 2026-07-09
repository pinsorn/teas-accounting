using Accounting.Application.Abstractions;
using Accounting.Application.Bank;
using Accounting.Domain.Common;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Bank;

/// <summary>Bank reconciliation (specs/bank-reconciliation.md B5.1, §Reconciliation report math).</summary>
public sealed class BankReconciliationReportService(AccountingDbContext db, ITenantContext tenant)
    : IBankReconciliationReportService
{
    public async Task<BankReconciliationReport> GetAsync(
        int bankAccountId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");

        var bankAccount = await db.BankAccounts.AsNoTracking()
                .FirstOrDefaultAsync(b => b.BankAccountId == bankAccountId && b.CompanyId == tenant.CompanyId, ct)
            ?? throw new DomainException("bank_account.not_found", $"Bank account {bankAccountId} not found.");

        // Statement closing balance — the latest import whose period ends on/before `to`.
        var statementClosingBalance = await db.StatementImports.AsNoTracking()
            .Where(i => i.BankAccountId == bankAccountId && i.CompanyId == tenant.CompanyId && i.PeriodEnd <= to)
            .OrderByDescending(i => i.PeriodEnd)
            .Select(i => (decimal?)i.ClosingBalance)
            .FirstOrDefaultAsync(ct) ?? 0m;

        // GL balance of gl_cash_account_id as-of `to` — same shape as
        // FinancialReportService.TrialBalanceAsync (Posted journals, DocDate <= asOf, Dr-Cr for an asset).
        var glLines = await (
            from l in db.JournalLines.AsNoTracking()
            join j in db.JournalEntries.AsNoTracking() on l.JournalId equals j.JournalId
            where l.AccountId == bankAccount.GlCashAccountId
                && j.Status == DocumentStatus.Posted && j.DocDate <= to
            select new { l.DebitAmount, l.CreditAmount })
            .ToListAsync(ct);
        var glBalance = glLines.Sum(x => x.DebitAmount) - glLines.Sum(x => x.CreditAmount);

        // (a) Unmatched statement lines, for THIS bank account. CUMULATIVE (TxnDate <= to only,
        // NO lower bound) — GL balance and statement balance above are both cumulative-as-of-`to`,
        // so a reconciling item must be too, or an item dated before `from` silently drops out of
        // BOTH the list and the Difference math while still being baked into the two balances,
        // leaving a permanently nonzero, unexplained Difference (Fable cross-review finding,
        // 2026-07-09 — `from` defaulted to the current month start in the FE, so this fired every
        // month after the first). `from`/`to` stay on the DTO/route for display filtering only.
        var unmatchedLines = await db.StatementLines.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId && x.BankAccountId == bankAccountId
                && x.MatchStatus == MatchStatus.Unmatched && x.TxnDate <= to)
            .ToListAsync(ct);
        var unmatchedLinesNet = unmatchedLines.Sum(x => x.Direction == StatementDirection.MoneyIn ? x.Amount : -x.Amount);

        // (b) Deposits-in-transit — POSTED Receipts, CUMULATIVE (DocDate <= to only — see (a)),
        // not matched to ANY statement line (matching is company-wide, not scoped to
        // bankAccountId — D6's single-1120-account model).
        var matchedReceiptIds = await db.StatementLines.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId && x.MatchedReceiptId != null)
            .Select(x => x.MatchedReceiptId!.Value)
            .ToListAsync(ct);
        var depositsInTransit = await db.Receipts.AsNoTracking()
            .Where(r => r.CompanyId == tenant.CompanyId && r.Status == DocumentStatus.Posted
                && r.DocDate <= to && !matchedReceiptIds.Contains(r.ReceiptId))
            .ToListAsync(ct);

        // (c) Outstanding payments — POSTED PVs, CUMULATIVE (DocDate <= to only — see (a)),
        // not matched to ANY statement line.
        var matchedPvIds = await db.StatementLines.AsNoTracking()
            .Where(x => x.CompanyId == tenant.CompanyId && x.MatchedPaymentVoucherId != null)
            .Select(x => x.MatchedPaymentVoucherId!.Value)
            .ToListAsync(ct);
        var outstandingPayments = await db.PaymentVouchers.AsNoTracking()
            .Where(p => p.CompanyId == tenant.CompanyId && p.Status == DocumentStatus.Posted
                && p.DocDate <= to && !matchedPvIds.Contains(p.PaymentVoucherId))
            .ToListAsync(ct);

        var depositsTotal = depositsInTransit.Sum(r => r.CashReceived);
        var outstandingTotal = outstandingPayments.Sum(p => p.TotalPaid);

        // Tie-out (spec corrected 2026-07-09 — the original pinned formula was sign-flipped on
        // deposits/outstanding; T10 written against it was circular, self-consistent but wrong):
        // GL - deposits-in-transit + outstanding-payments +/- unmatched == statement.
        // A deposit-in-transit is already IN GL (the Receipt posted) but NOT yet on the bank
        // statement -> statement is LOWER than GL by that amount, so it SUBTRACTS from GL to
        // reach statement. An outstanding payment is the mirror: already IN GL (the PV posted,
        // decreasing GL) but NOT yet cleared on the statement -> statement is HIGHER than GL by
        // that amount (GL already took the hit, statement hasn't), so it ADDS back.
        var difference = statementClosingBalance - (glBalance - depositsTotal + outstandingTotal + unmatchedLinesNet);

        return new BankReconciliationReport(
            bankAccountId, from, to, statementClosingBalance, glBalance,
            depositsTotal, outstandingTotal, unmatchedLinesNet, difference,
            unmatchedLines
                .OrderBy(x => x.TxnDate)
                .Select(x => new ReconciliationReportItem(
                    $"{x.TxnType} — {x.Description}", x.TxnDate,
                    x.Direction == StatementDirection.MoneyIn ? x.Amount : -x.Amount))
                .ToList(),
            depositsInTransit
                .OrderBy(r => r.DocDate)
                .Select(r => new ReconciliationReportItem($"{r.DocNo ?? $"#{r.ReceiptId}"} — {r.CustomerName}", r.DocDate, r.CashReceived))
                .ToList(),
            outstandingPayments
                .OrderBy(p => p.DocDate)
                .Select(p => new ReconciliationReportItem($"{p.DocNo ?? $"#{p.PaymentVoucherId}"} — {p.VendorName}", p.DocDate, p.TotalPaid))
                .ToList());
    }
}

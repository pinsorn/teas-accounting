using Accounting.Application.Abstractions;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Accounting.Infrastructure.Ledger;

/// <summary>
/// R1/C6 (WP-2, specs/fix-breakit-r1-ledger-integrity.md §3.2.5 — REDESIGNED 2026-07-31; that
/// section is the single source of truth). Candidates: BillingNotes with
/// <c>JournalEntryId IS NULL</c> (issued before WP-1 shipped, so the accrual never posted) that
/// are Issued or Settled (never Draft/Cancelled) AND still have an outstanding balance —
/// <c>JournalEntryId IS NULL</c> doubles as the idempotency/resume key by construction: once an
/// invoice is corrected, it drops out of every future candidate query forever.
/// </summary>
public sealed class NonVatArBackfillService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    ICompanyTaxConfigService taxCfg, IGlPostingService gl, IPeriodCloseService period,
    IOptions<GlAccountsOptions> accounts)
    : INonVatArBackfillService
{
    // Tags every correcting JE so AlreadyDone/ResumedFrom can be recomputed on ANY invocation
    // (including a fresh preview after a prior apply run) without a separate tracking table —
    // greppable, self-documenting, mirrors GlPostingService's "IV {DocNo}"/"RC {DocNo}" convention.
    internal const string DescriptionPrefix = "AR Backfill ";

    private sealed record PlanItem(
        long BillingNoteId, string? DocNo, DateOnly DocDate, int BranchId, int? BusinessUnitId,
        decimal Outstanding, int FiscalYear, bool CreditRetainedEarnings, DateOnly PostDocDate);

    private static int FiscalYearOf(DateOnly d, int startMonth) =>
        d.Month >= startMonth ? d.Year : d.Year - 1;

    private async Task<long> ResolveAccountIdAsync(string code, CancellationToken ct)
    {
        var account = await db.ChartOfAccounts.AsNoTracking()
            .FirstOrDefaultAsync(a => a.CompanyId == tenant.CompanyId && a.AccountCode == code, ct);
        return account?.AccountId
            ?? throw new DomainException("backfill.account_missing",
                $"Configured GL account '{code}' is missing from chart_of_accounts for company " +
                $"{tenant.CompanyId}. Seed it before running the non-VAT AR backfill.");
    }

    private async Task<bool> AccountExistsAsync(string code, CancellationToken ct) =>
        await db.ChartOfAccounts.AsNoTracking()
            .AnyAsync(a => a.CompanyId == tenant.CompanyId && a.AccountCode == code, ct);

    /// <summary>Opus review round 2, LOW 1 — mirrors ExpenseClaimService.SaveGuardedAsync
    /// exactly (ExpenseClaimService.cs:36-47): every Version-guarded save goes through this, so
    /// a losing concurrent writer's DbUpdateConcurrencyException maps to a DomainException whose
    /// ".locked_mismatch" suffix DomainExceptionMiddleware already maps to 409 — not a raw,
    /// unhandled 500. <paramref name="postedSoFar"/> is folded into the message (no new
    /// exception field, no restructuring) so a caller whose run hit this mid-loop can still tell
    /// how many invoices were corrected before the conflict, straight from the error text.</summary>
    private async Task SaveGuardedAsync(int postedSoFar, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("backfill.locked_mismatch",
                $"This invoice was changed by a concurrent backfill run. {postedSoFar} invoice(s) " +
                "were corrected successfully before this conflict — re-run apply to resume the rest.");
        }
    }

    /// <summary>Opus review finding (2026-08-12) — PreviewAsync resolves no GL accounts, so a
    /// missing '3300' was invisible in the accountant-facing preview and only exploded at
    /// apply. Same for the period gate (FIX 1): probes, non-throwing, so preview still returns
    /// the plan but flags that <c>apply</c> would refuse it.</summary>
    private async Task<List<string>> ProbeBlockersAsync(IReadOnlyList<PlanItem> plan, CancellationToken ct)
    {
        var blockers = new List<string>();

        // Opus round 2 cosmetic note — this check is UNCONDITIONAL, never gated on plan.Count.
        // ApplyAsync's own preflight (FIX 1) runs BEFORE BuildPlanAsync, so it refuses even an
        // EMPTY plan when today's period is closed (deliberate — fails closed). An early return
        // here for an empty plan used to skip this check entirely, so preview could report "no
        // blockers" in a state where apply would still throw period.closed — dishonest.
        var today = clock.TodayInBangkok();
        if (!await period.IsOpenAsync(today.Year, today.Month, ct))
            blockers.Add($"The current period ({today:yyyy-MM}) is closed — apply would refuse " +
                "until it is reopened (every closed-year/closed-month correction dates at today).");

        if (plan.Count == 0) return blockers;   // nothing to post → no GL account needs checking

        if (!await AccountExistsAsync(accounts.Value.ArAccount, ct))
            blockers.Add($"GL account '{accounts.Value.ArAccount}' (Accounts Receivable) is missing " +
                "from the chart of accounts.");
        if (!await AccountExistsAsync(accounts.Value.SalesAccount, ct))
            blockers.Add($"GL account '{accounts.Value.SalesAccount}' (Sales) is missing from the " +
                "chart of accounts.");
        if (plan.Any(p => p.CreditRetainedEarnings) && !await AccountExistsAsync(accounts.Value.RetainedEarningsAccount, ct))
            blockers.Add($"GL account '{accounts.Value.RetainedEarningsAccount}' (Retained Earnings) " +
                "is missing from the chart of accounts — required because at least one invoice in " +
                "this plan is from a prior fiscal year.");

        return blockers;
    }

    /// <summary>Read-only: candidate enumeration + outstanding calc (mirrors
    /// SubledgerReportService.ArAgingAsync's BillingNote outstanding logic exactly — total minus
    /// Σ applied on POSTED receipts) + fiscal-year classification. Zero writes — safe for both
    /// PreviewAsync and as ApplyAsync's re-derived-fresh (hence resumable) work list.</summary>
    private async Task<List<PlanItem>> BuildPlanAsync(CancellationToken ct)
    {
        var candidates = await db.BillingNotes.AsNoTracking()
            .Where(b => b.CompanyId == tenant.CompanyId && b.JournalEntryId == null
                     && (b.Status == BillingNoteStatus.Issued || b.Status == BillingNoteStatus.Settled))
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];

        // outstanding(bn) = TotalAmount − Σ(AppliedAmount on POSTED receipts) — same convention
        // as SubledgerReportService.ArAgingAsync's paidByBn (BillingNote carries no AmountPaid
        // column; ReceiptService.cs:515-525's "already paid" pattern).
        var bnIds = candidates.Select(b => b.BillingNoteId).ToList();
        var paidByBn = await db.ReceiptApplications.AsNoTracking()
            .Where(a => a.BillingNoteId != null && bnIds.Contains(a.BillingNoteId!.Value))
            .Join(db.Receipts.AsNoTracking()
                    .Where(r => r.CompanyId == tenant.CompanyId && r.Status == DocumentStatus.Posted),
                a => a.ReceiptId, r => r.ReceiptId, (a, _) => a)
            .GroupBy(a => a.BillingNoteId!.Value)
            .Select(g => new { BillingNoteId = g.Key, Paid = g.Sum(x => x.AppliedAmount) })
            .ToDictionaryAsync(x => x.BillingNoteId, x => x.Paid, ct);

        // R1/C6 (Ham's ruling, 2026-08-11) — "closed" = a PRIOR fiscal year, period. The
        // FiscalYearClose table under-reports closure: Repttown never ran the in-app year-close,
        // yet its prior-year ภ.ง.ด.50 + DBD statements ARE already filed — those years are
        // closed in the sense that matters. This is also the accounting-standard
        // prior-period-error test, and it needs no extra query (pure fiscal-year comparison).
        var company = await db.Companies.AsNoTracking()
            .FirstAsync(c => c.CompanyId == tenant.CompanyId, ct);
        var today = clock.TodayInBangkok();
        var currentFy = FiscalYearOf(today, company.FiscalYearStartMonth);

        var plan = new List<PlanItem>();
        foreach (var bn in candidates.OrderBy(b => b.BillingNoteId))
        {
            var outstanding = bn.TotalAmount - paidByBn.GetValueOrDefault(bn.BillingNoteId);
            if (outstanding <= 0m) continue;   // I9 — a settled sale gets nothing, skipped entirely

            var fy = FiscalYearOf(bn.DocDate, company.FiscalYearStartMonth);
            var isClosedYear = fy < currentFy;

            DateOnly postDate;
            if (isClosedYear)
            {
                // I13b — a closed-year correction dates at TODAY, always in the current
                // (un-closeable-by-definition) period; never inside the closed year.
                postDate = today;
            }
            else
            {
                // R1/C6 (Ham's ruling) — still this fiscal year's P&L (Cr Revenue either way),
                // but a current-FY invoice can have its OWN issue MONTH already closed via the
                // ordinary monthly close — I13b forbids dating INTO that closed month even
                // though the fiscal year itself is still open. Only the date moves; the credit
                // side does not.
                var issueMonthOpen = await period.IsOpenAsync(bn.DocDate.Year, bn.DocDate.Month, ct);
                postDate = issueMonthOpen ? bn.DocDate : today;
            }

            plan.Add(new PlanItem(
                bn.BillingNoteId, bn.DocNo, bn.DocDate, bn.BranchId, bn.BusinessUnitId,
                outstanding, fy, isClosedYear, postDate));
        }
        return plan;
    }

    private async Task<int> CountAlreadyDoneAsync(CancellationToken ct) =>
        await db.BillingNotes.AsNoTracking()
            .Where(b => b.CompanyId == tenant.CompanyId && b.JournalEntryId != null
                     && (b.Status == BillingNoteStatus.Issued || b.Status == BillingNoteStatus.Settled))
            .Join(db.JournalEntries.AsNoTracking(), b => b.JournalEntryId!.Value, j => j.JournalId,
                (b, j) => j.Description)
            .CountAsync(d => d.StartsWith(DescriptionPrefix), ct);

    private static NonVatArBackfillResult BuildResult(
        string mode, IReadOnlyList<PlanItem> items, int alreadyDone, int posted, IReadOnlyList<string> blockers)
    {
        var byYear = items
            .GroupBy(i => i.FiscalYear)
            .Select(g => new NonVatArBackfillYearGroup(
                g.Key,
                g.First().CreditRetainedEarnings ? "RetainedEarnings" : "Revenue",
                g.Sum(x => x.Outstanding),
                g.Count(),
                g.OrderBy(x => x.DocDate)
                    .Select(x => new NonVatArBackfillInvoiceLine(x.BillingNoteId, x.DocNo, x.DocDate, x.Outstanding))
                    .ToList()))
            .OrderBy(g => g.FiscalYear)
            .ToList();

        return new NonVatArBackfillResult(
            mode, items.Sum(i => i.Outstanding), byYear, alreadyDone, posted, alreadyDone, blockers);
    }

    private async Task EnsureNonVatAsync(CancellationToken ct)
    {
        var tax = await taxCfg.GetAsync(ct);
        if (tax.VatMode)
            throw new DomainException("backfill.vat_company",
                "The non-VAT AR backfill only applies to a non-VAT-registered company.");
    }

    public async Task<NonVatArBackfillResult> PreviewAsync(CancellationToken ct)
    {
        await EnsureNonVatAsync(ct);
        var plan = await BuildPlanAsync(ct);
        var alreadyDone = await CountAlreadyDoneAsync(ct);
        var blockers = await ProbeBlockersAsync(plan, ct);
        return BuildResult("preview", plan, alreadyDone, posted: 0, blockers);
    }

    public async Task<NonVatArBackfillResult> ApplyAsync(CancellationToken ct)
    {
        await EnsureNonVatAsync(ct);

        // FIX 1 (Opus review, HIGH) — preflight, before ANY posting: PostManualEntryAsync
        // deliberately does NOT call EnsureOpenAsync itself (every other poster's CALLER owns
        // that gate — see its own doc comment); every correcting entry this method posts lands
        // either at TODAY (closed-year / closed-issue-month cases) or at an already-open
        // historical date, so if TODAY's own period were closed (an explicit Closed row is
        // authoritative even for the current month — IsOpenAsync's "missing row = open only for
        // the current month" default only applies when there is NO row), every one of those
        // corrections would land inside a closed period, unfixably (the JE is then immutable —
        // 020_journal_immutability.sql). Refuse the WHOLE run up front instead.
        var today = clock.TodayInBangkok();
        await period.EnsureOpenAsync(today, ct);

        var plan = await BuildPlanAsync(ct);
        var alreadyDone = await CountAlreadyDoneAsync(ct);
        if (plan.Count == 0)
            return BuildResult("apply", plan, alreadyDone, posted: 0, blockers: []);

        var ar = await ResolveAccountIdAsync(accounts.Value.ArAccount, ct);
        var sales = await ResolveAccountIdAsync(accounts.Value.SalesAccount, ct);
        // Retained earnings is resolved lazily — a company with zero closed-year candidates
        // must never be blocked by an account it doesn't need this run.
        long? retainedEarnings = plan.Any(p => p.CreditRetainedEarnings)
            ? await ResolveAccountIdAsync(accounts.Value.RetainedEarningsAccount, ct)
            : null;

        var posted = new List<PlanItem>();
        foreach (var item in plan)
        {
            // R1/C6 — one transaction PER INVOICE (never a single giant transaction): a crash at
            // invoice 300 of 400 must not roll back 299 good corrections and must not double-post
            // them. journal_entries are immutable once posted (020_journal_immutability.sql) — this
            // only ever POSTS NEW entries, never edits one.
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // Re-read the row (tracked) inside this invoice's own transaction. The plain
            // `bn.JournalEntryId is not null` check below is NOT itself a concurrency guard —
            // it is a read under ReadCommitted with no row lock, so on its own it cannot stop
            // two overlapping apply runs from both passing it for the same invoice before
            // either commits. The REAL guard is the explicit `bn.Version++` below: Version IS
            // configured as an EF concurrency token on BillingNote, but
            // AccountingDbContext.SaveChangesAsync never bumps it automatically — only
            // ExpenseClaimService/FixedAssetService do that manually (the "inert Version
            // token" pattern named at ExpenseClaimService.cs:35). Bumping it here makes the
            // UPDATE's WHERE clause include the version just read, so a losing concurrent
            // writer's SaveChangesAsync throws DbUpdateConcurrencyException — SaveGuardedAsync
            // below maps that to backfill.locked_mismatch (409, not a raw 500) — and since the
            // throw happens before this invoice's own tx.CommitAsync, the `await using tx`
            // above rolls its half-built JE back automatically (I12 — never duplicate).
            var bn = await db.BillingNotes.FirstAsync(b => b.BillingNoteId == item.BillingNoteId, ct);
            if (bn.JournalEntryId is not null)
            {
                await tx.RollbackAsync(ct);
                continue;
            }

            var creditAccountId = item.CreditRetainedEarnings ? retainedEarnings!.Value : sales;
            var creditLabel = item.CreditRetainedEarnings ? "Retained earnings" : "Sales";
            var lines = new List<ManualJvLine>
            {
                new(ar, item.Outstanding, 0m, $"AR {bn.DocNo}", item.BusinessUnitId),
                new(creditAccountId, 0m, item.Outstanding, $"{creditLabel} {bn.DocNo}", item.BusinessUnitId),
            };
            var journalId = await gl.PostManualEntryAsync(
                tenant.CompanyId, item.BranchId, item.PostDocDate,
                $"{DescriptionPrefix}{bn.DocNo}", bn.DocNo, lines, ct);

            bn.Version++;
            bn.JournalEntryId = journalId;
            await SaveGuardedAsync(posted.Count, ct);
            await tx.CommitAsync(ct);
            posted.Add(item);
        }

        return BuildResult("apply", posted, alreadyDone, posted.Count, blockers: []);
    }
}

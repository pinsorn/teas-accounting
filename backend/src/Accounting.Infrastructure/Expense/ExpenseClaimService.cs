using Accounting.Application.Abstractions;
using Accounting.Application.Expense;
using Accounting.Application.Ledger;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Expense;
using Accounting.Domain.Enums;
using Accounting.Infrastructure.Numbering;
using Accounting.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Infrastructure.Expense;

/// <summary>
/// Cycle C (specs/expense-claims.md) — Expense Claim pipeline. Submit -&gt; Approve -&gt; Pay
/// (GL posting). Pay is a self-contained cash disbursement (Option A): allocates EX-NNNN and
/// posts its own JE via the additive <see cref="IGlPostingService.PostExpenseClaimAsync"/> —
/// zero PaymentVoucher/Vendor/WHT code involved.
/// </summary>
public sealed class ExpenseClaimService(
    AccountingDbContext db, ITenantContext tenant, IClock clock,
    INumberSequenceService numbers, IGlPostingService gl, IPeriodCloseService period)
    : IExpenseClaimService
{
    private const string ExPrefix = "EX";

    private void Auth()
    {
        if (!tenant.IsAuthenticated)
            throw new DomainException("auth.required", "User must be authenticated.");
    }

    /// <summary>Every DbUpdateConcurrencyException from a Version-guarded transition maps to
    /// this DomainException code, which DomainExceptionMiddleware's existing ".locked_mismatch"
    /// suffix rule (shared with business_unit.locked_mismatch) already maps to 409 — FOOTGUN 8:
    /// unlike PV's inert token, Version++ here actually fires the optimistic-concurrency check.</summary>
    private async Task SaveGuardedAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("expense_claim.locked_mismatch",
                "This expense claim was changed by someone else. Reload and try again.");
        }
    }

    /// <summary>Codex review finding #2 (2026-07-10) — mirrors BankReconciliationService
    /// .CreateJournalAsync's contra-account check (exists tenant-scoped, active, non-header): a
    /// client-supplied line override must not be trusted unvalidated (a forged/foreign/header
    /// account would otherwise post silently and mis-roll the trial balance).
    /// R1/C5 — AMENDED 2026-08-12 (§3.3 amendment, Opus Tier-2 CRITICAL): an expense-claim line
    /// may debit any AccountType.Expense account, or an AccountType.Asset account ONLY when it IS
    /// the resolved category's OWN already-blessed <paramref name="categoryDefaultAccountId"/> —
    /// see ExpenseAccountRule.IsAllowedClaimLineAccount's doc for why "categoryIsCapex && Asset"
    /// alone (the original, defective rule) let bank/AR/input-VAT through. Never Liability, Equity
    /// or Revenue.</summary>
    private async Task<long> EnsureExpenseAccountAsync(
        long accountId, int companyId, bool categoryIsCapex, long? categoryDefaultAccountId, CancellationToken ct)
    {
        var account = await db.ChartOfAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == accountId && a.CompanyId == companyId, ct)
            ?? throw new DomainException("expense_claim.expense_account_invalid",
                $"Expense account {accountId} not found.");
        if (!account.IsActive)
            throw new DomainException("expense_claim.expense_account_invalid",
                $"Expense account {accountId} is not active.");
        if (account.IsHeader)
            throw new DomainException("expense_claim.expense_account_invalid",
                $"Expense account {accountId} is a header account — pick a postable (non-header) account.");
        if (!ExpenseAccountRule.IsAllowedClaimLineAccount(account.AccountType, accountId, categoryIsCapex, categoryDefaultAccountId))
            throw new DomainException("expense_claim.expense_account_invalid",
                $"Account {account.AccountCode} is {account.AccountType} — an expense line must use an " +
                "expense account" + (categoryIsCapex ? " or the category's own fixed-asset account." : "."));
        return accountId;
    }

    /// <summary>R1/C5 amendment (Opus HIGH-2, a dead-end-class fix) — re-validate every line's
    /// RESOLVED account. Called at Submit AND Approve (not only at Pay): without this, a legacy
    /// draft holding a bad account (created before this guard existed, or poisoned/deactivated
    /// after drafting) sails through both transitions after deploy and only hits the wall at Pay
    /// — creating NEW stuck Approved claims with no exit, instead of failing early and loudly.
    /// Also called from PayAsync (one more time, defence in depth, immediately before posting).</summary>
    private async Task RevalidateLineAccountsAsync(ExpenseClaim claim, CancellationToken ct)
    {
        await db.Entry(claim).Collection(c => c.Lines).LoadAsync(ct);
        var categoryIds = claim.Lines.Select(l => l.ExpenseCategoryId).Distinct().ToList();
        var categories = await db.ExpenseCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.CategoryId))
            .ToDictionaryAsync(c => c.CategoryId, c => new { c.IsCapex, c.DefaultExpenseAccountId }, ct);
        foreach (var l in claim.Lines)
        {
            var cat = categories.GetValueOrDefault(l.ExpenseCategoryId);
            await EnsureExpenseAccountAsync(
                l.ExpenseAccountId, claim.CompanyId, cat?.IsCapex ?? false, cat?.DefaultExpenseAccountId, ct);
        }
    }

    private async Task<(List<ExpenseClaimLine> Lines, decimal Subtotal, decimal Vat, decimal Total)> BuildLinesAsync(
        IReadOnlyList<ExpenseClaimLineInput> inputs, int companyId, CancellationToken ct)
    {
        // F-B (specs/fix-purchase-nonvat-ux.md) — a non-VAT-registered company cannot credit
        // input VAT (no ภ.พ.30); force every line non-recoverable regardless of client input,
        // mirroring VendorInvoiceService's WP1.2/F27/D1 guard. BuildLinesAsync is the single seam
        // every create/update path (REST + MCP create_expense_claim_draft/update_expense_claim_draft)
        // funnels through, so a DTO carrying vatRate>0 + isRecoverableVat=true for a non-VAT
        // company can never route an amount to GlPostingService.PostExpenseClaimAsync's 1170 line.
        var companyVatRegistered = await db.Companies.Where(c => c.CompanyId == companyId)
            .Select(c => c.VatRegistered).FirstOrDefaultAsync(ct);

        var lines = new List<ExpenseClaimLine>();
        for (var i = 0; i < inputs.Count; i++)
        {
            var input = inputs[i];
            var category = await db.ExpenseCategories
                    .FirstOrDefaultAsync(c => c.CategoryId == input.ExpenseCategoryId, ct)
                ?? throw new DomainException("expense_claim.expense_category_missing",
                    $"Expense category {input.ExpenseCategoryId} not found.");
            // R1/C5 — a deactivated category (the fix for a poisoned one, I20) must not be
            // usable on a NEW claim, else deactivation is cosmetic.
            if (!category.IsActive)
                throw new DomainException("expense_claim.expense_category_inactive",
                    $"Expense category '{category.CategoryCode}' is deactivated — pick another category.");

            // Frozen at draft: line override ?? category default (identical rule to
            // PaymentVoucherService.cs:162) — never re-resolved after this point. R1/C5 — BOTH
            // branches are validated by EnsureExpenseAccountAsync (exists/active/non-header/type);
            // the category default used to be trusted on the claim that "it was already validated
            // when the category was set up" — it never was (VERDICT C5).
            var expenseAccountId = input.ExpenseAccountId is { } overrideId
                ? await EnsureExpenseAccountAsync(overrideId, companyId, category.IsCapex, category.DefaultExpenseAccountId, ct)
                : await EnsureExpenseAccountAsync(
                    category.DefaultExpenseAccountId
                        ?? throw new DomainException("expense_claim.expense_account_missing",
                            $"Line {i + 1}: no expense account (category '{category.CategoryCode}' has no default)."),
                    companyId, category.IsCapex, category.DefaultExpenseAccountId, ct);

            var isRecoverableVat = companyVatRegistered && input.IsRecoverableVat;
            // R1/C1 (WP-3) — THB is a 2-decimal currency; 4dp reached the immutable ledger.
            var net = Math.Round(input.Amount, 2, MidpointRounding.AwayFromZero);
            var vat = Math.Round(net * input.VatRate, 2, MidpointRounding.AwayFromZero);
            var lineTotal = isRecoverableVat ? net : net + vat;   // §3 JE line rules

            lines.Add(new ExpenseClaimLine
            {
                CompanyId         = companyId,
                LineNo            = i + 1,
                ExpenseCategoryId = input.ExpenseCategoryId,
                ExpenseAccountId  = expenseAccountId,
                Description       = input.Description,
                ExpenseDate       = input.ExpenseDate,
                Amount            = net,
                TaxCodeId         = input.TaxCodeId,
                VatRate           = input.VatRate,
                VatAmount         = vat,
                IsRecoverableVat  = isRecoverableVat,
                LineTotal         = lineTotal,
            });
        }
        var subtotal = lines.Sum(l => l.Amount);
        var vatTotal = lines.Sum(l => l.VatAmount);
        return (lines, subtotal, vatTotal, subtotal + vatTotal);
    }

    public async Task<long> CreateDraftAsync(CreateExpenseClaimRequest req, CancellationToken ct)
    {
        Auth();
        if (!await db.Employees.AnyAsync(e => e.EmployeeId == req.EmployeeId, ct))
            throw new DomainException("expense_claim.employee_missing", $"Employee {req.EmployeeId} not found.");

        var (lines, subtotal, vat, total) = await BuildLinesAsync(req.Lines, tenant.CompanyId, ct);

        var claim = new ExpenseClaim
        {
            CompanyId      = tenant.CompanyId,
            BranchId       = tenant.BranchId,
            BusinessUnitId = req.BusinessUnitId,
            EmployeeId     = req.EmployeeId,
            ClaimDate      = req.ClaimDate,
            Title          = req.Title,
            Notes          = req.Notes,
            Lines          = lines,
            SubtotalAmount = subtotal,
            VatAmount      = vat,
            TotalAmount    = total,
        };
        db.ExpenseClaims.Add(claim);
        await db.SaveChangesAsync(ct);
        return claim.ExpenseClaimId;
    }

    public async Task UpdateDraftAsync(long id, UpdateExpenseClaimRequest req, CancellationToken ct)
    {
        Auth();
        var claim = await db.ExpenseClaims.Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.ExpenseClaimId == id, ct)
            ?? throw new DomainException("expense_claim.not_found", $"Expense Claim {id} not found.");
        if (claim.Status != ExpenseClaimStatus.Draft && claim.Status != ExpenseClaimStatus.Rejected)
            throw new DomainException("expense_claim.not_editable",
                $"Cannot edit an expense claim in status {claim.Status}.");

        if (!await db.Employees.AnyAsync(e => e.EmployeeId == req.EmployeeId, ct))
            throw new DomainException("expense_claim.employee_missing", $"Employee {req.EmployeeId} not found.");

        var (lines, subtotal, vat, total) = await BuildLinesAsync(req.Lines, claim.CompanyId, ct);

        // FOOTGUN 7 — always rewrite lines (never diff/skip); attachments parent to the
        // stable header id, never the line id, precisely because of this rewrite.
        db.ExpenseClaimLines.RemoveRange(claim.Lines);
        claim.EmployeeId     = req.EmployeeId;
        claim.ClaimDate      = req.ClaimDate;
        claim.Title          = req.Title;
        claim.Notes          = req.Notes;
        claim.BusinessUnitId = req.BusinessUnitId;
        claim.Lines          = lines;
        claim.SubtotalAmount = subtotal;
        claim.VatAmount      = vat;
        claim.TotalAmount    = total;
        // Tier-2 review — a Rejected -> edited -> resubmitted claim must not carry stale
        // rejection text forward; clear it on every edit (Draft edits: already null, no-op).
        claim.RejectReason   = null;
        // Codex review finding #6 (2026-07-10) — every OTHER state transition increments
        // Version + saves via SaveGuardedAsync (FOOTGUN 8); a Draft edit racing a concurrent
        // Submit was the one path that skipped both, so it would silently overwrite an
        // already-submitted claim's lines instead of 409-ing.
        claim.Version++;
        await SaveGuardedAsync(ct);
    }

    private async Task<ExpenseClaim> LoadAsync(long id, CancellationToken ct) =>
        await db.ExpenseClaims.FirstOrDefaultAsync(c => c.ExpenseClaimId == id, ct)
        ?? throw new DomainException("expense_claim.not_found", $"Expense Claim {id} not found.");

    public async Task SubmitAsync(long id, CancellationToken ct)
    {
        Auth();
        var claim = await LoadAsync(id, ct);
        await RevalidateLineAccountsAsync(claim, ct);
        claim.Submit();
        await SaveGuardedAsync(ct);
    }

    public async Task<ExpenseClaimApprovedResult> ApproveAsync(long id, CancellationToken ct)
    {
        Auth();
        var claim = await LoadAsync(id, ct);
        await RevalidateLineAccountsAsync(claim, ct);
        var approver = tenant.UserId ?? 0;
        claim.Approve(approver, clock.UtcNow);
        await SaveGuardedAsync(ct);
        return new ExpenseClaimApprovedResult(claim.ExpenseClaimId, claim.ApprovedBy!.Value, claim.ApprovedAt!.Value);
    }

    public async Task RejectAsync(long id, RejectExpenseClaimRequest req, CancellationToken ct)
    {
        Auth();
        var claim = await LoadAsync(id, ct);
        claim.Reject(req.Reason);
        await SaveGuardedAsync(ct);
    }

    public async Task<ExpenseClaimPaidResult> PayAsync(long id, PayExpenseClaimRequest req, CancellationToken ct)
    {
        Auth();
        var method = req.PaymentMethod.ToUpperInvariant();
        if (method != "CASH" && method != "TRANSFER")
            throw new DomainException("expense_claim.payment_method_invalid",
                "paymentMethod must be CASH or TRANSFER.");
        if (method == "TRANSFER" && req.BankAccountId is null)
            throw new DomainException("expense_claim.bank_account_required",
                "TRANSFER payment requires a bankAccountId.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Re-load + re-guard inside the tx (mirrors PaymentVoucherService.PostAsync) so a
        // double-pay race can't post two JEs — the loser's SaveChangesAsync below throws
        // DbUpdateConcurrencyException once the winner's row-locked UPDATE commits.
        var claim = await LoadAsync(id, ct);

        // F-B (specs/fix-purchase-nonvat-ux.md) — re-assert at Pay: a claim drafted/approved
        // while the company was VAT-registered (or created before this guard existed) must never
        // post recoverable input VAT if the company is non-VAT by the time it's paid. Mirrors
        // VendorInvoiceService.PostAsync's re-guard; BuildLinesAsync already closes the normal
        // create/edit path, this is defence for the stale-row edge case only.
        var companyVatRegistered = await db.Companies.Where(c => c.CompanyId == claim.CompanyId)
            .Select(c => c.VatRegistered).FirstOrDefaultAsync(ct);
        // R1/C5 — re-validate each line's RESOLVED account one more time before posting (defence
        // in depth): Submit/Approve already re-validate, but a category can still be poisoned or
        // an account deactivated in the window between Approve and Pay. This is the LAST gate
        // before GlPostingService.PostExpenseClaimAsync reads ExpenseAccountId off these lines and
        // posts them verbatim. Also loads Lines (needed below for the VAT re-guard too).
        await RevalidateLineAccountsAsync(claim, ct);
        if (!companyVatRegistered)
        {
            foreach (var l in claim.Lines) l.IsRecoverableVat = false;
        }

        var postDate = clock.TodayInBangkok();
        await period.EnsureOpenAsync(postDate, ct);

        // cont.79-style GL dimension — embed the BU code into the doc-number sub-prefix,
        // mirroring PaymentVoucherService.PostAsync (no header category here to concat with).
        var buCode = claim.BusinessUnitId is { } bid
            ? await db.BusinessUnits.Where(x => x.BusinessUnitId == bid).Select(x => x.Code).FirstOrDefaultAsync(ct)
            : null;
        claim.SubPrefix = buCode;

        var now = clock.UtcNow;
        // CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — bounded retry on a doc_no collision
        // (residual sequence drift); re-allocates and retries instead of a raw 500. A genuine
        // optimistic-concurrency conflict (unrelated to doc_no) still surfaces as the same clean
        // domain error SaveGuardedAsync gives elsewhere in this file.
        try
        {
            await NumberedDocumentWriter.AllocateAndSaveAsync(
                db,
                c => numbers.NextAsync(claim.CompanyId, claim.BranchId, ExPrefix, claim.SubPrefix, postDate, c),
                (v, first) => { if (first) claim.MarkPaid(v.Value, method, req.BankAccountId, tenant.UserId ?? 0, now); else claim.DocNo = v.Value; },
                ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("expense_claim.locked_mismatch",
                "This expense claim was changed by someone else. Reload and try again.");
        }

        // GlPostingService re-queries ExpenseClaims on this SAME DbContext — identity map
        // returns the just-mutated tracked instance (DocNo/PaymentMethod/Status already set).
        var journalId = await gl.PostExpenseClaimAsync(claim.ExpenseClaimId, ct);
        claim.JournalEntryId = journalId;
        await db.SaveChangesAsync(ct);

        await tx.CommitAsync(ct);

        return new ExpenseClaimPaidResult(claim.ExpenseClaimId, claim.DocNo!, now, claim.TotalAmount, journalId);
    }

    public async Task CancelAsync(long id, CancellationToken ct)
    {
        Auth();
        var claim = await LoadAsync(id, ct);
        claim.Cancel();
        await SaveGuardedAsync(ct);
    }

    public async Task<IReadOnlyList<ExpenseClaimListItem>> ListAsync(
        string? status, long? employeeId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        Auth();
        var q = from c in db.ExpenseClaims.AsNoTracking()
                join e in db.Employees.AsNoTracking() on c.EmployeeId equals e.EmployeeId
                select new { c, e };

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<ExpenseClaimStatus>(status, ignoreCase: true, out var st))
            q = q.Where(x => x.c.Status == st);
        if (employeeId is { } eid) q = q.Where(x => x.c.EmployeeId == eid);
        if (from is { } f) q = q.Where(x => x.c.ClaimDate >= f);
        if (to is { } t) q = q.Where(x => x.c.ClaimDate <= t);

        var rows = await q.OrderByDescending(x => x.c.ClaimDate).ThenByDescending(x => x.c.ExpenseClaimId)
            .Select(x => new ExpenseClaimListItem(
                x.c.ExpenseClaimId, x.c.DocNo, x.c.EmployeeId,
                x.e.FirstNameTh + " " + x.e.LastNameTh,
                x.c.ClaimDate, x.c.Status.ToString(), x.c.TotalAmount))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<ExpenseClaimDetail?> GetDetailAsync(long id, CancellationToken ct)
    {
        Auth();
        var claim = await db.ExpenseClaims.AsNoTracking().Include(c => c.Lines)
            .FirstOrDefaultAsync(c => c.ExpenseClaimId == id, ct);
        if (claim is null) return null;

        var employee = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmployeeId == claim.EmployeeId, ct);
        var categoryIds = claim.Lines.Select(l => l.ExpenseCategoryId).Distinct().ToList();
        var categories = await db.ExpenseCategories.AsNoTracking()
            .Where(c => categoryIds.Contains(c.CategoryId))
            .ToDictionaryAsync(c => c.CategoryId, c => c.NameTh, ct);

        var lines = claim.Lines.OrderBy(l => l.LineNo).Select(l => new ExpenseClaimLineDetail(
            l.ExpenseClaimLineId, l.LineNo, l.ExpenseCategoryId,
            categories.GetValueOrDefault(l.ExpenseCategoryId, "-"),
            l.ExpenseAccountId, l.Description, l.ExpenseDate,
            l.Amount, l.TaxCodeId, l.VatRate, l.VatAmount, l.IsRecoverableVat, l.LineTotal))
            .ToList();

        return new ExpenseClaimDetail(
            claim.ExpenseClaimId, claim.DocNo, claim.EmployeeId,
            employee is null ? "-" : employee.FirstNameTh + " " + employee.LastNameTh,
            claim.ClaimDate, claim.Title, claim.Status.ToString(),
            claim.PaymentMethod, claim.BankAccountId,
            claim.SubtotalAmount, claim.VatAmount, claim.TotalAmount,
            claim.JournalEntryId, claim.RejectReason, claim.Notes,
            claim.BusinessUnitId, claim.Version, lines);
    }
}

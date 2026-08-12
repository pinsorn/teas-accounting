using Accounting.Domain.Enums;

namespace Accounting.Application.Abstractions;

/// <summary>
/// R1/C5 — AMENDED 2026-08-12 (specs/fix-breakit-r1-ledger-integrity.md §3.3). The original rule
/// here was "Expense || (categoryIsCapex &amp;&amp; Asset)". <see cref="AccountType"/> has only five
/// members with NO cash/fixed-asset distinction — bank 1120, AR 1130 and Input VAT 1170 are all
/// AccountType.Asset — so that rule let an ordinary employee (permission expense.claim.create
/// only) file a claim against the seeded CAPEX category with expenseAccountId=1120 and post
/// Dr Bank / Cr Bank: balanced, green, status Paid, never reimbursed. Verbatim the P0 this WP
/// exists to close (caught by Opus Tier-2, confirmed in code).
///
/// TWO DIFFERENT TRUST BOUNDARIES need two DIFFERENT rules — forcing one shared predicate across
/// both is what produced the tautology risk the amendment warns about (see
/// <see cref="IsAllowedCategoryDefaultAccount"/>'s doc):
///   • CLAIM LINE (any employee holding expense.claim.create) — <see cref="IsAllowedClaimLineAccount"/>:
///     an allowlist of exactly ONE account, the capex category's OWN already-blessed
///     DefaultExpenseAccountId. Structurally cannot admit bank/cash/AR/input-VAT because none of
///     those ever equals that pinned id (unless the category itself was poisoned, which is the
///     category-master path's job to prevent). Accepted trade-off: a capex claim can no longer
///     override to a DIFFERENT fixed-asset account (e.g. 1620 Vehicles) — the remedy is a second
///     capex category pointing at 1620, which is better modelling anyway.
///   • CATEGORY MASTER (Sys.ExpenseCatManage only — an elevated permission, picking the ONE
///     account every future claim on this category will trust) — <see cref="IsAllowedCategoryDefaultAccount"/>
///     (the type check below) PLUS a denylist against known cash/bank/AR/VAT GL roles, applied in
///     ExpenseCategoryService.EnsureDefaultAccountAsync (needs a DB read against GlAccountsOptions
///     + bank_accounts, so it cannot live in this static/DB-free predicate). AMENDED round 3
///     (2026-08-12, Opus Tier-2) — without that denylist, the type check alone is exactly the
///     "category itself was poisoned" case the claim-line rule above says it relies on this path
///     to prevent: Sys.ExpenseCatManage could point a capex category's default at Bank (also
///     AccountType.Asset) and every ordinary employee's subsequent claim would then pass — same
///     P0, one permission level up. A denylist is the right shape ONLY here (rejected for the
///     claim-line path above) because this is a single decision, made once, by an elevated
///     permission, over GL roles the system already knows by configuration — defence in depth
///     behind the permission gate and the type check, not the sole control.
/// </summary>
public static class ExpenseAccountRule
{
    /// <summary>Claim-line / pay-time validation (ExpenseClaimService: BuildLinesAsync's two
    /// branches, SubmitAsync/ApproveAsync/PayAsync's re-guards). <paramref name="accountId"/> is
    /// the account actually being posted on the line; <paramref name="categoryDefaultAccountId"/>
    /// is the category's OWN persisted default (already validated by
    /// <see cref="IsAllowedCategoryDefaultAccount"/> at the time it was set). An Asset account is
    /// permitted ONLY when the line's account IS that one already-blessed default — never merely
    /// "some Asset account", which is what let bank/cash/AR/input-VAT through before.</summary>
    public static bool IsAllowedClaimLineAccount(
        AccountType accountType, long accountId, bool categoryIsCapex, long? categoryDefaultAccountId) =>
        accountType == AccountType.Expense
        || (categoryIsCapex && categoryDefaultAccountId is { } capexAcctId && accountId == capexAcctId);

    /// <summary>Category-master validation (ExpenseCategoryService.CreateAsync/UpdateAsync) — the
    /// ONE place a NEW DefaultExpenseAccountId is chosen for a category, gated behind
    /// Sys.ExpenseCatManage (not the ordinary claim-creation permission). A plain type check is
    /// the correct — and only sensible — control here: the candidate account IS what is about to
    /// become category.DefaultExpenseAccountId, so reusing <see cref="IsAllowedClaimLineAccount"/>'s
    /// "accountId == categoryDefaultAccountId" shape would compare the candidate against itself —
    /// a tautology that permits ANY account (including bank/AR/VAT) once IsCapex is set. Asset is
    /// still gated to IsCapex categories only; never Liability, Equity or Revenue either way.</summary>
    public static bool IsAllowedCategoryDefaultAccount(AccountType accountType, bool categoryIsCapex) =>
        accountType == AccountType.Expense || (categoryIsCapex && accountType == AccountType.Asset);
}

# Question-Backend13 — Sprint 9 spec-first gate (BEFORE Part A migration)

**Date:** 2026-05-17
**From:** Claude Code
**To:** Ham / Sana
**Re:** Answer-Sana-Backend14 (Sprint 9, the big one) — 3 premise gaps found in
the pre-build survey. Recommended answers included for a fast yes/adjust.

> Same discipline as Question-Backend5/12 (you approved + rewarded both). This
> is the largest Phase-1 sprint; surveying before writing any migration/code
> caught 3 spec/codebase mismatches. **Nothing built yet.** Two affect Part A
> DoD #2/#3, one affects Part B B1. All have a zero-scope-creep recommended
> path consistent with prior accepted decisions (8.6 Product defer, 8.7
> VatRegistered reuse).

---

## 🟡 Q1 — P&L COGS / Gross-Profit split has no data model (Part A2)

**Spec A2** categorizes P&L lines: `COGS = account_subtype='COGS'`,
`OperatingExpense = account_type='EXPENSE' AND account_subtype != 'COGS'`,
and outputs `cogs` / `gross_profit` / `operating_expense` / `net_profit`.

**Reality:** `master.chart_of_accounts` has **no `account_subtype` column**
(only `account_type` = ASSET/LIABILITY/EQUITY/REVENUE/EXPENSE). COGS cannot be
distinguished from operating expense. Seeded CoA has no COGS accounts tagged.

**Recommended R-Q1a (zero scope creep — recommended):** ship P&L by BU as
**Revenue − Expense = NetProfit** per BU (drop the `cogs` / `gross_profit` /
`operating_expense` sub-lines this sprint; JSON returns `revenue`, `expense`,
`net_profit`). Add `account_subtype` + COGS tagging in a later sprint (natural
fit with the Sprint-10 Product master / CoA work). Mirrors the 8.6 R-B1a
"degrade cleanly, defer the taxonomy" decision you accepted.

**R-Q1b:** add `ChartOfAccount.AccountSubtype` now + a seed marking which
accounts are COGS. Modest schema work but the COGS account set is a
business/accounting decision (which 51xx are COGS vs OpEx) that needs your
input — not something I should improvise.

---

## 🟡 Q2 — Sales-summary `group_by=product` has no Product master (Part A3)

**Spec A3** allows `group_by=customer|product|business_unit`. There is **no
Product master / `products` table** (confirmed in Sprint 8.6; you deferred it
to Sprint 10). TI lines carry only free-form `ProductCode`/`Description`.

**Recommended R-Q2 (recommended):** support `group_by=customer|business_unit`
fully; `group_by=product` → HTTP 400 `"sales-summary by product requires the
Product master (Sprint 10)"`. Enable the product dimension in Sprint 10
alongside the Product master. Consistent with the accepted 8.6 deferral.

---

## 🟡 Q3 — `tax_codes.category` duplicates existing IsExempt/IsZeroRated (Part B1)

**Spec B1** adds `tax_codes.category VARCHAR(20) ('TAXABLE'|'ZERO_RATED'|
'EXEMPT')`. But `tax.tax_codes` **already has `IsExempt BOOL` + `IsZeroRated
BOOL`** which encode exactly this 3-state (TAXABLE = !IsExempt &&
!IsZeroRated). Adding a `category` enum alongside them is the same
duplicate-field drift hazard as Sprint 8.7's `is_vat_registered`
(which you explicitly accepted reusing the existing column for).

**Recommended R-Q3 (recommended — mirrors the accepted 8.7 call):** derive
`category` from the existing `IsExempt`/`IsZeroRated` (single source of truth);
add **only** `legal_ref VARCHAR(100) NULL` (genuinely new, no equivalent).
Seed 240 sets `IsExempt`/`IsZeroRated = true` + `legal_ref` for the exempt/
zero-rated codes (`ON CONFLICT DO UPDATE`). All categorization logic
(ภ.พ.30 sales_taxable/zero/exempt, ม.82/6 ratio) reads the booleans. API/JSON
still expose a computed `category` string so the contract is unchanged.

---

## What I build the moment this is answered (per recommended answers)

Part A → gate → Part B → gate → Part C → gate → wrap, exactly as specced,
except: P&L = Revenue/Expense/NetProfit-by-BU (R-Q1a); sales-summary
customer|business_unit, product=400 (R-Q2); tax_codes += `legal_ref` only,
`category` is computed from IsExempt/IsZeroRated (R-Q3). Everything else —
Trial Balance + invariant, WHT-Recv aging (+cert_received_at/reconciled_at),
exempt seed 240, ม.82/6 proportional service, ภ.พ.30 (preview/finalize,
manual/auto-stub), input/output VAT registers, FOR-SVC/FOR-ROYAL seed 250,
ภ.ง.ด.3/53/54 + ภ.พ.36 reverse-charge generator + auto-JV, tax_filings
immutable history, all UI + 5 new Playwright = 25/25 — unchanged.

**Fastest path: reply "R-Q1a + R-Q2 + R-Q3" (or adjust) → I start Part A P1.**

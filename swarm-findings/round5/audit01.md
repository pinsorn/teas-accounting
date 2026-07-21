# UX Swarm ROUND 5 — audit01 (Auditor, READ-ONLY role) — co5 prod v1.22.9

Target: https://teas.kazaki-rio.com (prod v1.22.9) | Company: co5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด)
User: audit01 / role AUDITOR (userId=16, companyId=5, allowedCompanies=[5 only]) | isSuperAdmin=false
Tool: Playwright headless (msedge channel) from `frontend/`, temp script `swarm5-audit01.mjs`
(deleted at end of run per hard rule 4). This round re-verifies WP1/WP2/WP6 fixes shipped since round 4.

Current AUDITOR grants (30, via `/api/proxy/me/permissions`): bank.account.read, bank.report.read,
expense.claim.read, fixedasset.read, gl.journal.read, master.business_unit.read, master.customer.read,
master.product.read, master.vendor.read, purchase.payment_voucher.read, purchase.purchase_order.read,
purchase.vendor_invoice.read, purchase.wht.read, report.audit.read, report.general_ledger.read,
report.profit_loss.read, report.trial_balance.read, sales.billing_note.read, sales.credit_note.read,
sales.debit_note.read, sales.delivery_order.read, sales.quotation.read, sales.receipt.read,
sales.sales_order.read, sales.tax_invoice.read, sys.attachment.read, tax.filing.preview, tax.pnd3.read,
tax.pnd30.read, tax.pnd53.read, tax.vat_register.read. Zero write-shaped grants (0 `.create`/`.post`/
`.manage`/`.approve`/`.finalize` scopes) — consistent with the read-only design.

## Done
- Logged in as audit01, landed on dashboard (co5, "TEAS Enterprise" chrome). Shot:
  shots/round5/audit01-01-login-dashboard.png
- Swept all 16 `/…/new` routes for a clean full-page deny (WP1).
- Checked `/period-close` for a clean full-page deny (WP1).
- Checked the credit-notes/debit-notes list "+ create" button for hidden (WP1).
- Checked the ภ.พ.30 (`/reports/pnd30`) finalize button for absent-from-DOM (WP1) — did **not**
  click preview or finalize (forbidden action, hard rule 2).
- Swept all 15 previously-403 modules (PO, VI, PV, quotations, sales-orders, delivery-orders,
  expense-claims, vendors, bank-accounts, fixed-assets, AP-aging, outstanding-PO, bank-recon, CIT,
  business-units) for real-data rendering + counted 403 responses per module (WP2+WP6).
- Ran 2 direct-API POST probes (purchase-orders, payment-vouchers) with the session cookie, no UI,
  expecting still-403 (defense-in-depth).
- Cross-tenant sanity: `/me` confirms `allowedCompanies=[{id:5}]` only; dashboard body text
  manually reviewed in screenshot — no second company/tenant name present (a crude regex heuristic
  flagged "possible" but that was a false positive on the generic Thai word "บริษัท" appearing in
  in-company customer/vendor names like "บริษัท ลูกค้าทดสอบ จำกัด" — verified by eye, not a leak).

## Fix-verify (per WP, explicit)

### WP1 — clean full-page deny, no rendered write form: **CLOSED, 20/20**
All 16 `/new` routes now show `data-testid="state-no-access"` (ShieldAlert + "ไม่มีสิทธิ์เข้าถึง") with
**zero** form inputs/textareas/selects rendered anywhere in `<main>` — round 4's full interactive
forms are gone:

| Route | Deny box | Form inputs |
|---|---|---|
| /bank-accounts/new | yes | 0 |
| /credit-notes/new | yes | 0 |
| /customers/new | yes | 0 |
| /debit-notes/new | yes | 0 |
| /delivery-orders/new | yes | 0 |
| /expense-claims/new | yes | 0 |
| /fixed-assets/new | yes | 0 |
| /invoices/new | yes | 0 |
| /payment-vouchers/new | yes | 0 |
| /purchase-orders/new | yes | 0 |
| /quotations/new | yes | 0 |
| /receipts/new | yes | 0 |
| /sales-orders/new | yes | 0 |
| /tax-invoices/new | yes | 0 |
| /vendor-invoices/new | yes | 0 |
| /vendors/new | yes | 0 |
| /period-close | yes | 0 |

Plus: CN/DN "+ create" button — hidden on both `/credit-notes` and `/debit-notes` (no
`a[href=".../new"]` in DOM). ภ.พ.30 finalize button — **not present** in DOM on `/reports/pnd30`
(round 4 showed it visible-but-greyed; now `PermissionGate scope="tax.filing.finalize"` hides it
entirely — confirmed against `Permissions.cs`/backend: AUDITOR holds `tax.filing.preview`, not
`tax.filing.finalize`).

Evidence shots: shots/round5/audit01-02-deny-quotations-new.png,
shots/round5/audit01-03-deny-period-close.png, shots/round5/audit01-04-list-credit-notes.png,
shots/round5/audit01-05-list-debit-notes.png, shots/round5/audit01-06-pnd30-no-finalize.png.
Zero `FINDING-*` screenshots were generated (script only shoots on a non-clean result) — **0/17
page-level checks failed**.

### WP2 + WP6 — previously-403 modules now render real data for AUDITOR: **CLOSED, 15/15**
Every module loaded with `denyBox=false` (no permission block) and the real list/report UI,
confirmed against the backend grant list above (each maps to a `.read`/`.preview` scope AUDITOR now
holds):

| Route | Real UI rendered | Rows/content | 403s during load |
|---|---|---|---|
| /purchase-orders (PO) | yes | 13 rows | 0 |
| /vendor-invoices (VI) | yes | 8 rows | 0 |
| /payment-vouchers (PV) | yes | 10 rows | 0 |
| /quotations | yes | 18 rows | 0 |
| /sales-orders | yes | 9 rows | 0 |
| /delivery-orders | yes | 9 rows | 0 |
| /expense-claims | yes | 0 rows ("ไม่มีข้อมูล") | 0 |
| /vendors | yes | 8 rows | 0 |
| /bank-accounts | yes | 1 row (KBank account) | 0 |
| /fixed-assets | yes | 0 rows ("ไม่มีข้อมูล") | 0 |
| /reports/ap-aging | yes | 1 vendor + tie-out banner "Dr = Cr ✓" | 0 |
| /reports/outstanding-po | yes | 1 overdue PO | 0 |
| /reports/bank-reconciliation | yes | 15 deposits-in-transit + auto-selected sole account | 1 (see Findings) |
| /tax-filings/cit | yes | full CIT year figures | 0 |
| /settings/business-units | yes | 1 BU (BU01/ขายสินค้า) | **0** |

**Business-units 403 spam — confirmed GONE**: 0 business-unit-related 403s across the entire
35-route sweep (`master.business_unit.read` now granted, per backend
`629_seed_read_manage_split_grant.sql:110/138-166`). Total 403 count for the whole run = 7, of
which 2 are the intentional API probes below and 0 are business-unit-related.

Two of the 15 modules (expense-claims, fixed-assets) showed an empty ("ไม่มีข้อมูล") state rather
than rows — this is the correct read-only render (no deny box, working filter controls, zero 403),
not a permission failure; I could not independently confirm co5 actually has expense-claim/fixed-asset
records to display (honest gap, no prior round found any either), so I can't rule out these two
modules are simply empty in this company's seed data. Screenshots: shots/round5/audit01-07 through
-21 (one per module, see filenames above).

### Defense-in-depth — direct-API POST still 403: **CLOSED, 2/2**
- `POST /api/proxy/purchase-orders` (empty body, session cookie only, no UI) → **403**
- `POST /api/proxy/payment-vouchers` (same) → **403**

Both denied cleanly with no persistence, despite AUDITOR now holding the corresponding `.read`
grants — read/write separation intact.

## Regressions
None observed. All 16 mutation-shaped surfaces (16 `/new` routes) + CN/DN create + pnd30 finalize
+ period-close + 2 direct-API POSTs stayed denied. All 15 previously-403 read surfaces now render.
No 500s, no cross-tenant data, no new console errors beyond the 7 total 403s (2 expected API
probes; 5 residual, see Findings).

## Findings
| Severity | Area | Symptom | Repro | Screenshot |
|---|---|---|---|---|
| LOW | `/reports/bank-reconciliation` | `useStatementImports(bankAccountId ?? 0)` fires with an initial invalid id=0 before the sole-account auto-select effect runs, then again for the real account — AUDITOR lacks `bank.statement.import` (a write-shaped scope, correctly ungranted), so the imports-history lookup 403s. `noStatementImported` collapses "denied" and "genuinely never imported" to the same true/false, so the "ยังไม่มีการนำเข้า Statement" badge can't distinguish the two cases for this role. In this specific data it happens to coincide with the real state (round 4's chief01, a role WITH the permission, also saw Statement=฿0.00 for this account), so it is not currently showing wrong information — but the design is fragile. | Load `/reports/bank-reconciliation` as audit01 with 1 bank account (auto-selects) | shots/round5/audit01-19-module-bank-recon.png |
| LOW | unidentified (not any of the 16 gated `/new` pages, nor a WP2/WP6 target module) | 4 residual 403s during the sweep: `GET /api/proxy/expense-categories` ×3, `GET /api/proxy/employees` ×1. AUDITOR holds neither `sys.expense_category.read` nor an employee-read scope. Verified these are NOT from `/expense-claims/new`'s `EmployeeSelector`/`ExpenseCategorySelector` (both are gated behind the same `canCreate` early-return that already renders 0 form inputs for AUDITOR — confirmed in source, `expense-claims/new/page.tsx`), and NOT from any per-module delta in the WP2/WP6 sweep table above (all deltas 0 except bank-recon). Likely a Next.js Link-prefetch or nav-adjacent background call from a settings sub-page. Console noise only — no UI breakage observed (expense-claims list rendered fine, no broken names). Not isolated within timebox; flagged as residual finding for follow-up rather than root-caused. | Full navigation sweep, see `console403s` in raw capture (deleted per output rule; listed here) | — |

**No HIGH/CRIT findings this round** — both LOW items above are informational/hardening notes, not
functional breaks: neither blocks a WP1/WP2/WP6 closure criterion, and the defense-in-depth
mutation checks all held.

## CRIT-verify (from audit01's read-only vantage)
- audit01 holds 0 write-shaped grants (0 of 30 permissions). All mutation-shaped probes (16 `/new`
  route submits — blocked at render, no form to submit; 2 direct-API POST creates) returned a clean
  403 with zero persistence. No 500s, no 23505-shaped errors observed anywhere in the run — does not
  contradict CRIT-1/CRIT-2 staying closed.
- CRIT-2 (ภ.พ.30) is tax01's primary this round; audit01 independently confirms the SoD half — the
  finalize button is now entirely absent from the DOM for AUDITOR (stronger than round 4's
  "visible but disabled"), and preview/finalize were not clicked (forbidden, hard rule 2).

## Not tested (honest gaps)
- Could not confirm whether `/expense-claims` and `/fixed-assets` are genuinely empty in co5's
  current seed data vs. some other filtering effect — no prior round (any role) recorded seeing
  actual rows there either. No deny/403 was observed, so this does not affect the WP2/WP6 verdict.
- Did not root-cause the exact triggering page for the `expense-categories`/`employees` 403s (LOW
  finding above) within the timebox.
- vendorId/FK placeholder not needed this round — both API probes used an empty `{}` body since the
  permission check runs before body validation (confirmed again: 403, not 400/422).

## Cleanup
- `frontend/swarm5-audit01.mjs` deleted after this run (per hard rule 4).
- `swarm-findings/round5/audit01-raw.json` (intermediate capture) deleted after folding into this
  report (per hard rule 5 — output is this `.md` + `shots/round5/audit01-*.png` only).

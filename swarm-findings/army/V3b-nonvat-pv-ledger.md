# V3b — non-VAT PV fold-into-cost live ledger proof, co7, prod v1.22.12 — **PASS**

Retry of V3 (blocked on co6: all 12 FY2026 months closed, no monthly-reopen route, DocDate
server-pinned to today — see `swarm-findings/army/V3-nonvat-pv-ledger.md` and
`troubles-wiki.md`'s `period.closed` entry) on a **fresh** non-VAT company with an open period.

Target: `https://teas.kazaki-rio.com`, company **co7 = "บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด"
(id=7)**. Users: nvadmin02 (COMPANY_ADMIN, creates master data + the PV draft), nvchief02
(CHIEF_ACCOUNTANT, approves/posts). Driven via headless Playwright (`army-V3b.mjs` +
2 short follow-ups, all deleted after the run per HARD RULES) against
`https://teas.kazaki-rio.com`, `page.request` against the BFF proxy for API calls, real UI
navigation for master-data create + approve/post.

## Company verification

Before every mutating action, confirmed via `GET /api/proxy/me`:
- nvadmin02: `{"userId":24,"companyId":7,"companyName":"บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด"}`
- nvchief02: `{"userId":25,"companyId":7,"companyName":"บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด"}`

Both scoped to co7 only (`isSuperAdmin:false`, single `allowedCompanies` entry) — no
company-switcher risk. co2/co3/co5/co6 never touched.

## Done

1. **Master data** (all created fresh, co7 had none): 1 domestic **VAT-registered** vendor
   (`ผู้ขายจด VAT ทดสอบ V3b`, code `V3BV769748`, `vatRegistered:true`, taxId
   `0105556123453` — deliberately VAT-registered: the vendor charges VAT, the non-VAT
   *company* can't claim it), 1 bank account (`999 / ธนาคารทดสอบ V3b / V3B87771608`,
   defaulted to GL cash account 1120), 1 expense category (`หมวดทดสอบ V3b`, code
   `V3BC73380`, `defaultIsRecoverableVat:true`, `defaultExpenseAccountId` → CoA account
   `5200 ค่าใช้จ่ายค่าบริการ`). Screenshots: `V3b-01-dashboard-nvadmin02.png`,
   `V3b-02-vendor-new-filled.png`, `V3b-03-vendor-list-after-create.png`,
   `V3b-04-bank-new-blank.png`, `V3b-05-bank-new-filled.png`,
   `V3b-06-bank-list-after-create.png`, `V3b-07-expense-categories-list.png` (expense
   categories have no create UI — confirmed by code read, `frontend/app/(dashboard)/
   expense-categories` doesn't exist; created via direct `POST /api/proxy/expense-categories`,
   screenshot is the read-only `/settings/expense-categories` list showing the new row).
2. **Resolved the Input VAT account code from co7's live CoA, not hardcoded**
   (`GET /reports/general-ledger/accounts`, dumped to `V3b-gl-accounts.json`): account
   `1170 ภาษีซื้อ` (DR normal balance) — distinct from `2151` (co6's output-VAT code per
   B2-nv F3) and from `5350 ภาษีซื้อขอคืนไม่ได้` (irrecoverable-VAT expense account). Same
   resolved-from-CoA account gave the expense account too: `5200 ค่าใช้จ่ายค่าบริการ`.
3. **Standalone PV created via direct API call, not the FE form** — and this is itself a
   finding (see below): `payment-vouchers/new/page.tsx`'s `vendorVat` predicate ANDs
   `companyVatRegistered` (L169, the WP-G fix), so on a non-VAT company `lineVat()` is
   **unconditionally 0** (L173) — there is no UI control left to enter the vendor's real 7%.
   The only way to drive this scenario live is the same "MCP/API-key caller, or a form
   drafted while the company was still VAT-registered" shape
   `PaymentVoucherNonVatCompanyTests` exercises: client explicitly sends
   `isRecoverableVat:true, vatRate:0.07`. **Stated BEFORE looking**: net 1,000.00 + vendor's
   7% = gross 1,070.00.
4. **Read the stored draft via the API** (`GET /api/proxy/payment-vouchers/22`, full dump
   `V3b-pv-draft.json`) — quoted exactly:
   - line: `amount:1000, vatRate:0.07, vatAmount:70, isRecoverableVat:false`
   - header: `subtotalAmount:1000, vatAmount:70, totalPaid:1070`
   Server-side WP-G gate fired exactly as designed: flipped the client's `isRecoverableVat:true`
   to `false` (a non-VAT company can never CLAIM input VAT) while leaving `vatRate`/`vatAmount`
   untouched (the VAT is real money the vendor charged — folded into cost, not dropped).
5. **Approved + Posted as nvchief02 via real UI clicks** (`pv-approve` → confirm dialog →
   `pv-post` → confirm dialog, both on `/payment-vouchers/22`). Screenshots
   `V3b-09-pv-before-approve-nvchief02.png` (this one and `V3b-08-pv-draft-detail.png`
   unfortunately captured the client route's `กำลังโหลด…` skeleton, not settled content — a
   navigation-timing race, same class as the documented cold-cache `waitForURL` issue in
   `troubles-wiki.md`, non-blocking since the API reads below are the load-bearing evidence),
   `V3b-10-pv-approved.png`, `V3b-11-pv-posted.png` (re-captured with a text-based wait —
   shows the real posted paper document: doc no `07-2026-PV-V3BC73380-0001`, Grand Total
   **฿ 1,070.00**, vendor, notes, full activity log create→approve→post by nvadmin02/nvchief02).
6. **`GET /api/proxy/journals/173`** (full dump `V3b-journal.json`) — the posted JE:
   ```
   Dr 5200 ค่าใช้จ่ายค่าบริการ   1,070.00
       Cr 1120 เงินฝากธนาคาร           1,070.00
   totalDebit: 1070, totalCredit: 1070
   ```
   Reference `07-2026-PV-V3BC73380-0001` (the PV's own doc no). **Exactly 2 lines — no 1170
   line at all.**
7. **Trial balance** after posting (`GET /api/proxy/reports/trial-balance`,
   `V3b-trial-balance.json`): `{"debit":1070,"credit":1070,"balanced":true}`. Screenshot
   `V3b-12-trial-balance.png`.
8. **co7 final state**: `GET /api/proxy/periods/2026/7/status` → `{"open":true}` — **period
   left open** as instructed (co7 stays usable as the non-VAT playground, unlike frozen co6).
   1 vendor, 1 bank account, 1 expense category, 1 posted PV (`#22`, JE `#173`) — well inside
   the ≤4-document blast cap.

## Expected-vs-actual (the four assertions the dispatch asked for)

| # | Assertion | Expected (stated before looking) | Actual | Result |
|---|---|---|---|---|
| 1 | Expense debit = gross (1,000 + 7% = 1,070.00) | 1,070.00 | JE `#173` line 1: `Dr 5200 = 1,070.00` | **PASS** |
| 2 | No input-VAT (1170) debit line at all | absent | JE `#173` has exactly 2 lines: `5200` (Dr) and `1120` (Cr) — `1170` never appears | **PASS** |
| 3 | `TotalPaid` = gross (1,070.00) | 1,070.00 | `GET /payment-vouchers/22` → `"totalPaid":1070` | **PASS** |
| 4 | Dr = Cr on the posted JE | balanced | `totalDebit:1070 = totalCredit:1070`; TB `{"debit":1070,"credit":1070,"balanced":true}` | **PASS** |

**No net-only (1,000) leak anywhere** — the VAT the vendor actually charged reached the ledger
folded into the expense debit, and the vendor was paid in full. This is the live, posted-JE
confirmation of the same invariant `PaymentVoucherNonVatCompanyTests
.NonVatCompany_StandalonePv_FoldsVatIntoCost_NoInputVatLine` proves at the unit level — V3's
blocked mission is now closed out.

## JE dump

```
Journal #173 (07-2026-JV-0001), Reference 07-2026-PV-V3BC73380-0001, Status Posted
  L1  Dr 5200 ค่าใช้จ่ายค่าบริการ         1,070.00   (description: ค่าบริการทดสอบ V3b)
  L2      Cr 1120 เงินฝากธนาคาร                 1,070.00   (description: Cash/Bank 07-2026-PV-V3BC73380-0001)
  totalDebit = totalCredit = 1,070.00
```

## Findings

**MEDIUM (UX gap, not a money bug — flagged for Ham/Fable's awareness, not filed as a WP
regression)**: on a non-VAT company, `payment-vouchers/new` now has **no way to enter a
vendor-charged VAT rate at all** (WP-G's own FE fix, `companyVatRegistered && (...)` on
L169, correctly hides the VAT UI — but as a side effect also zeroes `lineVat()`
unconditionally at L173, with no override). The fold-to-cost path this whole WP-G effort
protects can currently only be reached by an API/MCP caller (or a stale form drafted while
the company was still VAT-registered), never through the normal create-PV screen. If a
non-VAT company's accountant needs to record a real vendor tax invoice with VAT by hand, there
is today no FE affordance for it — they'd have to know to call the API directly. Server-side
correctness is fully proven (this leg); this is a product-completeness gap for Ham to decide
on, not a defect in WP-G's fix.

**No other findings.** The WP-G server-side gate performed exactly as designed under a live
posted-JE proof: overrides `IsRecoverableVat` only, preserves `VatRate`/`VatAmount`, folds into
the expense debit via `GlPostingService`'s existing `expenseGross = l.IsRecoverableVat ?
l.Amount : l.Amount + l.VatAmount`, no GL code changed, no 1170 exposure.

## Raw evidence files (this directory)

`V3b-gl-accounts.json` (co7's full GL account picker — input VAT = 1170 confirmed live),
`V3b-pv-draft.json` (draft PV #22 as stored, post-gate), `V3b-pv-posted.json` (posted PV #22),
`V3b-gl-expense-account.json`, `V3b-journal.json` (JE #173 full dump), `V3b-trial-balance.json`.

## Screenshots

`V3b-01-dashboard-nvadmin02.png`, `V3b-02-vendor-new-filled.png`,
`V3b-03-vendor-list-after-create.png`, `V3b-04-bank-new-blank.png`,
`V3b-05-bank-new-filled.png`, `V3b-06-bank-list-after-create.png`,
`V3b-07-expense-categories-list.png`, `V3b-08-pv-draft-detail.png` (loading-skeleton
timing artifact, non-blocking), `V3b-09-pv-before-approve-nvchief02.png` (same),
`V3b-10-pv-approved.png`, `V3b-11-pv-posted.png` (re-captured, fully settled — the load-bearing
screenshot), `V3b-12-trial-balance.png`.

## No tenant leak

Every API call and screenshot stayed under co7 (`companyId:7`) for both users throughout;
co2/co3/co5/co6 never appeared in any request or response.

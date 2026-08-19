# PROGRESS — Testing Swarm Round 2 (2026-08-18 night)

Ham handed this off to run autonomously overnight (สั่ง 22:04: "ทำ PLAN-testing-swarm-r2.md ซะ").
Scope: `PLAN-testing-swarm-r2.md` — 6 legs over modules round 1 never touched. Find, don't fix.
Findings append here per leg as found, so a dead session loses nothing.

**Rules in force:** test data through UI/API only, psql READ-ONLY (`test-data-via-ui-only`) ·
co2 is WRITE-BANNED this round (`co2-demo-loadbearing-pl-polluted`) · no `dotnet test` during the
swarm (shared DB) · Legs 1–4 post into co1 → sequential; Leg 5 read-only → parallel-safe ·
workers report findings only, never commits.

## Phase 0 — stack boot: ✅ UP (22:10)

| Piece | State | Evidence |
|---|---|---|
| PostgreSQL 18 | running | `accounting_dev` up; 4 companies, 33 user_roles (round-1 state + co4) |
| API :5080 | ✅ | `Application started`; migration `20260818125457_QuotationSingleInvoice` applied clean (no 23505 on round-1 data); seed **637** applied (co1 tax ID repaired); login `admin` 200 → `access_token` |
| FE :3000 | ✅ | 307 → login (alive); fresh `npm run dev` |

Boot command (verbatim, env dies per shell):
```
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
FileStorage__StorageRoot='D:\teas-attachments' Database__SeedDemoData=true \
dotnet run --project backend/src/Accounting.Api
```
Logins: `admin`/`approver`/`sales_staff`/`ap_clerk`, `rbac_*` (co1), `rbac_nv_*` (co3) = `Admin@1234`;
co2 `demo-*` = `Demo@1234`. Companies local: 1=Demo (VAT), 2=แมนนวล เดโม (VAT), 3=ร้านนอนแวต (non-VAT),
4=NV-ร้านนอนแวต2 (round-1 leftover).

## Leg status

| Leg | Scope | Worker | State |
|---|---|---|---|
| 1 | Payroll (WHT ภ.ง.ด.1/1ก, สปส., GL, closed-period) | sonnet | ✅ 23:0x — 1×🔴 (L1-1), rest PASS w/ evidence |
| 2 | Bank reconciliation (KBiz CSV, tie to satang, dup import) | sonnet | ✅ 09:3x — 2×🔴 1×🟠 1×🟡 |
| 3 | Fixed assets + depreciation (proration, double-run, disposal) | sonnet | ✅ 10:2x — 0×🔴 1×🟠 1×🟡 2×⚪; money math exact |
| 4 | Expense claims (spec-vs-reality first, SoD, VAT/WHT, GL) | sonnet | ✅ 11:0x — 1×🟠 1×🟡, GL worked-example exact |
| 5 | co2 READ-ONLY tie-out + master integrity + report cross-check | sonnet | ✅ 22:25 — verdict N/A (co2 empty), 1×🟡 5×⚪ |
| 6 | Round-1 leftovers (co4 sale, N1/N2 live re-verify) | sonnet | ✅ 23:1x — 1×🔴 1×🟡 +1×🔴 Fable; N1/N2 live PASS (worker died at report step post-work; findings file complete) |

**Browser directive (Ham, 22:3x):** ตรวจผ่าน browser, don't test by raw API. Claude-in-Chrome
extension is a separate client not reachable from this Claude Code session (ToolSearch swept twice —
no browser tools), so workers drive the REAL FE on a real Chromium via **Playwright 1.60**
(frontend/e2e has helpers + idiom specs). Throwaway specs `frontend/e2e/r2-legN-*.spec.ts`, never
committed, cleaned up at consolidation. Direct-API probes remain ONLY for guard checks that are
explicitly about bypassing the UI (plan method #5). DB verification via psql read-only unchanged.
Leg 1 redirected mid-flight at 22:3x; Leg 5 completed pre-directive (read-only sweep, API+SQL —
its findings stand but co2 was empty anyway).
Workers write findings to scratchpad `findings-legN.md`; Fable DB-verifies then appends here
(avoids concurrent-append clobber on this file).

Order: Leg 1 ∥ Leg 5 first → then 2 → 3 → 4 sequential (co1 posting serialized). Leg 6 folds into
whichever co1 slot is free.

## Findings

(appended per leg; severity per round-1 scale, 🔴 money/tax/security → low)

### Leg 1 — Payroll — ✅ walked browser-first (Playwright), ledger tie-out green
Full detail: `scratchpad/findings-leg1.md` (295 lines, 14 screenshots, rendered ภ.ง.ด.1 PDF).

- **L1-1 🔴 CONFIRMED BY FABLE** (SQL + source read): seed 637 repaired `master.companies.tax_id`
  (→ `0105000000012`) but NOT `master.company_profile.tax_id` (still `0000000000000`), and all three
  filing services resolve `EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? ""`
  (Pnd1FilingService.cs:71,113; SsoFilingService.cs:69) — profile wins, so the ACTUAL rendered
  ภ.ง.ด.1 PDF carries `0-0000-00000-00-0` as the taxpayer ID. A real RD filing would go out
  fictitious. Fix shape: repair seed to cover company_profile + consider an F10-style refuse guard
  in the filing services themselves (all-zero payer ID must refuse, per the PV precedent).
- PASS highlights (all DB/PDF/audit-verified): PIT hand-checked exact on 3 bracket scenarios incl.
  YTD carry-forward · SSO cap correctly ฿17,500/฿875 under current law (plan's 15,000/750 was a
  stale assumption, NOT a bug) · O8 mid-month proration exact · O10 deduction ฿5,000 → `Cr 2180`
  exact, Dr=Cr 367,600.81 · edit-door idempotent (audit-log proven) · closed/future period refuses
  typed + bilingual · `sso_batch.missing_employer_account` fires live (422) · RBAC 403/401 sweep
  clean incl. tax-officer OR-gate both directions · 8 malformed probes all typed 400/404, zero raw
  500s · blank employee national ID impossible at creation.
- ⚪ tooling note for other legs: Thai text via inline `curl -d` corrupts to `?` on this
  Windows/Git-Bash bridge — use file-based payloads (documented in findings-leg1.md; NOT a server bug).
- Test data left in co1 (via UI/API per house rule): employees 3–14, payroll runs 2 (202607
  Posted+Paid), 3 (202608 Posted), 4 (202609 Approved).
- Throwaway spec moved out of the tree → scratchpad (`r2-leg1-payroll-cycle.spec.ts`); repo clean.

### Leg 4 — Expense claims — ✅ walked browser-first (worker returned findings in-report; harness blocked its file write)
- Spec-vs-reality: `specs/expense-claims.md` §5 FE items marked [ ] are STALE — full FE surface
  exists (all pages + hooks); source hardening through 2026-08-14 never logged back to the spec.
- **L4-1 🟠 CONFIRMED BY FABLE** (source): whole `/employees` group requires
  `master.employee.manage` (EmployeeEndpoints.cs — deliberate, payroll-sensitive per seed 440
  comment) → ACCOUNTANT, the spec's designated claim submitter, gets 403 on the employee list and
  the create form is unusable (picker empty, canSave needs employeeId). Fails closed — workflow
  bug, not a hole. Fix must be a NAME-ONLY lookup surface, not a naive read split (DTO carries
  payroll data).
- L4-2 ⚪ SoD permission-only BY DESIGN (matches PV's dropped ck_pv_sod ruling) — chief_accountant
  self-approve 200, deliberate.
- L4-6 ⚪ GL exact worked-example match: `08-2026-EX-0001` → JE 76, Dr 5200 ×3 + Dr 1170 70.00
  (recoverable line only), Cr 1120 1,784.00, Dr=Cr; ENT/VEHI categories default non-recoverable
  (ม.82/5-consistent).
- L4-3/4/5 ⚪ PASS: 9 malformed probes all typed (403 before 404 — no existence leak), permission
  gates real (403 behind hidden buttons), edit door idempotent.
- **L4-7 🟡**: closed-period pay guard code-verified only (no closed period available to probe).
- Test data left in co1: expense claims 1–11, JE 76.

### Leg 3 — Fixed assets + depreciation — ✅ walked browser-first, money math exact
Full detail: `scratchpad/findings-leg3.md`. All 10 checklist items covered. Depreciation (incl.
final-month plug), disposal gain/loss, idempotent re-runs (same journalEntryId), closed-period
refusals (`period.closed` 422), year-close probe (`year.periods_not_closed` 422), RBAC 403s,
malformed 400s — all hand-computed/verified to the satang. Both co1 periods still OPEN after run.

- **L3-9 🟠 CONFIRMED BY FABLE** (DB): no validation that disposal date ≥ acquire date — asset
  `R2L3-F-DateOrderProbe` sits in `fixedasset.fixed_assets` with acquire 2026-08-10, disposal
  2026-07-15, status DISPOSED; the dispose modal accepted it (200) and posted a balanced JE dated
  before the asset existed. GL now carries a July loss for an August asset.
- **L3-12 🟡**: `PUT /fixed-assets/{id}` works but NO edit UI exists for Draft assets.
- **L3-2/L3-3 ⚪ design notes**: no first-month proration (full month regardless of acquire day);
  final scheduled month is a plug absorbing skipped months — run-line count ≠ elapsed months.
- ⚪ caveat: FA month-close hook (`period.depreciation_required`) verified by source read only —
  live repro short-circuited by co1 draft-TI debris from earlier legs.
- Test data left in co1: 6 assets `R2L3-*`, 2 depreciation runs, JEs 71–75.

### Leg 2 — Bank reconciliation — ✅ walked browser-first (10/10 throwaway tests green)
Full detail: `scratchpad/findings-leg2.md`. Reconciled-vs-GL ties to the satang (Postgres
cross-check); adjustment JEs Dr=Cr correct; 9 bank routes 403 low-privilege; unmatch/rematch
idempotent; "close reconciliation" doesn't exist BY DESIGN (spec D1: v1 = computed report).

- **L2-2 🔴 CONFIRMED BY FABLE** (source + DB): `BankReconciliationReportService.cs:66` orders
  imports by `OrderByDescending(PeriodEnd)` with NO tiebreaker, then `FirstOrDefault` — with
  multiple imports sharing a PeriodEnd the statement closing balance is ARBITRARY. Live proof in
  accounting_dev: 6 imports all period_end=2026-08-19, closing balances 255→1075; report picked a
  stale one. Fix shape: secondary sort on statement_import_id/imported_at desc.
- **L2-4 🔴 CONFIRMED BY FABLE** (source): `StatementImportService.cs` try/catch (lines 65–75)
  wraps only the parse phase; `SaveChangesAsync` at 117/147 sits OUTSIDE → an oversized field
  (>500 chars) escapes as raw `internal_error` 500 leaking the Postgres exception instead of a
  typed `bank.*` error. Atomicity holds (rollback confirmed, no orphans) — contract bug, not
  corruption.
- **L2-3 🟠**: duplicate import warns but duplicates rows by design; orphaned unmatched lines
  from superseded imports have NO bulk remediation — compounds L2-2's tie problem.
- **L2-1 🟡**: 3 bank-rec DaisyUI modals lack `role="dialog"` (inconsistent with app).
- ⚪ out-of-scope asides in findings file: shared `createVendor()` e2e helper + PV approve/post
  specs likely broken by recent confirm-dialog UI changes (suite debt, not product).
- Test data left in co1: bank account 1, 6 statement imports (r2l2-statement.csv), matching JEs.

### Leg 6 — round-1 leftovers (sales) — ✅ walked browser-first
Full detail: `scratchpad/findings-leg6.md`. Worker died on an API error AFTER completing all three
items and the findings file — nothing lost. Throwaway specs moved to scratchpad; tree clean.

- **L6-1 🔴 CONFIRMED BY FABLE** (source read): Billing Note creation is broken for any line whose
  tax code was never explicitly picked — `BillingNoteDtos.cs:16` types `TaxCodeId` as non-nullable
  `int` while every sibling DTO (SalesChainDtos.cs:17,64; TaxInvoiceDtos.cs:19) is `int?`; FE
  `LineItemsTable` leaves line state `taxCodeId:null` until the VAT `<select>` fires onChange, and
  for a NON-VAT company that select never renders → **non-VAT companies (co3/co4) cannot create
  their only revenue document through the UI at all** (binding throws → generic 400 toast). Worker
  bisected empirically: `taxCodeId:999`→201, `taxCodeId:null`→400. VAT companies hit it too when
  the user accepts the visually-pre-filled 7% without clicking. Task-1's co4 end-to-end sale was
  BLOCKED by this; the TI-refusal half PASSES (NonVatGuard ม.86/4 empty state, no nav links).
- **L6-4 🔴 FABLE-FOUND while verifying L6-1's probe leftovers:** `sales.billing_note_lines.tax_code_id`
  has **no FK to tax.tax_codes** (only billing_notes/products/tax_invoices FKs), and the bisection
  probe's draft (billing_note_id=4, co4, DRAFT, no doc_no) stored `tax_code_id=0` + `tax_code='VAT0'`
  — **neither exists in co4's master** (its codes are ids 37–48; 'VAT0' is co3's code). Round-1 F13
  shape (document carrying a tax code absent from company master) is live on the billing-note path:
  backend accepted a bogus taxCodeId without validating against the company's master. Whether POST
  would catch it is untested (draft left in place as evidence — Ham may want it deleted).
- **L6-2 ⚪ PASS — N1 live:** exempt product (EXEMPT_GOOD, product_id=14) through the real UI:
  screen shows locked 0% + VAT ฿0.00 (screenshots), stored pair `tax_code_id=5 EXEMPT-AGRI is_exempt=t,
  rate 0` survives BOTH doors (create + edit-resave; the Aug-16 hydration fix is what saves door 2).
  Fable re-ran the SQL — pair intact.
- **L6-3 🟡 — N2 live:** guard itself CORRECT — second convert refused, DB shows exactly one posted
  TI for quotation_id=5 (Fable re-verified), API returns typed 409 `quotation.already_invoiced` with
  the covering TI's doc number. But the UI toast swallows it → generic "เกิดข้อผิดพลาด" because
  `quotations/[id]/page.tsx` catches with the broken `e.detail` read instead of the existing
  `problemToast` helper. Repo-wide pattern: **22 files** still carry the ad-hoc catch (list in
  findings file) — incl. BillingNoteForm (which is why L6-1's 400 is also silent).
- Test data left: co4 customer_id=10 + draft billing_note_id=4 (probe evidence); co1 product_id=14,
  quotations 3 (draft) + 5 (accepted/invoiced), TI 08-2026-TI-0003 posted.

### Leg 5 — company 2 integrity sweep (read-only) — ✅ walked, verdict N/A
**Fable-verified** (SQL re-run): journals exist only for co1 (8) and co3 (2); company 2 has ZERO
transactional documents ever (0 TIs, 0 journals, audit log empty save the probe's own
company_switch) while master data is rich (5 customers, 5 vendors, 10 products, 28 COA, 12 tax
codes). Local "company 2 = แมนนวล เดโม" is NOT prod co2 — the plan's "production-shaped volume"
premise does not hold on this stack.

- **L5-1 🟡** Leg-5 core checks (real-volume tie-out, report-vs-SQL cross-check, ภ.พ.30 bucketing
  on real docs) are NOT EXECUTABLE locally — co2 empty. Needs prod-shaped data (post-migration
  server) or a walkthrough-seeded co2. N1-M5-on-real-data stays OPEN this round.
- L5-2 ⚪ pass: all report endpoints degrade gracefully on the empty company (no 500s; TB balanced:true).
- L5-3 ⚪ pass: master FK integrity clean, tax-code master well-formed (12 codes, exempt/zero flags exclusive).
- L5-4 ⚪ needs-write-repro: co2 products all have NULL default_output_tax_code_id → N1 ladder
  step 4 (company-lowest-id exempt fallback → EXEMPT-AGRI) never exercised on a posted doc.
- L5-5 ⚪ needs-write-repro: SalesCategorizer bucketing unverifiable on real co2 lines (none exist).
- L5-6 ⚪ methodology pass: same tie-out SQL on co1 ties out (Dr=Cr=32,724.12, header-line diff 0).

## Round close — Fable's own tie-out battery (2026-08-19, post-all-legs)
Run AFTER every leg finished posting (Leg 5's early co1 tie-out predates legs 1/6/2/3/4):
- **Trial balance: GREEN.** co1 Dr=Cr=1,336,252.66 (diff 0.0000); co3 Dr=Cr=3,599.98 (diff 0.0000)
  over POSTED journals.
- **Header=lines: GREEN.** 0 mismatches across all companies (sum of journal_lines vs header totals).
- **F13 sweep (tax code present in own company's master): 5 VIOLATION ROWS — escalates L6-4.**
  Beyond the known probe row (BN 4, co4, id 0): **co3's SETTLED billing note `08-2026-IV-0001`
  (billing_note_id=3, 2 lines) and co3's ACCEPTED quotation `08-2026-QT-0001` (quotation_id=2,
  2 lines) store `tax_code_id=1` — which is co1's VAT7** — while their denormalized string column
  says 'VAT0' and rate/amount are 0. Money outcome unharmed (0 VAT computed), but the stored id
  points at another company's 7% code: any report resolving via id would misclassify, and the
  id↔string mismatch means the write path stored an id it never validated. These are round-1-era
  documents → the defect predates this round and lives on POSTED/SETTLED rows. U2's migration
  survey MUST match on both (id not in company master) AND (id↔string disagreement).
- **L4-7 RESOLVED (live probe by Fable):** created claim 12 dated 2026-04-15 → submit → approve →
  pay all 200; JE 77 posted **dated 2026-08-19** (payment day). The pay-guard keys on PAYMENT date
  (current month auto-opens), NOT the claim date — so a closed-period refusal is unreachable unless
  the CURRENT month is closed. No contradiction with Leg 3: depreciation keys on its TARGET month
  (April → typed refusal), expense-claim pay keys on today. ⚪ note for Ham: a back-dated claim
  (April) silently books its expense into August GL — defensible cash-basis-at-payment, but nothing
  surfaces the divergence.
- Evidence + all per-leg findings now DURABLE in-repo: `findings-r2/` (leg files 1–6 + artifacts:
  screenshots, the rendered ภ.ง.ด.1 PDF with the all-zero tax ID, throwaway specs). Scratchpad no
  longer load-bearing.
- Extra debris from the battery: claim 12 (PAID), JE 77 (co1).

## Self-retro (Fable, round close)
- **What went wrong in orchestration:** (1) Leg-5 dispatch assumed the plan's "co2 =
  production-shaped volume" premise without a 30-second pre-check (`SELECT count(*)`) — one query
  would have retargeted the leg before spending 127k worker tokens. Pre-flight data-existence checks
  belong in dispatch prep for any "test on real data" leg. (2) Two workers hit the harness block on
  scratchpad file writes; future dispatches say "if a report-file write is blocked, return FULL
  findings in the final report text" (general lesson → fold to minions-assemble at next sync).
  (3) Browser-first should have been asked about at kickoff — Ham had to correct mid-flight; the
  capability review listed no browser tools but didn't surface Playwright-in-repo as the
  UI-testing route. (4) What went right: findings-only contract + Fable re-verification caught a
  worker misread (BN3 'VAT0' string hid a VAT7 id) and the round-close battery found posted-row
  violations every leg missed.

## Resume order (if session dies)
1. Read this file + PLAN-testing-swarm-r2.md.
2. Check stack: API :5080 `/system/info`, FE :3000; reboot per command above if down.
3. Continue from first non-✅ leg in the table; dispatch prompt shape is in the plan §Ops.
4. Quota: 85% → checkpoint + ScheduleWakeup; 7-day ≥85% → full stop, write state, pause.

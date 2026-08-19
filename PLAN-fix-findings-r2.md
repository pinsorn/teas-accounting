# PLAN — fix findings from Testing Swarm Round 2 (GO from Ham 2026-08-19 ~11:4x)

**Ham's decisions:** start now · L2-3 IN SCOPE (delete-superseded-import endpoint, not deferred) ·
co1 wipe: ORDERED, executed at END of batch (after RED-then-GREEN — the debris IS the repro data),
as drop+reseed accounting_dev with pg_dump backup first; doubles as the fresh-boot migration gate.

**Execution waves:** W1: U2 design (Opus) ∥ U1 (Sonnet, test-runner slot) ∥ U7 (Haiku, FE-only).
W2: U3+U4 one warm Sonnet (bank area) → U5+U6 Sonnet (U6 from Fable mini-spec). W3: U2 implement
(Sonnet from approved spec) + Opus review. Then: Tier-3 consolidated gate, Fable full suite + full
diff, Tier-2 on U1/U2/U6, commits per unit, wipe+reseed, live browser re-verify of headline fixes.
ONE test-running worker at a time (shared teas_test).

Source: `PROGRESS-hard-test-r2.md` + per-leg findings files in **`findings-r2/`** (leg files 1–6 +
`artifacts/`: screenshots, the rendered ภ.ง.ด.1 PDF, throwaway Playwright specs).
Round 2 walked payroll, bank rec, fixed assets, expense claims, co2 (empty → N/A), and round-1
leftovers **browser-first** (Playwright driving the real FE per Ham's directive), every number
DB-verified, every accepted finding personally re-verified by Fable in source and/or Postgres.

**Score: 5×🔴 · 4×🟠 · 5×🟡** (+ design notes). N1/N2 fixes (047fe95) verified live PASS both doors.

## Findings table

| ID | Sev | Area | One-liner (evidence in PROGRESS + findings files) |
|---|---|---|---|
| L1-1 | 🔴 | Payroll filings | ภ.ง.ด.1/SSO artifacts render payer tax ID `0000000000000`: seed 637 repaired `companies.tax_id` but filings read `company_profile.tax_id` first (`prof?.TaxId ?? c?.TaxId`, 3 call sites) — real PDF proof |
| L6-1 | 🔴 | Billing notes | `BillingLineInput.TaxCodeId` is non-nullable `int` (siblings are `int?`) → any line whose tax select was never clicked binds to 400; non-VAT companies (no select rendered) are 100% blocked from their only revenue doc |
| L6-4 | 🔴 | Billing notes / schema | `billing_note_lines.tax_code_id` has NO FK and no company-master validation — bogus `taxCodeId:999` accepted, stored `0`/`'VAT0'` absent from co4 master (F13 shape returns) |
| L2-2 | 🔴 | Bank rec | Statement closing balance nondeterministic: `OrderByDescending(PeriodEnd)` with no tiebreaker (`BankReconciliationReportService.cs:66`); 6 tied imports live in dev DB as proof |
| L2-4 | 🔴 | Bank rec | Oversized statement field (>500 chars) escapes as raw `internal_error` 500 leaking Postgres text — `SaveChangesAsync` (117/147) outside the parse try/catch (65–75); atomicity holds |
| L2-3 | 🟠 | Bank rec | Duplicate import warns-but-duplicates by design, leaves permanently orphaned unmatched lines, no bulk remediation — compounds L2-2 |
| L3-9 | 🟠 | Fixed assets | Disposal date before acquire date accepted (200): `R2L3-F` acquired 2026-08-10, disposed 2026-07-15, balanced JE posted into July |
| L4-1 | 🟠 | Expense claims | ACCOUNTANT (intended claim submitter) can't populate the Employee picker: whole `/employees` group behind `master.employee.manage` (payroll-sensitive by design, seed 440) → create form unusable for its primary actor |
| L6-3 | 🟡 | FE error UX | Typed domain errors swallowed to generic "เกิดข้อผิดพลาด": broken `e.detail` catch instead of existing `problemToast` — **22 files** (list in findings-leg6.md); makes L6-1's 400 silent too |
| L2-1 | 🟡 | FE a11y | 3 bank-rec modals lack `role="dialog"` |
| L3-12 | 🟡 | Fixed assets | `PUT /fixed-assets/{id}` works but no Draft-asset edit UI exists |
| L4-7 | ⚪ | Expense claims | RESOLVED by Fable live probe: pay-guard keys on PAYMENT date (current month auto-opens) — April-dated claim 12 paid fine, JE 77 dated 2026-08-19. Refusal unreachable unless CURRENT month closed. New ⚪: back-dated claims silently book into today's period (cash-basis defensible; surface it?) |
| L5-1 | 🟡 | Environment | Local co2 has ZERO transactions → real-volume tie-out / report cross-check / ภ.พ.30-on-real-data NOT RUN this round; needs prod-shaped data (post-server-migration) |

Design notes (not defects, Ham may still want them): L3-2 no first-month proration; L3-3 final-month
plug absorbs skipped months; L4-2 expense-claim SoD is permission-only (matches the deliberate PV
ruling); SSO cap is ฿17,500/฿875 under current law (plan's 15,000 was stale).

## Fix units (routing per ladder; DO NOT start without Ham's go)

### U1 — L1-1 payer tax ID in filings 🔴 (money/compliance)
Seed 638: repair `company_profile.tax_id` where all-zero (same shape as 637) **+** F10-style refuse
guard in `Pnd1FilingService`/`SsoFilingService` (all-zero/blank employer tax ID → typed
`filing.payer_tax_id_missing`-family error, mirroring the PV precedent Ham ruled REFUSE).
Route: Sonnet implements + Opus reviews (proven in-repo pattern; ladder #4 airtight path).
Traps: 3 call sites (Pnd1:71,113; Sso:69) — guard at service level, not per-call-site; ภ.ง.ด.1ก
shares the root; RED-then-GREEN against the rendered PDF, not just the DTO.

### U2 — L6-1 + L6-4 billing-note tax-code integrity 🔴🔴 (money + schema/migration)
One coherent design: (a) `TaxCodeId` nullable in `BillingNoteDtos` matching siblings; (b) server-side
resolution for null (non-VAT company default code — note co4's master LACKS a plain non-VAT sale
code, seed gap to close; co3 has `VAT0`); (c) validation that any provided tax code id/code exists in
the COMPANY's master (F13 rule) + decide FK vs CHECK vs service-validation; (d) FE `LineItemsTable`
line-state default. **Opus DESIGN first** (schema + the money resolver = footgun zone), Sonnet
implements from spec, Opus reviews. Traps: existing posted rows must stand (co3 billing_note 3 has
VAT0); dev has evidence row billing_note_id=4 (co4, DRAFT, bogus code 0/'VAT0') — decide
delete-vs-keep in the migration story; prod may hold similar rows — migration must survey first;
money invariant stated AS an invariant (totals unchanged for existing docs; non-VAT docs carry
0 VAT always), per the 2026-07-25 lesson.
**ESCALATION (Fable round-close battery):** the violation is NOT only the new probe row — co3's
SETTLED billing note `08-2026-IV-0001` (BN 3) and ACCEPTED quotation `08-2026-QT-0001` (QT 2) store
`tax_code_id=1` = **co1's VAT7** while their denormalized string says 'VAT0' (rate/amount 0, money
unharmed) — round-1-era rows, so the unvalidated write path predates this round and has already
reached POSTED/SETTLED documents. The migration survey must match BOTH (id absent from company
master) AND (stored id ↔ stored string disagreement), and the design must rule on remediation for
posted rows (immutable docs — likely a repair migration fixing the id to the company's own
matching-code row, never touching amounts).

### U3 — L2-2 (+L2-3 scope decision) bank-rec determinism 🔴
Secondary sort `ThenByDescending(statement_import_id)` (or imported_at) at
`BankReconciliationReportService.cs:66` — near-one-line. L2-3 (orphan remediation / supersede story)
is a SCOPE DECISION for Ham: minimal option = "delete superseded import + its unmatched lines"
endpoint; defer if Ham prefers. Route: Sonnet (money-adjacent; too much judgment for Haiku).
Trap: "latest" must be defined by import identity, not file name; add the regression test with two
tied imports.

### U4 — L2-4 typed error on import persistence 🔴
Widen the try/catch (or pre-validate line lengths at parse) so persistence failures return typed
`bank.import_failed`-family 422 without Postgres text. Route: Sonnet. Trap: keep the confirmed
rollback/atomicity behavior; test with the >500-char field fixture from findings-leg2.md.

### U5 — L3-9 disposal date validation 🟠
Refuse disposal_date < acquire_date (and < depreciation_start_date) with typed 422. Route: Sonnet.
Trap: closed-period guard already exists and passed — don't disturb; decide whether the existing
bad row (R2L3-F, dev-only) gets cleaned by hand.

### U6 — L4-1 employee picker for claim submitters 🟠 (security-adjacent design)
NOT a plain perm split: `/employees` is gated hard because the DTO carries payroll data (seed 440
intent). Fix = a minimal name-only lookup surface for claim submitters — either a
`master.employee.read` split whose LIST endpoint returns id+name+code ONLY, or a dedicated
`/employees/lookup` endpoint with the reduced DTO, granted to ACCOUNTANT. Route: Sonnet implements
from a Fable-written mini-spec (Fable co-authors: permission surface = footgun);
Opus review. Traps: rbac-seed-ordering footgun (insert perm code BEFORE grant script number);
run RbacAuthMapTests; ensure salary fields never serialize on the new path.

### U7 — L6-3 problemToast migration ×22 files 🟡
Mechanical: swap `(e as {detail?:string})?.detail ?? tc('error')` → `problemToast(e, tc('error'))`
across the 22 files listed in findings-leg6.md. Route: Haiku (exact recipe, zero judgment; stop on
any file that deviates from the pattern) + Sonnet spot-review of the diff. Trap: `oauth/consent`
may have a different toast context — Haiku stops there if unsure.

### U8 — small batch 🟡
L2-1 modals `role="dialog"` (Haiku) · L3-12 Draft-asset edit page (Sonnet, small) · doc hygiene:
mark `specs/expense-claims.md` §5 [x] with evidence + append the 2026-08-14 hardening to its log,
sync `specs/payroll-deductions-o10.md` [~] item per Leg-1 pass evidence (Haiku).

## Open items (not code fixes)
- L5-1: rerun the co2-style leg on prod-shaped data after server migration (or after a walkthrough
  seeds local co2).
- L4-7: live closed-period pay probe next time a closed month exists in the test window.
- E2E suite debt (worker aside): shared `createVendor()` helper + PV approve/post specs likely red
  from confirm-dialog UI changes — separate suite-repair task, verify before trusting suite green.
- co1 is now full of R2 test debris (payroll runs 2–4, 6 bank imports, 6 assets, 11 claims,
  JEs ~60–76, quotations, TI). Fine for dev; wipe+reseed before any demo/walkthrough.
- Leg-1 tooling note for future dispatches: inline `curl -d` corrupts Thai on this bridge → file
  payloads (already candidates for troubles-wiki; folded at next curation pass).

## Exit criteria for the fix batch
Every 🔴/🟠 unit RED-then-GREEN with the leg's own repro (L1-1 against the rendered PDF; U2 against
a non-VAT UI create), suite green, Tier-2 on U1/U2/U6 (money/compliance/permission), Fable full-diff
review, then live re-verify through the browser exactly like this round.

## Deploy runbook additions (from Tier-2 N1/N4, 2026-08-19)
- BEFORE the boot that applies seed 638 on prod: `SELECT company_id FROM master.company_profile WHERE tax_id = '0000000000000';` must return ONLY the demo company or nothing — any real tenant row = STOP, Ham rules first.
- Post-deploy probe set: widen the class-B survey (id valid, string disagrees) to quotation_lines, sales_order_lines, delivery_order_lines, billing_note_lines — not just tax_invoice_lines.

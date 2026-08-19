# Testing Swarm Round 2 — Leg 4 findings (expense claims)

(Reconstructed by Fable from the worker's final report — the harness blocked the worker's own
file write. Company 1, browser-first via Playwright; throwaway spec `r2-leg4-expense-claims.spec.ts`
— 7 tests green — archived in `artifacts/`.)

## Spec-vs-reality
`specs/expense-claims.md` sections 1–4 (schema/permissions/money-path/state-machine) are `[x]`;
section 5's FE pages listed `[ ]` are **STALE documentation, not a gap** — the full FE surface
exists: `frontend/app/(dashboard)/expense-claims/{page,new/page,[id]/page,[id]/edit/page}.tsx`,
`EmployeeSelector.tsx`, all `queries.ts` hooks. The spec's attempt log stops 2026-07-10 but source
carries hardening dated through 2026-08-14 (account-type allowlist, satang/precision guards,
non-VAT re-guards, numbering-collision retry, 409-on-double-pay) never logged back.

## Findings

### L4-1 🟠 ACCOUNTANT (the intended claim submitter) cannot populate the Employee picker
- `GET /api/proxy/employees` → 403 for `rbac_accountant`: `EmployeeEndpoints.cs` gates the ENTIRE
  `/employees` group (including plain list/read) behind `master.employee.manage`, held only by
  CHIEF_ACCOUNTANT/COMPANY_ADMIN/SUPER_ADMIN. Deliberate payroll-sensitivity gate (seed 440
  comment) — but the claim-create form's employee `<select>` renders only its placeholder and
  `canSave` requires non-null `employeeId` → **form unusable for the feature's primary actor**
  (the spec's role-split ruling gives ACCOUNTANT `expense.claim.create`+`read` only).
- Fails closed — wrong-behavior/broken-workflow, not a security hole.
- Screenshot: `artifacts/L4-1-employee-picker-403.png`.
- Fix direction (see PLAN-fix-findings-r2.md U6): NAME-ONLY lookup surface (id+name+code), not a
  naive read split — the employee DTO carries payroll data.

### L4-2 ⚪ SoD is permission-only BY DESIGN
`rbac_chief_accountant` created+submitted+approved its own claim, 200 OK. Matches
`ExpenseClaim.Approve` doc comment and PaymentVoucher's deliberately-dropped `ck_pv_sod` CHECK
(per `pv-approval-permission.spec.ts`) — a standing Ham ruling, not a defect.

### L4-3 ⚪ PASS — malformed probes all typed, never raw 500 (9 probes)
negative amount → 400 · unknown category → 422 `expense_category_missing` · empty lines → 400 ·
vatRate=1.5 → 400 · paymentMethod=BITCOIN → 400 · TRANSFER without bankAccountId → 400 (for a
permission holder) · nonexistent bankAccountId → 422 · approve/pay on nonexistent id → 404 for a
permission holder, 403 for a non-holder (authz short-circuits before leaking document existence).

### L4-4 ⚪ PASS — permission gating real, not cosmetic
`rbac_sales_staff` (zero expense perms) → 403 on list/create. `rbac_accountant` approve/pay on
another user's claim → 403. Every lifecycle button wrapped in `<PermissionGate>` — real 403 behind
hidden buttons (F6 shape covered).

### L4-5 ⚪ PASS — edit door
Re-saving an unchanged draft: totals identical, status Draft, no JE; version 0→1 per the
documented "always rewrite lines" convention (not a bug).

### L4-6 ⚪ PASS — GL posting exact worked-example match
Claim `08-2026-EX-0001` → JE 76 (`08-2026-JV-0070`): Dr 5200 (expense) 500/1000/214,
Dr 1170 (Input VAT) 70.00 — recoverable line ONLY, Cr 1120 (Bank) 1,784.00, Dr=Cr=1,784.00
(SQL-verified). Zero WHT lines — correct, reimbursement is not a withholding event.
`default_is_recoverable_vat` per category is ม.82/5-consistent (ENT/VEHI non-recoverable).

### L4-7 🟡 Closed-period pay guard — resolved by Fable post-hoc, see PROGRESS
Worker verified `PayAsync` → `period.EnsureOpenAsync` in code but reported "no closed period to
probe". (Leg 3 DID get a live `period.closed` refusal against April 2026 — contradiction resolved
by Fable's own probe; see the round-close section of PROGRESS-hard-test-r2.md.)

## Test data left in company 1
Expense claims ids 1–11 (6 Approved, 2 Draft, 2 Submitted, 1 Paid), JE 76. No accounting-period
rows touched.

# SPEC — U6: name-only employee lookup for expense-claim submitters (L4-1)

Author: Fable (permission surface = footgun; design decisions below are BINDING).
Implementer: Sonnet. Reviewer: Opus (permission lens). Blast cap: **7 files** (update this number
if remediation rounds add scope — in the same edit that adds the findings).

## Problem (Fable-verified)
`EmployeeEndpoints.cs` gates the whole `/employees` group behind `master.employee.manage`
(deliberate — the employee DTO carries payroll data, seed 440). ACCOUNTANT holds
`expense.claim.create` but not `.manage` → the claim form's Employee picker gets 403 and the form
is unusable for its intended primary actor. Evidence: findings-r2/findings-leg4.md L4-1 +
artifacts/L4-1-employee-picker-403.png.

## Binding design
1. **Do NOT loosen `/employees`.** The existing group and its permission stay exactly as-is.
2. New endpoint `GET /employees/lookup` in the SAME endpoints file but registered OUTSIDE the
   gated group, `RequireAuthorization` on a NEW permission code `master.employee.lookup`.
3. Response DTO — name-only, nothing else: `[{ employeeId, employeeCode, fullNameTh }]`
   (match casing/naming of neighboring DTOs). Active employees only, company-scoped exactly like
   the existing list. **No salary, no national ID, no bank, no dates — assert this in a test.**
4. **Seed 640** (639 is RESERVED for U2's possible seed — do not take it):
   `640_seed_employee_lookup_perm.sql` — INSERT the permission code FIRST, then grant, in this one
   script (rbac-seed-ordering footgun: a grant whose code is inserted by a later-numbered script
   silently no-ops). Grants: every role that holds `expense.claim.create` (at minimum ACCOUNTANT,
   CHIEF_ACCOUNTANT, COMPANY_ADMIN) — derive the grant set IN SQL from existing
   `expense.claim.create` grants rather than hardcoding role names, so custom tenants inherit.
5. FE: `EmployeeSelector.tsx` switches to `/employees/lookup`. No other FE surface changes.
6. Constants: add the permission to the C# `Permissions.Master` constants + any FE permission map
   if one exists (grep the existing `master.employee.manage` string in frontend).

## Tests (RED-then-GREEN)
- [ ] Endpoint test: `rbac_accountant` → 200 on `/employees/lookup`, still 403 on `/employees`.
- [ ] DTO leak test: serialized lookup response contains NO key matching salary/nationalId/bank
      (assert on the raw JSON keys).
- [ ] RbacAuthMapTests green (set `TEAS_REPO_ROOT` in the same shell — subst-drive footgun).
- [ ] Grant-derivation test or SQL assertion: every role with expense.claim.create got lookup.

## Attempt log
(implementer appends)

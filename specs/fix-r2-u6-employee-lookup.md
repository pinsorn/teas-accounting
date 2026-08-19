# SPEC — U6: name-only employee lookup for expense-claim submitters (L4-1)

Author: Fable (permission surface = footgun; design decisions below are BINDING).
Implementer: Sonnet. Reviewer: Opus (permission lens). Blast cap: **9 files** (was 7; raised
2026-08-19 with the implementer's per-file arithmetic — see Blast-cap note — Fable accepted).

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
- [x] Endpoint test: accountant-shaped token (real DB-resolved grants, not hand-typed) → 200 on
      `/employees/lookup`, still 403 on `/employees`.
      `EmployeeLookupGrantTests.Accountant_shaped_token_gets_200_on_lookup_but_stays_403_on_manage`.
- [x] DTO leak test: serialized lookup response contains NO key matching salary/nationalId/bank/tax
      (assert on the raw JSON keys of a REAL seeded employee, not an empty array).
      `EmployeeLookupGrantTests.Lookup_response_never_leaks_salary_national_id_or_bank_fields`.
- [x] RbacAuthMapTests green (`TEAS_REPO_ROOT` set in the same shell) — 1/1 pass; the new
      `GET /employees/lookup` → `master.employee.lookup` route appears correctly in the
      regenerated `docs/rbac/endpoint-permission-map.generated.md` (Perm count 297→299,
      registered OUTSIDE the `/employees` manage group).
- [x] Grant-derivation SQL assertion: every template role holding `expense.claim.create` also
      holds `master.employee.lookup` (`EmployeeLookupGrantTests.
      Every_template_role_holding_expense_claim_create_also_holds_employee_lookup`); confirmed in
      the regenerated `docs/rbac/role-permission-matrix.md` — ONLY ACCOUNTANT (+1 → 59),
      CHIEF_ACCOUNTANT (+1 → 80), COMPANY_ADMIN (+1 → 87) gained the new permission; no other role
      changed count.

## Blast-cap note (arithmetic, per CLAUDE.md's "report the arithmetic, don't silently exceed")
Spec header says 7. Actual: **9 files** (7 modified + 2 new). Every file is required by a distinct
architecture layer or an explicit binding-design point — none is incidental scope creep:
1. `Permissions.cs` — binding design point 6 (constant).
2. `PermissionCatalog.cs` — bilingual label for the new constant (repo convention: constant + `All`
   + label always travel together, per WP6/FixedAsset precedent; the completeness test would still
   pass without it, but the role editor would show a raw code with no label for any future custom
   role granted this permission).
3. `EmployeeDtos.cs` — new `EmployeeLookupItem` DTO (binding design point 3) + interface method.
4. `EmployeeService.cs` — `LookupAsync` implementation (Infrastructure layer, separate from the
   DTO's Application layer by existing architecture).
5. `EmployeeEndpoints.cs` — binding design point 2 (new route).
6. `SqlScripts/640_...sql` — binding design point 4.
7. `EmployeeLookupGrantTests.cs` — the 4 checklist tests above (new file; no existing file fit —
   `Rbac/` folder convention names one file per grant, mirrors `ReadManageSplitGrantTests.cs` /
   `TaxOfficerFilingGrantTests.cs` / `AuditorReadApproverGrantTests.cs`).
8. `frontend/lib/queries.ts` — `EmployeeLookupItem` type + `useEmployeeLookup()` hook (binding
   design point 5 needs a hook to switch to).
9. `EmployeeSelector.tsx` — binding design point 5 (the actual switch).

Point 6's FE half ("any FE permission map if one exists") is a **no-op**: grepped
`master.employee.manage` in frontend — its only hits are route-nav gating
(`SidebarNav.tsx`/`rbac-manifest.ts`, unrelated to this picker) and the MCP API-key scope list
(`settings/api-keys/page.tsx` `ALL_SCOPES`/`MCP_DEFAULT_SCOPES`, a SEPARATE namespace for MCP
tool grants — deliberately NOT touched: adding `master.employee.lookup` there is out of this
spec's scope, and `McpScopeFrontendParityTests` would need a matching backend
`McpScopes` change this spec never asked for). No central FE permission map exists for the
`/employees/lookup` surface itself.

Also auto-regenerated by running `RbacAuthMapTests`/the Rbac test suite (not hand-edited, derived
docs kept in sync by the test harness itself): `docs/rbac/endpoint-permission-map.generated.md`,
`docs/rbac/role-permission-matrix.md`.

## Attempt log
1. (2026-08-19, Sonnet) Read the spec + repo precedents (`629_seed_read_manage_split_grant.sql`,
   `ReadManageSplitGrantTests.cs`, `FixedAssetPermissionTests.cs`) for the grant-derivation +
   HTTP-RBAC-test patterns. Implemented all 6 binding-design points + wrote 640 (template top-up +
   per-company sync + direct-grant regression guard, mirrors 629's step 5b) + the 4 tests. Grepped
   `[{}]` on the new SQL file before running (troubles-wiki brace-in-comment scar) — caught and
   fixed one instance in my own footgun-warning comment. Built clean (0 warnings/errors). Ran
   targeted: `EmployeeLookupGrantTests` 3/3, `RbacAuthMapTests` 1/1, then the full `Rbac|Employee`
   filter as a regression check — 83/83, 0 skipped, 0 failures (includes `RbacCartesianTests`,
   `RbacMatrixTests`, `ReadManageSplitGrantTests`, `Identity.RbacAdminServiceTests`'s
   `PermissionCatalog` completeness test, `EmployeeSalaryPrecisionTests`,
   `EmployeeTerminationDateTests`). Frontend: `npx tsc --noEmit` clean (exit 0); no jest/vitest
   component tests reference `EmployeeSelector` or `/employees` to update.

# SPEC — Cleanup Unit C11: `181_seed_demo_pv_users.sql` seeds users but no roles

**Blast-radius cap: max 4 files. No EF migration, no C# schema change, no new dependency.**
**Do NOT `git commit`.** The orchestrator runs the gates, reviews the diff and commits.

## The defect (confirmed live 2026-08-19, fresh wipe+reseed)

`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/181_seed_demo_pv_users.sql` seeds
two demo users (`ap_clerk`=3, `sales_staff`=4) and then tries to assign each a role via:

```sql
INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 3, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r WHERE r.role_code = 'AP_CLERK' AND r.company_id = 1
ON CONFLICT DO NOTHING;
```

`sys.roles` is a **G3** RLS table (`600_superadmin_scoped_rls.sql`): `FORCE ROW LEVEL SECURITY`,
policy `company_id IS NULL OR company_id = app.company_id OR app.bypass_rls`. `DbInitializer`
(and `PostgresFixture`, same code path) run every `SqlScripts/*.sql` file in its own transaction
with **no session GUCs set at all** — no `app.company_id`, no `app.bypass_rls`. Under that
context the `SELECT ... FROM sys.roles WHERE ... company_id = 1` matches **zero rows** (neither
disjunct of the policy is true), so the `INSERT ... SELECT` silently inserts nothing — no error,
`ON CONFLICT DO NOTHING` on an empty source set is a no-op. `sys.user_roles` itself carries no
RLS (confirmed: not listed in any of 600's G1/G2/G3 arrays), so the write side was never the
problem — only the read side of the correlated subquery.

Symptom: `sys.user_roles` totalled 28 rows instead of the historical 33 on a fresh
wipe+reseed. Login for `ap_clerk`/`sales_staff` 401s `auth.no_company_assignment`
(same class as `troubles-wiki.md`'s "RLS masked by superuser tests" and the Unit D/F1 finding in
`specs/fix-company-roles-seed-ordering.md`, but here it hit the seed script itself).

Diagnosed and documented by the e2e-repair worker: `troubles-wiki.md` § "Fresh reseed:
`ap_clerk`/`sales_staff` login 401s `auth.no_company_assignment` (RLS-seed footgun in
181_seed_demo_pv_users.sql)". That worker's live fix went through the app's own
`PUT /admin/rbac/users/{id}/roles` admin API (real authorization/tenant context, idempotent) —
`accounting_dev` currently shows the correct 1-role-each state, but that is a **hand patch of
one database**, not a fix to the seed. Do not trust current `accounting_dev` state as proof the
seed works.

## Root-cause classification and fix shape (decide with evidence, record here)

Two independent questions:

1. **Does a FRESH install (new demo-seeded DB, 181 has never run) come up broken?** Yes — the
   RLS bug is deterministic, not data-dependent. Every fresh `SeedDemoData=true` boot hits it.
   **Fix: patch 181 in place** — wrap both `INSERT ... SELECT` statements in
   `SET LOCAL app.bypass_rls = 'on';` (transaction-scoped, auto-reverts, matches 636/639/640's
   idiom). This is a genuine bugfix to the seed script's own logic, not a data repair — safe to
   edit 181 directly (no applied-once concern for a DB where 181 has never run).

2. **Does an EXISTING DB (181 already applied and recorded in `sys.applied_sql_scripts`) stay
   broken even after 181 is fixed?** Yes — confirmed live against `teas_test`
   (2026-08-19, read-only `psql` probe, superuser connection so RLS is bypassed for the probe
   itself):
   - `sys.applied_sql_scripts` contains `181_seed_demo_pv_users.sql` (already recorded).
   - `sys.roles` has `AP_CLERK` (role_id 17) and `SALES_STAFF` (role_id 18) for `company_id=1`.
   - `sys.users` has `ap_clerk` (user_id 3) and `sales_staff` (user_id 4), both active.
   - `sys.user_roles` has **zero** rows for `user_id IN (3,4) AND company_id=1`.

   `DbInitializer.ApplyScriptsAsync` / `PostgresFixture` both skip any script name already present
   in `sys.applied_sql_scripts` — editing 181's *file contents* does not make it re-run.
   `teas_test` is exactly the "existing DB that already reached the broken state" case, same class
   `636`'s own header describes for the company-roles hole it repairs (a reconcile script, not a
   retroactive fix to the original seed). **Fix: new reconcile script 641** — SYSTEM (not
   added to `DbInitializer.DemoScripts`), following the 636/637/638 precedent: company-agnostic
   guard so it is a safe no-op on any DB (prod included) where `company_id=1`'s
   `ap_clerk`/`sales_staff` rows don't exist, and it only inserts what's missing (`NOT EXISTS`),
   idempotent by construction.

   Classified SYSTEM rather than added to `DemoScripts` because — per the existing 530/560/636
   precedent documented in `DbInitializer.cs`'s own doc comment — a *reconcile* script that
   references company-1-shaped literals but is a no-op when that data is absent stays SYSTEM;
   only scripts that unconditionally *create* placeholder/demo data belong on the DEMO allowlist.
   641 never creates anything that doesn't already exist (it requires the `sys.users` row and the
   `sys.roles` row to already be present before it does anything) — a fresh prod install has
   neither, so it is inert there. This also sidesteps the applied-once trap entirely: 641 is a
   brand-new script name, so it is guaranteed to run on the *next* boot of any DB regardless of
   `SeedDemoData`'s current or historical value, without an operator having to
   `DELETE FROM sys.applied_sql_scripts WHERE script_name = '181...'` by hand.

**Decision: BOTH paths are needed** — 181 in place (fresh installs) AND 641 (existing DBs,
including `teas_test` and `accounting_dev` today).

## Files (cap: 4)

1. `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/181_seed_demo_pv_users.sql` — add
   `SET LOCAL app.bypass_rls = 'on';` before the two `INSERT ... SELECT` statements.
2. `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/641_reconcile_demo_pv_user_roles.sql`
   — new SYSTEM reconcile script.
3. One test file (new or existing) proving the fresh-DB/reconciled shape.
4. This spec file.

## Traps

- No curly braces anywhere in either SQL file (`ExecuteSqlRawAsync` treats `{`/`}` as
  `string.Format` placeholders — boot-loops the API).
- State the RLS/GUC context explicitly in 641's header, matching 639/640's convention.
- Idempotent: a second run of either script must be a silent no-op.
- 641 must not touch any company other than 1, and must not assume `company_id=1` exists (must be
  a no-op if it doesn't — same contract as 636/637/638).

## Checklist

- [x] Read `troubles-wiki.md` entry + `181_seed_demo_pv_users.sql` + `636` for the mechanism.
- [x] Confirmed live against `teas_test`: 181 applied, roles exist, users exist, `user_roles` empty
      for user 3/4 @ company 1 — matches the wiki entry exactly.
- [x] Decided fix shape: 181 in-place fix (fresh installs) + new 641 reconcile (existing DBs).
      Reasoning recorded above.
- [x] RED: write test against current `teas_test` state (0 role rows for user 3/4 @ company 1) —
      run and confirm it fails for the right reason.
- [x] Patch 181 with `SET LOCAL app.bypass_rls = 'on';`.
- [x] Write 641 (SYSTEM, no-op-safe, idempotent, RLS header documented).
- [x] GREEN: re-run test suite (fixture applies 641 automatically as a new, previously-unapplied
      script) — confirm the test now passes.
- [x] Evidence pasted below.

## Evidence

**RED** (before 181/641 existed, `teas_test` reproduces the wiki-entry defect live):
```
Accounting.Api.Tests.Rbac.DemoPvUserRoleSeedTests.Ap_clerk_and_sales_staff_hold_their_company1_role_assignment [FAIL]
Expected assignments {empty} to contain "ap_clerk:AP_CLERK" because ...
Failed: 1
```

**GREEN** (after 181 patched + 641 added; PostgresFixture auto-applied 641 as a new,
previously-unapplied script on the next test-process boot):
```
Passed Accounting.Api.Tests.Rbac.DemoPvUserRoleSeedTests.Ap_clerk_and_sales_staff_hold_their_company1_role_assignment [36 ms]
Total tests: 1 / Passed: 1
```

**Data-layer confirmation** (`psql`, superuser connection):
```
 user_id |  username   | role_id |  role_code  | company_id | branch_id
---------+-------------+---------+-------------+------------+-----------
       3 | ap_clerk    |      17 | AP_CLERK    |          1 |         1
       4 | sales_staff |      18 | SALES_STAFF |          1 |         1
```
`sys.applied_sql_scripts` now records `641_reconcile_demo_pv_user_roles.sql`.

**Idempotency** — manual `psql -f` replay of 641 after it already applied:
```
INSERT 0 0
INSERT 0 0
```
row count unchanged (2). Second `dotnet test` process boot (641 already recorded, fixture skips
it) — test still passes (rows persist).

**Regression sweep** — `dotnet test ... --filter "FullyQualifiedName~Rbac"`: 72/72 passed, 0
failed, 0 skipped (includes `Identity.RbacAdminServiceTests`, `OAuth.OAuthScopeRevalidationTests`,
and the new `Rbac.DemoPvUserRoleSeedTests`). This is a filtered sweep, not the full suite — see
worker report for scope.

# SPEC — Unit D / F1: a company created by SQL after script 510 gets no roles, and nobody can log in

Finding and evidence: `PROGRESS-local-hard-test.md` F1. Plan: `PLAN-fix-findings-2026-08-16.md` Unit D.

**Blast-radius cap: max 4 files. No EF migration, no C# schema change, no new dependency.**
**Do NOT `git commit`.** Fable runs the gates, reviews the diff and commits.

Other workers are active in this tree: one is writing a spec (reads only), one is editing
`frontend/app/(dashboard)/payment-vouchers/new/page.tsx`. **Your files are under
`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/` plus, if you add one, a test.** Stay out of
the frontend entirely.

## The defect
`510_per_company_roles_reconcile.sql:109-116` materialises the per-company role catalogue by looping over
`master.companies` **once**, at the moment the script runs, and the script is then recorded in
`sys.applied_sql_scripts` so it never runs again. Any company that appears in `master.companies` *after*
that point, by raw SQL, therefore has **no roles at all**.

Reproduced: booting once with `Database:SeedDemoData=false` (so the DEMO company scripts 120/400/440 are
skipped) and then flipping the flag on for a later boot creates the demo companies *after* 510 has been
recorded. Result: `sys.roles` holds only the single global `SUPER_ADMIN` row, `sys.user_roles` holds 6
rows covering just the super-admins, and every seeded role user — the twenty `rbac_*` accounts plus
`ap_clerk` and `sales_staff` — gets **401 `auth.no_company_assignment`** on login. RBAC is untestable and
the tenants are unusable by anyone but a super-admin.

**Real tenants are not affected**, and the fix must not disturb that path:
`CompanyService.CreateAsync` calls `SELECT sys.seed_company_roles({companyId})` inside its transaction
(`backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs:388`). A company created through the
app always gets its roles — confirmed live on company 4, created through the UI, which came up with all
11 roles and a 30-account chart of accounts.

## Required behaviour, in two halves

**(a) Repair.** Any company that currently has no per-company roles gets them, without disturbing
companies that already do. `sys.seed_company_roles(p_company_id)` (defined at `510:83-106`) is already
idempotent — it inserts only role codes and grants that are missing — so calling it for every company is
safe; calling it only for companies that need it is cheaper and states the intent more clearly.

**(b) Prevention.** The DEMO company scripts should not depend on having run before 510. Each script that
inserts a company should call `sys.seed_company_roles` for that company immediately afterwards, so the
company is complete when the script finishes regardless of ordering. Those scripts are already recorded as
applied on existing databases, so this half only takes effect on a fresh install — which is exactly where
it is needed, and it is why half (a) exists for everything else.

Identify the DEMO scripts that insert into `master.companies` by reading them; the finding names
`120_seed_demo_company.sql`, `400_seed_manual_demo_company.sql` and `440_seed_nonvat_demo_company.sql`,
but verify rather than trusting the list, and check whether any later script also creates a company.

The next free script number is **636**.

## Traps — read before writing SQL
1. **Never insert the global SUPER_ADMIN as a per-company role.** `sys.roles` carries
   `ck_roles_company_required` (company_id NOT NULL except for SUPER_ADMIN) and a partial unique index
   `ux_roles_global_role_code` on `role_code WHERE company_id IS NULL`. `sys.seed_company_roles` copies
   from `sys.role_templates`, which deliberately excludes SUPER_ADMIN (`510:36-40`). Do not widen it.
2. **No curly braces anywhere in a SQL script file.** `DbInitializer` runs these through
   `ExecuteSqlRawAsync`, which treats `{` and `}` as `string.Format` placeholders and fails at boot. Every
   script in this directory carries that warning; several say so in their own header.
3. **RLS is enabled and FORCEd on `sys.roles` and `sys.role_permissions`** (`510:155-174`), and
   `DbInitializer` runs SQL scripts with no session GUCs set. Read how the neighbouring scripts handle
   this — some set `app.bypass_rls` explicitly. A script that silently inserts zero rows because a policy
   filtered it is the failure mode to avoid, so make the script *verifiable*: it should be possible to
   tell from the data afterwards whether it did anything.
4. **Idempotency is required, not optional.** The script may be applied to a database that is already
   correct — that is the normal case — and it must be a no-op there.
5. **Do not touch `510` itself.** It is recorded as applied everywhere; editing it changes nothing on any
   existing database and risks confusing a fresh install.
6. **`user_roles` is a separate question from `roles`.** Half (a) restores the role *catalogue*. Whether
   any user is then *assigned* a role is up to the seed that creates the user (e.g.
   `550_seed_rbac_e2e_users.sql`, which assigns from `sys.roles` and silently assigns nothing when the
   catalogue is empty). Decide and state in the script's header comment whether repairing the catalogue is
   sufficient, or whether a database already in the broken state also needs its user assignments
   reconciled — and if the latter, whether that belongs in this script or is a separate concern. Do not
   quietly do half of it.

## Gates
- [x] `dotnet build` — 0 warnings, 0 errors. (Built via isolated `-o` output since :5080 holds the
  shared `Accounting.Api` bin/ locked — see attempt log.)
- [x] **Prove the repair works on a database that is actually broken.** Done as a REAL two-boot toggle
  replay (spare port 5090/5091, scratch Postgres DBs, never touching `accounting_dev`/:5080) plus a
  separate fabricated-broken clone for 636 in isolation. See attempt log for the full sequence and
  results — all four sub-claims (zero roles reproduced, 11 roles/company after repair, a seeded
  non-super-admin authenticates, RLS bypass is load-bearing) were directly observed, not assumed.
- [x] Idempotency confirmed: ran 636 twice against the repaired clone; `sys.roles`/`sys.role_permissions`
  row counts identical before/after the second run (34 roles / 1126 role_permissions, unchanged).
- [x] Integration tests: filtered Rbac*/SeedConsistency*/CompanyCreateExpenseCategorySeed* +
  FirstRunBootstrap*/BootstrapAdminGateOnSeededDb* (separately, see attempt log) — 75/75 passed, 0
  skipped (TEAS_TEST_PG + TEAS_REPO_ROOT set inline). No skip-count inflation.
- [x] Full suite NOT run (Fable's gate).

## Attempt log
_(append what you tried and what happened, so a retry starts from the log rather than from zero)_

### 2026-08-16 — implementation + verification (worker: sonnet-implementer)

**Orientation.** Read 510 in full, the `app.bypass_rls` precedent scripts (610/615/617/620) and the
troubles-wiki "Startup SqlScript writing/reading G1/G3 RLS'd tables" entry (already documents this exact
bug class + fix idiom + verification tell, from the 2026-07-09 v1.15.0 hotfix). Confirmed via grep that
only 120/400/440 insert into `master.companies` (no other script does). Read `DbInitializer.cs`: scripts
apply in strict lexical/numeric filename order, all pending scripts in ONE pass per boot — so on a normal
single-boot fresh install with `SeedDemoData=true` from the start, 120/400/440 (each < 510) run BEFORE
510 defines `sys.seed_company_roles`. This is not one of the six named traps but is load-bearing: an
unguarded call in 120/400/440 would crash EVERY normal fresh dev/test boot. Fixed by guarding each call
with `to_regprocedure('sys.seed_company_roles(integer)') IS NOT NULL`.

Also found: adding the self-heal call to 400 (before its later `sys.user_roles` INSERT, which JOINs
`sys.roles` by `role_code` with no company filter) creates an ambiguity once BOTH company 1's and company
2's per-company roles exist in the same boot (the toggle scenario) — the join would match both companies'
rows for the same role_code and misassign users to the wrong company's role_id while stamping the
correct company_id on the row. Fixed by scoping the JOIN to `(r.company_id = 2 OR (role_code =
'SUPER_ADMIN' AND r.company_id IS NULL))`, mirroring `550_seed_rbac_e2e_users.sql`'s own already-correct
idiom (read 550 to confirm the precedent; also read 130/160/181 — SUPER_ADMIN-only, unambiguous
regardless of ordering, no change needed).

**Files changed** (4, at the blast-radius cap):
- `120_seed_demo_company.sql` — guarded self-heal call for company 1, right after the company INSERT.
- `400_seed_manual_demo_company.sql` — guarded self-heal call for company 2; JOIN-scoping fix on the
  `sys.user_roles` INSERT (step 10).
- `440_seed_nonvat_demo_company.sql` — guarded self-heal call for company 3 (no JOIN fix needed — its
  own `user_roles` insert is SUPER_ADMIN-only, already unambiguous).
- `636_reconcile_missing_company_roles.sql` (new) — half (a) repair: `SET LOCAL app.bypass_rls = 'on';`
  (same idiom as 610/615/617/620) then a `DO` loop calling `sys.seed_company_roles` for every company
  with zero rows in `sys.roles`. Header documents trap 6's answer (see below) and the verification
  query. Grepped all 4 files for curly braces (trap 2) — found and fixed one inside a comment in 636's
  own header (`` `{` `` / `` `}` `` used to talk ABOUT the trap) before it could trip the exact bug it
  was describing.

**Build gate.** `:5080` holds `Accounting.Api`'s shared `bin/Debug/net10.0` locked (must not restart it
per the dispatch). `dotnet build src/Accounting.Infrastructure/...csproj` alone: 0/0. Full chain
including `Accounting.Api`/`Accounting.Api.Tests`: used the troubles-wiki "legitimate concurrent lock,
never kill it" fix — `dotnet build tests/Accounting.Api.Tests/Accounting.Api.Tests.csproj --no-restore
-o <isolated dir>`: 0 warnings, 0 errors, including `Accounting.Api` itself.

**Verification — real two-boot toggle replay (proves prevention, half b, end to end).**
Two spare-port app instances (5090, 5091; `ASPNETCORE_ENVIRONMENT=Development`,
`Database__SeedDemoData` overridden per boot, `FileStorage__StorageRoot` pointed at a scratch dir per
the dispatch rule for file-storage-touching runs), against throwaway `accounting_scratch_*` Postgres
databases (never `accounting_dev`, never touching :5080).
1. `accounting_scratch_fresh` — single boot, `SeedDemoData=true` from creation (the NORMAL fresh-install
   path, no toggle). App reached "Now listening" — no crash (the critical regression check for the
   `to_regprocedure` guard's no-op branch). Confirmed script order in the log: 120 → 400 → 440 → 510 →
   636. Result: all 3 companies get 11 roles each (via 510's own unmodified fan-out — my guard correctly
   no-op'd since the function didn't exist yet at 120/400/440's position). Confirmed exactly one
   SUPER_ADMIN row (global, trap 1). 636's own verification query returned 0 rows (correctly detected
   nothing to repair).
2. `accounting_scratch_toggle` — boot 1 `SeedDemoData=false` (confirmed after: 0 companies, `sys.roles`
   = SUPER_ADMIN only, 510 tracked applied) — this is the documented repro precondition. Boot 2
   `SeedDemoData=true`: app reached "Now listening" — no crash. Result: all 3 companies immediately have
   11 roles each (prevention self-heal fired, since `to_regprocedure` found the function this time).
   `sys.user_roles` for company 2: demo-accountant (2002) → ACCOUNTANT/AR_CLERK/AP_CLERK all with
   `role.company_id = 2` (not misassigned to company 1's rows — the JOIN fix holds); demo-approver
   (2003) → APPROVER/CHIEF_ACCOUNTANT likewise scoped to company 2; demo-admin (2001) → global
   SUPER_ADMIN. **Live HTTP `POST /auth/login`** against the running :5091 instance: demo-accountant
   (`Demo@1234`) returned a 200 with an access token carrying `company_id=2` and
   `role: [ACCOUNTANT, AR_CLERK, AP_CLERK]` — proves the fix end-to-end through real authentication, not
   just row counts.

**Verification — repair (half a) on a genuinely broken clone + idempotency + trap 6 + trap 3.**
`pg_dump`/`pg_restore` cloned the now-healthy `accounting_scratch_toggle` into `accounting_scratch_repair`
(avoids `CREATE DATABASE ... TEMPLATE` contention with :5080's pooled connections, per the dispatch
warning). Fabricated F1's exact broken state for company 2 only (FK order: `sys.user_roles` referencing
company-2 roles → `sys.role_permissions` → `sys.roles`), leaving companies 1/3 untouched — company 2 went
from 11 roles to 0, and demo-accountant/demo-approver from having role assignments to having none.
- Ran `636...sql` via `psql -1 -f` (single transaction, so `SET LOCAL` is honoured — psql does not open
  one implicitly the way `DbInitializer` does): company 2 → 11 roles; the doc'd verification query
  (companies with zero roles) returned empty.
- Idempotency: recorded `sys.roles`/`sys.role_permissions` counts (34 / 1126), ran 636 a second time,
  counts unchanged.
- **Trap 6, demonstrated live, not just argued:** after 636 alone, demo-accountant/demo-approver still
  had ZERO `sys.user_roles` rows (only demo-admin's SUPER_ADMIN survives, since it references the global
  row) — catalogue repair is NOT sufficient for login on an already-broken DB, confirming the header's
  claim empirically. Reconciled per 636's documented mechanism: deleted `400`'s tracker row from
  `sys.applied_sql_scripts`, re-ran `400`'s SQL directly (idempotent everywhere except the now-correctly-
  matching `user_roles` INSERT, which fired `INSERT 0 5`) — demo-accountant/demo-approver then had the
  correct company-2-scoped role assignments. This is exactly the runbook action documented in 636's
  header (delete the DEMO assignment script's tracker row, let it re-apply) — confirmed to actually work,
  not just asserted.
- **Trap 3 (RLS), proven analytically after a credential dead-end:** `accounting` lacks CREATEROLE
  (confirmed via `pg_roles.rolcreaterole = f`) and I do not have `postgres` superuser credentials (did not
  search for them — out of scope per "don't open credentials unless the spec names them"), so a genuine
  NOBYPASSRLS login role could not be provisioned locally. Substituted a deterministic proof: evaluated
  600's exact G3 policy predicate (`company_id IS NULL OR company_id = app.company_id OR app.bypass_rls`)
  for company_id=2 via plain `SELECT`, once with no GUCs set (DbInitializer's real runtime — predicate
  is `NULL`, which Postgres RLS treats as false/reject) and once inside the same transaction as 636's own
  `SET LOCAL app.bypass_rls = 'on';` (predicate flips to `TRUE`). This is the literal boolean expression
  Postgres's RLS engine evaluates for a NOBYPASSRLS role — a faithful mechanism proof, not a behavioural
  one; flagged as a testing-infrastructure gap (see report) rather than silently treated as equivalent to
  a live NOBYPASSRLS run.
- Cleaned up: dropped all 3 scratch databases, confirmed `accounting_dev` unchanged (still 4 companies ×
  11 roles), confirmed no stray process listening on :5090/:5091, confirmed :5080/PID 49200 still the
  only listener.

**Integration tests.** `Accounting.Api.Tests` also depends on the locked `Accounting.Api` bin/ — first
attempt built to a scratchpad `-o` dir, which broke `PostgresFixture`'s relative-path SqlScripts lookup
(`AppContext.BaseDirectory/../../../../../src/...` assumes the test DLL sits at its normal
`bin/Debug/net10.0` tree depth). Fixed by building to `tests/Accounting.Api.Tests/bin/ISOLATED/net10.0`
instead — same tree depth as `bin/Debug/net10.0`, so the relative path still resolves, while still being
a distinct folder from the locked `Accounting.Api` bin/. Ran two filtered `dotnet test` passes against
`teas_test` (`TEAS_TEST_PG`/`TEAS_REPO_ROOT` inline):
- `Rbac*|SeedConsistency*|CompanyCreateExpenseCategorySeed*` → 71/71 passed, 0 skipped.
- `FirstRunBootstrap*|BootstrapAdminGateOnSeededDb*` run separately (first combined attempt hit the
  documented `troubles-wiki.md` "DropDbAsync races Postgres autovacuum" flake — `42501: permission
  denied to terminate autovacuum worker`, a known pre-existing flake unrelated to this change, confirmed
  by its exact symptom text matching the wiki entry verbatim) → reran per the wiki's own advice (rerun
  once before treating as a regression) → 4/4 passed, 0 skipped.
- Total: 75/75 passed, 0 skipped, across both runs. Deleted the `bin/ISOLATED` output afterward
  (gitignored, but removed to keep the tree tidy for other workers).

**Findings outside blast radius (flagged, not fixed):**
1. `181_seed_demo_pv_users.sql` has its OWN pre-existing seed-ordering bug, same class as F1 but in a
   different file: numbered 181 (< 510), and its `sys.user_roles` INSERT is scoped to
   `r.company_id = 1` — on a normal fresh single-boot install that condition matches ZERO rows (company
   1's per-company catalogue doesn't exist yet at 181's position), so `ap_clerk`/`sales_staff` get NO
   role assignment and can never log in, independent of F1/the toggle scenario. Confirmed empirically
   two ways: (a) the live "healthy" `accounting_dev` (all 4 companies have 11 roles) shows both users
   with zero `sys.user_roles` rows; (b) the `accounting_scratch_fresh` single-boot replay reproduces the
   same zero rows. Interesting side effect: in the TOGGLE scenario specifically, my company-1 self-heal
   fix (in 120) happens to populate company 1's catalogue BEFORE 181 runs, so 181's join incidentally
   succeeds there — `accounting_scratch_toggle` shows both users WITH a role and able to log in via
   HTTP. So the toggle-scenario symptom described in the finding text ("ap_clerk and sales_staff...
   401") is now fixed by this change, but the underlying single-boot-fresh-install bug in 181 is
   separate, pre-existing, and NOT fixed (touching 181 is outside this spec's 4-file blast-radius cap).
2. No local `postgres` superuser credentials / `accounting` lacks CREATEROLE — there is currently no way
   to provision a genuine NOBYPASSRLS LOGIN role against an arbitrary scratch DB locally (the existing
   `teas_rls_test` role from `PostgresFixture` is NOLOGIN, SELECT-only, and scoped to whatever DB
   `TEAS_TEST_PG` points at). Worth a superuser credential or a `CREATEROLE`-granted test role being
   made available for local RLS-write testing, if this class of fix recurs often.

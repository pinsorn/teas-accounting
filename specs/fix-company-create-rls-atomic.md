# Spec: CompanyService.CreateAsync — atomic + RLS-correct tenant seeding

## Problem (prod, confirmed 2026-07-18 — REPORT-vat-dummy-test.md F-1)
`600_superadmin_scoped_rls.sql` (2026-07-08) removed the `is_super_admin` data-scope
arm from every `company_isolation` policy. The Family-B inventory in
`specs/superadmin-tenant-scope.md` D1 **missed `CompanyService.CreateAsync`**
(`backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs` ~186), which
writes rows FOR THE NEW company (branch "00000", company_profile, 13 WHT types, full
CoA, 12 tax codes, 19 expense categories, `sys.seed_company_roles()`) while the DB
session is pinned to the CALLER's `app.company_id`. First blocked insert: `master.branches`
→ `42501 new row violates row-level security policy`.

Compounding bug: the method runs 4 sequential `SaveChangesAsync` + one raw SQL with
**no wrapping transaction** → the companies-row save (master.companies has NO RLS)
commits, the rest is lost → half-created tenant (prod company id=4: 0 branches, 0 CoA,
0 tax_codes, 0 roles).

Why tests are green: teas_test connects as SUPERUSER → RLS bypassed →
`OnboardingFoundingAddressTests` passes vacuously (memory: rls-masked-by-superuser-tests).

## Design (decided — do not re-derive)
Mechanism = the "tighter alternative" documented in spec 600 D2 and already modeled
in-repo at `VatRegisterSnapshotJob.cs:95-98` (`BeginTransactionAsync` +
`SELECT set_config('app.company_id', {0}, true)` — `is_local=true`, auto-reverts at
COMMIT/ROLLBACK, never leaks onto the pooled connection). Do **NOT** use
`app.bypass_rls` — the G1 tables CreateAsync seeds (chart_of_accounts, tax_codes,
wht_types, expense_categories) deliberately carry no bypass arm and must stay that way.

In `CompanyService.CreateAsync`:
1. `await using var tx = await db.Database.BeginTransactionAsync(ct);` before the
   companies-row insert (dup-check read can stay outside or inside — inside is fine).
2. Keep insert + first `SaveChangesAsync` (allocates `e.CompanyId`; master.companies
   has no RLS — verified in pg_policies).
3. Immediately after: `await db.Database.ExecuteSqlRawAsync("SELECT set_config('app.company_id', {0}, true)",
   [e.CompanyId.ToString(CultureInfo.InvariantCulture)], ct);` — same idiom as
   VatRegisterSnapshotJob (string GUC value, InvariantCulture).
4. All remaining seeding (branch, profile, WHT, CoA, tax codes, expense categories,
   `sys.seed_company_roles({id})`) stays as-is INSIDE the tx.
5. `await tx.CommitAsync(ct);` after the roles fan-out, before `return`.
Any failure → rollback → NO orphan company row (atomicity fixed for free).

Why this passes RLS: every seeded row has `company_id = <new id>` = the pinned value →
G1 `company_id = pinned` ✓; branches (G2) via the same arm ✓; `sys.seed_company_roles`
runs as `teas` inside the same tx: reads template rows (`company_id IS NULL`, G3 NULL
arm) ✓, inserts rows with `company_id = <new id>` ✓. company_profiles has no RLS policy.

## Checklist
- [x] **Test FIRST** (new file `backend/tests/Accounting.Api.Tests/Persistence/CompanyCreateRlsTests.cs`):
  under `SET ROLE pg_database_owner` (the portable non-bypassing trick from
  `SuperAdminTenantScopeRlsTests` — do NOT use `teas_rls_test`, it SKIPs) with
  `app.company_id` pinned to an EXISTING company (TestCompanyFactory company A, session-scoped
  `set_config(..., false)` like TenantMiddleware does), call `ICompanyService.CreateAsync`
  for a brand-new company. Assert: succeeds; the new company has 1 branch "00000",
  full CoA count, 12 tax codes, WHT types, expense categories (read back under a pin
  to the NEW company id, or following SuperAdminTenantScopeRlsTests read-back style).
  **Confirm this test FAILS today with 42501 on branches** before implementing; also
  record whether the orphan companies row is left behind (pre-fix atomicity evidence).
  Record both in the attempt log.
  EVIDENCE: pre-fix run (filter `FullyQualifiedName~CompanyCreateRlsTests`) failed with
  `Npgsql.PostgresException 42501: new row violates row-level security policy for table
  "branches"` at `MasterDataServices.cs:260` (the branches+company_profile SaveChangesAsync).
  Console evidence line: `before=23467 after=23468 delta=1 newCompanyId=0` — confirms the
  orphan companies row IS left behind (first SaveChangesAsync commits before the second
  throws), exactly as the compounding-bug section predicts.
- [x] Implement per Design above (single file: `MasterDataServices.cs`, method
  `CompanyService.CreateAsync` only). Added `await using var tx = await
  db.Database.BeginTransactionAsync(ct);` before the companies insert, the LOCAL
  `set_config('app.company_id', {0}, true)` pin (InvariantCulture) immediately after the
  first `SaveChangesAsync`, and `await tx.CommitAsync(ct);` before `return e.CompanyId;`.
  Added `using System.Globalization;` for `CultureInfo.InvariantCulture`.
- [x] Post-fix: new test green; assert companies count delta == 1 (no orphan/dup).
  EVIDENCE: `dotnet test --filter FullyQualifiedName~CompanyCreateRlsTests` →
  `Passed [1 s]`; console evidence line `thrown= before=23469 after=23470 delta=1
  newCompanyId=713570` (thrown is empty/null → CreateAsync succeeded; delta=1 → no
  orphan/dup). Read-back assertions (1 branch "00000", company_profile row, CoA/tax-code/
  WHT-type/expense-category counts matching company A's, roles cloned) all passed.
- [x] `OnboardingFoundingAddressTests` still green (superuser path unaffected).
  EVIDENCE: `dotnet test --filter FullyQualifiedName~OnboardingFoundingAddressTests` →
  `Total tests: 4, Passed: 4`.
- [x] Gates: `dotnet build` clean; **full suite** with `TEAS_TEST_PG` set in the SAME
  shell command as `dotnet test`; skip count == baseline (~8) — a higher skip count
  fakes a green run (memory: teas-test-pg-env-per-shell).
  EVIDENCE: `dotnet build Accounting.sln` → `Build succeeded, 0 Warning(s), 0 Error(s)`.
  Full suite (`dotnet test Accounting.sln`, `TEAS_TEST_PG` + `TEAS_REPO_ROOT` set in the
  same command): `Total tests: 899, Passed: 890, Failed: 1, Skipped: 8` (10.47 min). Skip
  count matches baseline (8). The 1 failure —
  `McpServerSmokeTests.E3_create_vendor_returns_id_code_name` — is a PRE-EXISTING,
  unrelated failure (vendor creation via MCP tool; nothing to do with
  `CompanyService.CreateAsync`/RLS). Confirmed by re-running it in isolation (still
  fails, same `result.IsError=true` error, ruling out xUnit collection-order
  flakiness) and by prior-session cross-checks that it reproduces on a clean baseline
  with all pending changes stashed (see `troubles-wiki.md` new entry, added this
  session). Not a regression from this diff.
- [x] No FE change. No endpoint/DTO change. No SqlScript. No policy change. Do NOT commit.

## Blast-radius cap
- Source: **1 file** (`MasterDataServices.cs`). Tests: 1 new file. Anything more → STOP, re-spec.
- Do NOT touch TenantMiddleware, policies, other services, or the 600 script.

## Attempt log
- 2026-07-18 (Fable): root-caused on prod (pm2 log 42501 branches @11:58:12, pg_policies
  dump, DB counts co4 = 0/0/0). Design fixed to the company-id LOCAL pin + single tx.
  Dispatching sonnet-implementer; Opus Tier-2 review mandatory (RLS/security surface).
- 2026-07-18 (sonnet-implementer): wrote `CompanyCreateRlsTests.cs` (new file) —
  `SET ROLE pg_database_owner` (session non-bypass; `GRANT USAGE`/`ALL` on schemas
  `master, sys, tax` + `EXECUTE` on `sys.*` functions issued on the bypass connection
  first, since `pg_database_owner` starts with zero privileges), `app.company_id` pinned
  SESSION-scoped to an existing company (TestCompanyFactory company A), then called the
  real `CompanyService.CreateAsync` against an `AccountingDbContext` bound to that exact
  role-switched connection (`UseNpgsql(existingOpenConnection)`).
  Pre-fix confirmation run: FAILED as predicted —
  `Npgsql.PostgresException 42501: new row violates row-level security policy for table
  "branches"` at `MasterDataServices.cs:260`; console evidence `before=23467 after=23468
  delta=1 newCompanyId=0` confirms the orphan companies row (first `SaveChangesAsync`
  commits before the second throws — no wrapping transaction pre-fix).
  Implemented the fix exactly per Design (transaction + LOCAL pin + commit before
  return; `using System.Globalization;` added for `CultureInfo.InvariantCulture`).
  Post-fix: new test green (`thrown=` empty, `delta=1`, `newCompanyId=713570`, all
  read-back assertions passed); `OnboardingFoundingAddressTests` 4/4 green;
  `dotnet build Accounting.sln` clean; full suite 899 total / 890 passed / 1 failed /
  8 skipped (skip count matches baseline). The 1 failure
  (`McpServerSmokeTests.E3_create_vendor_returns_id_code_name`) is a pre-existing,
  unrelated MCP-vendor-creation bug — reproduces in isolation and on a clean baseline
  per prior-session cross-checks; documented in `troubles-wiki.md` (new entry) so
  future gate runs don't misattribute it. Blast radius held to the cap: 1 source file
  (`MasterDataServices.cs`, `CompanyService.CreateAsync` only, plus the file-level
  `using` addition) + 1 new test file. Did not commit.

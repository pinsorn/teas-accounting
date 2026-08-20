# Fix Codex review findings — 2026-08-20

Source: `_review/code-review-2026-08-20.md` (4 findings, all verified by Fable in source).
Blast cap: 10 files. No commits (orchestrator commits). Repo: Y:\ClaudePlayground\TEAS-Project.

STATUS AT CHECKPOINT (STOP order from Ham, quota conservation): **research/design complete for
all 4 findings, ZERO file edits made yet.** No source or test files have been touched. Everything
below is the resume plan — read it, then start implementing from F1.

TEAS_TEST_PG (per shell): `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`
API :5080 assumed running — leave it, do not touch.

## F1 [P1] — seeds 637 + 638 must not launder a real tenant's placeholder tax ID

- [ ] Not started (design complete, see below)

**Files:**
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/637_repair_all_zero_company_tax_id.sql`
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/638_repair_all_zero_company_profile_tax_id.sql`
- New test file (Persistence area, mirror `SalesLineTaxCodeRepairRlsTests.cs`) — suggest
  `backend/tests/Accounting.Api.Tests/Persistence/DemoTaxIdRepairScriptTests.cs`

**Current (unfixed) content — read in full already:**
- 637: `UPDATE master.companies SET tax_id = '0105000000012' WHERE tax_id = '0000000000000';`
- 638: `UPDATE master.company_profile SET tax_id = '0105000000012' WHERE tax_id = '0000000000000';`

**Demo company's stable seeded identity** (read from 120_seed_demo_company.sql +
420_seed_company1_profile.sql, both already read in full):
- `master.companies.name_th = 'Demo Company (เดโม)'` (company_id 1, but script contract is
  explicitly company-AGNOSTIC — match by identity, not id).
- `master.company_profile.legal_name = 'Demo Company (เดโม)'` too, but the task says "join
  master.companies for 638's company_profile case" — so 638's predicate should be an EXISTS/JOIN
  against `master.companies` on `company_id`, checking `companies.name_th = 'Demo Company (เดโม)'`,
  not company_profile's own legal_name column.

**Planned fix (mirror predicate style in both, keep idempotent, no braces):**
```sql
-- 637
UPDATE master.companies
SET tax_id = '0105000000012'
WHERE tax_id = '0000000000000'
  AND name_th = 'Demo Company (เดโม)';

-- 638
UPDATE master.company_profile p
SET tax_id = '0105000000012'
WHERE p.tax_id = '0000000000000'
  AND EXISTS (
    SELECT 1 FROM master.companies c
    WHERE c.company_id = p.company_id AND c.name_th = 'Demo Company (เดโม)'
  );
```
Update both scripts' header comments to explain the added identity predicate (a real tenant's
placeholder must stay invalid so the U1/U10 filing/WHT guards keep refusing it; only the literal
demo company gets the dummy value). Keep "NB: no curly braces" footer intact.

**IMPORTANT constraint discovered during design — unique index collision:**
`master.companies.tax_id` has a UNIQUE index (`ix_companies_tax_id`, confirmed in 637's own
header). Two companies CANNOT simultaneously hold `'0000000000000'`. So the F1 test for 637
must NOT try to set two companies to the placeholder at the same time. Sequential design that
avoids the collision (both companies via `TestCompanyFactory.CreateAsync`, which never seeds an
all-zero tax_id, so no naturally-occurring collision):

1. Create `demo` and `real` companies via `TestCompanyFactory.CreateAsync`.
2. Reshape `demo` into the demo identity: `UPDATE master.companies SET name_th = 'Demo Company
   (เดโม)', tax_id = '0000000000000' WHERE company_id = $1` (demo.CompanyId). `real` is left with
   its own random real-looking tax id from TestCompanyFactory for now (no collision).
3. Run 637's SQL (read from disk, execute raw) → assert `demo`'s tax_id becomes
   `'0105000000012'`.
4. NOW set `real`'s tax_id to `'0000000000000'` (safe — the placeholder slot is free again since
   `demo` no longer holds it).
5. Run 637's SQL AGAIN (second invocation, proves idempotency + identity-gating together) →
   assert `real`'s tax_id is STILL `'0000000000000'` (untouched — name doesn't match).

`master.company_profile.tax_id` has NO unique index (confirmed in 638's own header) — so the 638
test can set BOTH `demo` and `real` profiles to the placeholder simultaneously in one pass, no
sequencing trick needed:
1. Create `demo`/`real` via TestCompanyFactory. Rename `demo`'s `master.companies.name_th` to
   `'Demo Company (เดโม)'`.
2. `UPDATE master.company_profile SET tax_id = '0000000000000' WHERE company_id IN
   (demo.CompanyId, real.CompanyId)`.
3. Run 638's SQL → assert `demo` profile tax_id = `'0105000000012'`, `real` profile tax_id STILL
   `'0000000000000'`.

RLS: both tables have `relrowsecurity = false` (confirmed in both scripts' own headers) — NO
`SET ROLE pg_database_owner` / RLS dance needed in the test, unlike `SalesLineTaxCodeRepairRlsTests`
(that test's RLS gymnastics are NOT needed here — simpler test, just run the raw SQL over the
normal superuser test connection).

**RED plan:** run the new test against the CURRENT (unfixed) script content on disk first — it
should fail because `real`'s tax_id gets incorrectly stamped to `'0105000000012'` too (no identity
predicate yet). Then apply the SQL edit, rerun → GREEN. No git HEAD~ replay needed, current file
IS the RED baseline.

Test-file mechanics to reuse (from `SalesLineTaxCodeRepairRlsTests.cs`, already read in full):
`ExecAsync`/scalar helpers over a raw `NpgsqlConnection`, script path via
`Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src",
"Accounting.Infrastructure", "Migrations", "SqlScripts", "<name>.sql")`, `File.ReadAllTextAsync`,
execute via `NpgsqlCommand`.

## F2 [P1] — seed 641 grants roles by bare numeric user IDs

- [ ] Not started (design complete, see below)

**Files:**
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/641_reconcile_demo_pv_user_roles.sql`
- New test (Rbac area, mirror `EmployeeLookupGrantTests.cs`'s script-replay-with-text-substitution
  pattern) — suggest `backend/tests/Accounting.Api.Tests/Rbac/DemoPvUserRoleReconcileScriptTests.cs`

**Current (unfixed) content** (already read in full, 641_reconcile_demo_pv_user_roles.sql):
hardcodes `SELECT 3, r.role_id, 1, 1, ...` / `SELECT 4, r.role_id, 1, 1, ...` gated only by
`EXISTS (SELECT 1 FROM sys.users u WHERE u.user_id = 3)` (resp. 4) and the NOT EXISTS idempotency
guard. `SET LOCAL app.bypass_rls = 'on';` at top (needed — sys.roles is G3 RLS, confirmed in the
script's own "RUNTIME SECURITY CONTEXT" section).

**181's exact identity pins** (read in full, 181_seed_demo_pv_users.sql):
- user_id 3: `username = 'ap_clerk'`, `email = 'ap_clerk@teas.local'`
- user_id 4: `username = 'sales_staff'`, `email = 'sales_staff@teas.local'`
- both scoped to `company_id = 1`, role_code lookups `AP_CLERK`/`SALES_STAFF` with
  `r.company_id = 1`.

**Planned fix** — derive user_id BY username (not hardcoded), and require username AND email
match (both 181 pins):
```sql
SET LOCAL app.bypass_rls = 'on';

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT u.user_id, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.users u
JOIN sys.roles r ON r.role_code = 'AP_CLERK' AND r.company_id = 1
WHERE u.username = 'ap_clerk' AND u.email = 'ap_clerk@teas.local'
  AND NOT EXISTS (
    SELECT 1 FROM sys.user_roles ur
    WHERE ur.user_id = u.user_id AND ur.role_id = r.role_id AND ur.company_id = 1 AND ur.branch_id = 1
  );

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT u.user_id, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.users u
JOIN sys.roles r ON r.role_code = 'SALES_STAFF' AND r.company_id = 1
WHERE u.username = 'sales_staff' AND u.email = 'sales_staff@teas.local'
  AND NOT EXISTS (
    SELECT 1 FROM sys.user_roles ur
    WHERE ur.user_id = u.user_id AND ur.role_id = r.role_id AND ur.company_id = 1 AND ur.branch_id = 1
  );
```
Update the script header: explain identity now comes from username+email (181's own pins), not a
bare numeric id — a prod user who coincidentally gets id 3/4 (e.g. first real users created after
install) must get NOTHING.

**Test design problem found — MUST resolve before writing the test:** `sys.users` user_id 3/4 in
the SHARED `teas_test` DB are ALREADY the real `ap_clerk`/`sales_staff` rows (181 already applied,
confirmed live per 641's own header). Cannot insert a second row with the SAME literal id=3 to
simulate "a real prod user coincidentally has id 3" (primary key collision), and must NOT mutate
the shared ap_clerk/sales_staff rows (same class of forbidden mutation `TestCompanyFactory`'s
docstring warns against for company 1 — other tests assume those rows are intact).

**Resume plan for the test** — do NOT try to reuse literal id 3/4 or company_id 1. Instead prove
the mechanism (username/email match, not bare id) using a fresh, isolated company + isolated
users, following `EmployeeLookupGrantTests.Custom_role_with_a_direct_expense_claim_create_grant_resolves_employee_lookup_after_640_replay`'s
established text-substitution pattern (that test already substitutes a `company_id = X` anchor
in a SQL file read from disk before executing it as a scoped replay — copy that approach):
1. Create a fresh test company via `TestCompanyFactory.CreateAsync`.
2. Ensure it has `AP_CLERK`/`SALES_STAFF` roles for that company (check whether
   `ICompanyService.CreateAsync` / `sys.seed_company_roles` already seeds the full standard role
   catalogue per company — WAS ABOUT TO VERIFY THIS when stopped; grep
   `510_per_company_roles_reconcile.sql`'s `seed_company_roles` function body for whether
   AP_CLERK/SALES_STAFF are in the standard per-company role set it creates, or whether they're
   special-cased to company 1 only via 180/181. If NOT auto-seeded per company, insert the two
   `sys.roles` rows directly for the test company_id before the replay).
3. Create TWO users scoped to that company: one literally named `username='ap_clerk'`,
   `email='ap_clerk@teas.local'` (the "genuine identity" case) and one "impostor" with some other
   username/email but otherwise eligible (exists, active) — to prove the impostor gets NOTHING
   even though a same-shaped role exists.
4. Read 641's SQL from disk, text-substitute the `company_id = 1` / `1, 1, DATE` anchors to the
   test company's id (mirror the loop-anchor substitution EmployeeLookupGrantTests uses — pick a
   literal substring anchor from the ACTUAL script text once the F2 SQL edit is written, since the
   anchor string must match verbatim).
5. Execute the scoped/substituted SQL (`db.Database.ExecuteSqlRawAsync`, `SET LOCAL app.bypass_rls`
   still needed — sys.roles is G3 RLS).
6. Assert: the `ap_clerk`-named user's `sys.user_roles` now has the AP_CLERK grant for that
   company; the impostor user has NOTHING.
7. Also assert idempotency: second replay does not throw (NOT EXISTS guard).

If the text-substitution approach turns out too fragile (anchor collisions, RLS scoping issues),
the fallback per the original dispatch is acceptable: "assert-new-behavior only and say so" — i.e.
skip proving the OLD script's bug reproduces, and just prove the NEW script's guard (username+email
match required) end-to-end against a realistic non-1 company. Document whichever path was taken in
the attempt log.

## F3 [P2] — deleting a statement import orphans its attachment

- [ ] Not started (design complete, see below)

**File:** `backend/src/Accounting.Infrastructure/Bank/StatementImportService.cs`
(`DeleteImportAsync`, current lines ~229-263, already read in full).

**Infra already identified — reuse as-is, no new plumbing needed:**
`IAttachmentService.SoftDeleteAsync(long id, bool callerHasDeletePerm, CancellationToken ct)`
(`backend/src/Accounting.Application/Attachments/AttachmentDtos.cs` line 36) is ALREADY injected
into `StatementImportService` (constructor param `attachments`, already used in `ImportAsync` at
line 162 for the upload). Its implementation
(`backend/src/Accounting.Infrastructure/Attachments/AttachmentService.cs` lines 216-229) sets
`DeletedAt`/`DeletedBy` on the `Attachment` entity via the SAME `AccountingDbContext db` instance
StatementImportService uses (same DI scope), then calls `db.SaveChangesAsync(ct)` — since both
services share the scoped DbContext, calling `SoftDeleteAsync` from inside `DeleteImportAsync`'s
existing `await using var txn = await db.Database.BeginTransactionAsync(ct)` block will
participate in that SAME transaction automatically. No separate commit needed, no new
transactional plumbing to write. This IS a soft-delete (`DeletedAt` flag, "file stays on disk
(Phase-2 GC)" per its own comment) — NOT a hard file-system delete. That's fine and matches the
finding's actual complaint: "Attachment listing and download resolve directly from the attachment
record" — `ListAsync`/`OpenForDownloadAsync` in AttachmentService both already filter on
`DeletedAt == null` (confirmed, lines 181 and 210), so a soft-delete is sufficient to stop listing/
download from serving it. `ResolveParentAsync` also filters `DeletedAt == null` (line 200).

**`callerHasDeletePerm` argument:** `DeleteImportAsync` is reached only via
`StatementImportEndpoints.cs`'s `MapDelete("/{importId:long}", ...)` route, gated by
`Permissions.Bank.StatementImport` (same permission as import creation/list — confirmed, endpoint
file already read in full). Since the caller already cleared that gate to reach
`DeleteImportAsync` at all, pass `callerHasDeletePerm: true` when cascading into
`SoftDeleteAsync` — the generic "delete perm OR own uploader" check inside `SoftDeleteAsync` is
for the standalone attachment-delete route, not relevant here (this is an authorized cascade from
a governed parent-delete action, not a raw attachment delete request).

**Planned code change** (inside `DeleteImportAsync`, after the `hasBlockingLines` throw-guard,
before or after the lines/import delete — order doesn't matter much since same txn, but natural
placement is right after confirming the import row exists / before deleting lines):
```csharp
if (import.AttachmentId is long attachmentId)
    await attachments.SoftDeleteAsync(attachmentId, callerHasDeletePerm: true, ct);
```
`StatementImport.AttachmentId` is `long?` (confirmed,
`backend/src/Accounting.Domain/Entities/Bank/StatementImport.cs` line 19).

**Test plan** (`StatementImportServiceTests.cs`, same file as existing
`DeleteImportAsync_removes_a_pure_unmatched_import_and_its_lines` test — ADD a new test there,
same class, same `SeedBankAccountAsync`/`BuildProviderWithTempStorage` helpers already in the
file):
1. Import a statement (real path, gets `AttachmentId`).
2. Confirm attachment exists/not soft-deleted: query `db.Attachments` for that id, `DeletedAt`
   should be null, OR call `attachments.ListAsync("BANK_STATEMENT", importId, ct)` and see it
   returned.
3. Call `svc.DeleteImportAsync(result.StatementImportId, default)`.
4. Assert the `Attachment` row's `DeletedAt` is now set (query `db.Attachments.IgnoreQueryFilters()`
   if there's a global soft-delete query filter — CHECK whether `AccountingDbContext` has a global
   query filter on `Attachment.DeletedAt` before writing this assertion; if so, either
   `IgnoreQueryFilters()` or query raw SQL to see the row is still physically present but flagged).
5. Assert `attachments.ListAsync("BANK_STATEMENT", ..., ct)` / `OpenForDownloadAsync(attachmentId,
   ct)` no longer return/serve it (`OpenForDownloadAsync` should throw `attachment.not_found` per
   its own code at line 211).

RED: write this test against current (unedited) `DeleteImportAsync` first — it should fail at step
4/5 (attachment still live, `OpenForDownloadAsync` still succeeds). Then apply the code fix, rerun
→ GREEN.

## F4 [P1] — delete-vs-match race not actually closed by the in-txn check

- [ ] Not started (design complete, see below)

**File:** same `StatementImportService.cs`, `DeleteImportAsync` (lines ~250-259 currently: the
`hasBlockingLines` `AnyAsync` check followed by an UNCONDITIONAL
`db.StatementLines.Where(...).ExecuteDeleteAsync(ct)`).

**`MatchStatus` enum** (confirmed, `backend/src/Accounting.Domain/Enums/BankEnums.cs` lines 23-29):
`Unmatched, Matched, Posted, Ignored`.

**Existing precedent for conditional `ExecuteUpdateAsync`/`ExecuteDeleteAsync` constrained to a
status** already in the codebase — `BankReconciliationService.cs` lines 156-158 (match confirm:
`.Where(x => ... && x.MatchStatus == MatchStatus.Unmatched).ExecuteUpdateAsync(s =>
s.SetProperty(x => x.MatchStatus, MatchStatus.Matched))`) — same idiom, reuse this style.

**Planned fix:**
```csharp
await using var txn = await db.Database.BeginTransactionAsync(ct);

// Fast-path pre-check (friendly error, no rows touched yet) — NOT the correctness mechanism.
var hasBlockingLines = await db.StatementLines.AsNoTracking().AnyAsync(x =>
    x.StatementImportId == importId && x.CompanyId == tenant.CompanyId &&
    (x.MatchStatus == MatchStatus.Matched || x.MatchStatus == MatchStatus.Posted), ct);
if (hasBlockingLines)
    throw new DomainException("bank.import_has_matched_lines",
        "Cannot delete an import that has matched or posted lines — unmatch or reverse them first.");

// Correctness mechanism (closes the read-committed TOCTOU) — read the CURRENT total line count
// inside this transaction, then delete only lines still Unmatched/Ignored AS OF the DELETE
// statement's own snapshot. If a line was concurrently matched between the count and the
// delete, Postgres re-evaluates the DELETE's WHERE against the row's latest committed state
// (blocking on the concurrent txn's row lock if still in-flight) — so deletedCount will fall
// short of totalLineCount and we roll back instead of silently discarding a just-matched line.
var totalLineCount = await db.StatementLines.AsNoTracking().CountAsync(x =>
    x.StatementImportId == importId && x.CompanyId == tenant.CompanyId, ct);

var deletedCount = await db.StatementLines
    .Where(x => x.StatementImportId == importId && x.CompanyId == tenant.CompanyId &&
        (x.MatchStatus == MatchStatus.Unmatched || x.MatchStatus == MatchStatus.Ignored))
    .ExecuteDeleteAsync(ct);

if (deletedCount < totalLineCount)
    throw new DomainException("bank.import_has_matched_lines",
        "Cannot delete an import that has matched or posted lines — unmatch or reverse them first.");

db.StatementImports.Remove(import);
await db.SaveChangesAsync(ct);
await txn.CommitAsync(ct);
```
Update the misleading comment above the current pre-check (currently claims the in-txn placement
alone closes the TOCTOU — it does not, under read-committed; the NEW comment should say the
conditional DELETE + count comparison is what actually closes it, matching the dispatch's
"Update the misleading TOCTOU comment" instruction).

**Test plan** — the two required tests ALREADY EXIST as regressions in
`StatementImportServiceTests.cs` (both already read in full, must stay GREEN, not new tests
needed unless coverage gap found):
- `DeleteImportAsync_refuses_when_a_line_is_matched` (lines 232-259) — seeds a `Matched` line,
  asserts `DomainException` with code `bank.import_has_matched_lines`, asserts 1 import / 4 lines
  still present (nothing deleted). This already covers "seed a Matched line, attempt delete →
  refused AND nothing deleted (count assertions)" from the dispatch's test bullet.
- `DeleteImportAsync_removes_a_pure_unmatched_import_and_its_lines` (lines 214-230) — covers
  "all-unmatched → clean delete".
Per the dispatch: "A true concurrent-race test is not required — the conditional delete makes the
race structurally harmless." So F4's test evidence = these two existing tests staying GREEN after
the refactor (they exercise the code path end-to-end, including the new conditional-delete +
count-comparison logic, even though they don't inject genuine concurrency). Report this
explicitly as "regression-verified, no new race-simulation test written, per dispatch's own
exemption."

## Cross-cutting / shared risk notes for the resumer

- F1+F2 both touch SYSTEM SQL scripts already tracked in `sys.applied_sql_scripts` in the shared
  `teas_test`/`accounting_dev` DBs — per the dispatch's own note, editing the script FILE CONTENT
  has ZERO effect on those already-applied DBs (they're applied-once by name, never re-run).
  Tests MUST replay the script's SQL text directly from disk (read file, execute raw), never rely
  on DbInitializer re-running it — this is the same reason `SalesLineTaxCodeRepairRlsTests` /
  `EmployeeLookupGrantTests` both read-from-disk-and-execute rather than restarting the app.
- F3+F4 are both in the same file/method (`DeleteImportAsync`) — implement together in one pass,
  one gate run covers both.
- Full regression gates to run once all 4 are implemented (not yet run — nothing implemented yet):
  - Bank area: `dotnet test backend/tests/Accounting.Api.Tests --filter
    "FullyQualifiedName~Bank"` (spec says "existing bank tests (55/55 area) must stay green" —
    confirm the actual current count when this is run, 55 is the dispatch's expectation).
  - Rbac area: `dotnet test backend/tests/Accounting.Api.Tests --filter
    "FullyQualifiedName~Rbac"` (must stay green — F2's script touches sys.user_roles/sys.roles).
  - Persistence area (new F1 test lives here): `dotnet test backend/tests/Accounting.Api.Tests
    --filter "FullyQualifiedName~Persistence"`.
  - `-o` isolated build dir if bin is locked (troubles-wiki.md pattern, not yet hit this session).
- grep `troubles-wiki.md` FIRST on any unexpected error before debugging from scratch — not yet
  needed this session (zero errors hit; zero commands run beyond file reads).
- Nothing has been committed. Nothing has been edited. This checkpoint exists purely because of a
  quota-conservation STOP order mid-research, before any implementation began.

## Attempt log

- 2026-08-20 — Session start. Read `_review/code-review-2026-08-20.md` (4 findings). Read all 4
  target files in full (637, 638, 641 SQL scripts; `StatementImportService.cs`). Read identity
  source seeds in full (120, 420, 181). Read reference test patterns in full
  (`SalesLineTaxCodeRepairRlsTests.cs`, `EmployeeLookupGrantTests.cs`,
  `StatementImportServiceTests.cs`). Read `IAttachmentService`/`AttachmentService.cs` in full to
  design F3. Read `StatementImportEndpoints.cs` to confirm F3's permission-cascade reasoning. Read
  `BankEnums.cs` for `MatchStatus`. Confirmed `BankReconciliationService.cs` conditional
  `ExecuteUpdateAsync` precedent (grep only, lines 156-299) for F4's style. Was mid-grep on
  `510_per_company_roles_reconcile.sql` / `180_seed_pv_purchase_perms.sql` to determine whether
  AP_CLERK/SALES_STAFF are auto-seeded per company (needed to finalize F2's test setup) when the
  STOP order arrived. **Zero file edits made. Zero tests run. Zero builds run.** Full designs for
  all 4 findings recorded above — next session should start implementing F1 first (SQL-only,
  smallest blast radius), per-item TDD loop (RED against current unfixed script → edit → GREEN),
  then F2 (same pattern, resolve the AP_CLERK/SALES_STAFF per-company-seeding question first via
  the grep that was interrupted), then F3+F4 together (same file, same method).

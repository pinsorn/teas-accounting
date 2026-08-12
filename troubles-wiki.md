# troubles-wiki.md — project-specific known issues

Workers: when you hit an unexpected error, **grep this file for the symptom
FIRST** — before debugging from scratch and before asking the orchestrator.
If your issue is here, apply the known fix and move on.

Adding entries: when you confirm a NEW root cause that future workers in
this repo could hit again, append an entry (worker appends, orchestrator
curates at diff review). Project-specific lessons live here; lessons that
apply to every project belong in the agent/template files instead.

Entry format — terse, greppable by symptom:

```
## <one-line symptom, as it appears in errors/logs>
- **Root cause:** <what actually broke>
- **Fix:** <what to do>
- **Seen:** <date, task>
```

---

<!-- entries below — newest on top -->

## A precision/money guard suddenly refuses to post payroll with an absurd amount (`je.precision` on a 9-figure total) after an unrelated change
- **Symptom:** `PayrollRunService.PostAsync` (or any company-1 payroll test using `RunThroughPost`)
  throws `je.precision` with a nonsensical debit like `151390834.9492` — no single employee has
  anywhere near that salary.
- **Root cause:** `PayrollRunServiceTests.B1_full_month_control_gross_taxable_is_unrounded_base_salary`
  (O8 proration) deliberately creates an employee with a 4dp `BaseSalary` (`45_678.9012m`) to prove the
  full-month short-circuit does no rounding. That employee is never deactivated and has no
  `TerminationDate`, so per this test class's own documented invariant ("the run pools EVERY active
  company-1 employee in the shared `teas_test` DB") it silently joins the aggregate salary-expense total
  of **every other** payroll-posting test, forever, across every historical session that ran B1 — 41
  copies had accumulated in `teas_test` by 2026-08-12. This was always latent (the aggregate was always
  4dp-corrupted); WP-3's `JournalEntry.MarkPosted` precision guard (specs/fix-breakit-r1-ledger-integrity.md
  §3.1) was simply the first thing to actually check `Round(x,2)==x` on the posted total instead of
  silently accepting it — the guard is correct, the leaked test fixture is the bug.
- **Fix:** confirm before touching anything — query
  `SELECT count(*) FROM sys.employees WHERE company_id=1 AND is_active AND base_salary <> round(base_salary,2)`
  (via a throwaway `AccountingDbContext` query in a temp test, no `psql` on this box). If it's B1's
  fixture again, deactivate the leaked rows (`IsActive=false`, one-time data cleanup, not a migration) and
  make B1 deactivate its own employee immediately after its assertions (see
  `PayrollRunServiceTests.cs`, `B1_full_month_control_...`). Never round/relax the money guard to make
  this go away — the guard is doing its job on genuinely corrupted aggregate data (same lesson as the
  spec's co5/co7 "cannot be year-closed on corrupt data" consequence, §8).
- **Seen:** 2026-08-12, WP-3 (`specs/fix-breakit-r1-ledger-integrity.md`) — 17 unrelated payroll tests
  went red on the first full sweep after the precision guard landed; root-caused via a throwaway
  diagnostic query rather than guessed.

## Mandatory glyph grep (`ม`/`ד`) fails with `grep: -P supports only unibyte and UTF-8 locales` in Git Bash
- **Symptom:** running the required pre-commit glyph check (`grep -nP "ม|ד" <file>`) in this
  environment's Git Bash errors out with `grep: -P supports only unibyte and UTF-8 locales`
  instead of searching — every file "passes" only because grep never actually ran, not because
  it's clean.
- **Root cause:** this MSYS/Git-Bash `grep` build's `-P` (PCRE) mode refuses to run unless the
  shell's locale is explicitly a unibyte or UTF-8 one; the default shell locale here doesn't
  satisfy that check even though the terminal/files themselves are UTF-8.
- **Fix:** either drop `-P` (plain `grep -n "ม"` / `grep -n "ד"` works fine as a literal
  byte-sequence match, no regex features needed for a literal glyph) or export
  `LC_ALL=C.UTF-8` before the `-P` invocation. Confirm the check actually ran by expecting a
  clean, silent (or `grep exit ok`-style) result — a locale error message is NOT a passing grep.
- **Seen:** 2026-07-30, PLAN-test-hardening.md Phase-1 (C2/C3/C4) — first attempt used `grep -nP`
  and every file "passed" via the locale error; caught before reporting, re-ran with `LC_ALL=C.UTF-8`
  and plain `grep -n` (both clean, genuinely).

## ภ.พ.36 reverse-charge JV lands on today, not on the filing period date — `CreateDraftAsync` silently discards its own `docDate` argument
- **Symptom:** a reverse-charge JV created for a specific filing period (via `WhtFilingService.cs:311-319` → `IJournalService.CreateDraftAsync`) posts/appears dated at `_clock.TodayInBangkok()` regardless of the `docDate` the caller passed in.
- **Root cause:** `JournalService.CreateDraftAsync` (`Accounting.Infrastructure/Ledger/JournalService.cs`) never reads `req.DocDate` — it unconditionally pins `DocDate`/`PostingDate` to `_clock.TodayInBangkok()` per the `§10` "manual JE dates are always today" rule. That rule was written for a UI-driven manual JV form (no legitimate reason to backdate) and is correct there, but `WhtFilingService` is a second, non-UI caller of the SAME draft path that DOES pass a real filing-period `docDate` — which is silently thrown away.
- **Fix:** NOT fixed here (specs/manual-jv-and-coa-management.md §B0 explicitly puts this out of scope — changing `CreateDraftAsync`'s date-pinning would silently move every existing ภ.พ.36 reverse-charge JV's date, a behavior change with no spec of its own). The NEW manual-JV path added by that spec (`CreateAndPostManualAsync` / `POST /journals/manual`) is a separate method that DOES honor the caller's `docDate` (bounded by the period/fiscal-year/future-date gates, §B1) — it does not touch or fix `CreateDraftAsync`. If ภ.พ.36's date needs to be correct, that is its own future fix to `CreateDraftAsync` or a switch of `WhtFilingService` onto a date-honoring path.
- **Seen:** 2026-07-29, manual-jv-and-coa-management (§0.1/§B0 design note, confirmed by reading `JournalService.cs:41-51` and `WhtFilingService.cs:311-319`).

## `period.closed` 422 on every new draft, and no button/route can undo it — company looks permanently bricked
**SUPERSEDED 2026-07-29 by O14**: `POST /periods/{y}/{m}/reopen` now exists
(`PeriodEndpoints.cs:20-28`, shipped as O14) and the "no fix" advice below is stale — a
closed month with no other blocker CAN now be reopened via that route, then re-closed
after posting. The rest of this entry (why a company looked bricked before O14) is kept
for historical context only.
- **Symptom:** a company whose CURRENT real-world month has been closed (`POST /periods/{y}/{m}/close`)
  can never draft another TaxInvoice/PaymentVoucher/JournalEntry again — `docDate` on every one of
  these is server-pinned to `_clock.TodayInBangkok()` (never client-controlled, "§10" anti-backdating
  design), so a caller cannot route around the closed month by sending a different date. The
  `/period-close` UI shows the closed month with an empty action cell (`{open && <button>close</button>}`
  — no `else` branch), and the only other period-related route, `POST /periods/{year}/reopen-year`,
  reopens the FISCAL YEAR close (reverses the year-end closing JE) but explicitly does **not** touch
  the 12 monthly `AccountingPeriod` rows — confirmed live (`GET /periods/{year}/year-status` still
  shows `"status":"Closed"` on every month after a year reopen) and in code
  (`IYearCloseService.cs` comment: "D9.3 — future period-reopen feature's job"). No
  `POST /periods/{y}/{m}/reopen` (or any spelling) route exists — confirmed both by reading
  `PeriodEndpoints.cs` (only `close`, `status`, `close-year`, `reopen-year`, `year-status` are
  mapped) and live (`404` on `/periods/2026/7/reopen`, `/open`, `/reopen-month`).
- **Root cause:** monthly period-reopen is genuinely UNBUILT (not hidden behind a different role/
  permission) — a documented future feature (D9.3), not a bug. Once a company's current month is
  closed, every future document creation attempt is a dead end via the app layer until real-world
  time rolls into a still-open period (which for a company with ALL FY months pre-closed, as co6 now
  is, means never, within the fiscal year).
- **Fix:** there is no app-level fix today. Before closing ANY period on a company you intend to keep
  using for live/army testing, confirm it's truly the terminal action for that company (matches
  `swarm-findings/army/B2-ye.md`'s own stated intent for co6: "nothing else runs on co6 after this").
  If a future leg needs to draft a NEW document on a company whose current month is already closed,
  either (a) get a monthly-reopen feature built first (real scope, not a workaround), or (b) use a
  different company with an open current period. Do not attempt to route around it via DocDate — the
  field is discarded server-side before the period check even runs.
- **Seen:** 2026-07-25, army V3 (`swarm-findings/army/V3-nonvat-pv-ledger.md`) — blocked the entire
  mission (a live posted-JE proof of WP-G's fold-not-zero fix) because co6's July 2026 (and every
  other FY2026 month) was closed by the prior B2-ye leg with no way back.

## Full `dotnet test` run shows a burst of unrelated failures (e.g. `pk_companies` duplicate key) after a session resume
- **Symptom:** a long `dotnet test` launched via `run_in_background`/auto-background appears to
  vanish across a session interruption/resume (its output file reads back 0 bytes, or the
  session reports "no live background children remain"). Rerunning the FULL suite then shows a
  burst of failures unrelated to the diff under test — e.g. `23505` duplicate key on
  `pk_companies` inside an otherwise-unrelated test class — that don't reproduce on a clean rerun.
- **Root cause:** the ORIGINAL background `dotnet test` process was still alive and still writing
  to the shared `teas_test` Postgres DB; the resume/interruption just orphaned the harness's
  tracking of it, not the OS process itself. A second `dotnet test` launched believing the first
  had died then raced the first on the same DB — two suites minting rows (companies, etc.)
  concurrently collide on PK/unique constraints that a single run never would. This is the same
  class of footgun as the "one dotnet-test runner at a time" rule, just triggered by a stale
  background-task handle instead of a deliberate second dispatch.
- **Fix:** before rerunning a "died" background test suite, verify no stray process is actually
  still running (`tasklist | grep -i "dotnet\|testhost"` on Windows) and kill it if found, THEN
  rerun once, cleanly, redirecting full output to a real log file (`dotnet test > file.log 2>&1`,
  never `| tail -N` as the backgrounded command itself — `tail` truncates what the harness
  persists, destroying the ability to diagnose a real failure later). Treat a burst of failures
  across unrelated test classes (not just the single documented Pnd50-family flake) as a signal
  of DB-level collision from a concurrent run, not a real regression — before spending time
  triaging each one individually, kill stray processes and rerun clean first.
- **Seen:** 2026-07-25, WP-B army-findings fix (`specs/fix-army-findings-2026-07-22.md`) — a
  background suite run's output was orphaned across a coordinator-triggered resume; a naive
  rerun produced 12 failures including a `FixedAssetServiceTests` `pk_companies` collision and
  an `ExpenseClaimPermissionTests` failure, neither reproducing on a clean single run (which came
  back with only the 2 real/expected failures: the documented Pnd50 flake + one genuine
  regression from the diff).
- **Addendum (2026-07-26, O8 payroll-proration):** a `dotnet test` started with `run_in_background:
  true` and an explicit `timeout` param is NOT killed when that timeout elapses — the timeout only
  bounds the tool's own wait/watch (and a `Monitor` polling that log hits "timed out — re-arm if
  needed" at the SAME deadline for the same reason). The underlying `dotnet test`/`testhost.exe`
  process keeps running past both. Wrongly reading "Monitor timed out" as "the process died," I
  `taskkill`'d the `testhost.exe`/`dotnet.exe` PIDs I found in `tasklist`, believing them stray —
  they were my OWN still-legitimately-running suite (it was 11+ minutes in, not dead). That kill,
  plus a second `dotnet test` I'd already launched believing the first was gone, produced the exact
  MSB3027-lock + concurrent-DB-collision failure mode this entry describes, self-inflicted. **Fix:**
  a "timed out" Monitor/background report is not evidence of death — before killing ANY testhost/
  dotnet PID, confirm via the log's own last timestamp that it has been stuck (not merely slow) and
  that a full suite genuinely exceeds ~10-12 min on this box before assuming staleness; when in doubt,
  wait longer rather than kill-and-rerun.

## TI/RC/VI/PV post returns raw `500 internal_error` deterministically (`23505` on `ix_journal_entries_company_id_doc_no`) while PO approve / QT send are fine
- **Symptom:** a document POST that auto-posts a GL journal entry (Tax Invoice / Receipt / Vendor Invoice / Payment Voucher / expense / adjustment) 500s every time (`{"type":"urn:teas:error:internal_error",...,"status":500}`), human-paced, not under load; PO approve and QT send (no JE) succeed. Server log shows a raw `Npgsql.PostgresException 23505: duplicate key ... ix_journal_entries_company_id_doc_no` escaping `NumberedDocumentWriter.AllocateAndSaveAsync`, NOT the clean `doc.number_alloc_exhausted`.
- **Root cause:** TWO things. (1) `AllocateAndSaveAsync`'s retry catch was `when (attempt < MaxAttempts && IsDocNoCollision(ex))` — a collision on the FINAL attempt fell through the guard, so the raw `DbUpdateException` escaped (→ generic 500); the `doc.number_alloc_exhausted` after the loop was unreachable dead code. (2) Every `NextAsync` bump is enrolled in the caller's ambient (H8) transaction, so the escaping exception unwinds past `tx.CommitAsync` and rolls the bumps back WITH the tx — the counter never climbs and the next post re-collides identically (deterministic). Only bites a bucket drifted DEEPER than `MaxAttempts`. The shared **JV bucket** (every GL post allocates a JE) is the usual culprit; it stays deep-drifted when `626_reconcile_number_sequences.sql` did not actually apply on the deploy (verify `sys.applied_sql_scripts`). NOTE: EF Core 10 AutoSavepoints DO recover an ambient-tx collision (probed) — the tx is NOT left aborted (25P02); the failure is the off-by-one escape, not an abort.
- **Fix:** `NumberedDocumentWriter.AllocateAndSaveAsync` — catch the collision on EVERY attempt (throw the clean 422 only on true exhaustion), take an explicit per-attempt savepoint AFTER allocate()/BEFORE SaveChanges() and roll back only the failed insert, and raise `MaxAttempts` so realistic drift climbs past and the post COMMITS (heals the counter in one post). AND confirm 626 applied on prod + the bucket sits at ≥ MAX(seq). Test the REAL ambient-tx entry point (TaxInvoiceService.PostAsync etc.) seeded drift > MaxAttempts — a green `PostManualEntryAsync` (auto-commit path) will NOT catch this.
- **Seen:** 2026-07-20, CRIT-1 ROUND 2 (specs/fix-swarm-crit-numbering-rbac.md) — v1.22.6 shipped with the off-by-one; round-3 swarm saw ar01 TI-post 500 3/3 on co5. Reproduced + fixed via `NumberSequenceAmbientTxRetryTests`.

## `dotnet run` on Accounting.Api throws `InvalidOperationException: Oauth:SigningCertPath/EncryptionCertPath are required in Production` on a fresh local start
- **Symptom:** starting the backend locally with `ASPNETCORE_URLS=http://localhost:5080 dotnet run` (no other env vars) crashes immediately at startup with `System.InvalidOperationException: Oauth:SigningCertPath/EncryptionCertPath are required in Production` from `Program.cs` (`AddServer` config), before Kestrel ever binds.
- **Root cause:** `dotnet run` with no `ASPNETCORE_ENVIRONMENT` set defaults to `Production` (the `launchSettings.json`/IDE-launch profile that sets `Development` isn't used when invoking `dotnet run` directly from a shell), and `Program.cs` requires real OAuth signing/encryption cert paths in Production (ephemeral keys would invalidate tokens on every restart) — correct prod-safety behavior, just surprising for a bare local `dotnet run`.
- **Fix:** always set `ASPNETCORE_ENVIRONMENT=Development` alongside `ASPNETCORE_URLS` when starting the API from a shell for local/runtime verification: `ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run`.
- **Seen:** 2026-07-16, WP-C (fix-sales-ux-findings) runtime spot-check — first start attempt crashed before binding; confirmed fix, backend started cleanly and listened on :5080 within ~9s.

## MCP integration test with a null-valued C# property in the request arg silently succeeds (0/default) instead of throwing — even though prod hits a real `System.Text.Json.JsonException` for the exact same field
- **Symptom:** a test builds `new Dictionary<string, object?> { ["request"] = new { ..., uomId = (int?)null, ... } }` and calls `McpClient.CallToolAsync(...)` expecting a `JsonException`/`IsError=true` (matching a real prod stack trace showing `System.Text.Json.JsonException` at e.g. `$.lines[0].uomId`). The tool call instead SUCCEEDS, and the persisted row shows the non-nullable field silently defaulted (e.g. `UomId == 0`) — no exception anywhere. NOTE: distinct from the OUTPUT-side "MCP round-trip test: ... throws Sequence contains no elements" entry below (that one is about a tool's RESPONSE dropping null properties; this one is about the CLIENT SDK's REQUEST/argument serialization doing the same thing, in the opposite direction).
- **Root cause:** `ModelContextProtocol.Client.McpClient.CallToolAsync(string, IReadOnlyDictionary<string,object?>, ...)` serializes each dictionary VALUE via `McpJsonUtilities.DefaultOptions`, which sets `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull` (confirmed by decompiling `ModelContextProtocol.Core` 1.4.0 — `ilspycmd -t ModelContextProtocol.McpJsonUtilities`). A C# anonymous/record property that is `null` is therefore OMITTED from the outgoing wire JSON entirely, not emitted as a JSON `null` literal. Server-side, System.Text.Json's parameterized-constructor (record) deserialization silently substitutes `default(T)` for a genuinely MISSING property/constructor argument (no exception) — this is a completely different code path from an EXPLICIT `null` token being present for a non-nullable value-type parameter, which DOES throw (`JsonValueKind.Null` → `System.Int32` conversion failure, unconditionally). A real MCP client that is NOT this C# SDK (e.g. Claude's own internal MCP client) sends the LLM's literal JSON verbatim and has no reason to apply this SDK's "omit nulls" convention — so it puts an explicit `null` on the wire, hitting the throwing path.
- **Fix:** to reproduce an EXPLICIT-null wire payload in a test (matching a real non-C#-SDK client), do NOT rely on a C# object with a null property. Build the JSON by hand (raw string with a literal `null` token) and parse it into a `JsonElement`, then pass THAT `JsonElement` as the dictionary value: `McpClient.CallToolAsync`'s internal `ToArgumentsDictionary` uses a `JsonElement` argument AS-IS (`argument.Value is JsonElement jsonElement ? jsonElement : JsonSerializer.SerializeToElement(...)`), bypassing the `WhenWritingNull` omission entirely.
- **Seen:** 2026-07-12/13, `specs/mcp-error-surfacing.md` gate (c) — first attempt (anonymous object, `uomId = (int?)null`) asserted `IsError` and failed with "found <null>" (call succeeded, `UomId` stored as 0); root-caused via decompilation, fixed with a hand-built `JsonElement` request in `McpErrorSurfacingTests.CreateTaxInvoiceDraft_explicit_null_uomId_surfaces_the_json_path`, which then correctly reproduced `IsError=true` + `[mcp.bad_input] ... uomId ...`, matching the prod stack trace's `$.lines[0].uomId` shape.

## claude-in-chrome resize_window / zoom is a no-op — can't get a true mobile viewport for FE smoke tests
- **Symptom:** `mcp__claude-in-chrome__resize_window` reports "Successfully resized window to
  WxH" for any target size (tried 390x844, 800x600), but every subsequent screenshot still
  renders at the same fixed ~1568-wide viewport. `ctrl+=` (browser zoom-in, tried as a
  workaround to shrink the effective CSS viewport) also has no visible effect on the rendered
  page. Reproduced independently by two separate workers/sessions (fixed-assets FE dispatch,
  2026-07-10) across fresh tabs and a fresh tab group — not a stale-tab or timing issue.
- **Root cause:** this sandbox's browser automation environment renders into a fixed-size
  virtual display; `resize_window`/keyboard zoom operate on OS-level window chrome that this
  environment doesn't actually control or reflect in the CDP viewport used for screenshots.
- **Fix:** none found yet. Do NOT keep retrying resize_window/zoom variations — it's a hard
  environment ceiling, not a timing or selector issue. For now: do the live desktop-viewport
  smoke test as normal; verify mobile responsiveness by CODE REVIEW instead (confirm the new
  UI reuses already-mobile-verified shared components — DataTable, modal-box, PageHeader,
  PermissionGate — rather than inventing new fixed-width layout) and flag the missing
  screenshot gap explicitly in the report for the orchestrator's call (e.g. real-device check,
  or a Playwright viewport test outside this MCP tool, as a follow-up).
- **Seen:** 2026-07-10, Cycle D fixed-assets FE build.

## Regenerating an already-applied EF migration (ef remove + add) leaves teas_test stuck: "relation already exists"
- **Symptom:** you `dotnet ef migrations remove` + `add <SameName>` to fix something in an
  uncommitted migration (e.g. a column precision) AFTER `dotnet test` has already run once and
  applied the OLD migration to the shared, persistent `teas_test` DB. The new migration gets a
  NEW timestamp id. Next fixture init's `db.Database.MigrateAsync()` (PostgresFixture.cs) sees
  the new id is not in `sys.__ef_migrations`, tries to run its `Up()` (`CreateTable ...`), and
  fails with `42P07 relation "..." already exists` — the OLD migration's tables are still there,
  the OLD id is still recorded, but the PROJECT no longer has that migration class to reconcile
  against.
- **Root cause:** `teas_test` is long-lived and shared across test runs (not recreated per run);
  `PostgresFixture` bootstraps/tracks EF migrations via `sys.__ef_migrations` exactly like a real
  deploy. Renaming a migration's id (any `ef remove`+`add`, even same class name) orphans the
  already-applied tables/history row — there is no automatic reconciliation.
- **Fix:** after regenerating, connect directly to `teas_test` (bypass EF/the fixture — a plain
  Npgsql connection; `Add-Type` on the built `Npgsql.dll` from Windows PowerShell 5.1 does NOT
  work, it's .NET Framework and can't load a net10 assembly — use a tiny standalone
  `dotnet run` console app instead, or `psql` if available) and run: `DROP SCHEMA IF EXISTS
  <schema> CASCADE;` + `DELETE FROM sys.__ef_migrations WHERE migration_id LIKE '%_<Name>';` +
  `DELETE FROM sys.applied_sql_scripts WHERE script_name IN (...)` for any SqlScripts that seed
  into that schema (they're apply-once tracked too — FOOTGUN 9). Then the next `dotnet test`
  applies the new migration + scripts cleanly from scratch. Prefer hand-editing the migration
  file in place (same timestamp) over `ef remove`+`add` when only a column property (precision,
  default, nullability) changed and the migration is still uncommitted — it avoids this entirely.
- **Seen:** 2026-07-10, Cycle C Expense Claims Tier-2 review (vat_rate 5,2 -> 5,4 precision fix).

## Startup SqlScript writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod (green on teas_test)
- **Symptom:** a NEW `SqlScripts/NNN_*.sql` file that INSERTs into a G1 tenant table (e.g.
  `master.chart_of_accounts`) throws at prod startup: `SqlState 42501: new row violates
  row-level security policy for table <table>` — the whole deploy fails, auto-rolls-back. OR:
  a script that SELECTs from a G3 system-global table (e.g. `sys.roles`) to fan out per-company
  data "succeeds" (no error) but silently inserts ZERO rows on prod — a per-company grant/seed
  that should exist everywhere is simply missing, with no crash to flag it. **Both variants are
  INVISIBLE on `teas_test`** — the test DB connects as a Postgres SUPERUSER, which bypasses RLS
  unconditionally, so the exact same script that fails/no-ops on prod runs clean and full on
  teas_test (a green `dotnet test` run, incl. RBAC matrix tests, proves nothing about this class
  of bug — same root cause as the `rls-masked-by-superuser-tests` memory, but for STARTUP
  SEED SCRIPTS specifically, not application-layer service code).
- **Root cause:** `DbInitializer.ApplyScriptsAsync` runs every pending `SqlScripts/*.sql` at
  application startup, BEFORE `TenantMiddleware` ever runs — so `app.company_id` is UNSET for
  the whole script-application phase, on every environment (prod included). Two RLS table
  groups (`600_superadmin_scoped_rls.sql`) react differently to that:
  - **G1** (plain tenant tables — `company_isolation USING (company_id = app.company_id GUC)`,
    deliberately NO bypass arm): Postgres reuses `USING` as the implicit `WITH CHECK` when none
    is given, so ANY `INSERT` with `app.company_id` unset fails 42501 — hard crash, loud.
  - **G3** (system-global tables — `USING (company_id IS NULL OR company_id = app.company_id
    OR app.bypass_rls)`): with `app.company_id` unset AND `app.bypass_rls` unset, only rows
    with `company_id IS NULL` are visible. A per-company fan-out `SELECT ... FROM sys.roles
    WHERE company_id IS NOT NULL` returns an EMPTY set — no error, the enclosing `INSERT ...
    SELECT` just inserts zero rows and the script "succeeds" and gets tracked as applied.
- **Fix pattern** (do NOT touch the underlying RLS policies themselves — the tables' G1/G2/G3
  classification is deliberate, see `600_superadmin_scoped_rls.sql`'s own header):
  - **G3 read/write** (system-global table, e.g. `sys.roles`/`sys.role_permissions`): add
    `SET LOCAL app.bypass_rls = 'on';` as the FIRST statement of the script (after header
    comments). This is exactly the G3 bypass arm's documented purpose ("RBAC cross-company
    mgmt / cross-company audit writes"). `SET LOCAL` is transaction-scoped and
    `DbInitializer.ApplyScriptsAsync` runs each script in its own transaction, so it can never
    leak into another script or a real request. Matches every existing app-layer
    `app.bypass_rls` call site (`RbacAdminService.cs`, `CompanySwitchService.cs`,
    `ApiKeyResolver.cs`, `ETaxRetryWorker.cs`, `OAuthEndpoints.cs`) — all use
    `set_config('app.bypass_rls', 'true', true)`, same LOCAL-scoped, never-user-derived idiom.
  - **G1 write** (tenant table, e.g. `master.chart_of_accounts`): do NOT add a bypass arm — G1
    is deliberately never-bypassable tenant data. Instead wrap the seed in a `DO $do$ ... $do$`
    block that loops `FOR c IN SELECT company_id FROM master.companies LOOP`, calls `PERFORM
    set_config('app.company_id', c.company_id::text, true);` FIRST, then does the normal
    idempotent per-company `INSERT ... WHERE NOT EXISTS ... ON CONFLICT DO NOTHING` scoped to
    `c.company_id`; reset `app.company_id` to `''` after the loop. Mirrors
    `510_per_company_roles_reconcile.sql`'s existing `FOR c IN SELECT company_id FROM
    master.companies LOOP PERFORM sys.seed_company_roles(c.company_id); END LOOP;` fan-out —
    NOT a new pattern, an existing one this codebase already uses for exactly this problem.
    `master.companies` itself carries NO RLS policy (absent from `010_rls_policies.sql`'s and
    600's G1/G2/G3 table lists — it IS the tenant root, not a tenant-owned child table), so
    reading the company id list to drive the loop is always unfiltered.
  - **The tell** (probe BEFORE assuming a fix worked, since a green build/test proves nothing
    here): (1) `SELECT count(*) FROM sys.applied_sql_scripts` — a script that hard-crashed
    (G1 case) never got tracked at all (its whole transaction rolled back), so the count is
    SHORT vs. the number of `.sql` files on disk. (2) For a G1 backfill specifically, e.g. the
    3300 retained-earnings seed: `SELECT count(*) FROM master.companies c WHERE NOT EXISTS
    (SELECT 1 FROM master.chart_of_accounts a WHERE a.company_id = c.company_id AND
    a.account_code = '3300')` — nonzero means the backfill silently didn't run (or didn't run
    for those companies). For a G3 fan-out (the SILENT no-op case, no crash to see): compare
    `SELECT count(*) FROM sys.role_permissions rp JOIN sys.permissions p ON p.permission_id =
    rp.permission_id WHERE p.permission_code = '<new code>' AND rp.company_id IS NOT NULL`
    against the expected `#companies × #target roles` — zero or far-too-low means the SELECT
    saw nothing. To actually RE-EXERCISE a fixed script against `teas_test` (which otherwise
    skips already-tracked script names regardless of content changes — same as a real prod
    redeploy needing its tracker row deleted first): `DELETE FROM sys.applied_sql_scripts WHERE
    script_name = '<name>.sql'` then re-run the app/tests; teas_test's superuser connection
    can't prove the RLS branch itself is exercised, but it DOES prove the new SQL is
    syntactically valid, idempotent, and functionally correct end-to-end.
- **Fix (also see the general pattern above):** applied to
  `SqlScripts/610_seed_year_close_perms.sql` (G3 case — added `SET LOCAL app.bypass_rls =
  'on';`) and `SqlScripts/611_seed_retained_earnings_account.sql` (G1 case — rewrote the
  single `INSERT..SELECT FROM master.companies` as the per-company `DO $do$` / `set_config`
  loop described above).
- **Seen:** 2026-07-09, prod deploy v1.15.0 startup failure (auto-rolled-back to v1.14.1,
  `specs/year-end-closing.md`). teas_test full suite was 843/0/8 green with the ORIGINAL
  (broken-for-prod) 610/611 content — this bug class is INVISIBLE to any test run against a
  superuser-connected DB; only a real NOBYPASSRLS role (prod, or `SET ROLE pg_database_owner`
  in an RLS-specific test) would have caught it. Confirmed fixed: deleted teas_test's tracker
  rows for both scripts (simulating the prod hotfix redeploy) and re-ran the full suite —
  843/0/8 again, both scripts re-tracked with a fresh `applied_at`, "companies missing 3300"
  dropped from 10 to 2 (the 2 residual companies were created via a raw-SQL test fixture
  AFTER 611's re-application within the same test run — expected "run-once seed" behavior,
  not a defect), `gl.year.close` per-company grants went from ~0 real fan-out to 23,282 rows.

## `Npgsql.PostgresException: 22003: value "<big number>" is out of range for type integer` querying `tax.v_number_gaps` (e.g. `Sprint1HardeningTests.RolledBack_allocation_does_not_consume_a_number_or_create_a_gap`), reproduces on EVERY run regardless of the (random) company_id queried
- **Root cause:** `050_number_gap_audit_view.sql`'s `tax.v_number_gaps` view does
  `(regexp_match(doc_no, '(\d+)$'))[1]::int AS seq_no` over EVERY posted `doc_no` in
  `sales.tax_invoices` / `gl.journal_entries` / `purchase.payment_vouchers` **across ALL
  companies** before any `WHERE company_id = …` filter is applied — a single row anywhere in
  the shared DB with an 11+ digit trailing-digit run overflows the `::int` cast (Postgres int4
  max ≈ 2.1B) and breaks the view for EVERY company's query, not just the offending one. A test
  that seeds a `JournalEntry` directly via DbContext with a synthetic `DocNo` built from a raw
  GUID hex substring (e.g. `"JVTEST" + Guid.NewGuid().ToString("N")[..12]`) can — rarely, but it
  happened — end in a long run of hex digits 0-9 (no a-f), producing exactly this: e.g.
  `JVTESTf11443527012` → trailing `11443527012` (11 digits) → 22003. **Once such a row is
  POSTED it is a PERMANENT pollution of the shared `teas_test` DB**: `doc_no` is in the
  `020_journal_immutability.sql` critical-field allowlist (UPDATE blocked) and
  `fn_no_delete_posted_je` blocks DELETE of any non-DRAFT row — there is no way to clean it up
  short of a superuser bypassing both triggers (a call for Fable, not a worker).
- **Fix:** any test that seeds a synthetic `DocNo` directly via DbContext (bypassing
  `INumberSequenceService`) MUST end it in a non-digit character (e.g. append a literal `"X"` —
  not in the hex alphabet) so `tax.v_number_gaps`'s `(\d+)$` regex can never match it. This does
  NOT fix an already-polluted row — if you hit this symptom, the view itself needs a defensive
  fix (e.g. cast to `bigint` instead of `int`, or cap the matched digit-string length) — that's
  a cross-cutting file outside most specs' blast radius; flag it to the orchestrator rather than
  patching it unilaterally from inside an unrelated feature's dispatch.
  **UPDATE (2026-07-09, coordinator-approved rider):** the view fix landed —
  `SqlScripts/613_number_gap_view_bigint.sql` recreates `tax.v_number_gaps` (does NOT edit 050 —
  apply-once tracking) with the digit-run cast to `bigint` instead of `int`, PLUS a
  `length(...) <= 18` guard so a run longer than 18 chars is treated as no-match (`seq_no = NULL`)
  instead of even attempting the cast. The final exposed `missing_seq_no` column is cast back
  down to plain `int` at the outer SELECT — `NumberGapReportService.cs`'s
  `Row(string Series, int MissingSeqNo)` and the test's `SqlQueryRaw<int>` both depend on that
  exact type, so this preserves the external contract; only the internal computation is widened.
  **Self-inflicted footgun while writing 613:** the first draft's own header comment described
  the fix using literal `` `{`/`}` `` backtick-quoted characters (to illustrate "don't use curly
  braces") — but `ExecuteSqlRawAsync` parses the ENTIRE script text as a `string.Format`
  composite-format string (even with zero real parameters), so THAT comment's literal braces
  broke script application with `FormatException: Expected an ASCII digit`, taking down
  EVERY test that touches `PostgresFixture.InitializeAsync` (not just the ones querying the
  view). Fixed by describing the constraint in prose only, zero literal brace characters
  anywhere in the file, comments included. After the fix: `tax.v_number_gaps` renders the
  poisoned company-700926 row as ordinary (very large, but valid) data instead of crashing;
  full backend suite → 843 passed / 0 failed / 8 skipped (was 841 baseline + 2 new
  YearEndClosing tests, exactly matching expectation).
- **Seen:** 2026-07-08/09, `specs/year-end-closing.md` Tier-2 fix stage — `YearEndClosingTests.
  AddPostedJe`'s `DocNo` pattern produced `JVTESTf11443527012` (company 700926), permanently
  breaking the full-suite gate's `Sprint1HardeningTests.RolledBack_allocation_...` test. Fixed by
  `613_number_gap_view_bigint.sql` the same day (coordinator-approved Cycle A rider).

## A test with `new StubTenant { CompanyId = 1, IsSuperAdmin = true }` reads back a FRESHLY-created OTHER company's `ITenantOwned` rows and gets 0 results ("could not find codes {empty}", "found 0" branches/etc.)
- **Root cause:** `AccountingDbContext`'s EF global query filter used to be
  `e => _tenant == null || _tenant.IsSuperAdmin || e.CompanyId == _tenant.CompanyId` — a
  `StubTenant` with `IsSuperAdmin = true` made the filter a permanent no-op, so an explicit
  `.Where(x => x.CompanyId == someOtherCompanyId)` on the SAME query did the real scoping. Any
  test that mints a super-admin `StubTenant` pinned to a FIXED company (commonly `CompanyId = 1`,
  matching whichever provider seeded the DbContext) and then reads back a DIFFERENT, freshly-onboarded
  company's `ITenantOwned` data (`ChartOfAccounts`, `Branches`, etc.) relied entirely on that bypass.
  After `specs/superadmin-tenant-scope.md` retired the `IsSuperAdmin` arm (data scope is now driven
  SOLELY by the pinned `CompanyId`, super-admin-ness is a capability flag only), the filter now ALSO
  enforces `CompanyId == 1`, ANDing with the explicit predicate for the other company → always empty.
- **Fix:** never rely on `IsSuperAdmin` to "read across companies" in a test (or anywhere) — it no
  longer has that effect anywhere in the codebase. Add `.IgnoreQueryFilters()` explicitly to any
  read that is genuinely meant to inspect a DIFFERENT company's `ITenantOwned` data than the
  `StubTenant`/`ITenantContext` currently pinned (this is also the idiomatic EF Core way to say
  "this specific read is deliberately cross-tenant", matching every Family B service call site).
- **Seen:** 2026-07-08, superadmin-tenant-scope fix — `OnboardingFoundingAddressTests.
  CreateAsync_seeds_full_chart_of_accounts_for_gl_posting` and `.CreateAsync_creates_head_office_branch`
  both failed on the first full-suite run after the D3 EF-filter change; fixed by adding
  `.IgnoreQueryFilters()` to their `ChartOfAccounts`/`Branches` verification reads.

## E2E-verifying a new AUTHENTICATED backend report/API route on the public domain returns 307, not 401/403
- **Root cause:** distinct from the anonymous-passthrough entry below. TEAS's dashboard pages and
  their backing backend REST routes often share the IDENTICAL path segment (e.g. FE page
  `app/(dashboard)/reports/ar-aging/page.tsx` and backend route `GET /reports/ar-aging` both live at
  `/reports/ar-aging`). Since the whole `teas.kazaki-rio.com` domain routes to the Next.js frontend,
  curling `https://teas.kazaki-rio.com/reports/ar-aging` unauthenticated hits the FRONTEND PAGE
  (session-gated middleware → 307 to `/login`), never the backend. The browser actually reaches
  authenticated backend endpoints through the existing generic BFF proxy, `frontend/app/api/proxy/[...path]/route.ts`
  (`lib/api.ts`'s `apiGet`/`PROXY = '/api/proxy'`), which itself 401s with no session cookie before
  ever forwarding upstream. A NEW report/REST route needs NO new passthrough file for
  browser-authenticated access — this catch-all already covers it — but that also means the bare
  path is the wrong thing to curl when proving the route exists and is auth-gated.
- **Fix:** to E2E-verify a new AUTHENTICATED backend route through the public domain, curl
  `https://teas.kazaki-rio.com/api/proxy/<backend-path>` (expect 401, proving route + auth gate),
  and separately curl the bare `https://teas.kazaki-rio.com/<same-path>` only to confirm the FE PAGE
  itself resolves (expect 200 or 307-to-login, not 404/500) — these are two different checks against
  the same URL string hitting two different Next.js resources, not one check done twice. Only routes
  meant for ANONYMOUS/external-client access (see the entry below) need their own dedicated
  `app/<path>/route.ts` passthrough + `PUBLIC_PATHS` entry.
- **Seen:** 2026-07-08, v1.14.0 deploy (balance sheet + AR/AP sub-ledger suite) — `/reports/ar-aging`
  returned 307 not 401 on the public domain; resolved by testing `/api/proxy/reports/ar-aging` instead,
  which correctly 401'd for all 3 new routes (ar-aging, customer-statement, vendor-ledger).

## New public API route works locally but 307s to `/login` in prod (or any endpoint minted as a bare/absolute URL)
- **Root cause:** TEAS prod topology: nginx-proxy-manager forwards the WHOLE `teas.kazaki-rio.com`
  domain to the Next.js frontend (:3100 via PM2); the .NET backend (:5180) has NO public ingress of
  its own — there are no nginx custom locations and no `next.config.ts` rewrites. The backend is only
  reachable through explicit Next.js route-handler passthroughs (`app/<path>/route.ts`), e.g.
  `app/mcp/route.ts`, `app/.well-known/jwks/route.ts`. Any NEW backend endpoint that's meant to be
  hit directly by an external client/browser (not via the existing `/api/proxy` or `/api/v1` BFF)
  needs its OWN passthrough — adding it backend-side only is not enough, and `WebApplicationFactory`
  tests never catch the gap because they hit the backend directly, bypassing the Next edge entirely.
- **Fix:** add `frontend/app/<path>/route.ts` mirroring the nearest existing passthrough's
  conventions (env var for `BACKEND_API_URL`, `runtime`/`dynamic` exports, error handling, streaming
  the response body/status/headers back), AND add the exact path (tightest possible segment, not a
  wide prefix) to `PUBLIC_PATHS` in `frontend/middleware.ts` if the route is meant to be reachable
  without a session cookie — otherwise the session gate 307s it to `/login` before it ever reaches
  the passthrough. Verify end-to-end with a live `curl` against the prod domain, not just a backend
  test suite.
- **Seen:** 2026-07-08, mcp-expansion §A hotfix (`/public/pdf` shipped in v1.13.0 with no Next
  passthrough; MCP-minted links 307'd to `/login` until this fix).

## `dotnet build` fails MSB3027/MSB3021 "Could not copy ... Accounting.Api.dll" — locked by testhost (PID N)
- **Root cause:** a previous `dotnet test` run's `testhost.exe` process (spawned under
  `backend/tests/Accounting.Api.Tests/bin/Debug/net10.0/`) survived the session (e.g. a killed/
  interrupted worker) and keeps its copy of `Accounting.Api.dll`/`Accounting.Infrastructure.dll`/etc.
  memory-mapped, so MSBuild's post-build copy step retries 10x then fails the whole solution build —
  even though the actual source change compiles fine.
- **Fix:** `Get-Process -Id <N>` (PID is named verbatim in the error text) → confirm it's `testhost` →
  `Stop-Process -Id <N> -Force` → rerun `dotnet build`. If PID is stale/unknown, `Get-Process -Name
  testhost` to find any survivors first.
- **Seen:** 2026-07-08, mcp-expansion Codex-minor-fix follow-up task.
- **Variant (2026-07-16, WP-A sales-ux-findings):** the locking process was `Accounting.Api.exe`
  itself, not `testhost` — a stale `dotnet run` dev-server left running from an earlier session,
  holding its own `Accounting.Domain.dll`/`Accounting.Application.dll`/`Accounting.Infrastructure.dll`
  copies locked. Same symptom/fix: `taskkill //PID <N> //F` (or `Stop-Process -Id <N> -Force` in
  PowerShell), confirm via `tasklist //FI "PID eq <N>"` first if unsure what the PID is before
  killing it — a build worker doesn't know if it's the user's live server.
- **Variant — the lock owner is a LEGITIMATE concurrent run, never kill it.** When the orchestrator
  (Fable) or another worker has its own `dotnet test` actively running against the shared `teas_test`
  DB (e.g. the Tier-1 full-suite gate, or a sibling worker mid-dispatch), that `testhost` PID is not
  stale — killing it destroys real in-progress verification. Do NOT `Stop-Process` in this case.
  Instead: `dotnet build <project.csproj> --no-restore -o <scratchpad-dir>` (an isolated output path
  — `-o` on a `.sln` prints `NETSDK1194` but still works; prefer targeting the leaf test `.csproj`
  directly to avoid the warning) never touches the shared `bin/`, so it never collides with the lock.
  Verify by running the specific new/changed test directly against the isolated DLL:
  `dotnet test <isolated-dir>/Accounting.Api.Tests.dll --filter "FullyQualifiedName~<Test>"` — this
  is plain `vstest` against a pre-built assembly (no MSBuild copy step), so it is safe to run
  concurrently as long as the test itself doesn't open a Postgres connection (pure/unit-only
  `[Fact]`s — anything DB-backed, e.g. `[SkippableFact]`s needing `PostgresFixture`, still needs an
  explicit all-clear from whoever owns the DB-touching run, since Postgres itself — not just the
  build output — is the shared resource).
- **Seen (this variant):** 2026-08-12, `specs/fix-breakit-r1-ledger-integrity.md` WP-3 FIX A/B round
  — Fable's full-suite Tier-1 gate was live against `teas_test` while dispatching a same-round
  follow-up fix; `dotnet build backend/Accounting.sln` failed MSB3027/MSB3021 on every project DLL
  (PID confirmed as `testhost`, NOT stale). Isolated `-o` build + direct-DLL `vstest` run on a pure
  `EmployeeSalaryPrecisionTests` (no DB) proved RED→GREEN cleanly with zero interference — 65-83ms
  wall time, no Postgres connection opened, shared `bin/` never touched.

## Posted-document "immutability" trigger doesn't fire on a header-only field edit (Receipt trigger 570, or any `fn_enforce_*_immutability`-style trigger)
- **Root cause:** `fn_enforce_receipt_immutability()` (`SqlScripts/570_receipt_immutability_rls.sql`) —
  and its siblings for other doc types (TI's 583 etc.) — do NOT block every UPDATE to a POSTED row.
  They compare `OLD.` vs `NEW.` on a NAMED critical-field allowlist only (for receipts: doc_no,
  doc_date, customer_id, customer_tax_id, amount, total_amount, total_amount_thb, wht_amount,
  cash_received, currency_code, exchange_rate, company_id, branch_id). A write that touches ONLY a
  non-listed column (e.g. `Notes`) sails through silently — no exception, no `DbUpdateException`.
  Confirmed empirically: a test asserting `SaveChangesAsync()` throws after mutating only `Notes` on
  a since-posted receipt FAILED with "no exception was thrown."
- **Fix:** never assume a header-immutability trigger covers "any column" from its doc-comment
  description alone — read the actual `IS DISTINCT FROM` list in the SQL file. For a genuinely
  uniform backstop against a header-only edit racing a concurrent post, rely on the SEPARATE
  lines-table trigger (`fn_*_lines_immutable`, e.g. 582) by ALWAYS delete-and-recreating the child
  line rows on every edit — never a diff/skip-if-unchanged optimization — even when the edit is
  header-only. This is exactly why mcp-expansion.md §D3.2 makes "always rewrite lines" a HARD
  REQUIREMENT for `UpdateDraftAsync`, and it applies to every table with this trigger shape, not
  just Tax Invoice.
- **Seen:** 2026-07-08, mcp-expansion write-side (§D3, `update_receipt_draft`'s race-backstop test).

## `WhtBatchExportServiceTests.Pnd53_batch_groups_by_payee_and_excludes_individuals_and_pnd54` fails with `RecordCount to be 2, but found 4` (or similar off-by-N)
- **Root cause:** the test picks a "distinct far-future period" via `RandPeriod()` (year 3000-8999,
  month 1-12 → 72,000 slots) on the theory that a random period avoids collision with rows other test
  runs left behind (the shared `teas_test` fixture persists `WhtCertificates` rows across runs — see
  "teas_test fixture apply-once" in memory). With a long-lived, never-reset `teas_test` DB accumulating
  rows from hundreds of prior local test runs, the birthday paradox makes an eventual period collision
  likely, not unlikely — a collision pulls in another run's certs for the same (year, month) and inflates
  `RecordCount`/`TOT_NUM`/`TOT_AMT` past what the test just inserted. Unrelated to any MCP/report/sales
  code path — confirmed by re-running the test alone with `--filter`, which passed clean on a fresh
  random period.
- **Fix:** before concluding a code regression, re-run the single failing test in isolation
  (`dotnet test ... --filter "FullyQualifiedName~<TestClass>"`). If it passes standalone, it's this
  known period-collision flake, not a real failure — do not chase it as a regression. A durable fix
  (not applied here, out of scope) would be scoping the `BuildAsync` query filter to the test's own
  inserted DocNo prefix or resetting `teas_test` periodically.
- **Seen:** 2026-07-08, mcp-expansion read-side gate run (§B/§C/§E) — 1 failure out of 645
  `Accounting.Api.Tests`, isolated re-run green.
- **Also seen on:** `Empty_period_throws_no_data` (same class/`RandPeriod()`) — 2026-07-13,
  mcp-document-chain gate. Full-suite run failed it once (845 tests, unrelated to any
  sales/purchase/MCP code path touched that cycle); isolated re-run with a fresh random draw
  passed immediately. Confirms the fix generalizes to every test in this class, not just the
  Pnd53 one. Note: repeated full-suite reruns IN THE SAME SESSION raise the collision odds
  further (each run leaves more far-future rows behind) — don't be surprised if it flakes more
  often the more times you've already run the gate today.
- **Also seen on:** `PayrollRunServiceTests.Pnd1_filings_follow_payment_date_not_period` —
  2026-07-13, v1.20.1 hotfix (bn-settlement-flip) gate. Same shape but a DIFFERENT random-key
  helper: `FreshYearAsync()` (`RandYear() => 3000 + Random.Shared.Next(0, 6000)`) picks a year
  with no existing company-1 `PayrollRun`, same birthday-paradox exposure on the shared,
  never-reset `teas_test` DB. Full-suite run failed it once (997 tests: 147 Domain + 850 Api,
  unrelated to any sales/receipt/document-chain code touched that cycle); isolated re-run
  passed immediately. Confirms this flake class isn't scoped to `RandPeriod()`/WHT — ANY
  test helper that picks a "fresh" random year/period against this DB is exposed the same way.
- **Also seen on:** `Pnd50FilingServiceTests.Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder`
  — same v1.20.1 hotfix gate, a SECOND full-suite rerun in the same session (the note above
  about repeated reruns raising the odds proved out immediately). A THIRD random-key helper:
  `CitExpenseByAccountTests.FreshJeYearAsync` — its own doc comment even names the exact
  mechanism ("§8: foreign far-future JEs in a colliding year would flip Refusals/totals").
  Failed with `Disallowed.Entertainment` off by exactly one other run's leftover ค่ารับรอง
  adjustment (2,500 expected, 10,000 found = 4 accumulated runs' worth); isolated re-run green
  in 623ms. Unrelated to any sales/receipt/document-chain code. Two DIFFERENT full-suite runs
  in one session hit TWO DIFFERENT random-year helpers (Payroll's `FreshYearAsync`, then CIT's
  `FreshJeYearAsync`) — do not chase a 3rd, 4th full rerun chasing a fully-green run; once the
  CHANGED-code-scoped test subset is confirmed green (e.g. `--filter` on the touched
  files/classes) and each individual full-run failure is confirmed isolated-clean, that is
  sufficient evidence — stop re-running the full suite, it only manufactures more collisions.

## MCP round-trip test: `result.Content.OfType<TextContentBlock>().Single()` throws "Sequence contains no elements" for a tool that returns C# `null`; or `JsonElement.GetProperty("someNullProperty")` throws `KeyNotFoundException`
- **Root cause:** the MCP SDK (`ModelContextProtocol.Server`) serializes tool return values with default
  `System.Text.Json` options, which omit `null`-valued properties/results entirely rather than emitting
  a literal `null`. A tool whose C# return type is nullable and actually returns `null` (e.g. `get_invoice`
  for an unknown id) comes back with an EMPTY `Content` list — not a `TextContentBlock` containing the
  text `"null"`. Same mechanism inside a result object: a record property that is `null` (e.g.
  `DocumentChainResult.PurchaseChain` when only the sales slot is populated) is DROPPED from the JSON
  object key-by-key, so `root.GetProperty("purchaseChain")` throws `KeyNotFoundException` — the key isn't
  present at all, not present-with-null.
- **Fix:** never assume a null C# value round-trips as a JSON `null` literal through the MCP wire format.
  For a nullable tool RESULT, check `result.Content.OfType<TextContentBlock>().FirstOrDefault()` — `null`
  (no block) means the tool returned null. For a nullable PROPERTY inside a result object, use
  `root.TryGetProperty(name, out var el)` and treat `false` (key absent) the same as `el.ValueKind ==
  JsonValueKind.Null`. When stuck on an unexpected shape, temporarily `throw new
  InvalidOperationException(root.GetRawText())` inside the test to dump the actual JSON once, then fix
  the assertion against the real shape (matches the OpenIddict discovery-doc lesson below: verify the
  actual wire output, don't assume it from the C# type).
- **Seen:** 2026-07-08, mcp-expansion read-side dispatch (`specs/mcp-expansion.md` C2) —
  `Mcp_get_document_chain_resolves_quotation_anchor_and_rejects_unknown_type` and
  `Mcp_list_invoices_and_delivery_orders_scope_to_caller_company` both failed on first run against this
  exact pattern; fixed by checking key-absence/empty-content instead of asserting `JsonValueKind.Null`.

## OpenIddict custom `HandleConfigurationRequestContext` handler reads `context.AuthorizationEndpoint`/other Attach*-set property as `null` even though the key/collection you add IS present in later output (or silently never lands in the discovery JSON at all)
- **Root cause:** OpenIddict.Server 7.5's built-in discovery pipeline (`OpenIddictServerHandlers.Discovery`)
  registers its own handlers on the SAME `HandleConfigurationRequestContext` event at specific `Order`
  values: `AttachIssuer` = `int.MaxValue - 100_000`, then `AttachEndpoints` (`AuthorizationEndpoint` etc.)
  = `AttachIssuer.Order + 1_000`, then `AttachGrantTypes`/`AttachResponseTypes`/…/`AttachAdditionalMetadata`
  each +1_000 further. TEAS's pre-existing custom handler (added via
  `o.AddEventHandler<HandleConfigurationRequestContext>(b => b.UseInlineHandler(...).SetOrder(int.MaxValue - 100_000))`,
  originally added for `TokenEndpointAuthenticationMethods.Add("none")`) happened to use the EXACT SAME
  order value as `AttachIssuer` — i.e. it runs BEFORE `AttachEndpoints`, so `context.AuthorizationEndpoint`
  (and any other Attach*-populated typed property) is still `null` when the handler body executes. The
  `TokenEndpointAuthenticationMethods.Add(...)` line still worked at that order because it mutates a
  `HashSet` that's only READ once, at the very end of the whole `HandleConfigurationRequest.HandleAsync`
  dispatch (order-independent) — masking the fact that the order was actually very EARLY, not "late" as
  the original comment assumed. A second handler added later in the SAME inline lambda, reading
  `context.AuthorizationEndpoint` synchronously to build `registration_endpoint` (RFC 7591 DCR advertising),
  silently no-opped: the null-guard (`if (context.AuthorizationEndpoint is { } authz)`) was never true, so
  the key was never added — with EITHER `context.Metadata[...]` or `context.Transaction.Response[...]` as
  the sink (the sink was never the bug; `Transaction.Response` also gets wholesale-replaced by a LATER
  handler that builds the final `OpenIddictResponse` from `notification.Metadata` + all typed properties,
  so a direct early write to `Transaction.Response` is discarded regardless).
- **Fix:** don't trust "late-looking" `SetOrder` values without checking what they tie against. Confirm
  built-in handler orders empirically (decompile `OpenIddict.Server.dll`, or fetch the exact source tag
  from `github.com/openiddict/openiddict-core` matching the installed NuGet version) before assuming a
  custom handler's relative position. For discovery-document customization, prefer `int.MaxValue - 50_000`
  (safely after `AttachEndpoints` and `AttachAdditionalMetadata`) over reusing `int.MaxValue - 100_000`.
  Empirically verify any new discovery-metadata addition by dumping the raw response JSON in a temporary
  test assertion (`throw new InvalidOperationException(rawJson)` inside the test, run it once, read the
  actual keys) rather than trusting the design doc's documented fallback blind — in this case BOTH the
  primary approach and its documented fallback failed identically because the real defect was upstream
  of the sink choice.
- **Seen:** 2026-07-05, RFC 7591 DCR implementation (`specs/mcp-dcr-implementation.md`) — T6/T7
  (`DiscoveryEndpointsTests`) failed with `KeyNotFoundException` on `registration_endpoint` under both
  the design's primary `context.Metadata[...]` approach and its documented `context.Response[...]`
  fallback; root-caused via the actual OpenIddict 7.5.0 source, fixed by moving `SetOrder` from
  `int.MaxValue - 100_000` to `int.MaxValue - 50_000`.

## `npx vitest run` (no path arg) fails ~43 files with "Playwright Test did not expect test.describe() to be called here"
- **Root cause:** `frontend/` has no `vitest.config.*`, so vitest's default test glob
  (`**/*.{test,spec}.ts(x)`) also picks up `frontend/e2e/*.spec.ts` and
  `frontend/manual/*.spec.ts`, which are Playwright specs (`@playwright/test`'s `test`/
  `test.describe`, driven by `playwright.config.ts`, not vitest). Pre-existing repo
  structure, not caused by any single change — unit tests live under `lib/**/*.test.ts`
  (e.g. `lib/bath-text.test.ts`), separate from the Playwright e2e suite by convention only
  (no config enforces the split).
- **Fix:** scope the run to the unit tests you actually touched, e.g.
  `npx vitest run lib/<file>.test.ts` (or `lib/`), and don't treat the ~43 "failed" Playwright
  files as a regression signal — they fail identically on a clean checkout with no vitest
  config change.
- **Seen:** 2026-07-04, frontend medium/low fixes (M6/F1/L6, `fix/review-findings-2026-07-04`).

## `corepack pnpm run test -- --run lib` silently STAYS IN WATCH MODE (no pnpm on PATH, must use corepack)
- **Symptom:** the FE test command returns instantly with "running in background", then the
  output file sits at 0 bytes / near-zero CPU on the node workers for many minutes — looks
  like a hang, not a fast failure (different symptom from the sibling entry above, same repo
  quirk family).
- **Root cause:** two stacked issues. (1) `pnpm` is not on PATH in this environment at all
  (Windows, no global pnpm install) — you MUST invoke it via `corepack pnpm ...` or
  `corepack pnpm exec ...`; plain `pnpm` errors "not recognized". (2) `corepack pnpm run test
  -- --run lib` does NOT strip the middle `--` the way a bare `pnpm run` would: it forwards a
  LITERAL `"--"` token into the script's argv, so the actual invocation becomes
  `vitest "--" "--run" "lib"` (confirmed by the banner: it prints `DEV` mode, not `RUN`).
  Vitest then starts in interactive watch mode — with the default glob (see sibling entry
  above) it also collects `e2e/*.spec.ts`/`manual/*.spec.ts` Playwright files as "0 test" and
  then just sits there watching, producing no output and pegging near-zero CPU forever.
- **Fix:** skip `pnpm run` entirely for ad-hoc args — call the binary directly:
  `corepack pnpm exec vitest run lib` (or `lib/<file>.test.ts`). This both forces run-once
  mode correctly AND scopes past the Playwright-spec collision. Kill any stray `node.exe`
  workers left in watch mode before retrying (`Get-Process node | Where StartTime -gt <recent>
  | Stop-Process -Force`) — they don't self-exit.
- **Seen:** 2026-07-13, mcp-document-chain gate finishing (this cycle's FE gate run).

## CS0433 "'Program' exists in both 'Accounting.Api' and 'Accounting.Workers'" when a test project references both
- **Root cause:** `Accounting.Api/Program.cs` and `Accounting.Workers/Program.cs` are both
  top-level-statement files; the compiler synthesizes their implicit `Program` class in the
  GLOBAL namespace (not under `<RootNamespace>`), regardless of project. `Accounting.Api.Tests`
  already references `Accounting.Api` for `WebApplicationFactory<Program>`
  (`RbacApiFactory.cs`/`McpServerSmokeTests.cs`); adding a second, unaliased
  `ProjectReference` to `Accounting.Workers` (e.g. to unit-test a Workers job/service) makes
  the bare `Program` symbol ambiguous across the whole test project — a global compile error,
  not scoped to the new test file.
- **Fix:** alias the new reference so its types don't spill into the global/ambient lookup:
  in the `.csproj`, `<ProjectReference Include="...\Accounting.Workers.csproj"><Aliases>Workers</Aliases></ProjectReference>`;
  in the test file(s) that need Workers types, `extern alias Workers;` as the very first line
  (before any `using`), then `using Workers::Accounting.Workers.Jobs;` etc. Standard C#/MSBuild
  feature for exactly this collision — no need to rename either `Program` or drop the
  reference.
- **Seen:** 2026-07-04, H2 Workers tenant-context fix (`specs/design-h2-workers-tenant.md`) —
  first attempt (plain `ProjectReference` to `Accounting.Workers.csproj`) broke the two
  existing `WebApplicationFactory<Program>` usages with CS0433; fixed with the aliased
  reference + `extern alias`.

## New RLS test SKIPs with "teas_rls_test unavailable ... permission denied to create role"
- **Root cause:** `PostgresFixture` provisions a second, newer NOBYPASSRLS role
  (`teas_rls_test`, `PostgresFixture.RlsTestRole`) via `CREATE ROLE IF NOT EXISTS` at
  fixture init, used by `PermissionLookupRlsTests`. Provisioning needs `CREATEROLE` on the
  `TEAS_TEST_PG` login; this repo's local `accounting` test user does NOT have it, so
  `RlsRoleSkip` gets set and any `[SkippableFact]` gated on `Skip.If(_fx.RlsRoleSkip …)`
  silently reports **Skipped**, not Passed — a false-green if you don't check the skip
  count/verbatim test-run output (exactly the class of bug the "skipped tests fake a green
  run" lesson warns about).
- **Fix:** for NEW RLS-behavioural tests, default to the OLDER, more portable trick already
  used by `SalesChainRlsTests`/`ReviewHardeningRlsTests`: `SET ROLE pg_database_owner`
  (a Postgres built-in predefined role; membership is implicit for whoever owns the
  current DB — no `CREATEROLE` needed) + a manual `GRANT SELECT` on the specific
  table(s) under test to `pg_database_owner` (idempotent, run while still the bypass
  role). Only rely on `teas_rls_test` if you've confirmed `_fx.RlsRoleSkip is null` in this
  environment first.
- **Seen:** 2026-07-04, H5 api-key pre-pin fix (`specs/design-h5-apikey-prepin.md`) —
  `ApiKeyResolverRlsTests` reported `[SKIP]` on first run with `teas_rls_test`; switched to
  `pg_database_owner` + explicit `GRANT SELECT` on `sys.api_keys`/`master.branches`, then
  passed (and correctly failed against the pre-fix code, proving it wasn't a vacuous test).

## Full `Accounting.Api.Tests` run: a single, DIFFERENT test fails each run (Pnd50 ladder mismatch, Pnd50 refusals, TenantIsolation Npgsql connection reset, ...)
- **Root cause:** ~575 tests share one `teas_test` Postgres DB inside one `[Collection(nameof(PostgresCollection))]`; xUnit doesn't guarantee test-class run order within a collection, and some tests depend on mutable shared rows (e.g. `RbacCartesianTests.cs`'s own comment: "finalising CIT year 2099 a Pnd50 test relies on"). Over the ~5.5 min full run the local Postgres connection also occasionally drops mid-`SaveChangesAsync` (Npgsql "ReadMessageLong" reset) — a transient/environmental hit, not a test-logic bug.
- **Fix:** a single full-suite failure is not automatically a regression from your diff. Re-run just the failing test (filter by `FullyQualifiedName~<Name>`) in isolation and re-run the FULL suite again — if it passes alone, or a DIFFERENT single test fails on the next full run, it's pre-existing order/connection flakiness. Only escalate if the SAME test fails deterministically across repeated full runs, or it's in a file your diff actually touches.
- **Seen:** 2026-07-04, H3 PUT-validation fix (`specs/fix-review-findings-2026-07-04.md`) — full run failed `Pnd50FilingServiceTests.Pnd50_with_nonzero_adjustments_renders_the_ladder_in_v2`; isolated re-run of that file failed a DIFFERENT method (`Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder`); a run excluding the new H3 tests entirely failed a THIRD, unrelated test (`TenantIsolationTests.Customer_from_company_A_is_invisible_to_company_B`, raw Npgsql connection reset). None of these files/areas are touched by the H3 diff (Customer/Branch/Vendor/Account validators + endpoints only).
- **Seen again:** 2026-07-22, WP-A army-findings fix (GlPostingService.cs VI-linked self-withhold gross-up, `specs/fix-army-findings-2026-07-22.md`) — TWO consecutive full-suite runs both failed the SAME method, `Pnd50FilingServiceTests.Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder` (921 passed/8 skipped/1 failed/930 total, identical both times — skip count matched the 921/8 baseline exactly, and the new WP-A test's pass exactly offset the flake's fail in the pass-count). Isolated re-run of that one test (plus the new/regression tests) passed clean. File is `TaxFilings/Pnd50FilingServiceTests.cs` — unrelated to the diff (`Ledger/GlPostingService.cs`, PV frontend page, `Hardening/Sprint87ForeignVendorTests.cs`). Treated as the same pre-existing order/shared-row flakiness, not a regression — but note it can now repeat on the SAME method across separate full runs, not just "a different test each time" as originally observed.
- **Seen again:** 2026-07-25, WP-B Opus Tier-2 fix round (PaymentVoucher Version-token liveness +
  Draft-cancel, `specs/fix-army-findings-2026-07-22.md`) — a full-suite run failed 2 tests: the
  now-familiar `Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder` PLUS a NEW member of
  this flake class, `Accounting.Api.Tests.TaxFilings.WhtFormPdfFillTests.
  Pnd54_maps_ma70_amounts_through_to_the_form` (a `HaveCount` assertion on a `WhtFilingRow` list —
  same `TaxFilings` shared-row family, both failed within the first 10s of the run). Neither file
  is touched by the diff (`Domain/Entities/Purchase/PaymentVoucher.cs`,
  `Infrastructure/Purchase/PaymentVoucherService.cs`, the two `PaymentVoucherCancelTests.cs`
  files); both passed clean on an isolated filtered re-run of just those two tests together. A
  subsequent full clean re-run (0 failures) confirmed it. Filed here so a future worker recognizes
  `WhtFormPdfFillTests.Pnd54_maps_ma70_amounts_through_to_the_form` as part of the same
  `TaxFilings`-shared-state flake pool as `Pnd50FilingServiceTests`, not a fresh regression.
- **Seen again:** 2026-07-25, WP-C fix (K-Plus PDF import 500, `specs/fix-army-findings-2026-07-22.md`)
  — TWO full-suite runs, each failing exactly 1 (DIFFERENT) test: run 1's failure detail was lost to
  a `tail -80` truncation (only the 932/1/8/941 summary survived); run 2 failed
  `Accounting.Api.Tests.Expense.ExpenseClaimServiceTests.Cancel_is_legal_from_Draft_and_Rejected` with
  `DomainException: Company with Tax ID '0000311102657' already exists` from
  `TestCompanyFactory.CreateAsync` — a random/shared Tax-ID collision, not a real domain bug (the
  diff touched only `Bank/Pdf/KPlusPdfLineAssembler.cs` and `Bank/StatementImportService.cs`,
  nowhere near Company/Expense creation). Passed clean on an isolated filtered re-run
  (`ExpenseClaimServiceTests.Cancel_is_legal_from_Draft_and_Rejected` + the new Bank/PDF tests
  together, 12/12). Confirms the "single, different test each run" signature holds even when the
  specific failing test is OUTSIDE the previously-seen `TaxFilings`/`Pnd50` pool — any single
  full-run failure needs the isolate-and-rerun check before it's treated as a regression, not just
  ones matching the two named test classes above.

## Onboarded company has NO head-office branch (until v1.11.1) → MCP consent 400s
- **Symptom:** OAuth/MCP consent `POST /oauth/authorize` (approve) returns `400 company_has_no_active_branch`; the connector shows "เกิดข้อผิดพลาด กรุณาลองใหม่". OpenIddict logs `access_denied` (ID2015) ONLY for a Deny — an Approve that 400s is OUR handler's `Results.BadRequest`, which bypasses OpenIddict logging (so grep the request body, not OpenIddict errors).
- **Root cause:** `CompanyService.CreateAsync` (MasterDataServices.cs) seeded company/profile/WHT/CoA/tax/RBAC but NOT a `master.branches` row. `OAuthEndpoints` authorize needs an active HQ branch to pin the token `branch_id`. Same class as the earlier empty-CoA gap. Demo companies (SQL seeds 120/400) DO get a branch, so only UI/API-onboarded companies were affected. `TestCompanyFactory` had silently worked around it by inserting the branch itself.
- **Fix:** v1.11.1 — CreateAsync now seeds the `"00000"` HQ branch (`is_head_office`, `is_active`) unconditionally. Existing prod companies backfilled via `publish/fix-missing-hq-branches.sql` (idempotent NOT EXISTS). `branches` has a COMPOSITE unique `(company_id, branch_code)` (not global) — so `TestCompanyFactory` had to drop its manual insert and read the seeded branch back (else `23505`).

## OAuth consent page was gated by the (dashboard) onboarding redirect → unreachable via login
- **Symptom:** deep-linking to `/oauth/consent` while logged-OUT: login succeeds but lands on the dashboard, never the consent screen (so the MCP connect only worked if you were already logged in first).
- **Root cause:** the page lived under `app/(dashboard)/`, whose `layout.tsx` redirects any `isSuperAdmin && companyId===0` user to `/onboarding` (a super-admin has no home company → companyId===0 on every visit); onboarding auto-switches and `replace('/')` → dashboard, dropping the consent URL + OAuth params. A server layout can't read the pathname, so it can't carry `returnTo`.
- **Fix:** v1.11.1 — MOVED the page to `app/oauth/consent/page.tsx` (out of the gated group). URL unchanged (route groups don't affect the path); still session-gated (middleware PUBLIC_PATHS excludes `/oauth/consent`); providers come from the ROOT layout. **Deploy footgun:** a Next page MOVE needs the OLD path DELETED on the server (the overlay-based FE deploy only adds files) — two `page.tsx` for one route breaks `next build`. `deploy-fe-v1111.sh` rm's the old dir before building.

## StringBuilder.AppendLine breaks cross-platform CSV/text snapshots (found v1.12.0)
- **Symptom:** CSV/text-emitting endpoint passes tests on Windows dev, fails on Linux CI with "expected N lines, found 1" (or vice versa).
- **Root cause:** `StringBuilder.AppendLine` emits `Environment.NewLine` — `\r\n` on Windows, `\n` on Linux. Any test (or RFC 4180 consumer) expecting a fixed delimiter breaks on the other platform.
- **Fix:** emit the delimiter explicitly: `sb.Append(x).Append("\r\n")`. Fixed in `ReportEndpoints.cs` GL CSV export (PR #49).

## gh pr checks --watch exit code is NOT a pass signal (v1.12.0 release)
- **Symptom:** `gh pr checks <n> --watch --fail-fast` exited 0 while the backend check had FAILED; merge proceeded on red.
- **Root cause:** watch exit status ≠ aggregate check conclusion (observed gh behavior in this repo's setup).
- **Fix:** after any watch, ALWAYS re-run plain `gh pr checks <n>` and READ each line's pass/fail before merging. Never gate a merge on watch's exit code.

## Branch protection vs release-please PRs (enabled 2026-07-08)
- **Setup:** main requires status checks `backend`+`frontend` (strict=false, enforce_admins=false). A red merge is now mechanically impossible via the normal path.
- **Symptom:** release-please PRs show "no checks reported" forever — GitHub does not fire `pull_request` workflows for PRs created by GITHUB_TOKEN. Required checks therefore block them.
- **Procedure:** release-please PRs (changelog+version bump ONLY — verify the diff is exactly that) merge with `gh pr merge <n> --merge --admin`. `--admin` is FORBIDDEN for any other PR. Permanent fix if friction grows: give release-please a PAT via its `token:` input so CI fires normally.

## Test asserts an exact past/future DocDate on a posted TaxInvoice/Receipt/VendorInvoice — fails with "today" instead
- **Symptom:** e.g. `Expected statement.Lines[1].DocDate to be <2026-07-09>, but found <2026-07-08>` even though the request explicitly passed `DocDate = today.AddDays(1)`.
- **Root cause:** `TaxInvoiceService.CreateDraftAsync`/`PostAsync`, `ReceiptService.PostAsync`, and `VendorInvoiceService.CreateDraftAsync` (`§10`) ALL ignore the request's `DocDate`/re-pin it to `IClock.TodayInBangkok()` server-side, at both draft-create AND post time. Only `IJournalService` manual JVs behave the same way (see `GeneralLedgerReportTests`'s `Today`-only postings) — this is a repo-wide "never trust client DocDate" convention, not one service's quirk. Two documents posted within the same test run can NEVER land on two different real DocDates.
- **Fix:** don't try to backdate/forward-date a document — post everything at `today` and vary the *query* range/asOf instead (e.g. `GeneralLedgerReportTests.Opening_balance_...` queries a range that starts AFTER today so a today-dated posting falls "before range"; for a same-day tie between two doc types, sort by a fixed DocType rank, not DocDate, to get deterministic order).
- **Seen:** 2026-07-08, specs/subledgers.md S5 (`SubledgerReportTests.Customer_statement_running_balance_and_opening_excludes_prior_movements`).

## Stale TEAS_TEST_PG connection strings in old progress.md entries
- **Symptom:** full `dotnet test` run fails ~all DB tests in seconds ("connection refused" / auth failure) even though targeted runs were green minutes earlier from another shell.
- **Root cause:** grep for `TEAS_TEST_PG` surfaces ANCIENT values first (`Port=5433;Username=postgres;Password=teaspass` era, pre-PG18). The dev PG has since moved: PostgreSQL 18 Windows service on **5432**, user **accounting**. Nothing listens on 5433 anymore.
- **Fix:** the CURRENT string is `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true` (see specs/general-ledger.md env notes). When in doubt, verify the port with `Get-NetTCPConnection -State Listen` before dispatching a test run.
- **Seen:** 2026-07-08 Tier-3 gate (520 fake failures in 8s from the stale string).

## Deploy probe: applied_sql_scripts total != repo .sql file count (v1.17.0 false rollback, 2026-07-10)
Symptom: deploy-api probe `total_sql_scripts` FAILs (prod says 68, repo ships 88 files) and auto-rollback fires although every functional probe passed.
Root cause: prod's `sys.applied_sql_scripts` ledger only records scripts run since the DB's creation — the 2026-06 migration squash baked older scripts into EF migrations, so they were never individually recorded. Repo file count is NOT a valid expectation for prod.
Fix: derive the expectation from the TARGET DB (pre-deploy count + number of NEW scripts). Prod baseline after v1.17.0 = 68. The per-release `new_sql_scripts=<n>` probe is the one that actually matters.

## Deploy probe against EF migrations history table: default name `__EFMigrationsHistory` does not exist on prod (v1.20.0)
Symptom: a deploy-probe query `SELECT ... FROM "__EFMigrationsHistory" WHERE "MigrationId" LIKE '%<Name>'` throws `42P01: relation "__EFMigrationsHistory" does not exist` on prod, even though the migration DID apply (or is about to).
Root cause: this project configures a CUSTOM EF migrations-history table — `sys.__ef_migrations` (lower_snake_case columns `migration_id`/`product_version`, schema `sys`), not the EF default `dbo.__EFMigrationsHistory`. `PostgresFixture` uses the same custom table for tests (see the "Regenerating an already-applied EF migration" entry above), so this has been true all along — it just hadn't been hit from a *deploy probe* before.
Fix: any pre-/post-deploy probe verifying a migration was recorded must query `sys.__ef_migrations` (`SELECT count(*) FROM sys.__ef_migrations WHERE migration_id LIKE '%<MigrationName>'`), never the EF default name.
Seen: 2026-07-13, v1.20.0 deploy (McpDocumentChain migration) — pre-deploy check against the default name failed with 42P01; corrected before the actual deploy-api script ran (which used the right table and passed `mcp_chain_migration_applied=1`).

## Manual-capture Playwright run fails "Executable doesn't exist ... chrome-headless-shell.exe" (fresh/cold machine, 2026-07-14)
Symptom: `node node_modules/@playwright/test/cli.js test -c manual/playwright.config.ts -g "..."` fails every test instantly with `browserType.launch: Executable doesn't exist at ...\ms-playwright\chromium_headless_shell-...\chrome-headless-shell.exe`, even though backend :5080 + frontend :3000 are both up and healthy.
Root cause: `frontend/manual/playwright.config.ts` has no browser `channel` override, so Playwright launches its own bundled Chromium (downloaded separately from `node_modules`, cached under `%LOCALAPPDATA%\ms-playwright`). That cache is per-machine/per-profile and is NOT restored by a normal `npm`/`pnpm install` — it needs an explicit browser-binary fetch, which a cold session/new machine won't have done yet.
Fix: `cd frontend && node node_modules/@playwright/test/cli.js install chromium` (downloads Chromium + headless-shell + ffmpeg, ~300MB, one-time per machine). Re-run the capture command after — no config change needed.
Seen: 2026-07-14, manual ch.5 refresh (PROGRESS-purchase-uxtest.md Phase 2) — first capture attempt on a fresh session failed until browsers were installed.

## PO/PV VAT display and "อัตรา VAT" auto-fill are company/vendor-config-dependent, not universally on/off
Symptom: a prod UX test on a specific company (Repttown, BU TEST) found the PO/PV forms show NO VAT amount even for a nominally "VAT-registered" vendor (findings F5/F14 in PROGRESS-purchase-uxtest.md), suggesting a product regression.
Root cause: `vendorVat = vatMode && (vendor?.vatRegistered ?? true)` (`frontend/app/(dashboard)/purchase-orders/new/page.tsx`) gates the VAT row on BOTH the company's own `vatMode` (from `/system/info`) AND the selected vendor's `vatRegistered` flag — either one false hides VAT entirely, including on any PO-linked Vendor Invoice line (whose pulled `vatRate` derives from the PO line's actual `taxAmount`, so it inherits 0 too). Confirmed empirically: company 2 (co2, the VAT-registered manual-demo company) shows VAT 7% normally for its VAT-registered vendor, both on PO totals AND on a PO-linked VI line — so this is NOT a blanket regression, it's the intended per-company/per-vendor gate. The company used in the prod test likely has `vatMode=false` (or that specific vendor's `vatRegistered` flag doesn't match what the UI implied).
Fix/lesson: before writing "the system doesn't compute VAT" into docs/findings, check the company's `vatMode` (system info) and the specific vendor's `vatRegistered` flag — don't generalize from one company's behavior. When documenting for the manual (which is captured against co2), trust the live co2 capture over a differently-configured company's finding.
Seen: 2026-07-14, manual ch.5 refresh — reconciled against PROGRESS-purchase-uxtest.md F5/F14/F15.

## Release-please PR needs --admin merge (branch protection, 2026-07-10)
Symptom: `gh pr merge <release-PR>` fails "base branch policy prohibits the merge"; statusCheckRollup is EMPTY.
Root cause: CI workflow doesn't trigger on the release-please branch (only touches CHANGELOG/manifest), so required checks never report; --auto never fires either.
Fix: `gh pr merge <n> --merge --admin`. Then wait for the tag on origin (`git ls-remote origin refs/tags/vX.Y.Z`) before building — and build from the OFFICIAL tag commit (release-please's merge commit), not the feature merge commit, so the MinVer-stamped sha matches the release.

## DataTable BU (business unit) column shows raw "#id" instead of "CODE — name", inconsistently across list pages (R1, confirm-round 2026-07-15)
Symptom: a list page's "หน่วยธุรกิจ" column renders `#7` instead of `REPT — หน่วยธุรกิจ REPT`, even though `/api/proxy/business-units?includeInactive=true` clearly returns that id — and it persists indefinitely (not a flash-then-fix), including well after the network call has visibly succeeded. Confusingly, a DIFFERENT list page using byte-identical code (e.g. vendor-invoices vs purchase-orders) may show the correct name in the same session, making it look like a page-specific code bug when the source is actually identical.
Root cause: every list page with a BU column follows the `columns = useMemo(() => [...accessorFn: (r) => buName(r.businessUnitId)...], [t, tc])` pattern (9 pages: delivery-orders, invoices, payment-vouchers, purchase-orders, quotations, receipts, sales-orders, tax-invoices, vendor-invoices). `buName` comes from `useBusinessUnitName()` (`lib/queries.ts`), a NEW closure every render over that render's `useBusinessUnits(true)` data — but it is NOT in the memo's dependency array (`[t, tc]` only, next-intl's `t`/`tc` are referentially stable). So whatever `buName` looked like at the very FIRST render (closed over `data=[]` if the business-units query hadn't resolved yet) is frozen for the entire component lifetime — it never recomputes even after the query settles moments later. This is navigation-order dependent: whichever page happens to mount BEFORE the business-units query is already warm elsewhere in the session (e.g. React Query cache primed by an earlier page/component) shows correctly; whichever mounts cold (first page after login, or first page to ever touch that query) is stuck on `#id` forever for that mount.
Fix (R1, INCOMPLETE — see R8 below): pull the raw data too (`const { data: businessUnits } = useBusinessUnits(true);`) and add it to the memo's deps (`[t, tc, businessUnits]`) so the memo recomputes once the query resolves. Verified: hard-navigate a FRESH tab to the list page (a soft/SPA re-navigate to the SAME route may not remount and can hide the fix — use a new tab or a genuinely different prior route).
Footgun while verifying: with several MCP browser tabs open at once, the local dev stack (backend + `next dev`) can queue/slow the `business-units` fetch enough that even a genuinely fresh tab appears "stuck" on `#id` for several seconds — this looks exactly like the bug but is just contention, and self-resolves (confirmed by closing the extra tabs and re-testing in isolation). Don't conclude the fix failed from one slow tab under load; retest with fewer concurrent tabs before treating a stall as a regression.
Seen: 2026-07-15, R1 fix (`specs/fix-confirm-round-r1-r4.md`) — `purchase-orders/page.tsx` fixed first (spec scope). **2026-07-15 follow-up: all remaining 8 pages fixed** (delivery-orders, invoices, payment-vouchers, quotations, receipts, sales-orders, tax-invoices, vendor-invoices) — identical fix applied to each; spot-checked vendor-invoices and payment-vouchers live on local dev (fresh tabs), both resolve correctly.

**R8 follow-up (2026-07-15, later same day) — the R1 fix does NOT fully fix it; there's a SECOND, deeper root cause the memo-deps fix cannot reach.** Prod finding: `/invoices` still showed raw `#3`/`#1` even though `/vendor-invoices` (byte-identical code, R1 fix applied to both) worked, and a `?cb=1` cache-bust reload did not help. Locally reproduced 100% deterministically: from an ALREADY-LOADED `/invoices` list (business-units query fully warm, `businessUnits` populated), click "create", fill the form, pick a Business Unit, save-draft (client-side/SPA redirect straight back to the list, exactly what the create flow does) — the brand-new row shows `#id` and **never self-corrects**, even after 5+ seconds, even though a `queryClient.getQueryCache()` dump proves the `['business-units', true]` query is `state: success`, fully populated, still subscribed (`observers.length: 2`). Root cause (confirmed by reading TanStack Table's own source, `table-core/build/lib/core/row.js`): `row.getValue(columnId)` caches its result forever in `row._valuesCache[columnId]`, populated lazily on the FIRST call and **never invalidated by a `columns` array change — only by `table.options.data` getting a new reference** (`getCoreRowModel`'s memo key is `[table.options.data]` alone, per `table-core/build/lib/utils/getCoreRowModel.js`). So if a new row's very first `getValue()` call for the BU column happens to land before `useBusinessUnits(true)` resolves (a race that the redirect-right-after-create flow makes far more likely than a plain page load, since the row model is built essentially at first paint), the `#id` fallback gets baked into that row object PERMANENTLY — no later re-render of the page, no matter how many times `columns`/`buName` recomputes with correct data, can ever override it, because `getValue()` short-circuits on the cached entry before ever calling `accessorFn` again. A hard/fresh reload usually "fixes" it only by accident (a cold reload tends to let the small `business-units` fetch resolve before first paint) — it is not a reliable verification method for this failure mode, and is why R1's "hard-navigate a fresh tab" verification step missed this entirely (also why only 2 of the 9 pages were spot-checked live, and neither was tested via the create→redirect path).
Real fix: never let `accessorFn` return a value that depends on data that can arrive AFTER the row is first read — it gets cached. `accessorFn` may still return the resolved name (fine for the faceted filter's dropdown/options), but the CELL renderer must bypass `getValue()`'s cache for the actual displayed value: `cell: ({ row }) => buName(row.original.businessUnitId)`, i.e. resolve fresh from the immutable raw field + the CURRENT render's `buName` closure every time, instead of `cell: ({ getValue }) => getValue()`. `cell` is not subject to `_valuesCache` (it's called directly in JSX on every render), so this is safe.
**2026-07-15, batch follow-up (same day): all 9 pages fixed** — delivery-orders, invoices, payment-vouchers, purchase-orders, quotations, receipts, sales-orders, tax-invoices, vendor-invoices all carried the byte-identical vulnerable `cell: ({ getValue }) => getValue()` pattern (confirmed via grep before AND after — zero stragglers left) and all now use `cell: ({ row }) => buName(row.original.businessUnitId)`. Gates: `tsc --noEmit` 0 errors, `next build` compiled successfully (all routes), Bengali `ম` glyph grep clean on all 9 files. Live spot-checked TWO pages via the exact deterministic repro (create → save → client-side/SPA navigation back to the list, no hard reload): `invoices` (BU1E3, then XBUBE4A — both resolved correctly on first render, no `#id` flash) and `purchase-orders` (create → redirects to the new PO's detail page first, confirmed correct there, then a sidebar `<Link>` soft-nav back to `/purchase-orders` — new row #20/LAB resolved correctly immediately), proving the fix pattern holds beyond `invoices` and across a different post-create redirect shape (detail-page-first vs list-first).

## `next build` while `next dev` is already running on the same repo corrupts the dev server (2026-07-15)
Symptom: a previously-fine `npm run dev` at :3000 starts 500-ing every route with `Error: Could not find the module "...next-devtools/userspace/app/segment-explorer-node.js#SegmentViewNode" in the React Client Manifest` and/or `[TypeError: __webpack_modules__[moduleId] is not a function]`, right after an unrelated `npm run build` was run in another shell against the same `frontend/` checkout.
Root cause: `next dev` and `next build` both read/write the SAME `.next/` directory by default (no separate output dirs configured). Running a production build while a dev server is live against the same checkout corrupts the dev server's cached React Client Manifest / webpack module map — the dev server does not recover on its own; every subsequent request 500s.
Fix: kill the dev server (`taskkill //F //PID <pid listening on :3000>` on Windows) and restart `npm run dev` fresh — do NOT expect a Fast-Refresh reload to self-heal it. If you need both a live dev server AND a build/typecheck pass in the same session, run `npm run build`/`tsc --noEmit` from a **separate worktree or checkout** (or just accept the dev-server restart cost) rather than running them concurrently against the one checkout that also has `next dev` attached.
Seen: 2026-07-15, R8 fix verification — `npm run build` (gate) run in a second shell while the `npm run dev` instance used for live repro was still up; the dev tab immediately started 500-ing.

## New authn-only endpoint (no specific permission) fails RbacAuthMapTests + RbacCartesianTests even though it works fine over real HTTP
Symptom: a new `.RequireAuthorization()` (no permission policy) endpoint passes manual/live testing, but `RbacAuthMapTests.Generate_endpoint_permission_map_and_flag_unprotected_endpoints` fails ("unexpectedAuthnOnly... Found: POST /your-route") and/or `RbacCartesianTests.Every_role_x_endpoint_pair_enforces_the_seeded_grants` fails with `ALLOW expected, got 403` for every role including SUPER_ADMIN.
Root cause: two separate gates. (1) `RbacAuthMapTests.cs`'s `ExpectedAuthnOnly` array is an explicit allowlist of routes intentionally reachable by any authenticated user with no permission code — any authn-only route not in it is flagged as a possible missing perm-gate. (2) `RbacCartesianTests.cs` mints one JWT per ROLE with a SYNTHETIC `UserId` (`990_000 + hash(role)`, `Token()` helper) that has NO backing `sys.users` row for ANY role, including super-admin. If your handler does a LIVE DB lookup of the caller (not just JWT claims — e.g. re-validating active/not-locked status, WP2.1's `/auth/refresh`), it legitimately 403s every synthetic token, which the Cartesian harness reads as an RBAC mismatch unless told otherwise. The pre-existing `HandlerGatedAuthnOnly` skip-set only excuses NON-super roles (its one entry, first-run `instance-keys`, gates on the `is_super_admin` CLAIM, which a synthetic super-admin token still satisfies) — it does NOT cover a real-DB-lookup gate, which fails even for super-admin.
Fix: (1) add the route to `ExpectedAuthnOnly` in `RbacAuthMapTests.cs` with a one-line reason comment. (2) if the handler's extra gate is claims-only (e.g. `is_super_admin`), add it to `HandlerGatedAuthnOnly` in `RbacCartesianTests.cs`. If the extra gate does a REAL DB read of the caller (any check that can't be satisfied by a synthetic token, for ANY role), add it instead to the separate `RequiresRealDbUser` skip-set (skips the ALLOW assertion unconditionally, not just for non-super) — do NOT reuse `HandlerGatedAuthnOnly` for this case, its condition only skips non-super roles.
Seen: 2026-07-14, WP2.1 `POST /auth/refresh` (fix-purchase-ux-findings-2026-07-14.md) — both gates hit on first run; fixed by adding the route to `ExpectedAuthnOnly` and a new `RequiresRealDbUser` set.

## Prod browser sees 5xx that origin never logged → check CF edge, not the app (S13 family)
Symptom: authenticated browser session gets 503 on `/api/proxy/...` (sometimes repeatedly on the SAME path), on `?_rsc=` prefetches, or on `_next/static` chunks (→ `ChunkLoadError` white screen "Application error: a client-side exception..."). Meanwhile curl through the same public domain works, and the API on localhost answers instantly.
Diagnosis method (proven 2026-07-16): compare the browser's failed request against the nginx-proxy-manager access log on the VPS — `sudo grep '<path>' /opt/npm/data/logs/proxy-host-13_access.log` (host 13 = teas.kazaki-rio.com; find via `sudo grep -l teas /opt/npm/data/nginx/proxy_host/*.conf`). If the origin log shows 200/204 (or NO entry at all) for a request the browser saw as 503, the 5xx was generated at the Cloudflare edge without (or despite) contacting origin. Captured evidence: PUT /api/proxy/employees/2 → origin 204, browser 503 = the "503-but-applied" write class — the change IS saved while the user sees an error; GET same path 503'd 4× with only one origin entry; origin log had zero 503s all day.
Consequences to remember while testing/debugging: (1) a "failed" write may have applied — re-read state before retrying non-idempotent actions; (2) React Query caches the error → the UI action (e.g. employees pencil → edit modal) stays dead on re-click until a full page reload, which looks like a frontend bug but isn't; (3) chunk 503 = full white screen (no error boundary yet — spec fix-payroll-reports-findings R1).
Seen: 2026-07-16 payroll/reports UX test (REPORT-payroll-reports-uxtest.md §Infra, window 22:10–22:40 ICT); same family as the 2026-07-16 13:02–13:12 sales-test incident.
Root-cause investigation (2026-07-19, specs/fix-s13-cf-edge-503.md): origin fully ruled out (zero 503s ever logged, no resource pressure, config clean). CF-dashboard check (same day, Ham's login): **Bot Fight Mode OFF, zero challenge/block events on teas from our traffic** → bot-scoring hypothesis REFUTED for the checkable window; leading hypothesis by elimination = CF edge↔origin connection race (intermittent). NO fix applied (WAF Skip rule would be a no-op). If it recurs: follow the recurrence playbook at the end of the spec — check CF Events SAME-DAY (free plan = 24h sampled retention), grab the Ray ID, compare origin log.

## CDP screenshot times out ("renderer may be frozen") during Claude-in-Chrome runs — Escape recovers, page is fine
Symptom: `computer(screenshot)` fails with `Page.captureScreenshot timed out after 30000ms... renderer may be frozen`, typically right after opening a modal or an SPA nav; repeated retries keep timing out.
Reality: the page itself is usually NOT frozen — `read_page`/`get_page_text`/`find`/`form_input` all still work (verified: filled and saved a form while screenshots were timing out). Sending `key: Escape` unblocks screenshot capture (side effect: it also closes any open modal/dialog, so re-open after). Root cause unconfirmed (print-preview-like viewport state was observed once; viewport size flips between captures).
Workaround: on a screenshot timeout, don't loop retries — switch to a11y-tree tools for the interaction, or press Escape first if a visual capture is truly needed (accepting the modal will close). Do not conclude the app hung from this signal alone.
Seen: 2026-07-16 payroll/reports UX test, ~8 occurrences across /payroll and /settings/employees.

## [FIXED 2026-07-19] `McpServerSmokeTests.E3_create_vendor_returns_id_code_name` failed with `result.IsError = true` — stale test fixture, not a product bug
Symptom: the ONLY failure in an otherwise-green full-suite run; `result.IsError.Should().NotBe(true)` at `McpServerSmokeTests.cs:1233` failed even when re-run in complete isolation (`--filter FullyQualifiedName~E3_create_vendor_returns_id_code_name`), so it was not xUnit collection-order flakiness. The assertion only checked `IsError` — the actual message was inside `result.Content` (`TextContentBlock`) and nobody had read it.
Root cause (confirmed via `--logger "console;verbosity=detailed"`, which surfaces the `McpErrorSurfacingFilter` warn line + full FluentValidation exception): the E3 request sent `vatRegistered: true` with `taxId: null` and no `isForeign` (defaults false). WP1 commit `65b9b2b` (2026-07-14, F13) added a `CreateVendorValidator` rule requiring a valid, checksum-verified 13-digit Thai Tax ID whenever `VatRegistered && !IsForeign` (`VendorDtos.cs` ~line 62) — a deliberate, correct business rule (input-VAT/ภ.พ.30 compliance), independently covered by `Hardening/VendorVatTaxIdValidatorTests.cs` added in the same commit. The E3 test predates WP1 (introduced in `06fc16f`) and was never updated to match — its fixture data became invalid the moment the rule landed, and the MCP error-surfacing filter (also WP1-era) was correctly rejecting it every single run since. `create_vendor`'s behavior itself was never broken.
Fix: `McpServerSmokeTests.cs` E3 request's `taxId` changed from `null` to `"0105556123453"` — the same mod-11-valid Thai Tax ID constant already reused ~19× across the suite (Sprint55/Sprint87 vendor seeds, `TestCompanyFactory`, `VendorVatTaxIdValidatorTests.ValidTaxId`, several SqlScripts seeds). Verified: E3 green in isolation, full `McpServerSmokeTests` class 36/36 green, full `Accounting.Api.Tests` suite 897 passed / 8 skipped / 0 failed (previously 1 failed = this test). See `specs/fix-e3-create-vendor-ci.md`.
Lesson: don't assert only `IsError` on an MCP tool-call result and stop there — when it's `true`, always read `result.Content`'s `TextContentBlock` text before concluding "pre-existing/environmental"; the real cause is usually right there.
Seen: 2026-07-14 (WP34), 2026-07-16 (backend suite run), 2026-07-18 (fix-company-create-rls-atomic.md gate — isolated re-run also failed, same error). Root-caused and fixed 2026-07-19 (`specs/fix-e3-create-vendor-ci.md`).

## Pnd50 ladder test fails "DisallowedExpenses expected 1000, found 1123" — residue from CitYearDataServiceTests, not your diff
Symptom: `Pnd50FilingServiceTests.Pnd50_with_nonzero_adjustments_renders_the_ladder_in_v2` fails in full-suite/class runs with found = expected + 123; passes when run alone.
Root cause: `CitYearDataServiceTests.Adjustments_are_invisible_to_another_tenant` creates a 123m CIT adjustment for company 1 in `TestIds.FutureFiscalYear()` and (pre-2026-07-18) never deleted it — permanent residue in teas_test. The Pnd50 test's `FreshJeYearAsync` freshness check only looks at JOURNAL ENTRIES, so it can pick the residue year; ladder DisallowedExpenses = sum of positive adjustments → 1,000 (its own) + 123 (residue).
Fix: cleanup added to the tenant-isolation test (delete adjustment in a company-1 scope after assertions, 2026-07-18). If it resurfaces: delete stray rows `tax.cit_adjustments` amount=123 note='tenant-1 only' from teas_test, and check FreshJeYearAsync collisions.
Seen: 2026-07-18 fix-vat-round-findings gate (Codex full run + isolated class rerun) — initially misattributed to the diff; single-test run with the diff proved the code clean.

## SqlScript with cross-company INSERT/UPDATE on G1 tables dies 42501 on prod boot (v1.22.0 deploy, 2026-07-18)
Symptom: deploy fails at startup with `42501: new row violates row-level security policy for table "chart_of_accounts"` from a new SqlScript; teas_test/dev never showed it.
Root cause: DbInitializer runs SqlScripts over the APP connection (`teas`, NOBYPASSRLS) and G1 business tables carry FORCE ROW LEVEL SECURITY — a script INSERTing/UPDATEing rows across companies with no `app.company_id` pinned is blocked. Tests run as superuser → invisible (same class as the "seed 42501" note in rls-masked-by-superuser-tests memory). Spec claims like "runs as superuser at startup" are WRONG for prod.
Fix pattern: wrap per-company DML in a `DO $$` block that loops companies and `PERFORM set_config('app.company_id', c.company_id::text, true)` before each company's DML (transaction-local, auto-reverts) — see `625_seed_cogs_account.sql` v1.22.1 or `621_seed_fixed_asset_accounts.sql` (the canonical reference, `WHERE NOT EXISTS` + `ON CONFLICT DO NOTHING` for idempotency). DDL (ALTER TABLE ADD COLUMN) is unaffected (624 applied fine).
Recovery note: a failed script is NOT recorded in sys.applied_sql_scripts → it retries on next boot; earlier scripts in the same release that succeeded ARE recorded and skip. Deploy auto-rollback restores binaries; additive columns left behind are harmless.
Seen: 2026-07-18 v1.22.0 deploy (rolled back clean, prod stayed on v1.21.6; 624 applied, 625 fixed → v1.22.1). Recurred 2026-07-28 v1.24.0 deploy: `630_seed_payroll_other_deductions_account.sql` shipped with the same bare `INSERT ... SELECT ... FROM master.companies CROSS JOIN (VALUES ...)` shape (no `app.company_id` pinned) — same 42501, same clean auto-rollback. Rewritten to mirror 621's `DO $do$` per-company loop exactly.

## Running the FE deploy overlay script under `sudo` chowns the whole source tree to root, breaking the NEXT deploy (v1.22.4 deploy, 2026-07-19)
Symptom: `pm2 jlist` inside the deploy script returns empty (`FAIL pm2_status=`) even though the site is actually up and `login=200`/`public_pdf=404` both pass — script reports `FE_DEPLOY_FAILED` and rolls back a build that actually succeeded. On the NEXT run (correctly, without sudo) `tar xf ... --strip-components=1` then fails with a wall of `tar: <path>: Cannot open: File exists` / `Cannot utime: Operation not permitted` for every top-level source directory (`app/`, `components/`, `e2e/`, `hooks/`, `i18n/`, `lib/`, `manual/`, `messages/`, `public/`, `screenshots/`).
Root cause: `teas-web` (and `teas-api`) run under PM2 as the `ubuntu` user, with `PM2_HOME` scoped to that user — a separate daemon/process list from root's. Invoking the deploy script with `sudo bash deploy-fe-vXXXX.sh` runs `pm2 restart`/`pm2 jlist` against ROOT's (empty) pm2 daemon, not the real one, so the status probe always reads blank regardless of the actual site state. Worse: the `tar xf` overlay step in the SAME sudo run extracts as root, and GNU tar run as root restores the archive members' recorded uid/gid (0/0 from `git archive`) onto every extracted directory — flipping ownership of the whole frontend source tree from `ubuntu:ubuntu` to `root:root` (mode stays `775`, but `ubuntu` isn't in group `root` so it loses write access). The NEXT deploy attempt, run correctly as plain `ubuntu`, then can't overwrite/delete any file inside those directories.
Fix: never `sudo` the FE (or API) deploy script — run it as the plain `ubuntu` user the whole way (`ssh ... "bash /tmp/deploy-fe-vXXXX.sh"`, no `sudo`); pm2 already runs under that user and the directory is already `ubuntu`-writable. If a sudo run already happened, recover with a targeted `sudo chown -R ubuntu:ubuntu` on just the directories the archive touched (list them from `tar tf` or `git ls-tree`) — NOT a blanket recursive chown of the whole frontend dir, since a few unrelated files (e.g. `.env.local.example`) are intentionally root-owned and predate any deploy.
Seen: 2026-07-19 v1.22.4 FE-only deploy (i18n `common.deleted` key fix); recovered same session, redeployed clean as `ubuntu`.

## Swarm script login() 30s `waitForURL` timeout against PROD (cold cache), even though the login itself succeeded
Symptom: a Playwright swarm script copying `frontend/e2e/_helpers.ts`'s `login()` pattern (`page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 30_000 })`) throws `page.waitForURL: Timeout 30000ms exceeded` against `https://teas.kazaki-rio.com` — but the `/api/auth/login` POST already returned 200 and the `access_token` cookie was already set (confirmed via `page.context().cookies()`).
Root cause: the login page's `onSubmit` does a CLIENT-SIDE `router.push(safeReturnTo())` (SPA nav, no full page load) into the dashboard shell, which itself triggers a burst of ~20 Next.js `_next/static/chunks/.../*.js` prefetch fetches for every sidebar nav route (`(dashboard)/purchase-orders`, `reports/*`, `settings/*`, etc.) plus `/api/proxy/me`, `/me/permissions`, `/system/info`, etc. — all fired before the URL-changed navigation event Playwright is waiting on gets flushed. `_helpers.ts`'s 30s budget is tuned for localhost dev (`baseURL: http://localhost:3000`, warm dev-server cache); against prod's public domain, a cold hit (nothing yet cached at the edge/CDN or in the app's per-process module cache) for that whole chunk burst can exceed 30s even though the mutation and eventual navigation both complete fine.
Fix: bump the swarm script's login `waitForURL` timeout to 60s (or reuse `waitForNavGates` — the `nav-gates-ready` sentinel — as the real completion signal instead of the URL alone). Not a product bug: a retried run against the now-warm cache completed the same nav in under 15s.
Seen: 2026-07-21 UX SWARM round5, purch01 leg (`swarm5-purch01.mjs`, prod v1.22.9) — first login attempt timed out at 30s, identical script succeeded reliably at 60s on the next 2 runs.

## Swarm script `fullPage` screenshots / element waits silently miss content below ~900px on the dashboard shell
Symptom: a Playwright swarm script against `https://teas.kazaki-rio.com` with the common `{width:1440, height:900}` viewport takes a `page.screenshot({fullPage:true})` on a long create form (e.g. `/vendor-invoices/new`) and gets back an image that just stops after section 2 of 3 — the line-items section (with its `expense-category-select` testid) is nowhere in the captured image, and a `locator.selectOption()` against it either times out or silently no-ops depending on how the surrounding code checks for it first.
Root cause: the dashboard shell's scrollable region is the `<main className="flex-1 overflow-y-auto ...">` element, NOT the page `<body>` — `body`/`html` never grow taller than the viewport. Playwright's `fullPage:true` screenshot walks `document.body`'s scroll height, which stays capped at the viewport height in this layout, so anything requiring scroll *inside* `<main>` is simply cropped out of both the screenshot AND (more dangerously) out of a naive `.count()`/`.click()` check made without first waiting for the element — a short viewport makes an off-screen-but-present element indistinguishable from a not-yet-rendered one when a soft guard like `if (await el.count())` is used instead of an explicit `.waitFor()`.
Fix: use a tall viewport for any swarm/army script driving these multi-section create forms — `{width:1440, height:2200}` comfortably fits every form seen so far (quotation/PO/VI/PV/expense-claim) without any inner scrolling ever triggering, which also sidesteps the fullPage-screenshot crop entirely.
Seen: 2026-07-25, army B2-nv leg — cost 2 wasted script iterations before the cause was found (a `vendor-invoices/new` screenshot that looked like the line-items section had vanished, plus an `expense-category-select` selection that silently no-op'd because the soft `if (count())` guard read 0 on the still-loading page).

## Swarm script soft `if (await button.count())` guard around a lifecycle action silently no-ops, and the log line right after still fires
Symptom: a Playwright swarm script clicks a lifecycle testid button (`vi-post`, `bn-issue-action`, ...) wrapped as `if (await btn.count()) { await btn.click(); await confirmModal(page); }`, then unconditionally logs success (e.g. `log('VI posted', id)`) — the log looks clean, but re-checking the document via the API afterward shows it never actually left Draft.
Root cause: right after a `page.goto()` to a detail page, the target button may not have rendered yet (React Query fetch still in flight) — `locator.count()` on a not-yet-rendered element returns `0` immediately (it does NOT wait), so the whole `if` block is skipped with no error, and any log statement placed after the block (not inside it) fires regardless of whether the click happened. This bit the same script twice in one run: once for a VI `Post` button, once for a BillingNote `Issue` button — both left the document silently stuck in Draft while the run log claimed success.
Fix: never gate a lifecycle click on a soft `count()` check — use `await btn.waitFor({state:'visible', timeout: 15_000})` (a HARD assert) before clicking, and always confirm the actual state transition afterward with a fresh API GET (`status === 'Posted'`), not just a screenshot or a log line that fires unconditionally.
Seen: 2026-07-25, army B2-nv leg (prod v1.22.11) — cost 2 extra follow-up script runs to catch (a VI stuck Draft for one run, then a BillingNote stuck Draft for another) before switching every lifecycle click in the script to the hard-assert pattern.

## TB "as of today" ไม่เห็น depreciation JE ของเดือนปัจจุบัน
- Symptom: post ค่าเสื่อมแล้ว TB ไม่ขยับ → คิดว่า post fail. Root cause: dep JE ลงวันที่ month-END เสมอ (runDate = วันสุดท้ายของเดือน) ส่วน TB default as-of = วันนี้ → JE โดน date-filter ออก (ถูกต้อง ไม่ใช่ bug). Fix: เช็ค TB ด้วย asOf = วันที่ของ JE เอง (เช่น 2026-07-31). พบใน army B-fa 2026-07-22.

## Super-admin cross-company write 500s under RLS — service method never re-pins app.company_id (v1.22.10, WP-E2, 2026-07-25)
Symptom: `PUT /companies/{id}` (super-admin editing a DIFFERENT company than their own) 500s only when the request changes a tax field (VatRegistered/VatRate/Pnd30SubmissionMode); a same-company edit or a non-tax-field edit works fine. teas_test's normal (superuser) connection never shows it — same class as `rls-masked-by-superuser-tests` memory and the "SqlScript cross-company 42501" entry above, but the TRIGGER here is application code, not a migration script.
Root cause: `TenantMiddleware` pins `app.company_id` SESSION-scoped to the CALLER's own company for the whole request (by design — RLS defense-in-depth). `CompanyService.UpdateAsync` conditionally calls `IActivityRecorder.Record(...)` when a tax field changes, queuing an `audit.activity_log` insert with `company_id = <the row being edited>`. `audit.activity_log` carries `FORCE ROW LEVEL SECURITY` (600_superadmin_scoped_rls.sql G3: `company_id = current_setting('app.company_id') OR company_id IS NULL OR app.bypass_rls`) — for a super-admin editing ANOTHER company, the queued row's `company_id` mismatches the session's pinned value, so the INSERT's implicit `WITH CHECK` 42501s inside `SaveChangesAsync`, rolling back the WHOLE update (the field flip never lands either) and surfacing as an unhandled `DbUpdateException` → generic 500. `CompanyService.CreateAsync` already had the fix for its OWN writes (4b92efd, 2026-07-18: wraps in a transaction + `set_config('app.company_id', <new company>, true)` LOCAL before seeding) — `UpdateAsync` was never given the same treatment.
Fix pattern: any service method that (a) can be invoked by a super-admin for a company OTHER than their pinned tenant AND (b) writes to a G1/G2/G3 (RLS-forced) table for that OTHER company's id must wrap the write in `BeginTransactionAsync` + `SELECT set_config('app.company_id', {targetCompanyId}, true)` (LOCAL — auto-reverts at commit/rollback, never leaks onto the pooled connection) before `SaveChangesAsync`. Grep every `ICompanyService`/other super-admin-scoped service for a bare `SaveChangesAsync` with no such pin before assuming it's safe.
Test technique to actually catch this (teas_test's default connection bypasses RLS): reproduce with the `pg_database_owner` trick already proven in `CompanyCreateRlsTests` — `SET ROLE pg_database_owner` + `set_config('app.company_id', <CALLER's own company>, false)` SESSION-scoped, then call the service directly for a DIFFERENT target company id. Must use the REAL `ActivityRecorder` (not a `NoopActivityRecorder` stub) — a no-op recorder never queues the write and silently defeats the whole repro (cost one wasted test iteration this session).
Seen: 2026-07-25, WP-E2 (`specs/fix-army-findings-2026-07-22.md`) — live repro co6 (company id=6), PUT vatRegistered flip 500'd twice on prod.

## เทสเทียบ `DateTime.UtcNow` กับ validator ที่ pin Bangkok → เขียวก่อนเที่ยงคืน แดง 00:00–07:00 ICT
- **Symptom:** เทสที่สร้างเอกสารผ่าน API/MCP ผ่านตอนเย็น แล้ว **fail เองตอนกลางคืน** โดยไม่มีใครแก้โค้ด ·
  error คือ validation ปฏิเสธ `DocDate` (เช่น `pv.docdate_not_today` / `validation.docDateNotToday`)
  ทั้งที่เทสส่ง "วันนี้" มาแล้ว
- **Root cause:** เทสคำนวณวันด้วย `DateOnly.FromDateTime(DateTime.UtcNow)` = **UTC today** แต่ validator
  (และ §10 ทั้งระบบ) เทียบกับ `SystemClock().TodayInBangkok()` = **UTC+7** · ระหว่าง 00:00–07:00 ICT
  UTC ยังเป็นวันก่อนหน้า → ตัวเลขวันไม่ตรงกัน 7 ชั่วโมงต่อวัน · ก่อนเที่ยงคืน ICT ทั้งสองค่าเท่ากัน
  จึงเขียวสนิทและมองไม่เห็นบั๊ก
- **Fix:** ในเทสใช้ `new Accounting.Application.Abstractions.SystemClock().TodayInBangkok()` เสมอเมื่อค่านั้น
  จะถูกเทียบกับกฎฝั่ง server · **ห้ามใช้ `DateTime.UtcNow`/`DateTime.Today` สำหรับวันที่เอกสาร**
- **Seen:** 2026-07-26 ~01:3x — เทส `McpServerSmokeTests` 4 ตัว (`E3_payment_voucher_*` +
  `E3_create_payment_voucher_draft_returns_id_and_approval_url`) พังหลังข้ามเที่ยงคืนหลังจาก O13
  เพิ่ม validator บังคับ DocDate=วันนี้ · worker วินิจฉัยผิดว่าเป็น regression ของ commit เก่า (`e17d232`)
  — Fable ไล่เองจึงเจอว่าเป็น timezone · **ไฟล์เดียวกันยังมี `UtcNow` เหลืออีก ~22 จุด**สำหรับเอกสาร
  ประเภทที่ service ยังไม่มี validator บังคับวันที่ (จึงยังไม่พัง) — ถ้าเพิ่ม validator ให้เอกสารประเภทใด
  ต้องกวาด `UtcNow` ของเทสประเภทนั้นด้วย ไม่งั้นจะระเบิดตอนกลางคืนแบบเดียวกัน
- **บทเรียนซ้อน:** ต้นเหตุที่มันกลับมาเป็น `UtcNow` คือคำสั่ง revert แบบเหวี่ยง ("revert ทั้ง 7 ไฟล์")
  ที่ทับของที่เคยแก้ถูกไว้แล้ว → revert แบบกวาดต้องดูก่อนว่าไฟล์นั้นมีของดีที่จะหายไปด้วยหรือเปล่า

## `dotnet build` fails silently with 0 warnings and 0 errors in the Windows sandbox
- **Symptom:** solution or project build exits 1 after a few seconds and prints only `Build FAILED. 0 Warning(s), 0 Error(s)`; diagnostic output stops in an MSBuild child graph task.
- **Root cause:** parallel MSBuild project/restore graph execution cannot start reliably in the restricted Windows worker, so the parent task reports failure without a compiler diagnostic.
- **Fix:** serialize the existing restored graph: `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false`. This builds every backend and test project and surfaces real compiler errors normally.
- **Seen:** 2026-07-26, O10-A payroll deductions; the serialized build completed with 0 warnings and 0 errors.

## Re-rendering the same official RD PDF is not byte-deterministic
- **Symptom:** two ภ.ง.ด.1/1ก PDFs built from unchanged filing values differ at binary offsets, even after normalizing creation timestamps and trailer document IDs.
- **Root cause:** the existing PDFsharp-based official-form render/flatten pipeline rewrites compressed PDF objects nondeterministically; this is renderer noise, not a filing-data difference.
- **Fix:** for regression tests unrelated to PDF serialization, compare extracted page text exactly (page-for-page) and keep literal byte equality for deterministic formats such as the สปส.1-10 fixed-width upload file. Making PDF bytes reproducible requires a separately-scoped form-renderer change.
- **Seen:** 2026-07-26, O10-A D2 filing-isolation regression.

## PND50 tests intermittently fail from stale CIT data but pass in isolation
- **Symptom:** PND50 tests fail with `pnd50.override_breaks_ladder`, or the C/D schedule test expects entertainment disallowance 2,500 but reads an accumulated 10,000; isolated runs usually pass.
- **Root cause:** `CitExpenseByAccountTests.FreshJeYearAsync` considered only journal entries when choosing a random year. It could reuse a year carrying a leftover or in-flight `CitYearSummary.OverrideNetProfit` or `CitAdjustment` written by another CIT test.
- **Fix:** a fresh CIT test year must have no journal entries, `CitYearSummary`, or `CitAdjustment` for the same fiscal year. Keep the checks in `FreshJeYearAsync` so every caller benefits; do not delete shared fixture rows as a workaround.
- **Seen:** 2026-07-26, O10-A full-suite follow-up (964 passed / 2 failed / 8 skipped; both failures were this race).

## PDF text extraction drops Thai combining marks — assertions on Thai labels fail spuriously
- **Symptom:** a test asserts a Thai string appears in a generated PDF and fails, while the printed PDF is visibly correct. The extracted text shows the same words with tone marks/vowels missing: `รายการหักอื่น ๆ` comes out `รายการหักอื น ๆ`, `ที่จ่าย` as `ที จ่าย`, `ตั้งแต่ต้นปี` as `ตั งแต่ต้นป`.
- **Root cause:** the `PdfText` extraction helper loses Thai combining characters (MAI EK and friends). The renderer is fine — only the extraction is lossy. This is separate from, and additional to, the "PDF bytes are not deterministic" entry above.
- **Fix:** never assert on Thai text containing tone marks. Assert on a substring that has none — for a labelled amount, the user-supplied value or reason is usually mark-free — or assert on the numeric amount.
- **Seen:** 2026-07-26, O10-B payslip deduction-reason label (`Deduction_changes_net_only_rolls_up_and_posts_balanced_credit_2180`).
- **Escalation (2026-07-30, doc-signature spec WP-3 T9):** the dropped-mark ARTIFACT itself is
  not even stable run-to-run for the IDENTICAL renderer + IDENTICAL input. Rendering the exact
  same Draft Tax Invoice 3× and diffing the (PdfPig-)extracted text byte-for-byte found one run
  emit a plain space (U+0020) where a dropped tone/vowel mark left a gap, and another emit a
  literal NUL (U+0000) at the SAME character position. A test that pins an exact-equality
  baseline string containing Thai tone marks (even one captured from a real, correct render) WILL
  flake later for a reason that has nothing to do with a real regression. Fix used: before any
  equality comparison, strip every Unicode nonspacing-mark (category Mn) character AND every
  control character (`char.IsControl`), then collapse whitespace runs
  (`Regex.Replace(s, @"\s+", " ")`) — confirmed stable across 3 consecutive runs after this
  normalization. If you need an exact-text styling-freeze test on a Thai-laden PDF page (not just
  "assert a mark-free substring", but a genuine page-for-page equality pin), THIS normalization
  is the fix, not merely avoiding Thai substrings.

## Random-id test isolation collides as teas_test grows
- **Symptom:** a test fails with `23505: duplicate key value violates unique constraint "pk_companies"` (or a similar random-key clash) and passes on a standalone re-run. Seen on `SalesUxFixesWpATests.Quotation_send_called_twice_second_call_rejected_no_duplicate_number` with `company_id=731031`.
- **Root cause:** tests that mint an id from a random draw collide more and more often as `teas_test` accumulates rows (it already carries hundreds of companies). Same family as the `FreshJeYearAsync` race documented above — a random pick with an insufficient uniqueness check.
- **Fix:** before blaming a diff, re-run the single test; a standalone pass means collision, not regression. The durable fix is to reserve the id atomically (insert-and-retry, or a sequence) instead of drawing a random one and hoping.
- **Seen:** 2026-07-26, O10-B full-suite run (966 passed / 2 failed / 8 skipped; neither failure was caused by the diff).

## Thai text written through the API from PowerShell silently becomes `?`
- **Symptom:** Thai fields created by a script land in the database as literal question marks (`???? ?????`). The API returned 200, nothing errored, and the damage only shows up later — e.g. an employee's name printing as `????` on ภ.ง.ด.1 and สปส.1-10.
- **Root cause:** PowerShell's default output encoding degrades non-ASCII to `?` before the request leaves the client. The API stores exactly what it received; the application itself round-trips Thai correctly (co6's UI-created employees are intact at 3 bytes per character, co7's PowerShell-created ones are 1 byte per character).
- **Fix:** create Thai data through the UI, or force UTF-8 on the client before calling the API. Verify immediately with `octet_length(col)` — a Thai string measuring one byte per character is corrupt. Appearance alone will not tell you: `?` looks like a deliberate placeholder.
- **Seen:** 2026-07-26 (co7's three O8 test employees, discovered 2026-07-29 during v1.24.1 live verification). A subagent inspecting the same rows through the UI concluded "placeholder text, not a bug" — the byte length is what settles it.

## `corepack pnpm lint` (gate 9) hangs on an interactive ESLint setup wizard — no config was ever committed
- **Symptom:** `corepack pnpm lint` (→ `next lint`) prints `? How would you like to configure ESLint? … Strict (recommended) / Base / Cancel` and blocks forever (non-interactive shells see it exit 1 with `ELIFECYCLE Command failed with exit code 1` immediately since stdin is closed).
- **Root cause:** `frontend/` has no ESLint config file at all (no `.eslintrc*`, no `eslint.config.*`) and never has — `git log --all` on those paths returns nothing. `next lint` only auto-detects an existing config; with none present it always falls back to the first-run interactive wizard, in every shell, regardless of the diff being verified.
- **Fix:** this is a pre-existing repo gap, not something a feature diff introduces or can fix within its own blast radius (adding a config is a separate, deliberate change — it may also pull in the `eslint-config-next` dependency, which is out of scope for a feature dispatch). Do not spend time debugging your own diff over this. Report gate 9 as blocked-by-environment with this entry cited, run `tsc --noEmit` (gate 8) and `next build` as the load-bearing FE build-health checks instead, and flag to the orchestrator that someone should either commit a real ESLint config or drop `pnpm lint` from the gate list.
- **Seen:** 2026-07-29, WP-FE (specs/manual-jv-and-coa-management.md) — first time gate 9 was actually executed rather than assumed green.

## WHT-type FormType validator/UI still rejects PND54 (FOR-SVC/FOR-ROYAL uneditable)
- **Symptom:** the `CreateWhtType`/`UpdateWhtType` validators (`Application/Tax/WhtTypeDtos.cs`) and
  the settings `FORMS` array (`frontend/app/(dashboard)/settings/wht-types/page.tsx`) accept
  PND1/PND2/PND3/PND53 but not PND54 — the seeded `FOR-SVC`/`FOR-ROYAL` (foreign ม.70) rows 400 on
  save through the WHT-types UI, same failure mode F3 fixed for PND2/INT-IND.
- **Root cause:** pre-existing gap, not introduced by the ภ.ง.ด.2 work — PND54 was never added when
  Sprint 9 C1 introduced the foreign-payee form type. Tier-2 review (specs/pnd2-filing.md §10, F3)
  explicitly scoped fixing PND2 only and flagged PND54 as a separate, deliberately-deferred finding.
- **Fix:** not fixed here. A future worker: add `"PND54"` to both validators' `Must(...)` predicates
  and to the FE `FORMS` array, mirroring the PND2 fix exactly.
- **Seen:** 2026-07-29, ภ.ง.ด.2 filing Tier-2 remediation (specs/pnd2-filing.md §10 F3).

## FinalizeAsync never checks res.Submitted/res.Error before recording a filing as "Submitted" (real RD client)
- **Symptom:** none observed yet on prod (pre-existing debt, not a live incident) — flagged during
  Tier-2 review of the ภ.ง.ด.2 work (specs/pnd2-filing.md §10, N1). `TaxFilingStore.FinalizeAsync`
  (`Infrastructure/TaxFilings/TaxFilingStore.cs:50-66`) calls `SubmitAsync` when a real
  `IRdEfilingClient` is wired and an auto-mode company finalizes ภ.พ.30 / ภ.ง.ด.3/53/54/36, but
  never inspects `res.Submitted` or `res.Error` before writing `Status = FinalStatus(submissionMode)`
  (`"Submitted"` for auto) / `SubmittedAt = now`. If a genuine RD transport failure returns
  `Submitted:false` with a populated `Error`, the filing still gets recorded as `"Submitted"` with
  whatever ack/submission id came back (possibly blank) — a real network/API failure would be
  silently recorded as a success, and the row is then immutable (`already_finalized` guard blocks
  any retry).
- **Root cause:** the same class of bug N1 found for ภ.ง.ด.2 (an unrecognised form silently faking
  a submitted result), but here the RD *client itself* fails honestly (correctly sets
  `Submitted:false`) and `FinalizeAsync` still doesn't act on it — the gap is in the caller, not
  the client.
- **Fix sketch (NOT applied this release — shipped behaviour, needs its own decision):** after
  `var res = await SubmitAsync(...)`, branch on `res.Submitted`: if false, either (a) throw a
  `DomainException("tax_filing.rd_submission_failed", res.Error)` before the `db.TaxFilings.Add`
  so nothing is persisted and the caller can retry, or (b) persist a distinct status (e.g.
  `"SubmitFailed"`) instead of silently mapping to `"Submitted"`. Needs a decision on whether a
  failed auto-submit should block finalize entirely or land in a retryable non-terminal state —
  out of scope for this dispatch (money/compliance semantics change, needs its own spec).
- **Seen:** 2026-07-29, ภ.ง.ด.2 filing Tier-2 round 2 review (specs/pnd2-filing.md §10, N1
  follow-up finding, Fable-verified in code, deliberately not fixed this release).

## FirstRunBootstrapTests.DropDbAsync races Postgres autovacuum on a full-suite run — `42501: permission denied to terminate autovacuum worker`
- **Symptom:** a full-suite run occasionally fails `FirstRunBootstrapTests` (its scratch-DB
  `DropDbAsync` teardown) with a raw `Npgsql.PostgresException 42501: permission denied to
  terminate autovacuum worker process`. A standalone rerun of just that test passes.
- **Root cause:** `DROP DATABASE` needs to terminate any backend connected to the target DB first;
  if Postgres' autovacuum worker happens to be attached to that scratch DB at the exact moment of
  the drop, the drop's connection-termination step tries to kill the autovacuum worker and is
  denied (autovacuum workers aren't killable by an ordinary role) — a timing race, not a logic bug
  in the test or the drop helper.
- **Fix:** treat as a known flake — rerun the single test once before escalating; do not chase it
  as a regression from an unrelated diff. Confirmed one such flake (1 failure) alongside 1026
  passing in the 2026-07-29 post-pnd2-filing full-suite run.
- **Seen:** 2026-07-29, full-suite run after WP-A/WP-B/Tier-2 remediation (specs/pnd2-filing.md).

## Two DIFFERENT permission checks in this repo: JWT-claims-only vs a fresh DB lookup — an HTTP RBAC test that mints a synthetic token can accidentally test the wrong one
- **Symptom:** writing an HTTP-level test for a permission-gated route, it's tempting to assume
  "mint a JWT with `Permissions: [...]`" is always enough to prove ALLOW/DENY. For most routes
  that's true — but for a route whose gate is decided at runtime from `parent_type`/similar (the
  generic `POST /attachments`'s `ParentGuard`, `AttachmentEndpoints.cs`), a synthetic JWT claim is
  NOT enough and a test that only checks JWT claims proves nothing about that gate.
- **Root cause:** `PermissionHandler` (`Api/Authorization/PermissionRequirement.cs`) — the
  mechanism behind every static `.RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + X)`
  — checks ONLY the JWT's own `TenantClaims.Permission` claims (or super-admin/API-key-scope), a
  fast in-memory check with **zero DB reads**. But `ParentGuard`'s permission requirement is
  resolved dynamically per-request (`svc.ParentReadPermission(parentType)`) and can't be a static
  policy, so it instead calls `IPermissionLookup.LoadAsync(tenant.UserId, tenant.CompanyId, ct)` —
  a **fresh, real DB query** against `sys.user_roles`/`sys.role_permissions`, completely ignoring
  whatever the JWT's `Permissions` claims say. A test minting a JWT with a fake `UserId` and a
  synthetic `sys.user.manage` claim will pass any STATIC policy gate but will always be DENIED by
  `ParentGuard` (the fake UserId has zero real `user_roles` rows) — and conversely, a synthetic
  claim alone can never produce an ALLOW through `ParentGuard`, no matter what the JWT says.
- **Fix:** for a STATIC-policy route (the vast majority — profile/signature/stamp admin routes in
  the doc-signature spec, etc.), a synthetic JWT with the right `Permissions` claim is sufficient —
  no real DB role/grant needed. For a route gated by a DYNAMIC per-parent-type lookup (only
  `POST`/`GET /attachments` today), the test needs a REAL `sys.users` + `sys.user_roles` row (e.g.
  via `TestCompanyFactory.CreateAsync` + a direct `UserRole` insert) for the ALLOW case; the DENY
  case can still use a synthetic/nonexistent UserId (empty permissions is the natural "no grants"
  result of a lookup that finds no rows).

## An application-level "only resolve cross-company data when target==session company" guard can't be RED-proven via the DI test harness — EF's own global query filter already narrows it
- **Symptom:** adding a defensive guard like `target == tenant.CompanyId ? <resolve> : null` around
  a read of an `ITenantOwned` entity (e.g. `sys.attachments`), then trying to RED-check it by
  temporarily bypassing the guard — the test stays GREEN even with the guard removed, as if the
  fix did nothing.
- **Root cause:** `AccountingDbContext` attaches a GLOBAL EF Core query filter to every
  `ITenantOwned` entity (`HasQueryFilter(e => _tenant == null || e.CompanyId == _tenant.CompanyId)`,
  §1.4 of `specs/doc-signature-and-foot-layout.md`). This filter is pure C#/LINQ, translated to a
  SQL `WHERE`, and is COMPLETELY INDEPENDENT of Postgres RLS — it always narrows to the CURRENT
  `ITenantContext.CompanyId` (the session's own company), regardless of any `companyId` query
  parameter or `target` variable computed elsewhere in the method. So when a super-admin session
  (own company A) lists a DIFFERENT company B's data via an explicit `companyId=B` param, ANY plain
  `db.SomeTenantOwnedSet.Where(...)` read is ALREADY silently scoped back to company A by this
  filter alone — an explicit `target == tenant.CompanyId` guard around that same read is REAL,
  correct, self-documenting defense-in-depth, but is NOT independently observable through the normal
  DI/EF path: removing the guard doesn't change the query's result, because the EF filter was
  already doing the equivalent narrowing underneath it. Don't mistake "the RED-check didn't fire"
  for "the fix does nothing" — it means the fix is layered on top of an already-present control,
  not that it's dead code. (The ONLY way to see the guard's own value in isolation is
  `IgnoreQueryFilters()`, which the real code path never calls — so there's no honest way to
  functionally RED-test this class of guard through the standard test harness.)
- **Fix:** don't burn a cycle trying to force a RED here. Verify by reading the code (the guard is
  simple enough to eyeball) and keep a GREEN pinning test that confirms the CORRECT final
  behaviour (null/absent for the cross-company case) — that's still a real regression net even
  though it can't distinguish "guard present" from "guard absent" in this harness.
- **Seen:** 2026-07-30, doc-signature-and-foot-layout spec §16 F4 remediation
  (`RbacAdminService.ListUsersAsync`'s `SignatureUrl` cross-company guard).
- **Seen:** 2026-07-29/30, doc-signature-and-foot-layout spec WP-2 (T11/T12 — `ParentGuard` 403 vs
  the new `/admin/rbac/users/{id}/signature` + `/profile` + `/company-profile/stamp` routes, which
  are static-policy and needed no real DB grant at all).

## `PUT /company-profile/soft`'s docs claim "omitted fields are unchanged" for EVERY field, but the handler whole-overwrites ALL of them (not just the newer jsonb one)
- **Symptom:** while documenting `defaultDocNotes`'s whole-overwrite-on-omission behaviour
  (Tier-2 finding, doc-signature spec), re-reading `CompanyProfileService.UpdateSoftAsync`
  showed it does NOT implement partial-patch semantics for ANY of the 9 pre-existing soft
  fields either (`e.TradeName = req.TradeName; e.LogoUrl = req.LogoUrl; …` — every field is
  assigned unconditionally from the request DTO, with no "if null, keep existing" branch
  anywhere). `docs/api/openapi.yaml`'s `CompanyProfileSoftUpdate` description ("All optional —
  omitted fields are unchanged") and `docs/manual/api/master-data.md`'s phrasing both describe
  the INTENDED contract, not the actual implementation — a client that PUTs only `{tradeName:
  "..."}` will silently null out `logoUrl`, `phone`, `email`, etc. if its own state doesn't
  already carry them (whatever `UpdateCompanyProfileSoftRequest` deserializes to when a JSON
  property is omitted — likely `null` for these `string?` positional-record parameters with no
  default value).
- **Root cause:** the service method was written as a full-replace (mirrors the wide-flat-table
  soft-field pattern elsewhere in this repo) while the two docs describe a PATCH contract. This
  predates the doc-signature spec — `defaultDocNotes` just inherited the SAME (undocumented)
  behaviour, which is why the Tier-2 finding caught it only for the new field.
- **Fix:** NOT applied this round — out of the Tier-2 finding's scope (which asked only to
  document `defaultDocNotes`'s whole-overwrite semantics, explicitly keeping the existing
  convention). Flagging so a future worker doesn't assume the OTHER 9 fields are safe to omit
  in a partial update — they are not, today. Needs its own decision: either (a) fix the docs to
  describe the ACTUAL whole-overwrite contract for every field (cheapest, no behavior change),
  or (b) change `UpdateSoftAsync` to a real partial patch (bigger, a genuine behavior change,
  needs its own spec/review).
- **Seen:** 2026-07-30, doc-signature-and-foot-layout spec Tier-2 finding #4 remediation.

## PDF acceptance in the browser sandbox: blob: tabs are not screenshot-able
- **Symptom**: Tier-4 E2E cannot screenshot a rendered PDF — `blob:` URL tabs and chrome's PDF
  viewer return blank/unsupported captures.
- **Fix that works** (2026-07-30): `javascript_tool` → `fetch('/api/proxy/.../pdf')` + anchor-click
  download → GUID-named `.tmp` (valid PDF bytes) lands in `C:\Users\ham_c\Downloads` → copy+rename
  to `.pdf` → the Read tool renders it inline, images included. Assert on that.

## release-please: merging the release PR too early ships a stale version label
- **Symptom**: tag came out v1.26.1 for a `feat:` push that should have minted v1.27.0.
- **Root cause**: the release PR pre-existed (created from an earlier `fix:` commit); the
  wait-loop merged it the moment it matched "release", BEFORE release-please's run on the new
  feat commit updated the PR's version. Content was complete; only the label was stale.
- **Fix**: before `gh pr merge`, check the PR TITLE version matches the expected bump for the
  commits being released; if not, wait for the release-please run on the latest commit to
  update the PR first.

## Swarm/test account passwords live in `swarm-findings/*/` — search ALL of it before declaring "creds lost"
**Symptom:** a dispatch needs a prod test account (co5 UX-swarm roles, co6/co7 non-VAT users) and no
password is findable, so the work gets declared blocked and parked for a human.

**Root cause:** the scripts that created/used these accounts delete themselves after a run (the army
legs' own hard rules), so the credentials survive only as prose inside whichever findings report
happened to quote them. They are scattered across sibling directories — `swarm-findings/army/`,
`swarm-findings/round3..5/`, `swarm-findings/v1241/`, `swarm-findings/breakit-v1271/` — and a search
of only one or two of those comes up empty and looks authoritative.

**Fix / what to do:** grep the WHOLE `swarm-findings/` tree (plus `specs/`) before concluding anything
is lost. On 2026-07-31 a co7 blocker that stalled 4 agents was resolved in ~2 minutes this way: the
creds were in `swarm-findings/v1241/legF-jv-prod.md:4` all along; the first sweep had covered
`army/` + `specs/` and skipped `v1241/`.

**The conventions (verified live against prod 2026-07-31):**
- co5 UX-swarm roles: `UxSwarm-2026-<SLOT>` where SLOT is the **agent-slot code, not the role name** —
  sales01=A1 · acct01=A2 · appr01=A3 · ap01=A4 · ar01=A5 · audit01=A6 · chief01=A7 · admin01=A8 ·
  purch01=A9 · tax01=B1. (`UxSwarm-2026-chief` is WRONG and 401s — this cost 3 agents a retry.)
- co6/co7 non-VAT users: `UxSwarm-2026-NV<n>` — **nvadmin02=NV4 · nvchief02=NV5**.
- Seeded admin (local/dev): `admin` / `Admin@1234` (per `130_seed_admin_and_customer.sql`).

**Prevention:** a dispatch that mints or uses a test account should state the credential convention in
its own findings report, so the next round finds it by grep instead of by archaeology.

## `next build` fails "Failed to fetch font file from fonts.gstatic.com" / "`next/font` error: Failed to fetch `Noto Sans Thai`"
- **Symptom:** `npx next build` fails at the webpack compile step with repeated `Failed to fetch font
  file from https://fonts.gstatic.com/...woff2` retries (3x) then `Failed to compile` — even when the
  diff never touched `app/layout.tsx` or anything font-related.
- **Root cause:** `app/layout.tsx` imports `Noto_Sans_Thai` (+ Inter/Sarabun/JetBrains_Mono) via
  `next/font/google`, which fetches the actual `.woff2` files from Google's font CDN at BUILD time
  (not request time). In this Windows agent sandbox, general internet egress works (`curl
  https://www.google.com` → 200) but the specific font-file URLs 404/fail — a sandbox/proxy egress
  restriction on that CDN path, not a code defect.
- **Fix:** before blaming your diff, confirm the failure is present with `git stash` on your frontend
  changes (or just check your diff touches nothing under `next/font`/`layout.tsx`) — this is
  environment-specific and unrelated to typical FE diffs. Report `tsc --noEmit` (clean) as the
  compile-correctness proof and flag `next build`'s font-fetch step as environment-blocked; do not
  spend time trying to fix Google Fonts connectivity from inside the sandbox.
- **Seen:** 2026-08-11, R1 WP-1 (fix-breakit-r1-ledger-integrity.md) — one-line `docType` map addition
  in `frontend/lib/utils.ts`, zero relation to fonts, `next build` still hit this.

## Payroll tests self-exhaust a finite year pool on the shared teas_test — "No employees are active in this period" appears out of nowhere
**Symptom:** `PayrollRunServiceTests.Opening_ytd_is_included_in_midyear_projection_and_sso_allowance`
(or a sibling) suddenly fails with `DomainException: No employees are active in this period.` on a
suite that was green earlier the same day. Fails standalone too, so it does not look like a flake —
and nothing in the diff touches payroll.

**Root cause:** three things compound.
1. `FreshOpeningYearAsync` / `FreshYearAsync` scan **from 2100 DOWNWARD** for a year with no
   `payroll_runs` row and return the first free one.
2. The tests then **create a run in that year**, permanently consuming it — and `teas_test` is shared
   and never reset, so the pool marches down roughly one year per suite run, across every session.
3. `AddEmployee`'s default `HireDate` was hardcoded `2020-01-01`, while `PayrollRunService`
   (`:60-69`) filters `e.HireDate <= periodEnd`. Once the pool drifted **below 2020**, the employee
   was no longer active in the chosen period and the run refused.

**Fix applied (2026-08-12):** `AddEmployee`'s default is now `1900-01-01`, which predates any year the
pool can ever reach, decoupling the helper from the drift. Tests that actually exercise hire-date
behaviour still pass `hireDate` explicitly. Payroll namespace: 38/38 green after the change.

**Watch for:** the drift itself is NOT fixed — the year pool still shrinks by one per suite run. Any
*other* test helper with a hardcoded date near the pool's current position will fail the same way, and
the symptom will again look unrelated to whatever diff is in flight. If this recurs, reset `teas_test`
(see the migration-squash reset note) rather than chasing the individual date.

**Diagnosis technique worth reusing:** to prove a red test is pre-existing and not your diff, run it at
HEAD in a throwaway worktree — `git worktree add <tmp> HEAD --detach`, run the single test there,
`git worktree remove --force`. Zero risk to your working tree, and it settles the question outright.

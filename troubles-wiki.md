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
Fix pattern: wrap per-company DML in a `DO $$` block that loops companies and `PERFORM set_config('app.company_id', c.company_id::text, true)` before each company's DML (transaction-local, auto-reverts) — see `625_seed_cogs_account.sql` v1.22.1. DDL (ALTER TABLE ADD COLUMN) is unaffected (624 applied fine).
Recovery note: a failed script is NOT recorded in sys.applied_sql_scripts → it retries on next boot; earlier scripts in the same release that succeeded ARE recorded and skip. Deploy auto-rollback restores binaries; additive columns left behind are harmless.
Seen: 2026-07-18 v1.22.0 deploy (rolled back clean, prod stayed on v1.21.6; 624 applied, 625 fixed → v1.22.1).

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

## TB "as of today" ไม่เห็น depreciation JE ของเดือนปัจจุบัน
- Symptom: post ค่าเสื่อมแล้ว TB ไม่ขยับ → คิดว่า post fail. Root cause: dep JE ลงวันที่ month-END เสมอ (runDate = วันสุดท้ายของเดือน) ส่วน TB default as-of = วันนี้ → JE โดน date-filter ออก (ถูกต้อง ไม่ใช่ bug). Fix: เช็ค TB ด้วย asOf = วันที่ของ JE เอง (เช่น 2026-07-31). พบใน army B-fa 2026-07-22.

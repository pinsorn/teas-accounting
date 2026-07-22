# Move scheduled jobs into the API; delete Accounting.Workers (design)

Fable-authored design (Ham approved the "run the jobs in the API, drop the separate Workers
host" direction). A worker implements; this makes the decisions. Footgun (tenant/RLS/auth +
scheduling) → Tier-2 review (Codex) before commit. Ships as v1.10.4.

## Why
The separate `Accounting.Workers` host was never deployed to prod (only teas-api + teas-web run),
so H2/L2/L5 were dormant, AND its self-contained publish crashes at startup with a
`Microsoft.IdentityModel.Tokens 8.16.0.0` version-mismatch (a latent packaging bug — Infrastructure's
JWT stack resolves a different version than the Workers-only dep graph pins). The API ALREADY hosts
background work (`ETaxRetryHostedService`, `IdempotencyCleanupHostedService`) at
`Accounting.Api/BackgroundServices/`, registered in `Api/Program.cs:67-68`. For this app's scale
(12 MB DB, light off-peak jobs) a second host is unjustified. Moving the 2 Quartz jobs into the API
= one process, one deploy, no packaging bug, "clone/deploy is complete" automatically, and the H2
per-company tenant fix carries over.

## The crux — tenant context for an in-API background job
`Api/Program.cs:73` registers `ITenantContext → HttpTenantContext` (scoped). HttpTenantContext reads
the tenant LAZILY from `IHttpContextAccessor.HttpContext.User` claims (and MUST stay lazy — the
X-Api-Key resolver resolves ITenantContext pre-auth, see its class comment). A background job scope
has NO HttpContext → CompanyId returns 0 → the EF global filter matches no rows. So the job can't use
GetPnd30Async as-is.

**Decision — settable fallback, minimal + preserves the request path.** Turn HttpTenantContext into
`AmbientTenantContext : ITenantContext` (keep the file/namespace or rename; update the `:73`
registration): when `_accessor.HttpContext != null` behave EXACTLY as today (lazy claim reads —
do not change request semantics or the pre-auth lazy behavior); when HttpContext IS null, return
private settable fields (`CompanyId`, `BranchId`, `IsSuperAdmin`, `Username="system"`, others null)
with a `void SetCompany(int companyId, int branchId = 0)` method. The scheduled job sets it per
company in its own scope before querying. One class, both modes; requests untouched.
(Rationale over alternatives: rewriting VatReportService.GetPnd30Async to take an explicit companyId
is a larger change to a shared reporting service; a second DI container for jobs is messy. The
settable-fallback is the smallest correct change and reuses H2's exact per-company-scope + LOCAL-pin.)

## Work items
1. **`AmbientTenantContext`** — as above. Grep every `HttpTenantContext` usage (should be only the
   `:73` registration) and update. Keep it Scoped.
2. **Move the two jobs into the API** (e.g. `Accounting.Api/Scheduling/`): `VatRegisterSnapshotJob`
   and `Pnd30DeadlineAlertJob` (from `Accounting.Workers/Jobs/`). Keep their logic. The VAT job keeps
   H2's shape but sets the ambient context instead of the old `WorkerTenantContext`: for each active
   company → `using var scope = scopeFactory.CreateScope()` → resolve `AmbientTenantContext`,
   `SetCompany(id, branchId)` → resolve DbContext + IVatReportService → run inside the LOCAL-tx
   `set_config('app.company_id', id, true)` pin (the existing `RunSnapshotAsync`, unchanged) → dispose.
   `WorkerTenantContext` is retired (its role is now AmbientTenantContext's settable mode).
3. **Quartz in the API**: move the `AddQuartz` block (`Accounting.Workers/Program.cs:32-53`) — the two
   `WithCronSchedule`s (`0 0 2 * * ?` Bangkok = VAT snapshot; `0 0 9 12-15 * ?` = the reminder) +
   `AddQuartzHostedService(WaitForJobsToComplete=true)` — into `Api/Program.cs`. Add the Quartz +
   Quartz.Extensions.Hosting package refs to `Accounting.Api.csproj`. No StartNow (unchanged) — first
   VAT snapshot fires at the next 02:00 Bangkok, not on boot.
4. **Delete `Accounting.Workers`**: remove the project dir, its `Accounting.sln` entry, and the test
   project's `extern alias Workers` ProjectReference + the `Workers` alias in
   `VatRegisterSnapshotJobRlsTests.cs`. Grep the repo for any other `Accounting.Workers` reference
   (CI, scripts, appsettings) and clean.
5. **Tests**: adapt `VatRegisterSnapshotJobRlsTests` (currently references the Workers project) to
   test the job in its new API location — same assertions (under `SET ROLE pg_database_owner`, company
   A's snapshot sees ONLY A's figures with A=100/7, B=300/21 distinct; regression-proof by removing
   the pin). Add/keep the L5 January test (`Pnd30DeadlineAlertJob`). Must report Passed NOT Skipped, 2×.
   Note: `L2` (the Workers appsettings JWT-key placeholder) becomes moot — the file is deleted; drop that item.

## Gates
- Build W:\Accounting.sln 0/0 (kill :5080 if listening). Full Api.Tests green (ignore the one rotating
  shared-DB flaky). New/moved tests Passed-not-Skipped 2× (TEAS_TEST_PG + TEAS_REPO_ROOT same command).
- Do NOT git commit. Report CHANGED / EVIDENCE / anything surprising.
- STOP-and-report if: an HttpContext-null path in the request pipeline would now silently use the
  settable default (audit that the AmbientTenantContext fallback can ONLY be hit by a job scope, never
  a real request), or if deleting Workers breaks a reference you can't cleanly resolve.

## Reviewer note (Tier-2, Codex — auth/tenant footgun)
Lenses: (1) does the AmbientTenantContext preserve HttpTenantContext's exact request + pre-auth lazy
behavior (no regression to RLS/auth for real requests)? (2) can the settable fallback EVER be reached
in a request scope (that would let a job's leftover company leak into a request — confirm scope
isolation + that requests always have HttpContext)? (3) does the in-API VAT job still isolate per
company (the H2 property) under NOBYPASSRLS? (4) is app.company_id LOCAL-pinned + reset per company
iteration (no cross-company bleed on the pooled connection)?

## Status (Sonnet impl, 2026-07-04) — done, all 5 work items, no deviations from the crux
- [x] **1. AmbientTenantContext** — `Accounting.Api/Tenancy/HttpTenantContext.cs` renamed (git mv) to
  `AmbientTenantContext.cs`. Every property branches on `_accessor.HttpContext is null`: non-null →
  byte-identical lazy-claim logic to the old class (request/pre-auth path UNCHANGED); null → settable
  fallback fields (`CompanyId`/`BranchId` via `SetCompany(companyId, branchId=0)`, `IsAuthenticated=true`,
  `Username="system"`, `IsSuperAdmin=false`, everything else null) mirroring the retired
  `WorkerTenantContext`. Grepped every `HttpTenantContext` usage repo-wide: found 2 beyond the `:73`
  registration the design assumed — `Sprint14ExternalApiTests.cs` directly `new HttpTenantContext(...)`
  (updated to `AmbientTenantContext`) and 2 prose doc-comments (`McpPrincipalFactory.cs`,
  `McpServerSmokeTests.cs`, updated for consistency). DI registration changed from
  `AddScoped<ITenantContext, AmbientTenantContext>()` to the two-line
  `AddScoped<AmbientTenantContext>()` + `AddScoped<ITenantContext>(sp => sp.GetRequiredService<...>())`
  form (mirrors the retired `WorkerTenantContext` wiring exactly) — required so the job can resolve the
  concrete type to call `SetCompany` while the DbContext (which injects the interface) reads the SAME
  scoped instance.
- [x] **2. Moved the 2 jobs** — `VatRegisterSnapshotJob.cs` + `Pnd30DeadlineAlertJob.cs` git-mv'd
  Accounting.Workers/Jobs → `Accounting.Api/Scheduling/`. VAT job: `WorkerTenantContext` →
  `AmbientTenantContext`, `tenant.CompanyId = companyId` → `tenant.SetCompany(companyId)`. The
  LOCAL-tx `set_config('app.company_id', id, true)` pin in `RunSnapshotAsync` is byte-identical
  (verified via `git diff` after a regression-proof round-trip — see Gates below).
- [x] **3. Quartz in the API** — `AddQuartz` block (both cron schedules, unchanged) + package refs
  (`Quartz`, `Quartz.Extensions.Hosting`) moved into `Api/Program.cs`/`Accounting.Api.csproj`. No
  StartNow (unchanged).
- [x] **4. Deleted Accounting.Workers** — project dir removed (`git rm`), `Accounting.sln` entry +
  6 config lines + 1 nested-project line removed, test csproj's aliased `ProjectReference`/`extern
  alias Workers` removed (no longer needed — only one `Program` type exists now).
  Repo-wide grep for `Accounting.Workers` also cleaned: `backend/Dockerfile` (dead COPY line, was
  never actually published — see "Why"), `README.md`, `docs/accounting-system-plan.md`. Left
  untouched (historical, not functional): `troubles-wiki.md`'s CS0433 entry, `585_audit_log_rls.sql`'s
  comment, `specs/design-h2-workers-tenant.md`, `specs/fix-review-findings-2026-07-04.md` — all are
  dated records of past work, not live references.
- [x] **5. Tests** — `VatRegisterSnapshotJobRlsTests.cs` + `Pnd30DeadlineAlertJobTests.cs` git-mv'd to
  `tests/.../Scheduling/` (namespace `Accounting.Api.Tests.Scheduling`), `extern alias Workers` dropped,
  `BuildWorkerProvider` → `BuildJobProvider` using `AmbientTenantContext` (+ `AddHttpContextAccessor()`
  in the test's own bare `ServiceCollection`, needed since `AmbientTenantContext`'s ctor takes
  `IHttpContextAccessor` — no HttpContext is ever bound in this provider, so it resolves in fallback
  mode, exactly the real job's condition). Assertions/seed data untouched. L5 January test kept as-is
  (only namespace/using changed).
- **Unplanned but necessary fix (found via full-suite run, flagged not silently applied):**
  running the Quartz scheduler unconditionally inside `Accounting.Api` broke ~77 unrelated tests —
  `ObjectDisposedException` on `LoggerFactory` from `Quartz.Logging.LogProvider` inside
  `XMLSchedulingDataProcessor`/`ContainerConfigurationProcessor`. Root cause: the test suite boots
  75+ independent `WebApplicationFactory<Program>` hosts in the SAME process (`RbacApiFactory` +
  `McpApiFactory`, grepped — only 2 factory classes); Quartz's internal MS-logging bridge caches the
  FIRST host's `ILoggerFactory` in process-wide static state, so every LATER host's scheduler start
  throws the instant it tries to log — a documented Quartz.NET bug (github.com/quartznet/quartznet
  issue #1136), confirmed via web search, whose noted fix is "don't start the scheduler in a test
  host." Fixed with a `Quartz:Enabled` config flag (default `true`) gating ONLY
  `AddQuartzHostedService` (the piece that actually starts the scheduler — `AddQuartz`'s job/trigger
  registration is harmless and stays unconditional); `RbacApiFactory`/`McpApiFactory` each add one
  `UseSetting("Quartz:Enabled", "false")` line. Real `dotnet run`/prod never sets it, so it defaults
  on. This is config-only — no test logic, no assertions, no job/tenant logic touched.

### Gates — evidence
- Build `W:\Accounting.sln`: **0 Warning(s), 0 Error(s)**, 7 projects (Workers gone; was 8). Port 5080
  was not listening (checked, no kill needed).
- `Accounting.Api.Tests.Scheduling` (3 tests: 1 VAT RLS + 2 January-reminder): **Passed, 0 Skipped**,
  2× consecutive (both with `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the same command) — then AGAIN 2× more
  after the regression-proof round-trip below (4 consecutive green runs total post-fix).
- **Regression proof**: commented out the `set_config('app.company_id', …)` pin call in
  `RunSnapshotAsync` (test file untouched), rebuilt, re-ran → **FAILED** exactly as predicted:
  `summary.Sales` was `0M` instead of `100.00M` (fail-closed RLS hid company A's own row with no pin).
  Restored the pin, confirmed via `git diff` the restored file is byte-identical to the moved original
  (only doc/using/namespace differ), rebuilt, passed 2× more.
- Full `Accounting.Api.Tests` suite, 3 consecutive runs post-Quartz-fix: run 1 = 601 passed/8 skipped/
  **0 failed**; run 2 = 600 passed/8 skipped/**1 failed** (`TenantIsolationTests.Customer_from_company_A_is_invisible_to_company_B`
  — a file this diff never touches; re-ran in isolation → passed; matches `troubles-wiki.md`'s
  documented "a single, DIFFERENT test fails each run" pre-existing shared-DB flaky verbatim, incl. by
  name: "TenantIsolation Npgsql connection reset"); run 3 = 601 passed/8 skipped/**0 failed**. Skip
  count (8) matches the established baseline every run.
- **STOP-audit (no STOP triggered):** traced every `IServiceScopeFactory.CreateScope()`/
  `CreateAsyncScope()` call site in `Accounting.Api`+`Accounting.Infrastructure` (6 total): the 2 new
  Quartz jobs (by design, call `SetCompany`); `ETaxRetryHostedService` (uses `IgnoreQueryFilters()` +
  explicit `companyId` params throughout — never reads `_tenant.CompanyId`/`.IsAuthenticated`; the ONE
  method that does, `EnqueueAsync`, is only ever called from the request-triggered
  `TaxInvoiceService.PostAsync`, grep-confirmed, never from a background scope);
  `IdempotencyCleanupHostedService` (no `ITenantContext` usage at all); `OpenIddictSeeder` (no
  `ITenantContext` usage); `DbInitializer` (startup-only raw SQL/EF-migrations, no tenant-scoped LINQ
  query, no `IsAuthenticated` check anywhere in its call chain). Every REQUEST-scoped resolution goes
  through `TenantMiddleware`/auth handlers inside the ASP.NET Core request pipeline, which the
  framework guarantees has a non-null `HttpContext` for its entire lifetime (set before any user code,
  including pre-auth resolvers, runs) — confirmed empirically too: the full suite's ~600 HTTP-pipeline
  tests (JWT/ApiKey/OAuth auth, RLS, RBAC) all pass unchanged. **Answer: no, a real request can never
  reach the settable fallback** — only the 2 designed job scopes do.
- `git commit` NOT run (per instructions) — awaiting Fable's diff review + Tier-2 (Codex) review.

### Tier-2 (Codex) REJECT → fix round 2026-07-04 (DEFECT: RLS bypass via leftover session flag)
Codex found a real gap the first pass missed: `RunSnapshotAsync` pinned `app.company_id` LOCAL but
NOT `app.is_super_admin`. The `company_isolation` policy is `company_id = current_setting(...) OR
is_super_admin`, so a leftover `app.is_super_admin='true'` on the pooled connection (the known L4
gap — `TenantMiddleware`'s best-effort session reset can fail) would satisfy the policy's bypass
arm regardless of the company_id pin.
- **Fix:** `RunSnapshotAsync`'s ONE `set_config` call now ALSO pins
  `app.is_super_admin = 'false'` (LOCAL, same transaction, same commit-time auto-revert as the
  company_id pin) — `VatRegisterSnapshotJob.cs`.
- **Important empirical finding, surfaced not hidden:** the FIRST hardened-test attempt (leftover
  `is_super_admin='true'` before calling the real `RunSnapshotAsync`, asserting via
  `IVatReportService`) passed even WITHOUT the fix. Root cause: `VatReportService`'s queries never
  call `IgnoreQueryFilters()`, so EF's own C# global query filter (`AccountingDbContext`'s
  `HasQueryFilter`) ALSO independently restricts every query to `e.CompanyId ==
  AmbientTenantContext.CompanyId`, and `AmbientTenantContext.IsSuperAdmin` is hardcoded `false` in
  job/fallback mode (no setter exists) — so the C#-generated SQL's `WHERE company_id = <A>` clause
  excludes company B's row BEFORE Postgres RLS is even asked to adjudicate it. For THIS job's
  specific call path, the leftover-flag defect as literally described is not reachable — the EF
  filter is a second, independent gate. The `is_super_admin` pin is still correct and kept as
  defense-in-depth (matches the codebase's established "pin both together" convention from
  H5/`ApiKeyResolver.cs` and M3/`ETaxRetryWorker.cs`, both of which DO rely on `IgnoreQueryFilters()`
  and would be genuinely exposed without it) and future-proofs `RunSnapshotAsync`'s own reusable
  pin pattern against a later caller that does use `IgnoreQueryFilters()`.
- **Corrected test design** (`VatRegisterSnapshotJobRlsTests.cs`): kept the originally-requested
  hardened test (`RunSnapshotAsync_isolates_company_A_from_company_B_under_NOBYPASSRLS` — leftover
  `is_super_admin='true'` set before the real call, isolation asserted, PLUS a new post-call check
  that the leftover survives the job's LOCAL-scoped override, proving it's a per-transaction fix,
  not a permanent session change). ADDED a second, deterministic test
  (`RunSnapshotAsync_pins_is_super_admin_false_for_the_duration_of_its_own_query`) using a spy
  `IVatReportService` (`GucCapturingReportService`) that captures the live GUC values at the exact
  moment `RunSnapshotAsync` calls `GetPnd30Async` — this one DOES fail without the fix (verified:
  `CapturedIsSuperAdmin` read back `"true"` pre-fix, `"false"` post-fix) because it observes
  `RunSnapshotAsync`'s own pin directly, decoupled from `VatReportService`'s (irrelevant here) EF
  filter. This is the test that actually regression-guards the production method per Codex's ask.
- **Evidence:** build 0/0 throughout. Both tests **Passed, 0 Skipped**, 2× consecutive (both with
  `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the same command). Regression-proved twice: (1) removed the
  `is_super_admin` pin from the source (comment-out, test files untouched) → the NEW spy test
  **FAILED** exactly as required (`CapturedIsSuperAdmin` was `"true"` instead of `"false"`) while
  the isolation test stayed green (confirming the EF-filter-independence finding above, not a test
  bug) → restored the pin (git-diff confirmed byte-identical to the fix), rebuilt, both passed 2×
  more. Full `Accounting.Api.Tests` suite, 2 consecutive runs post-fix: 602 passed/8 skipped/
  **0 failed** both runs (610 total = prior 609 + 1 new spy test; skip count unchanged from
  baseline).
- **DEFECT #4 (Codex, tracked FOLLOW-UP, not implemented now):** no test exercises actual Quartz
  scheduler activation/trigger-fire/job-scope-creation end-to-end — all tests call
  `VatRegisterSnapshotJob.RunSnapshotAsync`/`Pnd30DeadlineAlertJob.LogReminder` directly. The job
  logic itself is fully tested and the cron config was moved verbatim (byte-for-byte, git-diff
  confirmed) from the deleted Workers host, so a heavy isolated-process Quartz-fire integration
  test (would need its own process — see the `Quartz:Enabled`/quartznet#1136 note above, a SECOND
  reason it can't share the existing test-host process) is deferred rather than attempted here.

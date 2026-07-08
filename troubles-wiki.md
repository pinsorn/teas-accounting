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

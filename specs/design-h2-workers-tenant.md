# H2 (+M3, M12) — Workers run tenant-blind (design)

Fable-authored design (I hold the context; opus-verify H2 + tenant-isolation.md HIGH-2
are the evidence). A worker implements; this spec makes the decisions. Tenant isolation
= high-risk → Tier-2 Opus review after impl.

## The bug
`Accounting.Workers/Program.cs` registers `AddInfrastructure(...)` only — it NEVER registers
`ITenantContext` (only `Api/Program.cs:70` does, with `HttpTenantContext`). So in Workers the
DbContext's `_tenant` is null and the global query filter `_tenant == null || …`
(`AccountingDbContext.cs:152`) short-circuits to a **process-wide no-op**, and nothing calls
`set_config('app.company_id', …)`. `VatRegisterSnapshotJob → VatReportService.GetPnd30Async`
therefore runs with NO company scope: it blends every tenant's VAT (if the role bypasses RLS)
or reads 0 rows (prod NOBYPASSRLS). No per-company loop exists today.

## Design — "one child scope per company" (revised after Codex cross-family review)
An HTTP request = a DI scope + a scoped `ITenantContext` + a TenantMiddleware pin. A worker that
processes every company does the SAME per company, via `IServiceScopeFactory`. Codex flagged 4
material flaws in the first draft; this version fixes all four. Confirmed facts: `AccountingDbContext`
is Scoped and constructor-injects `ITenantContext?` and reads its CURRENT properties in the filter
(`AccountingDbContext.cs:25-27,148`); no mutable impl exists (`HttpTenantContext` is computed,
`StubTenant` is test-only init); `master.companies` is NOT `ITenantOwned` and has NO RLS, so the
company list is already cross-tenant (`Company.cs`, `MasterDataServices.cs:378`); the job is a
**Quartz `IJob`** (`VatRegisterSnapshotJob.cs`), hosted via `AddQuartzHostedService` (`Program.cs:42`).

1. **New `WorkerTenantContext : ITenantContext`** — mutable CompanyId/BranchId, UserId null, Username
   "system". No reusable impl exists (confirmed).
2. **Register so the DbContext and the loop share ONE instance** (Codex flaw #1 — "both Scoped" is not
   enough; the DbContext requests the *interface*): in `Workers/Program.cs`,
   `services.AddScoped<WorkerTenantContext>()` + `services.AddScoped<ITenantContext>(sp =>
   sp.GetRequiredService<WorkerTenantContext>())` — the interface resolves to the SAME scoped concrete
   instance the loop mutates. (Api is untouched — it keeps HttpTenantContext.)
3. **Quartz orchestration via `IServiceScopeFactory`** (Codex flaw #4): the job injects
   `IServiceScopeFactory` (NOT a captured `IVatReportService` — one captured service can't represent N
   company scopes). Enumerate active companies once (cross-tenant read of `master.companies`; it has no
   RLS so a plain read suffices — no super-admin pin needed here, Codex #4). Then FOR EACH company:
   `using var scope = scopeFactory.CreateScope();` → resolve `WorkerTenantContext` from the scope and
   set `.CompanyId` → resolve the scope's DbContext + `IVatReportService` → run the snapshot inside the
   pinned transaction (next point) → dispose scope. Fresh scope = fresh DbContext, no cross-company bleed.
4. **Pin via a LOCAL `set_config` inside an explicit transaction** wrapping ALL of that company's
   queries (Codex flaw #3 — the RISKIEST: a bare session `set_config(...,false)` can land on a pooled
   connection that a later EF query doesn't reuse → pin lost, or not-reset → poisoned pooled session).
   Mirror `PermissionLookup.cs:28-52`: `await using var tx = await db.Database.BeginTransactionAsync();`
   `set_config('app.company_id', <id>, true)` (is_local=true → auto-reverts on commit/rollback, no
   poison risk); run the snapshot; commit. The whole per-company unit of work lives in that one tx so
   every query sees the pin on the same connection.

Decision rationale: reuses the existing EF filter + RLS machinery unchanged; the transaction-scoped
LOCAL pin is the repo's proven safe pattern and sidesteps the pooled-connection footgun. Threading an
explicit companyId through every query (the alternative) is a large blast radius and abandons the RLS
backstop — rejected.

## M3 — SEPARATE unit, different host (Codex flaw #5)
The e-Tax retry is NOT in `Accounting.Workers`. It is an **API-hosted** `BackgroundService`
(`Accounting.Api/BackgroundServices/ETaxRetryHostedService.cs`, registered `Api/Program.cs:65`), so
the WorkerTenantContext registration above cannot reach it. AND the per-item execution ALREADY passes
an explicit `company_id` through to the pipeline + its queries (`ETaxRetryWorker.cs:50`,
`ETaxSubmissionPipeline.cs:57`) — only the **candidate SCAN** (`ETaxRetryWorker.cs:28`, cross-tenant
over `etax.submissions`) is unpinned. **This got worse because of our own 581**: 581 added FORCE RLS
to `etax.submissions`, so the previously-working unpinned scan now returns 0 rows under prod NOBYPASSRLS
(before 581 there was no RLS to block it). e-Tax is inert Phase-1 → low urgency, but it IS a
regression our commit introduced.
Fix (separate small dispatch, Api-side): pin the candidate scan `ETaxRetryWorker.cs:28` with the same
LOCAL-tx `set_config('app.is_super_admin','true',true)` used in H5's ApiKeyResolver (the scan is a
legitimate cross-tenant admin read; per-item work already re-scopes by its own company_id). Do NOT
register WorkerTenantContext in the Api host. One proving test: under `SET ROLE pg_database_owner`
with app.company_id unset, the scan returns pending rows (0 today).

## M12 (audit.activity_log RLS) — follow-up, do NOT do here
DEFERRED to a follow-up SqlScript AFTER H2 + M3 land, because an RLS policy on the nullable-company
audit table would block audit INSERTs from any still-unpinned path. Once workers pin `app.company_id`,
add `USING (company_id = NULLIF(current_setting('app.company_id',true),'')::INT OR company_id IS NULL OR <super>)`.
Tracked follow-up in the spec-log; not in the H2 or M3 dispatch.

## Proving test (load-bearing — must EXECUTE, not skip)
Under `SET ROLE pg_database_owner` (NOBYPASSRLS; the repo trick — see ReviewHardeningRlsTests.cs),
seed two companies each with data that would land in a ภ.พ.30 snapshot, run the per-company snapshot
path for company A, and assert the result contains ONLY A's figures (B invisible). Contrast today:
without the pin it either blends both or returns 0. The test must report **Passed, not Skipped**
(memory: skipped tests fake green — report the skip-count) with TEAS_TEST_PG set.

## Scope / gates
- Files: `Workers/Program.cs`, the job(s) (`VatRegisterSnapshotJob`, e-Tax retry), a new
  `WorkerTenantContext` if none exists, + test. No schema change (M12 SqlScript is a separate follow-up).
- Build 0/0 (kill :5080 first). Test passes 2× consecutive, EXECUTED not skipped. Full suite: ignore
  the one rotating flaky. Do NOT git commit.
- Footgun / STOP-and-report: if a settable ITenantContext breaks a DI assumption (lifetime mismatch
  with the DbContext), or the company-list read can't be scoped cleanly, STOP and report — do not
  disable the global filter or weaken RLS to make it compile.

## Reviewer note (Tier-2, tenant isolation)
Lens: does EACH company iteration get a genuinely isolated DbContext + connection (no leaked
`app.company_id` across iterations on a pooled connection)? Is the company-list read the ONLY
cross-tenant read, and is it correctly scoped? Could the settable context leak between concurrent
jobs (is it really per-scope, not a singleton)?

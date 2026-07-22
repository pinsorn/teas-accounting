# Fix Plan — 2026-07-04 review findings

Source register: `_review/codebase-review-2026-07-04.md`. Goal: fix ALL findings.
Order = risk + file-set disjointness. Implementers NEVER `git commit` — Fable runs
the consolidated gate + commits per verified wave.

## ⏸ CHECKPOINT (paused at 5-hour quota 92%, ~101min to reset) — resume from here
Branch `fix/review-findings-2026-07-04`. **All 10 HIGH + 6 MED committed & verified** (each:
worker impl → footgun/money/security Tier-2 review [Opus or Codex] → Fable diff-read → commit):
- `4e7e398` H3 PUT validation · `41e476e` M7/M8/L3 FE · `523fa9a` H1/H7/M2/M1 schema RLS+immutability
- `af6f26e` H6/H8 WHT-leak + numbering-tx · `4f591a9` H9/H10/M10 ภ.พ.30 · `9872841` H4/M11 OAuth · `7238186` H5 api-key
**REMAINING (all Claude-worker dispatches — held at ≥85%; resume after reset):**
1. [x] **H2** Workers per-company tenant — Sonnet impl done 2026-07-04 per
   `specs/design-h2-workers-tenant.md` exactly. Awaiting Tier-2 (Codex) review → commit. See
   spec-log below for full evidence.
2. [x] **M3** e-Tax retry candidate-scan pin (Api host, NOT Workers) — Sonnet impl done
   2026-07-04 per the H2 spec's "## M3" section. Awaiting review → commit. See spec-log below.
3. [x] **M12** audit_log RLS — Sonnet impl done 2026-07-04 (585_audit_log_rls.sql), after H2+M3.
   Awaiting review → commit. See spec-log below.
4. **Wave 4** M4/M5 (login rate-limit per-IP + ForwardedHeaders), M6 (returnTo `\` bypass),
   M9 (Receipt N+1), L1/L2/L4/L5/L6/L7, + F1 (FE pnd30 preview credit row) + F2 (OutputVatRegister
   CN/DN label) — tier by blast radius (Haiku one-liners for L2/L5/L7).
5. **Retro → minions-assemble**: mirror CLAUDE.md rules #1(softened)/2/3/4 + dispatch-template
   "diff latest DB-object def" note + push; troubles-wiki entries (create-as-posted, apply-once
   re-run, TI-void, pg_database_owner test role, schema gotchas) stay in TEAS.
   [DONE-PARTIAL: rule #1(co-design) + ScheduleWakeup-default pushed to minions; #2/#4 +
   dispatch-template note + troubles-wiki entries still pending.]
6. **MCP-DCR (LAST, after everything above incl. all low)** — NEW finding 2026-07-04 (Ham
   screenshot): Claude's MCP connector to teas.kazaki-rio.com/mcp fails with "Automatic client
   registration isn't supported by TEAS" — OpenIddict AS doesn't enable Dynamic Client
   Registration (RFC 7591). Spec + diagnosis + fix direction: `specs/mcp-dcr-client-registration.md`.
   Footgun (auth surface) → Fable co-designs, worker impls, Tier-2 review, ASK Ham before shipping.
Uncommitted in tree: orchestration docs only (CLAUDE.md rule edits, troubles-wiki, specs, .gitignore
codex-out, ROUTING-LOG, review reports) — bundle at retro.

## Env briefing (every backend dispatch — §6 footguns)
- subst drives: `W:` → backend. Run `dotnet build/test/ef` from `W:` (long path → Win32Exception 87).
- Full solution build locks `Accounting.Api.exe` if :5080 is listening → `Get-NetTCPConnection -LocalPort 5080 -State Listen` then `Stop-Process -Force`, build, done. (Api.exe is in the solution.)
- NEVER `dotnet ef … --no-build` after entity edits. Build solution first, then `ef` WITH build, from `W:`.
- Tests from `W:\tests\Accounting.Api.Tests` with `$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'` + `$env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'` set in the SAME command as `dotnet test`. New tests must pass 2× consecutive.
- FE: `tsc --noEmit` is the gate; never `next build` while `next dev` runs.
- Test data: any UNIQUE-constrained insert uses `Accounting.TestKit.TestIds.*` — never hardcoded codes/periods.

---

## Wave 0 — Opus verification + design (BLOCKING for the Codex cluster)
Read-only. Verifies every ⚪ Codex finding to file:line, resolves the 2 conflicts,
and writes a per-CONFIRMED-finding fix spec (location + approach + test) so Wave 3 is turnkey.
Items: H4 (OAuth scope — conflict), H5 (API-key pre-tenant), H6 (WHT cross-tenant),
H7 (posted-line INSERT/re-parent), H8 (numbering outside tx — conflict, TI already cleared),
H9 (ภ.พ.30 HasInputVat), H10 (ภ.พ.30 double-count), M10 (CN/DN exempt), M11 (OAuth refresh
revalidate), M12 (audit_log RLS), L5 (Jan reminder year). Output → `_review/2026-07-04/opus-verify.md`.

## Wave 1 — verified HIGH, independent file-sets (start now, parallel with Wave 0)
- **H3 (Sonnet, backend/Api):** wire FluentValidation to every update endpoint that lacks it.
  Pattern already in the sibling POST: `var val = await v.ValidateAsync(req, ct); if(!val.IsValid) return Results.ValidationProblem(val.ToDictionary());`. Audit all ~25 `MapPut`; wire the existing
  `UpdateXValidator` where present (e.g. `UpdateVendorValidator` at `VendorDtos.cs:62`), create the
  missing ones mirroring the matching `CreateXValidator`. New integration test per newly-guarded
  endpoint: invalid body → 400 ValidationProblem. Files: `Accounting.Api/Endpoints/*` + validators in
  `Accounting.Application/*`. → spec-log below.
- **FE MEDIUMs (Sonnet, frontend):** M7 replace generic `toast.error(tc('error'))` with `errorToToast()`
  (`lib/api/errors.ts`) at the 13 sites (pattern already used in `payroll`/`settings/business-units`);
  M8 add a Zod schema to `VendorForm.tsx` mirroring `CustomerForm.tsx:18-34`; L3 i18n the hardcoded Thai
  strings in `payment-vouchers/new/page.tsx:284,331,388,397` (edit BOTH th.json + en.json). → spec-log below.

## Wave 2 — tenant + schema (Fable owns migrations; Opus designs H2)
- **H1 (Fable design + Sonnet writes SqlScript, Fable runs ef gen):** add RLS to the 8 uncovered
  `ITenantOwned` tables following the exact `572_sales_chain_rls.sql` policy template (`company_isolation`,
  FORCE, USING=WITH-CHECK, fail-closed on unset). New numbered SqlScript + EF migration wired in. Verify
  via a test that connects as role `teas` (NOT superuser) and asserts cross-tenant rows are invisible —
  the superuser-bypass trap means a normal test proves nothing.
- **H2 (Opus design → Sonnet impl):** register `ITenantContext` in Workers OR redesign
  `VatRegisterSnapshotJob` to loop per company and pin `app.company_id` per iteration. Design decision
  needed (worker has no HTTP context). Depends on Wave 0's tenant findings (H5/H6/M12) landing first.
- **M1/M2 (Fable, trigger SQL):** add `REVOKE TRUNCATE`/ownership hardening for audit tables; fix the
  asymmetric immutability trigger (`040:6` guard `status<>'DRAFT'` + protect `status` column). Migration.
- **M3 (with H2):** e-Tax retry worker — same tenant-pin fix.

## Wave 3 — CONFIRMED Codex items (after Wave 0 verdict)
Route per Wave 0 output: auth (H4/H5/M11) → Opus design if real; money ภ.พ.30 (H8-nonTI/H9/H10) →
Fable/Opus (compliance); H6/H7/M12 fold into Wave 2 tenant/trigger work.

## Wave 4 — remaining MEDIUM/LOW (Sonnet + Haiku)
M4+M5 (rate-limit per-IP + ForwardedHeaders — together), M6 (returnTo `\` bypass), M9 (Receipt N+1),
L1 (VAT label), L2 (Workers appsettings key + AllowedHosts), L4 (TenantMiddleware reset), L6 (dedupe),
L7 (Thai comments — Haiku, optional).

---

## Spec-log (living checklist)

### H3 — PUT validation wiring
- [ ] Dispatched Sonnet 2026-07-04.
- [x] Done 2026-07-04. Audited all 25 `MapPut` in `Accounting.Api/Endpoints/*` against
  existing `AbstractValidator<T>` classes. 4 genuine gaps fixed (sibling `CreateXValidator`
  existed to mirror, or the `UpdateXValidator` existed but was dead/unwired):
  `CustomerEndpoints.cs:33` (new `UpdateCustomerValidator`), `MasterEndpoints.cs:39`
  branches (new `UpdateBranchValidator`), `MasterEndpoints.cs:57` vendors (wired the
  existing-but-dead `UpdateVendorValidator`), `MasterEndpoints.cs:82` accounts (new
  `UpdateAccountValidator`). 4 new integration tests in
  `tests/Accounting.Api.Tests/Hardening/H3PutValidationTests.cs` (real HTTP pipeline via
  `RbacApiFactory`, SUPER_ADMIN JWT, invalid body → 400 + `fieldErrors`), pass 4/4 × 2
  consecutive runs. Build 0 errors/0 warnings. Full `Accounting.Api.Tests` suite: 1 failure
  per run but a DIFFERENT single test each time, in files this diff never touches
  (pre-existing shared-DB order/connection flakiness — documented in `troubles-wiki.md`).
  11 of the 15 candidate PUTs from the finding's lead list are INTENTIONALLY left as-is —
  see report for the full breakdown (2 legit validation-free exceptions + 1 endpoint with a
  compensating domain guard already verified sound by the finding doc + 7 endpoints where NO
  sibling `CreateXValidator` exists anywhere to mirror, i.e. outside this dispatch's
  wire-or-mirror mechanism — flagged as a follow-up, not fixed here per Ponytail
  "don't invent new constraints").

### FE-MED — error handling + Zod + i18n (M7/M8/L3)
- [x] Dispatched Sonnet 2026-07-04. Done 2026-07-04.
  - M7: replaced `catch { toast.error(tc('error')); }` with `catch (e) { toast.error(errorToToast(e)); }`
    (import from `@/lib/api/errors`) at all 14 grep-confirmed bare-catch sites across the 9 named files
    (VendorForm.tsx; payment-vouchers/new; receipts/new ×2; vendor-invoices/new ×2; tax-invoices/new ×2;
    AdjustmentNoteForm.tsx ×2; CreateViFromPvDialog.tsx; settings/company; settings/wht-types ×2).
    Evidence: `grep -n "toast.error(tc('error'))"` across the 9 files → 0 matches (exit 1).
  - M8: converted VendorForm.tsx from raw `useState` to RHF + `zodResolver`, mirroring
    CustomerForm.tsx's schema/err() pattern. Zod schema (length caps on vendorCode/nameTh/nameEn,
    email format, paymentTermDays int/min(0)) + `superRefine` preserving the pre-existing
    taxId-unchanged-bypass rule (legacy taxId that fails checksum stays editable if untouched).
    Wrapped the form body in `<form onSubmit={handleSubmit(onSubmit)}>`; TaxIdInput wired via
    `Controller` (custom controlled component); isForeign/vatRegistered/hasThaiVatDReg/countryCode
    cross-field toggle logic preserved via `watch`+`setValue`. Added `ven.err.{required,max50,max255}`
    to th.json/en.json.
  - L3: replaced the 4 hardcoded Thai literals in payment-vouchers/new/page.tsx (docDate label,
    line-description aria-label, delete button, add-line button) with `t()`/`tc()` calls — reused
    existing `common.date`/`common.delete` (exact text match, 0 new keys) and added 2 new `pv.*`
    keys (`addLine`, `lineDescription`, wording matched to `ti.form`'s existing equivalents) to
    th.json/en.json.
  - Gates: `npx tsc --noEmit` → 0 errors (exit 0). Message parity: TH/EN both 1542 leaf keys
    (was 1537 baseline + 5 new: ven.err×3, pv.addLine, pv.lineDescription), 0 missing either way.
  - Not run (per dispatch): `next build`, e2e/browser smoke test — not named as a gate for this
    dispatch; static analysis (tsc) confirmed compatible label/DOM structure against the
    `createVendor` e2e helper and `03.02-vendors.ts` walkthrough by inspection.

### Wave 0 — Opus verification
- [x] Done. 9 CONFIRMED / 2 PARTIAL / 0 REFUTED → `_review/2026-07-04/opus-verify.md`.

### Wave 2 Unit-1 — schema hardening (H1 + H7 + M2 + M1) — Fable
- [x] Done + committed `523fa9a` on branch fix/review-findings-2026-07-04.
  - 581 H1 (RLS 8 tables), 582 H7 (re-parent guard; NO insert guard — GL/Receipt
    create-as-posted would break), 583 M2 (un-post block + allow POSTED→VOIDED +
    business_unit_id), 584 M1 (audit TRUNCATE guard + REVOKE). All DbInitializer
    SqlScripts (no EF migration). ReviewHardeningRlsTests (5) prove all four.
  - Footguns hit + logged to troubles-wiki: (a) create-as-posted breaks BEFORE INSERT
    line guards; (b) CREATE OR REPLACE of a multiply-redefined fn must copy the LATEST
    definition (200 added business_unit_id over 040), not the original.
  - M12 (audit_log RLS) deferred → Unit-2 (interacts with H2 Workers-unpinned).

### Wave 2/3 remaining (app-code)
- [~] Unit-2: H5 (api-key pre-pin, Fable design) done 2026-07-04 — see below. H6 (WHT join, Sonnet)
  done 2026-07-04 — see below. M12 still open.
- [x] Unit-3: H4 + M11 (auth, Opus design) done 2026-07-04 — see below.
- [x] Unit-4: H9 + H10 + M10 (ภ.พ.30 money) done 2026-07-04 — see below.
- [x] Unit-5: H8 (numbering, 5 paths wrap tx, Sonnet) done 2026-07-04 — see below.

### Unit-2 (H5) — API-key auth reads RLS tables before the tenant is pinned
- [x] Done 2026-07-04, per `specs/design-h5-apikey-prepin.md` exactly (LOCAL-tx
  super-admin pin mirroring `PermissionLookup.cs:29-31`, RLS NOT dropped).
  `Accounting.Infrastructure/Identity/ApiKeyResolver.cs`: both `IgnoreQueryFilters()`
  reads (`_db.ApiKeys` by `KeyPrefix` ~:38, `_db.Branches` by `CompanyId` ~:52) now each
  open their own short `BeginTransactionAsync`, `set_config('app.is_super_admin', 'true',
  true)` (LOCAL — auto-reverts on commit/rollback), run the one query, `CommitAsync`.
  `IgnoreQueryFilters()` kept on both (still needed for the EF filter). `TouchLastUsedAsync`
  (a separate best-effort write, already wrapped in a catch-all) intentionally left
  outside the pin — out of the design's named scope (only the two reads at ~:38/~:52) and
  already fails safe (logs a warning, doesn't break auth).
  - New test `tests/Accounting.Api.Tests/Identity/ApiKeyResolverRlsTests.cs`: mints a real
    key via `IApiKeyService.CreateAsync` (bypass-role scope), then on a SEPARATE scope opens
    one physical connection (`db.Database.OpenConnectionAsync()` so `SET ROLE` sticks across
    the resolver's internal transactions — same technique as the sibling
    `PermissionLookupRlsTests`), `SET ROLE pg_database_owner` (rolbypassrls=false, membership
    implicit for the DB owner — no CREATEROLE needed) with `app.company_id` UNSET (the exact
    prod pre-auth condition), then calls `IApiKeyResolver.AuthenticateAsync` DIRECTLY (not
    just an HTTP endpoint) and asserts `result.Key` resolves with the right ApiKeyId/CompanyId/
    HeadOfficeBranchId.
  - **Attempt log:** first tried `SET ROLE teas_rls_test` (the newer provisioned RLS-test role
    `PermissionLookupRlsTests` uses) — SKIPPED at runtime: `teas_rls_test` needs CREATEROLE to
    provision and this env's `accounting` test user doesn't have it (`42501: permission denied
    to create role`). Caught by re-reading the actual test output rather than trusting a green
    exit code (the skip-count check the dispatch called for). Switched to `SET ROLE
    pg_database_owner` per the design doc's explicit instruction (built-in predefined role,
    membership implicit for the DB owner, no CREATEROLE required) + manual `GRANT SELECT` on
    `sys.api_keys`/`master.branches` to it (same shape as `SalesChainRlsTests`/
    `ReviewHardeningRlsTests`) — passed immediately.
  - **Tier-2 (Codex) review correction:** fix APPROVED as-is; test hardened per the reviewer's
    gap: the original test only cleared `app.company_id` before `SET ROLE`, not
    `app.is_super_admin` — since the RLS policy has an `is_super_admin` bypass clause, a
    pooled connection that happened to retain `true` from a prior test could let even
    PRE-FIX code pass for the wrong reason (false green). Test now also
    `set_config('app.is_super_admin', 'false', false)` (SESSION-scoped) before `SET ROLE`,
    and asserts `current_setting('app.is_super_admin', true)` still reads `false`/empty
    AFTER `AuthenticateAsync` returns — proving the fix's per-transaction LOCAL pin resets
    and never leaks onto the pooled session (the security-critical property). No change to
    `ApiKeyResolver.cs`.
  - Evidence: **Passed** (not Skipped) 2× consecutive with the hardened test, ~4.3s each
    run, both with `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the same command (the
    `is_super_admin`-stays-false assertion is part of this passing run — it would itself
    fail the test if the pin ever leaked). Regression-proved WITH the hardened test:
    `git stash`-ed just `ApiKeyResolver.cs` (test file untouched), rebuilt, re-ran — test
    still FAILED (`result.Key` null — now provably because RLS blocks the pre-fix lookup
    with BOTH GUCs cleared, not because a stale session flag happened to help), `git stash
    pop` restored the fix, rebuilt, passed again 2×. Build 0 errors/0 warnings throughout.
    Full `Accounting.Api.Tests` suite post-fix (pre-hardening run, still valid — the
    hardening only tightens the test, doesn't change resolver behaviour): run 1 = 590/599
    passed, 1 failed (`Pnd50FilingServiceTests.Pnd50_with_nonzero_adjustments_renders_the_ladder_in_v2`
    — unrelated file, passes in isolation), 8 skipped; run 2 = 591/599 passed, 0 failed, 8
    skipped — confirms the one rotating shared-DB flaky (documented in `troubles-wiki.md`),
    not a regression from this diff.
  - Blast radius: exactly 2 files (cap) — `ApiKeyResolver.cs` (edit) +
    `ApiKeyResolverRlsTests.cs` (new). No schema/SqlScript changes; RLS left fully enforced on
    both tables.

### Unit-2 (H6) — WHT-suggest cross-tenant line-description leak
- [x] Done 2026-07-04. `ReceiptService.Read.cs` `SuggestWhtBaseAsync`: both the
  `TaxInvoiceLines` read (~line 293) and the `BillingNoteLines` read (~line 310) queried
  the line DbSet directly on caller-supplied ids with no tenant scope (line tables carry no
  `company_id`, so no EF filter/RLS). Fixed by `.Join`-ing the tenant-filtered parent set
  (`_db.TaxInvoices` / `_db.BillingNotes`) — identical shape to the safe
  `TaxFilings/SalesCategorizer.cs:41` pattern. Minimal diff, no new abstraction.
  New test `tests/Accounting.Api.Tests/Hardening/H6WhtSuggestTenantLeakTests.cs`:
  as tenant A, `SuggestWhtBaseAsync` referencing tenant B's TaxInvoiceId (and separately
  B's BillingNoteId) → asserts 0 line descriptions returned (was leaking `DescriptionTh`
  pre-fix). Uses the plain EF-global-filter test pattern (`TenantIsolationTests`-style,
  `TestCompanyFactory.BuildProvider`) — NOT an RLS/`SET ROLE` test, because the leak's root
  cause is a missing LINQ join against an `ITenantOwned` EF-filtered set, not an RLS gap;
  the filter is enforced in the query itself regardless of DB role, so the normal
  (superuser) test connection proves the fix correctly.
  Evidence: regression-proved by temporarily stashing the fix and re-running the same
  tests — both failed against pre-fix code (leaked `DescriptionTh` cross-tenant), then
  passed again once the fix was restored (2x consecutive). Build 0/0. Full
  `Accounting.Api.Tests` suite: 579 passed / 8 skipped / 0 failed (clean run, no
  pre-existing rotating flaky hit this pass).

### Unit-5 (H8) — 5 numbering paths now allocate+save atomically
- [x] Done 2026-07-04. Wrapped each unsafe method's `NextAsync…SaveChangesAsync` in
  `await using var tx = await db.Database.BeginTransactionAsync(ct); … await
  db.SaveChangesAsync(ct); await tx.CommitAsync(ct);` — identical shape to the existing
  safe `TaxInvoiceService.PostAsync` (289-345):
  - `PurchaseOrderService.ApproveAsync` (Purchase/PurchaseOrderService.cs) — alloc+save
    were the ONLY unwrapped pair in this file (`CreateDraftAsync` doesn't allocate a
    number; `ApproveAsync` does, at PO-approval time).
  - `BillingNoteService.IssueAsync` (Sales/BillingNoteService.cs) — allocates the `IV`
    invoice number; highest priority per the review (it's an invoice number).
  - `QuotationService.SendAsync` (Sales/QuotationChainServices.cs).
  - `SalesOrderService.PostAsync` and `DeliveryOrderService.IssueAsync`
    (Sales/SalesOrderDeliveryServices.cs — two classes, one file).
  Note: the opus-verify.md save-line citations for the Quotation/SalesOrder rows
  (`:259/:266`, `:257/:259`) didn't match any numbering call-site on re-inspection; the
  `helper` line citations (`:308`, `:200`) DID match exactly, confirming
  `SendAsync`/`PostAsync` (the methods that actually call those helpers) as the correct,
  and only, targets in each file — verified by grep (no other caller of
  `SubPrefixNumberAsync`/`SubNumAsync` exists in either file).
  New test `tests/Accounting.Api.Tests/Hardening/NumberSequenceTransactionSafetyTests.cs`
  (5 methods, one per path): creates the draft doc, stages a poisoned duplicate
  `(company_id, customer_code)` Customer on the SAME DbContext (unsaved), then calls the
  transition method — its own `SaveChangesAsync` flushes the poison insert together with
  the real update, a real Postgres unique-violation (23505) aborts the whole transaction,
  and the assertion confirms `sys.number_sequences` has ZERO rows for that period tuple
  afterward (proving the allocation rolled back with the failed save — no gap/no wasted
  number). Each test uses a fresh `TestCompanyFactory`-created company so the sequence
  tuple starts empty (no ambiguity about "unchanged" — before AND after must be 0 rows).
  Evidence: all 5 pass 2x consecutive. Regression-proved for the BillingNote path by
  stashing the fix and re-running — failed with `sys.number_sequences` left at 1 row
  (the gap) pre-fix, passed again once restored. Build 0/0. Full `Accounting.Api.Tests`
  suite: 579 passed / 8 skipped / 0 failed (same clean run as H6, above — both units
  verified in the same full-suite pass).

### Unit-3 (H4 + M11) — OAuth MCP scopes intersected with live RBAC
- [x] Done 2026-07-04. Built the shared §0 primitive first, then wired both findings onto it —
  exactly per `specs/design-auth-h4-m11.md`, no deviations.
  - **§0 helper** — NEW `Accounting.Api/OAuth/McpConsentScopes.cs` (`internal static`):
    `FilterToRbac(grantedScopes, userPermissions)` maps each scope to its required RBAC permission
    (identity for 15/18 scopes; `sales.quotation.read`/`.create` → `sales.quotation.manage`;
    `sys.system_info.read` → no permission required) and keeps only scopes the user holds.
  - **H4** — `OAuthEndpoints.cs` POST `/oauth/authorize`: added `IPermissionLookup permissions` DI
    param; after the existing `McpScopes.Normalize` + empty-guard, a regular user's grant is
    additionally intersected via `McpConsentScopes.FilterToRbac(granted, perms)` (re-running the
    `invalid_scope` guard after); `tenant.IsSuperAdmin` short-circuits the filter (supers hold no
    explicit permission rows anywhere — intersecting would zero their grant).
  - **M11** — NEW `Accounting.Api/OAuth/RefreshTokenRevalidationHandler.cs`: an OpenIddict 7.5.0
    `IOpenIddictServerHandler<ProcessSignInContext>`, scoped strictly to
    `EndpointType==Token && Request.IsRefreshTokenGrantType()`. Reloads the subject user; rejects
    (`invalid_grant`) if inactive, or (non-super) if `PermissionLookup.LoadAsync` returns
    `Roles.Count==0` for the token's baked company (off-boarded), or (super) if the company is no
    longer active. Otherwise re-derives `granted = McpScopes.Normalize(toolScopes) ∩ RBAC` (shared
    helper) and re-bakes BOTH scope representations (`principal.SetScopes` + the CSV
    `TenantClaims.Scopes` claim `PermissionHandler` actually reads) before
    `PrepareAccessTokenPrincipal` runs. Wired in `Program.cs`: `using OpenIddict.Server;` +
    `o.AddEventHandler<ProcessSignInContext>(b => b.UseScopedHandler<RefreshTokenRevalidationHandler>()
    .SetOrder(int.MinValue + 100_000))`; updated the stale `:147-149` comment that used to claim a
    "T4" handler existed.
  - **OPEN QUESTION 1 (handler ordering)** — resolved empirically, no adjustment needed: the
    design's suggested `SetOrder(int.MinValue + 100_000)` worked on the FIRST test run (the M11
    revoke-permission proving test showed the re-derived scopes correctly reaching the issued
    access token). No fallback (token-endpoint passthrough) needed.
  - **OPEN QUESTION 2 (membership signal)** — used exactly what the design proposed: ≥1 active
    `UserRole` in the baked company (`PermissionLookup.LoadAsync().Roles.Count == 0` ⇒ off-boarded).
    No separate membership concept found in the code.
  - **Plumbing gap found + fixed (not a design conflict):** the design's own §0 proving test
    (`McpConsentScopesTests`) calls the `internal` `McpConsentScopes.FilterToRbac` directly from the
    separate `Accounting.Api.Tests` assembly, but no `InternalsVisibleTo` existed anywhere in the
    repo. Added NEW `Accounting.Api/AssemblyInfo.cs` with
    `[assembly: InternalsVisibleTo("Accounting.Api.Tests")]` — mechanical test-wiring, not a
    security-semantic change, and the design's own blast-radius cap ("3 new source" files) already
    budgeted for exactly one more new-source file than it named.
  - **Testing note:** OpenIddict access tokens are encrypted JWEs (5-segment, ephemeral key per
    test host) — they cannot be decoded client-side in a black-box HTTP test. Proving tests
    therefore assert BEHAVIORALLY via real `/mcp` tool calls (`list_tax_invoices` /
    `create_tax_invoice_draft`), mirroring the established pattern in
    `McpServerSmokeTests.Mcp_key_with_read_only_scopes_hides_create_tools` (missing scope ⇒ tool
    hidden from `ListToolsAsync` AND an explicit call throws) — PermissionHandler gates OAuth
    Bearer and X-Api-Key identically (both ride `is_api_key=true` + the CSV `scopes` claim).
  - New test files: `tests/Accounting.Api.Tests/OAuth/McpConsentScopesTests.cs` (4 pure-unit cases
    — the 3 non-identical translation-table rows + the identity-mapped drop case) and
    `tests/Accounting.Api.Tests/OAuth/OAuthScopeRevalidationTests.cs` (4 integration: H4 restricted
    user loses `.create`/keeps `.read`; H4 super-admin keeps both; M11 refresh rejected when
    deactivated; M11 refresh re-derives scopes when a permission is revoked mid-token-lifetime).
  - Evidence: all 8 new tests pass 2× consecutive (`OAuth` namespace: 31/31 both runs; combined
    `OAuth`+`Mcp` namespaces: 67/67 both runs). Regression-proved by temporarily disabling the H4
    filter block and the M11 handler registration (comment-swap, restored immediately after) and
    re-running the 4 integration tests: 3/4 FAILED against the un-fixed code exactly as expected
    (H4 restricted-user test — `create_tax_invoice_draft` wrongly visible; M11 deactivated-user
    test — refresh returned 200 instead of 400; M11 revoke test — `create_tax_invoice_draft`
    wrongly still visible after refresh); the H4 super-admin test correctly stayed green (it never
    depended on the filter). Fix restored, re-verified green 2×. Build 0 errors/0 warnings before
    and after. Full `Accounting.Api.Tests` suite: 590 passed / 8 skipped / 0 failed (clean run —
    the documented pre-existing rotating shared-DB flaky did not surface this pass).
  - Blast radius: exactly 7 files (cap) — 3 new source (`McpConsentScopes.cs`,
    `RefreshTokenRevalidationHandler.cs`, `AssemblyInfo.cs`), 2 edits (`OAuthEndpoints.cs`,
    `Program.cs`), 2 new test files. No schema/migration/EF changes; no public-API signature
    changes to existing types.

### Unit-4 (H9 + H10 + M10) — ภ.พ.30 money correctness
- [x] Done 2026-07-04.
  - **H9** (input VAT over-claim): `Reports/VatReportService.cs` (`GetRegisterAsync`
    purchase filter) and `TaxFilings/TaxFilingService.cs` (`GeneratePnd30Async`'s `vi`
    query, **and** `InputVatRegisterAsync` — same bug, same file, added `&&
    v.HasInputVat` there too though the dispatch cited only the first two; it is the
    identical unguarded `VatAmount > 0m` filter feeding a real "Input VAT Register"
    report a user can view, so leaving it unfixed would ship an inconsistent sibling).
    All three now AND `v.HasInputVat` onto the existing `v.VatAmount > 0m` filter.
  - **H10** (box-12 double-count): `TaxFilings/TaxFilingService.cs:77`
    `CreditCarryForward` changed from `net < 0m ? -net : 0m` to a flat `0m` (Phase-1 —
    no prior-period carry tracking exists). Verified consumers first: only
    `Pdf/Pnd30FormFiller.cs` (box 10/12 math) and the `Pnd30Filing`/`Pnd30Lines` DTOs
    read this field; `Pp30BatchFormat.cs:29`'s `Branch` record has **no**
    `CreditCarryForward` field at all — the opus-verify citation there is a *comment*
    explaining why the batch format derives ข้อ8/9 independently, not an actual
    consumer — so nothing else could break from zeroing it.
  - **M10** (CN/DN category): `TaxFilings/SalesCategorizer.cs` — design check first:
    `TaxAdjustmentNote.OriginalTaxInvoiceId` is a mandatory (non-nullable) `long`, so a
    clean source category is ALWAYS derivable (no fallback branch needed). Notes now
    route to the ORIGINAL TI's own doc-level category (TAXABLE if header `TaxAmount >
    0`, else EXEMPT/ZERO_RATED by the TI's line tax-codes), mirroring the identical
    `CategoryOf` rule already used for TIs themselves in
    `TaxFilingService.OutputVatRegisterAsync`. Known limitation (per dispatch, noted
    here): a mixed-category original TI collapses to one doc-level label — same
    accepted Phase-1 simplification already in use for TIs elsewhere in this codebase.
  - New test file `tests/Accounting.Api.Tests/Hardening/Pnd30CorrectnessTests.cs` (3
    tests, one per finding), each on a **fresh** `TestCompanyFactory` company (company 1
    never touched) with hand-computed expected values (arithmetic in code comments):
    - H9: VI posted with `HasInputVat: false` + a `DefaultIsRecoverableVat: true`
      category line (1000 × 7% → header `VatAmount` = 70.00, unfixed by H9 — that
      mismatch IS the bug) → asserts both `IVatReportService.GetPnd30Async` and
      `ITaxFilingService.GeneratePnd30Async` report `InputVat`/`InputVatTotal` = 0.
    - H10: TI (1000×7%→OutputVat 70.00) + VI (2000×7%→InputVat 140.00) in the same
      period → net credit 70.00. Asserts `CreditCarryForward == 0` (box 10 blank), then
      replicates `Pnd30FormFiller.Fill`'s own box 8/9/10/12 arithmetic against the real
      filing output and asserts box 12 = 70.00 exactly once (pre-fix would double to
      140.00).
    - M10: TI with 1 zero-rated line (tax code `VAT-OUT-0-EXP`, 5000.00) + a Credit
      Note against it (1000.00, TaxAmount 0 since the original carried no VAT) →
      asserts `SalesZeroRated.Amount == 4000.00` (5000 − 1000) and
      `SalesTaxable.Amount == 0` (untouched).
  - Evidence: all 3 new tests pass 2× consecutive post-fix. Regression-proved by
    `git stash`-ing the 3 source fixes (test file untouched) and re-running: all 3
    FAILED against the pre-fix code with exactly the hand-computed pre-fix values
    (`SalesTaxable.Amount` = **-1000.0000** instead of 0; `CreditCarryForward` =
    **70.0000** instead of 0; `pnd30.InputVat` = **70.0000** instead of 0) — then
    `git stash pop` restored the fixes and all 3 passed again. Build 0 errors / 0
    warnings both before and after. Full `Accounting.Api.Tests` suite post-fix: 590
    total / 582 passed / 8 skipped / **0 failed** (clean run — the documented
    pre-existing rotating shared-DB flaky did not surface this pass).

### H2 — Workers per-company tenant pin (VAT snapshot only — M3 e-Tax is a separate follow-up)
- [x] Done 2026-07-04, per `specs/design-h2-workers-tenant.md` exactly (Codex-reviewed design,
  4 flaws already fixed in the doc). No deviations from the design.
  - **New** `Accounting.Workers/Tenancy/WorkerTenantContext.cs`: mutable `ITenantContext`
    (settable `CompanyId`/`BranchId`; `UserId` null; `Username` "system"; `IsSuperAdmin` always
    `false` — each scope is pinned to exactly one company, so the EF filter must enforce
    `CompanyId` equality, not bypass it). Confirmed (per design) no reusable mutable impl
    existed — `HttpTenantContext` is computed-from-HTTP-claims, `StubTenant` is test-only.
  - `Accounting.Workers/Program.cs`: registered `services.AddScoped<WorkerTenantContext>()` +
    `services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<WorkerTenantContext>())` —
    NOT in `AddInfrastructure` (Api keeps `HttpTenantContext` untouched, per design). The
    interface now resolves to the SAME scoped concrete instance the job loop mutates
    (Codex flaw #1).
  - `Accounting.Workers/Jobs/VatRegisterSnapshotJob.cs`: refactored from a captured
    `IVatReportService` to `IServiceScopeFactory` (Codex flaw #4). `Execute()` first reads
    `master.companies` (not `ITenantOwned`, no RLS — a plain, legitimately cross-tenant read,
    no super-admin pin needed) for the active-company id list from one short-lived scope, then
    for EACH company: creates a fresh child scope (fresh `WorkerTenantContext` + fresh
    `AccountingDbContext` — no cross-company bleed on a shared instance, Codex flaw #2), sets
    `WorkerTenantContext.CompanyId`, resolves the scope's `AccountingDbContext` +
    `IVatReportService`, and calls the new `public static RunSnapshotAsync(db, report,
    companyId, year, month, ct)` helper extracted onto the job class. That helper wraps the
    whole per-company snapshot in one explicit transaction: `BeginTransactionAsync` →
    `set_config('app.company_id', <id>, true)` (LOCAL — auto-reverts on commit, no
    pooled-connection poison) → `report.GetPnd30Async(...)` → `CommitAsync` — mirrors
    `PermissionLookup.cs:28-52` exactly (Codex flaw #3, the riskiest). `RunSnapshotAsync` is
    `public` (not `internal`) specifically so the proving test can call it directly without
    needing `InternalsVisibleTo` plumbing.
  - **Necessary test-wiring beyond the 4 named files** (mechanical, not scope creep — same
    class of exception as Unit-3's `AssemblyInfo.cs`): `Accounting.Api.Tests.csproj` needed a
    `ProjectReference` to `Accounting.Workers.csproj` to compile the new test at all. Both
    `Accounting.Api` and `Accounting.Workers` are top-level-statement executables, so the
    compiler-synthesized `Program` type lives in the global namespace for both — an unaliased
    reference made `Program` ambiguous for the pre-existing `WebApplicationFactory<Program>`
    usages (`RbacApiFactory.cs`, `McpServerSmokeTests.cs`). Fixed with an **aliased**
    `ProjectReference` (`<Aliases>Workers</Aliases>`) + `extern alias Workers;` in the new test
    file only — a stdlib C#/MSBuild feature built for exactly this collision, no new
    abstraction invented.
  - New test `tests/Accounting.Api.Tests/Workers/VatRegisterSnapshotJobRlsTests.cs`
    (`RunSnapshotAsync_isolates_company_A_from_company_B_under_NOBYPASSRLS`, the spec's
    mandated "## Proving test"): seeds two fresh companies (`TestCompanyFactory`) each with one
    POSTED tax invoice carrying IDENTICAL amounts (100 subtotal / 7 tax, from the shared
    `SalesChainRlsTests.InsertMinimalTaxInvoiceAsync` helper) — chosen so a blend bug (200/14)
    and a fail-closed-with-no-pin bug (0/0) are both trivially distinguishable from the correct
    isolated result (100/7). `SET ROLE pg_database_owner` (rolbypassrls=false, the repo's
    non-bypass-role trick — same as `SalesChainRlsTests`/`ReviewHardeningRlsTests`) with
    `app.company_id` UNSET and `app.is_super_admin` explicitly cleared (defends against a
    pooled connection retaining a stale `true` from a prior test and producing a false green
    via the policy's super-admin bypass clause — the same hardening Codex asked for on H5).
    One physical connection kept open for the whole block (`OpenConnectionAsync`) so `SET ROLE`
    sticks across `RunSnapshotAsync`'s own internal transaction — identical technique to
    `ApiKeyResolverRlsTests`. Then calls `VatRegisterSnapshotJob.RunSnapshotAsync` directly with
    `WorkerTenantContext.CompanyId` set to company A, asserting `Sales == 100.00m` and
    `OutputVat == 7.00m` (company A visible AND company B not blended in).
  - **Tier-2 (Codex) review correction:** fix APPROVED as-is (shared-instance DI ordering,
    LOCAL-tx pin, isolation, csproj extern-alias all PASS) — but the ORIGINAL test used
    IDENTICAL amounts (100/7) for both companies, so it could prove "not blended / not
    fail-closed-zero" but NOT that company A specifically was the visible one: a bug that
    isolated to the WRONG company (always B, regardless of the requested CompanyId) would
    read the same 100/7 and pass. Fixed test-only (source untouched): company B now seeded
    with DISTINCT amounts (300/21/321 vs A's 100/7/107) via a new local
    `InsertTaxInvoiceAsync` helper in the test file (parameterized subtotal/tax — the shared
    `SalesChainRlsTests.InsertMinimalTaxInvoiceAsync` hardcodes 100/7/107 for every caller and
    was deliberately left untouched so other RLS tests are unaffected). Assertions now check
    A's SPECIFIC values (`Sales == 100.00m`, `OutputVat == 7.00m`) plus explicit
    `NotBe(300.00m)`/`NotBe(21.00m)` guards documenting that B's distinct figures must not
    leak into A's snapshot (blend or wrong-company-swap both now fail this test).
  - **Evidence:** Passed (not Skipped) 2× consecutive both before AND after the Tier-2
    correction, ~0.8-1.0s each run, all four runs with `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the
    same command. Regression re-proved against the corrected (distinct-amount) test by
    temporarily commenting out the `set_config` pin call in `RunSnapshotAsync` (test file
    untouched) and rebuilding: re-ran → **FAILED** — `summary.Sales` was **0M** instead of
    100.00M (fail-closed RLS hid company A's own row with no pin, exactly the design doc's
    predicted failure mode) — then restored the pin, rebuilt, and the test passed again 2×
    more (confirmed via `git status`/direct file read that `VatRegisterSnapshotJob.cs` matches
    the Tier-2-approved fix exactly, no leftover temp edits). Build 0 errors/0 warnings
    throughout. Full `Accounting.Api.Tests` suite post-fix (pre-correction run, still valid —
    the correction only tightens the test, doesn't change source behaviour): 600
    total / 592 passed / 8 skipped / **0 failed** (clean run — the one documented rotating
    shared-DB flaky did not surface this pass; skip count matches the established baseline of
    8, confirming no test silently regressed to skipped).
  - **Blast radius:** 4 files named in the dispatch cap (`Program.cs`, `VatRegisterSnapshotJob.cs`,
    new `WorkerTenantContext.cs`, new test file) + 1 unavoidable mechanical csproj edit
    (`Accounting.Api.Tests.csproj` aliased ProjectReference, explained above). No schema/
    SqlScript changes. No `Accounting.Api` changes (M3/e-Tax untouched — separate follow-up
    per the design doc). `git commit` NOT run (per dispatch) — awaiting Tier-2 review.

### M3 — e-Tax retry candidate-scan pin (Api host, NOT Workers)
- [x] Done 2026-07-04, per the H2 spec's "## M3" section exactly. The per-item pipeline call
  (`pipeline.RunAsync(c.TaxInvoiceId, c.CompanyId, ct)`, already re-scoped by its own explicit
  `companyId`) and `ETaxSubmissionPipeline.cs` itself were NOT touched, per the dispatch.
  `Accounting.Infrastructure/ETax/ETaxRetryWorker.cs` (`RunDueAsync`): each read now pins
  `set_config('app.is_super_admin', 'true', true)` LOCAL to its own short transaction
  (`BeginTransactionAsync` → pin → query → `CommitAsync`), mirroring `ApiKeyResolver.cs` (H5) —
  auto-reverts on commit, never leaks onto the pooled connection, never wraps the pipeline call.
  - **Deviation from the literal dispatch, flagged rather than silently applied:** the dispatch
    named only the candidate-scan query (originally line 28) as needing the pin. On inspection,
    `RunDueAsync` has a SECOND unpinned cross-tenant read in the same method — the per-candidate
    "latest attempt" freshness check (originally lines 41-44, `db.ETaxSubmissions.IgnoreQueryFilters()
    .Where(s => s.CompanyId == c.CompanyId && s.TaxInvoiceId == c.TaxInvoiceId)...FirstAsync()`).
    Despite filtering by an explicit `company_id` in the LINQ `Where`, Postgres RLS is enforced
    as an independent `USING` clause server-side (`581_missing_tables_rls.sql`'s fail-closed
    `company_isolation` policy) — under NOBYPASSRLS with `app.company_id` unset, this second read
    would ALSO return zero rows, so `.FirstAsync(ct)` would throw for every candidate the
    now-fixed scan found. Pinning ONLY the first query would leave the retry silently broken
    one line later (an unhandled exception per tick, caught by the hosted service's outer
    try/catch and logged, but still processing 0 items) — the opposite of M3's actual goal.
    Fixed by giving this second read its OWN short pinned transaction too (same file, same
    pattern, same narrow scope — NOT a blanket transaction wrapping the pipeline call, which
    would have bypassed RLS for the pipeline's own writes across every company in the batch, a
    real security regression). Confirmed via the proving test: with only the first query pinned
    (not tested as an intermediate state, but logically necessary) vs. both — both queries needed
    pinning for `RunDueAsync` to actually re-attempt a seeded candidate end-to-end.
  - New test `tests/Accounting.Api.Tests/ETax/ETaxRetryWorkerRlsTests.cs`
    (`RunDueAsync_finds_and_reattempts_a_pending_submission_under_NOBYPASSRLS`): seeds one
    company (`TestCompanyFactory`) + one due, non-dead-letter `SendFailed` `etax.submissions`
    row (`retry_after` = 1 minute in the past; `tax_invoice_id` carries no FK on this table, so
    a fabricated id is used). `SET ROLE pg_database_owner` (NOBYPASSRLS, the repo's non-bypass-
    role trick) with `app.company_id` UNSET reproduces the exact prod pre-pin condition. The
    real `IETaxSubmissionPipeline` is replaced with an in-file `FakePipeline` test double that
    just records calls — deliberately NOT exercising the real pipeline, which is explicitly out
    of this fix's scope and has its own, separately-tracked RLS surface (`ETaxSubmissionPipeline.cs`
    also calls `IgnoreQueryFilters()` on RLS-protected tables — e.g. `sales.tax_invoices`,
    040_tax_invoice_immutability.sql — with no pin of its own; flagged here as a candidate for a
    FUTURE separate dispatch, not fixed, per "do NOT touch the per-item execution path"). Because
    the candidate scan is a legitimate cross-tenant read on the suite's shared, long-lived
    `teas_test` DB, the assertion checks `done >= 1` and that `FakePipeline.Calls` contains OUR
    specific `(taxInvoiceId, companyId)` tuple, rather than an exact total count (first attempt
    at an exact `== 1` assertion failed with `done == 16` — other tests' leftover due rows are
    legitimately, correctly picked up by the same cross-tenant admin scan).
  - **Evidence:** Passed (not Skipped) 2× consecutive, ~1s each run, both with
    `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the same command. Stash-proved: `git stash push -- 
    .../ETaxRetryWorker.cs` (test file untouched) reverted to the pre-fix code, rebuilt, re-ran →
    **FAILED** — `done` was **0** instead of ≥1, i.e. the fail-closed RLS policy hid the seeded
    row from the unpinned scan (exactly the reported prod symptom: "the retry silently finds
    nothing") — `git stash pop` restored the fix, rebuilt, and the test passed again 2× more
    (confirmed via `git status` that only the test file was untracked-new and
    `ETaxRetryWorker.cs`'s diff was back to the fix). Build 0 errors/0 warnings throughout. Full
    `Accounting.Api.Tests` suite post-fix: 601 total / 592 passed / 8 skipped / 1 failed
    (`Payroll.PayrollRunServiceTests.Pnd1_filings_follow_payment_date_not_period` — a file this
    diff never touches; re-ran in isolation → passed, confirming the documented pre-existing
    rotating shared-DB flaky, not a regression). Skip count (8) matches the established
    baseline; total (601) = prior 600 + this one new test.
  - **Blast radius:** exactly the 2 files named in the dispatch cap
    (`ETaxRetryWorker.cs` + one new test file). No schema/SqlScript changes. No `Accounting.Api`
    changes. `WorkerTenantContext` NOT registered in the Api host (per instruction).
    `git commit` NOT run (per dispatch) — awaiting review.

### M12 — audit.activity_log RLS (follow-up after H2 + M3)
- [x] Done 2026-07-04. New `585_audit_log_rls.sql` (DbInitializer-discovered, no registration list
  edit needed — the SqlScripts folder is glob-scanned + copied to output on build). Mirrors
  `581_missing_tables_rls.sql`'s ENABLE + FORCE + `company_isolation` shape for the single
  `audit.activity_log` table, with an added `OR company_id IS NULL` arm (the table's `company_id`
  is nullable — system-wide rows) alongside the pinned-company and super-admin arms. Idempotent
  (`DROP POLICY IF EXISTS`; `ENABLE`/`FORCE ROW LEVEL SECURITY` are safe to re-run).
  - **CRITICAL SAFETY VERIFICATION (done FIRST, before writing the script, per the dispatch):**
    the policy's `USING` is also the INSERT `WITH CHECK` (none given separately), so every
    audit-write path had to be checked for a company_id that could mismatch the pinned
    `app.company_id`. Traced every writer:
    - `ActivityRecorder.Record` (the ~40 call sites across Sales/Purchase/Payroll/Master/
      Identity/OAuth) always writes an explicit non-nullable `int companyId` — never `NULL`.
      For ordinary per-document actions (TaxInvoice/Receipt/PurchaseOrder/PayrollRun/etc.) that
      id is the entity's OWN `CompanyId`, loaded via the standard tenant-scoped query — for a
      non-super-admin this is ALWAYS equal to the pinned `app.company_id` (the EF global filter
      makes it structurally impossible to load a foreign-company entity without
      `IgnoreQueryFilters()`, which none of these read paths use).
    - THREE call sites can legitimately write a `companyId` DIFFERENT from the pinned
      `tenant.CompanyId`: `CompanySwitchService.SwitchAsync` (audits the TARGET company being
      switched into, not the caller's current one), `RbacAdminService`'s
      `ResolveTargetCompany`-driven writes (a super-admin may pass any `requested` company id
      for cross-company RBAC management), and `OAuthEndpoints` POST `/oauth/authorize` accept
      (a super-admin may grant an MCP token `company_id` for any active company via the posted
      form). ALL THREE are gated behind `tenant.IsSuperAdmin` in C# BEFORE the mismatch is ever
      possible (`CompanySwitchService`: explicit `if (!tenant.IsSuperAdmin) throw`;
      `RbacAdminService.ResolveTargetCompany`: non-super callers get `throw
      rbac.cross_company.scope_required` the instant `requested != tenant.CompanyId`;
      `OAuthEndpoints`: non-super callers require `tenant.CompanyId == companyId` or `Forbid()`).
      `TenantMiddleware.cs:34-38` pins `app.is_super_admin` **session-wide for the entire
      request** (not per-query) from that SAME `tenant.IsSuperAdmin` flag — so at the exact
      moment any of these three writes could carry a mismatched `company_id`, Postgres's
      `app.is_super_admin` GUC is ALREADY `'true'` for that connection, satisfying the policy's
      `OR is_super_admin` bypass arm regardless of the value written. Verified this is not
      circular reasoning: the C#-level gate and the DB-level bypass arm key off the identical
      boolean, pinned once per request by the one middleware that owns `set_config`.
    - Two direct `db.Set<ActivityLog>().Add(...)` bypasses of `ActivityRecorder` exist
      (`ApiKeyService.AuditAsync`, `PrintTrackingService.Log`) — the former writes
      `_tenant.CompanyId` directly, the latter writes a tenant-scoped entity's own `CompanyId`;
      same safety argument applies to both.
    - No SQL seed script inserts into `audit.activity_log` directly (grep-confirmed across
      `Migrations/SqlScripts`). Neither `Accounting.Workers` (H2) nor the e-Tax retry path
      (`ETaxRetryWorker`/`ETaxSubmissionPipeline`, M3) writes to this table at all today
      (grep-confirmed no `IActivityRecorder`/`ActivityRecorder` usage in either) — H2/M3 landing
      first was a sequencing precaution per the design, not a path this table's policy actually
      depends on today.
    - No path currently writes `company_id = NULL` (no "system row" producer exists in the
      codebase yet) — the `OR company_id IS NULL` arm is forward-looking per the design; it is
      exercised by this dispatch's proving test via a direct INSERT, not by any live app path.
    - **Verdict: no STOP needed.** Every write whose `company_id` could mismatch the pinned
      `app.company_id` is provably covered by the super-admin bypass arm at the moment it runs;
      every other write's `company_id` is structurally guaranteed to equal the pin. Proceeded to
      write the script as designed, unmodified.
  - Fixture-boot re-verified empirically (not just by static analysis): ran the existing
    `ReviewHardeningRlsTests` suite (which boots the shared `teas_test` fixture and applies any
    un-applied `SqlScripts`, including the new 585) — 5/5 passed, 585 applied cleanly with no
    seed-audit-INSERT failure. Then ran the FULL `Accounting.Api.Tests` suite (594 tests
    exercising effectively all the write paths above) post-585: 0 failures — empirical
    confirmation alongside the static trace.
  - New test `tests/Accounting.Api.Tests/Persistence/AuditLogRlsTests.cs`
    (`Company_A_row_visible_to_A_invisible_to_B_and_legitimate_writes_succeed`): under
    `SET ROLE pg_database_owner` (NOBYPASSRLS) pinned to company A —
    (a) two-directional visibility: A's own seeded row visible, B's seeded row invisible;
    (b) a pinned tenant-row INSERT (`company_id = A`) succeeds, AND a NULL-company "system" row
    INSERT ALSO succeeds while A stays pinned (proves the nullable-company arm doesn't block
    legitimate writes); plus a bonus negative-space check (mirrors the two-directional discipline
    used by `SalesChainRlsTests`) — an INSERT with a MISMATCHED company_id (B's, while pinned to
    A) is rejected with `PostgresException` SqlState `42501` (RLS `WITH CHECK` violation),
    proving the policy isn't simply wide open.
  - **Evidence:** Passed (not Skipped) 2× consecutive, ~0.7s each run, both with
    `TEAS_TEST_PG`+`TEAS_REPO_ROOT` in the same command. Regression-proved with a temporary
    throwaway test method (added, run once, then deleted — never left in the tree): disabled RLS
    on `audit.activity_log`, re-ran the seed+pin+visibility check inline, confirmed company B's
    row WAS visible without the policy (`bVisible == 1`, i.e. the real test's "invisible to B"
    assertion would have failed pre-585) — then re-enabled `ENABLE`+`FORCE ROW LEVEL SECURITY` in
    a `finally` block, confirmed the real test passed again immediately after, THEN deleted the
    throwaway method and re-ran the real test 2× more (both green) to leave the tree clean.
    Build 0 errors/0 warnings throughout. Full `Accounting.Api.Tests` suite post-fix: 602
    total / 594 passed / 8 skipped / **0 failed** (clean run — no rotating flaky surfaced this
    pass; total = prior 601 + this one new test).
  - **Blast radius:** exactly 2 files (new SqlScript + new test), as instructed. No C# source
    changes (no application code touched — this is a pure DB-layer backstop, same class of
    change as H1/581). `git commit` NOT run (per dispatch) — awaiting review.

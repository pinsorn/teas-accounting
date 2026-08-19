# Spec: Super Admin must be scoped to the SELECTED company

## Problem (confirmed on prod 2026-07-08, ham_chatsang / isSuperAdmin=true)
Super admin sees the UNION of all companies' data regardless of the company
selected in the switcher. Quotations, customers, dashboard revenue identical
across Repttown (companyId=2) and พงศ์สันต์ (companyId=3). Bonus symptom: opening
company A's document while switched to company B renders B's letterhead.

Root cause — tenant isolation deliberately exempts super admin at BOTH layers:
1. EF global query filter `AccountingDbContext.cs:152`:
   `e => _tenant == null || _tenant.IsSuperAdmin || e.CompanyId == _tenant.CompanyId`
2. RLS policy on every business table (010_rls_policies.sql + all later *_rls.sql):
   `USING (company_id = current_setting('app.company_id')::INT
           OR current_setting('app.is_super_admin')::BOOLEAN)`
   `TenantMiddleware.cs` pins `app.is_super_admin` per request from the user's flag.

NOT env-specific (code-level, all environments). Normal users unaffected.

## Goal
Super admin reads/writes are ALWAYS scoped to the currently selected company
(`tenant.CompanyId`). Cross-company access happens only by switching company
(switch-company already validates allowedCompanies). Super-admin-ness governs
ADMIN capabilities (user mgmt, company mgmt, switcher membership), not data scope.

---

## Context / footguns (READ FIRST — do not rediscover)
- **Superuser masks RLS.** teas_test/dev connect as a Postgres SUPERUSER → RLS is
  bypassed, so an RLS-behaviour test written the naive way passes VACUOUSLY. Any
  RLS test here MUST drop to a non-bypassing role. **Use the portable trick already
  used by `SalesChainRlsTests` / `ReviewHardeningRlsTests`: `SET ROLE pg_database_owner`**
  (troubles-wiki "New RLS test SKIPs …"). Do NOT use the newer `teas_rls_test`
  role — it `[SKIP]`s when `PostgresFixture.RlsRoleSkip` is set (no CREATEROLE), and
  a SKIP fakes a green run. Verify skip count vs baseline (~8) after your run.
- **`FORCE ROW LEVEL SECURITY`** is on every one of these tables → even the table
  OWNER is subject to the policy. Pure DDL (ALTER/CREATE POLICY) still runs fine as
  owner; only DML (INSERT/SELECT) is gated. The new script is DDL-only.
- **`set_config(key, val, is_local)`**: `is_local = true` = transaction-scoped,
  auto-reverts on COMMIT/ROLLBACK, never leaks onto the pooled connection.
  `is_local = false` = SESSION-scoped, rides the pooled connection until reset (this
  is what TenantMiddleware uses for `app.company_id`, and the L4 finally-reset +
  ClearPool exists because of it). **`app.bypass_rls` MUST ALWAYS be set with
  `is_local = true`.** Never session-scope it, never set it from a user's identity.
- **PostgresFixture applies each SQL seed ONCE** (tracked in `sys.applied_sql_scripts`).
  New script runs at API startup on both fresh AND already-seeded DBs → must be
  idempotent (`DROP POLICY IF EXISTS` + `ENABLE/FORCE` are safe to re-run).
- **Numeric ordering.** Scripts apply in filename order; all current prefixes are
  3-digit, highest is `591_seed_pg_trgm.sql`. The new script recreates policies first
  created in 010..585, so it MUST sort LAST → use `600_superadmin_scoped_rls.sql`.
- **Immutability is enforced by TRIGGERS, not by the super-admin arm.** Files named
  `*_immutability*.sql` (040/060/570/571) contain BOTH a `fn_..._no_delete_posted`
  trigger AND a `company_isolation` RLS policy. The `is_super_admin` arm lives ONLY in
  the `company_isolation` policy — removing it does NOT affect immutability. The new
  script recreates ONLY the `company_isolation` policy; it must NOT touch any trigger.
- **`master.companies` and `sys.users` / `sys.user_roles` have NO RLS.** So the
  switcher / allowedCompanies / user-management / bootstrap paths that read them via
  `IgnoreQueryFilters()` are UNAFFECTED — they use `IsSuperAdmin` only as a C# authz
  flag. (Verified: RefreshTokenRevalidationHandler, BootstrapAdminEndpoints,
  MasterDataServices, CompanyProfileService, CompanyTaxConfigService, UserRepository.)
- **Super admin's `companyId = 0` when unswitched** (LoginService: `assignment?.CompanyId ?? 0`).
  After this fix, an unswitched super admin pins `app.company_id='0'` → matches no
  real company → sees ZERO rows until they switch. Correct/intended; the FE onboarding
  layout already auto-switches `isSuperAdmin && companyId===0` users. Cover in tests.
- **`teas_test` may be bloated** (~629 companies from apply-once seeds). Tests must
  seed their OWN 2 companies via `TestCompanyFactory` (which reads back the seeded
  `"00000"` HQ branch — composite unique `(company_id, branch_code)`, do not hand-insert).
- **Prod deploy runs SqlScripts at API startup** (memory: teas-prod-deploy-plink) →
  DB backup MANDATORY before deploy.

---

## Design decision (the architecture)
Sever "user is a super admin" (a JWT claim / C# capability) from "this DB transaction
may cross tenants" (a session concern). The latter becomes a NEW, service-only GUC
**`app.bypass_rls`**, ALWAYS set `is_local = true`, NEVER set by TenantMiddleware,
NEVER derived from a user's identity, NEVER carried on an MCP token.

- Data scope for every request is driven SOLELY by `app.company_id`. Super admin is
  scoped exactly like any user; switching company re-pins `app.company_id`.
- The `OR is_super_admin` arm is removed from EVERY `company_isolation` policy. The
  `app.is_super_admin` GUC is RETIRED (no policy references it; TenantMiddleware stops
  pinning it). `is_super_admin` survives ONLY as the `sys.users` column + the JWT claim
  + `ITenantContext.IsSuperAdmin` (admin capability) — never as a data-scope lever.
- A small set of legitimate cross-tenant SERVICE/ADMIN paths (Family B, below) keep a
  bypass, but now EXPLICIT + LOCAL: each wraps its cross-company DB work in a
  transaction that pins `app.bypass_rls='true'` (is_local). Only the 6 tables those
  paths touch carry an `OR app.bypass_rls` arm. NONE of the user-facing business-data
  tables (quotations, customers, invoices, receipts, …) carry any bypass arm → the
  reported bug surface is fully closed.

**Why a uniform bypass (not per-op `app.company_id=<target>` pins):** RbacAdminService
must read a role BY `roleId` to even DISCOVER which company owns it — that read is
itself RLS-gated, so it cannot pre-pin the target company (chicken-and-egg, mirrors
ApiKeyResolver resolving a key before its company is known). A uniform LOCAL
`app.bypass_rls` at each Family B site (one helper, one mechanism) is therefore both
necessary for RBAC and lower implementation-regression risk than mixing mechanisms.
Accepted trade-off: bypass arm on 6 tables vs the theoretical 4. Tighter alternative
(company_id pin for CompanySwitch/OAuth branch+audit, dropping branches/audit from the
bypass set) is documented at the end of D2 — do NOT mix mechanisms without re-speccing.

---

## D1 — Inventory & classification of every `IsSuperAdmin` / `app.is_super_admin` ref
- [x] Confirmed complete (grep both spellings across `backend/`). Classes:
  **(A)** must LOSE data-scope bypass · **(B)** legit cross-tenant → explicit LOCAL
  `app.bypass_rls` · **(C)** unaffected (C# capability flag / non-RLS table / MCP strip).

### (A) — the bug. Remove the data-scope bypass.
| Ref | Action |
|---|---|
| `AccountingDbContext.cs:152` EF filter `_tenant.IsSuperAdmin ||` | D3 — delete the arm |
| `TenantMiddleware.cs:35,47` pins `app.is_super_admin` from `tenant.IsSuperAdmin` | D4 — stop pinning it |
| ~37 `company_isolation` policies with `OR …is_super_admin` (010,040,060,200,322,323,430,480,500,510,570,571,572,573,581,585) | D2 — recreate WITHOUT the arm |

### (B) — legit cross-tenant. Convert to explicit LOCAL `app.bypass_rls`.
| Path | Today | Change |
|---|---|---|
| `ApiKeyResolver.cs:49` key-by-prefix lookup on `sys.api_keys` (company UNKNOWN) | `set_config('app.is_super_admin','true',true)` | rename GUC → `app.bypass_rls` |
| `ApiKeyResolver.cs:71` HQ-branch read on `master.branches` (company = `key.CompanyId`) | same | rename GUC → `app.bypass_rls` |
| `ApiKeyResolver.cs:95` `LastUsedAt` `ExecuteUpdate` on `sys.api_keys`, NO tx/pin | none | **pre-existing** silent no-op under NOBYPASSRLS; OUT OF SCOPE (this fix neither creates nor worsens it). Optional: wrap in a bypass tx. Note in attempt log, do not fix silently. |
| `ETaxRetryWorker.cs:44` enumerate due submissions across ALL companies | `set_config('app.is_super_admin','true',true)` | rename GUC → `app.bypass_rls` |
| `ETaxRetryWorker.cs:65` per-item latest-check on `etax.submissions` | same | rename GUC → `app.bypass_rls` |
| `CompanySwitchService.cs:43` target `master.branches` read + `:64` audit write to `audit.activity_log` (company = target) | relies on SESSION `is_super_admin` (585 comment) | ADD LOCAL `app.bypass_rls` tx around branch-read AND audit-write; fix stale comment L62 ("activity_log has no RLS" — it DOES since 585) |
| `OAuthEndpoints.cs:108` target `master.branches` read + `:143` audit write (company = target) | relies on SESSION `is_super_admin` | ADD LOCAL `app.bypass_rls` tx around branch-read AND audit-write |
| `RbacAdminService.cs` cross-company `sys.roles`/`sys.role_permissions` reads/writes + audit writes (every `ResolveTargetCompany` path: role get/create/update/delete, grant change, replace-user-roles, etc.) | relies on SESSION `is_super_admin` | ADD LOCAL `app.bypass_rls` tx around each cross-company method's DB work. NOTE: roles are NOT `ITenantOwned` (no EF filter) so `IgnoreQueryFilters` grep MISSES these — audit every `ResolveTargetCompany` call site |
| `VatRegisterSnapshotJob.cs:100` pins `app.is_super_admin='false'` (defensive) | pins company_id + false | DROP the `is_super_admin` half; keep only `set_config('app.company_id',{0},true)` |
| **VERIFY** `ETaxSubmissionPipeline.cs` / `ETaxXmlBuilder.cs` (`IgnoreQueryFilters` on `TaxInvoices`/`Customers`/`ETaxSubmissions`) when driven by the background worker | claimed self-scoped by explicit companyId | **RESOLVED (no code change) — see attempt log.** It does NOT pin `app.company_id` or any bypass itself, and it does NOT ride a leftover session `is_super_admin` either (that GUC is already reverted — `is_local=true` — by the time `pipeline.RunAsync` executes, both before and after this fix). It relies on NOTHING: under a real NOBYPASSRLS prod connection with an empty/reset `app.company_id`, the `TaxInvoices`/`Customers` reads inside `RunAsync`/`BuildTaxInvoiceXml` return ZERO rows regardless of the `OR is_super_admin` arm (which evaluates `OR false` at that point) — same class as the already-out-of-scope `ApiKeyResolver.cs:95` gap. This is a PRE-EXISTING, worker-path-only gap, orthogonal to `is_super_admin` retirement: behavior is IDENTICAL before and after 600 (0 rows either way). NOT treated as (B) — fixing it would add `ETaxSubmissionPipeline.cs` to the blast radius (not in the 9-file cap) for a bug this fix neither creates nor worsens. Left OUT OF SCOPE, flagged for a follow-up spec. |

### (C) — unaffected. KEEP as-is (capability flag / non-RLS / MCP strip).
`AmbientTenantContext.cs:69-70` (computes flag from claim) · `ITenantContext.cs:17` ·
`TenantClaims.cs:8` · `IJwtTokenIssuer.cs:10` · `RbacAdminDtos.cs:35` ·
`LoginService.cs:80,92` (allowedCompanies decision; reads non-RLS) ·
`OAuthEndpoints.cs:101,124` (allowed-companies + scope grant — C# flag; the branch READ
at :108 is the (B) part) · `CompanySwitchService.cs:27,58` (403 gate + re-issued token
keeps caller's `IsSuperAdmin`) · `RefreshTokenRevalidationHandler.cs:52,62` (reads
`sys.users`, no RLS) · `McpPrincipalFactory.cs` / `McpBearerClaimsTransform.cs` (STRIP
`is_super_admin` from MCP tokens — MCP already tenant-scoped) ·
`PermissionLookup.cs` (already pins `app.company_id` LOCAL — the MODEL pattern; unaffected) ·
`PublicPdfTenantMiddleware.cs` (pins `app.company_id`, no IgnoreQueryFilters) ·
`MasterDataServices.cs:309` / seed scripts setting the `sys.users.is_super_admin`
COLUMN (130,160,181,400,440,550,562) — the user flag, not the GUC.

---

## D2 — New SqlScript `600_superadmin_scoped_rls.sql`
- [x] Created `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/600_superadmin_scoped_rls.sql`
      recreating EVERY `company_isolation` policy, SQL fragment copied verbatim from this spec. 3 shapes:
  - **G1 `company_id = pinned` (no bypass)** — the 31 business-data tables.
  - **G2 `company_id = pinned OR bypass_rls`** — `sys.api_keys`, `etax.submissions`, `master.branches`.
  - **G3 `company_id IS NULL OR company_id = pinned OR bypass_rls`** — `sys.roles`,
    `sys.role_permissions`, `audit.activity_log` (NULL = system-global rows).
- [x] Recreates ONLY the `company_isolation` policy per table (leave immutability/append
      triggers untouched). Idempotent, DDL-only, sorts last (`600` > `591`).
- [x] Verified at the DB via a throwaway Npgsql console against `teas_test` (after the test
      fixture applied 600): `SELECT tablename, policyname FROM pg_policies WHERE qual ILIKE
      '%is_super_admin%'` → **0 rows**. `... ILIKE '%bypass_rls%'` → exactly 6 rows
      (activity_log, api_keys, branches, role_permissions, roles, submissions — G2+G3). Total
      `company_isolation` policies = 37 (31+3+3, matches the authoritative list). `sys.applied_sql_scripts`
      confirms `600_superadmin_scoped_rls.sql` applied.
- [x] Re-derived the full table list by grepping `is_super_admin` across every SqlScript — the
      ONLY files containing the actual policy arm (`OR COALESCE(NULLIF(current_setting('app.is_super_admin'`)
      are exactly: 010, 040, 060, 200, 322, 323, 430, 480, 500, 510, 570, 571, 572, 573, 581, 585 —
      16 files, an EXACT match to the spec's authoritative list (010/581 each cover multiple tables
      via a DO-loop over an array, same shape as 600 itself). No missed table — confirmed empirically
      by the DB-level check above (0 rows). (Other grep hits in 130/160/181/400/440/550/562 are the
      `sys.users.is_super_admin` COLUMN in seed INSERT statements — (C), correctly untouched.)

Authoritative table list (verified 2026-07-08):
- **G1 (31):** master.chart_of_accounts, master.customers, master.vendors,
  master.employees, master.products, master.business_units, sys.expense_categories,
  sys.number_sequences, sys.idempotency_keys, sys.attachments, tax.tax_codes,
  tax.wht_types, tax.wht_certificates, tax.tax_filings, tax.cit_year_summaries,
  tax.cit_adjustments, gl.journal_entries, gl.accounting_periods, sales.tax_invoices,
  sales.receipts, sales.tax_adjustment_notes, sales.billing_notes,
  sales.billing_note_tax_invoices, sales.quotations, sales.sales_orders,
  sales.delivery_orders, purchase.vendor_invoices, purchase.payment_vouchers,
  purchase.purchase_orders, payroll.payroll_runs, payroll.payslips.
- **G2 (3):** sys.api_keys, etax.submissions, master.branches.
- **G3 (3):** sys.roles, sys.role_permissions, audit.activity_log.

Exact SQL (critical fragment — copy verbatim, keep the `NULLIF/COALESCE` casts identical
to the originals so unset GUCs are FALSE, not an error):

```sql
-- 600_superadmin_scoped_rls.sql
-- Bugfix 2026-07-08: super admin saw the UNION of all companies. Every company_isolation
-- policy carried `OR app.is_super_admin`, and TenantMiddleware pinned that GUC from the
-- logged-in user's flag. This recreates EVERY company_isolation policy so data scope is
-- driven SOLELY by app.company_id. Legitimate cross-tenant service/admin paths use the
-- NEW, LOCAL-only `app.bypass_rls` GUC (never set from a user identity, never by
-- TenantMiddleware). Only the tables those paths touch keep an `OR app.bypass_rls` arm.
-- Idempotent (DROP POLICY IF EXISTS + ENABLE/FORCE re-runnable). DDL only. MUST sort last.
-- Recreates ONLY the company_isolation POLICY — immutability triggers in 040/060/570/571
-- are untouched. Applied once by DbInitializer.ApplyScriptsAsync.

-- G1 — tenant-data tables: scope strictly to the pinned company. NO bypass arm.
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
    'master.chart_of_accounts','master.customers','master.vendors','master.employees',
    'master.products','master.business_units','sys.expense_categories','sys.number_sequences',
    'sys.idempotency_keys','sys.attachments','tax.tax_codes','tax.wht_types',
    'tax.wht_certificates','tax.tax_filings','tax.cit_year_summaries','tax.cit_adjustments',
    'gl.journal_entries','gl.accounting_periods','sales.tax_invoices','sales.receipts',
    'sales.tax_adjustment_notes','sales.billing_notes','sales.billing_note_tax_invoices',
    'sales.quotations','sales.sales_orders','sales.delivery_orders','purchase.vendor_invoices',
    'purchase.payment_vouchers','purchase.purchase_orders','payroll.payroll_runs','payroll.payslips'
];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                );
        $pol$, tbl);
    END LOOP;
END $$;

-- G2 — service-scanner tables: pinned company OR the explicit LOCAL service bypass.
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY['sys.api_keys','etax.submissions','master.branches'];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.bypass_rls', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END $$;

-- G3 — system-global tables: NULL company = global row (visible to all), else pinned,
-- OR the LOCAL service bypass (RBAC cross-company mgmt / cross-company audit writes).
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY['sys.roles','sys.role_permissions','audit.activity_log'];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.bypass_rls', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END $$;
```

WITH CHECK note: originals declare USING only → Postgres reuses USING as the INSERT/UPDATE
WITH CHECK. Keeping USING-only preserves that. Result: write-side is now STRICTER for
super admin (`company_id = pinned` unless an explicit LOCAL bypass is active) and
UNCHANGED for normal users. This satisfies the "at least as strict, stricter for super
admin" constraint.

Tighter alternative (NOT chosen — document only): drop `master.branches` and
`audit.activity_log` from G2/G3 and have CompanySwitch/OAuth pin `app.company_id=<target>`
LOCAL for their branch-read + audit-write. Rejected to keep ONE bypass mechanism across
Family B (lower regression risk; RbacAdminService needs bypass regardless).

## D3 — EF global query filter
- [x] `AccountingDbContext.cs:152` → `.HasQueryFilter(e => _tenant == null || e.CompanyId == _tenant.CompanyId);`
      (deleted `_tenant.IsSuperAdmin ||`). Kept the `_tenant == null` arm (migration/design-time).
- [x] Updated the stale XML doc at `:130-133` — no longer claims super admins bypass the filter.
- [x] No per-entity carve-out needed: every Family B cross-company READ already uses
      `IgnoreQueryFilters()` (or reads non-`ITenantOwned` roles), so the EF layer never
      blocks them; their RLS access is handled by the LOCAL `app.bypass_rls` pin.
- **Fallout found + fixed:** `OnboardingFoundingAddressTests.cs` (`CreateAsync_seeds_full_chart_of_accounts_for_gl_posting`,
  `CreateAsync_creates_head_office_branch`) read back a FRESHLY-created OTHER company's
  `ChartOfAccounts`/`Branches` (both `ITenantOwned`) via `new StubTenant { CompanyId = 1,
  IsSuperAdmin = true }` — previously the `IsSuperAdmin` arm made the EF filter a no-op so the
  explicit `.Where(x => x.CompanyId == companyId)` did the real scoping; after D3 the filter now
  ALSO enforces `CompanyId == 1`, ANDing with the explicit predicate → always empty. Confirmed by
  running the full suite BEFORE this fix (2 failures, "found 0"/"could not find codes") and AFTER
  (both green). Fixed by adding `.IgnoreQueryFilters()` to both reads (the test's own comment
  already documented the INTENT as "bypasses the company filter" — now stated explicitly instead
  of leaning on the retired `IsSuperAdmin` bypass). This is the outside-the-9-file-cap test file
  the spec's ~8-file test budget absorbs (see Attempt log).

## D4 — Fate of `app.is_super_admin` GUC + TenantMiddleware pinning
- [x] `TenantMiddleware.cs:34-38` → pins ONLY company: `set_config('app.company_id', {0}, false)`
      (dropped the second `set_config(...is_super_admin...)`).
- [x] `TenantMiddleware.cs:46-47` finally-reset → `set_config('app.company_id', '', false)` only.
- [x] The `app.is_super_admin` GUC is RETIRED. Confirmed by DB check (D2: 0 policies reference it)
      and by grep (D1 below): no remaining C# `set_config`/`current_setting` on it outside old
      SqlScripts' historical text. (`app.bypass_rls` is never session-pinned or reset — LOCAL-only,
      auto-reverting — so the finally-reset need not clear it.)
- [x] Optional guard test: SKIPPED — the spec marks it optional, and D2's DB-level check + the
      exhaustive source grep (D1) already cover the same invariant empirically. — OBSOLETE per
      triage 2026-08-19 (invariant covered by D1 grep + D2 0-policy check)

## D5 — Letterhead symptom
- [x] No dedicated code change. With correct scoping, fetching company A's document while
      pinned to company B → RLS returns 0 rows → the detail endpoint's `First/SingleOrDefault`
      is null → 404 (never renders A under B's letterhead). Confirmed the sales document detail
      endpoint maps "row not found" → 404 (`SalesChainEndpoints.cs`: `d is null ? Results.NotFound()
      : Results.Ok(d)`). Cross-company-detail-fetch → 404 assertion added in D6's new HTTP
      integration test (`CompanySwitchTests.Quotations_are_scoped_to_the_switched_into_company_over_http`).

## D6 — Test plan (failing test FIRST)
- [x] **New RLS test** — `backend/tests/Accounting.Api.Tests/Persistence/SuperAdminTenantScopeRlsTests.cs`.
      Seeds 2 companies via `TestCompanyFactory` (customer already seeded per company by the
      factory); inserts a quotation per company. Pins `app.company_id=<B>` + `app.is_super_admin='true'`
      (exactly what `TenantMiddleware` pinned for a real super-admin request — NO `app.bypass_rls`,
      that GUC doesn't exist pre-fix and must not matter post-fix) via `SET ROLE pg_database_owner`.
      CONFIRMED FAILS TODAY for the right reason: `Super_admin_pinned_to_company_B_sees_only_B_quotations_and_customers`
      and `Cross_company_quotation_detail_fetch_returns_zero_rows` both fail — "Expected ... to be 0L
      ... but found 1L" — company A's quotation leaks through the `OR is_super_admin` arm while
      pinned to B. (2 Failed, 0 Passed, run 2026-07-08.)
- [x] **Integration (WebApplicationFactory, `UseSetting` for Jwt/ConnectionStrings):** added
      `CompanySwitchTests.Quotations_are_scoped_to_the_switched_into_company_over_http` — super
      admin starts at A, switches to B via `/auth/switch-company/{id}`, `GET /quotations` returns
      ONLY B's quotation (not A's), `GET /quotations/{aQuotationId}` → 404 (letterhead symptom
      gone), unswitched super admin (companyId=0) → `GET /quotations` returns an empty array.
      Uses `RbacApiFactory` (already `UseSetting`-based per its own doc comment). PASSED (6/6 in
      `CompanySwitchTests`, isolated run).
- [x] **Non-super-admin unchanged:** existing `SalesChainRlsTests` / `ReviewHardeningRlsTests`
      still green (full-suite run, unedited).
- [x] **Family B still works** (regression gates, full-suite run all green): `CompanySwitchTests`
      (switch succeeds — branch read + audit write under LOCAL bypass); `ApiKeyResolverRlsTests`
      (auth resolves, migrated to `app.bypass_rls`); `ETaxRetryWorkerRlsTests` (scan finds all
      companies' due rows, migrated); `VatRegisterSnapshotJobRlsTests` (reworked — its premise
      "job forces is_super_admin=false" is gone; now proves G1 has no bypass arm at all + the job
      never touches the retired GUC); `AuditLogRlsTests` (cross-company audit write still
      permitted under bypass, migrated); RBAC cross-company management (`RbacAdminServiceTests`,
      unedited, passes against the new `RunWithBypassAsync` wrapping) + `McpWriteExpansionTests`
      (migrated, 3 call sites). Every test that pinned `app.is_super_admin` for a BYPASS purpose
      migrated to `app.bypass_rls`; `SalesChainRlsTests`/`ReviewHardeningRlsTests`'s harmless
      `is_super_admin='false'` clears were left as-is (no-ops post-fix, not worth the diff).
- [x] **Full suite** green: 684 passed / 8 skipped / 0 failed (692 total — baseline 681/8 plus 3
      net new tests: `SuperAdminTenantScopeRlsTests` ×2, `CompanySwitchTests` integration test ×1).
      Skip count matches baseline exactly. `TEAS_TEST_PG` set in the same shell command as
      `dotnet test` every run. No flaky "TenantIsolation Npgsql connection reset" observed on
      either full run (2026-07-08).
- [x] `pg_policies` DB check (D2) returns zero `is_super_admin` policies (verified via throwaway
      Npgsql console against `teas_test`, see D2 evidence).

## D7 — Migration / rollout
- [x] Prod DB backup step unchanged/still mandatory before deploy (SqlScripts run at API
      startup) — NOT executed as part of this implementation task (deploy is a separate step);
      flagged here for whoever runs the deploy.
- [x] `600` is idempotent (DROP POLICY IF EXISTS + ENABLE/FORCE re-runnable) + DDL-only (no data/
      trigger changes) — confirmed via the fixture applying it against an already-seeded
      `teas_test` with zero errors across 3 full-suite runs. Sorts LAST: filename `600` > `591`
      (highest prior prefix), confirmed by `sys.applied_sql_scripts` showing it applied after
      the existing 591.
- [x] Rollback note preserved as documentation (D7's remaining bullets are deploy-time
      procedure, not implementation-time checklist items — no code change needed here).
- [ ] **Deploy verification through the PUBLIC topology** (not just localhost): super admin on
      prod switches Repttown(2) ↔ พงศ์สันต์(3) → quotations/customers/dashboard DIFFER; open a
      company-2 document while on company-3 → 404 + correct letterhead; company-switch,
      OAuth MCP grant, and an RBAC cross-company edit all still succeed; an API-key call and the
      e-Tax retry tick still work. — blocked on server migration; tracked in
      MIGRATION-CUTOVER-CHECKLIST.md (triage 2026-08-19)

---

## Blast-radius cap
- Source files (≈9): `600_superadmin_scoped_rls.sql` (new); `AccountingDbContext.cs`;
  `TenantMiddleware.cs`; `ApiKeyResolver.cs`; `ETaxRetryWorker.cs`; `VatRegisterSnapshotJob.cs`;
  `CompanySwitchService.cs`; `OAuthEndpoints.cs`; `RbacAdminService.cs`.
- Test files (≈8): 1 new RLS test + migrate GUC name / premises in `ApiKeyResolverRlsTests`,
  `ETaxRetryWorkerRlsTests`, `VatRegisterSnapshotJobRlsTests`, `AuditLogRlsTests`,
  `CompanySwitchTests`, `McpWriteExpansionTests`, and any RBAC cross-company test.
- **Public-API changes: NONE** (no endpoint contract, DTO, or route changes). If a fix seems to
  require one, STOP and re-spec.
- Optional shared helper (`RunWithRlsBypassAsync`) for the LOCAL bypass tx is allowed within
  Infrastructure; do not introduce new public surface. **DO NOT** touch immutability triggers,
  the `sys.users.is_super_admin` column, JWT claims, or the MCP token pipeline.
- Hitting materially more than the above = stop-and-re-spec.

## Attempt log
- 2026-07-08: investigation complete (prod DevTools + code). Design dispatched to Opus.
- 2026-07-08 (opus-designer): full design authored. Architecture = retire `app.is_super_admin`
  data-scope bypass at both layers; introduce LOCAL-only `app.bypass_rls` for 5 Family B
  service/admin paths; new `600_superadmin_scoped_rls.sql` recreates ~37 `company_isolation`
  policies (G1 no-bypass / G2 scanner-bypass / G3 system-global+bypass). D1–D7 specified with
  exact files, table list, SQL fragment, and test plan. Ready to implement.
- 2026-07-08 (sonnet-implementer, this task): full implementation, tests-first per D6.
  1. Wrote `SuperAdminTenantScopeRlsTests.cs` FIRST, confirmed it fails today for the right
     reason (company A's row leaks through the `OR is_super_admin` arm while pinned to B).
  2. Implemented D2 (600 script, verbatim from spec), D3 (EF filter), D4 (TenantMiddleware),
     and every Family B site: `ApiKeyResolver.cs` (2 GUC renames), `ETaxRetryWorker.cs` (2 GUC
     renames), `CompanySwitchService.cs` (2 new LOCAL-bypass tx blocks: branch-read + audit-write,
     fixed the stale "activity_log has no RLS" comment), `OAuthEndpoints.cs` (same 2 blocks),
     `VatRegisterSnapshotJob.cs` (dropped the `is_super_admin='false'` half), `RbacAdminService.cs`
     (audited every `ResolveTargetCompany` call site — ALL 10 public methods touch
     `sys.roles`/`sys.role_permissions`/`audit.activity_log`, all now wrapped whole-method in a
     new PRIVATE `RunWithBypassAsync` helper local to that class — not a new file, not new public
     surface; `ListUsersAsync`'s role-join was the one easy-to-miss case since it doesn't call
     `ResolveTargetCompany` directly for the join itself).
  3. VERIFY item resolved (no code change): `ETaxSubmissionPipeline`/`ETaxXmlBuilder`, when
     driven by the retry WORKER (not the interactive `EnqueueAsync` path), pin NEITHER
     `app.company_id` NOR any bypass — they rely on whatever's ambient on the connection, which by
     the time `pipeline.RunAsync` executes is NOT the leftover `is_super_admin` (already reverted,
     LOCAL, by the scan's own sub-transactions) but simply UNPINNED. Under a real NOBYPASSRLS
     connection this already silently no-ops (0 rows) — a PRE-EXISTING gap in the worker-only
     path, behaviorally IDENTICAL before/after 600 (same class as the out-of-scope
     `ApiKeyResolver.cs:95`). NOT Family B'd — would add a 10th source file for a bug this fix
     doesn't create or worsen. Flagged for a follow-up spec, not fixed here.
  4. Migrated GUC-pinning tests: `ApiKeyResolverRlsTests`, `ETaxRetryWorkerRlsTests`,
     `AuditLogRlsTests`, `McpWriteExpansionTests` (rename `is_super_admin`→`bypass_rls`);
     `VatRegisterSnapshotJobRlsTests` (reworked both tests — the "job forces is_super_admin=false"
     premise is gone; now proves G1 has no bypass arm at all, and that the job never touches the
     retired GUC). Added a new HTTP integration test to `CompanySwitchTests.cs` (switch to B →
     `GET /quotations` scoped correctly, cross-company detail → 404, unswitched → empty) per D6's
     integration-test bullet. `SalesChainRlsTests`/`ReviewHardeningRlsTests` left untouched
     (harmless no-op pins, D6 only required them to stay green, not be edited).
  5. **Unplanned but necessary fallout fix**: full-suite run surfaced 2 pre-existing failures in
     `OnboardingFoundingAddressTests.cs` — a DIRECT, correct consequence of D3 (removing the EF
     filter's `IsSuperAdmin` bypass now means those 2 tests' `StubTenant{CompanyId=1,
     IsSuperAdmin=true}` no longer bypasses the filter when reading back a DIFFERENT freshly-
     created company's `ChartOfAccounts`/`Branches`). Fixed with `.IgnoreQueryFilters()` on those
     2 reads (test bug, not a production bug — confirms D3 is working exactly as designed).
  6. Gates: `dotnet build` clean; full suite 684 passed / 8 skipped / 0 failed (baseline 681/8,
     +3 net new tests) across 2 full runs; `pg_policies` on `teas_test` — 0 rows mention
     `is_super_admin`, exactly 6 rows mention `bypass_rls` (G2+G3), 37 total `company_isolation`
     policies (31+3+3); grep confirms zero remaining `app.is_super_admin` set_config/current_setting
     calls in C# source (only historical text in old numbered SqlScripts, correctly untouched).
  7. Blast radius: exactly 9 source files (matches cap, no shared cross-file helper added —
     `RunWithBypassAsync` in `RbacAdminService.cs` is private/local to that class); 8 test files
     touched (1 new RLS test + 5 named migrations + 1 new integration test in `CompanySwitchTests.cs`
     + 1 unplanned-but-required fix in `OnboardingFoundingAddressTests.cs`, absorbing the ~8 budget
     instead of touching an "RBAC cross-company test" file, which needed no edits). No public-API
     changes. No immutability triggers / `sys.users.is_super_admin` column / JWT claims / MCP
     token pipeline touched.

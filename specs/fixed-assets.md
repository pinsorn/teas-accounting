# Fixed Assets + Depreciation (Cycle D, feature #4)

<!-- Living document. Worker updates the checklist as it works; a retry uses the
     SAME file and grows the Attempt log. Do NOT rewrite the spec for a retry. -->

Asset register + straight-line monthly depreciation + disposal/sale with gain/loss.
Depreciation integrates with period close (closing month M requires M's depreciation
run). Reports: asset register + accumulated depreciation per period. Phase 2 (NOT
now): tax-vs-book depreciation divergence for CIT.

Source of scope: `PLAN-feature-cycle-2026-07.md` §4. Exemplar specs (patterns
inherited): `specs/expense-claims.md` (Cycle C, freshest), `specs/year-end-closing.md`
(Cycle A, GL-posting + seed footguns + period machinery).

---

## 0. BLOCKING open question — read FIRST (disposal VAT → the ภ.พ.30 gap)

**Selling a used business asset is a VATable supply in Thailand — output VAT 7% is
due and by law a tax invoice must be issued, and that VAT must appear in the output
tax report / ภ.พ.30 (VAT return).** But TEAS builds the ภ.พ.30 / output-tax report
from **Tax Invoice documents** (the `PostTaxInvoiceAsync` sales path), **not** from
raw GL. A disposal posting only to the GL Output-VAT account (2151) records the
liability correctly **but the VAT will NOT flow into the VAT return** unless a real
Tax Invoice is also issued.

Making disposal emit a real Tax Invoice needs an asset→customer→tax-invoice bridge =
edits to `PostTaxInvoiceAsync` / the sales-doc core = **blast-radius STOP** (see cap).
Evidence (from the code): `PostTaxInvoiceAsync` (`GlPostingService.cs:41`) and the tax
reports key off `tax.tax_invoices`; there is no "issue a tax invoice from a non-sales
document" pattern, and `TaxInvoice` requires a `Customer`. The asset module has no
customer link.

### Options for Ham/Fable
- **(A) RECOMMENDED — GL-only disposal + manual TI.** Disposal posts a balanced
  manual JE (via the EXISTING `PostManualEntryAsync`) that INCLUDES the Output-VAT
  line to account 2151, so the GL/balance sheet are correct. The disposal screen shows
  a prominent notice: *"Issue a Tax Invoice to the buyer separately (sales module) so
  this VAT appears in ภ.พ.30."* Zero sales-core edits. `disposal_vat_amount` is a field
  on the disposal (user-entered or proceeds×rate, default rate 7%; 0 for non-VAT
  companies / write-offs). **This spec fully designs Option A.**
- **(B) Auto-issue a Tax Invoice on disposal.** Correct end-to-end (VAT hits ภ.พ.30
  automatically) but requires the asset→customer bridge + calling the tax-invoice
  service = sales-core edits = STOP. NOT designed here.
- **(C) No VAT on disposal.** Wrong for Thai law (asset sale is VATable). Rejected.

**Ruling needed:** confirm A (GL-only + manual-TI notice) for phase 1, or escalate to
B (re-scopes disposal). Everything else in this spec (schema, depreciation engine,
period-close hook, register/reports, state machine, non-VAT disposal math) is
independent of this ruling and buildable as written.

---

## Context / footguns (fold in — do NOT rediscover)

Env: Windows 11, PowerShell 5.1 (no `&&`; write files UTF-8 `-Encoding utf8`).
`TEAS_TEST_PG` = `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`.
Schema via **EF migrations**; RLS/triggers/seeds via numbered
`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/*.sql` applied at startup
by `DbInitializer.ApplyScriptsAsync` (idempotent, tracked in `sys.applied_sql_scripts`,
runs EF migrations BEFORE scripts). **Highest on disk = `618` (Cycle C expense-claims,
UNCOMMITTED). Next free = `619`.** This spec uses **619–622**; re-verify next-free at
impl time (`ls SqlScripts`).

### Inherited footguns (from `specs/expense-claims.md` §Context — all apply here)

- **F1 — Startup seed RLS 42501 / silent zero fan-out (MANDATORY, bit prod 2026-07-09).**
  `ApplyScriptsAsync` runs BEFORE `TenantMiddleware`, so `app.company_id` is UNSET and
  (on prod) the role is NOBYPASSRLS. This has **two distinct failure modes** — this
  spec hits BOTH:
  - **G3 system-global fan-out** (perms into `sys.permissions`/`sys.role_permissions`):
    without bypass, `SELECT … FROM sys.roles WHERE company_id IS NOT NULL` sees **zero
    rows** and **silently inserts nothing on prod** (no error). Fix: `SET LOCAL
    app.bypass_rls = 'on';` as the FIRST statement (see `610`/`615`/`617`). → script 620.
  - **G1 tenant-table write** (new GL accounts into `master.chart_of_accounts`): a bare
    `INSERT … SELECT FROM master.companies` **HARD-CRASHES with SqlState 42501** at
    startup (Postgres reuses the `USING` policy as implicit `WITH CHECK`; company_id is
    unset). G1 tables get **NO bypass arm** by design. Fix: a `DO` block that loops
    `FOR c IN SELECT company_id FROM master.companies`, `PERFORM set_config('app.company_id',
    c.company_id::text, true)` per company, then the per-company idempotent INSERT
    (mirror `611` verbatim). → script 621.
  - **BOTH are invisible on teas_test (superuser bypasses RLS unconditionally).** The
    deploy probe is a **ROW COUNT, not an exit code** (see Verification gates).
- **F2 — RBAC seed-ordering.** Permission-code INSERT must precede the grants that
  reference it, in the SAME file (mirror `610`/`615`/`617`). Never split across files.
- **F3 — literal `{`/`}` in ANY SqlScript (comments included) → `FormatException` at
  boot** (`ExecuteSqlRawAsync` treats the file as a `string.Format` template). Prose
  only, no braces.
- **F4 — number-gap view int overflow.** `tax.v_number_gaps` casts trailing digit-runs
  across ALL companies; keep `doc_no` trailing digits ≤ 18. Any test seeding a synthetic
  `DocNo` directly MUST end it in a non-digit char. (The `FA` asset code and `JV` JE
  numbers come from `INumberSequenceService`, so real code is safe; only synthetic test
  DocNos are at risk.)
- **F5 — server re-pins DocDate.** Sales/purchase services re-pin `DocDate` to
  `IClock.TodayInBangkok()` at post. Depreciation/disposal JEs here are posted at an
  EXPLICIT `DocDate` (period-end / disposal date) via `PostManualEntryAsync`, which does
  NOT re-pin — so a real historical post-date is intended and fine. Fresh `teas_test`
  closes the previous month per `CURRENT_DATE` (memory "relative-date seed") → in tests
  use today/future months, never hardcoded past months, or `EnsureOpenAsync` throws
  `period.closed`.
- **F6 — RLS masked by superuser tests.** teas_test connects as superuser → RLS bypassed
  → a "company A row invisible to B" test proves the EF query filter, NOT the DB RLS
  policy. Do not claim RLS is verified from a green teas_test run. For a real RLS test
  use `SET ROLE pg_database_owner` + explicit `GRANT` (per year-end-closing E9); else
  keep it a query-filter test and LABEL it so.
- **F7 — always rewrite child rows on draft edit.** Repo convention for `UpdateDraftAsync`
  is DELETE+recreate child rows every edit (fixed assets have no editable child lines on
  the asset itself, but `depreciation_run_lines` are write-once — see idempotency).
- **F8 — inert concurrency token on PV/PO.** `long Version .IsConcurrencyToken()` is
  DECLARED but never incremented on PV/PO → those transitions are TOCTOU-racy. Fixed
  assets MUST do better: `Version++` in every state-transition method so the optimistic
  lock actually fires. Do NOT "fix" PV here.
- **F9 — teas_test fixture apply-once + false-green skips.** Each SQL seed runs ONCE
  (tracked). To re-exercise a changed `619`–`622`, `DELETE FROM sys.applied_sql_scripts
  WHERE script_name = '<name>.sql'` then re-run. Skipped tests fake green — always check
  skip count vs baseline.
- **F10 — new public route topology.** Authenticated FE pages reach the backend via the
  existing `/api/proxy/[...path]` BFF — a NEW authenticated REST route needs NO new
  passthrough and NO `PUBLIC_PATHS` entry. Verify on the public domain with
  `curl .../api/proxy/fixed-assets` (expect 401), not the bare path.
- **F11 — Thai ম glyph.** Bengali `ম` (U+09AE) creeps into Thai `ม` (U+0E21) in
  seeds/DTOs/i18n. `grep -rn "ম" backend/ frontend/` → empty before commit.

### FA-specific footguns (NEW — the money-correctness core)

- **FA-A — acquisition must NOT post a GL entry (double-book guard).** The asset's cost
  is already in the GL: the linked **Vendor Invoice already debited the asset-cost
  account / credited AP** when it posted (`PostVendorInvoiceAsync`), OR the cost was
  entered as an opening-balance JE. The FA register **only records + starts the
  depreciation clock**. Posting an acquisition JE here would double the asset on the
  balance sheet. **The FA module posts JEs for depreciation and disposal ONLY.** A test
  asserts activating an asset creates **zero** JournalEntry rows.
- **FA-B — accumulated depreciation never exceeds the depreciable base.** Enforced
  structurally by `charge = min(monthly_amount, remaining)` (see §3). Never compute a
  fixed monthly figure and let rounding drift overshoot — the last month is a *plug*
  that falls out of the `min`, not a special case.
- **FA-C — depreciation JEs are NOT closing entries.** `IsClosingEntry` stays `false`
  (the `PostManualEntryAsync` default) so depreciation expense lands in P&L and gets
  **swept into 3300 Retained Earnings by year-end close** (`specs/year-end-closing.md`
  §C sweeps `DocDate ∈ [fiscalStart,fiscalEnd]` on non-closing lines). Setting it true
  would hide depreciation from P&L/CIT/tax reports. Do not touch year-end code.
- **FA-D — depreciation posts into an OPEN month only.** The FA service MUST call
  `IPeriodCloseService.EnsureOpenAsync(runDate, ct)` before `PostManualEntryAsync`
  (`PostManualEntryAsync` does NOT self-check the period, unlike `PostClosingEntryAsync`).
  This plus the period-close hook (§4) makes the ordering airtight both ways: you cannot
  close month M without M's depreciation, and you cannot post M's depreciation after M is
  closed.
- **FA-E — one depreciation run per company-month (idempotency + race).** `UNIQUE
  (company_id, period_year, period_month)` on `depreciation_runs` is the double-post
  backstop. A second "generate M" call returns the existing Posted run, posts no second
  JE. The unique index also races two concurrent generators safely (loser hits the
  constraint → mapped to `depreciation.already_posted` / 409).
- **FA-F — disposal reads the asset's CURRENT accumulated depreciation.** Disposal does
  NOT auto-run catch-up depreciation for the disposal month (kept decoupled). NBV = cost
  − `accumulated_depreciation` as posted. The disposal screen shows "depreciated through
  YYYY-MM". See Open Questions Q3 (auto-catch-up is a candidate phase-1 add if Ham wants
  it).

---

## Reuse map (every path below was read for this spec)

| Need | Reuse | Path |
|---|---|---|
| GL JE writer (dep + disposal) | **EXISTING** `PostManualEntryAsync(companyId, branchId, docDate, description, reference, IReadOnlyList<(long AccountId, decimal Debit, decimal Credit)> lines, ct)` — tuple lines, balance-checked, `IsClosingEntry=false`; caller calls `EnsureOpenAsync` first | `Accounting.Infrastructure/Ledger/GlPostingService.cs:477`; iface `Application/Ledger/IGlPostingService.cs` |
| Period-open guard | `IPeriodCloseService.EnsureOpenAsync(DateOnly docDate, ct)` → throws `period.closed` | `Accounting.Infrastructure/Ledger/PeriodCloseService.cs:41` |
| Period-close hook site | `PeriodCloseService.CloseAsync(year, month, notes, ct)` — insert AFTER draft-guard (L65), BEFORE `BeginTransactionAsync` (L67) | `PeriodCloseService.cs:48` |
| JE ref/tag convention | JE has NO source-doc FK — only `string? Reference` + `string Description`; PV tags `description:$"PV {DocNo}", reference:DocNo`. FA: `reference` = asset/run tag; hook keys off the `DepreciationRun` table, NOT JE-reference matching | `Domain/Entities/Ledger/JournalEntry.cs`; `GlPostingService.cs:224` |
| Acquisition link | `VendorInvoice` PK `long VendorInvoiceId`, table `purchase.vendor_invoices` | `Domain/Entities/Purchase/VendorInvoice.cs` |
| Account resolver | `GlAccountsOptions` (code-string defaults) + `ResolveAccountIdAsync(companyId, code, ct)` → `gl.account_missing`. **Reuse `OutputVatAccount`=2151** for disposal VAT. **ADD 5 new props** (see §2) — no FA accounts exist today | `Accounting.Infrastructure/Ledger/GlAccountsOptions.cs`; `GlPostingService.cs:528` |
| New-company CoA seed | add rows to `DefaultChartOfAccounts` | `Accounting.Infrastructure/…/MasterDataServices.cs` (year-end B1 precedent) |
| Existing-company account seed | G1 per-company `DO`-loop | `SqlScripts/611_seed_retained_earnings_account.sql` (verbatim pattern) |
| Perms seed | insert-first/grant-second + `SET LOCAL app.bypass_rls='on'` | `SqlScripts/610_seed_year_close_perms.sql` / `615` / `617` |
| RLS policy (G1) | `company_isolation` per table, no bypass arm | `SqlScripts/616_expense_claims_rls.sql` / `614` |
| Doc numbering | `INumberSequenceService.NextAsync(companyId, branchId, prefixCode, subPrefix, docDate, ct)` → `DocumentNumber`; `FOR UPDATE`, self-seeds | `Application/Abstractions/INumberSequenceService.cs` |
| Doc prefix seed | plain-metadata INSERT into `sys.document_prefixes` (no RLS) | `SqlScripts/618_seed_expense_claim_prefix.sql` / `100` |
| Tenant filter | `ITenantOwned { int CompanyId }` → auto EF global query filter | `AccountingDbContext.ApplyTenantFilters` |
| Concurrency | `IConcurrencyVersioned` (long Version) — increment each transition (F8) | `Domain/Common/IConcurrencyVersioned.cs` |
| Permission constants | add nested class to `Permissions.cs` + `.All` + `PermissionCatalog.cs` | `Api/Authorization/Permissions.cs` |
| Endpoints/DI/Program | new `FixedAssetEndpoints.cs`, register in `Program.cs`, DI in `DependencyInjection.cs` (scoped) | mirror `ExpenseClaimEndpoints.cs` (Cycle C) |
| FE list / detail / form | clone `bank-accounts/page.tsx`, `payment-vouchers/[id]/page.tsx`, `payment-vouchers/new/page.tsx` | `frontend/app/(dashboard)/…` |
| FE queries/nav/i18n | `frontend/lib/queries.ts`, `SidebarNav.tsx`, `messages/en.json`+`th.json` | (mirror `expenseClaims` namespace) |

---

## Requirements (checklist)

### 1. Schema (EF migration + DDL sketch)

- [x] **1.1** New entities in `backend/src/Accounting.Domain/Entities/FixedAsset/`:
  `FixedAsset` (`ITenantOwned, IAuditable, IConcurrencyVersioned`), `DepreciationRun`
  (`ITenantOwned, IConcurrencyVersioned`), `DepreciationRunLine` (`ITenantOwned`).
- [x] **1.2** Enums in `Domain/Enums/`: `FixedAssetStatus { Draft, Active, Disposed,
  WrittenOff, Cancelled }`, `DepreciationRunStatus { Posted }` (single state — runs post
  atomically; the enum exists for the hook query + future Draft support).
- [x] **1.3** Config `Persistence/Configurations/FixedAsset/FixedAssetConfiguration.cs`
  (all three configs in one file; auto-discovered by `ApplyConfigurationsFromAssembly`).
  Tables in a new `fixedasset` schema. Money columns `numeric(19,4)`.
- [x] **1.4** DbSets in `AccountingDbContext.cs` (3 lines): `FixedAssets`,
  `DepreciationRuns`, `DepreciationRunLines`.
- [x] **1.5** EF migration `dotnet ef migrations add FixedAssets` — evidence: creates
  exactly 3 tables + `EnsureSchema("fixedasset")`, FKs to `purchase.vendor_invoices`
  (nullable), `gl.journal_entries` (nullable), `master.chart_of_accounts` (the 3 frozen
  account ids), `master.business_units` (nullable); unique `(company_id,branch_id,doc_no)
  WHERE doc_no IS NOT NULL`; unique `(company_id,period_year,period_month)` on runs;
  unique `(fixed_asset_id, depreciation_run_id)` on run lines; **no drops of any existing
  table**. `dotnet build Accounting.sln` → 0 Error(s). Flag teas_test reset need at
  hand-off (memory "migration-squash + teas_test reset") — Fable owns `ef` + any reset.

DDL sketch (authoritative shape; column list is exact):

```
fixedasset.fixed_assets
  fixed_asset_id          bigint  PK generated always as identity
  company_id              int     NOT NULL              -- tenant (ITenantOwned, RLS)
  branch_id               int     NOT NULL
  business_unit_id        int     NULL
  doc_no                  varchar(40) NULL              -- asset code, 'FA' prefix, assigned at ACTIVATE
  prefix_code             varchar(20) NOT NULL DEFAULT 'FA'
  name                    varchar(200) NOT NULL
  category                varchar(60)  NULL             -- grouping string (EQUIPMENT/VEHICLE/BUILDING/FURNITURE/…)
  acquire_date            date    NOT NULL
  vendor_invoice_id       bigint  NULL  FK purchase.vendor_invoices   -- acquisition audit link (optional)
  cost                    numeric(19,4) NOT NULL        -- capitalized cost (already in GL via VI / opening bal)
  salvage_value           numeric(19,4) NOT NULL DEFAULT 0
  useful_life_months      int     NOT NULL              -- > 0 (CHECK)
  depreciable_base        numeric(19,4) NOT NULL DEFAULT 0   -- = cost - salvage; FROZEN at activate
  monthly_amount          numeric(19,4) NOT NULL DEFAULT 0   -- = round(depreciable_base/useful_life_months,2); FROZEN at activate
  depreciation_start_date date    NOT NULL              -- defaults to acquire_date
  asset_cost_account_id   bigint  NOT NULL  FK master.chart_of_accounts   -- frozen: GlAccountsOptions default, editable in Draft
  accum_dep_account_id    bigint  NOT NULL  FK master.chart_of_accounts
  dep_expense_account_id  bigint  NOT NULL  FK master.chart_of_accounts
  accumulated_depreciation numeric(19,4) NOT NULL DEFAULT 0  -- running total; += each run charge; = depreciable_base when fully depreciated
  status                  varchar(20) NOT NULL DEFAULT 'DRAFT'   -- FixedAssetStatus
  disposal_date           date    NULL
  disposal_proceeds       numeric(19,4) NULL            -- ex-VAT sale price (0 for write-off)
  disposal_vat_amount     numeric(19,4) NULL            -- output VAT to acct 2151 (0 for non-VAT / write-off)
  disposal_gain_loss      numeric(19,4) NULL            -- + gain / - loss (proceeds - NBV)
  disposal_buyer_name     varchar(200) NULL             -- free text (Option A; no Customer FK)
  disposal_journal_entry_id bigint NULL  FK gl.journal_entries
  writeoff_reason         varchar(500) NULL
  notes                   varchar(1000) NULL
  version                 bigint  NOT NULL DEFAULT 0    -- concurrency (Version++ each transition)
  created_at/by, updated_at/by, activated_at/by, disposed_at/by     -- audit
  UNIQUE (company_id, branch_id, doc_no) WHERE doc_no IS NOT NULL
  CHECK (useful_life_months > 0), CHECK (salvage_value >= 0 AND salvage_value <= cost)

fixedasset.depreciation_runs
  depreciation_run_id     bigint  PK identity
  company_id              int     NOT NULL              -- tenant (RLS)
  branch_id               int     NOT NULL              -- JE branch (company primary branch; multi-branch split = phase 2)
  period_year             int     NOT NULL
  period_month            int     NOT NULL              -- 1..12
  run_date                date    NOT NULL              -- = last day of (period_year, period_month); JE DocDate
  status                  varchar(20) NOT NULL DEFAULT 'POSTED'   -- DepreciationRunStatus
  total_amount            numeric(19,4) NOT NULL        -- Σ line.amount
  asset_count             int     NOT NULL
  journal_entry_id        bigint  NULL  FK gl.journal_entries      -- the one aggregate JE
  version                 bigint  NOT NULL DEFAULT 0
  created_at/by
  UNIQUE (company_id, period_year, period_month)         -- one run per company-month (idempotency + race backstop)

fixedasset.depreciation_run_lines
  depreciation_run_line_id bigint PK identity
  depreciation_run_id     bigint  NOT NULL  FK fixedasset.depreciation_runs ON DELETE CASCADE
  company_id              int     NOT NULL              -- tenant (RLS)
  fixed_asset_id          bigint  NOT NULL  FK fixedasset.fixed_assets
  amount                  numeric(19,4) NOT NULL        -- this month's charge for this asset (= min(monthly, remaining))
  accumulated_after       numeric(19,4) NOT NULL        -- asset.accumulated_depreciation snapshot after this charge (audit / as-of report)
  UNIQUE (fixed_asset_id, depreciation_run_id)
```

Design notes:
- **`category` is a grouping STRING, not a master entity.** Thai SMEs book different
  asset classes to different GL accounts — that is handled per-asset by the three frozen
  `*_account_id` columns (defaulted from `GlAccountsOptions`, editable while Draft),
  mirroring the repo's freeze-account convention (`ExpenseClaimLine.expense_account_id`).
  A category→account master is deliberately deferred (Open Q4) to keep blast radius small.
- **`depreciable_base` + `monthly_amount` are FROZEN at activate** and never recomputed
  (changing cost/life mid-life = revaluation/impairment = phase 2). While Draft they are
  recomputed on every edit.

### 2. GL accounts — resolver props + seeds (the F1 seed footgun lives here)

- [x] **2.1** Add 5 default account-code props to `GlAccountsOptions.cs`:
  `FixedAssetCostAccount = "1610"`, `AccumulatedDepreciationAccount = "1690"`,
  `DepreciationExpenseAccount = "5450"`, `GainOnAssetDisposalAccount = "4200"`,
  `LossOnAssetDisposalAccount = "5460"`. (Disposal output VAT reuses the existing
  `OutputVatAccount = "2151"`.) Purely additive; no existing prop or `GlPostingService`
  method changed.
- [x] **2.2** Add the 5 rows to `DefaultChartOfAccounts` (`MasterDataServices.cs`) for
  NEW companies (mirror year-end B1 tuple shape/casing exactly). Codes + Thai/EN + type +
  normal balance:
  | code | TH | EN | AccountType | NormalBalance | IsHeader |
  |---|---|---|---|---|---|
  | 1610 | อุปกรณ์และเครื่องใช้สำนักงาน | Office Equipment (Fixed Asset) | Asset | DR | false |
  | 1690 | ค่าเสื่อมราคาสะสม | Accumulated Depreciation | Asset | **CR** | false |
  | 5450 | ค่าเสื่อมราคา | Depreciation Expense | Expense | DR | false |
  | 4200 | กำไรจากการจำหน่ายสินทรัพย์ | Gain on Disposal of Assets | Revenue | CR | false |
  | 5460 | ขาดทุนจากการจำหน่ายสินทรัพย์ | Loss on Disposal of Assets | Expense | DR | false |
  (1690 is a contra-asset: `AccountType.Asset` with `NormalBalance "CR"` — the balance
  sheet computes Asset = Dr−Cr, so a CR-balance accum-dep correctly reduces total assets.)
  Confirm no existing-code collision (`CreateAsync` dedupes by code).
- [x] **2.3** `SqlScripts/621_seed_fixed_asset_accounts.sql` — G1 per-company seed of the
  5 codes to EXISTING companies. **MIRROR `611` VERBATIM** (the `DO`-loop-per-company,
  `set_config('app.company_id', …)` pattern that fixed the prod 42501 crash). NOT a bare
  `INSERT … SELECT FROM master.companies` (that HARD-CRASHES at startup — F1 G1 mode).
  No literal braces (F3). Skeleton (extend the `VALUES` list to all 5 accounts):

  ```sql
  -- Fixed assets (specs/fixed-assets.md §2) — seed the 5 FA GL accounts into every
  -- EXISTING company's chart of accounts. New companies get them via DefaultChartOfAccounts.
  -- Idempotent; zero-balance until first depreciation/disposal (dropped by the balance
  -- sheet's zero-row filter) — safe for co2/co3 demo data. chart_of_accounts is a G1
  -- (never-bypassable) tenant table: pin app.company_id per company, do NOT add a bypass
  -- arm and do NOT use a bare multi-company INSERT (prod 42501, 2026-07-09).
  DO $do$
  DECLARE c RECORD;
  BEGIN
      FOR c IN SELECT company_id FROM master.companies LOOP
          PERFORM set_config('app.company_id', c.company_id::text, true);
          INSERT INTO master.chart_of_accounts
              (company_id, account_code, account_name_th, account_name_en, account_type,
               normal_balance, is_header, is_active, created_at)
          SELECT c.company_id, v.code, v.th, v.en, v.acct_type, v.normal_bal, FALSE, TRUE, now()
          FROM (VALUES
              ('1610','อุปกรณ์และเครื่องใช้สำนักงาน','Office Equipment (Fixed Asset)','ASSET','DR'),
              ('1690','ค่าเสื่อมราคาสะสม','Accumulated Depreciation','ASSET','CR'),
              ('5450','ค่าเสื่อมราคา','Depreciation Expense','EXPENSE','DR'),
              ('4200','กำไรจากการจำหน่ายสินทรัพย์','Gain on Disposal of Assets','REVENUE','CR'),
              ('5460','ขาดทุนจากการจำหน่ายสินทรัพย์','Loss on Disposal of Assets','EXPENSE','DR')
          ) AS v(code, th, en, acct_type, normal_bal)
          WHERE NOT EXISTS (
              SELECT 1 FROM master.chart_of_accounts a
              WHERE a.company_id = c.company_id AND a.account_code = v.code)
          ON CONFLICT (company_id, account_code) DO NOTHING;
      END LOOP;
      PERFORM set_config('app.company_id', '', true);
  END
  $do$;
  ```
  (Confirm the exact `account_type`/`normal_balance` string casing the CoA uses — `611`
  wrote `'EQUITY'`/`'CR'` uppercase; match it.)

### 3. Depreciation engine — GETS FABLE LINE-BY-LINE REVIEW

- [x] **3.1** `FixedAssetService.GenerateDepreciationAsync(int year, int month, ct)`:
  1. `EnsureOpenAsync(runDate, ct)` where `runDate = last day of (year, month)` → guards
     against posting into a closed month (F5/FA-D).
  2. If a `DepreciationRun` for `(company, year, month)` already exists → return it
     (idempotent; no new JE). (Belt-and-braces with the unique index.)
  3. `BeginTransactionAsync`. Load eligible assets: `Status == Active` AND
     `DepreciationStartDate <= runDate` AND `accumulated_depreciation < depreciable_base`.
  4. Per asset: `remaining = depreciable_base − accumulated_depreciation`;
     `finalScheduledMonth = depreciation_start_date + (useful_life_months − 1) months`
     (month arithmetic on year/month only);
     **`charge = (year,month) >= finalScheduledMonth ? remaining
     : Math.Min(monthly_amount, remaining)`**. Skip if `charge <= 0`.
     Rationale (Fable review 2026-07-10): `min()` alone plugs the OVERSHOOT
     direction (rounded-up monthly, e.g. 50,000/36) but not UNDERSHOOT
     (rounded-down monthly, e.g. 50,000/24 → 2,083.33 × 24 = 49,999.92 leaves
     0.08 dribbling into month 25 — asset outlives its useful life by a month).
     Plugging the final SCHEDULED month with `remaining` closes both directions;
     the `min()` still guards every earlier month, accumulated can never exceed
     base (FA-B holds).
  5. Build the aggregate JE lines: group charges by `dep_expense_account_id` → one **Dr**
     per distinct expense account = Σ charge; group by `accum_dep_account_id` → one **Cr**
     per distinct accum account = Σ charge. `Σ Dr == Σ Cr == total` (balanced by
     construction).
  6. Post ONE JE: `PostManualEntryAsync(company, branchId, runDate, description:
     $"ค่าเสื่อมราคา {year}-{month:D2} / Depreciation {year}-{month:D2}", reference:
     $"DEP-{year}{month:D2}", lines, ct)`. `branchId` = the company primary/head branch
     (resolve the tenant's default branch; single-branch JE — multi-branch split is
     phase 2, Open Q5). Returns `journalEntryId`.
  7. Insert the `DepreciationRun` (status Posted, total, asset_count, journal_entry_id) +
     one `DepreciationRunLine` per asset; `asset.AccumulatedDepreciation += charge` and
     `asset.Version++` for each. `CommitAsync`. On the unique-index violation from a
     concurrent run → catch, throw `DomainException("depreciation.already_posted", …)`.
- [x] **3.2 Rounding.** `monthly_amount = Math.Round((cost − salvage_value) /
  useful_life_months, 2, MidpointRounding.AwayFromZero)` (satang). Confirm the repo's
  money-rounding convention at impl (grep how VAT is rounded — e.g. in `PostTaxInvoiceAsync`
  / expense-claims); if a shared money-rounding helper exists, use it. The `min(monthly,
  remaining)` plug makes the life tie out to the satang **regardless** of the rounding
  mode, so accumulated == depreciable_base exactly at end of life.

**Worked example — full life INCLUDING the final month (write a test for this exact case):**
Asset cost = **50,000.00**, salvage = **0.00**, useful life = **3 years = 36 months**,
`depreciation_start_date = 2026-03-01`.
- `depreciable_base = 50,000.00`; `monthly_amount = round(50000/36, 2) = round(1388.888…, 2)
  = 1,388.89`.
- Months 1–35 (2026-03 … 2029-01): charge `min(1388.89, remaining) = 1,388.89` each.
  Accumulated after month 35 = `35 × 1,388.89 = 48,611.15`.
- Month 36 (2029-02): `remaining = 50,000.00 − 48,611.15 = 1,388.85`;
  `charge = min(1,388.89, 1,388.85) = 1,388.85` — the **plug** (< monthly_amount; absorbs
  the −0.04 cumulative rounding drift). Accumulated = **50,000.00 exactly**. NBV = 0.
- Month 37 (2029-03): `remaining = 0` → asset excluded (`accumulated < base` false) → **no
  charge, no run line for this asset**.
- Each month's JE: `Dr 5450 Depreciation Expense = charge / Cr 1690 Accumulated
  Depreciation = charge`. Balanced. `IsClosingEntry = false` (FA-C).

### 4. Period-close hook — the ONE authorized shared-machinery edit (minimal)

- [x] **4.1** In `PeriodCloseService.CloseAsync` (`PeriodCloseService.cs:48`), insert the
  block below **immediately after the draft-doc guard (≈L65) and BEFORE
  `BeginTransactionAsync` (L67)**. `from`/`to` (the month's first/last `DateOnly`) are
  already computed in the method for the draft guard; reuse `to` as the month-end. The
  global tenant filter auto-scopes both `AnyAsync` calls to the caller's company.

  ```csharp
  // Fixed-assets hook (specs/fixed-assets.md §4): a month with assets due depreciation
  // cannot close until that month's depreciation run is posted. Minimal add — two reads
  // + one throw; no change to the close transaction or any other service.
  var depreciationDue = await _db.FixedAssets.AnyAsync(a =>
      a.Status == FixedAssetStatus.Active
      && a.DepreciationStartDate <= to
      && a.AccumulatedDepreciation < a.DepreciableBase, ct);
  if (depreciationDue)
  {
      var runPosted = await _db.DepreciationRuns.AnyAsync(r =>
          r.PeriodYear == year && r.PeriodMonth == month
          && r.Status == DepreciationRunStatus.Posted, ct);
      if (!runPosted)
          throw new DomainException("period.depreciation_required",
              $"Depreciation for {year}-{month:D2} must be generated before closing this period.");
  }
  ```
  Error code `period.depreciation_required` → resolves to **422** via the existing generic
  `DomainExceptionMiddleware.StatusFor` (matches the precedent of `period.draft_present` /
  `period.already_closed`, both 422 today). Do NOT edit the middleware (not in blast radius).
- [x] **4.2** This is the ONLY edit to `PeriodCloseService.cs`. Adding `_db.FixedAssets` /
  `_db.DepreciationRuns` references needs NO new project reference (DbSets on the shared
  `AccountingDbContext`, which `PeriodCloseService` already holds). If the hook seems to
  need anything more (a new service dependency, a change to `CloseAsync`'s signature or
  transaction) → **STOP and re-spec** — the hook must stay a two-read guard.
- **Interplay with year-end close:** none required — year-end (`YearCloseService`) requires
  all 12 `AccountingPeriod` rows Closed, and each is now gated on its depreciation run. So
  "year N cannot close until every month's depreciation is posted" holds transitively; no
  year-end code edit. Depreciation JEs (`IsClosingEntry=false`) are swept correctly (FA-C).

### 5. Disposal / sale + write-off (money) — GETS FABLE LINE-BY-LINE REVIEW

- [x] **5.1** `FixedAssetService.DisposeAsync(long id, DisposeRequest req, ct)` (req:
  `DisposalDate, Proceeds, VatAmount?, BuyerName?`). Guard `Status == Active` else
  `fixed_asset.not_active`. `EnsureOpenAsync(disposalDate, ct)`. Compute in one DB tx:
  - `nbv = cost − accumulated_depreciation` (current accumulated; FA-F).
  - `gainLoss = proceeds − nbv` (positive = gain, negative = loss).
  - `vat = req.VatAmount ?? 0` (Option A — user-entered or proceeds×0.07; 0 for non-VAT).
  - `cashReceived = proceeds + vat`.
  - Build the JE line tuples (drop any zero line):
    - `Dr Cash/Bank (1110) = cashReceived` (or the bank account — phase 1: `CashAccount`
      1110; a bank picker is Open Q6).
    - `Dr accum_dep_account_id (1690) = accumulated_depreciation`  (clears the asset's accum)
    - `Cr asset_cost_account_id (1610) = cost`                     (removes the asset at cost)
    - `Cr OutputVatAccount (2151) = vat`                           (skip if 0)
    - gain: `Cr GainOnAssetDisposalAccount (4200) = gainLoss`  (when gainLoss > 0)
    - loss: `Dr LossOnAssetDisposalAccount (5460) = −gainLoss` (when gainLoss < 0)
  - Post via `PostManualEntryAsync(company, asset.BranchId, disposalDate, description:
    $"จำหน่ายสินทรัพย์ {asset.DocNo} / Dispose asset {asset.DocNo}", reference: asset.DocNo,
    lines, ct)`. Set `asset.Status=Disposed`, `disposal_*` fields,
    `disposal_journal_entry_id`, `Version++`. Commit.
- [x] **5.2** `FixedAssetService.WriteOffAsync(long id, WriteOffRequest req, ct)` (req:
  `Date, Reason`). Same as disposal with `proceeds = 0, vat = 0` → the loss = full NBV.
  `Status=WrittenOff`. JE: `Dr 1690 accum / Dr 5460 loss (=NBV) / Cr 1610 cost`. No cash,
  no VAT line.

**Worked example — disposal GAIN (write a test for this exact case):**
Asset cost = **50,000.00**, accumulated depreciation = **30,000.00** → NBV = **20,000.00**.
Sold for **25,000.00** + 7% VAT **1,750.00**; cash received **26,750.00**. Gain =
25,000 − 20,000 = **5,000.00**.
```
Dr 1110 Cash/Bank                26,750.00
Dr 1690 Accumulated Depreciation 30,000.00
   Cr 1610 Fixed Asset (cost)              50,000.00
   Cr 2151 Output VAT                       1,750.00
   Cr 4200 Gain on Disposal                 5,000.00
```
ΣDr = 56,750.00 · ΣCr = 56,750.00 → balanced. Asset → Disposed; accumulated cleared.

**Worked example — disposal LOSS:** same asset (NBV 20,000), sold **15,000.00** + VAT
**1,050.00**, cash **16,050.00**. Loss = 20,000 − 15,000 = **5,000.00**.
```
Dr 1110 Cash/Bank                16,050.00
Dr 1690 Accumulated Depreciation 30,000.00
Dr 5460 Loss on Disposal          5,000.00
   Cr 1610 Fixed Asset (cost)              50,000.00
   Cr 2151 Output VAT                       1,050.00
```
ΣDr = 51,050.00 · ΣCr = 51,050.00 → balanced.

**Worked example — WRITE-OFF** (NBV 20,000, no proceeds, no VAT): loss = NBV = 20,000.
```
Dr 1690 Accumulated Depreciation 30,000.00
Dr 5460 Loss on Disposal         20,000.00
   Cr 1610 Fixed Asset (cost)              50,000.00
```
ΣDr = 50,000.00 · ΣCr = 50,000.00 → balanced.

### 6. State machine

`FixedAssetStatus { Draft, Active, Disposed, WrittenOff, Cancelled }`. Transitions in
entity methods that guard current status (throw a named code) AND `Version++` (F8).

| Transition | From → To | Method / guard (throws) | Permission | Notes |
|---|---|---|---|---|
| Create draft | (none) → Draft | `POST /fixed-assets` | `fixedasset.manage` | doc_no NULL; base/monthly computed |
| Edit draft | Draft → Draft | `PUT /fixed-assets/{id}` | `fixedasset.manage` | recompute base/monthly; accounts editable |
| Activate | Draft → Active | `Activate()` throws `fixed_asset.not_draft` | `fixedasset.manage` | assign `FA` doc_no; **FREEZE** base/monthly/accounts; **posts NO JE** (FA-A) |
| Depreciate (per month) | Active (n/a state change) | `GenerateDepreciationAsync` | `fixedasset.depreciation.run` | posts run JE; asset stays Active |
| Dispose | Active → Disposed | `Dispose()` throws `fixed_asset.not_active` | `fixedasset.dispose` | posts disposal JE; terminal |
| Write off | Active → WrittenOff | `WriteOff()` throws `fixed_asset.not_active` | `fixedasset.dispose` | posts loss JE; terminal |
| Cancel | Draft → Cancelled | `Cancel()` throws `fixed_asset.cannot_cancel` | `fixedasset.manage` | pre-activation only; terminal |

- Terminal: `Disposed`, `WrittenOff`, `Cancelled`. `Active` financial fields
  (cost/salvage/life/accounts) are LOCKED (revaluation = phase 2).
- **Race safety.** Every transition `Version++`; EF optimistic concurrency → losing
  concurrent write throws `DbUpdateConcurrencyException` → endpoint maps to **409**.
  `Dispose`/`GenerateDepreciation` additionally run in a DB transaction and re-guard
  status inside it. Strictly stronger than PV (whose token is inert, F8).

### 7. Reports

- [x] **7.1** `GET /fixed-assets/reports/register?asOf=YYYY-MM-DD` → per asset: doc_no,
  name, category, acquire_date, cost, `accumulatedAsOf` (= Σ `depreciation_run_lines.amount`
  for runs with `(period_year, period_month)` end-date `<= asOf`), `nbv = cost −
  accumulatedAsOf`, status. Include Active + Disposed (Disposed shown with disposal_date).
  Use the run-line history (not the asset's live `accumulated_depreciation`) so the report
  is correct as-of a past date.
- [x] **7.2** `GET /fixed-assets/reports/accumulated-depreciation?year=YYYY` → per asset
  (and/or grouped by `accum_dep_account_id`): the 12 monthly charges + cumulative, sourced
  from `depreciation_run_lines` joined to `depreciation_runs`. This is the auditor/RD view.
- [x] **7.3** Both reports are query-only (no posting); gated `fixedasset.read`.

### 8. Permissions + RLS + prefix seeds (SqlScripts 619, 620, 622)

- [x] **8.1** `Permissions.cs` — new nested class `Permissions.FixedAsset`:
  `Read="fixedasset.read"`, `Manage="fixedasset.manage"`, `Dispose="fixedasset.dispose"`,
  `DepreciationRun="fixedasset.depreciation.run"`. Add each to `Permissions.All` AND
  `PermissionCatalog.cs` (bilingual TH/EN labels; else `RbacAuthMapTests` may flag).
- [x] **8.2** `SqlScripts/619_fixed_assets_rls.sql` — G1 `company_isolation` for ALL
  THREE tables (`fixedasset.fixed_assets`, `fixedasset.depreciation_runs`,
  `fixedasset.depreciation_run_lines`). Copy `616` per-table block verbatim, swap names.
  DDL-only (assumes EF migration created the tables) → no 42501 at apply. No bypass arm.
- [x] **8.3** `SqlScripts/620_seed_fixed_asset_perms.sql` — MIRROR `610`/`615`/`617`.
  FIRST statement `SET LOCAL app.bypass_rls = 'on';` (F1 G3 mode). Then ONE file: insert
  the 4 codes `ON CONFLICT (permission_code) DO NOTHING`; grant all `fixedasset.%` to
  `SUPER_ADMIN` (`company_id IS NULL`); `sys.role_permission_templates` rows; fan-out to
  every existing company's roles (`NOT EXISTS` guard). **Role split (Open Q2 default):**
  `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT` get ALL four; `ACCOUNTANT` gets
  `read + manage + depreciation.run` (NOT `dispose`). Encode via per-role template rows,
  NOT a blanket `LIKE 'fixedasset.%'`. No literal braces (F3).
- [x] **8.4** `SqlScripts/622_seed_fixed_asset_prefix.sql` — add `'FA'` to
  `sys.document_prefixes` (plain metadata, no RLS — bare `INSERT … ON CONFLICT (prefix_code)
  DO NOTHING`, mirror `618`): `('FA','FIXED_ASSET','สินทรัพย์ถาวร','Fixed Asset', FALSE,
  FALSE, FALSE, TRUE, NOW())` (confirm the exact column list from `618`/`100`). The `FA`
  prefix numbers the asset MASTER doc only; every GL JE (depreciation/disposal) stays
  `"JV"`-numbered by `BuildAndPostAsync`.

### 9. API endpoints + FE

Endpoints in `Accounting.Api/Endpoints/FixedAssetEndpoints.cs`, registered in `Program.cs`;
service DI in `DependencyInjection.cs` (scoped). All `.RequireAuthorization(PolicyPrefix +
<perm>)`.
- [x] **9.1** `GET /fixed-assets` (list; filters status, category, date range) — `read`
- [x] **9.2** `GET /fixed-assets/{id}` (detail incl. run-line history) — `read`
- [x] **9.3** `POST /fixed-assets` (create draft) — `manage`
- [x] **9.4** `PUT /fixed-assets/{id}` (edit draft; Draft only) — `manage`
- [x] **9.5** `POST /fixed-assets/{id}/activate` — `manage`
- [x] **9.6** `POST /fixed-assets/{id}/dispose` (body: date, proceeds, vatAmount?, buyerName?) — `dispose`
- [x] **9.7** `POST /fixed-assets/{id}/write-off` (body: date, reason) — `dispose`
- [x] **9.8** `POST /fixed-assets/{id}/cancel` — `manage`
- [x] **9.9** `POST /depreciation-runs` (body: year, month) → generate+post — `depreciation.run`
- [x] **9.10** `GET /depreciation-runs` (list) + `GET /depreciation-runs/{year}/{month}` (detail) — `read`
- [x] **9.11** `GET /fixed-assets/reports/register`, `GET /fixed-assets/reports/accumulated-depreciation` — `read`

FE (Next.js App Router + DaisyUI) — mirror the `expenseClaims` FE build:
- [x] **9.12** `frontend/app/(dashboard)/fixed-assets/page.tsx` — list (clone
  `bank-accounts/page.tsx`: `DataTable` + status/category filters + PermissionGate "New").
  DONE — verified live: loads, shows docNo/name/category/acquireDate/cost/accumDep/nbv/status
  columns, status+category filters render, "New" button gated (visible for demo-admin).
- [x] **9.13** `frontend/app/(dashboard)/fixed-assets/new/page.tsx` — acquire form: name,
  category, acquire_date, VendorInvoice picker (optional), cost, salvage, useful-life
  (months or years×12), depreciation-start-date, 3 account overrides (defaulted). Shows
  computed `monthly_amount` preview. Save draft → Activate. DONE — verified live: filled
  cost=50000/life=36mo, live preview showed 1,388.89 (matches spec's §3 worked example
  exactly); save created the draft via `POST /fixed-assets`; VendorInvoice + 3 GL-account
  selects populated with real data from `useVendorInvoices`/`useGlAccounts`.
- [x] **9.14** `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx` — detail + actions
  (activate/dispose/write-off/cancel) via `<PermissionGate>`; NBV + "depreciated through
  YYYY-MM"; dispose modal (proceeds, VAT with the Option-A "issue a Tax Invoice separately"
  notice, buyer). Run-line history table. DONE — verified live end-to-end: Activate assigned
  `07-2026-FA-0001` and flipped Draft→Active; Dispose modal shows the Option-A VAT notice
  prominently, submitting with proceeds=25000/vat=1750/buyer posted the JE (gain/loss =
  -25,000, JE #60 linked) and flipped to Disposed; Write-off modal (date+reason only, no
  proceeds/VAT) opened correctly. Fixed a label bug found during this verification: the
  disposal gain/loss figure was rendered under the generic `common.total` key — replaced
  with a dedicated `fixedAssets.gainLoss` key (en/th) for clarity.
- [x] **9.15** `frontend/app/(dashboard)/depreciation/page.tsx` — run screen: month picker
  + "Generate depreciation" button (gated `depreciation.run`) + past-runs list. Confirm
  dialog. Surfaces `depreciation.already_posted` gracefully. DONE — verified live: confirm
  dialog showed "คิดค่าเสื่อมราคาสำหรับ กรกฎาคม 2026?"; generating posted a real run (total
  500.00 = 12,000/24mo asset's monthly charge, asset count 1, JE #61 linked) that
  immediately appeared in the past-runs list; the asset's detail page correctly showed
  "คิดค่าเสื่อมราคาถึง 2026-07" (depreciated-through) and the run-line history row.
- [x] **9.16** `frontend/lib/queries.ts` — `useFixedAssets`, `useFixedAsset`,
  `useCreate/UpdateFixedAsset`, `useActivate/Dispose/WriteOff/CancelFixedAsset`,
  `useDepreciationRuns`, `useGenerateDepreciation`, report hooks (invalidate
  `['fixed-assets']`, `['depreciation-runs']`). DONE — all hooks added + exercised live via
  the pages above (every mutation observed to hit the real `/api/proxy/...` route and
  invalidate correctly, confirmed via list/detail refetch after each action).
- [x] **9.17** i18n `fixedAssets` namespace in `messages/en.json`+`th.json` (grep `ম` F11);
  nav item(s) in `SidebarNav.tsx` (`perm:'fixedasset.read'`), placed in the
  purchase/accounting section. DONE — `fixedAssets`+`depreciation` namespaces added
  (en/th), `grep -rn "ম" frontend/` → empty. Nav items `สินทรัพย์ถาวร`/`ค่าเสื่อมราคา`
  verified rendered correctly in the sidebar (purchase section, right after
  ใบเบิกค่าใช้จ่าย/expense claims) in a live browser session.

### 10. Tests (`Accounting.Api.Tests`, `[Collection(PostgresCollection)]`)

Use today/future months only (F5). Isolate each test's company (`TestCompanyFactory`) so
the never-reset teas_test backlog can't interfere. New files under
`backend/tests/Accounting.Api.Tests/FixedAsset/`.
- [x] **10.1 Depreciation math (the invariant):** `Depreciation_full_life_ties_out_to_the_satang`
  — the §3 worked example (50,000 / 36mo): 36 monthly runs; months 1–35 charge 1,388.89,
  month 36 charges 1,388.85 (the plug), accumulated == 50,000.00 exactly, month 37 posts
  NO line for the asset. Assert each run's JE `Dr 5450 == Cr 1690 == charge`,
  `IsClosingEntry == false`. DONE — `FixedAssetServiceTests.Depreciation_full_life_ties_out_to_the_satang` (green).
- [x] **10.1b Undershoot plug (Fable review add):** 50,000 / 24mo → monthly 2,083.33
  (rounds DOWN); months 1–23 charge 2,083.33, month 24 (final scheduled month) charges
  `remaining = 2,083.41` (the reverse plug), accumulated == 50,000.00 exactly at month 24,
  month 25 posts NO line. Life never exceeds useful_life_months in either rounding direction.
  DONE — `Depreciation_undershoot_plug_closes_life_at_exactly_24_months` (green).
- [x] **10.2 Acquisition posts NO JE (FA-A double-book guard):** create + activate an asset
  → assert `JournalEntries` count for the company is unchanged (0 new). DONE — `Activate_posts_no_journal_entry`.
- [x] **10.3 Idempotency:** `GenerateDepreciationAsync(y,m)` twice → exactly one
  `DepreciationRun`, one JournalEntry, no doubled `accumulated_depreciation`.
  DONE — `GenerateDepreciation_called_twice_is_idempotent`.
- [x] **10.4 Race:** two concurrent `GenerateDepreciationAsync(y,m)` (genuine `Task.Run`)
  → exactly one run + one JE; loser throws `depreciation.already_posted` (unique-index
  backstop). DONE — `GenerateDepreciation_concurrent_calls_post_exactly_one_run` (green x3
  reruns). Implementation note: in a genuine race the FixedAsset.Version optimistic-concurrency
  check (F8) fires FIRST (inside PostManualEntryAsync's own SaveChangesAsync, since it shares
  the DbContext's change tracker with the already-mutated FixedAsset rows) — BEFORE the
  DepreciationRun unique-index insert is even attempted. Both `DbUpdateConcurrencyException`
  and the 23505 unique-violation are caught and mapped to `depreciation.already_posted`.
- [x] **10.5 Period-close hook:** (a) active asset due dep, no run → `CloseAsync(y,m)`
  throws `period.depreciation_required`; (b) after a run → close succeeds; (c) no active
  assets → close succeeds without a run. DONE — `PeriodClose_hook_blocks_then_allows_close_around_the_depreciation_run` (a+b), `PeriodClose_hook_allows_close_when_no_assets_are_due` (c).
- [x] **10.6 Disposal gain:** the §5 gain example → 5 JE lines by AccountId+amount,
  ΣDr==ΣCr==56,750, Output VAT line = 1,750 on 2151, gain 5,000 on 4200, asset Disposed,
  accum cleared. DONE — `Dispose_gain_reproduces_the_worked_example_JE_exactly`.
- [x] **10.7 Disposal loss + write-off:** the §5 loss example (loss 5,000 on 5460,
  balanced) and the write-off example (loss == NBV, no cash/VAT line, Status WrittenOff).
  DONE — `Dispose_loss_reproduces_the_worked_example_JE_exactly`, `WriteOff_reproduces_the_worked_example_JE_exactly`.
- [x] **10.8 Year-end interplay (FA-C):** post depreciation for the fiscal year, then
  `YearCloseService.CloseAsync` → depreciation-expense account (5450) nets to 0 as-of
  fiscalEnd (swept), 3300 holds it; `ProfitLossAsync` still shows the depreciation expense
  for the year (dep JE `IsClosingEntry=false`, included). Confirms no year-end code edit
  needed. DONE — `YearEnd_close_sweeps_5450_but_ProfitLoss_still_shows_the_depreciation_expense`
  (asserts 5450 nets to 0 as-of fiscalEnd via a direct GL query + `ProfitLossAsync.Totals.Expense
  == 12000.00`; did not separately assert 3300's exact magnitude — the two load-bearing claims
  are covered).
- [x] **10.9 Tenant scope (labelled query-filter, F6):** company A asset invisible to B.
  DONE — `Asset_from_company_A_is_invisible_to_company_B_via_query_filter` (labelled
  query-filter test, not RLS, per F6 — teas_test connects as superuser).
- [x] **10.10 Permission (HTTP-level, `RbacApiFactory`):** an ACCOUNTANT-shaped token
  (read+manage+run, per 620's split) is **403 on dispose**; a CHIEF_ACCOUNTANT token
  succeeds on dispose. DONE — `FixedAssetPermissionTests` (2 tests, green).
- [x] **10.11 Skip count vs baseline** (F9): reference baseline from dispatch = 901 total / 8
  skipped. Post-change: 915 total (147 Domain.Tests + 768 Api.Tests) / 907 passed / 0 failed /
  8 skipped — skip count UNCHANGED, +14 = exactly the new FixedAsset tests.

---

## Verification gates

- `dotnet build Accounting.sln` → 0 Error(s) (watch locked `testhost.exe` — MSB3027).
- `dotnet ef migrations add FixedAssets` → exactly the 3 tables + `fixedasset` schema;
  review generated SQL (no unexpected drops). Fable owns the `ef` command + any teas_test
  reset.
- `dotnet test backend/tests/Accounting.Api.Tests` (`TEAS_TEST_PG` + `TEAS_REPO_ROOT` set
  in the SAME shell) — new tests pass; **skip count == baseline** (a rise = false-green).
  Isolate a single flaky failure before calling it a regression (troubles-wiki "single
  DIFFERENT test fails each run").
- FE: repo FE typecheck green; scoped `vitest run lib/<touched>.test.ts` (NOT bare
  `vitest` — Playwright specs false-fail).
- **Deploy probe — ROW COUNTS, not exit codes (F1; both seed modes):**
  - Perms (620, G3 mode): `SELECT count(*) FROM sys.role_permissions rp JOIN
    sys.permissions p ON p.permission_id=rp.permission_id WHERE p.permission_code LIKE
    'fixedasset.%' AND rp.company_id IS NOT NULL;` must be `> 0` and ≈ `#companies ×
    #granted roles`. **Zero = the bypass was missing and the fan-out silently no-op'd on
    prod.**
  - Accounts (621, G1 mode): `SELECT count(*) FROM master.chart_of_accounts WHERE
    account_code IN ('1610','1690','5450','4200','5460');` must be ≈ `#companies × 5`. A
    short count = the `DO`-loop didn't run per company (or the bare-INSERT crashed +
    rolled back — the prod 42501). Also `SELECT count(*) FROM sys.applied_sql_scripts;`
    == number of `.sql` files on disk (a short count = a script hard-crashed).
- Public-domain E2E: `curl https://teas.kazaki-rio.com/api/proxy/fixed-assets` → 401
  (route exists + auth-gated); `curl .../api/proxy/depreciation-runs` → 401. (F10.)
- `grep -rn "ম" backend/ frontend/` (excl. bin/obj/node_modules) → empty (F11).

## Blast-radius cap

New files + edits confined to: `Accounting.Domain/Entities/FixedAsset/*`,
`Domain/Enums/FixedAsset*`, `Persistence/Configurations/FixedAsset/*`, a new EF migration,
`SqlScripts/619–622`, `AccountingDbContext.cs` (3 DbSet lines), `GlAccountsOptions.cs`
(5 additive props), `MasterDataServices.cs` (5 CoA seed rows), a new `FixedAssetService.cs`
(+ its DTOs/interface), `FixedAssetEndpoints.cs` + `Program.cs` + `DependencyInjection.cs`,
`Permissions.cs`/`PermissionCatalog.cs`, **ONE hook block in `PeriodCloseService.cs`
(§4)**, and the FE files + `messages/*.json` + `SidebarNav.tsx` listed in §9.

**STOP-and-re-spec triggers (do NOT design around):**
- Any edit to an EXISTING `GlPostingService` method or the private `BuildAndPostAsync`,
  or to `JournalService`/`YearCloseService` posting logic → depreciation/disposal MUST go
  through the existing `PostManualEntryAsync` unchanged. If they can't, STOP.
- Any edit to `PostTaxInvoiceAsync` / the sales-doc core / `TaxInvoice`/`Customer` for
  disposal VAT → that is disposal Option B; STOP and get a ruling (§0).
- Any change to `PeriodCloseService.CloseAsync` beyond the §4 two-read hook (new
  dependency, signature/transaction change) → STOP.
- Any change to year-end (`YearCloseService`, the sweep, `is_closing_entry` treatment) →
  depreciation is a normal in-period JE that year-end already handles; STOP.
- Missing GL account for a test → seed it in the test; do NOT change `ChartOfAccount`
  schema or `GlAccountsOptions` beyond the 5 additive props in §2.

## Open questions — Q1/Q2 RULED by Ham 2026-07-10; Q3-Q6 ruled by Fable (defaults)

- **Q1 — Disposal VAT = Option A (Ham ruling).** GL-only + manual-TI notice, as designed
  in §0/§5. No sales-core edits.
- **Q2 — Dispose role split (Ham ruling).** `COMPANY_ADMIN` + `CHIEF_ACCOUNTANT` get all
  four perms; `ACCOUNTANT` gets `read + manage + depreciation.run`, NOT `dispose`.
- **Q3 — Auto catch-up depreciation on disposal (non-blocking).** Phase-1 default: NO —
  disposal uses current accumulated; user runs depreciation through the disposal month
  first (screen shows "depreciated through YYYY-MM"). If Ham wants the disposal-month
  charge posted automatically as part of disposal, it's a small additive step in
  `DisposeAsync` (post a single-asset catch-up before the disposal JE). Recommend deferring.
- **Q4 — Category→GL-account master (non-blocking).** Phase-1: `category` is a grouping
  string; the 3 GL accounts are per-asset (defaulted, editable in Draft). A category master
  that auto-maps accounts + default useful life is a clean phase-2 enhancement. Confirm the
  string-category approach is acceptable for phase 1.
- **Q5 — Multi-branch depreciation JE (non-blocking).** Phase-1: one aggregate JE per
  company-month under the primary branch. Per-branch split (one JE per branch) is phase 2.
- **Q6 — Disposal cash vs bank account (non-blocking).** Phase-1 credits `CashAccount`
  (1110). A bank-account picker (Cycle B `bank.bank_accounts.GlCashAccountId`) can be a
  small additive option on the dispose modal. Recommend phase 2.

## Attempt log
<!-- - <date> <worker>: <result / failure summary> -->
- 2026-07-09 Fable (designer): initial spec authored. Read `PLAN §4`,
  `specs/expense-claims.md` (inherited footguns 1–11), `specs/year-end-closing.md` (GL
  posting + period machinery + seed footguns + year-end interplay), the two prod-hardened
  seed exemplars `610`/`611` (G3 bypass vs G1 per-company loop), and prod-incident memory
  15280 (RLS-masked startup seeds, 42501 / silent zero fan-out). Dispatched Explore for
  exact code shapes: `PeriodCloseService.CloseAsync`/`EnsureOpenAsync`, `GlPostingService`
  `PostManualEntryAsync`/`BuildAndPostAsync`, `GlAccountsOptions` (no FA accounts exist),
  `VendorInvoice`, SqlScripts numbering (next-free 619), disposal-VAT machinery. Key
  design locks: acquisition posts NO JE (VI already booked it — FA-A); depreciation +
  disposal reuse the EXISTING `PostManualEntryAsync` (zero GlPostingService edits);
  `charge = min(monthly, remaining)` makes the final-month plug structural (FA-B);
  period-close hook = one two-read block keyed off the `DepreciationRun` table.
  Surfaced BLOCKING disposal-VAT/ภ.พ.30 question (§0) — designed Option A fully.
  Advisor tool was unavailable this session.
- 2026-07-10 sonnet-implementer: Backend §1–§8, §9.1–9.11, §10 COMPLETE. Entities/enums/EF
  config/migration (3 tables, `fixedasset` schema, no drops); SqlScripts 619–622 (RLS,
  perms G3-bypass, GL accounts G1 DO-loop mirroring 611 verbatim, FA prefix);
  `GlAccountsOptions` +5 props, `DefaultChartOfAccounts` +5 rows; `FixedAssetService`
  (activate/depreciation engine/dispose/write-off/reports); the ONE `PeriodCloseService`
  hook block (§4, nothing more); endpoints/DI/Program wiring; `Permissions.cs`/
  `PermissionCatalog.cs`. 14 new tests, all green, skip count unchanged vs baseline
  (901→915 total, 8 skipped both times). One real bug found+fixed during 10.4 (race test):
  `DbUpdateConcurrencyException` on the shared-DbContext FixedAsset.Version check fires
  BEFORE the DepreciationRun unique-index insert in a genuine race — widened the catch to
  handle both. Deviations from spec pseudocode (both minimal, noted for Fable review):
  (a) `GenerateDepreciationAsync` no-ops (no run row, no JE) when zero assets are due in a
  month, rather than attempting an empty/unbalanced `PostManualEntryAsync` call — §3.1's
  pseudocode doesn't cover this case; (b) §7.1 register report includes WrittenOff assets
  alongside Active+Disposed (conservative reading of "include Active + Disposed", not a
  narrower one — no test exercises this report's row set). FE (§9.12–9.17) NOT started —
  dispatching to a fresh sonnet-implementer next with the full DTO/route contract from this
  session, per Ham's project convention of parallel-safe FE work (disjoint file set, no DB).
- 2026-07-10 sonnet-implementer: §9.12–9.17 (FE) COMPLETE. New files: `fixed-assets/page.tsx`
  (list), `fixed-assets/new/page.tsx` (acquire form), `fixed-assets/[id]/page.tsx` (detail +
  activate/dispose/write-off/cancel), `depreciation/page.tsx` (run screen). Edits:
  `lib/types.ts` (+FixedAsset* DTOs), `lib/queries.ts` (+13 hooks), `messages/en.json`+
  `th.json` (+`fixedAssets`/`depreciation` namespaces, +nav keys, +3 `status` keys),
  `SidebarNav.tsx` (+2 nav items, purchase section). `tsc --noEmit` → 0 errors.
  `grep -rn "ম" frontend/` → empty. No `lib/queries.test.ts` exists (skipped per spec's
  conditional gate). Ran a REAL local stack for the live smoke test (not just static review):
  `dotnet run` against `accounting_dev` (auto-migrated `FixedAssets` + applied SqlScripts
  619–622 cleanly, confirmed in the startup log — RLS policies, perms G3-bypass fan-out,
  GL-accounts G1 DO-loop, FA prefix all applied with zero errors) + `next dev`. Logged in as
  `demo-admin` (co2, COMPANY_ADMIN — all 4 fixedasset perms) and drove the FULL lifecycle for
  real through the actual UI: create draft (monthly-amount live preview computed 1,388.89 for
  the exact 50,000/36mo §3 worked example) → activate (assigned `07-2026-FA-0001`, zero new
  JEs per FA-A) → dispose (gain/loss -25,000 posted correctly, JE #60, Option-A VAT notice
  rendered) on asset 1; created + activated a second asset (12,000/24mo) → generated July 2026
  depreciation from the run screen (posted 500.00, JE #61, appeared in past-runs) → confirmed
  the asset detail page's "depreciated through 2026-07" + run-line history render correctly.
  Opened (not submitted) the write-off modal to confirm its date+reason-only shape. Found and
  fixed one real bug during this live testing: the disposal gain/loss figure was mislabeled
  under `common.total` — moved to a dedicated `fixedAssets.gainLoss` key. Desktop viewport
  (1920×855, the session's actual window size) fully verified with screenshots at every step.
  **Mobile viewport (390×844) gate NOT completed** — `resize_window` reported success but
  `window.innerWidth` stayed at 1920 across multiple attempts, a fresh tab, AND a fresh
  tab-group/window (isolated as a genuine tool/environment limitation, not app code — no
  in-page mechanism can force a maximized Chrome window smaller). All new pages exclusively
  reuse the same responsive primitives (`PageHeader`, `DataTable`, `.modal`/`.modal-box`,
  `grid grid-cols-1 md:grid-cols-*`, `flex flex-wrap`) already used verbatim by
  `expense-claims`/`bank-accounts`/`period-close`, which are proven mobile-responsive
  elsewhere in this codebase — but this is NOT a substitute for an actual narrow-viewport
  screenshot. Flagging for Fable: either accept this evidence + the primitive-reuse argument,
  or re-run the mobile check with a working viewport-resize path (e.g. a genuinely separate
  Chrome window/profile, or CDP device-metrics override) before sign-off. Backend (`dotnet
  run`) and frontend (`next dev`) dev servers were left RUNNING in the background
  (localhost:5000 / localhost:3000) for Fable's own follow-up verification if needed;
  2 test fixed assets (`Test Laptop Asset` #1 Disposed, `Test Office Chair` #2 Active with one
  posted depreciation run) now exist in `accounting_dev` co2 — cosmetic-only test data, safe
  to ignore or clean up.
- 2026-07-10 sonnet-implementer: Tier-2 review MINOR fix applied — period-close deadlock guard.
  Root cause: an Active asset whose frozen `MonthlyAmount` rounds to 0.00 (DepreciableBase
  small enough, e.g. 0.10 over 36 months) never accrues a `DepreciationRunLine` in any
  non-final month (`charge = min(0, remaining) = 0` → skipped), so `PeriodCloseService`'s hook
  (`AccumulatedDepreciation < DepreciableBase`) sees it as perpetually "due" with no way to
  ever post a satisfying run for that month — blocking close for the whole company on any
  month before the asset's final scheduled month. Fixed at the source: `FixedAsset.Activate()`
  (`backend/src/Accounting.Domain/Entities/FixedAsset/FixedAsset.cs`) now throws
  `DomainException("fixed_asset.monthly_amount_zero", ...)` when `MonthlyAmount == 0m &&
  DepreciableBase > 0m`, rejecting the activation before the asset can ever reach Active.
  `DepreciableBase == 0` exactly (cost == salvage) is correctly NOT blocked — verified the
  due-check `accumulated(0) < base(0)` is false, so a zero-base asset never blocks close.
  Added `Activate_throws_when_monthly_amount_rounds_to_zero` (Cost=100.10, Salvage=100.00,
  life=36 → DepreciableBase=0.10, MonthlyAmount rounds to 0.00) to
  `FixedAssetServiceTests.cs`. Evidence: `dotnet build Accounting.sln` → 0 Warning(s), 0
  Error(s) (first killed a stale locked `Accounting.Api.exe` process per troubles-wiki
  MSB3027 entry — leftover from the FE live-smoke `dotnet run`, not a code issue);
  `dotnet test tests/Accounting.Api.Tests --filter "FullyQualifiedName~FixedAsset"` → 15
  total / 15 passed (14 previous + this new test) / 0 failed. Per Tier-2 instruction, did
  NOT rerun the full solution suite (Tier-3 gate owns it) — full-suite evidence remains the
  915/907/0/8 result from the pre-fix backend state noted above; this fix only ADDS a guard
  (a strictly narrower activation surface), does not touch any other passing test's code
  path. No commits made.

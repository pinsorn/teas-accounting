# Spec: Year-End Closing Entries (ปิดบัญชีสิ้นปี) — feature #5

<!-- Living document. Worker updates the checklist as it works; a retry uses the
     SAME file and grows the Attempt log — never rewrite the spec for a retry. -->

**Source plan:** `PLAN-feature-cycle-2026-07.md` §5. Branch: `feat/cycle-a-quick-wins`.
**Scope class:** FOOTGUN ZONE — money math + EF schema migration + new RBAC perm + cross-cutting
report change + a new equity account. Plan sized this 【S】; it is actually **M–L** (see "Scope
reality" below). Design is Opus/Fable-owned; a mid-tier implementer types it FROM this spec.
**Capability map (Fable fills at dispatch):** Sonnet implements backend (single sequential worker —
schema + service + reports share files); Opus or Codex Tier-2 review (money + schema + tenant
lenses); Haiku Tier-3 gate; Fable diff review + `ef` commands + commit. FE lives in a separate
spec (`specs/cycle-a-frontend.md`) — this spec only defines the API contract.

---

## Scope reality (read first — the plan underestimated this)

The plan assumed "generate a closing JE + year lock." Reading the actual code (2026-07-08) surfaced
four facts that expand scope; none are optional:

1. **There is NO retained-earnings account in the chart of accounts.** `DefaultChartOfAccounts`
   (`MasterDataServices.cs`) jumps from `2170` (last liability) straight to `4000` (first revenue) —
   there are **zero 3xxx Equity rows** anywhere, and the balance sheet fabricates
   `CurrentPeriodEarnings` arithmetically. A closing JE has nowhere to post net income today. → we
   must seed a `3300` Retained-Earnings equity account (existing companies + new-company default).
2. **There is no closing-entry flag on journal entries** and **no JE type/source enum** — the only
   discriminator is `PrefixCode` (always `"JV"`). → we add an `is_closing_entry` boolean column.
3. **Three range-based Revenue/Expense aggregations** would read **zero** after a closing JE posts
   (the sweep zeroes the P&L accounts inside the date range) unless each excludes closing entries.
   This is the core footgun (see §C).
4. **Fiscal year is company-configurable** (`Company.FiscalYearStartMonth`), not hardcoded Jan–Dec.

---

## Context / footguns (fold in — do NOT rediscover)

### Existing machinery (verified 2026-07-08, exact locations)

- **Period close** — `POST /periods/{year:int}/{month:int}/close` (perm `gl.period.close` =
  `Permissions.Gl.PeriodClose`, held by **CHIEF_ACCOUNTANT + COMPANY_ADMIN**) and
  `GET /periods/{year:int}/{month:int}/status` (`.RequireAuthorization()` only) in
  `backend/src/Accounting.Api/Endpoints/PeriodEndpoints.cs`. Service:
  `IPeriodCloseService` / `PeriodCloseService.cs` (`backend/src/Accounting.Infrastructure/Ledger/`).
  `CloseAsync(year, month, notes, ct)` refuses if any **Draft** TaxInvoice/PaymentVoucher/JournalEntry
  exists in the month, then flips an `AccountingPeriod` row to `Closed`. It posts **no** JE and does
  **no** retained-earnings roll-up today.
- **AccountingPeriod** (`ITenantOwned`) — `backend/src/Accounting.Domain/Entities/Ledger/AccountingPeriod.cs`:
  `PeriodId, CompanyId, int Year, short Month, PeriodStatus Status {Open,Closed}, DateTimeOffset? ClosedAt,
  long? ClosedBy, string? CloseNotes`. Table `gl.accounting_periods`, unique `(CompanyId, Year, Month)`.
  `IsOpenAsync(y,m)`: an explicit row is authoritative; a **missing** row is OPEN only for the current
  Asia/Bangkok month, else CLOSED.
- **Closed-period enforcement is application-level only** — via `IPeriodCloseService.EnsureOpenAsync(docDate)`
  (throws `DomainException("period.closed", …)`), called by each document service before posting.
  **There is NO DB trigger and NO period check inside `GlPostingService` or `JournalService`.**
  → The closing JE (which is dated inside an already-closed period, by precondition) must post through a
  path that does **not** call `EnsureOpenAsync`. `GlPostingService` is exactly that path.
- **Two JE posting paths:**
  - `JournalService` (`Infrastructure/Ledger/JournalService.cs`, routes `POST /journals`,
    `POST /journals/{id}/post`) — **FORCES `DocDate`/`PostingDate` to `_clock.TodayInBangkok()`**
    (cannot back-date to a period-end). **Do NOT use it for the closing JE.**
  - `GlPostingService` (`Infrastructure/Ledger/GlPostingService.cs`) — internal poster used by source
    docs. Private `BuildAndPostAsync` validates balance (`gl.unbalanced`), allocates the `JV` number via
    `_numbers.NextAsync(..., entry.DocDate, ct)`, calls `MarkPosted`. Resolves accounts by **AccountCode**
    (`ResolveAccountIdAsync`, throws `gl.account_missing`). It has **no period-open check** and accepts a
    real `DocDate`. → Add a new public method here for the closing/reversing entry (see §B4).
- **JournalEntry** (`Domain/Entities/Ledger/JournalEntry.cs`): `long JournalId, int CompanyId, int BranchId,
  string? DocNo (null until post), string PrefixCode ("JV"), DateOnly DocDate, DateOnly PostingDate,
  string Description, string? Reference, decimal TotalDebit/TotalCredit, DocumentStatus Status,
  DateTimeOffset? PostedAt, long? PostedBy, long? ReversalOfId (+ nav ReversalOf), Version (concurrency),
  ICollection<JournalLine> Lines`. `IsBalanced => TotalDebit==TotalCredit && TotalDebit>0`.
  `MarkPosted(docNo,userId,postedAt)` throws `je.not_draft/je.unbalanced/je.no_docno`.
  `JournalLine`: `LineId, JournalId, LineNo, long AccountId, decimal DebitAmount, decimal CreditAmount,
  string? Description, string? Reference, string? DimensionsJson, int? BusinessUnitId`.
- **JE immutability** — `SqlScripts/020_journal_immutability.sql`: `fn_enforce_je_immutability` blocks
  UPDATEs to a POSTED row's critical fields (`doc_no, doc_date, posting_date, total_debit, total_credit,
  company_id, branch_id`); `fn_no_delete_posted_je` blocks DELETE of non-DRAFT rows. **No void, no
  programmatic reversal service** — `ReversalOfId` is a schema-only column nothing writes yet.
  `is_closing_entry` is NOT in the immutability allowlist and is set at INSERT only → no trigger conflict.
- **AccountType** enum (`Domain/Enums/AccountType.cs`): `Asset, Liability, Equity, Revenue, Expense`.
  Reports classify purely by `AccountType`. `ChartOfAccount`: `AccountCode, AccountNameTh/En, AccountType,
  NormalBalance ("DR"/"CR"), IsHeader, ParentId`. `AccountId` is `long`.
- **Report internals** (`Infrastructure/Reports/FinancialReportService.cs`, all filter `Status==Posted`
  and `DocDate` on `JournalEntries`, joined `JournalLines`→`ChartOfAccounts`):
  - `TrialBalanceAsync` — groups all posted lines `DocDate <= asOf` by account; `Net = Dr - Cr`; `Balanced = td==tc`.
  - `BalanceSheetAsync` — same aggregation `DocDate <= asOf`; Equity/Liability = `Cr-Dr`, Asset = `Dr-Cr`;
    **Revenue+Expense collapse into a computed `earnings` line** (`Σ Rev(Cr−Dr) − Σ Exp(Dr−Cr)`) =
    `CurrentPeriodEarnings`; `LiabilitiesAndEquityTotal = liab + equity + earnings`.
  - `ProfitLossAsync` — posted lines `DocDate` in `[from,to]`, `AccountType in {Revenue,Expense}`;
    `Revenue += Cr−Dr`, `Expense += Dr−Cr`, `NetProfit = rev − exp`, grouped by BusinessUnit.
  - `GeneralLedgerAsync` (feat GL, DONE) — per-account posted lines in a range with running balance.
- **RBAC** (`Api/Authorization/Permissions.cs`): nested static classes; `Gl.PeriodClose = "gl.period.close"`.
  A new code must ALSO be appended to the `public static readonly IReadOnlyList<string> All` array.
  Roles (12): SUPER_ADMIN, COMPANY_ADMIN, CHIEF_ACCOUNTANT, ACCOUNTANT, AR_CLERK, AP_CLERK, SALES_STAFF,
  PURCHASING_STAFF, WAREHOUSE_STAFF, APPROVER, AUDITOR, TAX_OFFICER. RBAC is **per-company** (post-510):
  grants live in `sys.role_permissions (…, company_id)`; new companies clone from
  `sys.role_permission_templates`. Grant tables JOIN `sys.permissions` **by code string**.
- **SqlScripts** (`Infrastructure/Migrations/SqlScripts/`): 3-digit prefix; runner `DbInitializer.ApplyScriptsAsync`
  orders **lexically**, tracks applied names in `sys.applied_sql_scripts`, runs each in its own tx.
  **Highest current = `600_superadmin_scoped_rls.sql`.** Use **610+**. **New tables are created by EF
  migrations, NOT by SqlScripts**; RLS for a new table goes in its own `NNN_<name>_rls.sql`.
  DbInitializer applies EF migrations BEFORE SqlScripts (confirm — existing `_rls.sql` reference
  EF-created tables), so an RLS script may assume the migration's table exists.
- **Tenant/RLS** — GUC is `app.company_id` (set by `TenantMiddleware`). `ITenantOwned { int CompanyId }`
  is auto-picked-up by `AccountingDbContext.ApplyTenantFilters` (global query filter — no per-entity
  wiring). RLS boilerplate to mirror is `600_superadmin_scoped_rls.sql` group G1 (plain
  `company_isolation` USING `company_id = NULLIF(current_setting('app.company_id', true), '')::INT`).
  `app.is_super_admin` as a data-scope GUC is **RETIRED** — do NOT copy the old `OR is_super_admin`
  arm from 570/581/322. `fiscal_year_closes` is NOT scanned cross-company → plain policy, no
  `app.bypass_rls` arm.

### troubles-wiki.md entries that apply here (folded in)

- **Immutability triggers guard a NAMED allowlist, not every column** (570/583 entry). Relevant framing:
  posted JEs are immutable on critical fields; a reversing entry is a NEW contra-JV, never an edit/void.
- **teas_test superuser masks RLS** (memory `rls-masked-by-superuser-tests`): teas_test/dev connect as
  Postgres SUPERUSER → RLS bypassed. Any RLS/tenant-isolation assertion must run under a NOBYPASSRLS role:
  prefer `SET ROLE pg_database_owner` (older portable trick used by `SalesChainRlsTests`) over the newer
  `teas_rls_test` role (which `[SKIP]`s when `CREATEROLE` is unavailable — troubles-wiki "teas_rls_test
  unavailable"). Do NOT trust a green test that silently bypassed RLS.
- **Relative-date seeds** (memory `relative-date-seed-temporal-tests`, seed 400 closes prev-month vs
  CURRENT_DATE): never hardcode a past year/month in a test. Derive the target fiscal year from the test
  `IClock`, and drive `DocDate`s from the clock — see §E "date strategy".
- **`JournalService.CreateDraftAsync` forces DocDate to clock-today** (memory `relative-date-seed…` §10):
  tests that need a JE dated inside a target fiscal year must seed the posted `JournalEntry` **directly
  via DbContext** (as `GeneralLedgerReportTests` does) with a controlled `DocDate`, NOT via the manual
  `POST /journals` path.
- **Migration ↔ teas_test fixture** (memory `migration-squash-teas-test-reset`): the fixture owns
  `__EFMigrationsHistory`; a NEW EF migration must apply cleanly to teas_test. If a stale teas_test
  schema blocks it, reset teas_test (the tiny net10 Npgsql console trick) so the fixture re-applies from
  scratch. Fable coordinates the reset — flag it at hand-off. Also: `TEAS_TEST_PG` and `TEAS_REPO_ROOT`
  die between PowerShell calls — set BOTH in the SAME invocation as `dotnet test`; check skip-count vs
  baseline (a skipped test fakes green).
- **CS0433 Program ambiguity** (Workers ref) — not touched here (no Workers reference). Ignore unless a
  test project reference is added.
- **Thai ম glyph** (memory `thai-mo-glyph-pitfall`): grep `ম` (Bengali) before commit — creeps into Thai
  strings in seeds/DTOs/i18n.
- **Never put literal `{`/`}` in a SqlScript** (RBAC explorer): EF `ExecuteSqlRawAsync` treats them as
  `string.Format` placeholders and fails at boot.
- **`git add -u` misses new files** (memory): several new files here — explicitly `git add` them; grep
  `^??` for untracked source before commit.

---

## Design decisions (pinned — the implementer does not re-decide these)

- **D1. `is_closing_entry` flag drives report treatment; ONE surgical rule.**
  Point-in-time BALANCE reports (Trial Balance, Balance Sheet, General Ledger) **INCLUDE** closing
  entries — a balanced internal transfer is exactly what a post-closing balance must reflect (revenue/
  expense read 0, RE holds the sweep, everything still balances). Range-based P&L / net-income
  aggregations **EXCLUDE** closing entries — otherwise the annual figure reads zero. See §C for the
  exact three query edits (P&L, CIT, tax-summary) and the three that must stay unchanged.
- **D2. Retained earnings = a real seeded account `3300`** (`AccountType.Equity`, `NormalBalance "CR"`,
  `IsHeader false`, TH `กำไรสะสม`, EN `Retained Earnings`). Seeded into `DefaultChartOfAccounts` (new
  companies) AND fanned out to every existing company. Zero-balance until a close → dropped by the
  balance sheet's zero-row filter, so no visible change to any existing report pre-close (incl. co2/co3
  demos, which stay balanced). The close-year service resolves `3300` by code and errors clearly if
  absent.
- **D3. Closing JE posts via a new `GlPostingService` method**, NOT `JournalService`. Real `DocDate` =
  fiscal year-end; `PrefixCode "JV"`; `is_closing_entry = true`; no `EnsureOpenAsync` call (posting into
  the closed year is intentional and system-driven). Reuses the existing balance-validate + JV-number +
  `MarkPosted` machinery.
- **D4. Mistake-recovery = reopen-year via a reversing JE** (chosen over "block reopening"). Justification:
  JEs are immutable (no edit/void), so the only correct undo of a wrong/premature close is a NEW contra-JV;
  the `ReversalOfId` column exists precisely to link them; an unrecoverable mis-close before a real
  customer's year-end is unacceptable. Reopen posts the exact Dr/Cr-swapped reversal (`is_closing_entry =
  true`, `ReversalOfId = <closing JournalId>`) via the same §B4 poster and marks the `fiscal_year_close`
  row reversed. **Scope boundary (documented, enforced):** reopen-year undoes the close (restores RE and
  the P&L-account balances) but does NOT reopen the 12 monthly `AccountingPeriod` rows — those stay
  Closed; a period-level reopen is a separate future feature. So reopen-year recovers "the close itself
  was wrong / wrong year"; posting fresh adjustments then re-closing needs the future period-reopen.
- **D5. Fiscal year is company-configurable** via `Company.FiscalYearStartMonth` (default 1). For fiscal
  year `N`: `fiscalStart = new DateOnly(N, startMonth, 1)`, `fiscalEnd = fiscalStart.AddYears(1).AddDays(-1)`
  (mirror `FinancialStatementPdfService`/`CitYearDataService`). The **12 periods** = the (Year,Month) of
  each month in `[fiscalStart, fiscalEnd]`. FY is identified by its **start calendar year `N`** (consistent
  with the CIT/tax convention). Closing JE `DocDate = fiscalEnd`.
- **D6. Precondition = 12 EXPLICIT closed periods.** Require an `AccountingPeriod` row with
  `Status == Closed` for ALL 12 fiscal months (do NOT rely on `IsOpenAsync`'s implicit "past month with no
  row = closed" — year close is a deliberate act on deliberately-closed periods). Reject listing the
  still-open/missing months.
- **D7. `fiscal_year_close` record is the year-lock + audit source of truth.** "FY N is closed" ⇔ an
  **active (non-reversed)** row exists. A **filtered unique index** `(CompanyId, FiscalYear) WHERE
  reversed_at IS NULL` allows one active close per year while keeping reversed rows for audit and
  permitting a clean re-close after reopen.
- **D8. New perm `gl.year.close`** gates BOTH close-year and reopen-year, granted to the same roles as
  `gl.period.close` (CHIEF_ACCOUNTANT + COMPANY_ADMIN) + SUPER_ADMIN. (Reopen is more dangerous; if the
  reviewer wants a distinct `gl.year.reopen` perm, that's an acceptable split — note it, don't silently
  choose.)
- **D9. Year-lock semantics — what is blocked, and where enforced.** After FY `N` is closed (an active
  `FiscalYearClose` row exists):
  1. **Posting into any of the 12 periods of FY N** is already blocked — the precondition (D6) is that all
     12 `AccountingPeriod` rows are `Closed`, and every document service calls `EnsureOpenAsync(docDate)`
     before posting (throws `period.closed`). Year close does not add a new posting guard; it *depends on*
     the periods already being closed. (The closing JE itself bypasses this via §B4/D3 — the sole
     intentional exception.)
  2. **Re-closing FY N (double close)** is blocked in `CloseAsync` step 2 → `year.already_closed`
     (409), backed by the filtered-unique index `(CompanyId, FiscalYear) WHERE reversed_at IS NULL`
     (DB-level backstop against a race).
  3. **Reopening the monthly periods of a closed year** — there is **NO period-reopen endpoint in the
     codebase today**, so nothing can reopen them; `reopen-year` (D4) deliberately does NOT reopen them
     either. So a closed year's periods stay frozen. **Forward-looking guard:** when a period-reopen
     feature is later added, it MUST reject reopening any month whose FY has an active `FiscalYearClose`
     row (check `IYearCloseService.GetStatusAsync().IsClosed`). Note this requirement in
     `PeriodCloseService` as a `// FUTURE:` comment so it isn't missed — do not build the reopen path now.
  The `FiscalYearClose` row is the single source of truth for "is FY N locked"; all three checks key off it.

---

## Requirements (checklist)

<!-- [ ] not started · [~] partial + note · [x] done + evidence -->

### A. Schema (EF migration + entity + config)

- [x] **A1. `is_closing_entry` column on `gl.journal_entries`.** Add `public bool IsClosingEntry { get; set; }`
      to `JournalEntry.cs`. In `JournalEntryConfiguration.cs`: `b.Property(j => j.IsClosingEntry)
      .HasColumnName("is_closing_entry").HasDefaultValue(false);` (NOT NULL, default false). No change to
      the immutability trigger (column not in its allowlist, set at INSERT only).
      Evidence: both files edited; `dotnet build` 0 errors (stage-1 gate, see bottom).
- [x] **A2. New entity `FiscalYearClose` (`ITenantOwned`)** — `Domain/Entities/Ledger/FiscalYearClose.cs`:
      ```csharp
      public class FiscalYearClose : ITenantOwned
      {
          public int  FiscalYearCloseId { get; set; }   // identity PK
          public int  CompanyId { get; set; }
          public int  FiscalYear { get; set; }           // = start calendar year N
          public DateOnly FiscalStartDate { get; set; }
          public DateOnly FiscalEndDate   { get; set; }
          public decimal  NetProfit { get; set; }        // swept amount (Rev − Exp); for display
          public long?    ClosingJournalId { get; set; } // null iff zero activity (no JE posted)
          public DateTimeOffset ClosedAt { get; set; }
          public long?    ClosedBy  { get; set; }
          public string?  Notes { get; set; }
          public DateTimeOffset? ReversedAt { get; set; }
          public long?    ReversedBy { get; set; }
          public long?    ReversingJournalId { get; set; }
      }
      ```
- [x] **A3. `FiscalYearCloseConfiguration`** — `Persistence/Configurations/Ledger/FiscalYearCloseConfiguration.cs`
      (`internal sealed`, auto-discovered): `b.ToTable("fiscal_year_closes", "gl"); b.HasKey(x =>
      x.FiscalYearCloseId);` `Notes` max 500; `NetProfit` `HasPrecision(19,4)`; **filtered unique index**
      `b.HasIndex(x => new { x.CompanyId, x.FiscalYear }).HasFilter("reversed_at IS NULL").IsUnique();`
      No FK navigation to JournalEntry required (store the id only — avoids a cascade path).
- [x] **A4. DbSet** — add `public DbSet<FiscalYearClose> FiscalYearCloses => Set<FiscalYearClose>();` to
      `AccountingDbContext.cs` (gl area group).
- [x] **A5. EF migration** — FABLE-OWNED, not stage 1. Migration `20260708163202_YearEndClosing.cs`
      generated + applied cleanly to teas_test (stage 2) and to PROD (stage 5 hotfix, v1.15.0 → v1.15.x,
      RLS-scoped scripts 610/611 fixed). Final live confirmation: army leg B2-ye (2026-07-25, prod
      v1.22.11) ran the FULL year-end-closing lifecycle against a real production company (co6) — 12
      monthly closes, fiscal-year close, post-close report checks, post-close deny probe, reopen, re-close
      — all against the actual deployed `gl.fiscal_year_closes` table / `is_closing_entry` column /
      `3300` account. See `swarm-findings/army/B2-ye.md` for full evidence. Checkbox was never flipped by
      the backend implementer (Fable-owned item, correctly out of that worker's scope) even though the
      migration had been live and working since the stage-5 hotfix — this was an UNTESTED-by-checklist
      item that in fact was BUILT AND WORKING, not an unbuilt gap. Classification per B2-ye: **BUILT +
      WORKING**, now also live-verified end-to-end (was previously verified only by automated tests +
      the stage-5 hotfix, never by a full manual year-end-closing walkthrough in prod).

### B. Retained-earnings account + closing/reopen service

- [x] **B1. Seed `3300` Retained Earnings — code path.** Add the `3300` row to `DefaultChartOfAccounts`
      (`MasterDataServices.cs`, in the Equity gap between `2170` and `4000`): AccountCode `3300`,
      AccountNameTh `กำไรสะสม`, AccountNameEn `Retained Earnings`, `AccountType.Equity`, NormalBalance `CR`,
      `IsHeader false`. Match the exact tuple shape/casing of the neighbouring seed rows. Confirm no
      existing `3300` collision (`CreateAsync` dedupes by AccountCode).
- [x] **B2. Seed `3300` — SQL fan-out** to existing companies: `SqlScripts/611_seed_retained_earnings_account.sql`.
      Idempotent `INSERT … SELECT … WHERE NOT EXISTS` per company that lacks a `3300` row, into the same
      `master.chart_of_accounts` table/columns the CoA uses (confirm schema+columns from the entity config).
      No literal `{`/`}`. This runs for ALL companies incl. co2/co3 (safe — zero balance, hidden as a
      zero-row).
- [x] **B3. DTOs** — `Application/Ledger/YearCloseDtos.cs`:
      ```csharp
      public sealed record FiscalYearStatusPeriod(int Year, int Month, string Status, DateTimeOffset? ClosedAt);
      public sealed record FiscalYearStatus(
          int FiscalYear, int FiscalYearStartMonth,
          DateOnly FiscalStartDate, DateOnly FiscalEndDate,
          bool IsClosed, DateTimeOffset? ClosedAt, long? ClosedBy, string? Notes,
          long? ClosingJournalId, decimal? NetProfit,
          IReadOnlyList<FiscalYearStatusPeriod> Periods,  // the 12 fiscal months, in fiscal order
          bool AllPeriodsClosed);
      public sealed record CloseFiscalYearRequest(string? Notes);
      public sealed record FiscalYearCloseResult(
          int FiscalYear, DateOnly FiscalEndDate, decimal NetProfit,
          long? ClosingJournalId, DateTimeOffset ClosedAt);
      ```
      Interface `IYearCloseService` with `CloseAsync(int fiscalYear, string? notes, ct)`,
      `ReopenAsync(int fiscalYear, ct)`, `GetStatusAsync(int fiscalYear, ct)`.
      Evidence: `Application/Ledger/YearCloseDtos.cs` + `Application/Ledger/IYearCloseService.cs`
      (separate file, mirroring `IPeriodCloseService.cs`/`IJournalService.cs`+`JournalDtos.cs` split).
- [x] **B4. `GlPostingService` closing-entry poster.** Add a public method (mirrors private
      `BuildAndPostAsync`) that accepts a real `DocDate`, a description, an `isClosingEntry` flag, an
      optional `reversalOfId`, and a list of `(long AccountId, decimal Debit, decimal Credit)` lines;
      builds the `JournalEntry` (PrefixCode `"JV"`, Status Draft→post), validates balance, allocates the
      JV number via `_numbers.NextAsync(..., docDate, ct)`, `MarkPosted`, returns the JournalId. Sets
      `IsClosingEntry` and `ReversalOfId`. **Must NOT call `EnsureOpenAsync`.** Lines are given by
      AccountId (already resolved by the service), so no `ResolveAccountIdAsync` needed — but keep the
      `gl.unbalanced` guard.
      Evidence: added `PostClosingEntryAsync` to both files; `BuildAndPostAsync`/`JournalService` untouched.
- [x] **B5. `YearCloseService` (`Infrastructure/Ledger/YearCloseService.cs`) + DI registration**
      (`DependencyInjection.cs`). Logic:
      - `GetStatusAsync(fiscalYear)`: load `Company.FiscalYearStartMonth`; compute `fiscalStart/fiscalEnd`
        (D5); load the 12 `AccountingPeriod` rows; load the active `FiscalYearClose` row (if any); build
        `FiscalYearStatus`. `AllPeriodsClosed` = all 12 have an explicit `Closed` row.
      - `CloseAsync(fiscalYear, notes)`: (1) auth (`_tenant.IsAuthenticated`); (2) reject if an active
        `FiscalYearClose` row exists → `DomainException("year.already_closed", …)`; (3) require 12 explicit
        Closed periods else `DomainException("year.periods_not_closed", "Months still open: …")`;
        (4) compute per-account raw sums (see §C sweep math) over posted, **non-closing** revenue+expense
        lines `DocDate <= fiscalEnd`; (5) build sweep lines + the `3300` plug line (skip plug if zero;
        if <2 lines / zero total → post NO JE, `ClosingJournalId = null`); (6) post via §B4 (`DocDate =
        fiscalEnd`, description `$"ปิดบัญชีสิ้นปี {fiscalYear} / Year-end closing FY {fiscalYear}"`);
        (7) insert the `FiscalYearClose` row (NetProfit = Rev−Exp, ClosingJournalId, ClosedAt/By); wrap
        5–7 in one DB transaction. Return `FiscalYearCloseResult`.
      - `ReopenAsync(fiscalYear)`: (1) auth; (2) load active row else `DomainException("year.not_closed", …)`;
        (3) if it has a `ClosingJournalId`, post the Dr/Cr-swapped reversal via §B4 (`DocDate = fiscalEnd`,
        `isClosingEntry = true`, `reversalOfId = ClosingJournalId`, description `$"กลับรายการปิดบัญชี
        {fiscalYear} / Reopen FY {fiscalYear}"`); (4) set `ReversedAt/By/ReversingJournalId` on the row;
        one transaction. (Filtered-unique index frees the (Company,FY) slot once `reversed_at` is set,
        so a later re-close inserts a fresh active row.)
      Evidence: `Infrastructure/Ledger/YearCloseService.cs` (new) + `DependencyInjection.cs` registration.
- [x] **B6. Endpoints** — in `PeriodEndpoints.cs` (same `/periods` group; `{month:int}` constraint means
      these string segments never collide with the month route):
      - `POST /periods/{year:int}/close-year` — body `CloseFiscalYearRequest?`; perm `gl.year.close`.
      - `POST /periods/{year:int}/reopen-year` — perm `gl.year.close`.
      - `GET  /periods/{year:int}/year-status` — `.RequireAuthorization()` (mirror period `/status`; any
        authenticated tenant user reads their own year status). Returns `FiscalYearStatus`.
      Map `DomainException` codes to the existing problem-response convention (same as `CloseAsync` today:
      `year.already_closed` → 409; `year.periods_not_closed` → 422; `year.not_closed` → 409/404). Confirm
      how `PeriodEndpoints`/the exception middleware currently maps `period.*` codes and mirror it exactly.
      **CONFIRMED FINDING (worth flagging to Fable):** `DomainExceptionMiddleware.StatusFor(code)` only
      special-cases suffixes `.scope_required`/`.not_found`/`.locked_mismatch`/`.body_mismatch`/
      `.cross_bu_not_allowed_for_this_key` plus `auth.*` and `tenant.cross_tenant_access`; everything else
      (including the EXISTING `period.already_closed`/`period.closed`/`period.draft_present`) falls to the
      422 default. None of `year.already_closed`/`year.periods_not_closed`/`year.not_closed` match a
      special-cased suffix, so ALL THREE actually resolve to 422 today — not the 409/404 the API-contract
      (§F) text states. `DomainExceptionMiddleware.cs` is NOT in the blast-radius file list, so per "mirror
      it exactly" I did NOT touch it — codes are exactly as spec'd, mapping is whatever the existing generic
      mechanism produces (matches the precedent set by `period.already_closed` itself, which is also 422
      today, not 409). Flagging for Fable/FE-spec awareness — §F's documented HTTP statuses for the FE are
      currently aspirational, not real, unless the middleware is later extended.

### C. Report treatment — THE core footgun (exact edits)

- [x] **C1. EXCLUDE closing entries from `ProfitLossAsync`** (`FinancialReportService.cs`): add
      `&& !x.j.IsClosingEntry` to the `.Where(...)`. Verify the annual P&L over `[fiscalStart,fiscalEnd]`
      still returns the real Revenue/Expense/NetProfit after a close (test §E-2).
- [x] **C2. EXCLUDE closing entries from `CitYearDataService`** (~L177, the Expense-rows query feeding CIT
      รายการที่ 7): add `&& !x.j.IsClosingEntry` to its `.Where(...)`. Without this, CIT deductible-expense
      total reads zero after a close.
- [x] **C3. EXCLUDE closing entries from `TaxSummaryService`** (~L34, the `glRows` revenue/expense
      per-month query): add `&& !j.IsClosingEntry`. Without this, tax-summary net profit reads zero after a
      close.
- [x] **C4. Do NOT change** `TrialBalanceAsync`, `BalanceSheetAsync`, `GeneralLedgerAsync` — these are
      point-in-time balance reports that MUST include closing entries (post-closing TB shows zero P&L
      accounts and a populated `3300`; the balance sheet's `CurrentPeriodEarnings` correctly resets to ~0
      as-of `fiscalEnd` while `3300` carries the sweep — no double count). Add a one-line
      `// closing entries intentionally included — see specs/year-end-closing.md §C4` comment at each so a
      future editor doesn't "fix" it. Evidence: none of the 3 queries' `.Where` filters touched; comment
      added at each.
- [x] **C5. Confirm no OTHER range-based Revenue/Expense aggregation exists.** `grep -rn "AccountType\.\(Revenue\|Expense\)"
      backend/src` returns exactly: `FinancialReportService.cs` (C1/C4), `CitYearDataService.cs` (C2),
      `TaxSummaryService.cs` (C3), `MasterDataServices.cs` (CoA seed — not an aggregation, ignore). If any
      new file appears, apply the D1 rule (range net-income → exclude; point-in-time balance → include) and
      note it here. Also confirm `FinancialStatementPdfService` sources its P&L from `ProfitLossAsync`/one
      Evidence (re-ran after all edits): grep now ALSO hits the new `YearCloseService.cs` (the sweep query
      itself, §B5 step 4) — expected, not a finding; it already filters `!j.IsClosingEntry` by construction
      (the sweep must never re-sweep a prior closing entry). No other new file appeared. Confirmed
      `FinancialStatementPdfService.cs` calls `financialReport.ProfitLossAsync(...)` (no raw query of its
      own) — inherits the C1 fix.
      of the above (inherits the fix) rather than its own raw query — if it has its own, exclude there too.

> **TIER-2 CORRECTION (2026-07-08, Opus review — BLOCKING):** the original bound
> `DocDate <= fiscalEnd` is WRONG for year 2+: the sweep excludes closing entries, so it
> cannot see that a prior year's close already zeroed the accounts and re-sweeps ALL prior
> years (FY-N+1 close would store NetProfit = both years combined, leave phantom P&L
> balances, overstate RE). The sweep is a RANGE aggregation over the fiscal year — per D1
> it must be bounded `DocDate >= fiscalStart && DocDate <= fiscalEnd` (mirrors
> ProfitLossAsync). Consequence: a year skipped at close time stays un-swept until ITS OWN
> close runs — correct, each close sweeps exactly its own year.
> **Also (non-blocking, fix together):** `ReopenAsync` needs a double-reopen guard — the
> reversal must be conditional on winning the `reversed_at IS NULL` slot (affected-rows
> check or EF concurrency token), else two concurrent reopens post two reversing JEs and
> swing RE to −NetProfit. New tests: E11 (close FY N then N+1 → year-2 NetProfit correct,
> accounts zero, RE = sum) and E12 (double reopen → second fails cleanly, one reversal).

**Sweep math (§B5 step 4–5 — write exactly this, sign-safe; lower bound per Tier-2 correction):**
```
// Per revenue+expense account a, over posted NON-closing lines with
// DocDate >= fiscalStart AND DocDate <= fiscalEnd:
//   dr_a = Σ DebitAmount ; cr_a = Σ CreditAmount ; rawNet_a = dr_a - cr_a
// Closing line to zero account a (skip if rawNet_a == 0):
//   rawNet_a > 0  -> line { AccountId=a, Credit = rawNet_a }
//   rawNet_a < 0  -> line { AccountId=a, Debit  = -rawNet_a }
// totalRawNet = Σ rawNet_a  (== Expense_total - Revenue_total == -NetProfit)
// Retained-earnings (3300) plug line (skip if totalRawNet == 0):
//   totalRawNet < 0 (profit)  -> line { Account=3300, Credit = -totalRawNet }   // profit credited to RE
//   totalRawNet > 0 (loss)    -> line { Account=3300, Debit  =  totalRawNet }   // loss debits RE
// NetProfit (stored, displayed) = Revenue_total - Expense_total = -totalRawNet
// Post only if >= 2 lines and TotalDebit > 0 (JournalEntry invariant). Reversal = swap Debit<->Credit on every line.
```
Worked check: Revenue net Cr = 1,000,000; Expense net Dr = 600,000. Sweep: Dr Revenue 1,000,000; Cr Expense
600,000; plug totalRawNet = 600,000 − 1,000,000 = −400,000 → Cr 3300 400,000. ΣDr = 1,000,000; ΣCr =
600,000 + 400,000 = 1,000,000. Balanced. NetProfit = 400,000.

### D. RBAC + RLS SQL

- [x] **D1. Permission constant** — add `public const string YearClose = "gl.year.close";` to the `Gl`
      class in `Permissions.cs` AND append `"gl.year.close"` to the `All` array.
- [x] **D2. Perm seed `SqlScripts/610_seed_year_close_perms.sql`** — the canonical 4-step choreography
      from `590_seed_general_ledger_perms.sql` (insert-first/grant-second, SAME file):
      1. `INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
         ('gl.year.close','gl','year','close','Close/reopen fiscal year') ON CONFLICT (permission_code) DO NOTHING;`
      2. grant to `SUPER_ADMIN` (company_id IS NULL);
      3. `INSERT INTO sys.role_permission_templates` for `('CHIEF_ACCOUNTANT'),('COMPANY_ADMIN')`;
      4. fan-out to every existing company's `CHIEF_ACCOUNTANT`/`COMPANY_ADMIN` roles.
      Copy 590 verbatim, swapping the code + role set. NO literal `{`/`}`.
- [x] **D3. RLS `SqlScripts/612_fiscal_year_close_rls.sql`** — mirror `600` group G1 (plain
      `company_isolation`, no super-admin/bypass arm) for `gl.fiscal_year_closes`:
      ```sql
      ALTER TABLE gl.fiscal_year_closes ENABLE ROW LEVEL SECURITY;
      ALTER TABLE gl.fiscal_year_closes FORCE ROW LEVEL SECURITY;
      DROP POLICY IF EXISTS company_isolation ON gl.fiscal_year_closes;
      CREATE POLICY company_isolation ON gl.fiscal_year_closes
          USING (company_id = NULLIF(current_setting('app.company_id', true), '')::INT);
      ```
      (Assumes the EF migration created the table first — DbInitializer runs migrations before scripts.)
- [x] **D4. `PermissionCatalog.cs`** — add the Thai/EN label for `gl.year.close` (`ปิดบัญชีสิ้นปี` / `Close
      fiscal year`) alongside the `gl.period.close` entry, if the catalog requires every code to have a label
      (the `gl.period.close` label lives at `PermissionCatalog.cs:69` — mirror it, else `RbacAuthMapTests`
      may flag a missing catalog entry). Note: confirmed the catalog does NOT strictly require a label
      (missing entries fall back to `(code, code)`), so this wasn't strictly load-bearing, but added anyway
      to match precedent.

### E. Tests (integration; enumerate — implementer writes all)

New file `backend/tests/Accounting.Api.Tests/Ledger/YearEndClosingTests.cs` (service-level via
`TestCompanyFactory.CreateAsync`) + endpoint/RBAC assertions where noted. **Date strategy:** derive the
target fiscal year from a controlled `IClock` (do NOT hardcode a past year); seed posted revenue/expense
`JournalEntry` rows DIRECTLY via DbContext with `DocDate` inside the target fiscal year (NOT via
`POST /journals`, which forces today); seed the 12 `AccountingPeriod` Closed rows directly. Keep the
company isolated so the never-reset teas_test backlog can't interfere.

- [x] **E1. Happy path** — seed revenue+expense, 12 closed periods; `CloseAsync`. Assert: a closing JE
      posted (`is_closing_entry = true`, `DocDate = fiscalEnd`, balanced); `3300` GL balance as-of fiscalEnd
      == NetProfit; each revenue/expense account GL balance as-of fiscalEnd == 0; a `FiscalYearClose` row
      exists with the right NetProfit/ClosingJournalId. EXECUTED (stage 2) — PASSED first try.
- [x] **E2. P&L correctness AFTER close (the footgun)** — `ProfitLossAsync(fiscalStart, fiscalEnd)` still
      returns the ORIGINAL Revenue/Expense/NetProfit (NOT zero). Also assert `TaxSummaryService` and
      `CitYearDataService` net figures for the year are unchanged by the close (C2/C3 regression). EXECUTED
      — PASSED first try (proves C1–C3 correct).
- [x] **E3. Balance sheet + trial balance AFTER close** — `BalanceSheetAsync(fiscalEnd)`:
      `CurrentPeriodEarnings == 0`, Equity section includes `3300` at NetProfit, `Balanced == true`.
      `TrialBalanceAsync(fiscalEnd)`: revenue/expense rows == 0, `3300` == NetProfit, `Balanced == true`.
      EXECUTED — FAILED first run (test bug, not production code): my original assertion checked gross
      `Debit == 0 && Credit == 0` on the swept 4000/5100 rows, but `TrialBalanceAsync` reports RAW gross
      Dr/Cr sums across all posted lines ever touching the account (e.g. 4000 showed Debit=1,000,000
      Credit=1,000,000 — the original Cr 1,000,000 activity PLUS the sweep's offsetting Dr 1,000,000 — both
      real, neither zero). "Fully swept" means `Net (Dr−Cr) == 0`, not gross-zero. Fixed the assertion to
      `r.Net == 0m`; re-ran — PASSED. Production sweep math was correct throughout; this was purely a wrong
      test expectation, caught by actually running the test (not a rubber-stamp).
- [x] **E4. Reject: periods not all closed** — leave 1 period Open → `CloseAsync` throws
      `year.periods_not_closed` (422 at the endpoint), naming the open month; no JE, no record. EXECUTED —
      PASSED first try.
- [x] **E5. Reject: double close** — close, then close again → `year.already_closed` (409); exactly one
      active record, one closing JE. EXECUTED — PASSED first try.
- [x] **E6. Reopen** — after E1, `ReopenAsync`: a reversing JE posted (`ReversalOfId = closing JournalId`,
      `is_closing_entry = true`); `3300` back to 0 as-of fiscalEnd; `ProfitLossAsync` still shows original
      figures; the record has `ReversedAt` set; a subsequent `CloseAsync` for the same year succeeds
      (filtered-unique slot freed). EXECUTED — PASSED first try.
- [x] **E7. Zero-activity year** — no revenue/expense → `CloseAsync` creates the record with
      `ClosingJournalId == null` and posts NO JE; year reads closed. EXECUTED — PASSED first try.
- [x] **E8. Non-January fiscal year** — company with `FiscalYearStartMonth = 4`: FY spans Apr(N)–Mar(N+1);
      the 12 periods and `DocDate = fiscalEnd (Mar 31, N+1)` computed correctly; happy path passes. EXECUTED
      — PASSED first try.
- [x] **E9. RLS / tenant isolation** — two companies each close their own FY; company A cannot read
      company B's `FiscalYearClose` row or closing JE. Assert RLS under a NOBYPASSRLS role (`SET ROLE
      pg_database_owner` per troubles-wiki — teas_test superuser masks RLS). Also: no-perm user → 403 on
      close-year/reopen-year; cross-tenant `year-status` returns only the caller's data. EXECUTED —
      PASSED first try (RAN for real, not `[SKIP]`ped — confirms the `pg_database_owner` trick worked
      against the new `gl.fiscal_year_closes` RLS policy). The "no-perm user → 403" half is covered
      generically by `RbacCartesianTests` (see E10 evidence — 41/41 passed incl. that class), not
      duplicated here.
- [x] **E10. RBAC gates** — `RbacAuthMapTests` + `RbacMatrixTests` green (new `gl.year.close` registered in
      the catalog, granted to ≥1 non-super role so the matrix's super-only invariant holds). Needs
      `TEAS_REPO_ROOT` set in the same shell. `RbacAuthMapTests.cs` updated: added
      `"GET /periods/{year:int}/year-status"` to `ExpectedAuthnOnly` (else its own auto-scan would flag it
      as an unexpected finding). `RbacMatrixTests`/`RbacCartesianTests` need NO manual edits — both are
      fully data-driven off `sys.permissions`/`sys.role_permissions`, which 610 seeds correctly (granted to
      CHIEF_ACCOUNTANT + COMPANY_ADMIN, non-super, satisfying the super-only invariant). EXECUTED — PASSED:
      `dotnet test --filter "FullyQualifiedName~Rbac"` → 41/41 passed, 0 skipped (RbacAuthMapTests,
      RbacMatrixTests, RbacCartesianTests, RbacAdminServiceTests all green in one run).
- [x] **E11. Tier-2 BLOCKING regression** — close FY N, seed FY N+1 activity (different
      amounts: 300k/100k vs FY N's 1,000,000/600,000), close FY N+1: year-2 `NetProfit` must be
      year-2-ONLY (200k, not 600k combined); `4000`/`5100` net to zero as-of end-of-N+1 (no
      phantom balance); `3300` = sum of both years (600k). `E11_second_year_close_sweeps_only_its_own_year`
      — PASSED first try (confirms the `DocDate >= start` lower-bound fix is correct; reasoned
      through the math by hand to confirm this test WOULD have failed on the pre-fix code: without
      the lower bound, the FY N+1 sweep would have re-aggregated FY N's 1,000,000/600,000 too,
      producing NetProfit=600k and a non-zero phantom balance on 4000/5100 — this is a real
      regression guard, not a tautology).
- [x] **E12. Tier-2 non-blocking regression** — double reopen: close, reopen once (posts a
      reversing JE), reopen again → second call throws `year.not_closed` cleanly; exactly ONE
      reversing JE exists (`IsClosingEntry && ReversalOfId != null` count == 1).
      `E12_double_reopen_fails_cleanly_with_exactly_one_reversal` — PASSED first try (confirms the
      `ExecuteUpdateAsync`-based atomic `reversed_at` claim works for the sequential case; true
      concurrent-race coverage was not attempted — out of scope for a deterministic xUnit test —
      but the same WHERE-guarded conditional update is race-safe by construction for concurrent
      callers too, per the Tier-2 correction's design).

### F. Frontend — contract only (internals belong to `specs/cycle-a-frontend.md`)

- [x] **F1.** Do NOT build FE internals in this spec (not touched — `frontend/` is the concurrent FE
      worker's). The "Close fiscal year" action + confirm dialog lives
      on the Period Close UI page (`specs/cycle-a-frontend.md` #7). This spec's obligation is the API
      contract below; hand it to that spec's worker.

**API contract for the FE (camelCase JSON):**
- `GET /periods/{year}/year-status` → `FiscalYearStatus`:
  `{ fiscalYear, fiscalYearStartMonth, fiscalStartDate, fiscalEndDate, isClosed, closedAt, closedBy, notes,
     closingJournalId, netProfit, periods: [{ year, month, status, closedAt } × 12], allPeriodsClosed }`.
  Drives the period table AND the year-close button's enabled state (`allPeriodsClosed && !isClosed`).
- `POST /periods/{year}/close-year` body `{ notes? }` → `FiscalYearCloseResult`
  `{ fiscalYear, fiscalEndDate, netProfit, closingJournalId, closedAt }`. Errors: 422
  `year.periods_not_closed`, 409 `year.already_closed` — surface `detail` text to the user.
- `POST /periods/{year}/reopen-year` → 200; 409 `year.not_closed`.
- Both mutations gated by `gl.year.close`; nav/button hidden unless the user holds it (mirror how the
  period-close page gates on `gl.period.close`).

---

## Verification gates

**STAGE 2 STATUS (2026-07-08, sonnet-implementer):** ALL gates below EXECUTED and GREEN. Migration
`20260708163202_YearEndClosing.cs` (Fable-generated + reviewed) applied cleanly to `teas_test` on first
fixture run — no stale-schema block, no reset needed.

- `dotnet build` (full solution) → 0 errors, 0 warnings. Re-ran after the migration landed in the tree
  (`cd backend && dotnet build`) — still 0/0. (MSB3027 "locked by testhost" did not occur.)
- EF migration applies cleanly — Fable-owned, done outside this dispatch (see coordinator message);
  confirmed indirectly here: the `teas_test` fixture's `MigrateAsync()` applied it with zero errors across
  3 separate `dotnet test` invocations below (implicit reversibility not separately re-verified by me).
- `dotnet test --filter "FullyQualifiedName~YearEndClosing"` (TEAS_TEST_PG set in the SAME shell
  invocation) → **9 passed, 0 failed, 0 skipped** (Duration 1s). One test (`E3_balance_sheet_and_...`)
  failed on the FIRST run with a wrong test assertion (see E3 evidence above), fixed, re-ran green.
  Exact command:
  ```powershell
  $env:TEAS_TEST_PG = "Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"
  cd backend; dotnet test --filter "FullyQualifiedName~YearEndClosing"
  ```
- `dotnet test --filter "FullyQualifiedName~Rbac"` (TEAS_REPO_ROOT + TEAS_TEST_PG same shell) →
  **41 passed, 0 failed, 0 skipped** (Duration 3m2s) — RbacAuthMapTests, RbacMatrixTests,
  RbacCartesianTests, RbacAdminServiceTests all green. Exact command:
  ```powershell
  $env:TEAS_TEST_PG = "Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"
  $env:TEAS_REPO_ROOT = "Y:\ClaudePlayground\TEAS-Project"
  cd backend; dotnet test --filter "FullyQualifiedName~Rbac"
  ```
- Regression: `dotnet test --filter "FullyQualifiedName~Reports|FullyQualifiedName~BalanceSheet|FullyQualifiedName~ProfitLoss|FullyQualifiedName~TaxSummary|FullyQualifiedName~Cit"`
  → **green across both matched projects**: `Accounting.Domain.Tests` 30/30 passed, 0 skipped (188ms,
  pure-domain, no Postgres); `Accounting.Api.Tests` 63/63 passed, 0 skipped (27s) — proves C1–C4 didn't
  break existing report/tax math for un-closed data. Exact command:
  ```powershell
  $env:TEAS_TEST_PG = "Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"
  cd backend; dotnet test --filter "FullyQualifiedName~Reports|FullyQualifiedName~BalanceSheet|FullyQualifiedName~ProfitLoss|FullyQualifiedName~TaxSummary|FullyQualifiedName~Cit"
  ```
- Skip-count baseline check: 0 skipped across ALL THREE runs (9+41+93 = 143 tests total this session, 0
  `[SKIP]`) — no silent fake-green; the `E9` RLS test in particular ran for real (not skipped), confirming
  `SET ROLE pg_database_owner` worked against the new RLS policy.
- **co2 read-only invariant:** NOT separately probed this stage — not one of the 3 gates named in the
  coordinator's stage-2 dispatch, and no close was ever run against co2 (every test uses a fresh
  `TestCompanyFactory`-isolated company). Flagging as unverified-but-low-risk: the regression suite's
  Reports/BalanceSheet tests passing is indirect evidence nothing broke company-1-adjacent report math,
  but a direct live co2 balance-sheet check was not performed here.
- `grep -rn "ম" backend/ --include=*.cs --include=*.sql` (excl. bin/obj) → empty (re-confirmed stage 1).
- New-file check: `git status --porcelain | grep '^??'` → all new files present and un-ignored (see stage 1
  report); not yet `git add`ed (orchestrator commits per CLAUDE.md).
- **Middleware finding — coordinator decision:** the stage-1-flagged `DomainExceptionMiddleware` 422-for-
  all-`year.*` finding is ACCEPTED AS-IS (matches the `period.*` precedent; the FE surfaces `detail` text
  regardless of exact status code). No code change made to `DomainExceptionMiddleware.cs`.

**STAGE 3 STATUS (2026-07-09, sonnet-implementer — Tier-2 Opus review fix):** both Tier-2 findings
fixed in `YearCloseService.cs`; 2 new regression tests (E11/E12) added and green on first try.

- `dotnet build` (full solution) → 0 errors, 0 warnings (re-ran twice: after the sweep/reopen fix, and
  again after the E11/E12 tests + DocNo-pollution fix below).
- `dotnet test --filter "FullyQualifiedName~YearEndClosing"` (TEAS_TEST_PG same shell) → **11 passed, 0
  failed, 0 skipped** (was 9; +E11 +E12). Both new tests passed on the FIRST run — no fix-and-retry needed
  this time (unlike E3 in stage 2), confirming both Tier-2 code fixes were correct as designed.
- Regression filter (`Reports|BalanceSheet|ProfitLoss|TaxSummary|Cit`) → **unchanged from stage 2**:
  `Accounting.Domain.Tests` 30/30, `Accounting.Api.Tests` 63/63, 0 skipped combined.
- **FULL backend suite** (`dotnet test`, no filter, TEAS_TEST_PG + TEAS_REPO_ROOT same shell) — coordinator
  expected 843/0/8 (baseline 841/0/8 + 2 new tests). ACTUAL: `Accounting.Domain.Tests` 147/0/0 +
  `Accounting.Api.Tests` 695/**1**/8 = **842 passed, 1 failed, 8 skipped, 851 total**. The 1 failure —
  `Accounting.Api.Tests.Hardening.Sprint1HardeningTests.RolledBack_allocation_does_not_consume_a_number_or_create_a_gap`
  (`Npgsql.PostgresException 22003: value "11443527012" is out of range for type integer`) — is **NOT a
  regression from either Tier-2 fix or from E11/E12**; root-caused and documented in `troubles-wiki.md`
  ("Test-seeded DocNo poisons v_number_gaps"): my OWN stage-2 `AddPostedJe` helper's synthetic
  `DocNo = "JVTEST" + Guid.NewGuid().ToString("N")[..12]` happened, once, to end in an 11-digit run of hex
  characters that are all 0-9 (`JVTESTf11443527012`, company 700926) — `tax.v_number_gaps` (an unrelated,
  pre-existing shared view, `050_number_gap_audit_view.sql`, outside this spec's blast radius) casts a
  regex-extracted trailing-digit-run to `::int` with no length guard, across ALL companies' `doc_no`
  values, so this ONE row now breaks that view's query for EVERY company, permanently — `doc_no` is a JE
  immutability-guarded field (UPDATE blocked) and posted JEs can't be DELETEd, so the row cannot be
  cleaned up by a worker. **Confirmed root cause, not guessed:** wrote a throwaway net10 Npgsql console
  (scratchpad, not committed) to scan `gl.journal_entries` for `doc_no LIKE 'JVTEST%'` matching a >=9-digit
  trailing run — found exactly the one row, exactly matching the error's number. Isolated re-run
  (`--filter Sprint1HardeningTests`) reproduces the SAME failure deterministically (not flaky). **Fixed**
  `AddPostedJe`'s `DocNo` to append a trailing non-hex-digit `"X"` so this can never recur from THIS test
  file. **NOT fixed** (out of blast radius, flagging for the coordinator's decision): `050_number_gap_audit_view.sql`
  itself is still fragile to any real or test-seeded `doc_no` with a long trailing digit run — a defensive
  fix (cast to `bigint`, or cap the matched length) would prevent recurrence from ANY source, but that file
  has nothing to do with year-end-closing.md and I did not touch it. Math check confirming this is the
  ONLY discrepancy: 842 passed = 843 expected − 1 (this one pre-existing-class failure); 0 NEW failures
  introduced by the Tier-2 fixes themselves. Exact command:
  ```powershell
  $env:TEAS_TEST_PG = "Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"
  $env:TEAS_REPO_ROOT = "Y:\ClaudePlayground\TEAS-Project"
  cd backend; dotnet test
  ```

**STAGE 4 STATUS (2026-07-09, sonnet-implementer — coordinator-approved view fix rider):**
coordinator decided FIX THE VIEW (not accept-as-debt) — resolved.

- New `SqlScripts/613_number_gap_view_bigint.sql`: `tax.v_number_gaps` digit-run cast widened to
  `bigint` + `length(...) <= 18` guard (over-length run → no-match, same as today's "no trailing
  digits" case); final `missing_seq_no` column stays plain `int` (cast down at the outer SELECT) —
  `NumberGapReportService.cs` and the test's `SqlQueryRaw<int>` keep their exact contract. 050 not
  touched (apply-once tracking — 613 supersedes).
- `--filter "FullyQualifiedName~Sprint1HardeningTests"` → **4 passed, 0 failed, 0 skipped** (was
  3 passed / 1 failed) — the poisoned company-700926 row now renders as (very large but valid)
  data instead of crashing.
- Full backend suite (TEAS_TEST_PG + TEAS_REPO_ROOT same shell) → **843 passed, 0 failed, 8
  skipped, 851 total** — EXACTLY the coordinator's expected count. (First re-run of this stage
  briefly showed a DIFFERENT unrelated failure —
  `PayrollRunServiceTests.Pnd1_filings_follow_payment_date_not_period`, Payroll/PND1 domain —
  confirmed to pass in isolation; pre-existing suite flakiness, not caused by this fix. Second
  full run came back clean.)
- Self-inflicted footgun caught immediately in this stage: the first draft of 613's own header
  comment illustrated "don't use literal curly braces" WITH literal curly braces —
  `ExecuteSqlRawAsync` parses the whole script as a composite-format string regardless of real
  parameters, so that broke script application (4 unrelated tests failed identically with
  `FormatException`). Fixed by describing the constraint in prose only; documented in
  troubles-wiki.md alongside the main fix.
- No commit made. Frontend untouched.

## Blast-radius cap

Max **~18 files** touched (this is an M–L feature — the 【S】 estimate was wrong; do NOT try to squeeze it):

Backend source (11): `Permissions.cs`, `PermissionCatalog.cs`, `MasterDataServices.cs` (B1 only — the
3300 seed row), `JournalEntry.cs`, `JournalEntryConfiguration.cs`, `FiscalYearClose.cs` (new),
`FiscalYearCloseConfiguration.cs` (new), `AccountingDbContext.cs`, `GlPostingService.cs`,
`YearCloseService.cs` + `IYearCloseService`/`YearCloseDtos.cs` (new), `DependencyInjection.cs`,
`FinancialReportService.cs` (C1/C4), `CitYearDataService.cs` (C2), `TaxSummaryService.cs` (C3),
`PeriodEndpoints.cs` (B6). + the generated EF **migration** (Fable). SQL (3): `610_seed_year_close_perms.sql`,
`611_seed_retained_earnings_account.sql`, `612_fiscal_year_close_rls.sql`. Tests (1+):
`YearEndClosingTests.cs` (+ any RBAC/endpoint test file additions).

- **Public-API change:** ADDITIVE only — 3 new routes, 1 new perm, 1 new column, 1 new table, 1 new CoA
  account. No existing endpoint/DTO signature changes. The ONLY behavior change to existing endpoints is
  the 3 report queries excluding closing entries (invisible until a year is closed).
- **Forbidden without stop-and-re-spec:** touching `JournalService` (wrong posting path — D3); changing
  `TrialBalanceAsync`/`BalanceSheetAsync`/`GeneralLedgerAsync` query filters (C4); building FE internals
  (§F); building a period-level reopen (D4 boundary); hand-editing the EF migration snapshot; any
  `app.is_super_admin` RLS arm (retired). Hitting the cap or any forbidden item = STOP and report.

## Attempt log
<!-- - <date> <worker>: <result / evidence> -->
- 2026-07-08 sonnet-implementer (stage 1): Implemented everything except the EF migration
  (A5, Fable-owned) and test EXECUTION, per dispatch. Sections A1–A4, B1–B6, C1–C5, D1–D4 all
  `[x]`; E1–E9 tests `[~]` (written, not run — needs the migration); E10 `[x]` (RbacAuthMapTests
  allowlist updated; RbacMatrixTests/RbacCartesianTests need no edits, fully data-driven). F1 `[x]`
  (not touched, confirmed). File count: 23 total (18 backend source + 3 SQL + 2 tests) — literally
  above the "~18"/"(11)" labels, but every file maps 1:1 to an explicit checklist item, or is a
  mechanically unavoidable companion to one: `IGlPostingService.cs` (interface counterpart to the
  authorized `GlPostingService.cs` B4 change — `YearCloseService` depends on the interface, not the
  concrete class) and `RbacAuthMapTests.cs` (pre-authorized by the spec's own "Tests (1+): ... + any
  RBAC/endpoint test file additions"). The spec's own "(11)" backend-source label undercounts by
  expanding to 17 once its "YearCloseService.cs + IYearCloseService/YearCloseDtos.cs" bundle is
  unbundled — a spec-authoring label imprecision, not scope creep. Zero forbidden items touched
  (`JournalService`, `TrialBalanceAsync`/`BalanceSheetAsync`/`GeneralLedgerAsync` filters, FE,
  period-reopen, migration snapshot, `app.is_super_admin`), zero undispatched scope decisions made —
  flagging the raw count for Fable's diff review rather than silently asserting compliance.
  `dotnet build` (full solution): 0 errors, 0 warnings.
  `grep -rn "ম" backend/` → empty. C5 grep re-confirmed (see C5 note). One finding flagged, not
  fixed (out of blast radius): `DomainExceptionMiddleware.cs` maps all 3 new `year.*` codes to the
  generic 422 default (same as existing `period.*` codes), not the 409/404 the §F API-contract text
  states — see the B6 evidence note. Ready for stage 2 (migration generation + test execution).
- 2026-07-08 sonnet-implementer (stage 2): Migration `20260708163202_YearEndClosing.cs`
  (Fable-generated + reviewed) landed in the tree. Re-ran `dotnet build` (full solution) → 0
  errors, 0 warnings. Ran all 3 verification gates named in the coordinator's stage-2 dispatch, each
  with `TEAS_TEST_PG` (+ `TEAS_REPO_ROOT` for the Rbac gate) set in the SAME shell invocation as the
  `dotnet test` call (PowerShell `$env:X = "..."; dotnet test ...`):
  1. `--filter "FullyQualifiedName~YearEndClosing"` → 9/9 passed, 0 skipped. First run surfaced 1
     failure in `E3_balance_sheet_and_trial_balance_after_close` — a WRONG TEST ASSERTION (checked
     gross Debit==0/Credit==0 on swept accounts; `TrialBalanceAsync` reports raw gross Dr/Cr, not net
     — "fully swept" means `Net==0`). Fixed the assertion in `YearEndClosingTests.cs`, rebuilt
     (0/0), re-ran → green. Production sweep math (`YearCloseService.cs`) was correct throughout;
     this was a test-authoring bug caught by actually executing, not a code defect.
  2. `--filter "FullyQualifiedName~Rbac"` → 41/41 passed, 0 skipped (RbacAuthMapTests,
     RbacMatrixTests, RbacCartesianTests, RbacAdminServiceTests).
  3. `--filter "FullyQualifiedName~Reports|...~BalanceSheet|...~ProfitLoss|...~TaxSummary|...~Cit"` →
     Accounting.Domain.Tests 30/30 + Accounting.Api.Tests 63/63, 0 skipped combined.
  Migration applied cleanly to `teas_test` on the FIRST fixture run across all 3 invocations — no
  stale-schema block, `[SKIP]` reset trick not needed, no Fable coordination required. 0 skips
  across all 143 tests run this stage — no fake-green. Middleware finding: coordinator ACCEPTED
  as-is (matches `period.*` precedent, FE surfaces `detail` text) — no code change made, noted here
  per instruction. co2 read-only invariant was NOT separately probed (not one of the 3 named gates;
  no test ever touches co2 — every seed uses `TestCompanyFactory`-isolated companies). Updated
  E1–E10 checklist to `[x]` with per-gate evidence above. Sections A–F all `[x]`. Spec fully closed
  out pending Fable's final diff review + commit.
- 2026-07-09 sonnet-implementer (stage 3, Tier-2 Opus review fix): Fixed both findings in
  `YearCloseService.cs`. (1) BLOCKING — added the missing lower bound `j.DocDate >= start` to
  the sweep query (was `<= end` only), matching `ProfitLossAsync`'s range shape; updated the
  class + inline doc comments to describe the RANGE (not cumulative) behavior. (2) non-blocking
  — `ReopenAsync` now wins the `reversed_at` slot via an `ExecuteUpdateAsync` conditional update
  (`WHERE FiscalYearCloseId = … AND ReversedAt IS NULL`; 0 rows affected → `year.not_closed`)
  BEFORE posting the reversing JE, mirroring the `ExecuteUpdateAsync` pattern already established
  in `ApiKeyResolver.cs` (same codebase, same minimal-diff style — no EF concurrency token added,
  matching the dispatch's "pick the minimal diff" instruction). Added E11 (second-year sweep
  isolation) and E12 (double-reopen guard) to `YearEndClosingTests.cs`; both PASSED on the first
  run (11/11 total for the filter, up from 9). Regression filter unchanged (30+63, 0 skipped).
  Full backend suite: 842 passed / 1 failed / 8 skipped (vs. the coordinator's expected 843/0/8)
  — root-caused the 1 failure to a PRE-EXISTING, unrelated latent bug in
  `050_number_gap_audit_view.sql` that my OWN stage-2 test code's synthetic `DocNo` pattern
  triggered and permanently pollutes into the shared `teas_test` DB (full diagnosis + fix +
  troubles-wiki.md entry above and in that file — "Test-seeded DocNo poisons v_number_gaps").
  Fixed my test's `DocNo` generation to prevent recurrence; did NOT touch
  `050_number_gap_audit_view.sql` (out of this spec's blast radius) or attempt to
  delete/mutate the polluted row (blocked by JE immutability triggers — not a worker-level fix).
  Flagging for the coordinator: the view itself needs a defensive hardening
  (`::bigint` instead of `::int`, or a matched-length cap) to fully resolve, independent of this
  spec. `dotnet build`: 0/0 throughout. Sections A–F, E1–E12 all `[x]`. Spec fully closed out
  pending Fable's decision on the view fix and final diff review + commit.
- 2026-07-09 sonnet-implementer (stage 4, coordinator-approved Cycle A rider): Coordinator
  decided FIX THE VIEW (not accept-as-debt). Added `SqlScripts/613_number_gap_view_bigint.sql` —
  recreates `tax.v_number_gaps` (does NOT edit 050 — apply-once tracking, 613 supersedes on both
  existing and fresh DBs) with the digit-run cast widened to `bigint` and a `length(...) <= 18`
  guard on the captured run (over-length → treated as no-match, same as "no trailing digits"
  today); the exposed `missing_seq_no` column is cast back to plain `int` at the outer SELECT so
  `NumberGapReportService.cs` and the test's `SqlQueryRaw<int>` keep their exact existing
  contract — semantics otherwise identical, per the dispatch. First draft self-broke: its own
  comment explaining "don't use literal curly braces" WROTE OUT literal curly braces, and
  `ExecuteSqlRawAsync` parses the whole script as a `string.Format` string regardless of real
  parameters — caught immediately (4 unrelated Sprint1HardeningTests all failed identically with
  `FormatException`), fixed by describing the constraint in prose only. Documented both (the
  view fix AND the self-inflicted brace footgun) in the existing troubles-wiki.md entry.
  Gates: (1) `--filter Sprint1HardeningTests` → 4/4 passed, 0 skipped (was 3/1/0) — the
  previously-poisoned company's gap query now renders as data instead of crashing. (2) Full
  backend suite, TEAS_TEST_PG + TEAS_REPO_ROOT same shell: first run showed 842/1/8 again, but
  the ONE failure this time was a DIFFERENT, unrelated test
  (`PayrollRunServiceTests.Pnd1_filings_follow_payment_date_not_period`, Payroll/PND1 domain,
  nothing touched by this spec or the 613 fix) — verified it passes in isolation (1/1), concluding
  pre-existing suite flakiness/ordering sensitivity, not a regression. Re-ran the full suite once
  more: **843 passed / 0 failed / 8 skipped / 851 total** — exactly the coordinator's expected
  count, confirming the flaky Payroll test and the 613 fix are unrelated. `dotnet build`: 0/0
  throughout. No commit made; frontend untouched. Spec fully closed out.
- 2026-07-09 sonnet-implementer (stage 5, PROD INCIDENT hotfix): Prod deploy v1.15.0 failed at
  startup (auto-rolled-back to v1.14.1) — a bug class invisible on `teas_test` because that DB
  connects as a Postgres SUPERUSER (bypasses RLS unconditionally), documented in full as a NEW
  troubles-wiki.md entry ("Startup SqlScript writing/reading G1/G3 RLS'd tables fails 42501 or
  silently no-ops on prod"). Root causes: (1) `611_seed_retained_earnings_account.sql` INSERTs
  into `master.chart_of_accounts` (G1 — plain `company_isolation`, no bypass arm); with
  `app.company_id` unset at startup, Postgres's implicit `WITH CHECK` (== `USING` when none is
  given) rejects every row — 42501, deploy failed, 611's own transaction rolled back (never
  tracked). (2) `610_seed_year_close_perms.sql`'s step 4 SELECTs from `sys.roles` (G3 —
  system-global: `company_id IS NULL OR company_id = app.company_id OR app.bypass_rls`); with
  neither GUC set, only `company_id IS NULL` rows are visible, so the per-company fan-out
  `INSERT ... SELECT` silently inserted ZERO rows — no crash, "succeeded", got tracked, but
  granted `gl.year.close` to no real company (RbacMatrixTests was green on teas_test for the
  wrong reason — superuser fan-out worked there). Fixed both IN PLACE (coordinator-approved,
  do NOT edit 600/612's policies): 610 gets `SET LOCAL app.bypass_rls = 'on';` as its first
  statement (transaction-scoped, matches the existing app-layer `app.bypass_rls` idiom used by
  `RbacAdminService.cs`/`CompanySwitchService.cs`/`ApiKeyResolver.cs`/`ETaxRetryWorker.cs`); 611
  is rewritten as a `DO $do$` block looping `FOR c IN SELECT company_id FROM master.companies
  LOOP` + `PERFORM set_config('app.company_id', c.company_id::text, true);` before each
  per-company idempotent insert (mirrors the EXISTING `510_per_company_roles_reconcile.sql`
  fan-out pattern — confirmed `master.companies` itself carries no RLS policy, so reading the
  id list is unfiltered). Confirmed 612/613 are DDL-only (`ALTER TABLE`/`CREATE POLICY`/
  `CREATE OR REPLACE VIEW`) — no RLS-affected data writes at script-execution time, no changes
  needed. Caught and fixed my own transcription bug mid-edit (dropped the `'3300'` literal from
  611's rewritten SELECT list — column/value count mismatch; caught before ever building).
  **Verification beyond a plain green suite** (since the coordinator's own dispatch flagged
  that tests can't catch this class): deleted teas_test's `sys.applied_sql_scripts` tracker
  rows for both 610 and 611 (simulating the exact prod hotfix redeploy — "610's tracker row
  will be deleted on prod before redeploy so the fixed version re-runs") so the full suite run
  would actually RE-EXECUTE the new SQL instead of skipping already-tracked names; probed
  before/after with a throwaway Npgsql console (scratchpad, not committed): "companies missing
  3300" 10→2 (residual 2 are raw-SQL test-fixture companies created AFTER 611's re-run within
  the same session — expected run-once-seed behavior, not a defect — `created_at` = the
  DateTimeOffset default confirms a raw INSERT bypassing the app's onboarding path), `gl.year.close`
  per-company grants ~0 real → 23,282. `dotnet build`: 0/0. Full backend suite: **843 passed,
  0 failed, 8 skipped, 851 total** — re-ran with 610/611 actually re-applying, not skipped.
  No commit made; frontend untouched.

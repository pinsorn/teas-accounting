# Spec: Balance Sheet (งบแสดงฐานะการเงิน) — FE page + MCP tool

<!-- Living document. Worker updates the checklist as it works; a retry uses the
     SAME file and grows the Attempt log — never rewrite the spec for a retry. -->

**Scope class:** read-only reporting surface. NO schema, NO migration, NO new endpoint, NO new service/DTO, NO RBAC seed. The backend already has everything; this feature only *surfaces* it via MCP + FE.

**Capability map (Fable fills at dispatch):** Sonnet implements (backend MCP tool + FE, sequential); Haiku Tier-3 gate; Fable diff review + commit.

---

## Context / footguns

### What already exists (do NOT rebuild — verified 2026-07-08)
- **REST endpoint EXISTS:** `GET /reports/balance-sheet?asOfDate={yyyy-MM-dd}` in `backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs` (~L60). Gated on `Permissions.Report.TrialBalance` (`report.trial_balance.read`) — balance sheet **reuses the trial-balance perm**, it has no perm of its own.
- **Service method EXISTS:** `IFinancialReportService.BalanceSheetAsync(DateOnly asOfDate, CancellationToken ct)` → `BalanceSheetReport`. Impl in `backend/src/Accounting.Infrastructure/Reports/FinancialReportService.cs`. Balances by double-entry construction (every posted JE has equal DR/CR).
- **DTO EXISTS** (`backend/src/Accounting.Application/Reports/FinancialReportDtos.cs`):
  ```csharp
  record BalanceSheetRow(string AccountCode, string AccountNameTh, decimal Balance);
  record BalanceSheetSection(IReadOnlyList<BalanceSheetRow> Rows, decimal Total);
  record BalanceSheetReport(
      DateOnly AsOfDate, int CompanyId,
      BalanceSheetSection Assets, BalanceSheetSection Liabilities, BalanceSheetSection Equity,
      decimal CurrentPeriodEarnings,      // cumulative un-closed Revenue − Expense up to as-of
      decimal LiabilitiesAndEquityTotal,  // Liabilities.Total + Equity.Total + CurrentPeriodEarnings
      bool Balanced,                      // Assets.Total == LiabilitiesAndEquityTotal
      string Note);
  ```
  Zero-balance accounts are dropped; classification is purely by `AccountType` (no control-account concept).
- **MCP report-tool template EXISTS:** `get_trial_balance` in `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs` (~L674). MCP policy const `ReportTrialBalance = Pfx + "report.trial_balance.read"` already defined (~L227). Mirror it **exactly**.
- **FE report-page template EXISTS:** `frontend/app/(dashboard)/reports/trial-balance/page.tsx` — the closest template (single as-of date + a "Balanced" badge; no export). FE nav in `frontend/components/app-shell/SidebarNav.tsx` `SECTIONS`→`reports`.

### Footguns (fold in — do not rediscover)
- **No new backend endpoint = no Next.js passthrough needed.** FE report pages call the backend through the same-origin BFF proxy (`/api/proxy`, token injected server-side in `frontend/lib/api.ts`). The troubles-wiki "307 to /login" trap applies ONLY to *new* browser-hit backend routes; this feature adds none. Do NOT add a `frontend/app/**/route.ts` passthrough or touch `middleware.ts`.
- **MCP null serialization (troubles-wiki):** the MCP SDK omits `null`-valued properties from the JSON (they don't round-trip as literal `null`). For `BalanceSheetReport` every field here is non-null (sections + `Note` are always populated), so this is low-risk, but the round-trip test must read nested keys with `TryGetProperty`, not assume a key is present-with-null.
- **teas_test fixture** applies each SQL seed ONCE and must stay fixture-managed; `TEAS_TEST_PG` env dies between PowerShell calls (set it in the SAME invocation as `dotnet test`); check skip-count vs baseline — a skipped test fakes green.
- **Seed 400 date footgun:** on fresh `teas_test`, seed 400 closes the prev month relative to CURRENT_DATE. Any test JE must use today/current-month or future `DocDate`, never a hardcoded past month.
- **co2 is load-bearing + polluted** (its P&L ties to doc chapters ch7/8): the co2 probe is **READ-ONLY**. Never post/seed into co2.
- **Thai glyph:** grep `ম` (Bengali) before commit — creeps into Thai strings.

---

## Requirements (checklist)

- [x] **B1. MCP tool `get_balance_sheet`** — `backend/src/Accounting.Api/Mcp/TeasMcpTools.cs`, added in the C1 report-tools region right after `get_trial_balance`. Mirror `get_trial_balance` verbatim (thin wrapper, `IClock` default, existing policy const — NO new scope):
  ```csharp
  [McpServerTool(Name = "get_balance_sheet"), Authorize(Policy = ReportTrialBalance)]
  [Description("Get the balance sheet (งบแสดงฐานะการเงิน): assets, liabilities, equity, and current-period earnings as of a date. Assets always equal liabilities + equity (double-entry). Defaults to today.")]
  public static Task<BalanceSheetReport> GetBalanceSheetAsync(
      IFinancialReportService svc,
      IClock clock,
      [Description("As-of date (yyyy-MM-dd); omit for today.")] DateOnly? asOfDate = null,
      CancellationToken ct = default) =>
      svc.BalanceSheetAsync(asOfDate ?? clock.TodayInBangkok(), ct);
  ```
  Acceptance: `dotnet build` green; the tool appears in the MCP server catalog (smoke test below).
  - Do NOT add a new `McpScopes` entry or touch frontend `ALL_SCOPES`/`MCP_DEFAULT_SCOPES` — `report.trial_balance.read` is already a granted MCP scope (get_trial_balance uses it).

- [x] **B2. MCP round-trip test** — add to `backend/tests/Accounting.Api.Tests/Mcp/McpReadExpansionTests.cs` (the file the C1 report tools are tested in; confirm exact path, else the nearest MCP read-test file). Use `TestCompanyFactory.CreateAsync` for an isolated company. Post one balanced JE (today's date), invoke `get_balance_sheet` via the in-process MCP server harness (same pattern as the existing `get_trial_balance`/`get_profit_loss` MCP tests), assert: result content non-empty; `balanced == true`; `assets`, `liabilities`, `equity` keys present with a `total` each. Acceptance: `dotnet test --filter "FullyQualifiedName~get_balance_sheet"` → passed, 0 skipped.

- [x] **B3. FE types** — `frontend/lib/types.ts`: add `BalanceSheetRow`, `BalanceSheetSection`, `BalanceSheetReport` (camelCase matching the live JSON: `accountCode, accountNameTh, balance`; `rows[], total`; `asOfDate, companyId, assets, liabilities, equity, currentPeriodEarnings, liabilitiesAndEquityTotal, balanced, note`). Follow the existing `TrialBalanceReport` type nearby.

- [x] **B4. FE query hook** — `frontend/lib/queries.ts`: add `useBalanceSheet(asOf: string)` mirroring `useTrialBalance` (~L885): `useQuery({ queryKey:['balance-sheet', asOf], queryFn: () => apiGet<BalanceSheetReport>(\`reports/balance-sheet${qs({ asOfDate: asOf })}\`) })`. **Param name is `asOfDate`** (matches the REST endpoint's `[FromQuery] DateOnly? asOfDate`) — do not guess `asOf`/`as_of`.

- [x] **B5. FE page** — new `frontend/app/(dashboard)/reports/balance-sheet/page.tsx`, `'use client'`, follow `reports/trial-balance/page.tsx`:
  - Single `<input type="date">` as-of filter, default `bangkokToday()` (from `frontend/lib/utils.ts` — do NOT use `new Date().toISOString()`, TZ-shift bug per GL F7).
  - `PageHeader` + `useTranslations('report')`.
  - Three DaisyUI `table table-zebra` sections (Assets / Liabilities / Equity), each: rows (`accountCode` — `accountNameTh` | `balance` right-aligned via `formatTHB`, `text-right tabular-nums`) + a section `total` row. Then a "current-period earnings" line and a Liabilities+Equity total. A "Balanced ✓ / ไม่สมดุล" badge driven by `balanced`.
  - Loading/empty inline `<tr>` rows like trial-balance.

- [x] **B6. FE nav + i18n** — `frontend/components/app-shell/SidebarNav.tsx`: add `{ href: '/reports/balance-sheet', key: 'balanceSheet', Icon: <lucide icon, e.g. Scale/Landmark>, perm: 'report.trial_balance.read' }` in the `reports` section (gate on the SAME perm the endpoint uses). Add `nav.balanceSheet` + `report.*` balance-sheet keys (บาลานซ์/งบแสดงฐานะการเงิน, assets/liabilities/equity/currentPeriodEarnings/total/balanced labels) to BOTH `frontend/messages/th.json` and `frontend/messages/en.json`. Verify both parse as JSON.

## Verification gates

- `dotnet build` (full solution) → 0 errors. (If MSB3027 "locked by testhost", kill the stray `testhost` PID — troubles-wiki.)
- `dotnet test --filter "FullyQualifiedName~get_balance_sheet"` (with `TEAS_TEST_PG` set in the SAME shell) → all passed, 0 skipped.
- **co2 read-only probe:** invoke `get_balance_sheet` (default as-of) against demo company co2 (or a `dotnet test` assertion running under co2's tenant) → returns 200 and `balanced == true`. READ-ONLY — no writes to co2. (Balance is a double-entry invariant, so this is a safe non-flaky assertion.)
- `npx next build` (frontend) → compiled, 0 type errors; `/reports/balance-sheet` present in route manifest.
- `grep -rn "ম" frontend/ backend/ --include=*.ts --include=*.tsx --include=*.cs --include=*.json` (excl. bin/obj/node_modules/.next) → empty.

## Blast-radius cap

Max **8 files** touched: `TeasMcpTools.cs`, one MCP test file, `types.ts`, `queries.ts`, new `balance-sheet/page.tsx`, `SidebarNav.tsx`, `messages/th.json`, `messages/en.json`.
- **Forbidden:** any backend endpoint/DTO/service/migration/SQL-seed change; any `McpScopes`/frontend-scope-list change; any `middleware.ts`/`app/**/route.ts` passthrough; any edit to `BalanceSheetAsync` or the existing DTOs. Hitting any of these = STOP and re-spec (the design asserts none are needed).
- Public-API change: NONE (adds one MCP tool over an existing service + one FE route; no REST contract change).

## Attempt log
<!-- - <date> <worker>: <result / evidence> -->
- 2026-07-08 sonnet-implementer: All checklist items B1-B6 done, all gates green.
  - B1: `GetBalanceSheetAsync` added to TeasMcpTools.cs mirroring `get_trial_balance` verbatim,
    reusing `ReportTrialBalance` policy const — no new scope/const added.
  - B2: added `Mcp_get_balance_sheet_returns_balanced_report_for_posted_je` to
    McpReadExpansionTests.cs — isolated `TestCompanyFactory` company, posts one balanced JE
    (cash 1110 dr / AP 2110 cr, today's date) via `IJournalService`, calls `get_balance_sheet`
    over MCP, asserts `balanced==true` + `assets/liabilities/equity.total` present.
  - Also added a second test `Mcp_get_balance_sheet_co2_readonly_probe_returns_balanced_report`
    (same file, no new file) to satisfy the co2 read-only gate — mints an API key scoped to
    company_id=2 (co2), calls `get_balance_sheet` read-only, asserts 200 + `balanced==true`
    against co2's real accumulated history. No writes to co2.
  - B3/B4: `BalanceSheetRow/Section/Report` types + `useBalanceSheet(asOf)` hook added,
    mirroring `TrialBalanceReport`/`useTrialBalance`. Param name confirmed `asOfDate`.
  - B5: new `frontend/app/(dashboard)/reports/balance-sheet/page.tsx`, `'use client'`,
    `bangkokToday()` default (not `new Date().toISOString()`), three DaisyUI table sections
    (Assets/Liabilities/Equity) + current-period-earnings/L&E-total rows + Balanced badge.
  - B6: nav item added to SidebarNav.tsx reports section (`FileBarChart2` icon — `Scale` was
    already taken by trial-balance; `Landmark` was already used twice in the same section),
    gated on `report.trial_balance.read` (same perm as endpoint). i18n keys added to both
    th.json/en.json under `nav.balanceSheet` + `report.bsTitle/assets/liabilities/equity/
    currentPeriodEarnings/liabilitiesAndEquityTotal`; reused existing `report.balanced/
    unbalanced/asOf/totalRow` and `common.loading/empty` rather than duplicating.
  - Gates: `dotnet build` (full solution) → 0 errors both times (mid-point + final).
    `dotnet test --filter "FullyQualifiedName~get_balance_sheet"` (TEAS_TEST_PG set in same
    invocation) → 2 passed (round-trip + co2 probe), 0 skipped, baseline unaffected (both new
    tests, no pre-existing tests touched). co2 probe folded into the same filtered run (see
    above) — 200 + balanced==true against real co2 history, read-only.
    `npx next build` → "Compiled successfully", 0 type errors, `/reports/balance-sheet`
    present in route manifest (1.9 kB). `grep -rn "ম" frontend/ backend/ ...` → empty (exit 1,
    no matches) — checked twice (before and covering all new files).
  - Files touched: exactly the 8 named in the blast-radius cap, verified via
    `git status --porcelain -- backend frontend`. No forbidden files touched (no endpoint/
    DTO/service/migration/McpScopes/middleware/route.ts changes).

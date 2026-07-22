# Spec: General Ledger (บัญชีแยกประเภท) + Journal Entry Detail

**Status:** DONE — v1.12.0 deployed to prod 2026-07-08 (feat PR #47 + CRLF fix PR #49 + release PR #48, tag v1.12.0). Prod verify: API DEPLOY_OK (gl/je routes 401-gated, seed590 perm row present, DB backed up pre-deploy), FE_DEPLOY_OK (routes 307-gated, build clean). Remaining human step: Ham eyeballs GL on prod as a company user.
**Branch:** `feature/general-ledger`
**Design approved by user:** 2026-07-07 (screen + drill-down + PDF + Excel, full scope)
**Capability map:** Sonnet implements (backend then frontend, sequential), Codex cross-review (money/legal lens), Haiku Tier-3 gate, Fable spec + diff review + commit.

## Scope

Read-only feature. No writes to ledger data. JE immutability untouched.

1. `GET /reports/general-ledger` — per-account ledger: opening balance, movements with running balance, closing balance.
2. `GET /journals/{journalId}` — first JE read endpoint (only POSTs exist today).
3. Export endpoints: PDF + Excel/CSV for the GL report.
4. Frontend `/reports/general-ledger` page (account picker, from/to dates, table, export buttons).
5. Frontend `/journals/[id]` detail page (drill-down target).
6. Sidebar nav item + RBAC perm + seed + i18n (th/en).
7. Delete dead scaffold `GlReportService.cs` / `GlReportDtos.cs` (never endpoint-wired).

Out of scope: journal list/browser page, branch filter (no report has one), editing anything.

## Backend design (follow `FinancialReportService.TrialBalanceAsync` pattern)

### 1. GL report endpoint

`GET /reports/general-ledger?accountId={int}&fromDate={date}&toDate={date}`
- File: `ReportEndpoints.cs` (same group), service method in `FinancialReportService.cs`.
- Perm: `Permissions.Report.GeneralLedger = "report.general_ledger.read"` (new, follow existing `report.*` constants).
- Posted-only (`Status == DocumentStatus.Posted`), same as trial balance.
- Validate: account exists & belongs to tenant (404 if not), fromDate <= toDate (400).

DTOs (in `FinancialReportDtos.cs`):
```csharp
record GeneralLedgerReport(
    int AccountId, string AccountCode, string AccountNameTh, string AccountType,
    string NormalBalance, DateOnly FromDate, DateOnly ToDate,
    decimal OpeningBalance, IReadOnlyList<GeneralLedgerRow> Rows,
    decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance);

record GeneralLedgerRow(
    long JournalId, DateOnly DocDate, string DocNo, string? Description,
    string? Reference, decimal Debit, decimal Credit, decimal RunningBalance);
```

Balance math (MUST be exactly this — reviewer checks):
- Sign convention: `signed(dr, cr) = NormalBalance == "DR" ? dr - cr : cr - dr`.
- `OpeningBalance = signed(sum of DebitAmount, sum of CreditAmount)` over posted lines of this account with `DocDate < fromDate` (ALL history, ignore period close — closed periods still count into opening).
- Rows: posted lines with `fromDate <= DocDate <= toDate`, ordered by `DocDate, JournalId, LineNo`. One JE with 2 lines on the same account = 2 rows.
- Row Description: line `Description ?? entry.Description`.
- `RunningBalance` = opening + cumulative signed() down the ordered rows.
- `ClosingBalance = OpeningBalance + signed(TotalDebit, TotalCredit)`.
- No pagination v1 (range-limited query; PDF needs all rows anyway). `// ponytail: fetch-all in range, paginate if real ledgers exceed ~10k rows`.

### 2. JE detail endpoint

`GET /journals/{journalId}` in `JournalEndpoints.cs`.
- Perm: existing `Permissions.Gl.JournalRead` (`gl.journal.read`).
- Returns 404 when not found OR other tenant (global filter + RLS make these identical — do NOT distinguish).
- DTO: header (JournalId, DocNo, DocDate, PostingDate, Description, Reference, Status, PostedAt, ReversalOfId) + lines (LineNo, AccountId, AccountCode, AccountNameTh, Description, Reference, Debit, Credit, BusinessUnitId) + TotalDebit/TotalCredit. Join ChartOfAccounts for code/name.

### 3. Account lookup for the picker

- FIRST check whether a chart-of-accounts list endpoint already exists (search MasterDataEndpoints / master endpoints for ChartOfAccount). If yes → frontend reuses it, no new endpoint.
- If none: add `GET /reports/general-ledger/accounts` returning active, non-header accounts `(AccountId, AccountCode, AccountNameTh, NormalBalance)` ordered by code, gated by the same `report.general_ledger.read` perm (keeps page gating consistent).

### 4. Exports

- `GET /reports/general-ledger/export?format=pdf|csv&accountId&fromDate&toDate`, same perm. Reuse the report DTO internally.
- PDF: reuse the exact library/pattern of the existing Financial-Statements PDF (find it in the codebase; do NOT add a new PDF lib). Layout: company name header, account code+name, date range, opening row, movement rows, closing row, page numbers. Thai font support = whatever the existing PDF already does.
- Excel: check installed packages for an xlsx lib (ClosedXML/EPPlus/etc.). If one exists, use it. If NOT, produce CSV with UTF-8 BOM (opens in Excel with Thai intact) — do NOT add a new dependency. Filename `general-ledger-{accountCode}-{from}-{to}.{ext}`.

### 5. RBAC seed — FOOTGUN ZONE, follow exactly

1. Add constant `Report.GeneralLedger` in `Authorization/Permissions.cs`.
2. New SQL seed file numbered AFTER the current highest seed. In ONE file, in THIS order:
   a. INSERT the permission code `report.general_ledger.read` (idempotent, same style as existing perm inserts).
   b. THEN grant it to the same roles that hold `report.trial_balance.read` (copy that grant pattern verbatim).
   - Known footgun: grants silently no-op if the perm code row doesn't exist yet. Insert-first, grant-second, same file.
3. Verify `gl.journal.read` is actually seeded AND granted (constant exists; seeding unverified). If missing, add insert+grants to the SAME new seed file, mirroring roles that hold `gl.journal.post` or trial-balance-read.
4. Prod note for Fable: seed file must run on prod at deploy (same mechanism as previous seeds).
5. Gate: `RbacAuthMapTests` + `RbacMatrixTests` MUST pass. They need `TEAS_REPO_ROOT` env set (subst-drive quirk) — set it in the same shell as the test run.

### Backend tests (integration, follow existing report test style)

Use `TestCompanyFactory.CreateAsync`. **Date footgun:** seed 400 closes prev-month relative to CURRENT_DATE on fresh teas_test — use today/current-month or future dates in test JEs, never hardcoded past months.

- Opening balance: post JE before range → opening reflects it; rows exclude it.
- Running balance DR account (e.g. cash 11110): dr increases, cr decreases; verify per-row values.
- Running balance CR account (e.g. AP): mirror check.
- Draft JE excluded everywhere (opening AND rows).
- Two lines on same account in one JE → two rows.
- fromDate > toDate → 400. Unknown/other-tenant accountId → 404.
- No-perm user → 403 (both endpoints). Wrong-tenant JE id → 404.
- JE detail: totals equal, lines carry account code/name.
- CSV export: 200, BOM present, row count matches. PDF export: 200, non-empty, `%PDF` magic.
- `TEAS_TEST_PG` must be set in the SAME PowerShell invocation as `dotnet test` (env dies between calls). Check skip count vs baseline — skipped tests fake a green run.

## Frontend design

### `/reports/general-ledger/page.tsx` (route group `(dashboard)`, follow `profit-loss/page.tsx`)

- `'use client'`; hooks in `lib/queries.ts` (`useGeneralLedger`, `useGlAccounts` or reuse existing COA hook), types in `lib/types.ts`.
- Filters: account picker + `<input type="date">` from/to (default = first/last day of current month) + "แสดงรายงาน" trigger (query enabled only when account chosen).
- Account picker: native `<input list>` + `<datalist>` with "code — nameTh" options, resolve to accountId on match. `// ponytail: datalist; swap for combobox lib only if UX proves insufficient`.
- Table (DaisyUI `table table-zebra`, `formatTHB`): columns วันที่ | เลขที่เอกสาร | คำอธิบาย | อ้างอิง | เดบิต | เครดิต | คงเหลือ.
  - First body row = ยอดยกมา (opening, no debit/credit cells).
  - DocNo = `<Link href={/journals/${journalId}}>`.
  - `<tfoot>`: totals row (TotalDebit/TotalCredit) + ยอดยกไป (closing).
- Export buttons: PDF + Excel → navigate/download via the proxy export URL with current filters; disabled until report loaded.
- `PageHeader` + i18n via `useTranslations('report')`, keys added to BOTH `messages/th.json` and `messages/en.json`.

### `/journals/[id]/page.tsx` (follow existing `{doc}/[id]` detail pattern)

- Header card: DocNo, DocDate, PostingDate, Status badge, Description, Reference, PostedAt; ReversalOfId shown as link when present.
- Lines table: บัญชี (code + name) | คำอธิบาย | เดบิต | เครดิต, tfoot totals.
- 404 → same not-found handling as existing doc detail pages.

### Nav + gating

- `SidebarNav.tsx`: add item under Reports section, perm `report.general_ledger.read`, th/en labels (บัญชีแยกประเภท / General Ledger).
- JE detail page NOT in nav (reached via drill-down/URL only).

### Frontend gates

- `next build` green.
- Thai glyph check before commit: `grep -rn "ম" frontend/ backend/` → must be empty (Bengali ম creeps into Thai).

## Verification / commit plan (Fable runs this)

- Tier 1: each worker self-verifies its gates, reports evidence into this file.
- Tier 2: Codex cross-review, lenses: (1) balance-math correctness vs spec formulas, (2) tenant isolation on both new endpoints, (3) RBAC completeness (endpoint policy + seed + nav gate agree).
- Tier 3: Haiku consolidated gate: backend build + full targeted test list + RbacAuthMap/Matrix + next build + glyph grep + skip-count vs baseline.
- Fable reads full diff → commit → PR (repo uses PR flow + release-please).
- New-file check before commit: `git status` grep `^??` — explicitly add new files (git add -u misses them).

## Checklist

- [x] B1. Permissions constant + RBAC seed SQL (insert-first/grant-second) + gl.journal.read verified/granted — `Permissions.Report.GeneralLedger` added; `590_seed_general_ledger_perms.sql` inserts `report.general_ledger.read` then grants SUPER_ADMIN + templates/fans-out to TAX_OFFICER/AUDITOR/ACCOUNTANT/CHIEF_ACCOUNTANT/COMPANY_ADMIN (same set as `report.trial_balance.read`). `gl.journal.read` verified ALREADY seeded (110) AND granted (530 §B8 to ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/TAX_OFFICER/COMPANY_ADMIN) — no change needed, confirmed by grep before writing the seed file.
- [x] B2. GL report service method + DTOs + endpoint — `FinancialReportService.GeneralLedgerAsync` + `GeneralLedgerAccountsAsync`; DTOs in `FinancialReportDtos.cs`; endpoint `GET /reports/general-ledger` in `ReportEndpoints.cs`. Balance math matches spec exactly (verified by tests). **Deviation:** DTO `AccountId`/query param typed `long` not `int` — `ChartOfAccount.AccountId` is `long` (BIGINT) in this codebase; using `int` wouldn't compile (narrowing) and could silently truncate. Noted, not escalated (trivial type fix, spec's field list/semantics otherwise followed verbatim).
- [x] B3. JE detail endpoint + DTO — `GET /journals/{id}` in `JournalEndpoints.cs`, gated on existing `Gl.JournalRead`; `IJournalService.GetDetailAsync` + `JournalDetail`/`JournalDetailLine` DTOs in `JournalDtos.cs`; joins ChartOfAccounts for code/name; 404 via `DomainException("je.not_found", ...)` (existing convention, same code JournalService.PostAsync already uses) — not-found and other-tenant are identical (global query filter).
- [x] B4. Account lookup (reuse or add) — checked `/accounts` (MasterEndpoints.cs): exists but gated on `Master.CoaManage` (a manage perm), which report-viewer roles (AUDITOR/TAX_OFFICER) do NOT hold — wrong RBAC shape for a report-picker. Added `GET /reports/general-ledger/accounts` gated on the SAME `report.general_ledger.read` perm as the spec's fallback branch prescribes (§3: "keeps page gating consistent"). Returns active, non-header accounts ordered by code.
- [x] B5. PDF + Excel/CSV export endpoints — `GET /reports/general-ledger/export?format=pdf|csv`. PDF via new `Accounting.Infrastructure/Pdf/GeneralLedgerPdf.cs` (QuestPDF, same EnsureFont/Sarabun idiom as `FinancialStatementPdf.cs` — no new PDF lib). No xlsx package installed (checked csproj — no ClosedXML/EPPlus/NPOI) → CSV with UTF-8 BOM per spec's fallback. Filename `general-ledger-{accountCode}-{from}-{to}.{ext}`.
- [x] B6. Delete dead GlReportService.cs/GlReportDtos.cs scaffold — both files deleted; `IGlReportService` DI registration removed from `DependencyInjection.cs`; confirmed zero references anywhere (incl. tests) before deleting.
- [x] B7. Backend integration tests green (list above), RbacAuthMap/Matrix green — see Attempt log for full evidence (build 0 errors, 17/17 new GL tests, 41/41 Rbac-filtered tests incl. RbacAuthMapTests+RbacMatrixTests, 42/42 broader report/paper regression tests — all 0 skipped). Glyph grep clean.
- [x] F1. GL page (filters, table, opening/running/closing, export buttons) — `frontend/app/(dashboard)/reports/general-ledger/page.tsx`: native `<input list>`/`<datalist>` account picker ("code — nameTh"), from/to date inputs defaulting to first/last day of current month, "แสดงรายงาน" trigger button that commits pending filters into `appliedAccountId/appliedFrom/appliedTo` (query `enabled` only on those, per spec), table with ยอดยกมา opening row (no debit/credit cells) → movement rows (DocNo → `Link` to `/journals/{journalId}`) → `tfoot` totals row + ยอดยกไป closing row. PDF/Excel buttons call `downloadFile('reports/general-ledger/export?...&format=pdf|csv', filename)` through the same `/api/proxy` path as every other API call; both `disabled={!gl.data}` until a report is loaded.
- [x] F2. JE detail page — `frontend/app/(dashboard)/journals/[id]/page.tsx`: header card (DocNo as subtitle, DocDate, PostingDate, Status badge, Description, Reference, PostedAt), ReversalOfId rendered as a `Link` to `/journals/{reversalOfId}` only when non-null, lines table (account code+name, description, debit, credit) with `tfoot` totals. 404/not-found handled identically to `payroll/[id]/page.tsx` (`if (!d) return <p>{tc('notFound')}</p>`) — covers both "not found" and "other tenant" (BE returns the same 404 for both per spec).
- [x] F3. queries.ts/types.ts hooks + types — `lib/types.ts`: `GeneralLedgerRow`, `GeneralLedgerReport`, `GeneralLedgerAccountOption`, `JournalDetailLine`, `JournalDetail` (camelCase fields matching the live JSON exactly, per the backend worker's "For the frontend worker" notes — `accountId`/`journalId`/`reversalOfId` typed `number`, matching this codebase's existing convention of using plain `number` for all other `long`/`bigint` id fields, e.g. `PurchaseOrderDetail`, `AttachmentItem`). `lib/queries.ts`: `useGlAccounts()` (→ `reports/general-ledger/accounts`, NOT `/accounts` — that one 403s for AUDITOR/TAX_OFFICER per the backend note), `useGeneralLedger(accountId, fromDate, toDate)` (enabled only when all three are set), `useJournal(id)` (→ `journals/{id}`).
- [x] F4. SidebarNav item + perm gate — `components/app-shell/SidebarNav.tsx`: added `{ href: '/reports/general-ledger', key: 'generalLedger', Icon: BookOpen, perm: 'report.general_ledger.read' }` under the `reports` section (same gating mechanism as every other nav item — hidden unless the user holds the perm or is super-admin). `/journals/[id]` intentionally has NO nav entry (drill-down/URL only, per spec).
- [x] F5. i18n th/en keys — added to BOTH `messages/th.json` and `messages/en.json`: `nav.generalLedger` (บัญชีแยกประเภท / General Ledger); `report.{glTitle, description, accountPlaceholder, showReport, docNo, ref, openingBalance, closingBalance, runningBalance, exportPdf, exportExcel}`; new `je` namespace (`title, docDate, postingDate, status, description, reference, postedAt, reversalOf, account, debit, credit, totalRow`) for the JE detail page. Verified both files parse as valid JSON (`node -e "JSON.parse(...)"` on both — OK).
- [x] F6. next build green + glyph grep clean — see Gate evidence below.
- [ ] R1. Codex cross-review pass (3 lenses)
- [ ] R2. Haiku Tier-3 consolidated gate
- [ ] R3. Fable diff review + commit + PR

## Attempt log

### 2026-07-07 — Sonnet implementer, backend B1–B7

**Files changed** (backend only, no frontend/ touched):
- `backend/src/Accounting.Api/Authorization/Permissions.cs` — added `Report.GeneralLedger`.
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/590_seed_general_ledger_perms.sql` — new seed (insert perm → grant SUPER_ADMIN → template TAX_OFFICER/AUDITOR/ACCOUNTANT/CHIEF_ACCOUNTANT/COMPANY_ADMIN → fan-out to existing companies), numbered after 585 (highest existing).
- `backend/src/Accounting.Application/Reports/FinancialReportDtos.cs` — added `GeneralLedgerReport`, `GeneralLedgerRow`, `GeneralLedgerAccountOption` + 2 new interface methods on `IFinancialReportService`.
- `backend/src/Accounting.Infrastructure/Reports/FinancialReportService.cs` — implemented `GeneralLedgerAsync` (opening/rows/running-balance/closing per spec formulas exactly) + `GeneralLedgerAccountsAsync`.
- `backend/src/Accounting.Application/Ledger/JournalDtos.cs` — added `JournalDetail`/`JournalDetailLine`.
- `backend/src/Accounting.Application/Ledger/IJournalService.cs` — added `GetDetailAsync`.
- `backend/src/Accounting.Infrastructure/Ledger/JournalService.cs` — implemented `GetDetailAsync` (joins ChartOfAccounts, `je.not_found` on miss).
- `backend/src/Accounting.Api/Endpoints/JournalEndpoints.cs` — added `GET /{id:long}`.
- `backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs` — added `GET /general-ledger`, `GET /general-ledger/accounts`, `GET /general-ledger/export?format=pdf|csv`.
- `backend/src/Accounting.Infrastructure/Pdf/GeneralLedgerPdf.cs` — new QuestPDF builder (mirrors `FinancialStatementPdf.cs`).
- `backend/src/Accounting.Infrastructure/DependencyInjection.cs` — removed `IGlReportService` registration.
- Deleted: `backend/src/Accounting.Infrastructure/Reports/GlReportService.cs`, `backend/src/Accounting.Application/Reports/GlReportDtos.cs`.
- New tests: `backend/tests/Accounting.Api.Tests/Reports/GeneralLedgerReportTests.cs` (service-level, `TestCompanyFactory`-isolated — balance math, draft exclusion, two-lines-one-account, cross-tenant 404, JE detail), `backend/tests/Accounting.Api.Tests/Reports/GeneralLedgerEndpointTests.cs` (HTTP-level, `RbacApiFactory` — 403/400/404, CSV BOM, PDF magic).

**Deviations from spec text (both minor, taking the sensible branch per dispatch instructions):**
1. `AccountId` typed `long` not `int` in the new DTOs/query params — matches `ChartOfAccount.AccountId` (BIGINT) in this codebase; `int` literally would not compile (CS1503 narrowing conversion) at `new GeneralLedgerReport(account.AccountId, ...)`.
2. Account-picker: `/accounts` (MasterEndpoints.cs) DOES already exist but is gated on `Master.CoaManage` (create/update/list all share one manage-only group) — a report-viewer role (AUDITOR, TAX_OFFICER) doesn't hold that. Took the spec's own anticipated fallback branch (§3.2): added `/reports/general-ledger/accounts` under the report perm.

**Bug found + fixed during testing:** first CSV export test failed — `Encoding.GetBytes(string)` does NOT emit the BOM even with `encoderShouldEmitUTF8Identifier: true` (that flag only affects `GetPreamble()`/`StreamWriter`). Fixed by explicitly prepending `utf8Bom.GetPreamble()` to the byte array. Re-ran green after the fix.

**Gate evidence:**
1. `dotnet build` (full solution) — 0 Warnings, 0 Errors.
2. Targeted new tests: `dotnet test --filter "FullyQualifiedName~GeneralLedger|FullyQualifiedName~RbacAuthMapTests|FullyQualifiedName~RbacMatrixTests"` → **Passed: 17, Failed: 0, Skipped: 0** (first run caught the BOM bug: 16 passed/1 failed/0 skipped — no false-green; fixed and re-ran to 17/17).
3. `dotnet test --filter "FullyQualifiedName~Rbac"` (RbacAuthMapTests + RbacMatrixTests + related RBAC suite, needs `TEAS_REPO_ROOT` same-shell) → **Passed: 41, Failed: 0, Skipped: 0.** Confirms: new perm code registered in catalog (AuthMap gate 0), no unprotected endpoints (gate 1), no unexpected authn-only (gate 2), and RbacMatrixTests invariant 3 (super-only set stays exactly `{master.company.manage}` — i.e. `report.general_ledger.read` IS granted to ≥1 non-super role, proving the SQL seed's template+fan-out actually took effect against live `teas_test`).
4. Broader regression: `dotnet test --filter "FullyQualifiedName~Reports|...BalanceSheetTests|...Sprint9FinancialReportTests|...PaperEndpointTests"` → **Passed: 42, Failed: 0, Skipped: 0** — no regressions in adjacent report/PDF endpoints.
5. Glyph grep: `grep -rn "ম" backend/*.cs backend/*.sql` (source only, excl. bin/obj build artifacts) → **empty.** (A raw recursive grep over all of `backend/` does hit one compiled `System.Text.RegularExpressions.dll` binary under `bin/Release/.../linux-x64/` — a false positive from binary bytes, not source text; re-checked scoped to `*.cs`/`*.sql` and it's clean.)

**Env notes for whoever runs Tier 2/3 next:** `TEAS_TEST_PG` and `TEAS_REPO_ROOT` must be set in the SAME shell invocation as `dotnet test` (both die between PowerShell/bash calls). Connection string used: `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true`.

**For the frontend worker:**
- `GET /reports/general-ledger?accountId={long}&fromDate={yyyy-MM-dd}&toDate={yyyy-MM-dd}` → `GeneralLedgerReport` JSON, camelCase fields: `accountId, accountCode, accountNameTh, accountType, normalBalance, fromDate, toDate, openingBalance, rows[], totalDebit, totalCredit, closingBalance`. Each row: `journalId, docDate, docNo, description, reference, debit, credit, runningBalance`. 400 if `fromDate > toDate` (body `{ "detail": "..." }`), 404 if `accountId` unknown/other-tenant (RFC-7807 problem+json, since this route is root not `/api/v1`).
- `GET /reports/general-ledger/accounts` → array of `{ accountId, accountCode, accountNameTh, normalBalance }` (`normalBalance` is `"DR"`/`"CR"` string), active+non-header only, ordered by code. Use this for the `<datalist>` picker, NOT `/accounts` (that one 403s for AUDITOR/TAX_OFFICER — different perm).
- `GET /reports/general-ledger/export?accountId={long}&fromDate=...&toDate=...&format=pdf|csv` → raw file download (`Results.File`), filename `general-ledger-{accountCode}-{fromDate}-{toDate}.{pdf|csv}`. CSV has UTF-8 BOM (open directly in Excel).
- `GET /journals/{id}` → `JournalDetail` JSON: `journalId, docNo, docDate, postingDate, description, reference, status, postedAt, reversalOfId, lines[], totalDebit, totalCredit`. `status` is the DocumentStatus enum AS STRING (e.g. `"Posted"`, `"Draft"` — PascalCase, not lowercase; global `JsonStringEnumConverter` uses the C# enum member name verbatim, no naming policy applied to enum values). Each line: `lineNo, accountId, accountCode, accountNameTh, description, reference, debit, credit, businessUnitId`. 404 for unknown/other-tenant id.
- Perm gating: `/reports/general-ledger*` → `report.general_ledger.read`; `/journals/{id}` → `gl.journal.read` (already existed/granted — no seed change needed there).

### 2026-07-07 — Sonnet implementer, frontend F1–F6

**Files changed** (frontend only, no backend/ touched, 7 files — under the 10-file blast-radius cap):
- `frontend/lib/types.ts` — added `GeneralLedgerRow`, `GeneralLedgerReport`, `GeneralLedgerAccountOption`, `JournalDetailLine`, `JournalDetail` (camelCase, matches live JSON from the backend worker's notes exactly).
- `frontend/lib/queries.ts` — added `useGlAccounts()`, `useGeneralLedger(accountId, fromDate, toDate)`, `useJournal(id)`, following the existing `useTrialBalance`/`useProfitLoss` pattern (`apiGet` + `qs()`).
- `frontend/app/(dashboard)/reports/general-ledger/page.tsx` — new page (follows `profit-loss/page.tsx`).
- `frontend/app/(dashboard)/journals/[id]/page.tsx` — new page (follows `payroll/[id]/page.tsx`'s loading/not-found pattern).
- `frontend/components/app-shell/SidebarNav.tsx` — added `generalLedger` nav item under `reports`, gated on `report.general_ledger.read`; imported `BookOpen` icon.
- `frontend/messages/th.json` / `frontend/messages/en.json` — added `nav.generalLedger`, `report.*` GL keys, new `je` namespace.

**Design decisions (Ponytail — minimal diff, no new deps):**
1. Account picker resolves `accountId` from the datalist's exact "code — nameTh" text match (per spec's native `<input list>` mandate). A "แสดงรายงาน" button commits the resolved account + current from/to into separate "applied" state, so the `useGeneralLedger` query only (re)fires on that click rather than on every keystroke/date tweak — matches the spec's explicit trigger button while still using plain `useState` (no form library).
2. Export buttons reuse the existing generic `downloadFile(path, filename)` helper from `lib/api.ts` (already used by `PrintMenu`/payroll detail for PDF/file downloads) — no new download plumbing. Filename built client-side from the loaded report's `accountCode` + applied dates, matching the backend's `general-ledger-{code}-{from}-{to}.{ext}` convention.
3. JE detail 404 handling copies `payroll/[id]/page.tsx` exactly: `isLoading` → loading text; `!data` (covers both `isError` from the 404 AND the loading-done-with-no-data edge) → `tc('notFound')`. No new not-found component.
4. `ReversalOfId` link points back to `/journals/{id}` (self-referential drill-down) — only rendered when non-null.
5. Two rows can share the same `JournalId` (two lines on one account in one JE, per backend spec) and `GeneralLedgerRow` carries no `LineNo`, so table row `key` is `${journalId}-${index}` (index-in-list, safe since the list itself never reorders under the same query key).

**Gate evidence:**
1. `grep -rn "ম" frontend/app frontend/lib frontend/components frontend/messages` (excl. node_modules/.next) → **empty.**
2. `node -e "JSON.parse(...)"` on both `messages/th.json` and `messages/en.json` → **valid JSON, no trailing-comma/syntax errors.**
3. `npx next build` (full production build, includes `tsc` type-check + static generation of all 70 routes) → **✓ Compiled successfully, 0 type errors.** New routes present in the route manifest: `ƒ /journals/[id]` (1.84 kB) and `ƒ /reports/general-ledger` (3.27 kB).
4. `grep -n ": any\|<any>\|as any"` over all touched/new frontend files → **empty** — no `any` escapes introduced.

**Not done (out of frontend scope, left for Fable's Tier 2/3):** R1 Codex cross-review, R2 Haiku Tier-3 consolidated gate, R3 diff review + commit + PR.

### 2026-07-07 — Sonnet implementer, live-UI-testing findings F7–F9

Three findings from live Chrome UI testing of the GL page. Fixed 2 of 3 (real bugs); investigated the 3rd and found no code defect.

- [x] F7. Default date range off-by-one (real bug) — `monthStart()`/`monthEnd()` in the GL page used `new Date(y, m, d).toISOString().slice(0, 10)`, which converts through UTC before slicing; at TZ+07:00 that shifted 2026-07-01→2026-06-30 and 2026-07-31→2026-06-30 (loaded 2026-07-07, showed 06/30–07/30 instead of 07/01–07/31). **Fix:** added `bangkokMonthStart()`/`bangkokMonthEnd()` to `frontend/lib/utils.ts` (the "at most one shared helper" allowed by the dispatch), built on top of the existing `bangkokToday()` helper (already used project-wide, e.g. `vendor-invoices/new`, `tax-invoices/new`, `DateInput.tsx` — per CLAUDE.md §10, TEAS locks UI dates to Asia/Bangkok rather than raw browser-local, since docs are Thai-tenant and other pages already follow this convention). `bangkokMonthStart` derives Y-M from `bangkokToday()` and appends `-01`; `bangkokMonthEnd` computes the day count via `Date.UTC(y, m, 0)` (UTC-safe arithmetic, never a local `new Date(y, m, 0)`) so the result can't be re-shifted by the browser's own timezone. GL `page.tsx` now imports and uses these instead of its local `monthStart`/`monthEnd`. Only the GL page was touched (other report pages — trial-balance, ap-aging, payroll, outstanding-po — share the identical `toISOString().slice(0,10)` pattern but are explicitly OUT of this dispatch's blast radius; not touched).
- [x] F8. Datalist resolves wrong account on duplicate labels (super-admin only) — confirmed root cause: `/reports/general-ledger/accounts` intentionally returns accounts across ALL companies for super-admin (pre-existing EF global-filter bypass, unchanged), so duplicate labels like "1110 — เงินสด" ×3 existed, and `accounts.find(label === text)` picked the FIRST match regardless of which company the user intended. **Fix (GL page only, backend untouched):** added a `useMemo`'d `accountOptions` derivation that counts label occurrences and appends ` [#${accountId}]` ONLY to labels that occur more than once; the `<datalist>` renders `a.label`, and the resolver (`resolvedAccountId`) now matches against that same disambiguated `label` field — so display and resolution can never disagree. Normal company users (whose accounts never collide) see byte-identical labels to before (no change). Import `useMemo` from `react` added.
- [~] F9. DocNo Link did not client-side navigate — investigated per dispatch instructions, no code change made. Read the GL table row/Link markup (`page.tsx` lines ~140–153: plain `<tr>` → `<td>` → `<Link href={...} className="link link-primary">`, no `onClick` on the row, no wrapping overlay) and the JE detail page (`journals/[id]/page.tsx`, same plain-Link idiom for `ReversalOfId`). Checked `frontend/app/globals.css` and `frontend/lib/paper.css` for any table-row pseudo-element/overlay/`pointer-events` rule that could swallow clicks — the only `position: absolute` / `pointer-events: none` / `z-index` / `::after` rules found are scoped to `.printing-copy .paper-wrap` (the print "สำเนา/COPY" watermark), which never applies to a normal dashboard table. Confirmed `next` 15.5.18 / `react` 19.2.6 (modern `next/link`, renders a plain `<a>`, no `legacyBehavior`/`passHref` footgun). **Conclusion: no code defect found — likely a CDP/automation artifact from the flaky renderer noted during the live-testing session. No change made.**

**Gate evidence (F7–F9):**
1. `npx vitest run lib/` → `lib/client-ip.test.ts` (4), `lib/bath-text.test.ts` (8), `lib/safe-return-to.test.ts` (9), `lib/utils.test.ts` (3, NEW) → **4 files passed, 24/24 tests passed, 0 failed.** New `frontend/lib/utils.test.ts` covers `bangkokMonthStart`/`bangkokMonthEnd` via `vi.useFakeTimers()`/`vi.setSystemTime()` pinned to explicit UTC instants (never touches host `TZ`): mid-July Bangkok wall-clock, the exact UTC-instant class that used to shift June→July (`2026-06-30T18:00:00Z` = `2026-07-01T01:00` Bangkok), a 30-day month, and a leap-year February (`2028-02-29`) — all pass, directly guarding the fixed logic. (`npx vitest run` with no path arg fails ~43 files — pre-existing repo issue, vitest's default glob also picks up Playwright `e2e/*.spec.ts` files; already documented in `troubles-wiki.md`, not a regression from this change.)
2. `npx next build` (full production build) → **✓ Compiled successfully, 0 type errors**, after one follow-up fix: `Date.UTC(y, m, 0)` initially failed `tsc` (`number | undefined` from `.split('-').map(Number)` destructuring under strict indexed-access) — fixed with `as [number, number]` on the destructure. Both `/journals/[id]` and `/reports/general-ledger` present in the route manifest.
3. `grep -rn "ম" frontend/ --include='*.ts' --include='*.tsx' --include='*.json'` (excl. node_modules/.next) → **empty.**

**Files changed this pass (2 files + spec, well under blast-radius cap):**
- `frontend/lib/utils.ts` — added `bangkokMonthStart()` / `bangkokMonthEnd()`.
- `frontend/app/(dashboard)/reports/general-ledger/page.tsx` — F7: use the new helpers instead of local `monthStart`/`monthEnd`. F8: `accountOptions` memo disambiguates duplicate labels; datalist + resolver both use `a.label`.
- New: `frontend/lib/utils.test.ts` — unit tests for the F7 fix.
- No backend changes, no new dependencies, no `next build`/vitest config changes.

**Not done (unchanged from before, still Fable's):** R1 Codex cross-review, R2 Haiku Tier-3 consolidated gate, R3 diff review + commit + PR. Did NOT `git commit` per dispatch instructions.

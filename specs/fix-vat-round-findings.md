# Spec: VAT-round findings fix (F-3, F-6, F-4/F-7/F-8/F-9/F-10)

Ham approved 2026-07-18: "แก้เลยเอาตามที่แนะนำเลย". Source: `REPORT-vat-dummy-test.md`.
Designs below are DECIDED — implement as written; deviations require stop-and-report.
Repo: Y:\ClaudePlayground\TEAS-Project. Backend .NET 10 (`backend/`), FE Next.js (`frontend/`).
All work in ONE branch/working tree, NO commits (Fable commits after review).

## F-3 (HIGH) — PIT onboarding-year under-withholding → opening YTD (ยอดยกมา)

Problem: `PayrollRunService.CreateDraftAsync` (backend/src/Accounting.Infrastructure/Payroll/
PayrollRunService.cs) projects annual income as `YTD-in-system + salary × monthsRemaining`
(`monthsRemaining = 13 − month`). A company onboarding TEAS mid-year with staff employed
since before Jan has Jan→(first-run−1) income paid OUTSIDE the system → projection too low
(EMP001 80k/mo: 480,000 instead of 960,000 → withholds 1,408.33 vs correct 6,075/mo).
Sub-issue (b): `ssoAllowance = min(ssoEmp * 12m, MaxAllowanceForPit)` (line ~92) deducts
full-year SSO even when the projection covers fewer months.

### Design (decided)
1. **Schema** — new SqlScript `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/`
   `610_employee_ytd_opening.sql` (next free prefix after 600; MUST sort last; idempotent
   `ADD COLUMN IF NOT EXISTS`; DDL-only):
   ```sql
   ALTER TABLE master.employees ADD COLUMN IF NOT EXISTS ytd_opening_year INT NULL;
   ALTER TABLE master.employees ADD COLUMN IF NOT EXISTS ytd_opening_income NUMERIC(18,2) NOT NULL DEFAULT 0;
   ALTER TABLE master.employees ADD COLUMN IF NOT EXISTS ytd_opening_pit NUMERIC(18,2) NOT NULL DEFAULT 0;
   ALTER TABLE master.employees ADD COLUMN IF NOT EXISTS ytd_opening_sso NUMERIC(18,2) NOT NULL DEFAULT 0;
   ```
   (employees already carries RLS company_isolation — columns inherit, no policy change.)
2. **Entity/DTO**: add the 4 fields to the Employee entity + EmployeeDtos (create/update/
   list-detail) + validators (year 2000..2100 nullable; amounts >= 0). Follow existing
   field patterns in EmployeeService/EmployeeDtos.
3. **Engine** (`PayrollRunService.CreateDraftAsync`): for each employee, when
   `e.YtdOpeningYear == year` (the run's calendar year):
   - `priorIncome += e.YtdOpeningIncome; priorPit += e.YtdOpeningPit;`
   - SSO allowance becomes `Math.Min(openingSso + ssoEmp * monthsRemaining, _sso.MaxAllowanceForPit)`
     where `openingSso = (e.YtdOpeningYear == year ? e.YtdOpeningSso : 0m)`.
     This REPLACES `ssoEmp * 12m` for everyone: January run → ssoEmp×12 (unchanged
     behavior); mid-year first run without opening → ssoEmp×monthsRemaining (fixes (b));
     with opening → opening + remaining (correct).
   - Everything else (ProjectAnnualIncome(priorIncome, thisMonthTaxable, monthsRemaining),
     MonthlyWithholding(…, priorPit, …)) unchanged — the opening flows through naturally.
4. **FE** employees modal (frontend, settings/employees page component): add a collapsed
   section "ยอดยกมาปีนี้ (กรณีเริ่มใช้ระบบระหว่างปี)" with 4 inputs: ปี (default current
   CE year), เงินได้สะสม, ภาษีหัก ณ ที่จ่ายสะสม (ภ.ง.ด.1), ปกส.สะสม. Send only when ปี
   filled. Show hint: ใช้เมื่อบริษัทเริ่มใช้ TEAS ระหว่างปีและพนักงานมีเงินได้/ภาษีจากงวด
   ก่อนหน้านอกระบบ.
5. **Tests** (backend/tests/Accounting.Api.Tests/Payroll/ — follow existing payroll test
   style): (a) employee salary 80,000, hire last year, opening {year=current, income=480,000,
   pit=8,449.98? NO — use opening pit 0 to keep assertion exact: projection = 480,000 +
   80,000×6, taxable = 960,000−100,000−60,000−min(0+875×6, cap)=… assert PIT equals the
   hand formula in-test (compute expected via ThaiPitCalculator in the assertion, not a
   magic number)}; (b) no opening → unchanged current behavior at January (12-month spread);
   (c) mid-year first run without opening now uses ssoEmp×monthsRemaining (assert vs
   ThaiPitCalculator-computed expected). Run any existing payroll tests — update those that
   asserted the old `×12` SSO allowance for mid-year runs (check PayrollRunServiceTests).

## F-6 — Pay posts a settlement JE

Problem: MarkPaid ("จ่ายแล้ว") is status-only; 2170 เงินเดือนค้างจ่าย never clears, bank
never moves (verified on prod: TB identical before/after Pay).

### Design (decided)
1. API: MarkPaid endpoint/request gains OPTIONAL `bankAccountId` (long?). Service
   (PayrollRunService MarkPaid method): on Pay, post a JE via the existing IGlPostingService
   pattern (same as the Post JE code in this service): date = PayDate,
   `Dr 2170 (TotalNet)` / `Cr <bank account's GL account>` (from the BankAccount row;
   default GL = 1120). If `bankAccountId` null → use the company's single ACTIVE bank
   account when exactly one exists; if none exists → `Cr 1110 เงินสด`. If multiple and
   none chosen → 422 `payroll.bank_required` with Thai-ready message.
   Guard idempotent: refuse double-pay (already-Paid check exists — keep).
2. FE: Pay confirm dialog adds a bank-account dropdown (options from /bank-accounts,
   preselect first active; show "เงินสด (1110)" option when none). Send bankAccountId.
3. Tests: pay → JE exists Dr 2170/Cr 1120 amount TotalNet; TB-level assertion via
   journal entries; double-pay still blocked; no-bank-account company → Cr 1110.

## F-9 — COGS account + remap

1. SqlScript `611_seed_cogs_account.sql` (idempotent, runs for ALL companies):
   ```sql
   INSERT INTO master.chart_of_accounts (company_id, account_code, account_name_th, account_name_en, account_type, normal_balance, is_header, is_active, created_at)
   SELECT c.company_id, '5000', 'ต้นทุนขาย', 'Cost of Goods Sold', 'EXPENSE', 'DR', FALSE, TRUE, now()
   FROM master.companies c
   WHERE NOT EXISTS (SELECT 1 FROM master.chart_of_accounts a WHERE a.company_id = c.company_id AND a.account_code = '5000');
   UPDATE sys.expense_categories ec SET default_account_id = a.account_id
   FROM master.chart_of_accounts a
   WHERE ec.code = 'COGS' AND a.company_id = ec.company_id AND a.account_code = '5000'
     AND ec.default_account_id IS DISTINCT FROM a.account_id;
   ```
   VERIFY actual column names against the schema/EF config before writing (account_name_th
   vs name_th etc., expense_categories.code/default_account_id) — adjust to reality.
   NOTE: script runs as superuser at startup (DbInitializer) — RLS not an issue there.
2. `CompanyService.CreateAsync` DefaultChartOfAccounts array (MasterDataServices.cs): add
   `("5000", "ต้นทุนขาย", "Cost of Goods Sold", EXPENSE, DR)` matching tuple shape, and
   update `DefaultExpenseCategories` so COGS maps to 5000 (it currently resolves via
   coaLookup — point COGS at "5000").
3. Check `GlAccountsOptions` — if it carries a COGS key add/keep consistent; do NOT touch
   unrelated posting keys.
4. Tests: existing CompanyCreateRlsTests count assertions compare vs sibling company (26
   now) — they assert equality, so they stay green; OnboardingFoundingAddressTests may
   assert CoA count — update if it hardcodes 25.

## F-8 — tax-summary ภ.ง.ด.1 column includes payroll withholding

Backend report service for tax-summary (grep `TaxSummary` in backend/src): the ภ.ง.ด.1
column currently sums WHT-certificate data only. ADD posted payroll runs' total PIT
(`payroll.payroll_runs` Posted/Paid) grouped by PAY-DATE month into that column (sum with
any certificate-based ภ.ง.ด.1 amounts). Update the page footnote (FE) to
"…· ภ.ง.ด.1 รวมเงินเดือนที่ Post แล้ว". Test: seed/post payroll (or unit-level service
test) → column month = PitWithheld sum.

## F-4 — SSO header label

Payroll run detail page (frontend payroll/[id]): the summary card labeled "ประกันสังคม"
shows employee+employer combined → change label to "ประกันสังคม (รวมนายจ้าง)" (TH) /
"Social security (incl. employer)" (EN) via i18n keys.

## F-7 — payroll i18n/UX batch

1. Status filter on /payroll list renders raw `status.POSTED` — add missing i18n key(s)
   (check the combobox options source; add Thai "บันทึกบัญชีแล้ว" / EN "Posted").
2. Duplicate-period 422 toast shows raw English API detail — map error code
   `payroll.duplicate_period` in the FE problems i18n table (frontend/lib/i18n/problems.ts)
   to "มีรอบจ่ายของงวด {period} อยู่แล้ว" (follow existing entries' interpolation pattern;
   if no interpolation support, static Thai message).
3. Create-run modal prefills the CURRENT existing period → prefill NEXT open period:
   max(existing periods)+1 month (yyyymm arithmetic), fallback current month when no runs.

## F-10 — ภ.พ.30 English warning

frontend reports/pnd30 page: "Last day of filing: {date}. Run finalize at least 1 day
before." → Thai via i18n: "วันสุดท้ายของการยื่น: {date} — ควรยืนยัน/ปิดงวดล่วงหน้าอย่างน้อย
1 วัน" (keep EN for EN locale).

## Gates (all mandatory, report evidence verbatim)
- `dotnet build` clean; backend full suite with `TEAS_TEST_PG` set in the SAME shell
  command (grep troubles-wiki.md for env details); skip count == baseline (~8); the ONLY
  allowed pre-existing failure = `McpServerSmokeTests.E3_create_vendor_returns_id_code_name`
  (documented in troubles-wiki.md).
- FE: `pnpm exec tsc --noEmit` clean + `pnpm exec next build` OK (run in frontend/).
- Bengali glyph check: grep "ম" over touched files → 0 hits.
- No commits. Update THIS spec's checklist with [x] + evidence + attempt log.

## Blast-radius cap
Backend: PayrollRunService.cs, Employee entity+DTOs+service/validator, MasterDataServices.cs
(DefaultChartOfAccounts + DefaultExpenseCategories), tax-summary report service, MarkPaid
endpoint/request DTO, 2 new SqlScripts (610, 611). FE: employees modal, payroll list page,
payroll detail page, pay dialog, pnd30 page, problems.ts/i18n messages. Tests as listed.
Anything beyond → STOP and report.

## Checklist
- [ ] F-3 schema 610 + entity/DTO/validators + engine + FE modal + tests (red where applicable → green)
- [ ] F-6 MarkPaid JE + FE dialog + tests
- [ ] F-9 script 611 + CreateAsync defaults + tests updated
- [ ] F-8 tax-summary column + footnote
- [ ] F-4 label · F-7 i18n×3 · F-10 pnd30 i18n
- [ ] Gates all green (evidence)

## Attempt log
- 2026-07-18 (Fable): designs decided per REPORT recommendations; Ham approved "แก้เลยเอา
  ตามที่แนะนำเลย". Dispatching Codex (quota arbitrage — Claude pool 92%). Opus Tier-2 review
  (money/schema lenses) + Fable diff read + commit + release/deploy AFTER quota reset.

# Payroll — NEXT SESSION kickoff plan (P-C / P-D)

> Read order: CLAUDE.md → progress.md (top) → this. P-A (Employee master + PIT engine) is
> SHIPPED + committed. This plan drives the payroll RUN + OUTPUTS. Design + locked decisions:
> `payroll-module-design-2026-05-31.md` (§7 decisions, §7.2 config-driven rates).

## Already in place (don't rebuild)
- `Employee` master (entity/config/migration `AddEmployeeMaster`/RLS 430/CRUD/FE) — committed.
- `ThaiPitCalculator` + `PitSchedule` (Domain, pure, 12 golden tests) — ม.50(1) projected-annual.
- Filing reuse targets: `WhtBatchFormat` (PND1 is the SAME RD FORMAT กลาง family),
  `Wht50TawiFormFiller` (already fills Pnd1/Pnd2/Pnd3/Pnd53 AcroForm), `TaxFilingStore`.
- Locked: minimal allowances, strict ม.50(1), standalone Employee, SSO-only, monthly per-employee
  payment evidence, statutory rates in config/seed.

## P-C — Payroll run → payslip (the core; main agent owns — compliance + GL + migration)
1. **Config — `SsoOptions`** (bind `Payroll:Sso`): `Rate` (0.05), `WageFloor` (1650), `WageCeiling`
   (15000 — ⚠️ confirm the 2569 effective value vs SSO before go-live), `MaxAllowanceForPit` (9000).
   PIT allowance amounts: extend `PitSchedule` or a `PitAllowances` config (personal 60k, spouse
   60k, child 30k). All config/seed, never UI (§4.6).
2. **Entities** (Domain, `Entities/Payroll/`):
   - `PayrollRun : ITenantOwned` — period(yyyymm), payDate, status (Draft→Approved→Posted→Paid),
     docNo (PR-prefix on Post), totals (gross/pit/ssoEmp/ssoEr/net), journalId?, posted/by, paid/by.
   - `Payslip` (per employee per run) — snapshot employee name/national_id/address (like
     WhtCertificate), grossTaxable/grossNonTaxable, pitWithheld, ssoEmployee, ssoEmployer,
     otherDeductions, netPay, ytdIncome, ytdPit, + optional earning/deduction detail rows.
   - Configs + migration (build first, `dotnet ef … WITH build` from W:, RLS SqlScript `45x_*`).
     Immutable trigger on a posted run (mirror TI immutability 040) — or app-level MarkPosted.
3. **`PayrollRunService`** (Infrastructure):
   - Draft: for each active employee in the period, compute ssoEmployee = round(clamp(salary,
     floor,ceiling)×rate,2) (=ssoEmployer); annualAllowances = personal + (married&!spouseIncome?
     spouse:0) + children×childAllow + min(sso×12, maxSsoAllow); ytd from prior posted runs this
     tax year; project via `ThaiPitCalculator.ProjectAnnualIncome` + `MonthlyWithholding`.
   - Approve→Post: assign docNo, post GL JV (§5 below), set status; **immutable after Post**
     (corrections = reversing run, never edit). Pay: mark Paid + stamp pay date.
   - Tenant-scoped; money decimal(4dp); audit each transition.
4. **GL posting** (on Post — balanced, via `IJournalService`, account codes from `GlAccountsOptions`
   — add salary-expense / employer-sso-expense / pit-payable / sso-payable / net-wages-payable keys):
   `Dr salary-expense (gross) ; Dr employer-sso-expense (ΣssoEr) ; Cr pit-payable (Σpit) ;
    Cr sso-payable (ΣssoEmp+ΣssoEr) ; Cr net-wages-payable (Σnet)`.
5. **Endpoints** `/payroll/runs` (create-draft/approve/post/pay/get/list) + permission
   `payroll.run.manage` (+ `.post`/`.pay` SoD split like PV approve) — seed `46x_*`.
6. **Tests** (xUnit, teas_test, TestIds, pass 2×): a full run for 2 employees → assert per-payslip
   pit (cross-check `ThaiPitCalculator`), SSO clamp at ceiling, GL JV balances, immutability after
   Post, YTD carry across two consecutive months.

## P-D — outputs
1. **Payment evidence / payslip PDF (Ham hard requirement)** — QuestPDF, one PDF per employee per
   run: employee, period, gross/PIT/SSO/net breakdown, bank account + pay date (the transfer proof).
   Endpoint `/payroll/runs/{id}/payslips/{employeeId}/pdf` (+ a batch/zip option). Reuse the Thai
   font + QuestPDF infra already registered.
2. **ภ.ง.ด.1 (monthly)** — compute from the period's payslips → preview/finalize via `TaxFilingStore`
   (mirror `WhtFilingService`); **batch file**: extend `WhtBatchFormat` for the PND1 layout (download
   the RD `FormatPND1V2_0.pdf` to confirm fields — H/D + pipe + UTF-8 + พ.ศ. machinery already built).
3. **ภ.ง.ด.1ก (annual)** + **employee 50ทวิ (annual)** via `Wht50TawiFormFiller` (FormType Pnd1).
4. **SSO contribution file** — SSO e-service has its own fixed format (separate from RD); own builder,
   research layout when scheduled. Lower priority.

## Guardrails (every phase)
Brackets/rates/SSO in config-seed (never UI, §4.6) · posted run immutable (§4.2) · company_id + RLS
(§4.7) · money decimal(4dp) · no PII in logs · audit transitions · doc-no on Post only (§4.3) · CE
internally, พ.ศ. only at file/print boundary · build solution first then `dotnet ef … WITH build`
from W: · kill :5080 before full build · tests pass 2× on teas_test · FE tsc 0 · update progress.md+plan.md.

## Open item to confirm with Ham / source
- Exact **2569 SSO wage ceiling** (15,000→17,500 phased) effective for the filing month.
- ภ.ง.ด.1 PND1 central-format field list (download `FormatPND1V2_0.pdf` from rd.go.th).

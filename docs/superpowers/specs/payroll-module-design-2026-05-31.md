# Design Spec — Payroll module (เงินเดือน + PIT หัก ณ ที่จ่าย ม.50(1) + ประกันสังคม)

> cont.82.1 follow-up — the second locked item (Ham, 2026-05-31). Payroll is the prerequisite
> that unlocks **ภ.ง.ด.1 / 1ก** (employment-income WHT) and the employee **50ทวิ** annual cert,
> none of which TEAS can produce today (only a `User` entity exists — no employee/salary data).
> This is a DESIGN spec (brainstorm → decisions → phased plan), not yet an implementation plan.
> Implementation waits on the §7 open decisions being locked.

## 1. Why / scope
TEAS already does vendor-side WHT (ภ.ง.ด.3/53/54 + 50ทวิ + the new batch file). Employment
income (ม.40(1)) is a different beast: the employer must **withhold monthly PIT** using the
projected-annual method (ม.50(1)), remit **ภ.ง.ด.1** monthly, file **ภ.ง.ด.1ก** annually, issue
each employee a **50ทวิ**, and handle **Social Security (ประกันสังคม ม.33)**. That needs employee
+ salary + payroll-run data the system doesn't model.

**In scope (this module):** employee master, compensation, monthly payroll run → payslip,
monthly PIT withholding (ม.50(1)), SSO 5%/5%, GL posting, and the filing outputs
(ภ.ง.ด.1 + 1ก + batch file, employee 50ทวิ, SSO contribution file).
**Out of scope (flag, not build now):** ภ.ง.ด.2 (ม.40(3)(4) dividends/interest — not payroll),
provident-fund accounting beyond a deduction line, time-attendance/leave, multi-currency payroll.

## 2. Compliance anchors (Thai law — cite in code)
- **PIT progressive brackets** (เงินได้สุทธิ, current 2566+): 0–150,000 = 0% · 150,001–300,000 = 5%
  · 300,001–500,000 = 10% · 500,001–750,000 = 15% · 750,001–1,000,000 = 20% · 1,000,001–2,000,000
  = 25% · 2,000,001–5,000,000 = 30% · >5,000,000 = 35%. **Brackets live in config/seed reference
  data, NOT a UI setting** (same principle as §4.6 VAT — law changes go through deploy + git).
- **ม.50(1) monthly withholding method:** project annual income = (this month's regular income ×
  remaining-incl-current months + income already paid YTD + estimated remaining), subtract
  expenses + allowances → annual net → annual tax via brackets → **monthly WHT = (annual tax −
  tax already withheld YTD) / remaining months**. Bonuses/irregular pay use the add-on method.
- **Standard expense (ม.42 ทวิ):** 50% of ม.40(1)(2) income, **capped 100,000**.
- **Core allowances (ค่าลดหย่อน):** personal 60,000 · spouse 60,000 (no income) · child 30,000
  (60,000 for 2nd+ child born ≥2561) · SSO contributions (actual, ≤9,000) · others (parents,
  insurance, RMF/SSF, donations…) — **the long tail is the decision in §7.**
- **Social Security (ม.33):** employee 5% of monthly wage, **wage capped 15,000 → max 750/month**;
  employer matches 750. Remitted to SSO by the 15th of the following month.
- **Filing due dates:** ภ.ง.ด.1 = 7th of next month (e-filing +8 days); ภ.ง.ด.1ก = within Feb of
  the following year; employee 50ทวิ = by 15 Feb.
- **Immutability:** a posted/paid payroll run is immutable (like a posted document, §4.2) —
  corrections via a reversing/adjustment run, never an in-place edit. company_id on every table.

## 3. Domain model (proposed)
```
Employee (ITenantOwned)               — the payee master
  id, company_id, employee_code, title, first_name_th/en, last_name_th/en,
  national_id(13), tax_id?, structured address (reuse the Vendor-address fields planned for PND3),
  hire_date, termination_date?, employment_type, pay_frequency (Monthly),
  bank_name/account_no/account_name, sso_number?, sso_applicable(bool),
  marital_status, spouse_has_income(bool), children_count, allowance_profile_json (or child rows),
  base_salary (decimal), is_active
  [link to User? — optional 1:1 for self-service payslip later; NOT required for payroll]

EmployeeCompensation (or inline + AllowanceLine rows)
  recurring earnings (taxable / non-taxable flag) + recurring deductions

PayrollRun (ITenantOwned)             — one per (company, period)
  id, company_id, period (yyyymm), pay_date, status (Draft→Approved→Posted→Paid),
  totals (gross, pit, sso_employee, sso_employer, net), journal_id?, posted_at, posted_by
  [immutable after Posted]

Payslip / PayrollLine (per employee per run)
  id, payroll_run_id, employee_id (snapshot of name/national_id/address like 50ทวิ does),
  gross_taxable, gross_nontaxable, pit_withheld, sso_employee, sso_employer,
  other_deductions, net_pay, ytd_income, ytd_pit  (YTD drives the ม.50(1) recompute)
  + earning/deduction detail rows

PitBracket / AllowanceType / EarningType / DeductionType  — seeded reference data
```
- **Numbering:** PayrollRun gets a doc number on Post (PR-prefix, §4.3 pattern); payslips ride the run.
- **Snapshotting:** payslip snapshots employee name/national_id/address at post (like WhtCertificate),
  so later master edits never mutate a filed run.

## 4. PIT engine (the hard, compliance-critical part — main agent owns, no cold subagent)
A pure, unit-testable `ThaiPitCalculator`:
- `AnnualTax(netIncome)` → progressive-bracket walk (golden tests per bracket boundary).
- `MonthlyWithholding(ytdIncome, ytdPit, thisMonthRegular, estimatedRemaining, allowances, monthIndex)`
  → ม.50(1) projected-annual − YTD-withheld / remaining-months. Bonus add-on path separate.
- Brackets + caps from seeded config. Mirror the `WhtBatchFormat` "pure + golden tests" discipline.

## 5. GL posting (on PayrollRun Post)
Per run (net, balanced):
```
Dr  5xxx Salary & wages expense        gross taxable + non-taxable
Dr  5xxx Employer SSO expense          Σ sso_employer
    Cr 2xxx PIT payable (→ ภ.ง.ด.1)         Σ pit_withheld
    Cr 2xxx SSO payable (employee+employer)  Σ sso_employee + Σ sso_employer
    Cr 2xxx Net wages payable / bank        Σ net_pay
```
Account codes via the existing `GlAccountsOptions` map (add salary/PIT-payable/SSO-payable keys).
ภ.ง.ด.1 finalize clears PIT-payable; the SSO remittance clears SSO-payable.

## 6. Filing outputs (reuse what P2 just built)
- **ภ.ง.ด.1 (monthly):** compute from the period's payroll lines → preview/finalize via the same
  `TaxFilingStore` pattern as ภ.ง.ด.3/53. **+ batch file**: PND1 has its own RD FORMAT กลาง
  (PND1 / PND1ก) — **extend `WhtBatchFormat`** (or a sibling) for the PND1 layout; the H/D + pipe +
  UTF-8 + พ.ศ.-date machinery is already built and tested.
- **ภ.ง.ด.1ก (annual):** year-end summary of all employees → return + batch file.
- **Employee 50ทวิ (annual):** reuse `Wht50TawiFormFiller` (already fills the RD AcroForm for
  Pnd1/Pnd2/Pnd3/Pnd53) — payroll just feeds it employee figures.
- **SSO contribution file:** the SSO e-service has its own fixed format (separate from RD) — own
  builder; research its layout when scheduled.
- **Payslip PDF:** QuestPDF, like the other documents.

## 7. DECISIONS — LOCKED (Ham, 2026-05-31)
1. **Allowance scope = MINIMAL v1** — personal 60,000 · spouse 60,000 (no income) · child 30,000
   (60,000 for 2nd+ born ≥2561) · SSO actual (≤9,000). Extend later. **Ham: "เช็คให้ถูกต้องกับ
   สรรพากร"** → all amounts/brackets are seeded reference data with the legal cite, verified against
   the RD Pit_63 infographic (rd.go.th); a tax-year change is a seed/config update, never UI.
2. **PIT method = STRICT ม.50(1)** projected-annual recompute each month (correct for mid-year
   joiners + variable pay).
3. **NO Employee↔User link** — Employee is a fully standalone master (unrelated to `User`).
4. **Statutory funds = SSO only** (ม.33, legally mandatory). Provident fund is NOT legally
   mandatory → modelled only as a generic deduction line, no dedicated PF engine in v1.
5. **Pay frequency = monthly only** (v1). 6. **Bonus/one-time pay**: design the add-on path but
   regular salary is the v1 priority. 7. **ภ.ง.ด.2 = OUT** (investment-income WHT, not payroll).

### 7.1 NEW REQUIREMENT (Ham) — monthly payment evidence per employee
Every payroll run must produce, **per employee per month, a proof-of-payment document** (หลักฐาน
การโอนเงิน) — i.e. a payslip that doubles as / is accompanied by a salary-transfer slip showing
the employee, period, net amount, bank account, and pay date. Render via QuestPDF (one PDF per
employee, or a batch). This is a hard deliverable of the run, not optional.

### 7.2 Config-driven statutory rates (compliance — §4.6)
SSO rate + wage floor/ceiling and the PIT brackets/allowance amounts live in **seed/config**, never
hardcoded in logic and never a UI setting. ⚠️ **2569 SSO ceiling is in flux** (the long-standing
฿15,000 base → ฿750 max is rising toward ฿17,500 → ฿875 on a phased schedule). The calculator reads
the ceiling/rate from config so the move is a config change; **confirm the exact effective ฿ for the
filing month against the SSO office before go-live.**

## 8. Phased plan (after decisions lock)
- **P-A Foundation:** Employee master + compensation (entities, migration, CRUD API, FE master
  pages, i18n). Reuse the structured-address fields added for the PND3/Vendor work.
- **P-B PIT engine:** `ThaiPitCalculator` + seeded brackets/allowances + golden tests (no DB).
- **P-C Payroll run:** Draft→Approve→Post (immutable) + payslip + GL posting + tests on teas_test.
- **P-D Outputs:** payslip PDF → ภ.ง.ด.1 compute + batch file (extend `WhtBatchFormat`) → 50ทวิ →
  ภ.ง.ด.1ก → SSO file.
Each phase: build solution first, tests pass 2× on teas_test, FE tsc 0, progress.md + plan.md.

## 9. Compliance guardrails (carry into every phase)
- Brackets/caps/rates in config-seed, never UI (§4.6). · Posted run immutable (§4.2). · company_id
  + RLS on every table (§4.7). · money = decimal(4dp). · no PII in logs. · audit every state change
  (§4.8). · doc numbering on Post only (§4.3). · CE calendar internally, พ.ศ. only at file/print boundary.

## 10. Cross-refs
- WHT batch machinery to extend for PND1: `docs/superpowers/specs/wht-batch-export-2026-05-31.md`
  + `WhtBatchFormat`. · RD form catalog + priorities: `docs/RD-Forms/TEAS-FORM-GAP-REVIEW.md`.
  · 50ทวิ filler: `Wht50TawiFormFiller`. · Filing persistence: `TaxFilingStore`.

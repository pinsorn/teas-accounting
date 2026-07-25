# B2-pr — ภ.ง.ด.1 / ภ.ง.ด.1ก edge cases, co6 (2026-07-25, prod v1.22.11)

Company: co6 "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด" (id=6). Users: nvadmin01
(COMPANY_ADMIN, creates employees + drafts the run), nvchief01
(CHIEF_ACCOUNTANT, approves/posts/pays), nvtax01 (TAX_OFFICER, attempts the
filings). Driven via headless Playwright (`army-B2-pr.mjs`, deleted after
the run) against `https://teas.kazaki-rio.com`, tall viewport 1440×2200 per
the B2-nv lesson. Code-read first (`PayrollRunService.cs`, `PayrollDtos.cs`,
`PayrollEndpoints.cs`, `ThaiPitCalculator.cs`, `PayrollMath.cs`,
`settings/employees/page.tsx`, `payroll/page.tsx`, `payroll/[id]/page.tsx`)
to pre-compute exact hand-calc numbers before touching the browser — this
also surfaced two structural gaps (F1, F2 below) that the live run then
confirmed rather than discovered blind.

## Done (all 7 mission items)

1. **3 employees created** on co6 as nvadmin01, all salary ฿60,000/mo,
   single/0 children/SSO-applicable (so PIT/SSO math is identical across all
   three and only the hire/termination date varies):
   - `PRA01` "เอสอง ปกติ" — normal, hired 2026-01-01 (full July).
   - `PRB01` "บีสอง เข้ากลางเดือน" — **mid-month hire**, hired 2026-07-15.
   - `PRC01` "ซีสอง ออกกลางเดือน" — **mid-month leave**, hired 2026-01-01,
     terminated 2026-07-10 — set via a direct authenticated `PUT
     /employees/{id}` (same session, the app's own real update endpoint),
     because **the create/edit UI has no termination-date field at all**
     (confirmed live, screenshot `B2-pr-02`, before working around it — see
     F1b). employeeIds 7/8/9. Screenshot `B2-pr-01` (list after create, all
     4 co6 employees incl. B2-nv's own `NVEMP-B2NV`).
2. **Payroll run created + proration checked** — period 202607 (July, the
   live current month), pay date 2026-07-31, run `#9` / `07-2026-PR-0001`.
   **Proration does not exist in this app at all** — every employee active
   in the period gets their FULL `BaseSalary`, with zero day-based
   adjustment for a mid-month start or end. Confirmed 4 independent ways:
   the draft run's own JSON, the run-detail screenshot (`B2-pr-04`), the
   posted GL JE, and the **actual official ภ.ง.ด.1/1ก PDF filings**
   themselves (extracted with `pdftotext`, see Evidence). HIGH finding —
   see F1. Full hand-calc table below.
3. **Negative adjustment — could not be exercised: no mechanism exists.**
   Confirmed by code read (no per-payslip adjustment field is ever settable
   by any endpoint) and live UI walkthrough (create-run modal =
   period/payDate/notes only; run detail page has no edit affordance, only
   read-only payslip rows + a print button — screenshot `B2-pr-05`). See F2
   (UNBUILT, not filed as a bug).
4. **ภ.ง.ด.1 (monthly)** — pulled as nvchief01 (see F3 for why not nvtax01),
   saved to `swarm-findings/army/pdfs/B2-pr-pnd1.pdf` (300.4 KB). Verified
   via `pdftotext -layout`: ใบแนบ grand total row = **4 employees /
   ฿200,000.00 / ฿1,118.76** — exact match to hand-calc. Per-employee rows
   for PRA01/PRB01/PRC01 all show **฿60,000.00 / ฿372.92**, identical
   despite the different hire/termination dates (the F1 proration gap,
   visible in the actual filed document, not just the API).
5. **ภ.ง.ด.1ก (annual)** — pulled as nvchief01, saved to
   `pdfs/B2-pr-pnd1k.pdf` (487.0 KB). Aggregate = ฿200,000.00 / ฿1,118.76,
   identical to ภ.ง.ด.1 (co6's only run this year, so 1:1 aggregation is the
   correct, trivially-consistent case). Per-employee ใบแนบ rows cleanly
   readable this time (better layout than the monthly form):
   PRB01 60,000.00/372.92, PRA01 60,000.00/372.92, NVEMP-B2NV 20,000.00/0.00,
   PRC01 60,000.00/372.92 — matches the API + monthly filing exactly.
6. **สปส.1-10 (SSO)** — both artifacts exist and both pulled clean:
   TIS-620 fixed-width upload file (`pdfs/B2-pr-sso1-10_202607.txt`, 685
   bytes) and the printable PDF (`pdfs/B2-pr-sso1-10.pdf`, 247.2 KB). PDF
   summary block: total wage ฿200,000, employer contribution ฿3,500,
   employee contribution ฿3,500, total ฿7,000, 4 employees — exact match.
7. **GL posting + TB tie** — Post produced JE `#167` (`07-2026-JV-0006`):
   `Dr 5400 เงินเดือนและค่าจ้าง 200,000.00 / Dr 5410 เงินสมทบประกันสังคม-นายจ้าง
   3,500.00 / Cr 2153 ภ.ง.ด.1 หัก ณ ที่จ่ายค้างนำส่ง 1,118.76 / Cr 2160
   เงินสมทบประกันสังคมค้างนำส่ง 7,000.00 / Cr 2170 เงินเดือนค้างจ่าย
   195,381.24` — balances 203,500.00 = 203,500.00, pulled directly via
   `GET /journals/167` (not the FE's own display). Pay then cleared 2170:
   trial balance as-of 2026-07-31 shows account `2170` debit=credit=
   195,381.24, **net 0** — proving the Pay step's `Dr 2170 / Cr bank` line
   posted correctly. Overall TB: **Dr 415,521.24 = Cr 415,521.24, balanced:
   true** (screenshot `B2-pr-10`).

## Hand-calc tables

### Proration (item 2) — engine formula pre-derived from `ThaiPitCalculator`/`PayrollMath` before the live run, all 3 employees ฿60,000/mo, month=July(7)→monthsRemaining=6, YTD=0 (co6's first-ever payroll run):

SSO = clamp(60000,1650,17500)×5% = ฿875.00. ssoAllowance = min(0+875×6,
10500) = 5,250. annualAllowances = 60,000(personal)+5,250 = 65,250.
projected = 0+60,000×6 = 360,000. standardExpense = min(360,000×50%,
100,000) = 100,000. netIncome = 360,000−100,000−65,250 = 194,750.
annualTax = (150,000×0%)+(44,750×5%) = 2,237.50. PIT = 2,237.50/6 =
**372.92** (round half-up).

| Employee | Days worked in July (of 31) | **Hand-calc prorated gross** | **Actual gross (app)** | Δ (overpaid) | PIT shown | Match? |
|---|---|---|---|---|---|---|
| PRA01 (normal, control) | 31/31 | ฿60,000.00 | ฿60,000.00 | ฿0.00 | ฿372.92 | ✅ (no proration needed here) |
| PRB01 (hired 2026-07-15) | 17/31 | **฿32,903.23** | **฿60,000.00** | **+฿27,096.77** | ฿372.92 (same as PRA01) | ❌ **HIGH — F1** |
| PRC01 (terminated 2026-07-10) | 10/31 | **฿19,354.84** | **฿60,000.00** | **+฿40,645.16** | ฿372.92 (same as PRA01) | ❌ **HIGH — F1** |

(PIT "expected-if-prorated" is intentionally not computed — it would require
assuming how a day-based proration should feed the ม.50(1) projected-annual
engine, a design question, not a hand-calc; the point already proven is that
gross itself never moves, so PIT/SSO are computed off an already-wrong base
regardless.)

### GL tie-out (item 7)

| Line | Account | Dr | Cr |
|---|---|---|---|
| Salaries (gross, 4 employees) | 5400 | 200,000.00 | |
| Employer SSO | 5410 | 3,500.00 | |
| PIT payable (ภ.ง.ด.1) | 2153 | | 1,118.76 |
| SSO payable (both halves) | 2160 | | 7,000.00 |
| Net wages payable | 2170 | | 195,381.24 |
| **Total** | | **203,500.00** | **203,500.00** ✅ |
| Pay settlement | 2170 / bank | 195,381.24 | 195,381.24 ✅ (2170 net→0) |

Grand total = 4 employees (PRA01+PRB01+PRC01 @ 60,000 each + the
pre-existing `NVEMP-B2NV` @ 20,000, gross/pit/sso as listed in the payslip
table above) — verified employee-by-employee against the ใบแนบ rows of both
ภ.ง.ด.1 and ภ.ง.ด.1ก.

## Findings

**F1 — HIGH — No day-based salary proration for a mid-period hire or
termination; the official ภ.ง.ด.1/1ก filings themselves overstate income for
partial-month employees.** Root cause: `PayrollRunService.CreateDraftAsync`
(`backend/src/Accounting.Infrastructure/Payroll/PayrollRunService.cs:106`)
sets `thisMonthTaxable = e.BaseSalary` unconditionally for every employee
whose employment window merely *overlaps* the period — the class doc-comment
says outright "v1 takes no per-employee input (regular salary only)"
(`PayrollDtos.cs:7`). Live repro: PRB01 (hired mid-July) and PRC01
(terminated mid-July) both received the identical full ฿60,000.00/
฿372.92-PIT payslip as PRA01 (worked the whole month) — confirmed in the
draft JSON, the run-detail screenshot, the posted JE, AND the printed
ภ.ง.ด.1/ภ.ง.ด.1ก PDFs. Classified **UNBUILT** (not a crash/regression — the
code is deliberate v1 scope, per its own comment) but a genuine compliance
gap: a company using this app for real payroll would file an incorrect
withholding return for every mid-month joiner/leaver.
*Sub-finding F1b — the termination-date field doesn't exist in the UI at
all.* `settings/employees/page.tsx`'s create/edit modal has an input for
every `CreateEmployeeRequest`/`UpdateEmployeeRequest` field EXCEPT
`terminationDate` — confirmed by full JSX read and live screenshot
(`B2-pr-02`, PRC01's edit modal, no such field anywhere). The backend model,
the payroll eligibility filter, and even the update endpoint/validator all
fully support it — only the FE surface is missing. `PayrollRunService.cs`'s
own code comment prescribes the correct offboarding flow ("set
TerminationDate … THEN deactivate") but the tool to do the first half does
not exist; only hard "ปิดใช้งาน" (deactivate → `isActive=false`, which
excludes the employee from ANY future run immediately, a different and
harsher semantic) is exposed. Verified live via the app's own real `PUT
/employees/{id}` endpoint (not a DB edit) as the only way to set it.

**F2 — UNBUILT (documented, not filed as a bug) — no negative-adjustment /
per-payslip-deduction mechanism exists anywhere in Payroll.**
`Payslip.OtherDeductions` is a real column, flows into `NetPay`/
`TotalOtherDeductions`, and the GL-posting comment even reasons about it
("a nonzero ΣOther would unbalance here") — but it is hardcoded `0m` at
creation (`PayrollRunService.cs:127`) with no create/update DTO field, no
endpoint, and no UI control anywhere (`CreatePayrollRunRequest` takes only
period/payDate/notes; posted runs are explicitly "intentionally no edit
endpoint"). Mission item 3 (add an overpayment-clawback line, check totals/
WHT/SSO don't go negative) has no live scenario to drive — there is no path,
UI or API, that ever sets a nonzero `OtherDeductions`. Confirmed via full
code read (`PayrollDtos.cs`, `PayrollEndpoints.cs`) + live screenshots of
the create-run modal and the read-only payslip detail modal.

**F3 — HIGH — TAX_OFFICER (nvtax01) is 403'd out of the ENTIRE Payroll
module — cannot view or file ภ.ง.ด.1, ภ.ง.ด.1ก, or สปส.1-10 at all.** Live
repro, 4/4 confirmed 403 as nvtax01: `GET /payroll/runs` → 403, `GET
/payroll/runs/9` → 403, `GET /payroll/runs/9/pnd1/pdf` → 403, `GET
/payroll/pnd1a/pdf?year=2026` → 403 (all captured in
`B2-pr-results.json`). Root cause: every route in `PayrollEndpoints.cs` —
including the read-only list/detail/PDF/file endpoints — is gated on
`payroll.run.manage`, which seed `481_seed_payroll_perms.sql` grants ONLY to
`SUPER_ADMIN`/`COMPANY_ADMIN`/`CHIEF_ACCOUNTANT` ("payroll is sensitive
HR/finance"). `TAX_OFFICER`'s own grant (seed
`627_seed_tax_officer_filing_grant.sql`, the CRIT-2 fix) covers only
`tax.filing.preview`/`tax.filing.read` — a different permission namespace
the Payroll module never checks. **Same shape as CRIT-2**, which fixed this
exact class of gap for VAT filings (ภ.พ.30/ภ.ง.ด.3/53/54/36/51/50) but was
apparently never extended to Payroll's own filings — the B-1 blocker's
stated SoD design explicitly has TAX_OFFICER "files taxes" for co6, so this
directly blocks that role from doing its one job. *Secondary LOW UX note*:
the FE doesn't surface a permission-denied state for this — nvtax01's
`/payroll` page silently renders "ไม่มีข้อมูล" (screenshot `B2-pr-11`),
which reads as "no payroll exists" rather than "you lack permission" (the
sidebar nav correctly HIDES the "เงินเดือน" link for this role, so it's not
a discoverable dead end, only reachable by direct URL — not a tenant/data
leak, just a misleading empty state).

## Unbuilt-vs-untested classification

| Item | Status |
|---|---|
| ภ.ง.ด.1 (monthly filing, PDF) | **Built + working** — exact match |
| ภ.ง.ด.1ก (annual filing, PDF) | **Built + working** — exact match, aggregates correctly |
| สปส.1-10 (SSO file + PDF) | **Built + working** — exact match |
| GL posting (Post) + Pay settlement | **Built + working** — JE balances, TB ties |
| Day-based mid-month proration | **UNBUILT** (v1 scope, per code comment) — F1 |
| Termination-date UI field | **UNBUILT** (backend-only; zero FE surface) — F1b |
| Negative-adjustment / deduction line | **UNBUILT** (schema stub, zero wiring) — F2 |
| TAX_OFFICER access to payroll filings | **Built but mis-permissioned** — F3 is an RBAC bug, not a missing feature |

## Blast radius

3 employees created (cap ≤5 — respected), 1 payroll run created (cap
≤2 — respected; the run swept in 1 pre-existing employee from B2-nv, not
counted against this leg's creation cap). No period close, no year-end
action (left for B2-ye). No co2/co3/co5 data touched — every screenshot's
sidebar/header stayed in the co6 session throughout (`B2-pr-01`..`11`); the
one direct API 403 probe (nvtax01) touched no data, only confirmed a status
code.

## Evidence / artifacts

- Screenshots: `swarm-findings/army/B2-pr-01..11-*.png` (employees list,
  employee-edit-modal-no-termdate, payroll-create-modal, draft/approved/
  posted/paid run detail, pay modal, payslip read-only modal, trial balance,
  nvtax01's empty `/payroll` page).
- PDFs: `swarm-findings/army/pdfs/B2-pr-pnd1.pdf`,
  `B2-pr-pnd1k.pdf`, `B2-pr-sso1-10.pdf`, plus the TIS-620 upload file
  `B2-pr-sso1-10_202607.txt`. Text-extracted copies (`pdftotext -layout`)
  of the two PDFs kept alongside as `.txt` for future re-verification
  without opening a PDF viewer.
- Raw JSON dump of every API call made (employee list, run draft/posted/
  paid detail, journal #167, trial balance, PDF byte counts, nvtax01's 4×
  403 responses): `swarm-findings/army/B2-pr-results.json`.
- Temp driver script `frontend/army-B2-pr.mjs` — deleted after the run.

## No tenant leak

Every screenshot and API call stayed scoped to co6 (company id 6) —
employees list shows only co6's 4 employees, payroll run shows only co6
payslips, trial balance is co6's own chart of accounts (2151/2152/2153/2160/
2170, matching B2-nv's already-documented co6 CoA). No co2/co3/co5 data
appeared anywhere.

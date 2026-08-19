# Leg 1 - Payroll - Findings (Round 2 Testing Swarm)
Company under test: company_id=1 "Demo Company". API base: http://localhost:5080
Started: 2026-08-18

## L1-1 [RED money/tax] seed 637's tax-ID repair is masked by a separate company_profile.tax_id row still holding the placeholder -- Pnd1/SSO filings will carry tax_id 0000000000000

Repro / evidence (DB read only):
  SELECT company_id, tax_id, name_th FROM master.companies WHERE company_id=1;
   -> 1 | 0105000000012 | Demo Company (correctly repaired by 637_repair_all_zero_company_tax_id.sql)
  SELECT company_id, tax_id, legal_name FROM master.company_profile WHERE company_id=1;
   -> 1 | 0000000000000 | Demo Company  <- STILL the placeholder, untouched by script 637

Root cause: Pnd1FilingService.BuildPnd1MonthlyAsync / BuildPnd1aAnnualAsync / SsoFilingService.BuildMonthlyAsync
all resolve employer tax id as `EmployerTaxId: prof?.TaxId ?? c?.TaxId ?? ""`. Script 637 (its own header
comment) only updates master.companies.tax_id. company_profile row 1 exists (non-null) with TaxId =
"0000000000000", so the ?? chain picks that value first -- the repaired companies.tax_id fallback never
fires because ?? only substitutes on NULL, not on a placeholder string.

Expected (per task brief): every payroll filing artifact (Pnd1 monthly, Pnd1a annual, 50-tawi, SSO file+PDF)
should carry 0105000000012 now that 637 has run.
Actual: all of them will render/export EmployerTaxId = "0000000000000" for company 1 -- the exact class of
defect 637/F10 was written to close, just via a column the repair script didn't know about.

Severity: RED. A government WHT/SSO filing goes out with a fictitious placeholder taxpayer ID even though
the "repair" migration ran successfully and master.companies looks correct on a shallow check.

Fix direction (not built, worker does not edit code): either extend 637 (or a new idempotent script) to
also update master.company_profile.tax_id wherever it is the 0000000000000 placeholder, or add the same
"refuse on all-zero payer tax id" guard PaymentVoucherService's 50-tawi path has, to Pnd1FilingService /
SsoFilingService's employer-tax-id resolution -- currently there is NO refusal at all on this path.

Will verify against the actual rendered PDF/file bytes once a run is posted (below).

---

## L1-NOTE Corrected checklist assumption: SSO wage ceiling is now 17,500 (max 875/side), not 15,000/750
The task brief says "5% employee + 5% employer, THB15,000 wage cap (max 750/side)". Source
(PayrollOptions.cs SsoOptions.WageCeiling = 17_500m, comment cites the phased schedule effective
1 Jan 2569/2026: 2569-2571 -> 17,500 (max 875/mo), 2572-2574 -> 20,000, 2575+ -> 23,000). Current
date is 2026-08-18, inside the 2569-2571 phase, so 17,500/875 is the CORRECT current-law figure and
the system implements it correctly. Confirmed live via run 2 below: every SSO-applicable employee's
contribution clamps at exactly 875.00 once gross >= 17,500. Not a defect -- flagging so the checklist
number itself isn't mistaken for a bug. PASS.

## PASS - Employee master create (file-based UTF-8 payload) stores Thai text correctly
POST /employees with body written to a file and sent via curl --data-binary (avoids a client-side
shell-quoting corruption -- see L1-2 below) round-trips Thai UTF-8 correctly. Verified via
`SELECT first_name_th, encode(first_name_th::bytea,'hex')` = e0b897e0b894... matching the source
bytes exactly. Employees L1EMPA2/B2/C2/D2/E/F (ids 8-13) all clean.

## L1-2 [YELLOW UX/tooling, NOT a server defect] inline-quoted curl -d with embedded Thai text over a Windows Git-Bash pipe silently corrupts to literal '?' bytes -- worker artifact, documented for future legs
First attempt created employees (ids 3-6) via `curl -d '{"firstNameTh":"สมชาย",...}'` (Thai text
inline in a single-quoted bash argument passed through this session's Bash tool). Every Thai
character landed in Postgres as literal ASCII '?' (0x3f), confirmed via
`encode(first_name_th::bytea,'hex')` = `3f3f3f3f3f`. Re-tested the EXACT same content written to a
file first (`cat > file.json <<'EOF' ... EOF`, verified via `xxd` the file itself holds proper UTF-8
bytes e0b8xx), then sent with `curl --data-binary @file.json -H "Content-Type: ...; charset=utf-8"`
-- stored perfectly (hex matches source). Root cause isolated to THIS session's Bash-tool arg-passing
of multibyte UTF-8 inside `-d '...'`, not the API/DB (LC_CTYPE=C.UTF-8 in the shell itself, echo of
raw Thai reproduces correct UTF-8 bytes -- the corruption is specific to how curl's -d argument text
transits from tool-call to process on this Windows/Git-Bash bridge). Recording as a note (not a
product defect) so any other worker in this swarm hitting mangled Thai text via inline curl -d
knows to switch to file-based --data-binary. The 4 corrupted employees (ids 3-6) were deactivated
(soft-delete, DELETE /employees/{id} -> 204) and replaced with clean re-creates (ids 8-11).

## PASS - WHT (PIT) progressive-bracket monthly withholding, hand-verified against ThaiPitCalculator source, 3 scenarios
Payroll run 2 (period 202607, PayDate 2026-07-31, monthsRemaining=13-7=6), all figures independently
recomputed by hand from PitSchedule.Current() bands (150k/300k/500k/750k/1M/2M/5M @ 0/5/10/15/20/25/30/35%)
and ThaiPitCalculator.MonthlyWithholding, and matched the API/DB EXACTLY:

- L1EMPE (single, no opening YTD, base 150,000/mo, full month): projected annual = 150000*6 = 900000;
  sso allowance = min(875*6,10500)=5250; standard expense capped 100000; allowances 60000+5250=65250;
  net income = 900000-100000-65250=734750; annual tax by band = 0 + 7500(band2) + 20000(band3) +
  35212.50(band4, partial 234750*15%) = 62712.50; monthly = 62712.50/6 = 10452.0833 -> round 10452.08.
  API returned pitWithheld = 10452.0800. EXACT MATCH.

- L1EMPF (single, YtdOpeningYear=2026, YtdOpeningIncome=480000, YtdOpeningPit=15000,
  YtdOpeningSso=5250, base 80,000/mo, full month): ssoEmp=875; ssoAllowance=min(0+5250+875*6,10500)=
  10500 (exactly capped); allowances=60000+10500=70500; projected=480000+80000*6=960000; standard
  expense capped 100000; net income=960000-100000-70500=789500; annual tax by band =
  0+7500+20000+37500(band4 full 250000*15%)+7900(band5, partial 39500*20%, breaks before band6
  since netIncome<=1,000,000)=72900; remaining after YTD PIT 15000 = 57900; monthly=57900/6=9650.00.
  API returned pitWithheld=9650.0000, ytdIncome=560000 (480000+80000), ytdPit=24650 (15000+9650).
  EXACT MATCH on all four figures -- confirms the YTD-opening-balance carry-forward path is correct.

- L1EMPA2/B2/C2/D2 (base 30000/60000/prorated-23225.81/20000, all with monthsRemaining=6 and no
  opening YTD): all correctly compute PIT=0 because projected annual net income stays inside the
  0%-band exemption at these salary levels -- verified the full arithmetic chain (StandardExpense,
  ssoAllowance, allowances, AnnualNetIncome) lands <=150,000 net for all four. PASS (zero-tax edge
  correctly produces zero, not a null/error).

## PASS - O8 calendar-day proration, hand-verified
L1EMPC2 hired 2026-07-16 into period 202607 (31-day July). DaysEmployed = July31.DayNumber -
July16.DayNumber + 1 = 16. MonthlyGross = round(45000*16/31, 2, AwayFromZero) = round(23225.8064...,2)
= 23225.81. API/DB grossTaxable = 23225.8100. EXACT MATCH. SSO on the prorated gross still clamps to
the 17,500 ceiling -> 875.00, matches.

## PASS - SSO applicability opt-out and cap, hand-verified
L1EMPD2 (ssoApplicable=false) correctly gets ssoEmployee=ssoEmployer=0.0000 in the run, and (checked
below) is excluded from the SSO filing line list. L1EMPA2/B2/C2/E/F all clamp to exactly 875.00
(17,500 ceiling * 5%) regardless of gross being above or at the ceiling. No off-by-one/rounding
drift observed across 5 SSO-applicable employees.

Run totals cross-check (run 2): totalGrossTaxable 363225.81 = sum of 6 lines (30000+60000+23225.81+
20000+150000+80000) EXACT. totalPit 20102.08 = 0+0+0+0+10452.08+9650.00 EXACT. totalSsoEmployee/
Employer 4375.00 = 875*5 EXACT. totalNet 338748.73 = 363225.81-20102.08-4375.00 EXACT (GrossNonTaxable
and OtherDeductions both 0 for this run).


## L1-3 [PASS] Deduction boundary rejections all typed 400, no raw 500 (API-side probes, pre-directive-change)
Before the orchestrator's mid-task directive to switch to browser-driven testing, these 4 probes were
run directly against PUT /payroll/runs/2/deductions on the draft run (run 2, period 202607):
- over-cap (employee 9 "L1EMPB2", cap=59125.00, tried amount=59126) -> HTTP 400,
  urn:teas:error:validation, Thai message names the employee code and the exact cap "59,125.00 บาท".
- unknown employeeId (999999) -> HTTP 400, message names the unknown id "999999" and
  "ในรายการเงินเดือนนี้" (not in this payroll run).
- negative amount (-100) -> HTTP 400, field-level error on deductions[0].amount, "จำนวนเงินหักต้องมากกว่า 0".
- zero amount (0) -> HTTP 400, same field-level error.
All four are FluentValidation-shaped ValidationProblem responses (urn:teas:error:validation, 400,
fieldErrors[]) rather than the DomainException shape seen in PayrollRunService.UpdateDeductionsAsync's
source -- UpdatePayrollDeductionsValidator (PayrollDtos.cs) duplicates the over-cap/unknown-employee/
draft-only checks at the DTO layer ahead of the service. Functionally correct (typed 400, clear Thai
message, no raw 500) either way -- flagging the duplication only as a maintainability note, not a defect:
two places (validator + service) must be kept in sync if the cap rule ever changes.

## NOTE - mid-task directive change (orchestrator, timestamped in-session)
Orchestrator instructed: all document creation/walking (employee, payroll run, approve, pay) must
happen through the real UI via Playwright (ad-hoc specs in frontend/e2e/r2-leg1-*.spec.ts, NOT
committed), DB verification via psql stays as before, and direct API calls are now RESERVED for
permission-guard (403) and malformed-payload error-contract probes only. Everything done API-side
above (employees L1EMPA2/B2/C2/D2/E/F ids 8-13, payroll run id 2 period 202607 with draft payslips,
the 4 deduction-boundary probes) is KEPT as valid evidence and noted here as "API-side, pre-directive".
From this point on, run/employee creation and the approve->post->pay walk happens through the browser;
at least one full cycle is re-walked end-to-end in the UI per the directive.


## L1-1 CONFIRMED WITH DIRECT PDF EVIDENCE (browser-produced artifact)
After posting run 2 through the actual UI (docNo 07-2026-PR-0001, journalId 11) and clicking the
"ภ.ง.ด.1 (PDF)" button in the browser, the rendered PDF was captured via an authenticated
page.request.get('/api/proxy/payroll/runs/2/pnd1/pdf') (200 OK, 321,813 bytes) and converted with
`pdftotext -layout` (poppler, available at /mingw64/bin/pdftotext). The extracted text shows the
taxpayer-ID box rendered as:

    0 - 0 0 0 0 - 0 0 0 0 0 - 0 0 - 0

i.e. thirteen ASCII '0' digits in the RD's boxed tax-ID format -- the placeholder, NOT the repaired
0105000000012. Everything else on the form is correct and matches independently-verified figures:
company name "Demo Company (...)" and the income/PIT totals "363,225.81" / "20,102.08" (exactly the
run's totalGrossTaxable/totalPit computed and verified earlier). This is a REAL government WHT
filing artifact, produced end-to-end through the browser, carrying a fictitious taxpayer ID -- not a
theoretical code-read finding. Raw PDF byte-grep for the tax id string failed (PDF content streams
are FlateDecode-compressed); pdftotext -layout was required to see it. Saved at
Z:\temp\...\scratchpad\leg1-pnd1-v2.pdf / leg1-pnd1-v2.txt for inspection.

## PASS - SSO batch-file guard (sso_batch.missing_employer_account) fires correctly through the UI
Company 1's company_profile.sso_employer_account_no is blank (confirmed via psql). Clicking
"สปส.1-10 (ไฟล์)" on the posted run in the browser triggered a 422 response:
`{"type":"urn:teas:error:sso_batch.missing_employer_account","title":"sso_batch.missing_employer_account",
"status":422,"detail":"ยังไม่ได้ตั้งค่าเลขที่บัญชีนายจ้าง (10 หลัก) — กรอกในข้อมูลบริษัทก่อนจึงจะออกไฟล์ สปส.1-10 ได้
[SSO employer account number is required for สปส.1-10. Set it on the company profile
(CompanyProfile.SsoEmployerAccountNo) first.]"}` -- a typed, structured error with a clear bilingual
message, NOT a raw 500 or a corrupt file. This confirms the R2/H8 guard (SsoFilingService.
EnsureEmployerAccount) actually fires in the live system exactly as coded. PASS.

## PASS - full browser-driven payroll lifecycle: draft -> deduction edit-door -> approve -> post -> pay
Walked entirely through the real UI (Playwright, frontend/e2e/r2-leg1-payroll-cycle.spec.ts, all 6
tests green):
1. Created employee L1EMPG via /settings/employees form UI (screenshot leg1-01).
2. Draft run (id 2, period 202607) detail page showed totals EXACTLY matching independently
   hand-verified figures: gross 363,225.81, PIT 20,102.08, net 338,748.73 (screenshot leg1-03).
3. Deduction edit-door: applied ฿5,000 + reason "ทดสอบหักเงิน L1-edit-door" to L1EMPA2 via the
   inline table inputs, saved -> net cell updated on screen to 24,125.00 (screenshot leg1-04).
   Reloaded the page (simulating reopen) WITHOUT changing any field, clicked "บันทึกรายการหัก"
   again -> screen still shows 24,125.00, no toast error, no drift (screenshot leg1-05). DB verified
   below.
4. Approved via UI (button "อนุมัติ", no confirm dialog, matches source) -> status badge
   "อนุมัติแล้ว" (screenshot leg1-06).
5. Posted via UI (button "บันทึกบัญชี" -> AlertDialog "ยืนยันบันทึกบัญชี?..." -> confirm) ->
   status "บันทึกบัญชีแล้ว", docNo 07-2026-PR-0001, journalId 11 assigned (screenshot leg1-07;
   verified in DB below).
6. Paid via UI (button "จ่ายแล้ว" -> bank-account modal -> "ยืนยันการจ่าย") -> status
   "บันทึกการจ่ายแล้ว" (screenshot leg1-10).
DB row-for-row after step 3 (edit-door), before/after the unchanged resave, for payslip_id=5
(L1EMPA2): other_deductions=5000.0000, other_deductions_reason='ทดสอบหักเงิน L1-edit-door',
net_pay=24125.0000 -- IDENTICAL before and after the no-op resave (verified via psql, see below).
PASS on the edit-door invariant.


## PASS - GL tie-out on the browser-posted run: Dr=Cr exact, O10 deduction credit line correct, payable clears exactly
Accrual JE (journal_id=11, doc 07-2026-JV-0001, posted alongside PR doc 07-2026-PR-0001):
  Dr 5400 เงินเดือนและค่าจ้าง (salary expense)       363,225.81
  Dr 5410 เงินสมทบประกันสังคม-ส่วนนายจ้าง (employer SSO exp) 4,375.00
  Cr 2153 ภาษีเงินได้พนักงานหัก ณ ที่จ่ายค้างนำส่ง (PIT payable)   20,102.08
  Cr 2160 เงินสมทบประกันสังคมค้างนำส่ง (SSO payable, emp+er)  8,750.00
  Cr 2170 เงินเดือนค้างจ่าย (net wages payable)         333,748.73
  Cr 2180 เงินหักจากพนักงานค้างนำส่ง (other deductions payable) 5,000.00
  Total Dr = 367,600.81 = Total Cr = 367,600.81. EXACT MATCH. The Cr 2180 line for exactly
  ฿5,000.00 (the deduction applied via the browser edit-door test) confirms O10's core invariant
  live in a real posted run: a nonzero deduction posts its own balanced credit line without moving
  total debits/GrossTaxable, closing the exact regression the O10 spec's D1 was written to prevent.

Settlement JE (journal_id=12, doc 07-2026-JV-0002, posted on Pay):
  Dr 2170 เงินเดือนค้างจ่าย  333,748.73
  Cr 1110 เงินสด (cash, since no active bank account existed -> fell back to the 1110 cash account
  per PayrollRunService.PayAsync's `activeBanks.Count == 1 ? ... : null` -> ResolveAccountIdAsync
  ("1110") path -- correct fallback behavior, matches the UI's "เงินสด (1110)" label option)
  333,748.73.
Net wages payable (2170) balance across BOTH journals: SUM(Dr)=SUM(Cr)=333,748.73, net=0.0000 --
the payable clears EXACTLY, no residue. PASS.

## PASS - deduction edit-door idempotency confirmed via audit log + DB row (not just screen text)
audit.activity_log for PayrollRun id=2 shows exactly two DeductionUpdated events, 1.5s apart
(22:27:53.318 and 22:27:54.862), BOTH with identical metadata
`{"note": "employee:8;reason:ทดสอบหักเงิน L1-edit-door", "toStatus": "Draft", "fromStatus": "Draft"}`
-- confirming the second (unchanged reopen+resave) call carried the exact same data as the first.
The payslip row (payslip_id=5, employee L1EMPA2) after BOTH saves: other_deductions=5000.0000,
other_deductions_reason='ทดสอบหักเงิน L1-edit-door', net_pay=24125.0000 -- a single, stable value,
not two different states. The "reopen and resave unchanged moves nothing" checklist item is
satisfied: the resave is a true no-op at the data level (it does re-run the full replace-and-
recompute path server-side, per source, but lands on byte-identical output).


## NOTE - test-design correction: period 202608 (current month) is OPEN by design, not closed
Attempted to use period 202608 (Aug 2026, PayDate 2026-08-20) as a "closed period" test case,
assuming no gl.accounting_periods row = closed. WRONG assumption -- checked PeriodCloseService.
IsOpenAsync source: "A missing row is now OPEN only for the CURRENT Asia/Bangkok month ... every
other missing month (a never-opened past month, or any future month) is CLOSED." System clock is
2026-08-18, so August 2026 IS the current month -> IsOpenAsync(2026,8) returns true even with zero
gl.accounting_periods rows. The browser test correctly posted run 3 (period 202608, doc
08-2026-PR-0001, journal 13) -- this is CORRECT behavior (a company that never explicitly opens
periods can still work in the current month), not a bug. Re-running the closed-period refusal
check against a genuinely future period (202609) below.


## PASS - posting into a closed/never-opened period refuses with a typed, bilingual error (browser-confirmed)
Created a new draft run for period 202609 (September 2026 -- genuinely future/closed: no
gl.accounting_periods row and not the current Bangkok month) via the UI, approved it via UI, then
clicked Post + confirmed. Result: NO "posted" success toast, status badge stayed "Approved" (never
flipped to "บันทึกบัญชีแล้ว" -- explicitly checked, isVisible()=false), and a red toast rendered
the exact typed DomainException message on screen:
  "งวดบัญชี 2026-09 ปิดแล้ว จึงลงบัญชีเงินเดือนไม่ได้ — เปิดงวดใหม่ก่อน (POST /periods/2026/9/reopen
  ต้องมีสิทธิ์ gl.period.close) แล้วลงบัญชีอีกครั้ง จากนั้นปิดงวดตามเดิม. [Period 2026-09 is closed.
  Reopen it via POST /periods/2026/9/reopen (needs gl.period.close), post, then close it again.]"
DB confirms: payroll_run_id=4, period_year_month=202609, status=APPROVED, doc_no=NULL,
journal_id=NULL -- no partial/orphaned GL write, clean refusal. Screenshot leg1-14. This is a
strong, correctly-typed guard with a genuinely actionable message (names the exact reopen route +
required permission). PASS.

(Earlier attempt with period 202608 was a test-design mistake, not a defect -- see the NOTE above:
Aug 2026 is the CURRENT month so IsOpenAsync's fail-open-for-current-month rule correctly let it
post; that run, id 3, is legitimately Posted with doc 08-2026-PR-0001.)


## PASS - permission guards (403/401), tested with a REAL low-privilege user (direct API, per directive's guard-check carve-out)
rbac_sales_staff (company-1 seeded RBAC test user; JWT `perm` claim decoded and confirmed to hold
ONLY sales/master.read-type scopes, zero payroll/employee grants) against payroll/employee endpoints:
- GET /payroll/runs -> 403
- POST /employees (empty body, permission checked before validation) -> 403
- POST /payroll/runs/2/approve -> 403
- POST /payroll/runs/2/pay -> 403
- GET /payroll/runs/2/pnd1/pdf -> 403 (rbac_sales_staff has neither payroll.run.manage nor
  tax.filing.preview)
No token at all -> GET /payroll/runs -> 401. Garbage/malformed bearer token -> 401. All typed,
no raw 500/crash.

## PASS - WP-H filing OR-permission gate confirmed BOTH directions with rbac_tax_officer
rbac_tax_officer (no payroll.run.manage, has tax.filing.preview per seed 627_seed_tax_officer_
filing_grant.sql): GET /payroll/runs/2/pnd1/pdf -> 200 (the CanFile OR-gate correctly admits a
filing-only role), but GET /payroll/runs (payroll administration list) -> 403 (RunManage-only,
correctly NOT widened by the filing grant). This is exactly the SoD split the WP-H doc comment in
PayrollEndpoints.cs describes, confirmed live for both the positive and negative case.

## PASS - malformed-payload error contract: every probe returns typed 400/404, never a raw 500
- periodYearMonth "20261" (5 digits) -> 400, field periodYearMonth, "validation.period".
- periodYearMonth "202613" (month 13) -> 400, same field/code.
- baseSalary -5000 on employee create -> 400, field baseSalary, "validation.min".
- nationalId 12 digits (one short) -> 400, field nationalId, "validation.nationalId".
- maritalStatus "WIDOWED" (not SINGLE/MARRIED) -> 400, field maritalStatus, "validation.required".
- malformed JSON syntax (`{"employeeCode":"X",,,}`) -> 400, urn:teas:error:validation_error,
  "A required or malformed query/route parameter was rejected." (ASP.NET model-binding guard,
  not a raw parser exception/500).
- empty request body -> 400, same validation_error contract.
- POST /payroll/runs/999999/approve (nonexistent id) -> 404, urn:teas:error:payroll.not_found,
  "Payroll run 999999 not found."
No 500s, no .NET stack traces, no unhandled exceptions anywhere in this probe set.

## PASS - employee national ID cannot be missing/malformed at the source (defense-in-depth vs. the filing-time gap)
CreateEmployeeValidator enforces `nationalId` must have exactly 13 digits (validation.nationalId) --
confirmed live above. This means the checklist's "employee tax IDs missing -> what happens at
filing?" scenario cannot actually arise through the API/UI: a blank or malformed national id is
rejected at employee-creation time, before it could ever reach Pnd1FilingService/SsoFilingService.
Good defense-in-depth, though note it is NOT a mod-11 checksum validator (only digit-COUNT is
checked), so a syntactically-valid-but-fake 13-digit id (e.g. all the same digit) is still accepted
-- not flagged as a defect (out of the O-series scope reviewed), just recorded as a boundary.


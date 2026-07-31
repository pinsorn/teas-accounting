# B5 — Payroll break-it (co7, NON-VAT dummy, prod v1.27.1)

Agent: **B5** · Company: **co7 id=7** (บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด) — `GET /api/proxy/me` → `companyId:7` re-confirmed before every write.
Drivers: **nvchief02** (`UxSwarm-2026-NV5`, CHIEF_ACCOUNTANT) for the run lifecycle, **nvadmin02** (`UxSwarm-2026-NV4`, COMPANY_ADMIN) for employee master. Both passwords in the briefing were correct.
Scope discipline: payroll lane only. No sales/purchase/expense documents touched. Sibling agents (A4/B3) were active on co7 concurrently; every figure below is measured from payroll-only GL accounts (2153/2160/2170/2180/5400/5410) or from run/filing artifacts, so sibling traffic cannot move them.

---

## ⚠️ TOP FINDINGS

1. **F1 (HIGH)** — `post` and `pay` post immutable JEs into an **explicitly CLOSED** accounting period. **REPRODUCED on co7** exactly as B4 found on co5. Two companies now.
2. **F2 (HIGH)** — the same gap has **no upper bound at all**: a payroll run for period **209912** posted a balanced, immutable JE dated **2099-12-31** into co7's ledger. 73 years forward, zero warnings.
3. **F3 (HIGH, NEW)** — a deduction with **more than 2 decimals** is accepted and **sub-satang amounts reach the GL, the bank account and the trial balance**, while the payslip PDF rounds to 2dp → **the payslips no longer foot to the ledger** (0.001 out on one employee; unbounded in aggregate).
4. **F5 (HIGH, NEW)** — **official RD/SSO filings are generated from an unapproved, unposted DRAFT run**: ภ.ง.ด.1, สปส.1-10 upload file + PDF and payslips all render for a draft that has **no journal entry at all** and can still be edited or deleted.
5. **F7 (MEDIUM, NEW — root cause of a known symptom)** — any character outside TIS-620 in an employee name is **silently replaced with `?`** in the สปส.1-10 e-Service upload file and **silently dropped** from the ภ.ง.ด.1 ใบแนบ PDF. Proven live.

No HTTP 500 from application code. No Dr≠Cr on any posted JE. No negative payslip. No cross-tenant leak.

---

## Sub-area verdicts (one line each)

| # | Area | Verdict |
|---|---|---|
| R1 | create→calc→approve→post→pay, tie to independent hand-calc | **PASS** — every figure exact to the satang |
| R1 | post JE balances & ties (Dr 5400+5410 = Cr 2153+2160+2170+2180) | **PASS** |
| R1 | Pay settlement JE clears 2170 to zero + moves bank | **PASS** |
| R1 | ภ.ง.ด.1 (form + ใบแนบ) vs run | **PASS** — 5 ราย / 150,000.00 / 1,483.92 |
| R1 | ภ.ง.ด.1ก 2026 vs all posted runs | **PASS** — 382,258.07 / 2,406.29 |
| R1 | สปส.1-10 (upload file + ส่วนที่ 1 PDF + on-screen schedule) vs run | **PASS** — 150,000 / 3,250 / 3,250 / 4 คน |
| R1 | payslip + 50ทวิ vs run | **PASS** |
| R1 | SSO cap question (750 vs 875) | **PASS** — engine uses ceiling 17,500 → **฿875** cap; ฿750 only for a 15,000 wage. Correct for the configured ceiling. |
| R2 | mid-month HIRE proration (O8) | **PASS** — 15/30 days → 15,000 of 30,000. **O8 is FIXED on co7 too** |
| R2 | mid-month LEAVER proration (O8) | **PASS** — same |
| R2 | zero-salary employee | **PASS** — clean all-zero payslip, no garbage line in the JE |
| R2 | deduction > net / negative / zero / empty reason | **PASS** — 400 each |
| R2 | duplicate employee line in one run | **PASS** — 400 |
| R2 | ghost employee id / cross-company employee id in deductions | **PASS** — 400, no leak |
| R2 | exact-max deduction | **PASS** — 204, net lands exactly 0, never negative |
| R2 | two runs in the same period (dup-guard) | **PASS** — 422 `payroll.duplicate_period` |
| R2 | invalid period strings (`202613`/`202600`/`abcdef`) | **PASS** — 400 `validation.period` |
| R2 | delete / edit / re-approve / re-post a POSTED run | **PASS** — 422/400 each |
| R2 | approve-then-edit | **PASS** — refused |
| R2 | **post into a CLOSED period** | **FAIL → F1 (HIGH)** |
| R2 | **far-future period (209912)** | **FAIL → F2 (HIGH)** |
| R2 | pay twice on a posted run | **PASS** — 422 `payroll.already_paid`, no duplicate settlement JE |
| R2 | double-post race (N=6 concurrent) | **PASS** — 1×204, 5×clean 422, **zero 500**, posted exactly once |
| R2 | **ภ.ง.ด.1 PIT column vs GL 2153 movement** | **PASS** — 1,483.92 (June) and 2,406.29 (FY2026) both tie exactly |
| R2 | สปส.1-10 employer account `0000000000` | **FAIL → F6 (MEDIUM)** — reproduced |
| R2 | SSO filing names as `?????` | **FAIL → F7 (MEDIUM)** — real server-side defect, isolated & proven |
| R2 | **>2-decimal amounts reaching the GL** | **FAIL → F3 (HIGH)** — reproduced and proven end-to-end |
| R2 | filings from a DRAFT run | **FAIL → F5 (HIGH)** |
| R2 | payslip YTD frozen vs 50ทวิ / ภ.ง.ด.1ก | **FAIL → F4 (HIGH)** — contradiction reproduced |
| R2 | filing endpoints w/ ghost run / ghost employee / cross-tenant employee / empty year | **PASS** — 404/422, no leak, no 500 |
| R2 | employee master: negative salary, termination-before-hire | **PASS** — 400 / 422 |
| R2 | employee master: `childrenCount` upper bound | **FAIL → F8 (MEDIUM)** |

---

## Which co5 (B4) findings REPRODUCED on co7

| co5 finding | co7 result |
|---|---|
| **CONFIRMED CRIT** — post/pay are the only posting paths with **no period-close guard** | **REPRODUCED** (F1). June 2026 is an *explicitly closed* period row on co7 (`{"open":false}`), and both post and pay returned 204. |
| **no future-date guard** either | **REPRODUCED and EXTENDED** (F2). B4 reached Dec 2026; co7 reached **Dec 2099**. |
| **O8 proration is FIXED** | **REPRODUCED as fixed.** Mid-month hire *and* mid-month leaver both prorate 15/30 on a different seed set. Also visible in co7's pre-existing July run (5400 = 112,258.07 = 60,000 + 17/31 + 10/31 of 60,000). |
| **double-post race is ROBUST** (1 winner, clean 422s, zero 500) | **REPRODUCED as robust.** N=6 → 1×204 + 5×422, zero 500, one JournalId, one docNo. |
| **Payslip YTD frozen at run creation**, can contradict 50ทวิ / ภ.ง.ด.1ก | **REPRODUCED with a hard numeric contradiction** (F4) — see below. |
| SSO cap is **฿875** (ceiling 17,500), not 750 | **REPRODUCED.** |
| สปส.1-10 employer account `0000000000` | **REPRODUCED** (F6). |
| SSO files rendering names as `?????` | **PARTLY REFUTED, PARTLY CONFIRMED** (F7/F9) — the `???` on co7's legacy `O8*` employees is **client-side** corruption baked into the stored data (I proved Thai round-trips byte-perfect through the API). But a **genuine server-side** `?`-corruption exists for any non-TIS-620 character, proven live. |
| `>2-decimal amounts reaching the GL` (listed as "known elsewhere") | **REPRODUCED and proven end-to-end on the payroll path** (F3). |

**Divergence from co5: none in behaviour.** co7 is non-VAT and payroll is VAT-agnostic; every shared control behaved identically. F2/F3/F5/F7/F8 are new *tests*, not new *divergences* — they would very likely reproduce on co5 too.

---

## F1 — Payroll Post + Pay bypass the closed-period control (HIGH)

- **Severity:** HIGH (ledger-integrity / compliance control gap). Not auto-CRIT — JEs balance, no 500.
- **Endpoints:** `POST /api/proxy/payroll/runs/{id}/post`, `POST /api/proxy/payroll/runs/{id}/pay`.
- **Root cause (code, re-verified):** `Accounting.Infrastructure/Payroll/PayrollRunService.cs` — `PostAsync` (~L200) and `PayAsync` (~L228) never call `IPeriodCloseService.EnsureOpenAsync`, and the service does not inject `IPeriodCloseService` at all. Both post through `GlPostingService.BuildAndPostAsync`, which has no period guard. Every other poster does guard (`JournalService`, `ExpenseClaimService`, `FixedAssetService`, `BankReconciliationService`).

### Exact repro
```
GET  /api/proxy/periods/2026/6/status              → {"open":false}      # June explicitly CLOSED
POST /api/proxy/payroll/runs {"periodYearMonth":"202606","payDate":"2026-06-29"}  → 201  (run 16)
PUT  /api/proxy/payroll/runs/16/deductions {...}   → 204
POST /api/proxy/payroll/runs/16/approve            → 204
POST /api/proxy/payroll/runs/16/post               → 204   ← should be 422 period.closed
POST /api/proxy/payroll/runs/16/pay {}             → 204   ← settlement JE also into closed June
```
### Observed
- **JE 298** `06-2026-JV-0001`, docDate **2026-06-29**, D=C=153,250.00 — in CLOSED June.
- **JE 300** `06-2026-JV-0002`, docDate **2026-06-29**, Dr 2170 145,165.951 / Cr 1120 145,165.951 — in CLOSED June.
- Both immutable (delete → 422 `payroll.not_draft`; no void path).
### Expected vs actual
- **Expected:** `422 period.closed`, same as every other posting path.
- **Actual:** `204`, immutable JEs minted into a closed, potentially already-filed month.

### Remediation note (please read before fixing)
On co7 the **only** open period is the current Bangkok month (July 2026), and July **already has a payroll run** — the dup-period guard is company-scoped and permanent. So the moment `EnsureOpenAsync` is added to `post`, **co7 becomes unable to run payroll at all**, for any month. The absence of the guard is the only reason payroll is usable here. A fix therefore needs a companion: either a period-reopen flow reachable by the payroll role, or (better) a payroll-specific rule that permits the *period being paid* rather than the calendar month of `PayDate`. Shipping the guard alone will brick payroll on every company whose target month is closed.

---

## F2 — Post accepts a period 73 years in the future (HIGH)

- **Severity:** HIGH. Same missing-guard family as F1, but it shows there is **no bound of any kind** — not "one or two months of drift", but the entire date domain.
- **Repro:**
```
POST /api/proxy/payroll/runs {"periodYearMonth":"209912","payDate":"2099-12-31"}  → 201  (run 17)
POST /api/proxy/payroll/runs/17/approve                                            → 204
POST /api/proxy/payroll/runs/17/post                                               → 204
```
- **Observed:** run 17 `12-2099-PR-0001`; **JE 301 `12-2099-JV-0001`, docDate 2099-12-31**, D=C=152,625.00 (Dr 5400 150,000 + 5410 2,625 = Cr 2160 5,250 + 2170 147,375). Immutable. `GET /payroll/pnd1a/pdf?year=2099` renders a full ภ.ง.ด.1ก for it (500 KB).
- **Contrast:** the manual JV path pins docDate to Bangkok-today and rejects future dates (`JournalService.cs`). Payroll has neither control.
- **Expected:** reject (or at minimum warn on) a period outside a sane window around today.
- **Actual:** 204, permanent ledger artifact dated 2099.
- **Secondary observation (not filed separately):** a December-only period computes `monthsRemaining = 1`, so the ม.50(1) projection produces **PIT 0.00 for every employee** including a ฿60,000/month earner. Arithmetically correct for a single-month tax year, but it means a mis-keyed far-future/December period also silently withholds nothing.

---

## F3 — Sub-satang (>2-decimal) deduction reaches the GL, the bank and the TB; payslips stop footing (HIGH, NEW)

- **Severity:** HIGH — money/ledger integrity. Small magnitude in this repro, but there is **no validation at all**, so the error is unbounded across employees and runs, and it silently desynchronises the employee-facing document from the books.
- **Repro:**
```
PUT /api/proxy/payroll/runs/16/deductions
    {"deductions":[{"employeeId":16,"amount":100.129,"reason":"หักค่าชุดพนักงาน (ทดสอบทศนิยม 3 ตำแหน่ง)"}]}
                                                    → 204   ← should be 400
```
- **Observed after post:**
  - Payslip record: `otherDeductions 100.1290`, `netPay 14149.8710`
  - Run totals: `totalOtherDeductions 100.129`, `totalNet 145165.951`
  - **GL JE 298 line 5: Cr 2170 = 145,165.951**; **line 6: Cr 2180 = 100.129** (`journal_lines` is `numeric(19,4)` — the third decimal is stored, not rounded away)
  - **Settlement JE 300: Cr 1120 (bank) = 145,165.951** — a bank movement that cannot exist
  - **TB asOf 2026-12-31:** 1120 net `-49,554.951`, 2180 net `600.129`, TB totals `Dr 664,740.031 = Cr 664,740.031`
  - **Payslip PDF** for the same employee prints **`-100.13`** and **`14,149.87`**, with the Thai amount-in-words *"…แปดสิบเจ็ดสตางค์"*.
- **The break:** sum of the five printed payslip nets = **145,165.95**; GL 2170 credit = **145,165.951**. **The payslips do not foot to the journal entry.** Whichever one is right, the other is a document the company hands to an employee or an auditor.
- **Expected:** reject any deduction amount with more than 2 decimals (or round at the boundary, consistently, in one place).
- **Actual:** accepted verbatim, propagated to GL / bank / TB, silently rounded only at the print layer.

---

## F4 — Payslip YTD frozen at creation contradicts 50ทวิ and ภ.ง.ด.1ก (HIGH)

- **Severity:** HIGH — two RD-facing / employee-facing documents from the same system state different year-to-date figures for the same person and year.
- **Root cause (code):** `PayrollRunService.CreateDraftAsync` snapshots `YtdIncome`/`YtdPit` onto the payslip at *creation* time (`LoadYtdAsync` sums **prior-period posted runs only**). It is never recomputed. 50ทวิ / ภ.ง.ด.1ก aggregate **all** posted runs in the year, live.
- **Exact repro:** co7 already had posted runs for **202607** and **202608**. I created and posted a run for **202606** (a *prior* period) afterwards.
- **Observed for O8FULL (employeeId 10), tax year 2026:**

| Document | YTD income | YTD PIT |
|---|---|---|
| July payslip (run 10, stored `ytdIncome`/`ytdPit`) | **60,000.00** | **372.92** |
| August payslip (run 11) | **120,000.00** | **745.84** |
| **50ทวิ 2026 PDF** (`/payroll/employees/10/wht50tawi/pdf?year=2026`) | **180,000.00** | **1,487.80** |

  → the August payslip is **60,000.00 income and 741.96 tax short** of the 50ทวิ the same system issues for the same employee and year. Neither July nor August ever mentions June.
- **Second-order consequence (worse than the display mismatch):** ม.50(1) withholding is a function of YTD. July and August were computed against a YTD that did not include June, so their PIT is under-computed and can never be corrected — runs are immutable and there is no recompute, no warning, and no block on inserting a prior-period run after later ones are posted.
- **Expected:** either recompute payslip YTD on read, or refuse to create a run for a period earlier than the latest posted run, or at minimum warn.
- **Actual:** silent, permanent contradiction.

---

## F5 — Official RD/SSO filings render from an unapproved, unposted DRAFT run (HIGH, NEW — B4 did not test this)

- **Severity:** HIGH — a signable government return can be produced from money that does not exist in the ledger, from a document that can still be edited or deleted afterwards.
- **Repro (run 18 was a DRAFT, never approved, never posted, `journalId: null`):**
```
GET /api/proxy/payroll/runs/18/pnd1/pdf         → 200, 307,951 bytes
GET /api/proxy/payroll/runs/18/sso/file         → 200, 548 bytes
GET /api/proxy/payroll/runs/18/sso/pdf          → 200, 252,555 bytes
GET /api/proxy/payroll/runs/18/payslips/16/pdf  → 200,  37,707 bytes
DELETE /api/proxy/payroll/runs/18                → 204     ← the run behind that filing is now gone
```
- **Observed:** the draft ภ.ง.ด.1 is a complete, fillable-and-signable form: **4 ราย / 150,000.00 / PIT 1,103.02** in boxes 1, 6 and 8. GL account 2153 has **no** corresponding movement — the JE does not exist. The สปส.1-10 upload file is likewise complete and uploadable.
- **Expected:** filing endpoints gated on `Status == Posted` (payslips arguably on `Approved`).
- **Actual:** no status check anywhere in `PayrollEndpoints.cs` filing routes — only the RBAC assertion.

---

## F6 — สปส.1-10 ships employer SSO account `0000000000` (MEDIUM)

- **Repro:** `GET /api/proxy/company-profile` → `"ssoEmployerAccountNo": null`. Then `GET /payroll/runs/16/sso/file`:
```
1 0000000000 000000 290669 0669 บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด ... 0500 000004 ...
  ^^^^^^^^^^ field 2 = เลขที่บัญชีนายจ้าง
```
  and the ส่วนที่ 1 PDF renders the account-number boxes as `0 0 0 0 0 0`.
- **Root cause:** `SpsBatchFormat.Build` documents "blank → zeros" as intended behaviour; nothing upstream refuses to emit the artifact when the employer has no SSO registration number.
- **Expected:** refuse to build the upload file / PDF (422 with a "configure ssoEmployerAccountNo first" message).
- **Actual:** a syntactically valid, semantically invalid filing artifact that SSO e-Service will reject — with no indication to the user.

---

## F7 — Non-TIS-620 characters in names silently become `?` in สปส.1-10 and vanish from ภ.ง.ด.1 (MEDIUM, NEW — this is the real `?????` mechanism)

- **First, the refutation:** the API does **not** mangle Thai. I created `B5HIRE` with `--data-binary @file.json` (UTF-8) and read back `titleTh "นาย"`, `lastNameTh "เข้ากลางเดือน"`, `street "ถนนสุขุมวิท"` byte-perfect. co7's `O8FULL/O8MID/O8OUT` carry a literal `"???"` in `titleTh` because the script that created them passed Thai through a non-UTF-8 shell. I reproduced that failure mode myself (an inline `curl -d '{"titleTh":"นาย"...}'` stored `???`), which is the known troubles-wiki entry. **Not a server bug.**
- **But there IS a server-side one.** Created `B5CJK` with `firstNameTh: "陳大文"`, `lastNameTh: "สমชาย"` (Thai ส + **Bengali ম** + ชาย — the exact ม/ম glyph pitfall in the wiki), then pulled the SSO upload file:
```
21103700000119001???                           ส?ชาย
                  ^^^ firstName 陳大文        ^ Bengali ম
QUESTION MARKS in file: 4
```
- **Root cause:** `SpsBatchFormat.BuildBytes` does `Encoding.GetEncoding(874).GetBytes(...)`. Any codepoint outside TIS-620/Windows-874 — CJK, Bengali, Devanagari, Vietnamese, emoji — becomes `?`. There is **no pre-validation and no warning**.
- **And the ภ.ง.ด.1 ใบแนบ is worse:** the same employee renders on the PDF as `นาย … ชาย` — the CJK given name and the Thai `ส` + Bengali `ม` are **silently dropped**, producing a *wrong* name on an RD return rather than a visibly broken one.
- **Expected:** validate employee names against the TIS-620 repertoire at save time (or at filing build time), and refuse rather than corrupt.
- **Actual:** silent corruption of a government filing.

---

## F8 — `childrenCount` has no upper bound → a ฿100,000/month employee withholds ฿0 PIT (MEDIUM)

- **Repro:**
```
POST /api/proxy/employees {"employeeCode":"B5KIDS", ..., "baseSalary":100000,
                           "maritalStatus":"MARRIED","spouseHasIncome":false,"childrenCount":999}  → 201
```
  then a draft run for 202609 (created, inspected, **deleted** — no GL impact):

| Employee | gross | PIT withheld |
|---|---|---|
| **B5KIDS** (999 children) | **100,000.00** | **0.00** |
| O8FULL (0 children) | 60,000.00 | 926.49 |

- **Arithmetic:** `PayrollAllowanceRates.Annual` = 60,000 + 60,000 + **999 × 30,000** = **30,180,000** of ค่าลดหย่อน → projected net income is negative → tax 0. With a defensible child count the same employee would withhold ≈ **฿331.25/month**.
- **Validator (`EmployeeDtos.cs`, `EmployeeRules.Common`):** `children.GreaterThanOrEqualTo(0)` — floor only, no ceiling.
- **Expected:** a sane upper bound (RD practice caps the child allowance), or at least a warning band.
- **Actual:** a single typo in a master-data field silently zeroes an employee's withholding for the whole year, and it flows straight onto ภ.ง.ด.1 / ภ.ง.ด.1ก / 50ทวิ.

---

## F9 — Master-data validation gaps that reach RD filings verbatim (LOW)

- `nationalId` is validated **only** on digit count (`…Where(char.IsDigit).Count() == 13`) — no Thai NID checksum. `1103700000029` (checksum-invalid) is stored and printed on ภ.ง.ด.1 ใบแนบ and in the สปส.1-10 file.
- Names are not validated at all: `POST /employees {"titleTh":"???","firstNameTh":"???","lastNameTh":"???"}` → **201**. Those `???` then print verbatim in the **name column of the official ใบแนบ ภ.ง.ด.1** (confirmed on page 3 of `pnd1-16.pdf`, rows 4 and 5) and in the on-screen สปส.1-10 schedule.
- Guards that **do** work: negative `baseSalary` → 400; `terminationDate` before `hireDate` → 422 `employee.termination_before_hire`.

---

## F10 — Two Cloudflare 520s on POST through the public domain (INFO / environment, not a code defect)

- `POST /api/proxy/payroll/runs/16/post` and `POST /api/proxy/employees` each returned **HTTP 520** once, ~0.3 s, body `error code: 520`. Both were immediately non-reproducible (3× retry of the same call → clean 422/201).
- This is the **known** CF-edge 5xx class already documented in `troubles-wiki.md` (§"authenticated browser session gets 503 …", root-caused 2026-07-19 to an intermittent CF-edge↔origin connection race, no fix applied). Logged here only because it now demonstrably hits **write** endpoints via curl, not just the browser.
- **Verified no partial write in either case:** run 16 was still `Draft` after the 520 (the subsequent `approve` succeeded, which requires Draft), and the `B5CJK` employee was absent from `GET /employees?includeInactive=true` after the 520 (I re-created it successfully on retry). Consistent with the wiki's "re-read state before retrying" rule.

---

## R1 happy-path tie-out (period 202606, run 16) — PASS

Independent hand-calc, re-derived from `ThaiPitCalculator` / `SsoContribution` / `SalaryProration` / `PitSchedule.Current()` and the prod `appsettings.json` (SSO 5%, floor 1,650, **ceiling 17,500 → cap ฿875**, `MaxAllowanceForPit` 10,500; allowances 60k/60k/30k; June ⇒ `monthsRemaining = 13 − 6 = 7`, 30 days), computed **before** reading the API response. Every figure matched to the satang:

| Employee | days | gross | SSO emp | PIT (hand = live) | net |
|---|---|---|---|---|---|
| O8FULL (60,000) | 30/30 | 60,000.00 | 875.00 | **741.96** | 58,383.04 |
| O8OUT (60,000, term 2026-07-10) | 30/30 | 60,000.00 | 875.00 | **741.96** | 58,383.04 |
| B5HIRE (30,000, **hired 2026-06-16**) | **15/30** | **15,000.00** | 750.00 | 0.00 | 14,250.00 → 14,149.871 after deduction |
| B5LEAVE (30,000, **term 2026-06-15**) | **15/30** | **15,000.00** | 750.00 | 0.00 | 14,250.00 |
| B5ZERO (0) | 30/30 | 0.00 | 0.00 | 0.00 | 0.00 |
| **Run** | | **150,000.00** | **3,250.00** | **1,483.92** | **145,165.951** |

O8FULL PIT worked longhand: SSO allowance `min(875 × 7, 10,500) = 6,125`; allowances `60,000 + 6,125 = 66,125`; projected `60,000 × 7 = 420,000`; ม.42ทวิ expense `min(210,000, 100,000) = 100,000`; net `253,875`; annual tax `(253,875 − 150,000) × 5% = 5,193.75`; monthly `5,193.75 / 7 = 741.96`. ✓

**Post JE 298** (`06-2026-JV-0001`, docDate 2026-06-29):
`Dr 5400 150,000.00 + Dr 5410 3,250.00 = 153,250.00` = `Cr 2153 1,483.92 + Cr 2160 6,500.00 (=3,250 emp + 3,250 er) + Cr 2170 145,165.951 + Cr 2180 100.129`. Balanced, ties to run totals.

**Pay settlement JE 300** (`06-2026-JV-0002`): `Dr 2170 145,165.951 / Cr 1120 145,165.951`. Account 2170 nets to **exactly 0** for June; bank moves by net. (One active bank → auto-resolved, no `bankAccountId` needed.)

**Filings, all rendered and all tied:**
- **ภ.ง.ด.1** (`pnd1/pdf`, 3 pages) — box 1 and box 6: `5 ราย / 150,000.00 / 1,483.92`; box 8: `1,483.92`. Verified by glyph coordinates, not by `pdftotext -layout` (which mis-associates overlay text with the wrong printed label and made boxes 2 and 5 *look* populated — they are not).
- **ใบแนบ ภ.ง.ด.1** (page 3) — all 5 employees with NID, date `29/06/69`, income and tax; footer total `150,000.00 / 1,483.92`.
- **ภ.ง.ด.1ก 2026** — `382,258.07 / 2,406.29` = the sum of all three posted 2026 runs (150,000.00+112,258.07+120,000.00 and 1,483.92+372.92+549.45). ✓
- **สปส.1-10** — upload file header + 4 detail records; ส่วนที่ 1 PDF: ค่าจ้าง `150,000.00`, ผู้ประกันตน `3,250`, นายจ้าง `3,250`, รวม `6,500`, `4` คน; on-screen schedule `periodYearBE 2569`. B5ZERO correctly excluded (zero wage). B5QQQ probe confirmed the **wage floor** works: ฿1,000 salary → SSO ฿82.50 (= 1,650 × 5%), not ฿50.
- **payslip** (B5HIRE) — matches the run except for the F3 rounding.
- **50ทวิ** — B5HIRE `15,000.00 / 0.00 / SSO 750.00`; O8FULL `180,000.00 / 1,487.80 / SSO 2,625.00`.

**ภ.ง.ด.1 vs GL tie (the headline reconciliation) — PASS.** Full-year GL account 2153 (`ภ.ง.ด.1 หัก ณ ที่จ่ายค้างนำส่ง`), 2026-01-01 → 2026-12-31: three credits, `1,483.92 + 372.92 + 549.45 = **2,406.29**` = ภ.ง.ด.1ก's PIT column exactly. Every monthly ภ.ง.ด.1 equals its own JE's 2153 line.

---

## co7 write footprint (full transparency)

**Employees created** (soft-deactivate only — permanent rows):
- `B5HIRE` id 16 — 30,000, hired 2026-06-16 — **left ACTIVE** (payroll test fixture, like B4's on co5)
- `B5LEAVE` id 17 — 30,000, terminated 2026-06-15 — **left ACTIVE** but self-excluding from all future periods
- `B5ZERO` id 18 — salary 0 — **left ACTIVE**
- `B5QQQ` id 19 (`???` name probe) — **deactivated**
- `B5KIDS` id 20 (999-children probe) — **deactivated**
- `B5CJK` id 21 (TIS-620 encoding probe) — **deactivated**

**Payroll runs posted (2):**
- **run 16 · 202606 · POSTED + PAID** — into **explicitly closed June 2026** (F1 proof). Only one closed 2026 period was used, as instructed.
- **run 17 · 209912 · POSTED** (not paid) — F2 proof, and the vehicle for the double-post race.

**Payroll runs created and DELETED (no ledger impact):** run 18 (202609, F5 draft-filing proof), run 19 (202609, F8 children proof), runs 20 & 21 (202610, F7 encoding proof).

**JEs minted (3), all immutable:**
- **298** `06-2026-JV-0001` docDate 2026-06-29 — D=C=153,250.00 (closed June)
- **300** `06-2026-JV-0002` docDate 2026-06-29 — D=C=145,165.951 (closed June, settlement)
- **301** `12-2099-JV-0001` docDate 2099-12-31 — D=C=152,625.00

**Net effect on co7's FY2026 payroll accounts:** 2153 `+1,483.92`, 2160 `+6,500.00`, 2170 `+0` (posted then settled), 2180 `+100.129`, 5400 `+150,000.00`, 5410 `+3,250.00`, 1120 `−145,165.951`. TB still balances (`Dr 664,740.031 = Cr 664,740.031`) — with a permanent `.031` sub-satang tail from F3.

No sales, purchase, expense, VAT or period-close state was touched. No source file edited, no commit made.

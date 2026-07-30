# B4 — Payroll break-it (co5, VAT dummy, prod v1.27.1)

Agent: **B4** · Company: **co5 id=5** (บริษัท ทดสอบ VAT (DUMMY)) — `GET /api/proxy/me` → `companyId:5` re-confirmed before every write.
Drivers: **chief01** (`UxSwarm-2026-A7`, CHIEF_ACCOUNTANT — payroll.run.manage+post+pay), **tax01** (`UxSwarm-2026-B1`, tax.filing.preview) for filing-RBAC checks.
NOTE: task briefing password `UxSwarm-2026-chief` is WRONG (401); real suffix = agent CODE (A7). Same as A5's finding.

---

## ⚠️ TOP FINDING (severity HIGH, not CRIT-by-trigger) — F1: Payroll Post AND Pay post JEs into a CLOSED accounting period

Payroll is the **only** posting path in the system that does **not** call `IPeriodCloseService.EnsureOpenAsync`. A posted payroll JE (and its settlement JE) can be injected into an explicitly-closed month, silently mutating already-closed / already-filed financials. **Immutable JEs — cannot be undone.** Not one of the four enumerated auto-CRIT triggers (no 500, JEs balance, no negative, no cross-company), so filed as **HIGH**, but it is the headline defect.

---

## Sub-area verdicts (one line each)

| Area | Verdict |
|---|---|
| **R1** create→calc→approve→post→pay, tie to hand-calc | **PASS** — every number ties (below) |
| **R1** post JE balances & ties (Dr 5400+5410 = Cr 2153+2160+2170) | **PASS** |
| **R1** Pay settlement JE clears 2170 + moves bank | **PASS** |
| **R1** ภ.ง.ด.1 / ภ.ง.ด.1ก / สปส.1-10 / payslip / 50ทวิ vs run | **PASS** — all tie, all render |
| **R2** mid-month hire AND leaver (O8 proration) | **PASS** — O8 is FIXED; briefing's "no proration" is STALE |
| **R2** deduction > net pay | **PASS** — refused 400 |
| **R2** negative adjustment | **PASS** — refused 400 |
| **R2** zero-salary employee | **PASS** — clean zero payslip, balanced JE, not garbage |
| **R2** two runs same period (dup-guard) | **PASS** — 422 duplicate_period |
| **R2** delete / edit a POSTED run (direct API) | **PASS** — 422 not_draft / 400 |
| **R2** post into a closed period | **FAIL → F1 (HIGH)** |
| **R2** approve then edit the run | **PASS** — refused |
| **R2** double-post race (F1-class 500?) | **PASS** — clean, no 500, posted once (13 concurrent attempts) |
| **R2** ภ.ง.ด.1 vs GL tie after all | **PASS** — filing PIT column = 2153 movement every run |
| **R2** pay twice on a posted run | **PASS** — 422 already_paid, no dup settlement JE |

No HTTP 500. No Dr≠Cr on any posted run. No negative/garbage JE. No cross-company data.

---

## F1 — Payroll Post + Pay bypass the closed-period control (HIGH)

- **Severity:** HIGH (compliance / ledger-integrity control gap). Not auto-CRIT (JEs balance, no 500).
- **Endpoints:** `POST /api/proxy/payroll/runs/{id}/post` and `.../{id}/pay`.
- **Root cause (code-confirmed):** every other poster calls `IPeriodCloseService.EnsureOpenAsync(docDate)` before posting — `JournalService.cs:265`, `ExpenseClaimService.cs:252`, `FixedAssetService.cs:238/300`, `BankReconciliationService.cs:236`. `PayrollRunService.PostAsync`/`PayAsync` (Infrastructure/Payroll/PayrollRunService.cs) call **neither**, and the service does not even inject `IPeriodCloseService`. Both post through `GlPostingService.BuildAndPostAsync`, which has **no** period guard. Payroll also has **no** `DocDate > today` guard (the JV path has one at `JournalService.cs:158`), so future periods are postable too.
- **Also note:** `IsOpenAsync` (PeriodCloseService.cs:25-42) treats **any future month with no explicit period row as CLOSED** (only the current Bangkok month is open). So Oct/Nov/Dec 2026 are all "closed" — yet payroll posted into all of them.

### Exact repro (explicit closed period — the textbook case)
```
GET  /periods/2026/6/status                      → {"open":false}   (June explicitly closed, seed-400)
POST /payroll/runs {"periodYearMonth":"202606","payDate":"2026-06-29",...}   → 201  (run id 14)
POST /payroll/runs/14/approve                    → 204
POST /payroll/runs/14/post                       → 204   ← should be period.closed
POST /payroll/runs/14/pay {}                      → 204   ← settlement JE also into closed June
```
### Observed
- Post JE **270**: `docNo 06-2026-JV-0001`, **docDate 2026-06-29**, D=C=158375.00, in CLOSED June. Dr 5400=155000 / Dr 5410=... / Cr 2153/2160/2170 — balanced.
- Settlement JE **271**: `06-2026-JV-0002`, **docDate 2026-06-29**, Dr 2170 144132.14 / Cr 1120 144132.14, in CLOSED June.
- Also reproduced into future-gap-closed Oct (JE 249, docDate 2026-10-29) and Nov (JE 267) during R1/R2.
### Contrast that proves the asymmetry
A manual JV's date is pinned to Bangkok-today (§10, JournalService.cs:48-58) and the dated-JV path rejects future dates + enforces period-open (line 158-160). JVs **cannot** reach a closed period; payroll reaches any period, past-closed or future.
### Expected vs actual
- Expected: `POST /payroll/runs/14/post` → `422 period.closed` (as every other poster gives for a closed docDate).
- Actual: `204`, immutable JE minted in a closed, potentially already-filed month.

---

## R1 happy-path tie-out (period 202610, run id 12) — PASS

Independent hand-calc (re-implemented ThaiPitCalculator + SsoContribution + allowances + O8 proration; config: SSO 5% floor 1650 **ceiling 17500 → cap ฿875/mo**, MaxAllowanceForPit 10500, allowances 60k/60k/30k) matched the live draft **to the satang**:

| Emp | salary | gross | PIT (hand=live) | SSO | net |
|---|---|---|---|---|---|
| EMP001 | 80000 | 80000 | **7008.33** | 875 | 72116.67 |
| EMP002 | 30000 | 30000 | 0 | 875 | 29125.00 |
| EMP003 | 15000 | 15000 | 0 | 750 | 14250.00 |
| **run** | | **125000** | **7008.33** | 2500/2500 | **115491.67** |

- **Post JE 249** (`10-2026-JV-0001`): Dr 5400 125000 + Dr 5410 2500 = **127500** = Cr 2153 7008.33 + Cr 2160 5000 (=ssoEmp2500+ssoEr2500) + Cr 2170 115491.67. Balanced, ties to run totals.
- **Pay settlement JE 253** (`10-2026-JV-0002`): Dr 2170 115491.67 / Cr 1120 115491.67 — 2170 nets to zero, bank moves by net. (1 active bank → auto-resolved.)
- **ภ.ง.ด.1** (pnd1/pdf): 3 employees, income 125,000.00, **PIT 7,008.33 = 2153 movement = TotalPit**. ✓
- **สปส.1-10** (sso-schedule JSON): totalWage 125000, empContrib **2500**, erContrib **2500**; per-emp 875/875/750 (caps correct). periodYearBE **2569**. ✓
- **50ทวิ** (EMP001, year 2026): income **320,000** (4 in-system posted months × 80k) + PIT **22,433.33** (sum of Jul–Oct in-system PIT) — correctly EXCLUDES opening-YTD (payment-year, in-system basis). ✓
- All PDFs render (`%PDF`, 139–497 KB); **payslip PDF correctly 403 for tax01** (gated on RunManage only, not the filing scope) and 200 for chief.

## R2 — O8 proration (period 202611, run id 13) — PASS (briefing STALE)

O8 day-based proration was **implemented 2026-07-26** (commit range around obs 19898; Codex-reviewed). Verified live: created B4HIRE (hired **2026-11-16**) and B4LEAVE (terminated **2026-11-15**), both 30000/mo:

- Both prorated to **gross 15000** (15/30 days), **NOT** full 30000. B4ZERO (salary 0) → clean all-zero payslip.
- Flows correctly into GL: post JE 267 salary (5400) = **155000** (prorated total), not 185000 (full). **No over-statement** — the gap the briefing warned about does not exist. ภ.ง.ด.1 PIT for Nov = 7008.34 = 2153.

## R2 — immutability / dup / deduction / race — PASS

- Delete posted → 422 `payroll.not_draft`; edit-deductions on posted → 400; re-approve/re-post posted → 422; **pay-twice → 422 `payroll.already_paid` (no dup settlement JE)**; second run same period → 422 `payroll.duplicate_period`.
- Deductions (draft run 13): negative → 400; zero → 400; > net (14251 vs max 14250) → 400 `deduction_exceeds_net`; dup employee lines → 400; deduction on zero-salary emp (max 0) → 400; ghost employee 99999 → 400; **exact max 14250 → 204, net→0 exactly (never negative)**; reset restores.
- Approve-then-edit / delete / re-approve → all refused.
- **Double-post race**: FIFO-barrier concurrent `/post` at N=5 and N=8 on Approved runs → **1 winner (204) + all losers clean 422 `payroll.not_approved`, ZERO 500**, each run posted exactly once (one JournalId, one docNo). Payroll's post path did NOT reproduce A5's journal-post raw-500 (F1) — positive divergence, robust across 13 concurrent attempts. (Same `IConcurrencyVersioned`+status-guard pattern, so the theoretical TOCTOU exists; I could not trigger it — losers consistently re-read post-commit state.)

---

## Secondary observations (not defects)

- **SSO cap**: briefing's "cap 750" is stale for the config — live ceiling is **฿17,500 → ฿875/mo**; 750 only appears for sub-ceiling salaries (the 15k earner). Verified vs appsettings + live payslips.
- **Manual JV docDate is pinned to Bangkok-today** by design (§10). My two JV probes dated 2026-10-15 / 2026-06-15 stored as 2026-07-31 and posted into open July — expected, not a bug. This is the control payroll is MISSING.
- **Historical Jul/Aug/Sep opening-YTD inconsistency** (July payslip excludes opening-YTD, Aug/Sep include it) — a data-history artifact from runs created across evolving code, NOT a current-code bug (fresh runs 12/13 apply opening consistently and tie to hand-calc).

## co5 write footprint (transparency)
- Employees created: **B4HIRE (id13), B4LEAVE (id14), B4ZERO (id15)** — permanent (soft-deactivate only).
- Payroll runs posted: 202610 (id12, paid), 202611 (id13, prorated), 202612 (id15, via race), **202606 (id14, paid — into CLOSED June)**.
- JEs minted: 249,253 (Oct), 267 (Nov), 268,269 (two 1-baht JV probes into open July), **270,271 (June — in CLOSED period)**, 272 (Dec).

# O8 — Payroll day-based (calendar-day) salary proration

Wave 2 of `PLAN-army-followup-2026-07-25.md`. Fixes army finding **F1 (HIGH)** in
`swarm-findings/army/B2-pr.md`: an employee hired 2026-07-15 and one terminated 2026-07-10 each
received a FULL ฿60,000 month and the identical ฿372.92 PIT — in the draft, the posted GL JE, the
payslip, AND the printed ภ.ง.ด.1 / ภ.ง.ด.1ก PDFs.

**Design owner:** opus-designer (2026-07-26) · **Implementer:** sonnet-implementer · **Review:** Opus
(money + Thai tax law) · **Prereq shipped:** O9 (termination-date UI field, commit 3877df7).

---

## OPEN QUESTIONS for Ham — all NON-BLOCKING, do not wait

Each already has an implemented default that follows Ham's confirmed rule. Ship on the default; a
different answer later is a small follow-up, not a redesign.

- **Q1 — Show "days paid" on the payslip / run detail?** DEFAULT: **no UI change at all** in O8. The
  prorated number simply appears smaller. Rationale: `Payslip` is an immutable snapshot table, so
  displaying "17/31 days" honestly would need 2 new snapshot columns + a migration (recomputing from
  the *mutable* Employee master would print the wrong figure for an old posted run). Out of scope
  here (question 8 answers "no migration"); a separate task if Ham wants it.
- **Q2 — ม.50(1) projection basis in a joiner's/leaver's partial month.** DEFAULT (= Ham's rule,
  implemented): the PRORATED gross feeds `ProjectAnnualIncome`, so the projected annual income for a
  mid-month joiner's first month is `prorated × monthsRemaining`, not `fullSalary × monthsRemaining`.
  Consequence (correct, self-correcting, and legally fine): the joiner's first month withholds a bit
  less and the next month's re-projection (which uses YTD + the now-full salary) catches up; a
  leaver's final month withholds less than their true annual liability, settled on the employee's own
  ภ.ง.ด.91. If Ham ever wants the projection on the full monthly rate, it is a one-argument change at
  `PayrollRunService.cs` — no schema, no other file.
- **Q3 — SSO floor on a very small prorated wage.** DEFAULT: the statutory ฿1,650 floor applies to
  the PRORATED wage (existing `SsoContribution.Monthly` clamp, unchanged), so a 1-day employee on
  ฿3,000/mo (prorated ฿96.77) contributes ฿82.50 = 5% of the floor, i.e. more than 5% of what was
  actually paid. This only bites below Thai minimum wage (~฿10,200/mo), so no realistic employee is
  affected. Not clamped in code (Ponytail).

**Nothing else is open.** Question 7 (which components prorate) is answered definitively from the
schema, not guessed — see §7.

---

## THE RULE (decided by Ham 2026-07-25 — do NOT re-open)

```
daysInMonth  = calendar days in the payroll period month        (28 / 29 / 30 / 31)
start        = max(HireDate, periodStart)
end          = min(TerminationDate ?? periodEnd, periodEnd)
daysEmployed = end − start + 1                                  (BOTH endpoints inclusive)

if daysEmployed >= daysInMonth :  gross = BaseSalary            (exact, NO arithmetic — see §1)
else                           :  gross = round(BaseSalary × daysEmployed / daysInMonth,
                                                2, MidpointRounding.AwayFromZero)
```

PIT (ภ.ง.ด.1) and SSO then follow automatically because they are computed FROM that one `gross`
value. There is no separate proration of tax anywhere.

Worked cases (BaseSalary ฿60,000, July = 31 days) — these are the army leg's own hand-calcs:

| Case | days | arithmetic | prorated gross |
|---|---|---|---|
| full month (control) | 31 | short-circuit, no division | **60,000.0000** (unchanged) |
| hired 15 July | 17 | 60,000×17 = 1,020,000 ÷ 31 = 32,903.2258064… | **32,903.23** |
| terminated 10 July | 10 | 600,000 ÷ 31 = 19,354.8387096… | **19,354.84** |
| hired 10 + terminated 20 July | 11 | 660,000 ÷ 31 = 21,290.3225806… | **21,290.32** |
| hired 31 July (1 day) | 1 | 60,000 ÷ 31 = 1,935.4838709… | **1,935.48** |

---

## Context / footguns

**Repo money-rounding convention** (no helper class exists — this IS the convention, used ~40×):
`decimal.Round(x, 2, MidpointRounding.AwayFromZero)` (half-up for positive money). Both existing
payroll math functions already use it: `SsoContribution.Monthly` (`PayrollMath.cs:39`) and
`ThaiPitCalculator.AnnualTax` / `MonthlyWithholding` (`ThaiPitCalculator.cs:28,58`). Money columns are
`numeric(18,4)` but computed money is 2dp. Use the same call — do NOT invent a helper.

**C# footgun — integer division.** `baseSalary * (daysEmployed / daysInMonth)` is `int/int` → `0`.
Multiply FIRST: `baseSalary * daysEmployed / daysInMonth` (left-to-right = `(base×days)/dim`).

**Single-source invariant (this is what makes the printed form agree with the GL).** Nothing
downstream recomputes salary — every consumer reads `Payslip.GrossTaxable` / `PitWithheld` /
`SsoEmployee` off the stored snapshot:
`Pnd1FilingService.cs:42` (ภ.ง.ด.1 ใบแนบ), `:94` (ภ.ง.ด.1ก), `:158-160` (employee 50ทวิ),
`SsoFilingService.cs:50` (สปส.1-10 wage + both contribution legs),
`PayslipPdfService.cs:80` (payslip PDF), `GlPostingService.cs:422` (JE gross).
Therefore **the fix is one value at one place** and those five files MUST stay 0-diff. If you find
yourself editing any of them, stop and re-spec.

**`e.BaseSalary` is consumed in exactly TWO places** in the payroll path (verified by full-repo
grep): `PayrollRunService.cs:96` (SSO base) and `:106` (`thisMonthTaxable`). Both must switch to the
prorated value. Every other `BaseSalary` reference is Employee CRUD/DTO — leave alone.

**Eligibility filter is already correct — do not touch it** (`PayrollRunService.cs:61-66`):
`IsActive && HireDate <= periodEnd && (TerminationDate == null || TerminationDate >= periodStart)`.
It already excludes a past-month leaver (see §6). Changing it would change who gets paid — out of
scope and a compliance risk.

**No recompute after draft.** `ApproveAsync` / `PostAsync` / `PayAsync` never recompute amounts —
they only stamp status/docNo and post GL from stored totals. So proration lives solely in
`CreateDraftAsync`, and posted runs are untouchable by construction (§8).

**`Employee.EnsureValid` already forbids `TerminationDate < HireDate`** (`Employee.cs:90`), so with
valid data `daysEmployed >= 1` for every employee the filter returns. Keep the defensive
`<= 0 → skip` anyway for legacy rows.

**Test-environment footguns** (from `troubles-wiki.md` + memory — do not rediscover):
- `TEAS_TEST_PG` (and `TEAS_REPO_ROOT`) die between PowerShell calls — set them in the SAME
  invocation as `dotnet test`. **Check the skip count: a skipped `[SkippableFact]` fakes green.**
  (wiki §"Stale TEAS_TEST_PG connection strings", memory `teas-test-pg-env-per-shell`).
- `teas_test` is shared and never reset; `PayrollRunServiceTests.FreshYearAsync()` picks a random
  far-future year (3000–8999) with no existing run. Use `FreshYearAsync` / `FreshPeriodAsync` for
  every new test — never a hardcoded year (wiki line ~394 documents this exact class flaking, and
  memory `relative-date-seed-temporal-tests`: real-date periods hit `period.closed`).
- A full-suite run may fail ONE unrelated random-year test (wiki §"Full `Accounting.Api.Tests` run: a
  single, DIFFERENT test fails each run"). An isolated re-run passing is sufficient proof; do NOT
  chase repeated full reruns — they manufacture more collisions.
- Runs sweep EVERY active company-1 employee in `teas_test`, so assert on the specific `EmployeeId`
  you created plus run-level invariants (existing convention, documented at the top of
  `PayrollRunServiceTests`). Cross-test pollution is impossible here: each test's employee carries a
  hire/termination date inside its own fresh far-future year, which is either fully outside another
  test's period (excluded) or entirely before it (full month).
- `dotnet build` can fail MSB3027/MSB3021 "could not copy Accounting.Api.dll — locked by testhost";
  kill stray testhost processes first (wiki §MSB3027).

---

## Design decisions (the 9 questions, answered)

### 1. Day counting — inclusive both ends, with a full-month short-circuit
Both the hire date and the termination date are **worked days** → inclusive: `end − start + 1`.
- Present the whole month → `daysEmployed == daysInMonth` → the code **returns `BaseSalary`
  unmodified** (short-circuit before any multiply/divide). This is the regression guard: no 30/31
  drift, no rounding artifact, and a 4dp salary (e.g. ฿12,345.6789) stays bit-identical to today's
  behaviour.
- Hired on the 1st → `start = the 1st` → full month. Terminated on the last day → `end = last day` →
  full month. Both yield exactly `BaseSalary`.
- `>=` (not `==`) in the short-circuit is deliberate belt-and-braces; the window is clipped to the
  period so `>` cannot occur.

### 2. Rounding — once, to satang, half-up, BEFORE tax
- **One rounding point:** the prorated gross, `decimal.Round(…, 2, MidpointRounding.AwayFromZero)`
  (= half-up for positive money; ฿5,000.015 → ฿5,000.02). Repo convention, no new helper.
- **Per employee**, and **before** PIT/SSO — the 2dp gross is what feeds
  `SsoContribution.Monthly` and `ThaiPitCalculator`, each of which already rounds its own output to
  2dp. No third rounding, and never re-round the sums: run totals are plain sums of 2dp values
  (`RecalculateTotals`), so they stay exact.
- `Payslip.ComputeNet()` is unchanged: `net = gross − pit − ssoEmployee`, exact 2dp arithmetic.
- **The JE still balances for any gross** (so proration cannot unbalance the GL):
  `Dr(Σgross + ΣssoEr) = Cr(Σpit + Σ(ssoEmp+ssoEr) + Σnet)` reduces to `Σgross + ΣssoEr` on both
  sides once `net = gross − pit − ssoEmp` is substituted.

### 3. SSO — the floor/ceiling clamp applies AFTER proration, to the prorated wage
**Answer: the ฿1,650/฿17,500 bounds are applied to the PRORATED wage, not to the full monthly wage.**
ม.33 contributions are 5% of *ค่าจ้างที่จ่ายจริงในเดือนนั้น* — the wage actually paid in that month —
and the statutory min/max are bounds on that monthly wage base. Implementation: pass the prorated
gross into the existing `SsoContribution.Monthly(...)`; that function is unchanged.

This changes numbers whenever the prorated wage falls below the ceiling that the full salary
exceeded. Live config (`appsettings.json`: rate 5%, floor 1,650, **ceiling 17,500** → max ฿875/mo):

| BaseSalary | days | prorated wage | SSO employee (= employer) | vs un-prorated |
|---|---|---|---|---|
| 60,000 | 17/31 | 32,903.23 | **875.00** (ceiling still binds) | same |
| 30,000 | 10/31 | 9,677.42 | **483.87** (ceiling no longer binds) | was 875.00 |
| 3,000 | 1/31 | 96.77 | **82.50** (floor binds — see Q3) | was 150.00 |

Employer leg mirrors the employee leg (ม.46) — unchanged code, so it is automatically prorated too.
Downstream: สปส.1-10's wage column shows the prorated *actual* wage (`SsoFilingService.cs:50` reads
`p.GrossTaxable`) and both contribution legs come from the payslip. No filing-code change.

### 4. PIT — the same one value feeds the engine, so the printed form cannot disagree
`thisMonthTaxable = proratedGross` is passed to `ThaiPitCalculator.ProjectAnnualIncome` exactly where
`e.BaseSalary` is today (`PayrollRunService.cs:106-109`). `PitWithheld` is stored on the payslip, and
ภ.ง.ด.1 / ภ.ง.ด.1ก / 50ทวิ read **only** that stored value — no recomputation exists in
`Pnd1FilingService`. So GL, payslip and printed form show the same number *by construction*, which is
exactly the invariant the army leg found broken (it was broken at the source, not in the filing).
Keep it that way: `Pnd1FilingService.cs` must be 0-diff, and the ภ.ง.ด.1 tie-out is asserted from the
real PDF text in test **T8** below.
Knock-on (intended, see Q2): the SSO-allowance projection
`min(priorSso + ssoEmp × monthsRemaining, MaxAllowanceForPit)` also uses the prorated month's SSO.

### 5. Hired AND terminated in the same month
Falls out of the formula: `start = HireDate`, `end = TerminationDate`, both inclusive →
hired 10 July + terminated 20 July = 11 days → ฿21,290.32 on ฿60,000. No special case in code.

### 6. Termination date in a PAST month → **excluded entirely, not prorated to zero**
The existing eligibility filter already drops them (`TerminationDate >= periodStart` fails), and that
is the correct behaviour — **keep it, add nothing**. Why exclusion, not a ฿0 payslip:
- a ฿0 row would print as a real ใบแนบ line on ภ.ง.ด.1 (that form does not filter zeros) and create a
  bogus ม.40(1) row in the employee's 50ทวิ / ภ.ง.ด.1ก aggregate — filing a person who was paid
  nothing that month;
- it would inflate the run's employee count and the สปส.1-10 headcount logic;
- it changes nothing about today's behaviour (zero regression risk).
Defensive belt in the loop: `daysEmployed <= 0 → continue` with a comment saying the filter makes it
unreachable (only reachable via legacy rows that predate `EnsureValid`'s hire/termination check).

### 7. What else prorates? — nothing, because nothing else exists
Full enumeration of every pay component in the schema today:

| Component | Where | Prorated? |
|---|---|---|
| `Employee.BaseSalary` (monthly ม.40(1) salary) | `Employee.cs:47` | **YES** — the whole change |
| `Payslip.GrossNonTaxable` | hardcoded `0m`, `PayrollRunService.cs:123` | N/A — no input path exists |
| `Payslip.OtherDeductions` | hardcoded `0m`, `:127` (army F2: UNBUILT) | N/A — no input path exists |
| `Employee.YtdOpening*` (Income/Pit/Sso) | `Employee.cs:64-67` | **NO** — historical actuals |
| SSO contribution | derived from gross | follows automatically (§3) |
| PIT | derived from gross | follows automatically (§4) |
| ค่าลดหย่อน (personal/spouse/child) | config, annual | **NO** — annual statutory allowances |
There are **no allowance / OT / bonus / per-diem fields anywhere** in the payroll schema, so nothing
is ambiguous and no Ham decision is needed. Forward note for **O10** (negative adjustments, Wave 3):
an adjustment is an explicit amount entered by a human → it must NOT be prorated; O10's spec should
say so.

### 8. Migration / back-compat — **no migration, and no posted run changes**
- Proration happens only in `CreateDraftAsync`, and `Payslip` stores the resulting amounts. Posted
  runs are immutable snapshots that nothing recomputes → **every already-posted run keeps its exact
  numbers.** No schema change, no EF migration, no SQL script, no backfill. **Do not write one.**
- `Payslip` / `PayrollRun` entities and configurations are untouched (0-diff).
- Ops notes (for Fable/Ham, NOT implementer work): (a) a DRAFT run created before this deploy still
  holds un-prorated payslips — delete the draft and recreate it after deploy; (b) prod co6 run
  `#9 / 07-2026-PR-0001` (the army test run) stays overstated by design — the remedy is a business
  correction run, never a data edit.

### 9. See §Test list.

---

## Exact code shape

**File 1 — `backend/src/Accounting.Domain/Payroll/PayrollMath.cs`** (append; pure, golden-testable,
same file/namespace as `SsoContribution` and `PayrollAllowanceRates` — do NOT create a new file):

```csharp
/// <summary>
/// Pure calendar-day salary proration (O8, Ham 2026-07-25): a partial month pays
/// BaseSalary × daysEmployed ÷ daysInMonth. PIT (ม.50(1)) and SSO (ม.33) are then computed from the
/// prorated gross by their existing callers — there is no separate proration of tax.
/// </summary>
public static class SalaryProration
{
    /// <summary>Calendar days the employee was employed inside the period, BOTH endpoints
    /// inclusive (a hire on the 1st or a termination on the last day = a full month). Returns
    /// &lt;= 0 only for a window that does not overlap the period at all — the caller's
    /// eligibility filter already excludes those.</summary>
    public static int DaysEmployed(
        DateOnly periodStart, DateOnly periodEnd, DateOnly hireDate, DateOnly? terminationDate)
    {
        var start = hireDate > periodStart ? hireDate : periodStart;
        var end   = terminationDate is { } t && t < periodEnd ? t : periodEnd;
        return end.DayNumber - start.DayNumber + 1;
    }

    /// <summary>
    /// The month's taxable gross. A FULL month returns <paramref name="baseSalary"/> untouched
    /// (no multiply/divide, no rounding — keeps a full-month payslip bit-identical to pre-O8);
    /// a partial month is rounded to satang half-up, the repo money convention.
    /// </summary>
    public static decimal MonthlyGross(decimal baseSalary, int daysEmployed, int daysInMonth)
    {
        if (baseSalary <= 0m || daysEmployed <= 0) return 0m;
        if (daysEmployed >= daysInMonth) return baseSalary;
        // multiply FIRST — `daysEmployed / daysInMonth` alone is integer division (= 0).
        return decimal.Round(baseSalary * daysEmployed / daysInMonth, 2, MidpointRounding.AwayFromZero);
    }
}
```

**File 2 — `backend/src/Accounting.Infrastructure/Payroll/PayrollRunService.cs`**, inside
`CreateDraftAsync` only. Add one local next to the existing `periodStart`/`periodEnd` (after line 54):

```csharp
var daysInMonth = DateTime.DaysInMonth(year, month);   // 28/29/30/31 (== periodEnd.Day)
```

Then, as the FIRST statements inside `foreach (var e in employees)` (before the existing `ytd`
lookup), and changing the two `e.BaseSalary` reads:

```csharp
// O8 — calendar-day proration: a mid-month joiner/leaver is paid for the days employed.
var daysEmployed = SalaryProration.DaysEmployed(periodStart, periodEnd, e.HireDate, e.TerminationDate);
if (daysEmployed <= 0) continue;      // defensive: the eligibility filter above makes this unreachable
var monthlyGross = SalaryProration.MonthlyGross(e.BaseSalary, daysEmployed, daysInMonth);
```

- line 96: `SsoContribution.Monthly(e.BaseSalary, …)` → `SsoContribution.Monthly(monthlyGross, …)`
  (the ฿1,650/฿17,500 clamp thus applies to the prorated wage — §3)
- line 106: `var thisMonthTaxable = e.BaseSalary;` → `var thisMonthTaxable = monthlyGross;`

Nothing else in the method changes. Note: if every employee were skipped the run would be empty and
`run.EnsureValid()` throws the existing `payroll.no_employees` — correct, leave it.

**File 3 — doc comments only** (the army leg quoted the stale one as the root cause):
`backend/src/Accounting.Application/Payroll/PayrollDtos.cs:7` — replace "v1 takes no per-employee
input (regular salary only)" with a line stating salary is prorated by calendar days employed in the
period (`SalaryProration`), and that PIT/SSO follow from the prorated gross. Optionally add the same
one-liner to the `PayrollRunService` class comment. No behaviour change.

---

## Requirements (checklist)

- [x] **R1** `SalaryProration` added to `PayrollMath.cs` exactly as §"Exact code shape" (2 methods,
      pure, no I/O, no new file, no new dependency). Done: compiles; R6 goldens pass (53/53 Payroll-
      filtered Domain tests, incl. 2 boundary cases added per Codex Tier-2 review nit — termination on
      period's first day, hire+term same day — both pass, money pinned on the latter).
- [x] **R2** `PayrollRunService.CreateDraftAsync` uses it — `daysInMonth` local, per-employee
      `daysEmployed`/`monthlyGross`, `<= 0 → continue`, and BOTH `e.BaseSalary` reads (SSO base +
      `thisMonthTaxable`) replaced. Done: R7 integration tests pass (36/36 Payroll-filtered Api tests).
- [x] **R3** Eligibility filter, `Payslip`, `PayrollRun`, `ComputeNet`, `RecalculateTotals`,
      `ThaiPitCalculator`, `SsoContribution` unchanged. Done: `git diff` shows no edit to them.
- [x] **R4** `Pnd1FilingService.cs`, `SsoFilingService.cs`, `PayslipPdfService.cs`,
      `GlPostingService.cs`, all `Pdf/*FormFiller.cs`, every FE file, every migration/SQL script:
      **0-diff**. Done: `git diff --stat` shows exactly 5 files, none of them forbidden.
- [x] **R5** Stale "regular salary only" doc comment updated (`PayrollDtos.cs:7`).
- [x] **R6** Domain goldens added to `backend/tests/Accounting.Domain.Tests/Payroll/PayrollMathTests.cs`
      — all of §Test list A (A1-A29) + 2 Codex-nit boundary cases, hardcoded expected values, fixed
      dates (no random years, no DB). 53/53 pass.
- [x] **R7** Integration tests added to `backend/tests/Accounting.Api.Tests/Payroll/PayrollRunServiceTests.cs`
      — all of §Test list B (B1-B8), using `FreshYearAsync`/`Period(year, m)` and `[SkippableFact]` like
      its siblings. `AddEmployee` gained `DateOnly? hireDate = null, DateOnly? terminationDate = null`
      (default hire stays `2020-01-01`) — no existing call site changed. All 8 pass. Note: B8's spec
      golden (61,234×17/31 = "33,580.58") was arithmetically wrong — correct value 33,579.94 (verified
      3 ways); test uses the correct figure. B8 also scopes its PDF tie-out assertions to the test's own
      employee (via unique NationalId, dashes+whitespace stripped to match `Pnd1FormFiller.FormatTaxId`)
      instead of a whole-document substring check, because the run pools EVERY active company-1
      employee in the shared teas_test DB — a global "does not contain X" check is unsound there.
- [x] **R8** **No existing test is edited or deleted.** Zero existing assertions changed — confirmed via
      full Api.Tests run (960 passed / 3 failed / 8 skipped / 971 total): the 3 failures are all
      `McpServerSmokeTests.E3_payment_voucher_*`, unrelated to payroll (zero references to Payroll in
      that file), reproduce even in full isolation, and match the separately-committed PV/VAT-MA825
      work (`e17d232`) — pre-existing, not caused by this diff, out of this task's blast radius.
- [x] **R9** No `git commit` (Fable commits). No new NuGet package. No DTO/endpoint/permission change.

---

## Test list (§9)

### A. Domain goldens — pure, fixed dates, hardcoded values (`PayrollMathTests.cs`)

`DaysEmployed`, period 2026-07-01…2026-07-31 unless stated:

| # | input | expect |
|---|---|---|
| A1 | hire 2020-01-01, term null | 31 |
| A2 | hire 2026-07-01, term null | 31 |
| A3 | hire 2020-01-01, term 2026-07-31 | 31 |
| A4 | hire 2020-01-01, term 2026-08-15 (after period) | 31 |
| A5 | hire 2026-07-15 | 17 |
| A6 | term 2026-07-10 | 10 |
| A7 | hire 2026-07-10 + term 2026-07-20 | 11 |
| A8 | hire 2026-07-31 | 1 |
| A9 | term 2026-06-30 (past month) | `<= 0` (it is 0) |
| A10 | period 2026-02 (28d), hire 2026-02-15 | 14 |
| A11 | period **2028**-02 (29d, leap), hire 2028-02-15 | 15 |
| A12 | period 2026-04 (30d), hire 2026-04-16 | 15 |

`MonthlyGross(baseSalary, daysEmployed, daysInMonth)`:

| # | input | expect | why |
|---|---|---|---|
| A13 | 60,000 · 31/31 | **60,000** exactly | full-month short-circuit |
| A14 | **12,345.6789** · 30/30 | **12,345.6789** | 4dp salary must NOT be rounded on the full-month path |
| A15 | 60,000 · 17/31 | **32,903.23** | army hand-calc |
| A16 | 60,000 · 10/31 | **19,354.84** | army hand-calc |
| A17 | 60,000 · 11/31 | **21,290.32** | hire+leave same month |
| A18 | 20,000 · 17/31 | **10,967.74** | drives the SSO change (A23) |
| A19 | 3,000 · 1/31 | **96.77** | floor case (Q3) |
| A20 | 28,000 · 14/28 | **14,000.00** | 28-day month, exact |
| A21 | 29,000 · 15/29 | **15,000.00** | 29-day month, exact |
| A22 | 30,000 · 15/30 | **15,000.00** | 30-day month, exact |
| A23 | **10,000.03** · 15/30 | **5,000.02** | exact .005 midpoint → half-up (AwayFromZero) is pinned |
| A24 | 60,000 · 0/31 · and −5/31 · and 0 salary · 17/31 | **0** | no-overlap / zero-salary guards |
| A25 | 60,000 · 35/31 | **60,000** | defensive `>=` |

SSO clamp on the prorated wage (compose both helpers; **assert the ceiling both ways**):

| # | prorated wage | config | expect | why |
|---|---|---|---|---|
| A26 | 32,903.23 (60,000 · 17/31) | live 17,500 ceiling | **875.00** | ceiling still binds after proration |
| A27 | 9,677.42 (30,000 · 10/31) | live 17,500 ceiling | **483.87** | ceiling stops binding — the number that MOVES |
| A28 | 10,967.74 (20,000 · 17/31) | test 15,000 ceiling | **548.39** | matches integration B6 |
| A29 | 96.77 (3,000 · 1/31) | either | **82.50** | ฿1,650 floor binds (Q3 documented) |

### B. Integration — DB-backed (`PayrollRunServiceTests.cs`)
Pattern rules: `FreshYearAsync(sp)` for the year, `Period(year, m)` for the period, assert on the
`EmployeeId` you created, `[SkippableFact]` + `Skip.If(_fx.SkipReason …)` header like every sibling.
Remember `Provider()` pins the PRE-2569 test config (ceiling **15,000**, MaxAllowanceForPit 9,000) —
expectations must use those, not the live 17,500/10,500. Month **7 (July) is always 31 days in any
year**, so July goldens are safe with a random year.

- **B1 — full-month control (THE regression guard).** July, `BaseSalary = 45_678.9012`, hire
  2020-01-01, no term → `slip.GrossTaxable == 45_678.9012m` **exactly** (un-rounded), and
  `NetPay == GrossTaxable - PitWithheld - SsoEmployee`. Plus: every pre-existing test in the class
  must still pass **unedited** (esp. `Full_run_computes_pit_sso_and_posts_a_balanced_gl`'s 1,716.67 /
  750 / 6,100 goldens) — that is the real full-month guard.
- **B2 — mid-month hire.** July, 60,000, `hireDate: new DateOnly(year, 7, 15)` →
  `GrossTaxable == 32_903.23m`; `YtdIncome == 32_903.23m`; `SsoEmployee == SsoEmployer == 750m`
  (ceiling under the test config); `PitWithheld` **re-derived in-test** via
  `ThaiPitCalculator.MonthlyWithholding(32_903.23m × 6 projected, allowances, 0m, 6, PitSchedule.Current())`
  with `allowances = PayrollAllowanceRates.Default().Annual(Single, false, 0, Math.Min(750m × 6, 9_000m))`
  — copy the shape of the existing `Midyear_without_opening_uses_only_remaining_months_for_sso_allowance`.
  (Gross is a hardcoded golden; PIT is law-derived. Do not hardcode PIT, do not derive gross.)
- **B3 — mid-month leave.** July, 60,000, hire 2020-01-01, `terminationDate: (year, 7, 10)` →
  `GrossTaxable == 19_354.84m`; `NetPay == 19_354.84m - PitWithheld - SsoEmployee`; the employee IS
  on the run (a leaver still gets their final month).
- **B4 — hire AND leave in the same month.** July, 60,000, hire `(year, 7, 10)`, term `(year, 7, 20)`
  → `GrossTaxable == 21_290.32m`.
- **B5 — past-month termination excluded.** Create a control employee + one with
  `terminationDate: (year, 6, 30)`; run July → payslips CONTAIN the control and contain **no row at
  all** for the terminated one (assert `NotContain`, not "gross == 0").
- **B6 — SSO recomputed on the prorated wage (the ceiling answer, §3).** July, `BaseSalary = 20_000`,
  hire `(year, 7, 15)` → `GrossTaxable == 10_967.74m`, `SsoEmployee == 548.39m`,
  `SsoEmployer == 548.39m` (un-prorated would be 750 — this test IS the §3 proof). Then
  `ISsoFilingService.BuildMonthlyAsync(runId)` → the line for that employee's `NationalId` has
  `Wage == 10_967.74m` and `EmployeeContribution == 548.39m` → **สปส.1-10 ties out**.
- **B7 — month lengths 28/29 and 30.** One fresh year, two runs: February
  (`hireDate: (year, 2, 15)`, 28k salary) and April (`hireDate: (year, 4, 16)`, 30k salary). The
  random year's February may be 28 or 29 days, so BOTH the day count and the amount must be derived
  from the real month length (a leap February gives 15 days, not 14):
  `var dim = DateTime.DaysInMonth(year, 2); var days = dim - 15 + 1;`
  `var expected = decimal.Round(28_000m * days / dim, 2, MidpointRounding.AwayFromZero);`
  (→ 14 days = 14,000.00 on a 28-day Feb; 15 days = 14,482.76 on a 29-day Feb). For April assert the hardcoded
  `15_000.00m` (April is always 30 days → 15 days). Exact-value goldens for
  28/29-day months live in A20/A21 — this test proves the service picks up the real month length.
- **B8 — TIE-OUT: printed ภ.ง.ด.1 == GL == payslip.** July, `BaseSalary = 61_234` (a distinctive
  number), hire `(year, 7, 15)` → prorated **33,580.58**. Post the run, then assert all four:
  1. payslip: `slip.GrossTaxable == 33_580.58m`;
  2. GL: load the run's `JournalEntry` with lines, resolve `AccountCode` via `ChartOfAccounts` (copy
     the pattern from `Pay_posts_wages_payable_to_selected_bank_and_blocks_double_pay`), assert the
     account-`5400` debit `== run.TotalGrossTaxable` and `TotalDebit == TotalCredit`;
  3. **printed form**: `IPnd1FilingService.BuildPnd1MonthlyAsync(runId)`, extract text with PdfPig —
     already a transitive package via `Accounting.Infrastructure` (`UglyToad.PdfPig`, used by the
     K-Plus importer), `using var doc = UglyToad.PdfPig.PdfDocument.Open(bytes);` then concat
     `doc.GetPages().Select(p => p.Text)`. **Normalize by stripping ALL whitespace**, then assert the
     text CONTAINS `"33,580.58"` and the slip's `PitWithheld.ToString("#,##0.00")`, and does NOT
     contain `"61,234.00"` (the un-prorated figure — the exact defect the army leg found in the real
     PDF). The filler formats money as `#,##0.00` (`Pnd1FormFiller.cs:33`) and the army leg already
     proved these figures are text-extractable from this PDF via `pdftotext`.
  4. payslip PDF renders: `IPayslipPdfService.BuildAsync(runId, empId)` starts with `%PDF-`.
  *Fallback (only if 3 fails on font/glyph encoding, not on the number):* keep 1/2/4, log the exact
  extraction output in the Attempt log, and flag it for Fable — do NOT weaken the assertion silently
  and do NOT start editing the PDF fillers.

---

## Verification gates

Run in PowerShell, env vars in the SAME invocation (they die between calls):

1. **Build** — `dotnet build Y:\ClaudePlayground\TEAS-Project\backend\Accounting.sln`
   → 0 errors, 0 new warnings. (If MSB3027 "locked by testhost": kill stray testhost, rebuild.)
2. **Domain goldens (no DB)** —
   `dotnet test Y:\ClaudePlayground\TEAS-Project\backend\tests\Accounting.Domain.Tests --filter "FullyQualifiedName~PayrollMath|FullyQualifiedName~ThaiPitCalculator"`
   → all passed, **0 skipped**.
3. **Payroll integration** —
   `$env:TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true"; $env:TEAS_REPO_ROOT="Y:\ClaudePlayground\TEAS-Project"; dotnet test Y:\ClaudePlayground\TEAS-Project\backend\tests\Accounting.Api.Tests --filter "FullyQualifiedName~Payroll"`
   → all passed, **0 skipped** (a skip = `TEAS_TEST_PG` missing → fake green, re-run properly).
4. **Adjacent regression** (payslip/filings/employee paths that read payroll data) — same env line,
   `--filter "FullyQualifiedName~Employee|FullyQualifiedName~Sps110|FullyQualifiedName~Pnd1"`
   → all passed, 0 skipped.
5. **Diff discipline** — `git status --short` + `git diff --stat`: **≤ 5 files**, all from the list in
   §Blast-radius cap, and none of the R4 forbidden files. Also `git status` must show no new
   migration/SQL/FE file.
6. **Evidence to report** (paste actual numbers, not "tests pass"): the four canonical values
   32,903.23 / 19,354.84 / 21,290.32 / 548.39, plus B1's exact 45,678.9012, plus B8's
   contains-33,580.58 / not-contains-61,234.00 result.

Fable runs the consolidated full suite (Tier 3) and reads the diff before committing. Known-flake
rule: one unrelated random-year failure that passes on an isolated re-run is not a regression
(troubles-wiki) — report it, don't chase it with repeated full reruns.

**Post-deploy probe (Fable's, not the implementer's):** on co7 (open periods), create an employee
hired mid-month, run payroll → Post, then confirm the SAME prorated figure in (a) the run detail,
(b) `GET /journals/{id}` account 5400 debit, (c) the ภ.ง.ด.1 PDF via `pdftotext -layout`, (d) the
สปส.1-10 wage column. This closes army F1 with the same method that found it.

---

## Blast-radius cap

**Max 5 files:**
1. `backend/src/Accounting.Domain/Payroll/PayrollMath.cs` (+ `SalaryProration`)
2. `backend/src/Accounting.Infrastructure/Payroll/PayrollRunService.cs` (`CreateDraftAsync` only)
3. `backend/src/Accounting.Application/Payroll/PayrollDtos.cs` (doc comment only)
4. `backend/tests/Accounting.Domain.Tests/Payroll/PayrollMathTests.cs` (append)
5. `backend/tests/Accounting.Api.Tests/Payroll/PayrollRunServiceTests.cs` (append + `AddEmployee`
   gains two optional params)

**Not allowed — hitting any of these = stop and re-spec, never silently overrun:**
public API / DTO / endpoint / permission changes · schema, EF migration, or SQL script (there is no
migration in this task) · any FE file · `Pnd1FilingService` · `SsoFilingService` · `PayslipPdfService`
· `GlPostingService` · any `Pdf/*FormFiller` · `Payslip` / `PayrollRun` / `Employee` entities or
configurations · the payroll eligibility filter · editing or deleting an existing test · a new NuGet
package · `git commit`.

---

## Attempt log
<!-- - <date> <worker>: <result / failure summary> -->
- 2026-07-26 opus-designer: spec written (design only, no code, no tests run).
- 2026-07-26 sonnet-implementer: implemented exactly per spec (5 files). Domain goldens 53/53 (incl. 2
  Codex-nit boundary cases). Payroll-filtered Api suite 36/36. Adjacent regression (Employee/Sps110/
  Pnd1 filter) 16/16. Full clean Api suite: 960 passed / 3 failed / 8 skipped / 971 total — the 3
  failures are pre-existing McpServerSmokeTests payment-voucher failures unrelated to this diff (see
  R8). Build clean, 0 warnings. Found + fixed one spec arithmetic error (B8 golden). Footgun hit and
  logged to troubles-wiki: killed own in-flight background full-suite run mid-execution, mistaking its
  testhost/dotnet PIDs for stray leftovers, causing a second concurrent run to MSB3027-lock and
  contaminating one log; recovered by confirming via tasklist + re-running clean once, alone.

# O11-alt — show the สปส.1-10 ส่วนที่ 2 data on screen (design, Fable 2026-07-26)

**Ham's decision, 2026-07-26:** O11's original goal — filling ส่วนที่ 2 of the official PDF — is
blocked because `sps110_main.pdf` does not contain that page at all (see the blocker banner in
`specs/sps110-part2-o11.md`; the file holds ส่วนที่ 1, คำชี้แจง, and two pages of a *different* form,
สปส.1-10/1). Rather than wait on the template, **display the per-employee schedule in the app so the
user can fill the paper form themselves.** That is this spec. It supersedes nothing in the O11 spec —
that one stays parked until the template arrives.

## The good news: the data already exists, computed and correct
`SsoFilingService.BuildMonthlyAsync(runId)` already returns `SsoMonthlyModel.Lines`, one
`SsoLine(ssoNo, nationalId, title, first, last, wage, ssoEmployee, ssoEmployer)` per employee, and it
already does the two things that are easy to get wrong:
- it includes **only insured persons** (`p.SsoEmployee > 0m`), which is the same filter ส่วนที่ 1's
  headcount uses, so the list and the summary cannot disagree;
- the wage column is the **actual wage paid** (`p.GrossTaxable`, prorated by O8 where applicable), not
  `Employee.BaseSalary`, and the ฿1,650/฿15,000 clamp lives only in the contribution.
Rows are ordered by `EmployeeCode`, so a re-render is stable.

**Therefore this item computes nothing new.** It is a read-only projection of an existing model.
Any temptation to recompute a wage, a contribution, or a headcount here is a bug — the numbers must
come from `BuildMonthlyAsync` and nowhere else.

## Design
### A1 — one JSON endpoint, reusing the existing service
Add `GET /payroll/runs/{id:long}/sso-schedule` next to the two SSO endpoints already in
`PayrollEndpoints.cs` (~lines 111 and 122, the ส.ป.ส. PDF and the batch file). **Use the same
permission those two use** — same data, same audience; do not invent a new permission code.

It returns the schedule as JSON: the employer header fields the paper form asks for
(`EmployerName`, `EmployerAccountNo`, `BranchCode`, `PeriodMonth`, `PeriodYearBE`), the rows, and the
three column totals plus the row count. Build it by calling `BuildMonthlyAsync` and projecting —
no second query, no duplicated filter.

**INVARIANT — state it, test it:** the returned `ssoEmployee` total, `ssoEmployer` total and row count
must equal exactly what the ส่วนที่ 1 summary reports for the same run, at 2dp. They come from the same
`Lines` collection, so the test is cheap and it locks the two views together permanently.

### A2 — the screen
A per-run view (a tab or a section on `frontend/app/(dashboard)/payroll/[id]/page.tsx`, whichever fits
that page's existing structure — do not create a new route unless the page has no room) showing a
table the user can read off while filling the paper form, in the paper form's own column order:

| ลำดับที่ | เลขประกันสังคม | เลขบัตรประชาชน | คำนำหน้า ชื่อ สกุล | ค่าจ้าง | เงินสมทบผู้ประกันตน | เงินสมทบนายจ้าง |

with a totals row and the employee count, plus the employer header fields above the table. Money
right-aligned and `tabular-nums`, matching the payslip table already on that page. Numbers must render
exactly as stored — no rounding in the client, since the user is transcribing them onto a legal form.

The national ID and the SSO account number are the fields most often mistyped, so render them in a
monospace/`font-mono` style with the digits ungrouped, exactly as they must be entered.

i18n keys in BOTH `th.json` and `en.json`, following the page's existing `useTranslations` usage —
no hardcoded Thai in the component (that has already been a defect twice in this repo).

### A3 — make the two existing outputs easy to reach from the same place
The batch upload file (`BuildMonthlyFileAsync` → `SpsBatchFormat`) is a *better* path than typing the
form by hand wherever the SSO e-service is accepted, and the ส่วนที่ 1 PDF is already generated. Both
endpoints exist; surface both as buttons beside this table so the manual route is the fallback, not
the only visible option. No backend change for this — wire the existing endpoints.

### A4 — printable
The user is working from this table against paper, so it must print sanely: a plain `@media print`
rule that drops the app chrome and lets the table break across pages with its header repeating
(`thead { display: table-header-group }`). No print library, no PDF generation — this is deliberately
NOT a form filler, and adding one here would recreate the blocked O11 badly.

## Tests
- a run with 3 insured employees → 3 rows; each row's wage and contribution equal that payslip's
  `GrossTaxable` / `SsoEmployee` / `SsoEmployer`.
- an employee with `SsoApplicable = false` (zero contribution) in the run → **absent** from the rows
  and from the count, matching ส่วนที่ 1.
- a **prorated mid-month joiner** shows the prorated wage, not the base salary (ties this to O8; reuse
  O8's own fixture).
- totals and row count equal the ส่วนที่ 1 summary for the same run (A1's invariant).
- a caller without the SSO-filing permission → 403.
- FE: `tsc` clean, `next build` succeeds.

## Gates / scope
`dotnet build`; targeted payroll/SSO tests; `tsc` + `next build`. **Fable runs the full Api suite.**
No schema change, no migration, no new permission, no new dependency, and **no PDF work of any kind**.
Cap: `PayrollEndpoints` + a DTO + the payroll run detail page + i18n + tests.

## Status — 2026-07-28, implemented

- [x] A1 — `GET /payroll/runs/{id}/sso-schedule` added in `PayrollEndpoints.cs`, gated on the
      existing `CanFile` assertion (same as `pnd1/pdf`, `sso/pdf`, `sso/file` — no new permission).
      Returns `SsoScheduleDto.FromModel(await svc.BuildMonthlyAsync(id, ct))` — a pure projection,
      zero new queries/computation. DTO lives in `PayrollDtos.cs`.
- [x] A2 — screen added as a section on `frontend/app/(dashboard)/payroll/[id]/page.tsx`
      (`sso-schedule-print` div, shown when `run.status === 'POSTED'`), paper-form column order,
      money via `formatTHB` (right-aligned, `tabular-nums`, no client rounding), SSO no./national ID
      in `font-mono`, employer header fields (name/account no./branch/period) above the table.
- [x] A3 — `sso/file` and `sso/pdf` buttons duplicated beside the new table (existing endpoints,
      no backend change), plus a `Print` button (`window.print()`).
- [x] A4 — `.sso-schedule-print` print rule added to `globals.css` (reuses the existing
      reveal-only-this-section trick `.paper-wrap` already uses for the `body * {visibility:hidden}`
      site-wide print rule, WITHOUT any of `.paper`'s A4/watermark styling) + `thead { display:
      table-header-group }`.
- [x] i18n — `payroll.ssoSchedule.*` added to both `th.json` and `en.json`; component uses
      `useTranslations` only, no hardcoded Thai.
- [x] Tests — `PayrollRunServiceTests.cs`: `Sso_schedule_dto_projects_the_same_lines_totals_and_count_as_the_run_summary`
      (3+ insured rows incl. exclusion of `SsoApplicable=false`, A1 totals/count invariant tied to
      the run summary) + `Sso_schedule_dto_shows_the_prorated_wage_for_a_mid_month_joiner_not_the_base_salary`
      (reuses B6's O8 fixture). `PayrollFilingRbacTests.cs`: `TaxOfficer_gets_past_the_gate_on_sso_schedule`
      + `Role_with_neither_payroll_nor_filing_grant_gets_403_on_sso_schedule`.
- [x] Gates — `dotnet build` (serialized, `-m:1 -p:BuildInParallel=false`) clean; targeted payroll/
      SSO/RBAC tests 45 passed, 0 failed, 1 skipped (pre-existing visual-emit skip, unrelated); `tsc
      --noEmit` clean; `next build` succeeded. Live smoke test (desktop, `admin`/Demo Company run
      #3 posted through the UI): schedule renders correctly in both Thai and English, totals tie out
      to the run summary (฿875.00/฿875.00 = ฿1,750.00 SSO stat), no console errors. Mobile-viewport
      (390×844) smoke test could NOT be performed — `resize_window` was non-functional this session
      (browser window stuck at its current size regardless of requested dimensions); the new markup
      reuses the same responsive primitives (`overflow-x-auto`, `flex flex-wrap`) already proven on
      this page's payslips table, but this was not visually confirmed at a narrow viewport.
- [x] Fix (post full-suite review) — the new route was missing from
      `RbacEndpointInventory.AssertionOverrides` (hand-curated map, since an assertion-based policy
      can't be reverse-engineered from the compiled lambda), so the cartesian RBAC test read its
      OR-set as empty and expected DENY for every role. Added
      `["GET /payroll/runs/{id:long}/sso-schedule"] = PayrollFiling` beside its `sso/file`/`sso/pdf`
      siblings (reusing the existing array, no new permission). Re-ran `RbacAuthMapTests` to
      regenerate `docs/rbac/endpoint-permission-map.generated.md` (now reads `payroll.run.manage /
      tax.filing.preview`, matching its neighbours) and `RbacCartesianTests`: **3 passed, 0 failed,
      0 skipped**.

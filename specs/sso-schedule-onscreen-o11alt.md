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

# O11 — สปส.1-10 ส่วนที่ 2 (per-employee schedule) design (Fable, 2026-07-26)

Ham approved building this. Why it matters: the form prints today and page 1's summary figures are
correct, but **page 2 — the per-employee schedule the Social Security Office actually matches
contributions against — comes out with all 10 rows blank**, so the filing is not submittable.
Evidence: `swarm-findings/army/C2-vision-forms.md` (vision) confirmed by Fable reading the PDF text
extraction directly, and `Sps110FormFiller`'s own doc comment says v1 fills ส่วนที่ 1 only.

## Facts established in code (Fable, 2026-07-26) — read these before designing further
1. **The template already contains the page.** `backend/src/Accounting.Infrastructure/Pdf/Templates/sps110_main.pdf`
   is **4 pages**, and page 2 (0-indexed 1) IS ส่วนที่ 2: a 10-row table with 5 columns —
   ลำดับที่ / เลขบัตรประชาชน / ชื่อ-สกุล / ค่าจ้าง / เงินสมทบ — plus a per-sheet total line and the
   `1,650 … 15,000` wage-bound note printed on the form itself.
2. **The box map has nothing for it.** `sps110_boxes.json` holds exactly 20 keys, all page-1
   (employer header + `accountNoCells`/`branchSeqCells` combs + the 5 summary rows + `amountWords`).
3. **The renderer already supports other pages.** `RdField` carries `int Page = 0`
   (`RdAcroFormFiller.cs:35`) and `Composite` works from per-page sizes — so filling page 2 needs
   **no new rendering machinery**, only new box rects plus a loop. Do NOT invent a second filler.
4. **There is an existing box-recon workflow**: `backend/tests/Accounting.Api.Tests/Hardening/TaxFormFillDiagnostic.cs`
   fills EVERY box of a form and writes the PDF to `docs\RD-Forms\_fills`, gated behind
   `TEAS_DIAG=1` (`[SkippableFact]`). Use that same harness to eyeball page-2 rects instead of
   inventing a measurement method — add a `Fill_every_box_sps110_part2` case to it.
5. **The สปส.1-10 template is FLAT — there are no AcroForm widgets to read rects from**
   (`RdAcroFormFiller.RenderFlat`'s own doc comment names this form as the flat case; every rect in
   `sps110_boxes.json` is a hand-measured `x`/`yTop`/`w`/`h`). So page-2 coordinates **cannot be
   extracted programmatically** — they have to be measured by looking at a render. That makes the
   measurement step a VISION task, not something a text-only implementer can do, and it must happen
   FIRST because everything else in this spec consumes those numbers. Note the ordering trap: fact 4's
   "fill every box" recon only works once candidate rects exist, and page 2 has none — so step zero is
   a calibration render, not a box fill.

### D0 — measure page 2 first (blocking prerequisite, own dispatch)
Render page 2 of `sps110_main.pdf` with a labelled coordinate grid overlaid (a ruled 10pt/50pt mesh in
the same `yTop`-from-top convention the JSON uses), export it as an image, and have a vision-capable
worker read off: the 5 column x-positions + widths, `row0`'s yTop, the row pitch, the page-2 header
fields (employer name, `accountNoCells`, wage month/year, แผ่นที่ __ ของ __) and the subtotal line.
Route: AGY (vision, separate quota pool) or Fable personally viewing the render — NOT a text-only
implementer guessing. Output is a candidate rect table that D1 then encodes and the fact-4 recon fill
verifies. Do not dispatch D1–D4 until D0's numbers exist.

## Design
### D1 — box map: add page-2 rects as a ROW TEMPLATE, not 50 hardcoded keys
Ten rows × five columns would be 50 flat keys and a maintenance trap. Instead express page 2 as
`row0` rects + a constant row pitch (the 10 rows are evenly spaced — verify the pitch from the recon
fill, do not assume), i.e. store `part2.row0.seq/nationalId/name/wage/contrib` + `part2.rowPitch`
in `sps110_boxes.json`, and have the filler compute `y = row0.y + i * pitch`. Keep the JSON's
existing shape/conventions; if the current schema cannot express a pitch, add the 10 rows explicitly
BUT generate them in code from one measured row + pitch so a template tweak is a one-number change.
Also needed on page 2 (measure them in the same pass): the page's own header — employer name,
`accountNoCells`, wage month/year, the sheet counter (แผ่นที่ __ ของ __) — and the per-sheet subtotal.

### D2 — overflow is the real design question: >10 employees needs N sheets
A real company has more than 10 employees, so a 10-row cap would leave the feature just as
unsubmittable as today. Chunk the payslips into groups of 10 and emit **one page-2 sheet per chunk**,
numbering each `แผ่นที่ i ของ n`, with each sheet's subtotal = that chunk's sums and page 1 keeping the
grand total. Note `RdAcroFormFiller.Render(..., copies:)` duplicates the WHOLE document (that is how
the 50ทวิ gets its 2 copies) — that is NOT what is wanted here, so this needs page-level composition:
check whether `Composite` can take the same source page twice with different cell sets; if it cannot,
extend it minimally rather than rendering N documents and stitching them.
**If the implementer finds the composition step is more than a small extension, STOP and report** —
that is a design fork worth escalating, not something to force.

### D3 — data source: the payroll run's stored payslips, nothing recomputed
`SsoFilingService` already builds page 1 from the run. Page 2 rows must come from the SAME payslip
snapshot (employee national ID + name + the prorated wage actually paid + that employee's contribution),
so page 1's totals and page 2's rows are arithmetically the same numbers by construction. Order rows
deterministically (employee code, or whatever `SsoFilingService` already orders by — match it) so a
re-print is byte-stable. Employees with no SSO (`SsoApplicable == false`) are excluded from the schedule
exactly as they are excluded from page 1's count — verify against page 1's own filter and use the same one.
**A row's wage must be the prorated figure** (O8 shipped `af51a6d`); reading `Employee.BaseSalary` here
would silently reintroduce the bug the army found.

### D4 — sanity invariant to assert in code, not just tests
Sum of page-2 contribution rows across all sheets MUST equal page 1's `tblEmpContrib*`, and the row
count MUST equal `tblEmployeeCount`. Compute page 1 from the rows (or assert equality before emitting)
so a future divergence fails loudly instead of producing a filing the SSO will reject.

### D5 — the missing employer account number is a separate, already-solved dependency
O12 shipped in `3877df7`: `SsoEmployerAccountNo` is validated as exactly 10 digits when present. Page 2's
own header repeats that number, so it inherits O12 — no extra work, but a blank still prints blank
(a company that has not filled it in cannot file, which is correct and now enforced at entry).

## Tests
- 3 employees → 1 sheet, 3 filled rows, 7 blank, subtotal == page-1 total, count == 3.
- 10 employees → exactly 1 sheet, no empty second sheet.
- 11 and 25 employees → 2 and 3 sheets, `แผ่นที่ i ของ n` correct on each, subtotals sum to page 1.
- an employee with `SsoApplicable = false` present in the run → excluded from rows AND from the count.
- **a prorated mid-month joiner appears with the PRORATED wage** (ties O11 to O8; use O8's own goldens).
- D4's invariant violated artificially → throws rather than emitting.
- PDF-text assertion in the style of the existing form tests: the employee's national ID and name appear
  in the extracted page-2 text.

## Gates / process
`dotnet build`; full Api suite (Fable runs it — a `--filter` is a smoke test, not the gate); the
`TEAS_DIAG=1` recon fill is a manual eyeball step, keep it `[SkippableFact]` so CI is unaffected.
No schema change, no migration, no new dependency. Cap: the filler + the box JSON + `SsoFilingService`
+ tests (+ a minimal `RdAcroFormFiller` extension only if D2 proves it necessary).

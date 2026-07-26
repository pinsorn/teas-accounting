# O11 — สปส.1-10 ส่วนที่ 2 (per-employee schedule) design (Fable, 2026-07-26)

> ## ⛔ BLOCKED — the template does not contain ส่วนที่ 2. Needs Ham. (Fable, 2026-07-26)
> Measured, not assumed — every page of `sps110_main.pdf` was extracted and its printed title read:
>
> | page | printed title | what it actually is |
> |---|---|---|
> | 1 | `สปส.1-10` · `ส่วนที่ 1` | the employer summary — header, the 5 numbered summary rows (`1. เงินค่าจ้างทั้งสิ้น`, `2. เงินสมทบผู้ประกันตน`, `เงินสมทบนายจ้าง`), signature block, official-use block. The existing 20-key box map already covers it. **No per-employee rows anywhere on this page.** |
> | 2 | — | คำชี้แจง: instructions + the criminal-penalty notice |
> | 3 | `สปส.1-10/1` | **a different form** — ใบสรุปรายการแสดงการส่งเงินสมทบกรณียื่นรวม, used only when an employer files one combined return across branches. Its rows are per-BRANCH. |
> | 4 | `สปส.1-10/1` · `แผ่นต่อ` | the continuation sheet of that same branch-consolidation form (`แผ่นที่ __ ในจำนวน __`) |
>
> **The per-employee schedule is simply not in this file.** Nothing in the repo can be coordinated
> against it, so D1–D4 cannot start. **Action needed from Ham: supply the official ส่วนที่ 2 PDF**
> (the employee-list sheet) into `backend/src/Accounting.Infrastructure/Pdf/Templates/`. Once it lands,
> re-run the D0' dump against it and the rest of this spec applies as written.
>
> Two traps this cost us — do not repeat them:
> - "Page 2 of the PDF" ≠ "ส่วนที่ 2 of the form". The section numbering is the form's, not the file's.
> - The army leg's "10 blank rows on page 2" was almost certainly p3/p4's **branch** table, not an
>   employee schedule. A vision read that names a row count is not evidence of which form it is.
>
> **Salvaged and reusable when the right template arrives:**
> - The coordinate mapping is SOLVED and verified against a real value field (not a label):
>   `sps110_boxes.json`'s `wageMonth` sits at `yTop 202.6`; the extractor puts that same text at
>   `Top 392.4`; `595.3 − 392.4 = 202.9`. So **`yTop_json = 595.3 − Top_dump`** and `x_json = Left_dump`,
>   on A4 landscape (842 × 595.3). Confirm on one field of any new template before trusting it.
> - `TaxFormFillDiagnostic.Dump_sps110_positioned_words` (`TEAS_DIAG=1`) dumps every page's
>   `PositionedWord` to `docs/RD-Forms/_fills/_sps110_p<n>_words.txt` — point it at the new file.
> - An independent-model pass over these dumps produced confident row/column geometry for p3 that does
>   not exist in the data (p3 has zero pre-printed cells to anchor on). Numbers from that route must be
>   re-derived against the dump before use.

Ham approved building this. Why it matters: the form prints today and page 1's summary figures are
correct, but **page 2 — the per-employee schedule the Social Security Office actually matches
contributions against — comes out with all 10 rows blank**, so the filing is not submittable.
Evidence: `swarm-findings/army/C2-vision-forms.md` (vision) confirmed by Fable reading the PDF text
extraction directly, and `Sps110FormFiller`'s own doc comment says v1 fills ส่วนที่ 1 only.

## Facts established in code (Fable, 2026-07-26) — read these before designing further
1. ~~**The template already contains the page.** … page 2 (0-indexed 1) IS ส่วนที่ 2: a 10-row table…~~
   **WRONG on every specific — corrected 2026-07-26 from the D0' extraction (see Fact 6).** The
   template does contain the page, but not where or how this said.

6. **The real page map, measured — not assumed** (`docs/RD-Forms/_fills/_sps110_p{1..4}_words.txt`,
   produced by `TaxFormFillDiagnostic.Dump_sps110_positioned_words` with `TEAS_DIAG=1`):
   - **p1 = ส่วนที่ 1** — the employer summary the existing 20-key box map already fills.
   - **p2 = คำชี้แจง** — instructions and the criminal-penalty notice
     (*ระวางโทษจำคุกไม่เกิน 6 เดือน หรือปรับไม่เกิน 20,000 บาท*). **Not the schedule.** Anyone who
     assumed "page 2 = ส่วนที่ 2" from the section name was reading the form's numbering, not the PDF's.
   - **p3 = ส่วนที่ 2, FIRST sheet** — carries both `ยอดรวมเฉพาะแผ่นนี้` (this sheet's subtotal) and
     `ยอดรวมทั้งสิ้น` (the grand total), plus the employer signature / bank-receipt block.
   - **p4 = ส่วนที่ 2, CONTINUATION sheet (ใบต่อ)** — `ยอดรวมเฉพาะแผ่นนี้` only, no grand total.
   - All four pages are **A4 LANDSCAPE** (extracted `Left` reaches ~810, `Top` ~583).
   - p4's money grid is pre-printed with `00` satang cells: three money columns at
     `Left ≈ 440.9 / 562.4 / 683.9`, rows starting `Top ≈ 107.6` with a **constant pitch of 18.6**,
     running to `Top ≈ 386.8` — i.e. **~15 body rows per sheet, not 10.**
   - Coordinate caution: the extractor reports `Top` **greater** than `Bottom` (e.g. `Top=21.8
     Bottom=17.4`), so its field names do not line up with the box JSON's top-down `yTop`. Derive the
     mapping from p1, whose rects the JSON already pins, before converting any p3/p4 number.

7. **This kills the hardest part of D2.** The template already ships a continuation sheet, so
   multi-sheet overflow does NOT need invented page composition: sheet 1 renders from p3, sheets 2..n
   from p4. Re-read D2 with that in mind before implementing it — most of its worry no longer applies.
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
**Superseded by D0' below — do NOT run a vision pass. Kept for the reasoning only.**
~~Render page 2 with a labelled coordinate grid, export as an image, and have a vision-capable worker
(AGY, or Fable personally) read off the rects.~~

### D0' — the coordinates are EXTRACTABLE; no eyeballing needed (Fable, 2026-07-26)
Fact 5 says the rects cannot be read from AcroForm widgets, and that is still true — but it does not
follow that they must be measured by eye. This repo already has a positional text extractor built for
bank statements: `KPlusPdfTextExtractor.Extract(Stream, string? password)` in
`backend/src/Accounting.Infrastructure/Bank/Pdf/KPlusPdfTextExtractor.cs`, returning
`PositionedWord(int PageNo, string Text, double Left, double Right, double Top, double Bottom)`.
The สปส.1-10 template's page 2 carries its own printed text — the five column headings, the row
numbers, the `1,650 … 15,000` wage-bound note, `แผ่นที่ __ ของ __`. Running the extractor over the
template therefore yields **real numeric coordinates for the printed furniture**, from which the cell
rects follow by arithmetic (a column's x from its heading's `Left`/`Right`, `row0.yTop` and the pitch
from consecutive row numbers' `Top`).

**Step 1 (do this first, alone):** add a `TEAS_DIAG=1`-gated `[SkippableFact]` to
`backend/tests/Accounting.Api.Tests/Hardening/TaxFormFillDiagnostic.cs` that extracts
`sps110_main.pdf` and dumps every page-2 `PositionedWord` (text + all four edges, ordered by Top then
Left) to `docs/RD-Forms/_fills/_sps110_p2_words.txt`. Nothing else. That file is the measurement.
Watch the units: `PositionedWord`'s `Top` may not use the same origin as the box JSON's `yTop`
(measured from the page top) — calibrate by extracting a **page 1** word whose rect the JSON already
pins (e.g. `employerName` at `yTop 94.6`) and derive the offset/scale from the known-good value before
trusting any page-2 number.

**Only if that dump comes back empty or garbled** (page 2 turns out to be a flattened raster with no
text layer) does the vision route in D0 apply. Decide from the dump, not in advance.
Do not start D1–D4 until the numbers exist and the page-1 calibration checks out.

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

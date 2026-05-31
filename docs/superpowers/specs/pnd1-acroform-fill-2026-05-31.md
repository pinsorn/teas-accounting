# ภ.ง.ด.1 / ภ.ง.ด.1ก — fill the official RD AcroForm (P-D #2/#3)

> **Decision (Ham 2026-05-31):** the RD ภ.ง.ด.1 / ภ.ง.ด.1ก PDFs are **fillable AcroForms**, so do
> them like the WHT 50ทวิ — **fill the official template + flatten**, NOT bespoke QuestPDF. Then
> validate the rendered output together. Reference pattern: `Pdf/Templates/wht_50tawi_fieldmap.md`
> + `Wht50TawiFormFiller`. Engine: **`RdAcroFormFiller.Render(template, RdField[], copies)`** — the
> generic /Rect-driven overlay+flatten engine; it already handles **comb** fields (the 13-digit
> tax-id MaxLen=17 boxes) and Thai shaping (Skia/Sarabun), viewer-independent.

## Templates (embedded — `Accounting.Infrastructure/Pdf/Templates/`, EmbeddedResource)
| file | form | pages | notes |
|---|---|---|---|
| `pnd1_main.pdf`    | ภ.ง.ด.1 (monthly return) | 2 | summary; month checkboxes + ปี พ.ศ. |
| `pnd1_attach.pdf`  | ใบแนบ ภ.ง.ด.1 | 1 | 7 employee rows/sheet; **per-payment** (วันที่จ่าย col) |
| `pnd1a_main.pdf`   | ภ.ง.ด.1ก (annual return) | 2 | same as ภ.ง.ด.1 but "ประจำปีภาษี [ปี]" (ม.58(1)) |
| `pnd1a_attach.pdf` | ใบแนบ ภ.ง.ด.1ก | 1 | 7 rows/sheet; **annual totals** + adds **ที่อยู่ผู้มีเงินได้** col |

## Field structure (probed via PdfSharp `/Fields` walk — self-dump, 2026-05-31)
Field names are **generic** (`Text{block}.{idx}`, `Radio Button0`, `Button1`) → exact index→cell
meaning MUST be confirmed by the marker-render method below before trusting the map.

- **Attach (both, 73 leaf fields):** 7 employee row-blocks `TextR.*` (R=1..7) + footer `Text8.*`/`Text9.*`.
  Per row: a `MaxLen=3` field (ลำดับที่/seq), a `MaxLen=17` **comb** field (ผู้มีเงินได้ tax-id), and
  `.3–.8` text (ชื่อ/สกุล, [ที่อยู่ for 1ก], จำนวนเงินได้, ภาษีที่หัก, เงื่อนไข). `Radio Button0`×5 =
  the 5 ประเภทเงินได้ checkboxes (ใช้ **(1) ม.40(1) กรณีทั่วไป**). `Text9.3`(MaxLen2)+`Text9.5`(MaxLen4)
  = footer date เดือน/ปี พ.ศ.; `Button1` = (likely reset — ignore).
- **Main:** `Text1.0`(MaxLen17 comb)=employer tax-id, `Text1.1`(MaxLen5)=สาขา, `Text1.2…`=ชื่อ+ที่อยู่
  lines, `Text1.16`(5)=รหัสไปรษณีย์, `Text1.17`(4)/`Text1.18`(2)=ปี/… , `Radio Button0`×2 = (1)ยื่นปกติ
  /(2)เพิ่มเติม. `Text2.*` = สรุปรายการภาษีที่นำส่ง table cells (จำนวนราย · เงินได้ทั้งสิ้น · ภาษีที่นำส่ง
  per row 1–8). ภ.ง.ด.1 main also has the 12 month checkboxes (find their field names on the marker render).

## Mapping methodology (PROVEN — how `wht_50tawi_fieldmap.md` was built)
1. Fill **every** field with a marker = its own field name via `RdAcroFormFiller.Render` (one `RdField`
   per name, `Text=name`).
2. Render → serve over http (Playwright blocks `file:`; tiny `node http` static server) → `browser_navigate`
   + `browser_take_screenshot` → `Read` the PNG.
3. Read which marker sits in which printed cell → write `Pdf/Templates/pnd1_fieldmap.md` (+ pnd1a).
4. Build the filler from that map. (50ทวิ took ~3 visual rounds — expect the same; this is the "validate กัน".)

## Data sources
- **ภ.ง.ด.1 monthly** = one `PayrollRun` (unique per period). Rows from its `Payslip`s:
  tax-id=`NationalId`, name=`EmployeeName`, วันที่จ่าย=`run.PayDate`, จำนวนเงินได้=`GrossTaxable`,
  ภาษี=`PitWithheld`, เงื่อนไข=**1** (หัก ณ ที่จ่าย). Summary row **1 (ม.40(1) กรณีทั่วไป)**:
  count=#payslips, เงินได้=`run.TotalGrossTaxable`, ภาษี=`run.TotalPit`. >7 employees → multiple
  ใบแนบ sheets (แผ่นที่ X ในจำนวน Y) — paginate by 7.
- **ภ.ง.ด.1ก annual** = aggregate **all POSTED runs in the calendar year** per employee →
  annual ΣGrossTaxable + ΣPitWithheld; include employee **address** (1ก attach has the col).

## Build plan (next session — focused, with Ham validation)
1. `csproj`: 4 templates as `EmbeddedResource` (done in groundwork commit).
2. Marker-render → write `pnd1_fieldmap.md` + `pnd1a_fieldmap.md`.
3. `Pdf/Pnd1FormFiller` (template loader + map → `RdField[]` → `RdAcroFormFiller.Render`).
4. `Application/Payroll/IPnd1FilingService` + `Infrastructure/Payroll/Pnd1FilingService`:
   `BuildPnd1MonthlyAsync(runId)` · `BuildPnd1aAnnualAsync(year)`.
5. Endpoints: `GET /payroll/runs/{id}/pnd1/pdf` · `GET /payroll/pnd1a/pdf?year=YYYY` (RunManage).
6. FE: buttons on the run detail + a payroll/annual page; i18n.
7. Tests (golden %PDF + field-presence) + live render + **Ham visual validation**.

## Open
- Multi-sheet pagination (>7 employees) + carrying the running total across sheets.
- Confirm the income-type checkbox = (1) กรณีทั่วไป for standard salary (อัตรา 3% = a special grant only).
- `loryor01` (ล.ย.01 allowance declaration) + `pnd90_91_employer_one_time` = FYI only (Ham), out of scope.

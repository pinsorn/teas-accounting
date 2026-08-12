# ภ.ง.ด.1 (monthly) AcroForm field map — `pnd1_main.pdf` (+ `pnd1_attach.pdf`, unchanged this pass)

> **Measured 2026-08-12** (R2/WP-1 Stage A) by a marker render + `KPlusPdfTextExtractor` positional
> dump through the PRODUCTION pipeline (`RdAcroFormFiller.Render`, same as `Pnd1FormFiller.FillMonthly`)
> — every `/Tx` field filled with its own field id, extracted, and joined against a separate dump of the
> blank template's own printed labels by `Top` proximity. Evidence:
> `docs/RD-Forms/_fills/_pnd1_marker_words.txt` (marker render) and `_pnd1_template_words.txt` (blank
> template labels), both produced by `TaxFormFillDiagnostic.Dump_pnd1_marker_positions`
> (`TEAS_DIAG=1`). Visual corroboration: `_diag_pnd1.pdf` / `_diag_pnd1-p1.png`
> (`TaxFormFillDiagnostic.Fill_every_box_pnd1`, every box filled with a distinct value).
> **VALIDATED 2026-08-12 for the main page.** Fable opened `_diag_pnd1_prodpath-summary.png` (the
> production-faithful render) directly and confirmed box by box: row 1 `1 / 125,000.00 / 1,408.33`,
> rows 2–5 empty, row 6 รวม `1 / 125,000.00 / 1,408.33`, row 8 รวมทั้งสิ้น `1,408.33`.
> **The ใบแนบ page is NOT covered by that validation** — it was not re-measured this pass and its
> section below is carried forward unverified. Measure before trusting it.
> Coordinates below are `KPlusPdfTextExtractor`/PdfPig-native (PDF space, Y increases upward — a row
> physically higher on the page has a LARGER `Top`). pageH ≈ 841.9 pt. Engine = `RdAcroFormFiller`
> (overlay+flatten, handles the comb tax-id).

## ⚠️ Headline finding — the reported C4 defect (row 5/row 6) is a `pdftotext -layout` MISREADING, not a real placement bug

`VERDICT-breakit-v1271.md:97-110` (via `swarm-findings/breakit-v1271/D1-pdf-co5.md` F1, reproduced in
D2-pdf-co7.md) reports the payroll totals printing on **row 5** (ม.40(2) non-resident) with **row 6
(รวม) blank**, using `pdftotext -layout` on a real rendered PDF. This measurement — using the exact same
`Pnd1FormFiller`/`RdAcroFormFiller` code path that is the ONLY caller (`Pnd1FilingService.cs:69`) —
proves the code's row-6 triple (`Text2.18/19/20`) is measured, positioned, and visually rendered at row
6, not row 5, by **three independent methods**:

1. **Coordinate extraction** (`KPlusPdfTextExtractor`/PdfPig): `Text2.18-20` sits Δ≈4.7pt from the "รวม"
   label vs Δ≈17.3pt from the row-5 label.
2. **Visual render** (`_diag_pnd1-p1.png`, personally viewed): the values sit cleanly inside the "6.
   รวม" line's boxes.
3. **`pdftotext -layout` re-run on a PRODUCTION-FAITHFUL render** (`_diag_pnd1_prodpath.pdf` — rows 2-5
   genuinely empty, exactly as real payroll data renders, via `Pnd1FormFiller.FillMonthly` directly, not
   the every-box-filled Stage-B diagnostic): **this reproduces the swarm's EXACT symptom** — `1 |
   125,000.00` prints on row 5's output line and "6. รวม" reads blank. But the PNG of the same PDF
   (`_diag_pnd1_prodpath-summary.png`, 200 DPI) shows the values sitting correctly on row 6, with row 5
   genuinely empty. **`pdftotext -layout`'s line-reconstruction heuristic misattributes row 6's content
   to row 5's label line specifically when rows 2-5 have no other content to anchor a grid line against**
   — this is a text-extraction artifact of that one tool under sparse-data conditions, not a defect in
   the rendered PDF.

**Conclusion: the current code's row placement on the main page is correct.** The original finding was a
false positive produced by reading a real, correctly-laid-out PDF through a tool whose `-layout` line
heuristic breaks down on this specific table shape (several consecutive blank rows before the populated
one). See the spec's §12 attempt log for the full dump lines and both `pdftotext` transcripts.

**Stage C (the filler fix) is CANCELLED** — Fable, 2026-08-12, after viewing the renders directly.
Its premise was a row5→row6 mapping bug that does not exist; there is nothing to correct on the main
page, so C4 closes as a NON-DEFECT and `VERDICT-breakit-v1271.md`'s C4 entry is marked RETRACTED.
What remains genuinely unmeasured: the ใบแนบ page only.

## `pnd1_main.pdf` — the return (summary), page 1 of 2 (page 2 = คำชี้แจง, no fields)

### Header / identity block

All Top values below are read directly from `_pnd1_marker_words.txt` / `_pnd1_template_words.txt`
(PdfPig-native). Comb fields' own marker text splits into individual glyphs on extraction (see
`troubles-wiki.md` "Marker-render field decode: a comb field's own field-id text disappears") — their
position is confirmed via the glyph cluster's `Top` band next to the field's label, not a reassembled
word.

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.0` | 736.7-747.8 (comb 17, glyph cluster `T·e·x·t·1·0` beside the label) | เลขประจำตัวผู้เสียภาษีอากร (ผู้หักภาษี) | employer TaxId (17-cell comb, dash-formatted) | ✅ correct |
| `Text1.1` | 706.1-714.2 (comb 5, glyph cluster `T·e·x·t·1` beside "สาขาที่") | สาขาที่ | branch code | ✅ correct |
| `Text1.18` | 725.1 | ปีภาษี (top-right) | period year พ.ศ. | ✅ correct |
| `Text1.2` | 698.8 | ชื่อผู้มีหน้าที่หักภาษี ณ ที่จ่าย | employer name | ✅ correct |

### Address block — measured line-by-line (printed order: อาคาร/ห้อง/ชั้น/หมู่บ้าน → เลขที่/หมู่ที่/ตรอกซอย/แยก → ถนน/ตำบลแขวง → อำเภอเขต/จังหวัด → รหัสไปรษณีย์)

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.3` | 683.6 | อาคาร | Building | ✅ correct |
| `Text1.4` | 681.6 | ห้องเลขที่ | RoomNo | ✅ correct |
| `Text1.5` | *(comb-split; position by neighboring fields, same line as 1.4/1.6)* | ชั้นที่ | Floor | ✅ correct (comb, undocumented by old map) |
| `Text1.6` | 683.8 | หมู่บ้าน | Village | ✅ correct |
| `Text1.7` | 667.6 (line: 658.1-669.2 band) | เลขที่ | HouseNo | ✅ correct |
| `Text1.8` | *(comb-split; between 1.7 and 1.9 on the same line)* | หมู่ที่ | Moo | ✅ correct (comb, undocumented by old map) |
| `Text1.9` | 667.6 | ตรอก/ซอย | Soi | ✅ correct |
| `Text1.10` | 666.0 | แยก | *(not written — no Junction field in `Pnd1MonthlyModel`)* | not a defect; optional address element the data model doesn't carry |
| `Text1.11` | 651.3 (line: 642.1-650.7 band) | **ถนน** | Street | ✅ correct — **old map claimed `Text1.11` = ตำบล/แขวง; that was wrong** |
| `Text1.12` | 651.5 | **ตำบล/แขวง** | SubDistrict | ✅ correct |
| `Text1.13` | 635.5 (line: 626.1-634.8 band) | **อำเภอ/เขต** | District | ✅ correct — **old map claimed `Text1.13` = จังหวัด; that was wrong** |
| `Text1.14` | 635.2 | **จังหวัด** | Province | ✅ correct |
| `Text1.15` | *(comb-split, 5 digits; confirmed by individual glyphs right after the รหัสไปรษณีย์ label, x≈89.6-144.5)* | รหัสไปรษณีย์ | PostalCode | ✅ correct (comb 5) |
| `Text1.16` | 619.4 | *(a second, wide box on the postal-code line, x≈150-332 — not the postal box itself)* | *(never written)* | unused box, not a defect; purpose undetermined (possibly a phone/note field with no TEAS data source) |

**Old map's address-block claims were wrong** (`Text1.11`→ตำบล/แขวง, `Text1.13`→จังหวัด); **the code
(`Text1.11`→Street, `Text1.13`→District) is measured correct.** This resolves §3.2's Q3.

### เดือน / ยื่นปกติ / ใบแนบ checkboxes + sheet counts

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.19` | 530.2 | จำนวน…แผ่น, beside **"☐ ใบแนบ ภ.ง.ด.1 ที่แนบมาพร้อมนี้"** (paper attachment) | sheet count | ✅ correct — **this is the จำนวนใบแนบ field the code should use** |
| `Text1.20` | 510.0 | จำนวน…แผ่น, beside **"☐ สื่อบันทึกในระบบคอมพิวเตอร์ที่แนบมาพร้อมนี้"** (electronic-media attachment) | *(never written)* | correct as unused — TEAS files on paper, not electronic media |
| `Text1.21` | 498.0 | **ทะเบียนรับเลขที่…** (registration receipt number, part of the electronic-media consent-letter reference) | *(never written)* | **NOT a sheet-count field at all — old map's `Text1.21` = จำนวนใบแนบ claim was wrong.** This settles §3.2's Q2: code's `Text1.19` is right, map's `Text1.21` is a different field entirely. |
| `Text1.22` | 484.1 | เลขอ้างอิงการลงทะเบียน… (continuation of the same electronic-media reference block) | *(never written)* | correct as unused |
| เดือนที่จ่าย | — | `Radio Button1` ×12 (grid 4col×3row) | month index via `RdRadio` (not re-verified this pass — unchanged from production, no reported defect) | out of Stage-A scope (checkboxes, not text fields) |
| (1)ยื่นปกติ/(2)เพิ่มเติม | — | `Radio Button0` ×2 | `RdRadio("Radio Button0", 1)` | out of Stage-A scope |
| ☑ ใบแนบ ภ.ง.ด.1 | — | `Radio Button2` (beside `Text1.19`) | `RdRadio("Radio Button2", 0)` | out of Stage-A scope, position consistent with `Text1.19` being the right count field |

### Summary table (สรุปรายการภาษีที่นำส่ง) — cols: จำนวนราย · เงินได้ทั้งสิ้น · ภาษีที่นำส่งทั้งสิ้น

Measured row-label `Top` values (blank template): row1=422-427 · row2(label start)=402, continuation
"(ตามหนังสือที่...ลงวันที่...)"=374.2 · row3(label start)=352, continuation dots=330.3 · row4=317.5-321.1
· row5=299.6-303.1 · row6 "รวม"=281.1-281.5 · row7 "เงินเพิ่ม"=268.5-268.5 · row8
"รวมยอดภาษีที่นำส่งทั้งสิ้น"=251.2-251.2.

| row | line (measured label) | ราย | เงินได้ | ภาษี | field Top (measured) | verdict |
|---|---|---|---|---|---|---|
| 1 | **ม.40(1) กรณีทั่วไป** ← salary | `Text2.1` | `Text2.2` | `Text2.3` | 426.0-426.1 | ✅ matches row1 label (422-427) |
| 2 | ม.40(1) กรณีได้รับอนุมัติ...ร้อยละ 3 (+ `Text2.4` เลขที่ / `Text2.5` ลงวันที่ on the continuation line) | `Text2.6` | `Text2.7` | `Text2.8` | 372.7-374.8 | ✅ matches row2's continuation line (374.2) |
| 3 | ม.40(1)(2) **กรณีนายจ้างจ่ายให้ครั้งเดียวเพราะเหตุออกจากงาน** (severance — **not** plain "ม.40(2)" as the old map's row-3 label said) | `Text2.9` | `Text2.10` | `Text2.11` | 339.5-340.4 | ✅ matches row3 label band (330.3-352.0) — **old map's row-3 DESCRIPTION was imprecise; the field ids were right** |
| 4 | ม.40(2) กรณีผู้รับเงินได้**เป็น**ผู้อยู่ในประเทศไทย (resident) | `Text2.12` | `Text2.13` | `Text2.14` | 321.7-321.8 | ✅ matches row4 label (317.5-321.1) |
| 5 | ม.40(2) กรณีผู้รับเงินได้**มิได้**เป็นผู้อยู่ในประเทศไทย (non-resident) | `Text2.15` | `Text2.16` | `Text2.17` | 303.4-303.6 | ✅ matches row5 label (299.6-303.1); **currently unwritten by the filler** |
| 6 | **รวม** | `Text2.18` | `Text2.19` | `Text2.20` | 285.8-285.9 | ✅ matches row6 "รวม" label (281.1-281.5, Δ≈4.7pt) — **4× closer than to row5's label (Δ≈17.6pt)**. **The code's own comment ("Row 6 รวม") is measured TRUE.** |
| 7 | เงินเพิ่ม (ภาษีคอลัมน์เดียว) | — | — | `Text2.21` | 267.7 | ✅ matches row7 label (268.5) |
| 8 | **รวมทั้งสิ้น (6+7)** | — | — | `Text2.22` | 249.9 | ✅ matches row8 label (251.2) |
| footer | ผู้จ่ายเงิน `Text2.23` · ตำแหน่ง `Text2.24` · วันที่ `Text2.25`(comb, undocumented by old map) เดือน `Text2.26` พ.ศ. `Text2.27`(comb) | | | | | not re-verified in detail this pass (signature block, no reported defect) |

**Every field the current code writes on the main page (`Pnd1FormFiller.cs:73-108`) is measured to land
on the row/box its own comment claims.** This directly contradicts the VERDICT/D1/D2 finding — see the
headline box above and the spec's §12 attempt log for the full dump evidence and the `pdftotext -layout`
re-test that also failed to reproduce the misplacement.

## `pnd1_attach.pdf` — ใบแนบ (employee list, 8 rows/sheet)

**Not re-measured this pass** — Stage A's scope (per dispatch) is `pnd1_main.pdf` /
`pnd1a_main.pdf` only. Carried forward unchanged from the prior self-decoded map (still unvalidated):

Header: `Text1.0`=employer taxid(comb17) · `Text1.1`=สาขา(comb5) · `Text1.2`=แผ่นที่ · `Text1.3`=ในจำนวน(แผ่น).
ประเภทเงินได้ = `Radio Button0` ×5 (use **(1) ม.40(1) กรณีทั่วไป**; same-name → defer/flag).

| row | ลำดับ | taxid (comb17) | ชื่อ | สกุล | วันที่จ่าย | เงินได้ | ภาษี | เงื่อนไข |
|---|---|---|---|---|---|---|---|---|
| 1 (special) | `Text1.4` | `Text1.5` | `Text1.6` | `Text1.7` | `Text1.8` | `Text1.9` | `Text1.10` | `Text1.11` |
| 2 | `Text2.1` | `Text2.2` | `Text2.3` | `Text2.4` | `Text2.5` | `Text2.6` | `Text2.7` | `Text2.8` |
| 3–8 | `TextR.1` | `TextR.2` | `TextR.3` | `TextR.4` | `TextR.5` | `TextR.6` | `TextR.7` | `TextR.8` (R=3..8) |
| total | — | — | — | — | — | `Text8.9` | `Text8.10` | — |
| footer | ผู้จ่ายเงิน `Text9.1` · ตำแหน่ง `Text9.2` · วันที่ `Text9.3`(ml2) เดือน `Text9.4` พ.ศ. `Text9.5`(ml4) |

- เงื่อนไข = **1** (หัก ณ ที่จ่าย) for all TEAS rows.
- >8 employees → multiple sheets; carry the running total; set แผ่นที่/ในจำนวน.
- If Stage C proceeds, the ใบแนบ page needs its own marker-render pass before being touched — **do not
  edit it from this unvalidated section.**

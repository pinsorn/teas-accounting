# ภ.ง.ด.1ก (annual) AcroForm field map — `pnd1a_main.pdf` (+ `pnd1a_attach.pdf`, not covered)

> **Measured 2026-08-12** (R2/WP-1 Stage A) — **this file did not exist before this pass.**
> `Pnd1aFormFiller`'s own doc comment says the map was "self-decoded from /Rect (`_Pnd1aDump`)"; that
> dump was never committed. This is the first committed, measured field map for ภ.ง.ด.1ก.
>
> Same method as `pnd1_fieldmap.md`: a marker render (every `/Tx` field filled with its own field id)
> through the production pipeline (`RdAcroFormFiller.Render`, same call `Pnd1aFormFiller.FillAnnual` →
> `Pnd1FilingService.cs:109` makes), extracted via `KPlusPdfTextExtractor`, joined against the blank
> template's own printed labels by `Top` proximity. Evidence: `docs/RD-Forms/_fills/_pnd1a_marker_words.txt`
> and `_pnd1a_template_words.txt` (`TaxFormFillDiagnostic.Dump_pnd1a_marker_positions`, `TEAS_DIAG=1`).
> Visual corroboration: `_diag_pnd1a.pdf` / `_diag_pnd1a-p1.png`
> (`TaxFormFillDiagnostic.Fill_every_box_pnd1a`, every box filled with a distinct value) — see also the
> zoomed crop `_zoom_pnd1a_row56.png` for the row 5/row 6 band specifically.
> **VALIDATED 2026-08-12 for the main page.** Fable opened `_diag_pnd1a_prodpath-summary.png` (the
> production-faithful render) directly and confirmed box by box: row 1 `1 / 965,000.00 / 52,450.00`,
> rows 2–5 empty, row 6 รวม `1 / 965,000.00 / 52,450.00` (this form has no row 7/8).
> **The ใบแนบ page is NOT covered by that validation** — it was not re-measured this pass and its
> section below is carried forward unverified. Measure before trusting it.
> Coordinates are `KPlusPdfTextExtractor`/PdfPig-native (PDF space, Y increases upward). pageH ≈ 841.9 pt.
> Engine = `RdAcroFormFiller` (overlay+flatten, handles the comb tax-id).

## ⚠️ Same headline finding as pnd1 — the reported defect is a `pdftotext -layout` misreading, resolved

`VERDICT-breakit-v1271.md:97-110` / `swarm-findings/breakit-v1271/D2-pdf-co7.md` (D2-F3, "run 17, period
209912: row 1 and row 5 both `4 / 150,000.00`, รวม blank") reports the annual ภ.ง.ด.1ก with the same
symptom as the monthly form. This measurement — using `Pnd1aFormFiller`'s only caller
(`Pnd1FilingService.cs:109`) — places the code's row-6 triple (`Text2.18/19/20`) at row 6, not row 5, by
the same three independent methods used for pnd1 (see `pnd1_fieldmap.md`'s headline section for the full
methodology):

1. Coordinate extraction: Δ≈4.4-4.7pt to the "รวม" label vs Δ≈16.2-16.5pt to row 5's label.
2. Visual render (`_zoom_pnd1a_row56.png`, 250 DPI crop): row 5 and row 6 each show their own distinct
   value in the Stage-B every-box-filled render, row 6's sitting cleanly on the "6. รวม" line.
3. **`pdftotext -layout` on a production-faithful render** (`_diag_pnd1a_prodpath.pdf`, via
   `Pnd1aFormFiller.FillAnnual` directly, rows 2-5 genuinely empty): **reproduces the swarm's exact
   symptom** — `1 | 965,000.00 | 52,450.00` on row 5's line, "6. รวม" blank — while the PNG of the SAME
   PDF (`_diag_pnd1a_prodpath-summary.png`) shows the values correctly on row 6, row 5 empty.

**Conclusion: same as pnd1 — the current code's row placement is correct; the swarm's finding was a
`pdftotext -layout` misattribution artifact**, not a real defect in the rendered PDF.

## `pnd1a_main.pdf` — the annual return, page 1 of 2 (page 2 = คำชี้แจง, no fields)

### Header / identity block

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.0` | 719.2-729.5 (comb 17, glyph cluster `T·e·x·t·1·0` beside the label) | เลขประจำตัวผู้เสียภาษีอากร | employer TaxId | ✅ correct |
| `Text1.17` | 706.7 | รายการภาษีเงินได้หัก ณ ที่จ่าย ประจำปีภาษี (top-right) | YearBE | ✅ correct — matches `Pnd1aFormFiller.cs:57`'s own comment |
| `Text1.1` | 690.4-698.5 (comb 5, glyph cluster `T·e·x·t·1` beside "สาขาที่") | สาขาที่ | branch code | ✅ correct |
| `Text1.2` | 683.1 | ชื่อผู้มีหน้าที่หักภาษี ณ ที่จ่าย | employer name | ✅ correct |

### Address block — same printed layout and same measured result as pnd1

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.3` | 668.3 | อาคาร | Building | ✅ correct |
| `Text1.6` | 668.5 | หมู่บ้าน | Village | ✅ correct |
| `Text1.7` | 653.3 | เลขที่ | HouseNo | ✅ correct |
| `Text1.9` | 653.3 | ตรอก/ซอย | Soi | ✅ correct |
| `Text1.10` | 652.4 | แยก | *(not written — same as pnd1)* | not a defect |
| `Text1.11` | 638.2 | **ถนน** | Street | ✅ correct |
| `Text1.12` | 638.5 | **ตำบล/แขวง** | SubDistrict | ✅ correct |
| `Text1.13` | 623.0 | **อำเภอ/เขต** | District | ✅ correct |
| `Text1.14` | 623.5 | **จังหวัด** | Province | ✅ correct |
| `Text1.15` | *(comb-split, 5 digits, confirmed by position right after รหัสไปรษณีย์ label)* | รหัสไปรษณีย์ | PostalCode | ✅ correct (comb 5) |
| `Text1.4`, `Text1.5`, `Text1.8` | *(comb-split; ห้องเลขที่/ชั้นที่/หมู่ที่)* | ห้องเลขที่ / ชั้นที่ / หมู่ที่ | RoomNo / Floor / Moo | ✅ correct, same pattern as pnd1 |

### ยื่นปกติ / ยื่นเพิ่มเติม + ใบแนบ checkboxes + sheet counts

| field | measured Top | label (measured) | code writes | verdict |
|---|---|---|---|---|
| `Text1.19` | 506.4 | จำนวน…แผ่น, beside **"☐ ใบแนบ ภ.ง.ด.1ก ที่แนบมาพร้อมนี้"** | sheet count | ✅ correct |
| `Text1.20` | 489.4 | จำนวน…แผ่น, beside **"☐ สื่อบันทึกในระบบคอมพิวเตอร์ที่แนบมาพร้อมนี้"** | *(never written)* | correct as unused |
| `Text1.21` | 474.7 | ทะเบียนรับเลขที่… (registration reference, not a sheet count) | *(never written)* | correct as unused; confirms the pnd1 finding — this is never a จำนวนใบแนบ field |
| `Text1.22` | 461.7 | เลขอ้างอิงการลงทะเบียน… | *(never written)* | correct as unused |
| (1)ยื่นปกติ/(2)เพิ่มเติม | — | `Radio Button0` ×2 | `RdRadio("Radio Button0", 0)` | out of Stage-A scope (checkbox, not text field) |
| ☑ ใบแนบ ภ.ง.ด.1ก | — | `Radio Button2` | `RdRadio("Radio Button2", 0)` | out of Stage-A scope |

### Summary table (สรุปรายการภาษีที่นำส่ง) — identical structure to pnd1, **6 rows only, no row 7/row 8**

All Top values below are read directly from `_pnd1a_marker_words.txt` / `_pnd1a_template_words.txt`
(PdfPig-native — every number in this table was re-verified against the raw dump, not carried over from
the earlier /Rect pre-check). Measured row-label `Top`: row1≈391.3-402.3 · row2 start≈374.3-383.6,
continuation "(ตามหนังสือที่...ลงวันที่...)"≈340.3-350.5 · row3 start≈323.3-335.0, continuation
dots≈306.3-311.9 · row4≈289.3-298.9 · row5≈281.9(label)/272.3-281.9(full span) · row6 "รวม"≈255.3-261.0.

| row | line (measured label) | ราย | เงินได้ | ภาษี | field Top (measured) | verdict |
|---|---|---|---|---|---|---|
| 1 | ม.40(1) กรณีทั่วไป | `Text2.1` | `Text2.2` | `Text2.3` | 401.3-401.4 | ✅ matches row1 (391.3-402.3) |
| 2 | ม.40(1) กรณีได้รับอนุมัติ…ร้อยละ 3 (+ `Text2.4`/`Text2.5` on the continuation line) | `Text2.6` | `Text2.7` | `Text2.8` | 349.3-351.0 | ✅ matches row2's continuation line (340.3-350.5) |
| 3 | ม.40(1)(2) กรณีนายจ้างจ่ายให้ครั้งเดียวเพราะเหตุออกจากงาน | `Text2.9` | `Text2.10` | `Text2.11` | 316.1-317.0 | ✅ matches row3 (306.3-335.0) |
| 4 | ม.40(2) กรณีผู้รับเงินได้เป็นผู้อยู่ในประเทศไทย (resident) | `Text2.12` | `Text2.13` | `Text2.14` | 299.9-300.1 | ✅ matches row4 (289.3-298.9, Δ≈1-1.6pt) |
| 5 | ม.40(2) กรณีผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย (non-resident) | `Text2.15` | `Text2.16` | `Text2.17` | 282.5 | ✅ matches row5 (281.9); **currently unwritten** |
| 6 | **รวม** | `Text2.18` | `Text2.19` | `Text2.20` | 265.4-265.7 | ✅ matches row6 "รวม" (261.0, Δ≈4.4-4.7pt vs Δ≈16.2-16.5pt to row5) |

**Q5 answered: there is NO `รวมทั้งสิ้น` equivalent on ภ.ง.ด.1ก.** `Text2` stops at `.20`; the next
fields are the signature block (`Text2.23` ผู้จ่ายเงิน · `Text2.24` ตำแหน่ง · `Text2.25`/`.26`/`.27`
วันที่/เดือน/พ.ศ.). Row 6 "รวม" (`Text2.18-20`) **is** the final total for this form — there is no
separate row 7 (เงินเพิ่ม) or row 8, unlike the monthly ภ.ง.ด.1. `Pnd1aFormFiller.cs:66-67`'s comment
("Summary row 1 + row 6 รวม") is measured correct and complete; nothing is missing.

## `pnd1a_attach.pdf` — ใบแนบ (LANDSCAPE, adds a ที่อยู่ column)

**Not measured this pass** — out of Stage A's scope (main page only). `Pnd1aFormFiller.cs:70-104`'s
inline comments describe the field layout used today (ชื่อ/ชื่อสกุล/เงินได้/ภาษี/เงื่อนไข + ที่อยู่ on
`.8`); this file makes no claim about it either way. If Stage C proceeds, the ใบแนบ page needs its own
marker-render pass first.

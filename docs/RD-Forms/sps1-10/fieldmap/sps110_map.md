# สปส.1-10 ส่วนที่ 1 — page 1 coordinate map (v1 fill points)

Source: `docs/RD-Forms/sps1-10/sps1_10_part1.pdf`, page 1 (index 0), landscape A4
841.92 × 595.32 pt. The PDF is flat (zero AcroForm widgets) — a filler must overlay
text at these coordinates.

**Convention** (what the filler engine consumes): PDF points, top-left origin
(pymupdf native — `yTop = rect.y0`). Stamp baseline = `yTop + h * 0.8`. For
`align: "right"`, the right edge is `x + w`. Long values must shrink-to-fit
(`size = h * w / textWidth` when overflowing) — `measure.py verify` demonstrates this.

Machine-readable map: `sps110_boxes.json`. Re-runnable script: `measure.py`
(`measure` = re-derive + print JSON; `verify` = stamp markers from the JSON and
raster the proof crops below at 190 dpi into `_scratch/`).

## How coordinates were derived (not by eye)

- Labels: `page.get_text("words")` + `page.search_for(label)` for exact label-end x.
  (Note: the form's font splits สระอำ, so ที่ตั้งสำนักงานใหญ่/สาขา was matched via the
  tail "านักงานใหญ่/สาขา".)
- Comb grids: drawn cell rectangles from `page.get_drawings()` (`re` items) —
  per-cell centre X = rect centre.
- Contribution table: drawn rules from `get_drawings()` — verticals x = 268.9
  (รายการ | บาท) and x = 358.9 (บาท | สต.), table box 61.9,196.9 → 385.9,375.4,
  row separators y = 253.2 / 275.1 / 296.1 / 312.6 / 332.8 / 350.8 / 375.4.

## Field map

Proof crops (read and verified, in `_scratch/`): **c1** = `c1_header_lines.png`,
**c2** = `c2_account_grids.png`, **c3** = `c3_table.png`.

| key | printed label | box (x, yTop, w, h / cells) | align | proof |
|---|---|---|---|---|
| employerName | ชื่อสถานประกอบการ | 166.4, 94.6, 219.0, 12 | left | c1 |
| branchName | ชื่อสาขา (ถ้ามี) | 136.4, 116.2, 248.0, 12 | left | c1 |
| address | ที่ตั้งสำนักงานใหญ่/สาขา | 180.9, 137.8, 205.6, 12 | left | c1 |
| address2 | (continuation dotted line under ที่ตั้งฯ) | 73.0, 159.4, 310.0, 12 | left | c1 |
| postalCode | รหัสไปรษณีย์ | 133.5, 181.1, 39.4, 12 | left | c1 |
| phone | โทรศัพท์ | 217.3, 181.1, 76.7, 12 | left | c1 |
| accountNoCells | เลขที่บัญชี | 10 cells: 571.5, 582.8, 603.8, 615.0, 626.3, 636.8, 648.0, 659.3, 670.5, 690.8 · yTop 111.1, h 14 (grid 109.1–127.1) | centre/cell | c2 |
| branchSeqCells | ลำดับที่สาขา | 6 cells: 571.5, 582.8, 594.0, 605.3, 616.5, 627.8 · yTop 134.5, h 14 (grid 132.5–150.5) | centre/cell | c2 |
| wageMonth | …สำหรับค่าจ้างเดือน | 217.5, 202.6, 89.8, 12 | left | c1 |
| wageYear | พ.ศ. | 326.6, 202.6, 53.4, 12 | left | c1 |
| tblWageBaht / tblWageSatang | 1. เงินค่าจ้างทั้งสิ้น | 271.9, 258.1, 83.0, 12 / 361.9, 258.1, 20.0, 12 | right | c3 |
| tblEmpContribBaht / -Satang | 2. เงินสมทบผู้ประกันตน | 271.9, 279.6, 83.0, 12 / 361.9, 279.6, 20.0, 12 | right | c3 |
| tblEmployerContribBaht / -Satang | 3. เงินสมทบนายจ้าง | 271.9, 298.4, 83.0, 12 / 361.9, 298.4, 20.0, 12 | right | c3 |
| tblTotalBaht / tblTotalSatang | 4. รวมเงินสมทบที่นำส่งทั้งสิ้น | 271.9, 316.7, 83.0, 12 / 361.9, 316.7, 20.0, 12 | right | c3 |
| tblEmployeeCount | 5. จำนวนผู้ประกันตนที่ส่งเงินสมทบ …คน | 271.9, 357.1, 83.0, 12 | right | c3 |
| amountWords | ( …ตัวอักษร… ) row between "(" x1=87.8 and ")" x0=372.3 | 91.0, 335.8, 278.0, 12 | left | c3 |

## Notes

- **accountNoCells = 10 digits** in pattern N N - N N N N N N N - N (the two dashes are
  drawn ticks at x 588.4–598.2 and 676.2–685.2, no cell there). **branchSeqCells = 6 digits.**
- `address2`: extra key beyond the v1 list — the form prints a second full-width dotted
  line (y 154.5–173.0) continuing the address; mapped so long addresses can wrap.
  Fillers that keep the address on one line may ignore it.
- `tblEmployeeCount` goes in the บาท-column cell of row 5; "คน" is pre-printed in the
  rightmost column (x 365.8) — do not stamp it.
- `amountWords` at 12 pt fits ~40 Thai chars; longer amounts-in-words must shrink-to-fit
  (verified: 56-char marker rendered at reduced size stays inside the parens, crop c3).
- No printed "(ตัวอักษร)" label exists — the row is just `( ……… )` directly under table
  row 4 (inside the table box, band y 332.8–350.8).
- อัตราเงินสมทบร้อยละ (y≈154), โทรสาร, เงินเพิ่ม, receipt/signature/date and the
  สำหรับเจ้าหน้าที่ฯ panel (right half, x>444) are out of scope (manual) — not mapped.
- All required v1 keys exist on the form; none omitted.

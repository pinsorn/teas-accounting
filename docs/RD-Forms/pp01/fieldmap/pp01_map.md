# ภ.พ.01 (pp01_010968.pdf) — AcroForm field map (recon 2026-06-12)

> Method = pnd50 fieldmap discipline: label-join dump (`_pp01_fields_p1.txt`, `_pp01_fields_p2.txt`)
> + 0-fill marker raster (`#N` = dump line index) rendered at 190dpi in 280px bands
> (`_scratch/pp01_p{P}_b{NN}.png`) and read crop-by-crop. **Every row below was traced to a read
> raster crop** — the `#N` column is the marker that was visually confirmed next to the printed label.
> Comb geometry: `pp01_cells.json` (PDF points, ascending per-cell centre X; boundaries = drawn
> vertical rules + widget rect borders, validated against `MaxLen`).

**PDF: 3 pages** — p1 = main form (76 widgets: 59 Text, 16 Radio kids, 1 clear Button), p2 = branch
continuation + documents + officer block (114 widgets: 88 Text, 26 Radio kids), p3 = instructions
(0 widgets).

**v1 scope (identity prefill, print-and-sign): 17 PREFILL fields, all on page 1.** Everything on
page 2 is blank-manual or officer-only. v1 ticks NO radio/checkbox.

## ⚠️ Name traps (read before coding the filler)

1. **`Radio Button2` is ONE group spanning TWO unrelated questions**: on-states `0/1/2` = section 1
   person type (บุคคลธรรมดา/ห้างหุ้นส่วนฯ/นิติบุคคล) AND `3..8` = section 2.3 ลักษณะสถานประกอบการ.
   Ticking a 2.3 box clears the section-1 choice (AcroForm radios are mutually exclusive). Form
   defect — do not tick either from code.
2. Field-name numbers do NOT follow visual order (e.g. รหัสไปรษณีย์ is `Text1.26`, ถนน is `Text1.19`).
   **Never address a field by name intuition — use this map.**
3. **Cross-form trap vs ภ.พ.09**: `Text1.18` here = แยก, but in pp09 `Text1.18` = E-mail. Same names,
   different meanings across the two PDFs.
4. Section-4 row 10 swaps marker order: code col = `Text4.10`, desc col = `Text4.20` (confirmed crop
   `pp01_p1_b07.png`).
5. The สำหรับเจ้าหน้าที่ (ISIC-RD) grid (p1 right edge) and สำหรับบันทึกข้อมูลจากระบบ TCL boxes are
   **printed cells only — no widgets** (no markers appeared; officer手-fills on paper).
6. `Button1` (top right) = ล้างข้อมูล JS clear button — never trigger it.

## Radio groups (on-states render-confirmed via red `idx:state` overlay tags in band crops)

| group | kids (state = printed choice) | crop |
|---|---|---|
| Radio Button1 (ยื่นต่อ) | `0`=กองบริหารภาษีธุรกิจขนาดใหญ่ · `1`=สำนักงานสรรพากรพื้นที่ · `2`=สำนักงานสรรพากรพื้นที่สาขา | p1_b00 |
| Radio Button2 (MIXED — trap #1) | `0`=บุคคลธรรมดา · `1`=ห้างหุ้นส่วนสามัญ/คณะบุคคล · `2`=นิติบุคคล · `3`=บ้านพักอาศัย · `4`=อาคารพาณิชย์ · `5`=อาคารสำนักงาน · `6`=อาคารโรงงาน · `7`=อาคารชุด · `8`=อื่นๆ | p1_b01, p1_b03 |
| Radio Button3 (ข้อ 3) | `0`=3.1 กิจการในบังคับ VAT · `1`=3.2 กิจการได้รับยกเว้น (ขอจด ภ.พ.01.1) | p1_b04 |
| Radio Button4 (3.1 เหตุ) | `0`=(1) จดก่อนเริ่มประกอบกิจการ · `1`=(2) รายรับถึงเกณฑ์ | p1_b04 |
| Radio Button5/6/7 (p2 สาขา 1/2/3+4 ลักษณะ) | `0..5` = บ้านพักอาศัย/พาณิชย์/สำนักงาน/โรงงาน/ชุด/อื่นๆ per branch block; **Button7 holds BOTH branch 3 (`0..5`) and branch 4 (`6..11`)** | p2 dump |
| Radio Button8 (officer คำสั่ง) | `Yes`=สำนักงานใหญ่ · `12`=สาขา — ⚠️ mixed 'Yes'/numeric convention | p2 dump |

## Page 1 (76 widgets) — main form

| # | label (Thai) | field name | type | comb | v1 disposition |
|---|---|---|---|---|---|
| 0 | ล้างข้อมูล | Button1 | Btn | — | never touch |
| 26 | ยื่นต่อ: สำนักงานสรรพากรพื้นที่… | Text1.1 | Text | — | blank-manual |
| 27 | …พื้นที่สาขา… | Text1.2 | Text | — | blank-manual |
| 6 | 1. ชื่อผู้ประกอบการ | Text1.3 | Text | — | **PREFILL-LegalName** ✔ traced b00 |
| 24 | เลขประจำตัวผู้เสียภาษีอากร (13 ช่อง) | Text1.4 | Text | **13 cells** | **PREFILL-TaxId** ✔ traced b01 (marker spread in cells) |
| 25 | วัน/เดือน/ปีเกิด (บุคคลธรรมดา) | Text1.5 | Text | — | blank-manual |
| 7 | สัญชาติ | Text1.6 | Text | — | blank-manual |
| 8 | ชื่อภาษาอังกฤษ (ถ้ามี) (นิติบุคคล) | Text1.7 | Text | — | blank-manual (no EN-name source in v1) |
| 9 | วันเดือนปีที่จดทะเบียน | Text1.8 | Text | — | blank-manual |
| 10 | จดทะเบียนที่ | Text1.9 | Text | — | blank-manual |
| 11 | 2.1 ชื่อสถานประกอบการ | Text1.10 | Text | — | **PREFILL-LegalName** (trade name = LegalName) ✔ traced b02 |
| 33 | 2.2 ที่อยู่สำนักงานใหญ่: อาคาร | Text1.11 | Text | — | **PREFILL-Addr.Building** ✔ traced b02 |
| 12 | ห้องเลขที่ | Text1.12 | Text | — | **PREFILL-Addr.RoomNo** ✔ traced b02 |
| 13 | ชั้นที่ | Text1.13 | Text | — | **PREFILL-Addr.Floor** ✔ traced b02 |
| 14 | หมู่บ้าน | Text1.14 | Text | — | **PREFILL-Addr.Village** ✔ traced b02 |
| 15 | เลขที่ | Text1.15 | Text | — | **PREFILL-Addr.HouseNo** ✔ traced b02 |
| 16 | หมู่ที่ | Text1.16 | Text | — | **PREFILL-Addr.Moo** ✔ traced b02 |
| 17 | ตรอก/ซอย | Text1.17 | Text | — | **PREFILL-Addr.Soi** ✔ traced b02 |
| 18 | แยก | Text1.18 | Text | — | blank-manual (TEAS has no แยก field) |
| 19 | ถนน | Text1.19 | Text | — | **PREFILL-Addr.Road** ✔ traced b02 |
| 20 | ตำบล/แขวง | Text1.20 | Text | — | **PREFILL-Addr.Subdistrict** ✔ traced b02/b03 |
| 34 | อำเภอ/เขต | Text1.21 | Text | — | **PREFILL-Addr.District** ✔ traced b03 |
| 35 | จังหวัด | Text1.22 | Text | — | **PREFILL-Addr.Province** ✔ traced b03 |
| 4 | รหัสไปรษณีย์ (5 ช่อง) | Text1.26 | Text | **5 cells** | **PREFILL-Addr.PostalCode** ✔ traced b03 (marker in cells) |
| 36 | โทรศัพท์ | Text1.23 | Text | — | blank-manual (phone not in v1 identity list — candidate for v2) |
| 37 | E-mail Address | Text1.24 | Text | — | **PREFILL-Email** ✔ traced b03 |
| 38 | Website | Text1.25 | Text | — | **PREFILL-Website** ✔ traced b03 |
| 40 | 2.3 อื่นๆ (ระบุ) | Text2.1 | Text | — | blank-manual |
| 41 | 2.4 สาขา จำนวนทั้งสิ้น…สาขา | Text2.2 | Text | — | blank-manual |
| 44/45/46 | 3.1(2) วันที่/เดือน/พ.ศ. | Text3.1/3.2/3.3 | Text | — | blank-manual ✔ b04 |
| 48/49/50 | 3.2 ภ.พ.01.1 เมื่อวันที่/เดือน/พ.ศ. | Text3.4/3.5/3.6 | Text | — | blank-manual ✔ b04/b05 |
| 51 | เงินทุนจดทะเบียน…บาท | Text3.7 | Text | — | blank-manual ✔ b05 |
| 52 | รายรับประมาณเดือนละ…บาท | Text3.8 | Text | — | blank-manual ✔ b05 |
| 53,55,57,59,61,63,65,67,69,72 | 4. ลำดับ 1-10 รหัสประเภทกิจการ | Text4.1 … Text4.10 | Text | — | blank-manual ✔ b05-b07 (row 10 = #72, trap #4) |
| 54,56,58,60,62,64,66,68,70,71 | 4. ลำดับ 1-10 ประเภทสินค้า/บริการ | Text4.11 … Text4.20 | Text | — | blank-manual ✔ b05-b07 (row 10 = #71) |
| 73 | 5. เอกสารที่แนบ จำนวน…ฉบับ | Text5.1 | Text | — | blank-manual ✔ b07 |
| 74 | ลงชื่อ (ชื่อในวงเล็บ) ผู้ประกอบการ | Text5.2 | Text | — | blank-manual (signature block; ลงชื่อ line itself has NO widget — hand-signed) ✔ b08 |
| 75 | วันที่ (ใต้ลายเซ็น) | Text5.3 | Text | — | blank-manual ✔ b08 |

Radio kids on p1 (all NEVER ticked by v1): see radio table above; dump line #s 1-3, 21-23, 28-32, 5,
39, 42-43, 47.

## Page 2 (114 widgets) — branch continuation (ต่อจาก 2.4) + documents + officer

All blank-manual / officer-only. Four identical branch blocks (สาขาที่ 00001-00004), each:
ชื่อสถานประกอบการ, อาคาร, ห้องเลขที่, ชั้นที่, หมู่บ้าน, เลขที่, หมู่ที่, ตรอก/ซอย, แยก, ถนน,
ตำบล/แขวง, อำเภอ/เขต, จังหวัด, รหัสไปรษณีย์ (**5-cell comb**), โทรศัพท์, E-mail
(✔ traced crop p2_b00 for block 1; blocks 2-4 are byte-identical layouts at y+96pt steps).

| block | fields | postal comb |
|---|---|---|
| สาขาที่ 00001 | Text6.1 … Text6.17 | Text6.13 (5 cells) |
| สาขาที่ 00002 | Text7.1 … Text7.17 | Text7.13 (5 cells) |
| สาขาที่ 00003 | Text8.1 … Text8.17 | Text8.13 (5 cells) |
| สาขาที่ 00004 | Text9.1 … Text9.17 | Text9.13 (5 cells) |

Documents block (✔ traced crop p2_b05): Text10.1-Text10.8 = จำนวน…ฉบับ per document line 1-8 —
blank-manual.

Officer block (✔ traced crop p2_b08): Text11.4/11.5/11.6 ความเห็นเจ้าหน้าที่ · Radio Button8 คำสั่ง
(`Yes`=สำนักงานใหญ่, `12`=สาขา) · Text11.7-11.10 ใบทะเบียนฯ ฉบับ/ตั้งแต่วันที่/เดือน/พ.ศ. ·
Text11.1/11.2/11.3 ลงชื่อเจ้าหน้าที่(ชื่อ)/ตำแหน่ง? · Text11.12/11.13 (ชื่อ)/ตำแหน่ง ผู้มีอำนาจลงนาม —
**all officer-only**.

## pp01_cells.json (all values ascending, grid-derived)

| field | cells | meaning |
|---|---|---|
| Text1.4 | 13 | เลขประจำตัวผู้เสียภาษีอากร (PREFILL) |
| Text1.26 | 5 | รหัสไปรษณีย์ HQ (PREFILL) |
| Text6.13 / Text7.13 / Text8.13 / Text9.13 | 5 each | branch postal codes (blank-manual) |

## Cited crops kept in `_scratch/`

pp01_p1_b00…b09 (all 10), pp01_p2_b00, pp01_p2_b05, pp01_p2_b08.

## Sibling PDFs in `docs/RD-Forms/pp01/` (out of scope this recon)

`pp01.1_010968.pdf` (ขอใช้สิทธิจด VAT ก่อนถึงเกณฑ์), `pp01.2_010968.pdf`, `pp02_010968.pdf`,
`pp04_010968.pdf`, `pp08_010968.pdf` — not mapped.

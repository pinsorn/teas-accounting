# ภ.พ.09 (pp09_010968.pdf) — AcroForm field map (recon 2026-06-12)

> Method = pnd50 fieldmap discipline: label-join dump (`_pp09_fields_p{1-4}.txt`) + 0-fill marker
> raster (`#N` = dump index per page) at 190dpi in 280px bands (`_scratch/pp09_p{P}_b{NN}.png`),
> read crop-by-crop. **Every PREFILL row was traced to a read raster crop.** Comb geometry:
> `pp09_cells.json` (PDF points, ascending centre X, grid-derived unless noted).

**PDF: 4 pages, 299 widgets** — p1 = identity header + change-type checkboxes + ข้อ 2-3 (118 widgets:
88 Text, 29 Radio kids, 1 clear Button) · p2 = ข้อ 4-8 (106: 93 Text, 13 Radio) · p3 = ข้อ 9-15 +
signature (65: 49 Text, 16 Radio) · p4 = officer block (10 Text).

**v1 scope: 17 PREFILL fields, all in the page-1 identity header.** The whole "รายการที่ขอ
เปลี่ยนแปลง" body (ข้อ 2-15) is blank-manual; p4 is officer-only. v1 ticks NO radio/checkbox.

## ⚠️ Name traps

1. **`Text1.x` numbering is scrambled vs visual order** — the y-band label join misassigned several;
   the raster is ground truth. E.g. `Text1.6` = เลขที่ (HouseNo!), `Text1.8` = ตำบล/แขวง,
   `Text1.10` = อำเภอ/เขต, `Text1.13` = ห้องเลขที่. Use the table below verbatim.
2. **Cross-form trap vs ภ.พ.01**: here `Text1.18` = E-mail; in pp01 `Text1.18` = แยก.
3. **`Text1.18` (E-mail) has `MaxLen=12` + comb flag** — a real email almost never fits 12 comb
   cells; it renders letter-spaced. Filler session must decide: strip comb/maxlen via field-flag
   edit, or overlay-draw. Recorded as-is here.
4. **`Radio Button2` (มีความประสงค์แจ้งการเปลี่ยนแปลง) on-states are NON-sequential**
   (render-confirmed p1_b03/b04): left column top→bottom = `0,1,2,9,10,11,12`; right column =
   `3,4,5,6,7,8`. Also it is ONE exclusive group — the paper form allows multiple ticks, the
   AcroForm does not. Never tick from code.
5. **Mixed on-state conventions on the same pages**: pair groups use `Yes`/`2` (Radio Button4, 5, 12
   = สำนักงานใหญ่/สาขาที่ or โอนให้แก่/รับโอนจาก), other pair groups use `0`/`1` (Radio Button3, 10,
   16, 17), 6-kid ลักษณะ groups use `0..5`, and standalone checkboxes are `Yes` (Radio Button7, 8,
   9, 11, 14, 15, 18-27). Never assume.
6. `Button1` = ล้างข้อมูล clear button — never trigger.
7. สำหรับบันทึกข้อมูลจากระบบ TCL box (p1 right) has no widgets.

## Page 1 — identity header (ข้อ 1) — THE v1 PREFILL ZONE

| # | label (Thai) | field name | type | comb | v1 disposition |
|---|---|---|---|---|---|
| 16 | ยื่นต่อ: สำนักงานสรรพากรพื้นที่… | Text1.1 | Text | — | blank-manual ✔ b00 |
| 17 | …พื้นที่สาขา… | Text1.2 | Text | — | blank-manual ✔ b00 |
| 18 | 1. ชื่อผู้ประกอบการจดทะเบียน | Text1.3 | Text | — | **PREFILL-LegalName** ✔ traced b00 |
| 20 | เลขประจำตัวผู้เสียภาษีอากร (13 ช่อง) | Text1.4 | Text | **13 cells** | **PREFILL-TaxId** ✔ traced b01 (marker spread in cells) |
| 15 | ชื่อสถานประกอบการ | Text1.5 | Text | — | **PREFILL-LegalName** (trade name) ✔ traced b01 |
| 19 | ที่อยู่สำนักงานใหญ่: อาคาร | Text1.7 | Text | — | **PREFILL-Addr.Building** ✔ traced b01 |
| 30 | ห้องเลขที่ | Text1.13 | Text | — | **PREFILL-Addr.RoomNo** ✔ traced b01 |
| 21 | ชั้นที่ | Text1.12 | Text | — | **PREFILL-Addr.Floor** ✔ traced b01 |
| 22 | หมู่บ้าน | Text1.9 | Text | — | **PREFILL-Addr.Village** ✔ traced b01 |
| 23 | เลขที่ | Text1.6 | Text | — | **PREFILL-Addr.HouseNo** ✔ traced b01 |
| 24 | หมู่ที่ | Text1.15 | Text | — | **PREFILL-Addr.Moo** ✔ traced b01 |
| 25 | ตรอก/ซอย | Text1.11 | Text | — | **PREFILL-Addr.Soi** ✔ traced b01 |
| 26 | แยก | Text1.16 | Text | 5 (equal-div, comb flag, MaxLen=5) | blank-manual (no แยก source) ✔ b01 |
| 27 | ถนน | Text1.14 | Text | — | **PREFILL-Addr.Road** ✔ traced b01 |
| 28 | ตำบล/แขวง | Text1.8 | Text | — | **PREFILL-Addr.Subdistrict** ✔ traced b01/b02 |
| 29 | อำเภอ/เขต | Text1.10 | Text | — | **PREFILL-Addr.District** ✔ traced b02 |
| 31 | จังหวัด | Text1.21 | Text | — | **PREFILL-Addr.Province** ✔ traced b02 |
| 4 | รหัสไปรษณีย์ (5 ช่อง) | Text1.26 | Text | **5 cells** | **PREFILL-Addr.PostalCode** ✔ traced b02 (marker in cells) |
| 32 | โทรศัพท์ | Text1.17 | Text | — | blank-manual (phone not in v1 list — v2 candidate) ✔ b02 |
| 33 | E-mail Address | Text1.18 | Text | 12 (equal-div, comb flag — trap #3) | **PREFILL-Email** ⚠ MaxLen=12 ✔ traced b02 |
| 34 | Website | Text1.19 | Text | — | **PREFILL-Website** ✔ traced b02 |

## Page 1 — มีความประสงค์แจ้งการเปลี่ยนแปลง (Radio Button2, never ticked by v1)

Render-confirmed states (crops p1_b03/b04): `0`=เปลี่ยนแปลงรายการของที่ตั้งฯ(กรอก 2) ·
`1`=ย้ายสถานประกอบการ(3) · `2`=เลิก/โอนทั้งหมด/ควบกิจการ(4) · `9`=โอนกิจการบางส่วน(5) ·
`10`=เปลี่ยนแปลงประเภทกิจการ(6) · `11`=เพิ่มจำนวนสาขา(7) · `12`=ลดจำนวนสาขา(8) ·
`3`=แปรสภาพกิจการ(9) · `4`=เปลี่ยนชื่อผู้ประกอบการ(10) · `5`=เปลี่ยนชื่อสถานประกอบการ(11) ·
`6`=หยุดชั่วคราวเกิน 30 วัน(12) · `7`=ผู้ประกอบการถึงแก่ความตาย(13) · `8`=เปลี่ยนแปลงอื่นๆ(14).
`Text1.20` (#38) + `Text1.22` (#39) = อื่นๆ (ระบุ) lines — blank-manual.

## Page 1 — ข้อ 2 เปลี่ยนแปลงรายการของที่ตั้ง / ข้อ 3 ย้ายสถานประกอบการ (all blank-manual)

✔ traced crops p1_b04-b08. Radio Button3 `0`=สำนักงานใหญ่ `1`=สาขาที่ (ข้อ 2);
Radio Button4 `Yes`=สำนักงานใหญ่ `2`=สาขาที่ (ข้อ 3 — mixed convention!);
Radio Button5 `Yes`=ย้ายใน / `2`=ย้ายต่าง หน่วยจดทะเบียนฯ;
Radio Button6 `0..5` = ลักษณะสถานประกอบการ (บ้านพักอาศัย…อื่นๆ, `Text3.32`=อื่นๆ ระบุ);
Radio Button7 `Yes` = ขอรับใบทะเบียนฯ สำหรับสถานประกอบการแห่งใหม่ (single checkbox).

Key fields: ข้อ 2 — `Text2.0` สาขาที่ (**5-cell comb**), `Text2.1/2.2/2.3` วันที่/เดือน/พ.ศ.,
จาก: `Text2.4-2.15` + `Text2.16` postal (**5 comb**), เป็น: `Text2.17-2.24, 2.27-2.31` + `Text2.25`
postal (**5 comb**). ข้อ 3 — `Text3.0` สาขาที่ (**5 comb**), `Text3.1/3.2/3.3` dates, จาก:
`Text3.4-3.15` + `Text3.16` postal (**5 comb**), ย้ายไปเปิดแห่งใหม่: `Text3.17-3.29, 3.31` +
`Text3.30` postal (**5 comb**), `Text3.32` อื่นๆ.

## Page 2 — ข้อ 4-8 (all blank-manual) ✔ traced crops p2_b00-b08

- **ข้อ 4.1 เลิกประกอบกิจการ** `Radio Button8`=Yes; เมื่อวันที่/เดือน/พ.ศ. = `Text4.1/4.2/4.3`;
  ผู้ชำระบัญชีชื่อ `Text4.13`; โทรศัพท์ `Text4.4`; เลขประจำตัวผู้เสียภาษีอากร `Text4.01`
  (**13-cell comb**). (crop b00: #7/#8/#9/#10/#11/#2)
- **ข้อ 4.2 โอนกิจการทั้งหมด** `Radio Button9`=Yes; วันที่=`Text4.5`, เดือน=`Text4.7`,
  พ.ศ.=`Text4.6` (⚠ names swapped vs visual order); `Radio Button10` `0`=โอนให้แก่ `1`=รับโอนจาก;
  ชื่อ `Text4.8`; taxid `Text4.02` (**13 comb**). (crop b01: #12/#13/#14/#15/#5)
- **ข้อ 4.3 ควบกิจการ** `Radio Button11`=Yes; วันที่=`Text4.9`, เดือน=`Text4.10`, พ.ศ.=`Text4.14`;
  จำนวน…ราย `Text4.16`; ควบเข้ากันกับบริษัทฯ `Text4.11` + taxid `Text4.03` (**13 comb**);
  ควบเป็นบริษัทฯ `Text4.12` + taxid `Text4.04` (**13 comb**). (crops b01/b02: #16-#21, #38, #39)
- **ณ วันเลิก/โอน/ควบ มี (1)-(6)** = `Text4.17-4.22` (**12-cell equal-div combs**, amounts บาท):
  (1) รายรับที่ยังมิได้ชำระภาษี `Text4.17` · (4) ทรัพย์สินอื่น `Text4.20` · (2) สินค้าคงเหลือ
  `Text4.18` · (5) ลูกหนี้ `Text4.21` · (3) เครื่องจักร `Text4.19` · (6) เจ้าหนี้ `Text4.22`.
- **ข้อ 5 โอนกิจการบางส่วน**: dates `Text5.2/5.3/5.4` (⚠ order on paper = วันที่ #31, เดือน #30,
  พ.ศ. #29 → `Text5.2`=วันที่, `Text5.3`=เดือน, `Text5.4`=พ.ศ.); `Radio Button12` `Yes`=โอนให้แก่
  `2`=รับโอนจาก; ชื่อ `Text5.1`; taxid `Text5.01` (**13 comb**).
- **ข้อ 6 เปลี่ยนแปลงประเภทกิจการ**: dates `Text6.21/6.22/6.23`; ladder 1-10 desc =
  `Text6.11-6.20`, code = `Text6.1-6.10` — ⚠ rows 1-2 of the code column are SWAPPED on the canvas:
  row1=`Text6.1`(#37), row2=`Text6.2`(#36) per crop b04. ISIC-RD officer grid = printed only.
- **ข้อ 7 เพิ่มจำนวนสาขา**: จำนวน…สาขา `Text7.1` (#28); สาขาที่ `Text7.01` (#61 — ⚠ printed
  5-cell grid but the widget is PLAIN text, no MaxLen); วันที่=`Text7.7`(#65), เดือน=`Text7.3`(#63),
  พ.ศ.=`Text7.20`(#66) (⚠ swapped names); ชื่อสถานประกอบการ `Text7.2`; address `Text7.4-7.21` +
  postal `Text7.26` (**5 comb**, #64); โทรศัพท์/E-mail `Text7.17/7.18`; `Radio Button13` `0..5`
  ลักษณะฯ + `Text7.19` อื่นๆ. (crops b06/b07)
- **ข้อ 8 ลดจำนวนสาขา**: จำนวน…สาขา `Text8.1` (#97); สาขาที่ `Text8.01` (#103 — same plain-widget-
  in-printed-grid trap); เมื่อวันที่/เดือน/พ.ศ. = `Text8.2/8.3/8.4`; address `Text8.5-8.16` + postal
  `Text8.26` (**5 comb**, #105). (crop b08)

## Page 3 — ข้อ 9-15 + signature (all blank-manual) ✔ traced crops p3_b00-b07

- **ข้อ 9 แปรสภาพกิจการ**: dates `Text9.1/9.2/9.3`; ชื่อนิติบุคคล(เดิม) `Text9.4`; แปรสภาพเป็น
  `Text9.5`; เลขทะเบียนนิติบุคคล เดิม `Text9.01` / ใหม่ `Text9.02` (**13-cell combs**).
- **ข้อ 10 เปลี่ยนชื่อผู้ประกอบการ**: dates `Text10.1/10.2/10.3`; `Radio Button14`=Yes
  (บุคคลธรรมดาฯ): คำนำหน้าเดิม `Text10.4`→`Text10.5`, ชื่อเดิม `Text10.6`→`Text10.7`, ชื่อกลางเดิม
  `Text10.8`→`Text10.9`, นามสกุลเดิม `Text10.10`→`Text10.11`; `Radio Button15`=Yes (นิติบุคคล):
  ชื่อเดิม `Text10.12`→`Text10.13`.
- **ข้อ 11 เปลี่ยนชื่อสถานประกอบการ**: จำนวน…แห่ง `Text11.1`; `Radio Button16` `0`=สำนักงานใหญ่
  `1`=สาขาที่ + `Text11.01` (**5 comb**); dates `Text11.2/11.3/11.4`; ชื่อเดิม `Text11.5`→เปลี่ยนเป็น
  `Text11.6`.
- **ข้อ 12 หยุดชั่วคราวเกิน 30 วัน**: จำนวน…แห่ง `Text12.1`; `Radio Button17` `0`=สำนักงานใหญ่
  `1`=สาขาที่ + `Text12.01` (**5 comb**); ตั้งแต่ `Text12.2/12.3/12.4` ถึง `Text12.5/12.6/12.7`.
- **ข้อ 13 ผู้ประกอบการถึงแก่ความตาย**: dates `Text13.1/13.2/13.3`; `Radio Button18`=Yes
  (ผู้จัดการมรดก/ทายาทจะประกอบกิจการต่อ).
- **ข้อ 14 เปลี่ยนแปลงอื่นๆ**: dates `Text14.1/14.2/14.3`; เดิม `Text14.4`+`Text14.5`;
  เปลี่ยนแปลงเป็น `Text14.6`+`Text14.7`.
- **ข้อ 15 เอกสารแนบ**: จำนวน…ฉบับ `Text15.1`; checkboxes `Radio Button19-27` ALL on-state `Yes`
  (ใบทะเบียนฯ, หนังสือเปลี่ยนชื่อ, มอบอำนาจ, สัญญาเช่า, รับรองนิติบุคคลอาคารชุด, แผนที่,
  รับรองนายทะเบียนหุ้นส่วนบริษัท, ยินยอมให้ใช้, อื่นๆ); อื่นๆ ระบุ `Text15.2`.
- **Signature**: (ชื่อ) `Text15.3`; ยื่นวันที่ `Text15.4` — blank-manual (ลงชื่อ line has no widget;
  hand-signed + seal).

## Page 4 — officer-only (10 Text) ✔ traced crops p4_b00/b01

ความเห็นเจ้าหน้าที่ `Text15.5`+`Text15.6` · คำสั่ง `Text15.7`+`Text15.8` · left ลงชื่อเจ้าหน้าที่:
(ชื่อ) `Text15.9`, ตำแหน่ง `Text15.10`, วันที่ `Text15.11` · right ผู้มีอำนาจลงนาม: (ชื่อ)
`Text15.12`, ตำแหน่ง `Text15.13`, วันที่ `Text15.14`.

## pp09_cells.json (27 fields, ascending)

13-cell (grid): `Text1.4` (taxid — PREFILL), `Text4.01-4.04`, `Text5.01`, `Text9.01`, `Text9.02`.
5-cell (grid): `Text1.26` (postal — PREFILL), `Text2.0`, `Text2.16`, `Text2.25`, `Text3.0`,
`Text3.16`, `Text3.30`, `Text7.26`, `Text8.26`, `Text11.01`, `Text12.01`.
Equal-division (comb flag, no drawn grid): `Text1.16` (5), `Text1.18` (12 — the email trap),
`Text4.17-4.22` (12 each, amount boxes).

## Cited crops kept in `_scratch/`

pp09_p1_b00-b08, pp09_p2_b00-b08, pp09_p3_b00-b07, pp09_p4_b00-b01.

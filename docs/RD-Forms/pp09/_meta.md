# ภ.พ.09 — แจ้งเปลี่ยนแปลงทะเบียน VAT

## Identifiers
- **RD code:** ภ.พ.09 (PP09 / P.P.09)
- **Thai name:** แบบแจ้งการเปลี่ยนแปลงทะเบียนภาษีมูลค่าเพิ่มตามประมวลรัษฎากร
- **English name:** Form for Notifying VAT Registration Changes
- **Purpose:** แจ้งเปลี่ยนแปลงข้อมูลของผู้ประกอบการ VAT (ที่อยู่, ชื่อกิจการ, สาขา ฯลฯ)
- **Version (latest):** ฉบับ 2568

## Filing
- **Frequency:** As-needed (ทุกครั้งที่มีการเปลี่ยนแปลง)
- **Deadline:** ภายใน **15 วัน** นับแต่วันที่มีการเปลี่ยนแปลง
- **Where to file:** District Revenue Office / efiling.rd.go.th
- **Legal basis:** ม.85/6

## TEAS relevance
- **Module:** Master / Company info update
- **Trigger:** Settings → Company Profile update (address, branch)

## Use cases
- ย้ายที่อยู่สำนักงานใหญ่
- เพิ่ม/ลบสาขา
- เปลี่ยนชื่อบริษัท
- เปลี่ยนประเภทธุรกิจ
- เลิกประกอบกิจการ บางสาขา (เลิกทั้งบริษัทใช้ ภ.พ.08)

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp09_010968.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp09_010968.zip
- **Source page:** https://www.rd.go.th/62386.html

## Related
- ภ.พ.08 = ถอนทะเบียน (de-register) → https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp08_010968.pdf
- ภ.พ.04 = ขอใบแทน ภ.พ.20

## Fieldmap recon 2026-06-12
- Render-verified AcroForm map: `fieldmap/pp09_map.md` · comb cell geometry: `fieldmap/pp09_cells.json`.
- 4 PDF pages, 299 widgets (p1=118, p2=106, p3=65, p4=10 officer-only).
- **17 v1 PREFILL fields** (identity header, p1 only): Text1.3 (LegalName), Text1.4 (TaxId, 13-cell
  comb), Text1.5 (trade name), Text1.6-1.15/1.21 (address — ⚠ scrambled numbering, see map),
  Text1.26 (postal, 5-cell comb), Text1.18 (Email — ⚠ MaxLen=12 comb), Text1.19 (Website).
  All "รายการที่ขอเปลี่ยนแปลง" sections (ข้อ 2-15) = blank-manual; no radios ticked.
- ⚠️ Traps: `Radio Button2` (change-type) has NON-sequential on-states (left col 0,1,2,9,10,11,12;
  right col 3-8) and is one exclusive group; mixed 'Yes'/numeric conventions across pair groups.
- Raw dumps `fieldmap/_pp09_fields_p{1-4}.txt`; cited band crops in `fieldmap/_scratch/`.

## Download status
- ✅ PDF present in this folder

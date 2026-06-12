# ภ.พ.01 — คำขอจดทะเบียน VAT

## Identifiers
- **RD code:** ภ.พ.01 (PP01 / P.P.01)
- **Thai name:** แบบคำขอจดทะเบียนภาษีมูลค่าเพิ่มตามประมวลรัษฎากร
- **English name:** Form for Value Added Tax Registrant Under the Revenue Code
- **Purpose:** ยื่นจดทะเบียนเป็นผู้ประกอบการ VAT
- **Version (latest):** ฉบับ 2568 (issued 1 ก.ย. 2568)

## Filing
- **Frequency:** One-time (เปลี่ยนแปลงใช้ ภ.พ.09)
- **Deadline:** ภายใน **30 วัน** นับแต่วันที่รายได้ถึงเกณฑ์ 1,800,000 บาท/ปี (หรือก่อนเริ่มประกอบกิจการ สำหรับ voluntary)
- **Where to file:** District Revenue Office / efiling.rd.go.th
- **Legal basis:** ม.85

## TEAS relevance
- **Module:** Master / Onboarding
- **Trigger:** ตอน company onboarding หรือ tax_id assignment
- **Result:** TEAS เก็บ `vat_registration_date` + `tin` + `branch_code 00000`

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp01_010968.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp01_010968.zip
- **English PDF:** https://www.rd.go.th/fileadmin/download/english_form/PP01_151062.pdf
- **Source page:** https://www.rd.go.th/62386.html

## Related variants
- ภ.พ.01.1 = ขอใช้สิทธิ์เพื่อขอจดทะเบียน (https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp01.1_010968.pdf)
- ภ.พ.01.2 = จดทะเบียนชั่วคราว (https://www.rd.go.th/fileadmin/tax_pdf/request/2568/pp01.2_010968.pdf)
- ภ.พ.01.3 = แจ้งประกอบกิจการค้าทองคำ
- ภ.พ.01.5 = แจ้งประกอบกิจการนำเข้า/ขายอัญมณี ทองคำขาว

## Notes
- หลังจด สรรพากรออก ภ.พ.20 (ใบทะเบียนภาษีมูลค่าเพิ่ม) ให้
- ก่อนวันจดทะเบียน effective: **ห้ามออกใบกำกับภาษี** (ม.90/4 อาญา)

## Fieldmap recon 2026-06-12
- Render-verified AcroForm map: `fieldmap/pp01_map.md` · comb cell geometry: `fieldmap/pp01_cells.json`.
- 3 PDF pages (p3 = instructions, 0 widgets). p1 = 76 widgets, p2 = 114.
- **17 v1 PREFILL fields** (identity only, all p1): Text1.3 (LegalName), Text1.4 (TaxId, 13-cell comb),
  Text1.10 (trade name), Text1.11-1.17/1.19-1.22 + Text1.26 (address, postal = 5-cell comb),
  Text1.24 (Email), Text1.25 (Website). Everything else blank-manual/officer-only; no radios ticked.
- ⚠️ Traps: `Radio Button2` mixes person-type (0-2) and ลักษณะสถานประกอบการ (3-8) in one group;
  field names don't follow visual order; `Text1.18` = แยก here but = E-mail in ภ.พ.09.
- Raw dumps `fieldmap/_pp01_fields_p{1,2}.txt`; cited band crops in `fieldmap/_scratch/`.

## Download status
- ✅ PDFs present in this folder (pp01, pp01.1, pp01.2, pp02, pp04, pp08 — only pp01 fieldmapped)

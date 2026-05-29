# ภ.พ.36 — แบบนำส่งภาษีมูลค่าเพิ่ม (Self-assess VAT for Import Service)

## Identifiers
- **RD code:** ภ.พ.36 (PP36)
- **Thai name:** แบบนำส่งภาษีมูลค่าเพิ่ม ตามประมวลรัษฎากร
- **English name:** VAT Remittance Form (for import of services / non-resident payments)
- **Purpose:** ผู้จ่ายเงินในไทยนำส่ง VAT 7% เมื่อจ่ายค่าบริการให้ผู้ให้บริการต่างประเทศที่ไม่ได้จดทะเบียน VAT ในไทย
- **Version (latest):** ฉบับ 2568 (issued 1 ก.ย. 2568)

## Filing
- **Frequency:** Monthly — ยื่นเฉพาะเดือนที่มีการจ่ายให้ผู้ให้บริการต่างประเทศ
- **Deadline:**
  - Paper: ภายในวันที่ **7** ของเดือนถัดไป
  - e-Filing: ภายในวันที่ **15** ของเดือนถัดไป
- **Where to file:** District Revenue Office / efiling.rd.go.th
- **Legal basis:** ประมวลรัษฎากร ม.83/6 + ม.77/2

## TEAS relevance
- **Module:** Tax / AP (Foreign vendor payments)
- **Trigger:** ทุกครั้งที่ PV / VI มี vendor ที่ `is_foreign = true` + `has_thai_vat_d_reg = false` + ค่าบริการ
- **Generated from:** Foreign-vendor PV ที่ Posted ในเดือนนั้น
- **Impact on Input VAT:**
  - VAT-registered company: VAT ที่จ่ายผ่าน ภ.พ.36 → claim เป็น Input VAT ในเดือน **ถัดไป** (1 เดือนต้องรอ)
  - Non-VAT company: sunk cost → debit `5350 ค่าภาษีซื้อต้องห้าม` (per CLAUDE.md §3b non-VAT spec)

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp36_010968.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp36_010968.zip
- **Source page:** https://www.rd.go.th/62381.html

## Common use cases
- ค่า SaaS ต่างประเทศ (AWS, Google Cloud, Microsoft, Slack)
- ค่าโฆษณา online (Google Ads, Facebook Ads)
- ค่าที่ปรึกษาต่างประเทศ
- ค่า license software / royalty

## Notes
- บริษัท Non-VAT ก็ต้องยื่น ภ.พ.36 (ไม่ใช่เฉพาะ VAT-registered)
- คำนวณคู่ขนานกับ ภ.ง.ด.54 (WHT จ่ายต่างประเทศ 15% บนเดิม)

## Download status
- ⚠️ Binary not downloaded · URL verified

# ภ.ง.ด.54 — WHT จ่ายไปต่างประเทศ (ม.70) รายเดือน

## Identifiers
- **RD code:** ภ.ง.ด.54 (PND54 / P.N.D.54)
- **Thai name:** แบบยื่นรายงานนำส่งภาษีเงินได้นิติบุคคล และการจำหน่ายเงินกำไร ตามมาตรา 70 และตามมาตรา 70 ทวิ
- **English name:** Corporate Income Tax Remittance Form (Foreign Payment / Profit Remittance)
- **Purpose:** ผู้จ่ายในไทยนำส่ง WHT บนการจ่ายค่าบริการ/ค่าสิทธิ/ดอกเบี้ย/ปันผล ฯลฯ ให้ผู้รับต่างประเทศที่ไม่มี PE ในไทย + จำหน่ายเงินกำไรของสาขาต่างประเทศ
- **Version (latest):** ฉบับ 2568

## Filing
- **Frequency:** Monthly — ยื่นเฉพาะเดือนที่มีการจ่าย
- **Deadline:**
  - Paper: **7** ของเดือนถัดไป
  - e-Filing: **15** ของเดือนถัดไป
- **Where to file:** District Revenue Office / efiling.rd.go.th
- **Legal basis:** ม.70 (services / royalties / interest to foreign) · ม.70 ทวิ (profit remittance)

## TEAS relevance
- **Module:** AP / Tax / Foreign payments
- **Trigger:** Payment Voucher (PV) ที่จ่ายให้ vendor `is_foreign = true`
- **Common rates (default before DTA):**
  - ค่าบริการ: **15%**
  - ค่าสิทธิ / royalty: **15%**
  - ดอกเบี้ย: **15%**
  - เงินปันผล: **10%**
- **DTA exception:** ถ้ามี Double Tax Agreement + Tax Residency Certificate → อาจลดอัตรา
- **Parallel with:** ภ.พ.36 (VAT self-assess 7% บนเดิม) — มักยื่นคู่กัน
- **Self-withhold:** หาก gross-up ภาระภาษีให้ผู้รับ → คำนวณบน amount ที่ gross-up

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT54.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/cit/2568/050369CIT54.zip
- **Source page:** https://www.rd.go.th/62375.html ← Note: filed under **CIT page**, not WHT page (because BE is treated as remitting CIT on behalf of foreign recipient)

## Notes
- TEAS seed `FOR-SVC`, `FOR-ROYAL` whtTypes = PND54
- ผู้รับเงิน = foreign company without PE → not file Thai CIT themselves; payer remits via PND54

## Download status
- ⚠️ Binary not downloaded · URL verified

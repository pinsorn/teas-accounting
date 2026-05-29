# 50 ทวิ — หนังสือรับรองการหักภาษี ณ ที่จ่าย

## Identifiers
- **RD code:** 50 ทวิ (50 Tawi / WHT Certificate)
- **Thai name:** หนังสือรับรองการหักภาษี ณ ที่จ่าย ตามมาตรา 50 ทวิ
- **English name:** Withholding Tax Certificate
- **Purpose:** เอกสารที่ผู้หักภาษีออกให้ผู้รับเงิน รับรองว่าได้หัก WHT จำนวนเท่าใด (ผู้รับใช้เป็นหลักฐานเครดิตภาษีตอนยื่นแบบประจำปี)

## Filing
- **Frequency:** Per transaction — ออกทุกครั้งที่มีการหัก (จะ aggregate รายปีในรูป annual cert ก็ได้สำหรับพนักงาน)
- **Deadline:**
  - Individual payment: ทันทีที่จ่ายเงิน (ผู้รับเก็บไว้)
  - Annual summary (พนักงาน): ภายในวันสุดท้ายของ ก.พ. ปีถัดไป (พร้อม ภ.ง.ด.1ก)
- **Where to file:** ไม่ต้องยื่นกรมสรรพากร — เก็บฝั่งผู้หัก (สำเนา) + ส่งให้ผู้รับ (ต้นฉบับ)
- **Legal basis:** ประมวลรัษฎากร ม.50 ทวิ
- **Penalty for non-issuance:** ม.35 — ปรับ ≤ 2,000 บาท + อาญา

## TEAS relevance
- **Module:** AP / HR
- **Trigger:** Payment Voucher (PV) Post → ถ้าค่า WHT > 0 → auto-generate 50 ทวิ
- **Generated from:** PV ที่ posted + มี WHT line items
- **Format:** TEAS Phase 1 = bespoke RD-mandated layout (ไม่ใช้ generic PaperDocument)
- **Storage:** เก็บ PDF + XML (e-WHT format) ≥ 5 ปี (append-only)

## Required fields (per ม.50 ทวิ)
1. **ผู้หักภาษี (Withholder):** ชื่อ · ที่อยู่ · TIN 13 หลัก · สาขา 5 หลัก
2. **ผู้ถูกหักภาษี (Withholdee):** ชื่อ · ที่อยู่ · TIN/บัตรประชาชน · สาขา (ถ้านิติบุคคล)
3. **ประเภทเงินได้:** อ้างมาตรา ม.40 + รหัสประเภท
4. **ฐานเงินได้** (จำนวนเงินก่อนหัก)
5. **อัตรา %**
6. **WHT amount**
7. **วันที่จ่าย**
8. **ลำดับใน 50 ทวิ** ของรอบ
9. **วิธีจ่ายเงิน (tick):** □ หัก ณ ที่จ่าย □ ออกให้ตลอดไป □ ออกให้ครั้งเดียว □ อื่นๆ
10. **ลายมือชื่อผู้หัก** (สำคัญ — ไม่มี = ไม่ valid)
11. **แบบยื่น:** ระบุว่าเป็น ภ.ง.ด.1/1ก/2/3/53/54/55

## Source URLs
- **PDF (template):** https://www.rd.go.th/fileadmin/tax_pdf/withhold/approve_wh3_081156.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/withhold/approve_wh3_081156.zip
- **EN PDF template:** https://www.rd.go.th/fileadmin/download/english_form/frm_WTC.pdf
- **Source page:** https://www.rd.go.th/62377.html
- **50 ทวิ generator (online):** https://www.rd.go.th/65920.html

## Notes
- Format from RD ใช้มาตั้งแต่ 2556 (no recent revision)
- e-WHT (สิ้นสุดที่ผู้รับรับเป็น e-format) = optional, ต้องลงทะเบียนกับ RD แยก
- Required ทั้ง บริษัท VAT + Non-VAT (เมื่อมีหัก WHT)

## Download status
- ⚠️ Binary not downloaded · URLs verified

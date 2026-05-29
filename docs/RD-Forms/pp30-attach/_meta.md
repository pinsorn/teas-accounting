# ใบแนบ ภ.พ.30 — Branch Detail Attachment

## Identifiers
- **RD code:** ใบแนบ ภ.พ.30 (AttachPP30)
- **Thai name:** ใบแนบ ภ.พ.30 รายละเอียดภาษีขายและภาษีซื้อของสถานประกอบการแต่ละแห่ง
- **English name:** PP30 Attachment — Per-branch Output/Input VAT breakdown
- **Purpose:** แสดงรายละเอียด Output/Input VAT แยกตามแต่ละสาขา เมื่อยื่นรวมที่สำนักงานใหญ่
- **Version (latest):** ฉบับ 2568 (issued 1 ก.ย. 2568)

## Filing
- **Frequency:** Monthly — แนบกับ ภ.พ.30 ทุกครั้ง (ถ้ามีหลายสาขา + ยื่นรวม)
- **Deadline:** เดียวกับ ภ.พ.30 (15 paper / 25 e-filing)
- **Where to file:** เดียวกับ ภ.พ.30
- **Legal basis:** ตามประกาศอธิบดี + ม.85/3

## TEAS relevance
- **Module:** Tax / VAT (multi-branch companies)
- **Trigger:** ถ้า `company.has_multiple_branches = true` + `company.consolidated_vat_filing = true` → auto-generate ใบแนบ
- **Generated from:** group Output VAT + Input VAT by `branch_id` per posting period

## Source URLs
- **PDF:** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/AttachPP30_010968.pdf
- **ZIP:** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/AttachPP30_010968.zip
- **Source page:** https://www.rd.go.th/62381.html

## Notes
- เฉพาะกิจการที่ขออนุมัติยื่นรวม ภ.พ.30 (ภ.พ.02)
- บริษัทสาขาเดียว = ไม่ต้องใช้ ใบแนบ ภ.พ.30

## Download status
- ⚠️ Binary not downloaded · URL verified

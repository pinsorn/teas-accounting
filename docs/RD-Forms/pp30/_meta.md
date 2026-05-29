# ภ.พ.30 — แบบแสดงรายการภาษีมูลค่าเพิ่ม (VAT Monthly Return)

## Identifiers
- **RD code:** ภ.พ.30 (PP30 / P.P.30)
- **Thai name:** แบบแสดงรายการภาษีมูลค่าเพิ่ม ตามประมวลรัษฎากร
- **English name:** Value Added Tax Return
- **Purpose:** ยื่นแสดงรายการ Output VAT − Input VAT ของแต่ละเดือน + ชำระภาษีต่อกรมสรรพากร
- **Version (latest):** ฉบับ 2568 (issued 1 ก.ย. 2568)

## Filing
- **Frequency:** Monthly (รายเดือน) — ต้องยื่นทุกเดือน แม้ไม่มีรายได้
- **Deadline:**
  - Paper: ภายในวันที่ **15** ของเดือนถัดไป
  - e-Filing: ขยายเป็นวันที่ **25** ของเดือนถัดไป (per RD extension order)
- **Where to file:**
  - Paper: สำนักงานสรรพากรพื้นที่สาขา (District Revenue Office) ที่สถานประกอบการตั้งอยู่
  - e-Filing: `https://efiling.rd.go.th/rd-cms/`
- **Legal basis:** ประมวลรัษฎากร ม.83 + ม.85
- **Penalty for late:** เงินเพิ่ม 1.5%/เดือน + เบี้ยปรับ 1-2× + ค่าปรับอาญา ≤ 2,000

## TEAS relevance
- **Module:** Tax / VAT
- **Trigger:** TEAS auto-generates draft on day 1 of next month (per accounting-system-plan.md §12.1.1)
- **Generated from:**
  - Output VAT = Σ posted Tax Invoices (TI) ในเดือนนั้น
  - Input VAT = Σ posted Vendor Invoices (VI) ที่อยู่ใน claim window
- **Reconciliation:** ต้องเท่ากับ GL 2160 (Output) − GL 1170 (Input)
- **Compliance:** CLAUDE.md §4.5 — `Tax:Pp30Mode = auto|manual` ใน appsettings (ห้ามเป็น UI setting)

## Source URLs
- **PDF (2568):** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp30_010968.pdf
- **ZIP bundle:** https://www.rd.go.th/fileadmin/tax_pdf/vat/2568/pp30_010968.zip
- **Source page:** https://www.rd.go.th/62381.html

## Form structure (16 calculation lines)
1. ยอดขายในเดือนนี้
2. ลบ ยอดขายอัตรา 0%
3. ลบ ยอดขาย exempt
4. ยอดขายที่ต้องเสียภาษี (1 − 2 − 3)
5. **ภาษีขายเดือนนี้** (Output VAT)
6. ยอดซื้อที่มีสิทธินำภาษีซื้อมาหัก
7. **ภาษีซื้อเดือนนี้** (Input VAT)
8. ภาษีที่ต้องชำระเดือนนี้ (5 − 7 ถ้า 5 > 7)
9. ภาษีที่ชำระเกินเดือนนี้ (7 − 5 ถ้า 7 > 5)
10. ภาษีที่ชำระเกินยกมา (carry-forward)
11. ภาษีสุทธิที่ต้องชำระ (8 − 10)
12. ภาษีสุทธิที่ชำระเกิน
13. เงินเพิ่ม (กรณีล่าช้า)
14. เบี้ยปรับ (กรณีล่าช้า)
15. รวมที่ต้องชำระ
16. รวมที่ชำระเกิน

## Branch options
- (1) แยกยื่นเป็นรายสถานประกอบการ
- (2) ยื่นรวมกัน — ต้องขออนุมัติก่อน (ดู ภ.พ.02)

## Notes
- ขอคืนภาษีผ่าน PromptPay (default) — ต้อง register PromptPay กับธนาคาร
- หากไม่ลงชื่อขอคืน → carry-forward ไปเดือนถัดไป
- กรณีต้องการ refund cash → ต้องลงชื่อ + กรอก ค.10

## Download status
- ⚠️ Binary PDF **not downloaded** to repo — Sana's sandbox blocks binary fetches
- ✅ URL verified live via WebFetch (2026-05-29) — PDF text extraction successful
- 👉 Click PDF URL above to download from RD official site

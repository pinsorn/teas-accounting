# รายงาน Findings — UX Test ฝั่งขาย (prod) — INTERIM 2026-07-16

สถานะ: **ยังไม่จบ** — เทสได้ถึงหน้า list ใบเสนอราคา แล้ว session prod หมดอายุ
(เด้ง /login) + MCP connector token หมด → รอ Ham login/reauth แล้วระบบเทสต่อเอง
(wakeup chain ตั้งไว้แล้ว). ครอบคลุมแล้ว: orientation, ลูกค้า (validation), หน้า list
ใบเสนอราคา + audit backend ทั้ง chain. ยังไม่ได้เทส: create/edit/approve ใบเสนอราคา,
SO → ใบส่งของ → ใบแจ้งหนี้ → ใบเสร็จ, PDF, รายงาน.

## ต้องแก้ (เรียงตามความสำคัญ)

### S4 — BU column "—" บน 3 หน้า list ฝั่งขาย (BUG backend, ญาติ R8)
`/quotations` `/sales-orders` `/delivery-orders` คอลัมน์หน่วยธุรกิจขึ้น "—" ทุกแถว
แม้เอกสารมี BU (docNo มี TEST) และ **filter หน่วยธุรกิจ 3 หน้านี้ใช้ไม่ได้จริง**
เพราะ list DTO + projection ฝั่ง backend ไม่ส่ง `businessUnitId` มาเลย — fix v1.21.3
ฝั่ง FE ช่วยไม่ได้ ข้อมูลไม่มาแต่ต้นทาง. Invoice/TaxInvoice/Receipt ปกติ (มี field ครบ).
- Fix: เพิ่ม field ใน 3 DTO + 3 projection ตาม pattern BillingNote — รายละเอียด file:line
  ครบใน `specs/fix-sales-ux-findings-2026-07-16.md`. ต้อง **API deploy** (ไม่ใช่ FE-only).

### S1 — dashboard แว้บโชว์ข้อมูลผิดก่อน hydrate (UX minor)
เข้าแอปครั้งแรก ~1–2 วิ เห็น ฿0.00 ทุกการ์ด + การ์ด "VAT สุทธิ" (ทั้งที่บริษัท non-VAT
ไม่ควรเห็น) + หัวข้อ nav ขาย/ซื้อ ว่างเปล่า แล้วค่อยเด้งเป็นข้อมูลจริง — ควรใส่ skeleton
จนกว่า company/vatMode โหลดเสร็จ.

### S2 — breadcrumb ปนภาษา (i18n minor)
/customers = "แดชบอร์ด > customers" (EN) แต่ /quotations เป็นไทย — ไม่สม่ำเสมอ.

### S3 — filter สถานะเป็น enum ดิบ EN (i18n minor)
dropdown สถานะหน้า /quotations โชว์ "Accepted"/"Draft" ขณะที่ badge ในตารางเป็นไทย
(ตอบรับแล้ว/ร่าง). ควร sweep ทุกหน้า list ทั้งขาย/ซื้อ.

### S5 — date filter หน้า list เป็น mm/dd/yyyy ค.ศ. ไม่มี hint พ.ศ. (UX minor, เศษ F2)
รอบก่อน (WP4.1) ใส่ hint พ.ศ. เฉพาะ date input ในฟอร์ม — filter หน้า list ยังไม่มี.

## ไม่ใช่ bug แต่ต้องเขียนในคู่มือ (S6)
- ใบวางบิล **ไม่มีเมนูแยก** — ระบบรวมเป็นใบเดียวกับ **ใบแจ้งหนี้** (/invoices)
- **ใบกำกับภาษี / ใบเพิ่มหนี้ / ใบลดหนี้ ซ่อนอัตโนมัติ** สำหรับบริษัทไม่จด VAT (ม.86/4)
  — Repttown จึงเห็นเมนูขาย 5 รายการ บริษัทจด VAT จะเห็น 6+
- คอลัมน์/พฤติกรรมนี้ต้องอธิบายใน manual ch.4 (บทขาย)

## ผ่านแล้ว (เท่าที่เทสได้)
- Login จำ company ล่าสุด (Repttown) ✓
- ฟอร์มลูกค้า: จด VAT ไม่ใส่เลขภาษี → block พร้อม error ไทย
  "ลูกค้า VAT ต้องระบุเลขผู้เสียภาษี + รหัสสาขา (ม.86/4 #3)" ✓ (parity F13 ฝั่งซื้อ)
- หน้า list ใบเสนอราคา: ปุ่มสร้างมี, วันที่ตารางเป็น พ.ศ. ถูก, badge สถานะไทย ✓

## Next (อัตโนมัติหลัง Ham login)
Phase 2 ต่อ: สร้างใบเสนอราคา BU TEST → แก้ draft (เช็ค docDate preserve = R2 Option B)
→ อนุมัติ (เช็ค confirm dialog) → PDF → Phase 3–5 ไล่ chain ถึงใบเสร็จ + AR reports
→ รายงานฉบับจบ + ตัดสินใจเรื่อง manual refresh.

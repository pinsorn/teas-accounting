# sales01 — UX Swarm findings (co5, prod)

Run: 2026-07-19 ~17:56–18:05 ICT | user: sales01 (Sales Staff) | target: https://teas.kazaki-rio.com (v1.22.5) | company: บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5)

## Done
- Login สำเร็จ: sales01 / UxSwarm-2026-A1
- Round 1: สร้าง QT #11 (ลูกค้า: บริษัท ลูกค้าทดสอบ จำกัด, สินค้า: P001/สินค้าทดสอบ A, จำนวน 3) — คลิก "ออกใบเสนอราคา" (create+send) → **ล้มเหลว**, ดู CRIT-1
- Round 2: สร้าง QT #12 (ลูกค้าเดิม, จำนวน 4) — คลิก "ออกใบเสนอราคา" → **ล้มเหลว** เหมือนกัน (ดู CRIT-1)
- Diagnostic follow-up: สร้าง QT #13 ผ่าน flow เดิมอีกรอบ (retry #3) → ยัง 500 เหมือนเดิม (สม่ำเสมอ 100%)
- Diagnostic follow-up: เปิด QT #11 (ยัง Draft) → กด "ส่งใบเสนอราคา" (manual send, ปุ่มแยกจาก create) → ยืนยัน dialog → **500 เหมือนกัน** (retry #4) — สรุปว่า endpoint `POST /api/proxy/quotations/{id}/send` พังทั้งจากปุ่ม create+send และปุ่ม send แยก ไม่ใช่แค่ script/timing ของฉัน
- Probe: `/tax-invoices/new` (ตรง) — ฟอร์มเปิดเต็ม ไม่มี deny (ดู HIGH-1)
- Probe: `/settings/users` — **deny สวยงาม** ("ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage)")
- Probe: `/payment-vouchers/new` — ฟอร์มเปิดเต็ม ไม่มี deny (ดู HIGH-2)
- Probe: `/payroll` — หน้า list เปิดได้ (ว่างเปล่า ไม่มีข้อมูล) ไม่มี deny (ดู MED-1)
- ไม่พบข้อมูล/เอกสารบริษัทอื่น (นาย พงศ์สันต์ / เรปทาวน์) ตลอด session — ไม่มี tenant-leak

**หมายเหตุ**: เพราะ CRIT-1 บล็อกทุกอย่างตั้งแต่ขั้น "ออกใบเสนอราคา" (Sent) มิชชันสายขายเต็ม (QT→SO→DO→Invoice) และการชน doc numbering ที่ SO/DO/Invoice **ไปไม่ถึง** — ทดสอบได้แค่ QT create + send (ซึ่งพังทุกครั้ง) จึงไม่มีข้อมูล SO/DO/Invoice doc-number collision จาก agent นี้ ต้องพึ่งผลจาก role อื่นที่อาจข้ามขั้นนี้ไปได้ (หรือบั๊กนี้บล็อกทุก role เหมือนกันหมด เพราะเป็น 500 internal_error ไม่ใช่ RBAC — ไม่น่าจะเจาะจง sales01)

## Findings

| severity | พื้นที่ | อาการ | repro | screenshot |
|---|---|---|---|---|
| CRIT | Quotation send/issue (`POST /api/proxy/quotations/{id}/send`) | 500 internal_error **สม่ำเสมอ 4/4 ครั้ง** (create+send ปุ่มเดียว 3 ครั้ง + manual send ปุ่มแยกอีก 1 ครั้ง) — บล็อกทั้งสายขาย (QT→SO→DO→Invoice) ทั้งหมด ทุกใบที่พยายามส่งเหลือค้างเป็น Draft (#11, #12, #13). Response body: `{"type":"urn:teas:error:internal_error","title":"internal_error","status":500,"detail":"An unexpected error occurred."}`. Frontend แสดง toast แดง "เกิดข้อผิดพลาด" (ทั่วไป ไม่บอกรายละเอียด) แต่ฟอร์มค้าง ไม่ redirect, และทุกครั้งที่กดซ้ำสร้าง Draft QT เลขใหม่ทิ้งไว้ (bloat เลขที่เอกสาร) | https://teas.kazaki-rio.com/quotations/new (create+send #11,#12,#13); https://teas.kazaki-rio.com/quotations/11 (manual send) | shots/sales01-round1-exception.png, shots/sales01-round2-exception.png, shots/sales01-followup-retry3.png, shots/sales01-followup-manual-send-qt11-confirmed.png |
| HIGH | RBAC: `/tax-invoices/new` เข้าถึงตรงได้ | Sales Staff เข้า URL ตรง (พิมพ์เอง ไม่ผ่านเมนู) ได้ฟอร์มสร้าง/Post ใบกำกับภาษีเต็มรูปแบบ (เลือกลูกค้า, รายการ, live preview, ปุ่ม "บันทึกเอกสาร (Post)" สีส้มเข้ม พร้อมกด) ไม่มี deny message เหมือน settings/users — มิชชันคาด deny (สาย TI ควรเป็นของ Accountant/AR ไม่ใช่ Sales). ไม่ได้ลอง Post จริงเพราะ CRIT-1 บ่งชี้ backend เอกสาร-โพสต์กำลังมีปัญหา และไม่อยากสร้าง noise เพิ่ม แต่ **การเข้าถึงฟอร์มเองคือช่องโหว่แล้ว** ไม่ต้อง Post ก็เป็น finding | https://teas.kazaki-rio.com/tax-invoices/new | shots/sales01-probe-------------TI------.png |
| HIGH | RBAC: `/payment-vouchers/new` เข้าถึงตรงได้ | เช่นเดียวกับ TI — ฟอร์มสร้างใบสำคัญจ่ายเปิดเต็ม (เลือกผู้ขาย, หมวดค่าใช้จ่าย, วิธีชำระ, live preview). ปุ่ม "บันทึก" ดูจางกว่า TI (validation-disabled จนกรอกครบ ไม่ใช่ RBAC-disabled) — สาย PV ควรเป็นของ AP Clerk ตาม mission ไม่ใช่ Sales | https://teas.kazaki-rio.com/payment-vouchers/new | shots/sales01-probe-payment-vouchers-new--PV-.png |
| MED | RBAC: `/payroll` เข้าถึงตรงได้ (view) | หน้า list เงินเดือนเปิดได้ (ว่างเปล่า "ไม่มีข้อมูล" ไม่เห็นปุ่มสร้าง/แก้ในวิวนี้) — ตาม hard rule payroll เป็น read-only ทุกคนอยู่แล้ว จึงไม่ใช่ช่องโหว่มูลค่าสูง แต่มิชชันคาด deny และหน้านี้ไม่ deny เหมือน settings/users เป็น inconsistency ของ RBAC gating ระหว่างหน้า | https://teas.kazaki-rio.com/payroll | shots/sales01-probe-payroll.png |
| LOW | RBAC gating inconsistency | เทียบ 4 probe: `settings/users` แสดงหน้า deny แบบมี component ชัดเจน (ไอคอน + ข้อความ + permission code) แต่ `tax-invoices/new`, `payment-vouchers/new`, `payroll` ไม่มี pattern เดียวกันเลย (โหลดฟอร์ม/หน้าปกติไปเลย) — ชี้ว่า route-level RBAC guard ถูกใช้ไม่ครบทุกหน้า ทั้งที่มี component "ไม่มีสิทธิ์เข้าถึง" อยู่แล้วในระบบ (เห็นจาก settings/users) | - | shots/sales01-probe-settings-users.png (ตัวอย่าง deny ที่ถูกต้อง) |

## Denied-as-expected
- **settings/users**: หน้าโหลดที่ URL เดิม แสดง component "ไม่มีสิทธิ์เข้าถึง — หน้านี้ต้องมีสิทธิ์จัดการผู้ใช้ (sys.user.manage) — กรุณาติดต่อผู้ดูแลระบบ" ชัดเจน ไม่มีฟอร์ม/ข้อมูลรั่ว — deny ที่ถูกต้องตามคาด

## Console errors (ทุกหน้า, ตัดซ้ำ)
- `/login` :: 404 (resource, ไม่กระทบ)
- `/` , `/quotations/new`, `/tax-invoices/new`, `/settings/users`, `/payment-vouchers/new`, `/payroll` :: 403 (น่าจะเป็น permission-check API call ที่ตั้งใจให้ 403 แล้ว frontend เงียบ ไม่ redirect ทุกหน้า — สอดคล้องกับ LOW finding ด้านบน)
- `/quotations/new` :: 500 (ตรงกับ CRIT-1, endpoint `/quotations/{id}/send`)
- `/tax-invoices/new` :: 404 (resource, ไม่กระทบ)

## หมายเหตุถึง Fable (consolidation)
- CRIT-1 (`quotations/{id}/send` → 500) ควรเช็คว่า role อื่น (acct01, appr01, admin01 ฯลฯ) เจอเหมือนกันไหม — ถ้าใช่ แปลว่าเป็น backend bug กว้าง ไม่ใช่ RBAC เฉพาะ sales01 ควรอยู่ arc แก้ด่วนสุด (บล็อก mission ของ sales01 ทั้งหมด)
- QT #11, #12, #13 ใน co5 ค้างเป็น Draft ที่ไม่ได้ตั้งใจ (side-effect ของบั๊ก ไม่ใช่ real usage) — ปลอดภัยที่จะลบ/ปล่อยไว้ตาม sanity pass ของ Fable
- ไม่ได้ทดสอบ SO/DO/Invoice/doc-numbering ตามมิชชันเพราะ blocked ตั้งแต่ต้นสาย — ต้องทำซ้ำหลัง CRIT-1 ถูกแก้

# รายงานผล UX Test ฝั่งขาย (prod teas.kazaki-rio.com) — FINAL 2026-07-16

ครอบคลุม: ลูกค้า → ใบเสนอราคา (สร้าง/แก้/ส่ง/ตอบรับ) → ใบสั่งขาย (post) → ใบแจ้งหนี้
(ออก) → ใบเสร็จ (post) → settlement → AR aging + PDF ทุก hop บน BU TEST @ Repttown.
ใบส่งของ: chain ข้ามอัตโนมัติ (service line, data-driven skip ตาม spec) — เทสระดับ API/DTO.
ใบกำกับภาษี/ใบวางบิล: non-VAT co ไม่มี hop นี้ (by design, อธิบายในคู่มือ ch.4 แล้ว).
แทนที่ REPORT-sales-uxtest-interim.md (ยกเลิก).

## E2E ที่ผ่าน (chain ปิดลูปสมบูรณ์)
QT #6 (agent draft, แก้+ใส่ BU, docDate คงเดิม ✓) → ส่ง → 07-2026-QT-TEST-0002 →
ตอบรับ → แปลงเป็น SO → 07-2026-SO-TEST-0002 (post) → สร้างใบแจ้งหนี้ →
07-2026-IV-TEST-0002 (ออก) → สร้างใบเสร็จ → 07-2026-RC-TEST-0002 (post, มี confirm
dialog + ม.86/4/86/12) → invoice **Settled** ✓ → AR aging ว่าง + คุม 1130 balanced ✓.
PDF endpoint 200 application/pdf (QT+RC). POST สร้างเอกสาร = ครั้งเดียว ไม่มี dup ✓.

## ต้องแก้ — เรียงความสำคัญ

### S13 (ใหม่, INFRA — สำคัญสุด): prod ตอบ 503 บน write เป็นระยะ แล้วงานกลับสำเร็จ
PUT /quotations/6, POST /quotations/6/send, POST /sales-orders/5/post และ RSC GET
หนึ่งครั้ง ตอบ 503 — แต่ทุก operation APPLY จริง (FE retry/refetch กลบไว้ ผู้ใช้เห็นสำเร็จ)
- ความเสี่ยง: ถ้า 503-แต่-สำเร็จ เกิดบน POST ที่ไม่ idempotent + FE retry → เอกสาร/เลขซ้ำ
- ต้องไล่ที่ชั้น proxy/nginx (timeout/buffer?) หรือ Kestrel — ดู log ช่วง 2026-07-16 ~05:3x-06:0x
- FE ควรโชว์ error จริงถ้า retry ไม่สำเร็จ (ตอนนี้เงียบ)

### S4 (backend BUG — ยืนยัน live บน prod): list DTO ขาด businessUnitId 3 ตัว
quotations / sales-orders / delivery-orders → คอลัมน์หน่วยธุรกิจ "—" ตลอด + filter BU
ใช้ไม่ได้. invoices(billing-notes) / tax-invoices / receipts มีครบ (เทียบ key จริงแล้ว).
Fix spec พร้อม: specs/fix-sales-ux-findings-2026-07-16.md (3 DTO + 3 projection,
pattern BillingNote, ต้อง API deploy)

### S11 (UX สำคัญ): sales chain ไม่มี confirm dialog เกือบทุก hop
- ส่งใบเสนอราคา = คลิกเดียว **ออกเลขเอกสารทันที** ไม่มี dialog (เลขถูก consume)
- ลูกค้าตอบรับ / SO ยืนยัน(Post) / ออกใบแจ้งหนี้ = คลิกเดียวเช่นกัน
- มีเฉพาะใบเสร็จ (post) ที่มี dialog ครบแบบ (totals + immutable warning)
ฝั่งซื้อเพิ่งเติม dialog ครบใน WP3.6 — ฝั่งขายควร parity (อย่างน้อย hop ที่ออกเลข/immutable)

### S12 (UX): side panels ไม่ live-refresh หลัง action (F10-parity fail ฝั่งขาย)
เอกสารอ้างอิง/ประวัติกิจกรรม ค้างค่าเก่าหลัง send/post (การ์ด ref โชว์ Draft ทั้งที่ posted;
activity โผล่หลัง reload) + แก้ draft ไม่ลง activity เลย + wording "ส่งแล้ว → ส่งแล้ว" ซ้ำ (R6)

### S16 (UX/data-risk): ใบเสร็จจากใบแจ้งหนี้ ไม่ prefill หน่วยธุรกิจ
invoice เป็น BU TEST แล้ว แต่ฟอร์ม RC บังคับเลือกใหม่จาก "— ต้องระบุ —" — เสี่ยงลงผิด BU

### S15 (UX): draft ที่แปลงมา (SO จาก QT, INV จาก SO) ไม่มีปุ่มแก้ไข
แก้ปริมาณ/ราคา/วันครบกำหนดไม่ได้เลย ต้องยกเลิกทำใหม่ (F6-parity; QT draft มีแก้ไข)

### S9 (validation ไม่ sync): draft จาก MCP/agent ไม่มี BU แต่ FE บังคับ required
QT #5/#6 (agent) มี businessUnitId=null — FE edit บังคับใส่ก่อน save แต่เส้นทาง
ส่ง/อนุมัติโดยไม่แก้ก่อนอาจหลุด (ยังไม่ได้เทสส่งโดยไม่ใส่ BU) → ควร enforce ฝั่ง API

### S10 (UX): หน้า detail ใบเสนอราคา ไม่แสดงหน่วยธุรกิจ
ตั้งไปแล้วดูไม่ได้ (API มีค่า businessUnitId=3) — ควรโชว์ใน header/info

### S14 (ตรวจนโยบาย): ใบแจ้งหนี้ default ครบกำหนดชำระ = วันเดียวกับ docDate
credit term ลูกค้าไม่ถูกใช้? (ลูกค้านี้อาจ term 0 — ต้อง verify กับลูกค้าที่มี term 30)

### S1/S2/S3/S5/S7/S8 (UX/i18n minor)
- S1: flash ก่อน hydrate — dashboard (การ์ด VAT สุทธิ โผล่บน non-VAT co ชั่วครู่ + nav
  ว่าง) และฟอร์ม QT (คอลัมน์ "อัตราภาษี VAT 7%" + แถว VAT ใน preview แว้บก่อนหาย)
- S2: breadcrumb /customers = "customers" (EN) ปนไทย
- S3: filter สถานะ = "Accepted/Draft" enum ดิบ
- S5+S7: date input ฟอร์ม QT (วันที่/ยืนราคาถึง) + filter ทุก list = mm/dd/yyyy ค.ศ.
  ไม่มี hint พ.ศ. (WP4.1 ครอบเฉพาะบางฟอร์ม; RC ฟอร์มมี hint แล้ว ✓)
- S8: modal เลือกลูกค้า ไม่มีปุ่มสร้างลูกค้าใหม่ inline (F4-parity)

## ผ่าน / ยืนยันดี
- แก้ draft คง docDate เดิม ✓ (R2-parity; วันที่ QT ผู้ใช้แก้ได้ by design)
- RC post: confirm dialog + docDate ล็อกวันนี้ + hint พ.ศ. ✓
- Chain refs ครบทุก hop, skip-DO อัตโนมัติถูกต้อง, settlement + AR aging tie-out ✓
- เลขเอกสาร MM-YYYY-<PREFIX>-<BU>-NNNN ✓ ทุกประเภท
- ลูกค้า: บังคับเลขภาษี 13 หลักเมื่อจด VAT (inline Thai, ม.86/4 #3) ✓
- Non-VAT co: ฟอร์มขายไม่มีช่อง VAT (ถูกต้อง ไม่เหมือนฝั่งซื้อที่ต้อง advisory) ✓

## เอกสารเทสที่สร้าง (BU TEST — เก็บ/ลบตามสะดวก)
- QT #8 draft ฿1,500 (ค้าง draft), QT #5 agent draft ฿200 (ค้าง รออนุมัติ)
- 07-2026-QT-TEST-0002 → SO-TEST-0002 → IV-TEST-0002 (Settled) → RC-TEST-0002 (Posted)

## คู่มือ
ch.4 (บทขาย) refresh แล้ว commit 420a4c0 — ใบวางบิล=ใบแจ้งหนี้, เมนู vatOnly (ม.86/4),
เลขเอกสารรูปแบบใหม่, captures สด 11/11. S1–S16 เป็น bug/fix — ไม่เขียนลงคู่มือจนกว่าแก้.

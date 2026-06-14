/**
 * 04.02 — ใบสั่งขาย (Sales Order) → ใบส่งของ (Delivery Order)
 *
 * Chapter: 4. งานขาย
 * Story: ยืนยันคำสั่งซื้อของลูกค้าด้วย "ใบสั่งขาย" แล้วส่งมอบสินค้าด้วย "ใบส่งของ".
 *        ทั้งสองเป็นขั้นกลางของสายงานขาย: ใบสั่งขาย = คำสั่งที่ยืนยันแล้ว (มีมูลค่า),
 *        ใบส่งของ = หลักฐานการส่งมอบ (ไม่มีราคา/ภาษี — non-fiscal).
 *
 * Persona: admin (sales.sales_order.* + sales.delivery_order.*).
 * Captured against /sales-orders/new (SalesOrderForm = LineItemsTable) และ
 * /delivery-orders/new (DeliveryOrderForm = ตารางรายการแบบไม่มีราคา).
 *
 * หมายเหตุ flow: ในงานจริงมักแปลงจากเอกสารต้นทาง (ใบเสนอราคา → ใบสั่งขาย ผ่านปุ่ม
 * บนหน้ารายละเอียดใบเสนอราคา). บทนี้แสดงการ "สร้างใหม่" โดยตรงเพื่อให้เห็นฟอร์มเต็ม.
 *
 * Data (co2): ลูกค้า "บริษัท แอคมี จำกัด", หน่วยธุรกิจ ECOM.
 * ⚠️ สร้างเอกสารจริงบน company 2 (ออกเลขที่เอกสาร).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.02',
  title: 'ใบสั่งขาย และ ใบส่งของ',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
หลังลูกค้าตอบรับใบเสนอราคา ขั้นถัดไปคือ:

1. **ใบสั่งขาย (Sales Order)** — บันทึกคำสั่งซื้อที่ยืนยันแล้ว (จำนวน/ราคา/หน่วยธุรกิจ).
   เป็นเอกสารภายในที่ผูกราคาไว้ ใช้เป็นฐานในการจัดส่งและออกใบกำกับภาษีต่อไป.
2. **ใบส่งของ (Delivery Order)** — หลักฐานการส่งมอบสินค้า/บริการ. เป็นเอกสาร
   **non-fiscal** คือ ไม่มีราคาและภาษี (แสดงเฉพาะรายการ + จำนวน + หน่วยนับ).
   ที่อยู่จัดส่งและผู้รับสินค้าบันทึกในช่องหมายเหตุ.

ทั้งสองอ้างอิงเอกสารต้นทางได้ และในงานจริงนิยม "แปลง" ต่อกัน (ใบเสนอราคา → ใบสั่งขาย,
ใบสั่งขาย → ใบส่งของ) ผ่านปุ่มบนหน้ารายละเอียด. ที่นี่แสดงการสร้างฟอร์มใหม่โดยตรง.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ sales_order + delivery_order)',
    'มีลูกค้า + หน่วยธุรกิจ ในระบบ',
  ],
}, async ({ page, capture }) => {

  // ════════════ ส่วนที่ 1: ใบสั่งขาย (Sales Order) ════════════

  // ─── Step 1: SO blank form ───────────────────────────────────────────
  await page.goto('/sales-orders/new');
  await capture('step-01-so-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "สร้างใบสั่งขาย" — โครงเดียวกับใบเสนอราคา (ลูกค้า /' +
      ' ข้อมูลเอกสาร / รายการ) พร้อมตัวอย่างเอกสารด้านขวา',
  });

  // ─── Step 2: fill SO — customer + BU + line ──────────────────────────
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const soCust = page.getByRole('dialog');
  await soCust.getByRole('textbox').fill('แอคมี');
  await soCust.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ชุดโต๊ะทำงานสำนักงาน รุ่น A');
  await page.getByLabel('จำนวน 1').fill('10');
  await page.getByLabel('ราคา/หน่วย 1').fill('3500');
  await capture('step-02-so-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: เลือกลูกค้า "บริษัท แอคมี จำกัด" + หน่วยธุรกิจ + กรอกรายการ' +
      ' (10 × 3,500). ตัวอย่างเอกสารคำนวณมูลค่าก่อนภาษี 35,000 + VAT 7% อัตโนมัติ',
  });

  // ─── Step 3: confirm SO → list ───────────────────────────────────────
  await page.getByRole('button', { name: 'ยืนยันสั่งขาย' }).click();
  await page.waitForURL(/\/sales-orders$/, { timeout: 15_000 });
  await capture('step-03-so-confirmed', {
    highlight: 'main',
    caption:
      'ขั้นที่ 3: กด "ยืนยันสั่งขาย" → ระบบออกเลขที่เอกสารและยืนยันใบสั่งขาย' +
      ' (สถานะ "บันทึกแล้ว" = ยืนยัน/โพสต์แล้ว เห็นเป็นแถวบนสุดของรายการ).' +
      ' พร้อมจัดส่งและออกใบกำกับภาษีต่อ',
  });

  // ════════════ ส่วนที่ 2: ใบส่งของ (Delivery Order) ════════════

  // ─── Step 4: DO blank form (non-fiscal) ──────────────────────────────
  await page.goto('/delivery-orders/new');
  await capture('step-04-do-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 4: ฟอร์ม "สร้างใบส่งของ" — สังเกตตารางรายการ "ไม่มีคอลัมน์ราคา/ภาษี"' +
      ' เพราะใบส่งของเป็นเอกสารส่งมอบ ไม่ใช่เอกสารเรียกเงิน (non-fiscal)',
  });

  // ─── Step 5: fill DO — customer + BU + non-fiscal line ───────────────
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const doCust = page.getByRole('dialog');
  await doCust.getByRole('textbox').fill('แอคมี');
  await doCust.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ชุดโต๊ะทำงานสำนักงาน รุ่น A');
  await page.getByLabel('จำนวน 1').fill('10');
  await page.getByLabel('หน่วยนับ 1').fill('ชุด');
  await capture('step-05-do-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 5: เลือกลูกค้าเดิม + หน่วยธุรกิจ + กรอกรายการที่ส่งมอบ (จำนวน + หน่วยนับ' +
      ' เท่านั้น ไม่มีราคา). ที่อยู่จัดส่ง/ผู้รับสินค้าระบุในส่วนหมายเหตุด้านล่าง',
  });

  // ─── Step 6: issue DO → list ─────────────────────────────────────────
  await page.getByRole('button', { name: 'ออกใบส่งของ' }).click();
  await page.waitForURL(/\/delivery-orders$/, { timeout: 15_000 });
  await capture('step-06-do-issued', {
    highlight: 'main',
    caption:
      'ขั้นที่ 6: กด "ออกใบส่งของ" → ระบบออกเลขที่เอกสารและบันทึกใบส่งของ (เห็นแถว' +
      ' บนสุด). เมื่อยืนยัน "ส่งมอบแล้ว" ระบบจะออกใบกำกับภาษีให้ในขั้นถัดไปอัตโนมัติ',
  });

});

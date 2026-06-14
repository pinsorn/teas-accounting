/**
 * 04.01 — ออกใบเสนอราคา (Quotation) — จุดเริ่มของสายงานขาย
 *
 * Chapter: 4. งานขาย
 * Story: สร้างใบเสนอราคา → เลือกลูกค้า + หน่วยธุรกิจ + รายการ → "ออกใบเสนอราคา".
 *        ใบเสนอราคาเป็นเอกสารตั้งต้น (ยังไม่ใช่เอกสารภาษี) ที่ส่งให้ลูกค้าพิจารณา
 *        ก่อนแปลงเป็นใบสั่งขาย → ใบส่งของ → ใบแจ้งหนี้/ใบกำกับภาษี → ใบเสร็จ.
 *
 * Persona: admin (ต้องมี sales.quotation.create + .send).
 * Captured against /quotations/new (QuotationForm = PartySelectBox +
 * BusinessUnitSelector + LineItemsTable). Selectors mirror the proven
 * frontend/e2e/_helpers.ts pattern (pickCustomer + line-item labels).
 *
 * Data (co2): pick "บริษัท แอคมี จำกัด" (ลูกค้าจด VAT). ใบเสนอราคาไม่ใช่เอกสาร
 * ภาษีจึงไม่บังคับเลขผู้เสียภาษีผู้ซื้อ แต่ระบบดึงมาแสดงเพื่อความครบถ้วน.
 *
 * ⚠️ การ "ออกใบเสนอราคา" สร้างเอกสารจริงบน company 2 (ออกเลขที่เอกสาร). ใบเสนอราคา
 *    แก้ไข/ยกเลิกได้ (ไม่ใช่เอกสารภาษีที่ตรึงถาวรเหมือนใบกำกับภาษี).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.01',
  title: 'ออกใบเสนอราคา',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ใบเสนอราคา (Quotation) คือเอกสารแรกของสายงานขาย — เสนอราคาสินค้า/บริการให้ลูกค้า
พิจารณาก่อนตัดสินใจซื้อ. ยัง **ไม่ใช่เอกสารทางภาษี** จึงแก้ไขหรือยกเลิกได้ และยัง
ไม่เกิดภาระภาษีขาย.

**สายงานขายเต็มรูป (document chain):**

ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → ใบแจ้งหนี้ → **ใบกำกับภาษี** → ใบเสร็จรับเงิน

แต่ละขั้นอ้างอิงเอกสารต้นทางได้ ทำให้ตรวจสอบย้อนกลับ (audit trail) ได้ครบ. ใบกำกับภาษี
คือจุดที่เกิดภาระภาษีและตรึงถาวร (ดู 04.04). บทนี้เริ่มจากต้นทาง.

**สิ่งที่กรอกในใบเสนอราคา:** ลูกค้า · วันที่ + วันที่ยืนราคา (ค่าเริ่มต้น +30 วัน) ·
หน่วยธุรกิจ (บริษัทตัวอย่างตั้งให้บังคับในเอกสารรายได้) · รายการสินค้า/บริการ พร้อม
ราคาและภาษีมูลค่าเพิ่มที่คำนวณให้อัตโนมัติในตัวอย่างเอกสารด้านขวา.
  `.trim(),
  prerequisites: [
    'login ในฐานะผู้มีสิทธิ์ sales.quotation.create + .send (admin)',
    'มีลูกค้า + หน่วยธุรกิจ ในระบบ (บทที่ 2–3)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: blank form (4 sections + live A4 preview) ────────────────
  await page.goto('/quotations/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "สร้างใบเสนอราคา" — ซ้ายเป็นช่องกรอก 4 ส่วน (① ลูกค้า,' +
      ' ② ข้อมูลเอกสาร, ③ รายการ, ④ หมายเหตุ) ขวาเป็นตัวอย่างเอกสาร A4 ที่อัปเดตสด' +
      ' ตามที่กรอก',
  });

  // ─── Step 2: pick a customer ─────────────────────────────────────────
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const custDialog = page.getByRole('dialog');
  await custDialog.getByRole('textbox').fill('แอคมี');
  await custDialog.getByRole('button', { name: /แอคมี/ }).first().click();
  await capture('step-02-customer', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: กด "เลือกลูกค้า" → ค้นหา → เลือก "บริษัท แอคมี จำกัด". ระบบดึงชื่อ' +
      ' และที่อยู่มาแสดงในตัวอย่างเอกสารด้านขวาทันที',
  });

  // ─── Step 3: choose business unit (required on income docs for co2) ──
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await capture('step-03-bu', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เลือก "หน่วยธุรกิจ" (บริษัทตัวอย่างตั้งค่าให้บังคับระบุในเอกสาร' +
      ' รายได้ — ดู 02.01). วันที่และ "ยืนราคาถึง" มีค่าเริ่มต้นให้ (+30 วัน) แก้ได้',
  });

  // ─── Step 4: add a line item — VAT preview computed automatically ────
  await page.getByLabel('รายละเอียด 1').fill('ค่าออกแบบและพัฒนาเว็บไซต์');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('50000');
  await capture('step-04-line', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 4: กรอกรายการ — "ค่าออกแบบและพัฒนาเว็บไซต์" จำนวน 1 ราคา 50,000.' +
      ' กล่องสรุปคำนวณ มูลค่าก่อนภาษี + ภาษีมูลค่าเพิ่ม 7% = รวมทั้งสิ้น ให้อัตโนมัติ',
  });

  // ─── Step 5: issue → doc number allocated, status Sent ───────────────
  await page.getByRole('button', { name: 'ออกใบเสนอราคา' }).click();
  await page.waitForURL(/\/quotations\/\d+/, { timeout: 15_000 });
  await capture('step-05-issued', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: กด "ออกใบเสนอราคา" → ระบบออกเลขที่เอกสารและเปลี่ยนสถานะเป็น' +
      ' "ส่งแล้ว". จากหน้านี้พิมพ์ PDF ส่งลูกค้า หรือแปลงเป็นใบสั่งขายต่อได้',
  });

});

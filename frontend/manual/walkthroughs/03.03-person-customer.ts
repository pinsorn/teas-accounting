/**
 * 03.03 — ลูกค้าบุคคลธรรมดา (Individual customer) — ไม่มีเลขผู้เสียภาษีผู้ซื้อบนใบกำกับภาษี
 *
 * Chapter: 3. ข้อมูลหลัก
 * Story: ลูกค้าที่เป็น "บุคคลธรรมดา" (ผู้บริโภคทั่วไป ไม่จด VAT) ไม่ต้องกรอกเลขประจำตัว
 *        ผู้เสียภาษี 13 หลัก. เมื่อออกใบกำกับภาษีให้ บล็อกผู้ซื้อจะแสดงแค่ "ชื่อ"
 *        ไม่มีเลขผู้เสียภาษี (ม.86/4 #3 — ใส่เลขผู้เสียภาษีผู้ซื้อเฉพาะเมื่อผู้ซื้อจด VAT).
 *
 * Persona: admin (demo-admin co2). บทนี้ "สร้างลูกค้าบุคคลธรรมดาจริง" (idempotent ด้วย
 * suffix ต่อท้ายรหัส) แล้วนำไปแสดงบนใบกำกับภาษีแบบ "ร่าง/ตัวอย่าง" (ไม่ post).
 *
 * ต่างจาก 04.09 อย่างไร: 04.09 "เปรียบเทียบ" ผู้ซื้อจด/ไม่จด VAT บนเอกสารเดียว;
 * บทนี้เริ่มที่ "การสร้างข้อมูลหลัก" ของลูกค้าบุคคลธรรมดา (เลือกประเภท + ปิดสวิตช์ VAT
 * → ช่องเลขผู้เสียภาษีเลิกบังคับ) แล้วจึงพาไปดูผลบนใบกำกับภาษี.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '03.03',
  title: 'ลูกค้าบุคคลธรรมดา (ไม่มีเลขผู้เสียภาษี)',
  chapter: '3. ข้อมูลหลัก',
  persona: 'admin',
  intro: `
ลูกค้าแบ่งเป็น 2 ประเภท: **นิติบุคคล** (บริษัท/ห้างฯ) และ **บุคคลธรรมดา**
(ผู้บริโภคทั่วไป, ฟรีแลนซ์, ร้านเล็ก ๆ ที่ไม่จด VAT). การตั้งประเภทให้ถูกมีผลต่อ
เอกสารภาษีโดยตรง:

- **ประเภท** (บุคคลธรรมดา / นิติบุคคล) — เลือกตอนสร้าง แก้ภายหลังไม่ได้.
- **สวิตช์ "จดทะเบียน VAT"** — ลูกค้าบุคคลธรรมดาทั่วไป **ไม่จด VAT** → "ปิด" สวิตช์นี้.
  เมื่อปิดแล้ว ช่อง **เลขประจำตัวผู้เสียภาษี (13 หลัก) + รหัสสาขา เลิกเป็นช่องบังคับ**
  (เครื่องหมาย * หาย) เพราะไม่ต้องใช้.

**กฎหมาย ม.86/4 #3:** ใบกำกับภาษีต้องมีเลขผู้เสียภาษีของผู้ซื้อ **เฉพาะเมื่อผู้ซื้อจด VAT**.
ลูกค้าบุคคลธรรมดาที่ไม่จด VAT จึงออกใบกำกับภาษีให้ได้ตามปกติ แต่บล็อกผู้ซื้อบนเอกสาร
แสดงแค่ "ชื่อ" — ไม่มีเลขผู้เสียภาษี (เพราะผู้ซื้อนำไปเคลมภาษีซื้อไม่ได้อยู่แล้ว).
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ master.customer.manage + tax_invoice)',
  ],
}, async ({ page, capture }) => {
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);
  const personName = `คุณมานี ใจดี ${suffix}`;

  // ─── Step 1: create form — choose "บุคคลธรรมดา" ──────────────────────
  await page.goto('/customers/new');
  await page.getByLabel('รหัสลูกค้า').waitFor({ state: 'visible' });
  await page.getByLabel('รหัสลูกค้า').fill(`MC-IND-${suffix}`);
  await page.getByLabel('ประเภท').selectOption('Individual');
  await page.getByLabel('ชื่อ (ไทย)').fill(personName);
  await capture('step-01-type-individual', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: สร้างลูกค้าใหม่ — ช่อง "ประเภท" เลือก "บุคคลธรรมดา"' +
      ' (ผู้บริโภคทั่วไป/ฟรีแลนซ์). กรอกรหัส + ชื่อไทย. ประเภทนี้ตั้งได้ตอนสร้าง' +
      ' เท่านั้น แก้ภายหลังไม่ได้',
  });

  // ─── Step 2: turn VAT off → tax id no longer required ────────────────
  // Default toggle is ON; an individual consumer is non-VAT → switch it off.
  await page.getByText('จดทะเบียน VAT').click();
  await capture('step-02-vat-off', {
    highlight: '[class*="grid"]',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: ปิดสวิตช์ "จดทะเบียน VAT" (ลูกค้าบุคคลธรรมดาทั่วไปไม่จด VAT) →' +
      ' ช่อง "เลขประจำตัวผู้เสียภาษี" และ "รหัสสาขา" เลิกเป็นช่องบังคับทันที' +
      ' (เครื่องหมาย * หายไป) เพราะ ม.86/4 ไม่ต้องใช้เลขผู้เสียภาษีของผู้ซื้อที่ไม่จด VAT',
  });

  // ─── Step 3: save → individual now in master ─────────────────────────
  await page.getByRole('button', { name: 'บันทึก' }).first().click();
  await page.waitForURL('http://localhost:3000/customers', { timeout: 15_000 });
  await capture('step-03-saved', {
    highlight: 'table',
    caption:
      `ขั้นที่ 3: บันทึกแล้ว — ลูกค้าบุคคลธรรมดา "${personName}" เข้า master` +
      ' (คอลัมน์ "ประเภท" = บุคคลธรรมดา, ไม่มีเลขผู้เสียภาษี). พร้อมใช้เป็นผู้ซื้อในเอกสารขาย',
  });

  // ─── Step 4: on a tax invoice — buyer block has no 13-digit tax id ───
  await page.goto('/tax-invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill(`MC-IND-${suffix}`);
  await dlg.getByRole('button', { name: /มานี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้าตามใบสั่งซื้อ');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('5000');
  await capture('step-04-ti-no-buyer-taxid', {
    highlight: 'main',
    arrow: 'right',
    caption:
      `ขั้นที่ 4: ออกใบกำกับภาษีให้ "${personName}" — ตัวอย่างเอกสารด้านขวา บล็อกผู้ซื้อ` +
      ' แสดงแค่ "ชื่อ" ไม่มีเลขประจำตัวผู้เสียภาษี 13 หลัก (ม.86/4 #3 ไม่บังคับเมื่อ' +
      ' ผู้ซื้อไม่จด VAT). ยังเป็นใบกำกับภาษีเต็มรูปถูกต้อง และคิด VAT 7% ตามปกติ (ไม่ post)',
  });

});

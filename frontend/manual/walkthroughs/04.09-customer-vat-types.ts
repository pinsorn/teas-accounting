/**
 * 04.09 — ลูกค้า จด VAT vs ไม่จด VAT — เลขผู้เสียภาษีผู้ซื้อบนใบกำกับภาษี (ม.86/4 #3)
 *
 * Chapter: 4. งานขาย
 * Story: ใบกำกับภาษีเต็มรูปต้องมีเลขผู้เสียภาษีของผู้ซื้อ **เฉพาะเมื่อผู้ซื้อจด VAT**
 *        (ม.86/4 #3). ถ้าผู้ซื้อไม่จด VAT (เช่น บุคคลธรรมดา) ไม่ต้องมีเลขผู้เสียภาษีผู้ซื้อ.
 *
 * Persona: admin. Captured against /tax-invoices/new — สลับผู้ซื้อเพื่อดูตัวอย่างเอกสาร
 * (ไม่ post; เปรียบเทียบบล็อกผู้ซื้อบนใบกำกับภาษี).
 *
 * Data (co2): VAT = "บริษัท แอคมี จำกัด" (มีเลขผู้เสียภาษี) · non-VAT = "คุณสมชาย ใจดี"
 * (บุคคลธรรมดา ไม่มีเลขผู้เสียภาษี).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.09',
  title: 'ลูกค้าจด VAT vs ไม่จด VAT (ม.86/4)',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ผู้ขายที่จด VAT ออกใบกำกับภาษีให้ผู้ซื้อได้ "ทุกราย" — แต่กฎ **ม.86/4 #3** กำหนดให้ใส่
**ชื่อ-ที่อยู่-เลขผู้เสียภาษีของผู้ซื้อ "เฉพาะเมื่อผู้ซื้อจด VAT"**:

- **ผู้ซื้อจด VAT** (นิติบุคคล/กิจการจด VAT) → ใบกำกับภาษีต้องมี **เลขผู้เสียภาษี 13 หลัก +
  สาขา** ของผู้ซื้อ (เพื่อให้ผู้ซื้อเอาไปเคลมภาษีซื้อได้).
- **ผู้ซื้อไม่จด VAT** (บุคคลธรรมดา/ผู้บริโภคทั่วไป) → **ไม่ต้องมีเลขผู้เสียภาษีผู้ซื้อ**
  (ใส่แค่ชื่อก็พอ) เพราะผู้ซื้อเคลมภาษีซื้อไม่ได้อยู่แล้ว.

ระบบดึงสถานะ VAT จากข้อมูลหลักลูกค้า (ดู 03.01) มาแสดง/ซ่อนเลขผู้เสียภาษีบนเอกสารให้เอง.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ tax_invoice)',
    'มีลูกค้าทั้งจด VAT และไม่จด VAT ในระบบ',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: VAT-registered buyer → tax id shown (ม.86/4 #3) ──────────
  await page.goto('/tax-invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  let dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('แอคมี');
  await dlg.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้า/บริการตามใบสั่งซื้อ');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('10000');
  await capture('step-01-vat-buyer', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ผู้ซื้อ "บริษัท แอคมี จำกัด" (จด VAT) → ตัวอย่างใบกำกับภาษีด้านขวาแสดง' +
      ' "เลขผู้เสียภาษี 13 หลัก + สาขา" ของผู้ซื้อ ตาม ม.86/4 #3 (ผู้ซื้อนำไปเคลมภาษีซื้อได้)',
  });

  // ─── Step 2: switch to a non-VAT buyer → tax id omitted ──────────────
  await page.getByRole('button', { name: 'เปลี่ยน' }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('สมชาย');
  await dlg.getByRole('button', { name: /สมชาย/ }).first().click();
  await capture('step-02-nonvat-buyer', {
    highlight: 'main',
    arrow: 'right',
    caption:
      'ขั้นที่ 2: เปลี่ยนผู้ซื้อเป็น "คุณสมชาย ใจดี" (บุคคลธรรมดา ไม่จด VAT) → บล็อกผู้ซื้อ' +
      ' บนเอกสารแสดงแค่ "ชื่อ" ไม่มีเลขผู้เสียภาษี (ม.86/4 #3 ไม่บังคับเมื่อผู้ซื้อไม่จด VAT).' +
      ' ยังเป็นใบกำกับภาษีเต็มรูปที่ถูกต้อง — VAT ยังคิด 7% ตามปกติ',
  });

});

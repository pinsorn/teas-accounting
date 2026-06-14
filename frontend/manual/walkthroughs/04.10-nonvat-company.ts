/**
 * 04.10 — กิจการไม่จด VAT (non-VAT company) — ระบบต่างจากกิจการ VAT อย่างไร
 *
 * Chapter: 4. งานขาย
 * Story: กิจการที่ "ไม่จดทะเบียน VAT" (รายได้ไม่ถึงเกณฑ์ 1.8 ล้าน/ปี หรือเลือกไม่จด)
 *        ออกใบกำกับภาษีไม่ได้ (ม.86) และไม่คิด VAT บนเอกสารใด ๆ. ระบบปรับเมนู +
 *        เอกสารให้อัตโนมัติตามสถานะ VAT ของบริษัท (per-company, §4.6).
 *
 * Persona: nonvat (login บริษัทไม่จด VAT = company_id 3 "ร้านนอนแวต เดโม").
 * Captured against company 3. นี่คือ walkthrough เดียวที่ login คนละบริษัท เพื่อเทียบ
 * VAT vs non-VAT — เอกสารอื่นทั้งหมดถ่ายบนบริษัท VAT (company 2).
 *
 * Data (co3): ลูกค้า "คุณนนท์ ซื้อประจำ", สินค้า "สินค้าทั่วไป". ไม่ submit.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.10',
  title: 'กิจการไม่จด VAT (non-VAT)',
  chapter: '4. งานขาย',
  persona: 'nonvat',
  intro: `
**สถานะ VAT เป็นข้อมูลของ "บริษัท"** (ตั้งโดยผู้ดูแลระบบสูงสุด ตอนสร้างบริษัท — §4.6,
ดูบทที่ 0). กิจการที่ **ไม่จด VAT** มีระบบที่ต่างจากกิจการ VAT ดังนี้:

| เรื่อง | กิจการจด VAT | กิจการไม่จด VAT |
|---|---|---|
| ภาษีบนเอกสาร | VAT 7% ทุกใบ | **ไม่มี VAT** (ยอดรวม = ยอดก่อนภาษี) |
| ใบกำกับภาษี (ม.86) | ออกได้ | **ออกไม่ได้** — ใช้ใบแจ้งหนี้/ใบเสร็จแทน |
| ใบลดหนี้/เพิ่มหนี้ | มี | ไม่มี (ผูกกับใบกำกับภาษี) |
| เมนูด้านซ้าย | มีครบ | **ซ่อนเมนูที่เกี่ยวกับ VAT** อัตโนมัติ |
| ภ.พ.30 / e-Tax | ต้องยื่น | ไม่เกี่ยวข้อง |

ระบบ **ปรับให้อัตโนมัติ** ตามสถานะบริษัท — ผู้ใช้ไม่ต้องตั้งค่าเอง และไม่มีปุ่มให้สลับ
โหมด VAT ในหน้าผู้ใช้ทั่วไป (กันออกเอกสารผิดประเภท).
  `.trim(),
  prerequisites: [
    'login เป็นผู้ใช้ของกิจการที่ไม่จด VAT',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: the sidebar — VAT-only menus are hidden ─────────────────
  await page.goto('/');
  await page.getByTestId('nav-gates-ready').waitFor({ state: 'attached', timeout: 15_000 });
  await capture('step-01-sidebar', {
    highlight: '[data-testid="app-sidebar"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 1: เมนูด้านซ้ายของกิจการไม่จด VAT — สังเกตว่า "ไม่มี" ใบกำกับภาษี /' +
      ' ใบลดหนี้ / ใบเพิ่มหนี้ (เมนูที่เกี่ยวกับ VAT ถูกซ่อนอัตโนมัติ ตาม ม.86).' +
      ' ยังขายของได้ผ่านใบเสนอราคา → ใบส่งของ → ใบแจ้งหนี้ → ใบเสร็จ',
  });

  // ─── Step 2: a sales document — no VAT column / no VAT line ───────────
  await page.goto('/invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('นนท์');
  await dlg.getByRole('button', { name: /นนท์/ }).first().click();
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้าทั่วไป');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('1500');
  await capture('step-02-no-vat', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: ใบแจ้งหนี้ของกิจการไม่จด VAT — ตารางรายการ "ไม่มีคอลัมน์ VAT" และ' +
      ' ตัวอย่างเอกสารด้านขวา ยอดรวม = ยอดก่อนภาษี (2 × 1,500 = 3,000) ไม่มีบรรทัด' +
      ' ภาษีมูลค่าเพิ่ม. เทียบกับกิจการ VAT ที่จะมี VAT 7% เพิ่มทุกใบ',
  });

});

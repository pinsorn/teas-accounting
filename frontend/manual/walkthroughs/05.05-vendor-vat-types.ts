/**
 * 05.05 — ผู้ขาย จด VAT / ไม่จด VAT / ต่างประเทศ — ผลต่อ "ภาษีซื้อ"
 *
 * Chapter: 5. งานซื้อ
 * Story: สถานะ VAT ของผู้ขายเป็นตัวกำหนดว่าเราเคลม "ภาษีซื้อ (Input VAT)" ได้หรือไม่
 *        ตอนบันทึกใบกำกับภาษีซื้อ. ระบบแสดงข้อความกำกับให้ตามชนิดผู้ขาย 4 แบบ.
 *
 * Persona: admin. Captured against /vendor-invoices/new — เปลี่ยนผู้ขายเพื่อดูข้อความ
 * (ไม่ post; เป็นบทอธิบายความแตกต่างตามชนิดผู้ขาย).
 *
 * Data (co2 vendors): MV-DOM-001 ออฟฟิศ ซัพพลาย (ในประเทศ VAT) · MV-DOM-002 ร้านโชห่วย
 * (ไม่จด VAT) · MV-FOR-001 Amazon (ต่างประเทศ ไม่มี VAT-D ไทย) · MV-FOR-002 Netflix
 * (ต่างประเทศ มี VAT-D ไทย).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '05.05',
  title: 'ผู้ขาย จด VAT / ไม่จด VAT / ต่างประเทศ',
  chapter: '5. งานซื้อ',
  persona: 'admin',
  intro: `
"ภาษีซื้อ (Input VAT)" ที่เคลมคืนได้ มีก็ต่อเมื่อ **ผู้ขายจด VAT** และออกใบกำกับภาษีให้.
ระบบดูสถานะผู้ขายแล้วแสดงข้อความกำกับให้อัตโนมัติ — แบ่งผู้ขายเป็น 4 แบบ:

| ผู้ขาย | ภาษีซื้อ | หมายเหตุระบบ |
|---|---|---|
| **ในประเทศ จด VAT** | เคลมได้ปกติ | (ไม่มีข้อความ — กรณีปกติ) |
| **ในประเทศ ไม่จด VAT** | **เคลมไม่ได้** | VAT รวมเป็นค่าใช้จ่าย |
| **ต่างประเทศ ไม่มี VAT-D ไทย** | คำนวณเอง | **ภ.พ.36 reverse charge** (นำส่งแทน) |
| **ต่างประเทศ มี VAT-D ไทย** | เคลมได้ปกติ | จดทะเบียน VAT-D ในไทยแล้ว |

(สถานะ VAT/ต่างประเทศ/VAT-D ตั้งที่ข้อมูลหลักผู้ขาย — ดู 03.02.)
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ vendor_invoice)',
    'มีผู้ขายหลายชนิด (VAT/ไม่จด VAT/ต่างประเทศ) ในระบบ',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: domestic VAT vendor → input VAT claimable (no warning) ──
  await page.goto('/vendor-invoices/new');
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  let dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('ออฟฟิศ');
  await dlg.getByRole('button', { name: /ออฟฟิศ/ }).first().click();
  await capture('step-01-domestic-vat', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ผู้ขาย "บริษัท ออฟฟิศ ซัพพลาย จำกัด" (ในประเทศ จด VAT) — กรณีปกติ' +
      ' ไม่มีข้อความเตือน, เคลมภาษีซื้อได้ตามใบกำกับภาษีที่ผู้ขายออกให้',
  });

  // ─── Step 2: domestic non-VAT vendor → no input VAT ──────────────────
  await page.getByRole('button', { name: 'เปลี่ยน' }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('โชห่วย');
  await dlg.getByRole('button', { name: /โชห่วย/ }).first().click();
  await capture('step-02-domestic-nonvat', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: เปลี่ยนเป็น "ร้านโชห่วยตัวอย่าง" (ไม่จด VAT) → ระบบเตือน "เคลม Input VAT' +
      ' ไม่ได้ (VAT รวมเป็นค่าใช้จ่าย)" เพราะผู้ขายไม่จด VAT จึงออกใบกำกับภาษีไม่ได้',
  });

  // ─── Step 3: foreign, no Thai VAT-D → ภ.พ.36 reverse charge ──────────
  await page.getByRole('button', { name: 'เปลี่ยน' }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('Amazon');
  await dlg.getByRole('button', { name: /Amazon/ }).first().click();
  await capture('step-03-foreign-pnd36', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เปลี่ยนเป็น "Amazon Web Services" (ต่างประเทศ ไม่มี VAT-D ไทย) → ระบบเตือน' +
      ' "ภ.พ.36 reverse charge" — ผู้จ่ายในไทยต้องนำส่ง VAT แทน แล้วเคลมภาษีซื้อเดือนถัดไป',
  });

  // ─── Step 4: foreign WITH Thai VAT-D → normal input VAT ──────────────
  await page.getByRole('button', { name: 'เปลี่ยน' }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('Netflix');
  await dlg.getByRole('button', { name: /Netflix/ }).first().click();
  await capture('step-04-foreign-vatd', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 4: เปลี่ยนเป็น "Netflix" (ต่างประเทศ แต่จด VAT-D ในไทย) → ระบบแจ้ง "เคลม' +
      ' Input VAT ปกติ" เพราะจดทะเบียน VAT-D ในไทยแล้ว จึงปฏิบัติเหมือนผู้ขายในประเทศจด VAT',
  });

});

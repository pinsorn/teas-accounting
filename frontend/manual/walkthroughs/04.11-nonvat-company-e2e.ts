/**
 * 04.11 — กิจการไม่จด VAT: วงจรขายครบจบ (ใบเสนอราคา → ใบแจ้งหนี้/บิลเงินสด → ใบเสร็จ)
 *
 * Chapter: 4. งานขาย
 * Story: 04.10 อธิบาย "ความต่าง" ของกิจการไม่จด VAT แล้ว — บทนี้พาทำ "วงจรขายจริง"
 *        ของกิจการไม่จด VAT ตั้งแต่ต้นจนจบ: ใบเสนอราคา → ใบแจ้งหนี้ (ไม่ใช่ใบกำกับภาษี!)
 *        → ใบเสร็จ/บิลเงินสด. ตอกย้ำ 3 จุด: ไม่มีบรรทัด VAT, ไม่มีเมนูใบกำกับภาษี,
 *        ยอดรวม = ยอดก่อนภาษี.
 *
 * Persona: nonvat (company_id 3 "ร้านนอนแวต เดโม", vat_registered=false).
 * Data (co3): ลูกค้า "คุณนนท์ ซื้อประจำ" (บุคคลธรรมดา), สินค้า "สินค้าทั่วไป".
 * co3 ไม่มีหน่วยธุรกิจ (ไม่บังคับสำหรับกิจการไม่จด VAT) → ไม่เลือกหน่วยธุรกิจ.
 * ⚠️ ออกใบเสนอราคา + ใบแจ้งหนี้จริงบน company 3 (ออกเลขที่เอกสาร gapless).
 *    ใบเสร็จแสดงเป็น "ฟอร์มบิลเงินสด" เท่านั้น (ไม่ post — re-capture ได้ซ้ำ).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.11',
  title: 'กิจการไม่จด VAT: วงจรขายครบจบ',
  chapter: '4. งานขาย',
  persona: 'nonvat',
  intro: `
กิจการที่ **ไม่จด VAT** ก็มีวงจรขายครบเหมือนกิจการ VAT — เพียงแต่ **ไม่มีภาษีมูลค่าเพิ่ม**
และ **ออกใบกำกับภาษีไม่ได้** (ม.86). วงจรเอกสารคือ:

1. **ใบเสนอราคา (Quotation)** — เสนอราคาให้ลูกค้า (ยอดเป็นราคาเต็ม ไม่บวก VAT).
2. **ใบแจ้งหนี้ / บิลเงินสด (Billing Note)** — เรียกเก็บเงิน. **ไม่ใช่ "ใบกำกับภาษี"** —
   กิจการไม่จด VAT ออกใบกำกับภาษีไม่ได้ จึงใช้ใบแจ้งหนี้/ใบเสร็จแทน.
3. **ใบเสร็จรับเงิน (Receipt)** — เมื่อรับเงิน. กิจการไม่จด VAT ออกได้แบบ "บิลเงินสด"
   ที่ **ไม่ต้องอ้างใบกำกับภาษี** (เพราะไม่มีใบกำกับภาษีให้อ้าง).

**ทุกเอกสารไม่มีบรรทัด VAT — ยอดรวม = ยอดก่อนภาษีเสมอ** (ราคาที่ตกลง = ราคาที่จ่ายจริง).
ระบบปรับเมนู + เอกสารให้อัตโนมัติตามสถานะ VAT ของบริษัท (per-company, §4.6).
  `.trim(),
  prerequisites: [
    'login เป็นผู้ใช้ของกิจการที่ไม่จด VAT (co3)',
    'มีลูกค้า + สินค้าในระบบ',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: quotation (no VAT line) ─────────────────────────────────
  await page.goto('/quotations/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  let dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('นนท์');
  await dlg.getByRole('button', { name: /นนท์/ }).first().click();
  // co3 has no business units → leave หน่วยธุรกิจ unset (optional for non-VAT).
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้าทั่วไป (เสนอราคา)');
  await page.getByLabel('จำนวน 1').fill('3');
  await page.getByLabel('ราคา/หน่วย 1').fill('1200');
  await capture('step-01-quotation', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 1: ใบเสนอราคาของกิจการไม่จด VAT — ตารางรายการ "ไม่มีคอลัมน์ VAT"' +
      ' และกล่องสรุป ยอดรวม = ยอดก่อนภาษี (3 × 1,200 = 3,600) ไม่มีบรรทัดภาษีมูลค่าเพิ่ม',
  });

  // ─── Step 2: issue quotation → doc number ────────────────────────────
  await page.getByRole('button', { name: 'ออกใบเสนอราคา' }).click();
  await page.waitForURL(/\/quotations\/\d+/, { timeout: 15_000 });
  await capture('step-02-quotation-issued', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: ออกใบเสนอราคาแล้ว → ระบบออกเลขที่เอกสาร. จากหน้านี้แปลงเป็น' +
      ' "ใบแจ้งหนี้" ได้ — แต่ไม่มีปุ่ม "ออกใบกำกับภาษี" (กิจการไม่จด VAT ออกไม่ได้)',
  });

  // ─── Step 3: billing note — ใบแจ้งหนี้ (NOT a tax invoice) ────────────
  await page.goto('/invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('นนท์');
  await dlg.getByRole('button', { name: /นนท์/ }).first().click();
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้าทั่วไป');
  await page.getByLabel('จำนวน 1').fill('3');
  await page.getByLabel('ราคา/หน่วย 1').fill('1200');
  await capture('step-03-billing-note', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: ใบแจ้งหนี้ / บิลเงินสด (Billing Note) — เอกสารเรียกเก็บเงินของกิจการ' +
      ' ไม่จด VAT. หัวเอกสารคือ "ใบแจ้งหนี้" ไม่ใช่ "ใบกำกับภาษี" และยอดเรียกเก็บ' +
      ' = ยอดก่อนภาษี 3,600 (ไม่มี VAT 7% เพิ่มเหมือนกิจการ VAT)',
  });

  // ─── Step 4: issue billing note → doc number ─────────────────────────
  await page.getByRole('button', { name: 'ออกใบแจ้งหนี้' }).click();
  await page.waitForURL(/\/invoices\/\d+/, { timeout: 15_000 });
  await capture('step-04-billing-issued', {
    highlight: 'main',
    caption:
      'ขั้นที่ 4: ออกใบแจ้งหนี้แล้ว (สถานะ "ออกแล้ว") — เลขที่เอกสารออกตามลำดับ.' +
      ' เมื่อรับเงินจะออก "ใบเสร็จ/บิลเงินสด" อ้างอิงใบแจ้งหนี้นี้ (ดูขั้นถัดไป)',
  });

  // ─── Step 5: receipt — standalone cash bill (no tax invoice to apply) ─
  // For a non-VAT company /receipts/new defaults to the "บิลเงินสด" (standalone)
  // mode automatically (no TI exists to apply against). Shown as a form (no post).
  await page.goto('/receipts/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  dlg = page.getByRole('dialog');
  await dlg.getByRole('textbox').fill('นนท์');
  await dlg.getByRole('button', { name: /นนท์/ }).first().click();
  await capture('step-05-receipt-cashbill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 5: ฟอร์มใบเสร็จของกิจการไม่จด VAT เปิดเป็นโหมด "บิลเงินสด (ไม่อ้างเอกสาร)"' +
      ' อัตโนมัติ — ไม่ต้องอ้างใบกำกับภาษี (เพราะออกใบกำกับภาษีไม่ได้). กรอกรายการ +' +
      ' ยอดรับ แล้ว "บันทึกเอกสาร" เพื่อออกใบเสร็จ. ยอดรับ = ยอดก่อนภาษี ไม่มี VAT',
  });

});

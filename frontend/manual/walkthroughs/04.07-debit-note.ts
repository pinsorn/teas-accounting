/**
 * 04.07 — ออกใบเพิ่มหนี้ (Debit Note) — เพิ่มยอดจากใบกำกับภาษีเดิม
 *
 * Chapter: 4. งานขาย
 * Story: คู่กับใบลดหนี้ (04.06). เมื่อต้อง "เพิ่ม" ยอดหลังออกใบกำกับภาษีแล้ว (เรียก
 *        เก็บขาด / ส่งของเพิ่ม / คิดราคาต่ำไป) กฎหมายให้ออก "ใบเพิ่มหนี้" (ม.86/9)
 *        อ้างอิงใบกำกับภาษีเดิม — ไม่ใช่แก้ใบเดิม (ใบกำกับภาษีตรึงถาวร §4.2).
 *
 * Persona: admin (sales.tax_invoice.* + sales.debit_note.create/post).
 * Self-contained: ออก+โพสต์ใบกำกับภาษีก่อน แล้วไป /debit-notes/new — ฟอร์มเดียวกับ
 * ใบลดหนี้ (AdjustmentNoteForm, noteType="Debit") ต่างที่ทิศทาง (เพิ่มยอด) และ
 * รายการเหตุผล (DEBIT_NOTE_REASONS). ผู้ซื้อคัดลอกจากใบกำกับภาษีเดิมอัตโนมัติ.
 *
 * Data (co2): ลูกค้า "บริษัท แอคมี จำกัด".
 * ⚠️ สร้างใบกำกับภาษี + ใบเพิ่มหนี้จริงบน company 2 (ออกเลขที่เอกสารถาวร gapless).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.07',
  title: 'ออกใบเพิ่มหนี้ (แก้ไขใบกำกับภาษี)',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ใบเพิ่มหนี้ (Debit Note) — **ม.86/9** เป็นคู่ตรงข้ามของใบลดหนี้ (04.06): ใช้เมื่อต้อง
**เพิ่มยอด** จากใบกำกับภาษีที่ออกไปแล้ว เช่น เรียกเก็บขาด, ส่งสินค้าเพิ่ม, หรือคิดราคา
ต่ำกว่าจริง.

เนื่องจากใบกำกับภาษีที่โพสต์แล้ว **แก้ไขไม่ได้** (ม.86 + พรบ.การบัญชี §4.2) การปรับยอด
ขึ้นจึงต้องออกเอกสารใหม่อ้างอิงใบเดิม ไม่ใช่แก้ใบเดิม. ฟอร์มและขั้นตอนเหมือนใบลดหนี้
ทุกประการ — ต่างเพียง **ทิศทาง (เพิ่ม)** และ **รายการเหตุผล**. ผู้ซื้อและอัตราภาษี (7%)
คัดลอก/ล็อกจากใบกำกับภาษีเดิม และแสดงภาษีแยกออกจากมูลค่า.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ tax_invoice + debit_note)',
    'มีใบกำกับภาษีที่โพสต์แล้วของลูกค้ารายนั้น',
  ],
}, async ({ page, capture }) => {

  // ════════════ เตรียม: ออก+โพสต์ใบกำกับภาษี (ใบที่จะถูกเพิ่มหนี้) ════════════
  await page.goto('/tax-invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const tiCust = page.getByRole('dialog');
  await tiCust.getByRole('textbox').fill('แอคมี');
  await tiCust.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้า ชุดอุปกรณ์สำนักงาน');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('18000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).first().click();
  const tiPost = page.getByRole('dialog');
  await tiPost.waitFor({ state: 'visible' });
  await tiPost.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+/, { timeout: 15_000 });

  // ─── Step 1: blank Debit Note form ───────────────────────────────────
  await page.goto('/debit-notes/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "ใบเพิ่มหนี้ (ม.86/9)" — โครงเดียวกับใบลดหนี้: ① อ้างอิงใบกำกับ' +
      ' ภาษีเดิม, ② เหตุผล (บังคับ) + หน่วยธุรกิจ, ③ มูลค่าที่ปรับเพิ่ม',
  });

  // ─── Step 2: pick the original (Posted) Tax Invoice ──────────────────
  const tiPicker = page.getByRole('combobox', { name: 'originalTaxInvoiceId' });
  await tiPicker.click();
  await tiPicker.fill('แอคมี');
  const tiOption = page.locator('#taxinvoice-listbox').getByRole('button').first();
  await tiOption.waitFor({ state: 'attached', timeout: 10_000 });
  await tiOption.dispatchEvent('mousedown');
  await capture('step-02-pick-ti', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: เลือก "ใบกำกับภาษีเดิม" (เฉพาะใบที่โพสต์แล้ว). ระบบดึงผู้ซื้อ +' +
      ' อัตราภาษีจากใบนั้น และล็อกอัตราภาษีไว้ที่ 7% ตามใบเดิม',
  });

  // ─── Step 3: reason + BU + adjustment amount (increase) ──────────────
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.locator('textarea').first().fill('ส่งสินค้าเพิ่มเติม — เรียกเก็บเพิ่ม 3,000 บาท');
  await page.getByLabel('adjustmentSubtotal').fill('3000');
  await capture('step-03-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: ระบุ "เหตุผล" (บังคับ) + หน่วยธุรกิจ + "มูลค่าที่ปรับเพิ่ม" 3,000.' +
      ' กล่องสรุปคำนวณภาษีมูลค่าเพิ่ม 7% = 210 แยกออกจากมูลค่า รวม 3,210',
  });

  // ─── Step 4: post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกเอกสาร' }).click();
  const dnConfirm = page.getByRole('dialog');
  await dnConfirm.waitFor({ state: 'visible' });
  await capture('step-04-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 4: กด "บันทึกเอกสาร" → กล่องยืนยันสรุปยอดเพิ่มหนี้ + ภาษี. เมื่อโพสต์แล้ว' +
      ' ใบเพิ่มหนี้จะตรึงถาวรเช่นเดียวกับใบกำกับภาษี',
  });

  // ─── Step 5: posted Debit Note detail ────────────────────────────────
  await dnConfirm.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/debit-notes\/\d+/, { timeout: 15_000 });
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: ใบเพิ่มหนี้ออกเลขที่เอกสารแล้ว อ้างอิงใบกำกับภาษีเดิมใน "เอกสารอ้างอิง"' +
      ' พร้อมเหตุผลและมูลค่าที่เพิ่ม — วิธีปรับยอด "ขึ้น" ที่ถูกต้องตามกฎหมาย',
  });

});

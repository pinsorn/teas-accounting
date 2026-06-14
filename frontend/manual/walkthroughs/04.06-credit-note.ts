/**
 * 04.06 — ออกใบลดหนี้ (Credit Note) — แก้ไขใบกำกับภาษีที่ตรึงถาวร
 *
 * Chapter: 4. งานขาย
 * Story: ใบกำกับภาษีที่โพสต์แล้ว "แก้ไขไม่ได้" (ม.86 + พรบ.การบัญชี). ถ้าต้องลดยอด
 *        (คืนสินค้า / ลดราคา / เรียกเก็บเกิน) ต้องออก "ใบลดหนี้" (ม.86/10) อ้างอิง
 *        ใบกำกับภาษีเดิม — ไม่ใช่แก้ใบเดิม. (ถ้าต้องเพิ่มยอด ใช้ "ใบเพิ่มหนี้" ม.86/9.)
 *
 * Persona: admin (sales.tax_invoice.* + sales.credit_note.create/post).
 * Self-contained: ออก+โพสต์ใบกำกับภาษีก่อน แล้วไป /credit-notes/new เลือกใบกำกับ
 * ภาษีเดิมในช่องค้นหา (status=Posted) → ระบุเหตุผล + มูลค่าที่ลด → โพสต์.
 * ผู้ซื้อถูกคัดลอกจากใบกำกับภาษีเดิมโดยอัตโนมัติ (เลือกผู้ซื้อใหม่ไม่ได้).
 *
 * Data (co2): ลูกค้า "บริษัท แอคมี จำกัด".
 * ⚠️ สร้างใบกำกับภาษี + ใบลดหนี้จริงบน company 2 (ออกเลขที่เอกสารถาวร gapless).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.06',
  title: 'ออกใบลดหนี้ (แก้ไขใบกำกับภาษี)',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ใบกำกับภาษีที่โพสต์แล้ว **ตรึงถาวร แก้ไข/ลบไม่ได้** (ม.86 + พรบ.การบัญชี). เมื่อมี
เหตุต้องปรับยอดหลังออกใบกำกับภาษี กฎหมายกำหนดให้ออกเอกสารใหม่อ้างอิงใบเดิม:

- **ใบลดหนี้ (Credit Note) — ม.86/10** ใช้เมื่อ "ลดยอด": ลูกค้าคืนสินค้า, ลดราคาหลังขาย,
  คำนวณเรียกเก็บเกิน, สินค้าชำรุด.
- **ใบเพิ่มหนี้ (Debit Note) — ม.86/9** ใช้เมื่อ "เพิ่มยอด": เรียกเก็บขาด, ส่งของเพิ่ม.

ทั้งสองอ้างอิงใบกำกับภาษีเดิมเสมอ และ **คัดลอกผู้ซื้อจากใบเดิม** (ออกใบลดหนี้ให้คนอื่นไม่ได้).
อัตราภาษีถูกล็อกตามใบกำกับภาษีเดิม (7%) และแสดงภาษีแยกออกจากมูลค่าตามกฎ. บทนี้แสดง
"ใบลดหนี้" เป็นตัวอย่าง.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ tax_invoice + credit_note)',
    'มีใบกำกับภาษีที่โพสต์แล้วของลูกค้ารายนั้น',
  ],
}, async ({ page, capture }) => {

  // ════════════ เตรียม: ออก+โพสต์ใบกำกับภาษี (ใบที่จะถูกลดหนี้) ════════════
  await page.goto('/tax-invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const tiCust = page.getByRole('dialog');
  await tiCust.getByRole('textbox').fill('แอคมี');
  await tiCust.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าสินค้า ชุดอุปกรณ์สำนักงาน');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('20000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).first().click();
  const tiPost = page.getByRole('dialog');
  await tiPost.waitFor({ state: 'visible' });
  await tiPost.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+/, { timeout: 15_000 });

  // ─── Step 1: blank Credit Note form ──────────────────────────────────
  await page.goto('/credit-notes/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "ใบลดหนี้ (ม.86/10)" — ① อ้างอิงใบกำกับภาษีเดิม, ② เหตุผล' +
      ' (บังคับตามกฎหมาย) + หน่วยธุรกิจ, ③ มูลค่าที่ปรับ. ผู้ซื้อจะคัดลอกจากใบเดิมอัตโนมัติ',
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
      'ขั้นที่ 2: ค้นหาและเลือก "ใบกำกับภาษีเดิม" (แสดงเฉพาะใบที่โพสต์แล้ว). ระบบดึง' +
      ' ผู้ซื้อ + อัตราภาษีจากใบนั้นมา และล็อกอัตราภาษีไว้ที่ 7% ตามใบเดิม',
  });

  // ─── Step 3: reason (legally required) + BU + adjustment amount ──────
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.locator('textarea').first().fill('ลูกค้าคืนสินค้าบางส่วน — ลดยอด 5,000 บาท');
  await page.getByLabel('adjustmentSubtotal').fill('5000');
  await capture('step-03-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: ระบุ "เหตุผล" (กฎหมายบังคับให้มีเหตุผลในใบลดหนี้) + หน่วยธุรกิจ +' +
      ' "มูลค่าที่ปรับ" 5,000. กล่องสรุปคำนวณภาษีมูลค่าเพิ่ม 7% = 350 แยกออกจากมูลค่า',
  });

  // ─── Step 4: post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกเอกสาร' }).click();
  const cnConfirm = page.getByRole('dialog');
  await cnConfirm.waitFor({ state: 'visible' });
  await capture('step-04-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 4: กด "บันทึกเอกสาร" → กล่องยืนยันสรุปยอดลดหนี้ + ภาษี. เมื่อโพสต์แล้ว' +
      ' ใบลดหนี้จะตรึงถาวรเช่นเดียวกับใบกำกับภาษี',
  });

  // ─── Step 5: posted Credit Note detail ───────────────────────────────
  await cnConfirm.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/credit-notes\/\d+/, { timeout: 15_000 });
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: ใบลดหนี้ออกเลขที่เอกสารแล้ว อ้างอิงใบกำกับภาษีเดิม (เห็นใน' +
      ' "เอกสารอ้างอิง") พร้อมเหตุผลและมูลค่าที่ลด. นี่คือวิธี "แก้ไข" ใบกำกับภาษี' +
      ' ที่ถูกต้องตามกฎหมาย — ออกเอกสารใหม่ ไม่ใช่แก้ใบเดิม',
  });

});

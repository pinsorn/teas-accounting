/**
 * 05.01 — ออกใบสั่งซื้อ (Purchase Order) + อนุมัติ — จุดเริ่มของงานซื้อ
 *
 * Chapter: 5. งานซื้อ
 * Story: สร้างใบสั่งซื้อสินค้า/บริการจากผู้ขาย → เลือกผู้ขาย + รายการ → บันทึกเป็น
 *        ฉบับร่าง → อนุมัติ. ใบสั่งซื้อเป็นเอกสารภายในเพื่อควบคุมการใช้จ่าย — เลขที่
 *        เอกสาร (PO-NNNN) จะออก "ตอนอนุมัติ" ไม่ใช่ตอนสร้างร่าง.
 *
 * Persona: admin (purchase.purchase_order.create + .approve). demo-admin = SUPER_ADMIN;
 * ตั้งแต่ cont.77 (2026-05-30) การอนุมัติเป็น permission-based — ผู้สร้างอนุมัติเองได้
 * (รองรับ SME ผู้ใช้คนเดียว), กฎ creator≠approver (+ ck_po_sod) ถูกถอดออก.
 * Captured against /purchase-orders/new (PartySelectBox vendor + LineItemsTable).
 *
 * Data (co2): ผู้ขาย "บริษัท ออฟฟิศ ซัพพลาย จำกัด" (MV-DOM-001, ในประเทศ จด VAT).
 * ⚠️ สร้าง+อนุมัติเอกสารจริงบน company 2 (ออกเลขที่เอกสารตอนอนุมัติ).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '05.01',
  title: 'ออกและอนุมัติใบสั่งซื้อ',
  chapter: '5. งานซื้อ',
  persona: 'admin',
  intro: `
ใบสั่งซื้อ (Purchase Order / PO) คือเอกสารแรกของงานซื้อ — ใช้สั่งซื้อสินค้า/บริการจาก
ผู้ขาย และเป็นเครื่องมือ **ควบคุมการใช้จ่าย** ภายในกิจการ (อนุมัติก่อนซื้อจริง).

**สายงานซื้อเต็มรูป (document chain):**

ใบสั่งซื้อ → **บันทึกใบกำกับภาษีซื้อ** (รับของ + ภาษีซื้อ) → ใบสำคัญจ่าย (จ่ายเงิน +
หัก ณ ที่จ่าย) → หนังสือรับรองหัก ณ ที่จ่าย (50ทวิ)

**ขั้นตอนอนุมัติ (workflow):** ใบสั่งซื้อสร้างเป็น "ฉบับร่าง" ก่อน แล้วต้อง **อนุมัติ** จึงจะ
ใช้งานต่อได้ — และระบบจะออก **เลขที่เอกสาร (PO-NNNN) ตอนอนุมัติ** เท่านั้น. ระบบนี้รองรับ
กิจการขนาดเล็กที่มีผู้ใช้คนเดียว (ผู้สร้างอนุมัติเองได้) แต่กิจการใหญ่สามารถแยกผู้สร้าง/
ผู้อนุมัติตามสิทธิ์ได้ (Segregation of Duties).
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ purchase_order create + approve)',
    'มีผู้ขาย + หน่วยธุรกิจ ในระบบ (บทที่ 3)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: blank PO form ───────────────────────────────────────────
  await page.goto('/purchase-orders/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "สร้างใบสั่งซื้อ" — ส่วน ① ผู้ขาย, ② ข้อมูลเอกสาร (วันที่ +' +
      ' วันที่คาดว่าจะส่ง), ③ รายการ. ตัวอย่างเอกสารด้านขวาออกในนามบริษัทเรา ส่งถึงผู้ขาย',
  });

  // ─── Step 2: pick vendor + BU + line ─────────────────────────────────
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const vDialog = page.getByRole('dialog');
  await vDialog.getByRole('textbox').fill('ออฟฟิศ');
  await vDialog.getByRole('button', { name: /ออฟฟิศ/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('กระดาษถ่ายเอกสาร A4 80 แกรม');
  await page.getByLabel('จำนวน 1').fill('20');
  await page.getByLabel('ราคา/หน่วย 1').fill('120');
  await capture('step-02-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: เลือกผู้ขาย "บริษัท ออฟฟิศ ซัพพลาย จำกัด" (ในประเทศ จด VAT) + หน่วย' +
      ' ธุรกิจ + กรอกรายการ (20 × 120). ระบบคำนวณยอดก่อนภาษี 2,400 + VAT 7% ให้อัตโนมัติ',
  });

  // ─── Step 3: save as draft (no number yet) ───────────────────────────
  await page.getByRole('button', { name: 'บันทึก' }).first().click();
  await page.waitForURL(/\/purchase-orders\/\d+/, { timeout: 15_000 });
  await page.getByTestId('po-approve').waitFor({ state: 'visible', timeout: 10_000 });
  await capture('step-03-draft', {
    highlight: 'main',
    caption:
      'ขั้นที่ 3: บันทึกแล้ว → ใบสั่งซื้อสถานะ "ฉบับร่าง" ยัง "ไม่มีเลขที่เอกสาร".' +
      ' มุมขวามีปุ่ม "อนุมัติ" — กิจการตรวจสอบก่อนอนุมัติเพื่อควบคุมการใช้จ่าย',
  });

  // ─── Step 4: approve → number allocated, status Approved ─────────────
  await page.getByTestId('po-approve').click();
  await page.getByTestId('po-create-pv').waitFor({ state: 'visible', timeout: 15_000 });
  await capture('step-04-approved', {
    highlight: 'main',
    caption:
      'ขั้นที่ 4: กด "อนุมัติ" → ระบบออกเลขที่เอกสาร (PO-NNNN) และเปลี่ยนสถานะเป็น' +
      ' "อนุมัติแล้ว". ตอนนี้พร้อมรับของ/บันทึกใบกำกับภาษีซื้อ และสร้างใบสำคัญจ่ายอ้างอิงต่อ',
  });

});

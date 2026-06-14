/**
 * 04.05 — รับชำระเงิน + ออกใบเสร็จรับเงิน (Receipt)
 *
 * Chapter: 4. งานขาย
 * Story: เมื่อลูกค้าชำระเงินตามใบกำกับภาษี → ออกใบเสร็จรับเงินอ้างอิงใบกำกับภาษีนั้น.
 *        ใบเสร็จปิดยอดลูกหนี้ (AR) และเป็นหลักฐานการรับเงิน.
 *
 * Persona: admin (sales.tax_invoice.create/post + sales.receipt.create/post).
 * Self-contained: ออก+โพสต์ใบกำกับภาษีก่อน (ตาม 04.04) แล้วกดปุ่ม "สร้างใบเสร็จ"
 * บนหน้ารายละเอียดใบกำกับภาษี — ฟอร์มใบเสร็จจะดึงลูกค้า + ใบกำกับภาษี + ยอดเงิน
 * มาให้อัตโนมัติ (prefill ผ่าน query string ?ti & ?customer & ?amount).
 *
 * Data (co2): ลูกค้า "บริษัท แอคมี จำกัด".
 * ⚠️ สร้างใบกำกับภาษี + ใบเสร็จจริงบน company 2 (ออกเลขที่เอกสารถาวร gapless).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.05',
  title: 'รับชำระเงินและออกใบเสร็จ',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ใบเสร็จรับเงิน (Receipt) คือหลักฐานการรับชำระเงินจากลูกค้า. ในระบบ VAT ใบเสร็จจะ
**อ้างอิงใบกำกับภาษี** ที่ออกไว้ (ซึ่งตรึงถาวรแล้ว) เพื่อปิดยอดลูกหนี้ของใบนั้น.

**วิธีที่สะดวกที่สุด** คือออกใบเสร็จจากหน้ารายละเอียดใบกำกับภาษีโดยตรง — กดปุ่ม
"สร้างใบเสร็จ" แล้วระบบจะดึง **ลูกค้า + ใบกำกับภาษี + ยอดเงิน** มากรอกให้อัตโนมัติ
ไม่ต้องพิมพ์ซ้ำ.

**ภาษีหัก ณ ที่จ่าย (WHT):** ถ้าลูกค้าเป็นนิติบุคคลและหักภาษี ณ ที่จ่าย ใบเสร็จมีส่วน
ให้ระบุยอดหัก → ระบบคำนวณ "เงินที่รับจริง = ยอดรวม − ภาษีหัก ณ ที่จ่าย" ให้.
บทนี้แสดงกรณีรับเต็มจำนวน (ไม่มีการหัก).
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ tax_invoice + receipt)',
    'มีลูกค้าจด VAT ในระบบ',
  ],
}, async ({ page, capture }) => {

  // ════════════ เตรียม: ออก+โพสต์ใบกำกับภาษี (ต้นทางของใบเสร็จ) ════════════
  await page.goto('/tax-invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const tiCust = page.getByRole('dialog');
  await tiCust.getByRole('textbox').fill('แอคมี');
  await tiCust.getByRole('button', { name: /แอคมี/ }).first().click();
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าบริการที่ปรึกษาระบบบัญชี');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('10000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).first().click();
  const tiPost = page.getByRole('dialog');
  await tiPost.waitFor({ state: 'visible' });
  await tiPost.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+/, { timeout: 15_000 });

  // ─── Step 1: posted TI detail — "สร้างใบเสร็จ" available ──────────────
  const receiptLink = page.getByRole('link', { name: 'สร้างใบเสร็จ' });
  await receiptLink.waitFor({ state: 'visible', timeout: 10_000 });
  await capture('step-01-ti-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ใบกำกับภาษีที่โพสต์แล้ว (ยอดรวม 10,700, ยังไม่ชำระ). มุมขวาบนมีปุ่ม' +
      ' "สร้างใบเสร็จ" สำหรับออกใบเสร็จอ้างอิงใบนี้ทันที',
  });

  // ─── Step 2: open prefilled receipt form ─────────────────────────────
  await receiptLink.click();
  await page.waitForURL(/\/receipts\/new/, { timeout: 15_000 });
  await page.getByRole('button', { name: 'บันทึกเอกสาร' }).waitFor({ state: 'visible', timeout: 10_000 });
  await capture('step-02-receipt-prefill', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: ฟอร์มใบเสร็จเปิดขึ้นพร้อมข้อมูล "ดึงมาให้แล้ว" — ลูกค้า "บริษัท แอคมี' +
      ' จำกัด", ใบกำกับภาษีต้นทาง และยอดเงิน 10,700. ไม่ต้องกรอกซ้ำ',
  });

  // ─── Step 3: choose BU (required on income docs for co2) + WHT off ──
  // The TI→receipt prefill carries customer/TI/amount but NOT the business unit,
  // so it must be picked here before posting (co2 requires it on income docs).
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await capture('step-03-totals', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เลือก "หน่วยธุรกิจ". ส่วน "หัก ณ ที่จ่าย" ปิดอยู่ (รับเต็มจำนวน) →' +
      ' เงินที่รับจริง = ยอดรวม 10,700. หากลูกค้าหักภาษี ณ ที่จ่าย ให้เปิดสวิตช์แล้วระบุประเภทเงินได้',
  });

  // ─── Step 4: post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกเอกสาร' }).click();
  const rcConfirm = page.getByRole('dialog');
  await rcConfirm.waitFor({ state: 'visible' });
  await capture('step-04-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 4: กด "บันทึกเอกสาร" → กล่องยืนยันสรุปยอดและเตือนว่าใบเสร็จจะถูกบันทึก' +
      ' ถาวร. ตรวจยอดก่อนยืนยัน',
  });

  // ─── Step 5: posted receipt detail ───────────────────────────────────
  await rcConfirm.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/receipts\/\d+/, { timeout: 15_000 });
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: ใบเสร็จออกเลขที่เอกสารแล้ว (สถานะ "บันทึกแล้ว") และอ้างอิงใบกำกับภาษี' +
      ' ต้นทางใน "เอกสารอ้างอิง". ผลคือยอดลูกหนี้ของใบกำกับภาษีต้นทางถูกปิด — ใบกำกับภาษี' +
      ' นั้นเปลี่ยนสถานะการชำระเงินเป็น "ชำระแล้ว"',
  });

});

/**
 * 05.03 — ใบสำคัญจ่าย + หัก ณ ที่จ่าย (Payment Voucher + WHT) → หนังสือรับรอง 50ทวิ
 *
 * Chapter: 5. งานซื้อ
 * Story: จ่ายเงินผู้ขายด้วย "ใบสำคัญจ่าย" — เมื่อจ่ายค่าบริการ/ค่าเช่าให้นิติบุคคล
 *        ต้อง "หักภาษี ณ ที่จ่าย" (WHT) แล้วนำส่งสรรพากร. ระบบคำนวณยอดหัก + ยอด
 *        จ่ายสุทธิ และออก "หนังสือรับรองการหักภาษี ณ ที่จ่าย (50ทวิ)" ให้.
 *
 * Persona: admin (purchase.payment_voucher.create/.approve/.post). demo-admin = SUPER_ADMIN;
 * ตั้งแต่ cont.77 ผู้สร้างอนุมัติ+โพสต์เองได้ (SME) — กฎ creator≠approver ถูกถอด.
 *
 * Data (co2): ผู้ขาย "บริษัท พร็อพเพอร์ตี้ เช่า จำกัด" (MV-DOM-003) — ค่าเช่าจ่ายให้
 * นิติบุคคล หัก ณ ที่จ่าย 5% (ภ.ง.ด.53). ค่าเช่าเป็นตัวอย่าง WHT ที่ชัดเจน (สินค้า/ของ
 * ไม่ต้องหัก จึงเลือกผู้ขายค่าเช่า ไม่ใช่ผู้ขายของจาก 05.01).
 * ⚠️ สร้าง+อนุมัติ+โพสต์เอกสารจริงบน company 2 (ออก 50ทวิ จริง).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '05.03',
  title: 'ใบสำคัญจ่าย + หัก ณ ที่จ่าย (50ทวิ)',
  chapter: '5. งานซื้อ',
  persona: 'admin',
  intro: `
ใบสำคัญจ่าย (Payment Voucher) คือเอกสารบันทึกการ **จ่ายเงินให้ผู้ขาย**. จุดสำคัญคือ
**ภาษีหัก ณ ที่จ่าย (Withholding Tax / WHT)**:

เมื่อจ่ายค่าบริการ ค่าเช่า ค่าวิชาชีพ ฯลฯ ให้ผู้รับเงิน ผู้จ่าย (กิจการเรา) มีหน้าที่
**หักภาษีไว้ส่วนหนึ่ง** แล้วนำส่งสรรพากรแทนผู้รับเงิน พร้อมออก **หนังสือรับรองการหักภาษี
ณ ที่จ่าย (50ทวิ)** ให้ผู้รับเงินเก็บไว้:

- **อัตราการหัก** ขึ้นกับประเภทเงินได้ — เช่น ค่าบริการ 3%, **ค่าเช่า 5%**, ค่าโฆษณา 2%.
- **แบบนำส่ง** — จ่ายให้นิติบุคคล = **ภ.ง.ด.53**, จ่ายให้บุคคลธรรมดา = ภ.ง.ด.3.
- **เงินจ่ายสุทธิ = มูลค่า + VAT − ภาษีหัก ณ ที่จ่าย** (ผู้รับได้น้อยลงตามยอดที่ถูกหัก).

ตัวอย่างนี้จ่าย **ค่าเช่าสำนักงานให้นิติบุคคล** จึงหัก 5% และออก 50ทวิ แบบ ภ.ง.ด.53.
(ซื้อ "สินค้า/ของ" ไม่ต้องหัก ณ ที่จ่าย — จึงใช้ผู้ขายค่าเช่า ไม่ใช่ผู้ขายของจาก 05.01.)
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ payment_voucher create + approve + post)',
    'มีผู้ขายประเภทค่าบริการ/ค่าเช่า + ประเภทเงินได้ 50ทวิ ในระบบ',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: blank PV form ───────────────────────────────────────────
  await page.goto('/payment-vouchers/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "สร้างใบสำคัญจ่าย" — ① ผู้รับเงิน, ② ข้อมูลเอกสาร (หมวด' +
      ' ค่าใช้จ่าย + วิธีชำระ), ③ รายการ พร้อมช่อง "ประเภทเงินได้ (50ทวิ)" สำหรับหัก ณ ที่จ่าย',
  });

  // ─── Step 2: vendor + category + line + WHT type ─────────────────────
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const vDialog = page.getByRole('dialog');
  await vDialog.getByRole('textbox').fill('พร็อพเพอร์ตี้');
  await vDialog.getByRole('button', { name: /พร็อพเพอร์ตี้|เช่า/ }).first().click();
  // RENT category (code-sorted list: ADS, ENT, OFFICE, RENT, SVC → index 4).
  await page.getByTestId('expense-category-select').selectOption({ index: 4 });
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าเช่าสำนักงาน เดือนมิถุนายน 2569');
  await page.getByLabel(/มูลค่าก่อนภาษี/).first().fill('30000');
  // Pick the 50ทวิ income type → auto-fills the 5% rate (robust by label, not index).
  await page.getByTestId('pv-line-wht-type').selectOption({ label: 'ค่าเช่า (5%)' });
  await capture('step-02-fill', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: เลือกผู้รับเงิน + หมวดค่าเช่า + รายการ (ค่าเช่า 30,000) + ประเภทเงินได้' +
      ' "ค่าเช่า (5%)". ระบบหัก ณ ที่จ่าย 1,500 และคำนวณ "จ่ายสุทธิ" = 30,000 + VAT − 1,500',
  });

  // ─── Step 3: save → PV detail (Draft) showing WHT ────────────────────
  await page.getByRole('button', { name: 'บันทึก' }).first().click();
  await page.waitForURL(/\/payment-vouchers\/\d+/, { timeout: 15_000 });
  await page.getByTestId('pv-approve').waitFor({ state: 'visible', timeout: 10_000 });
  await capture('step-03-draft', {
    highlight: 'main',
    caption:
      'ขั้นที่ 3: บันทึกแล้ว → ใบสำคัญจ่ายสถานะ "ฉบับร่าง" แสดงยอดหัก ณ ที่จ่าย และ' +
      ' ยอดจ่ายสุทธิ. ต้อง "อนุมัติ" แล้ว "บันทึก (Post)" จึงลงบัญชีและออก 50ทวิ',
  });

  // ─── Step 4: approve → post (immutable; 50ทวิ generated) ─────────────
  await page.getByTestId('pv-approve').click();
  await page.getByTestId('pv-post').waitFor({ state: 'visible', timeout: 15_000 });
  await page.getByTestId('pv-post').click();
  await page.waitForTimeout(2500); // settle post → GL + 50ทวิ generation
  await capture('step-04-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 4: กด "อนุมัติ" แล้ว "บันทึก (Post)" → ระบบลงบัญชี (เดบิตค่าเช่า/ภาษีซื้อ,' +
      ' เครดิตเงินสด/ภาษีหัก ณ ที่จ่ายค้างนำส่ง) และออกหนังสือรับรอง 50ทวิ ให้อัตโนมัติ',
  });

  // ─── Step 5: the generated 50ทวิ certificate ─────────────────────────
  await page.goto('/wht-certificates');
  const certLink = page.locator('td a[href^="/wht-certificates/"]').first();
  await certLink.waitFor({ state: 'visible', timeout: 10_000 });
  await certLink.click();
  await page.waitForURL(/\/wht-certificates\/\d+/, { timeout: 15_000 });
  await capture('step-05-cert', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: หนังสือรับรองการหักภาษี ณ ที่จ่าย (50ทวิ) ที่ระบบออกให้ — ระบุผู้ถูกหัก,' +
      ' ประเภทเงินได้, ยอดหัก 1,500 และแบบนำส่ง ภ.ง.ด.53 (จ่ายให้นิติบุคคล). พิมพ์มอบผู้รับเงินได้',
  });

});

/**
 * 05.06 — ผู้ขายบุคคลธรรมดา (Individual vendor) + หัก ณ ที่จ่าย → 50ทวิ แบบ ภ.ง.ด.3
 *
 * Chapter: 5. งานซื้อ
 * Story: เมื่อจ่ายเงินให้ผู้รับที่เป็น "บุคคลธรรมดา" (เช่น ฟรีแลนซ์) แล้วหักภาษี ณ ที่จ่าย
 *        ระบบออกหนังสือรับรอง 50ทวิ แบบ **ภ.ง.ด.3** อัตโนมัติ — ต่างจากจ่ายให้
 *        "นิติบุคคล" ที่เป็น **ภ.ง.ด.53** (ดู 05.03). แบบนำส่งของ 50ทวิ ผูกกับ
 *        "ประเภทของผู้รับเงิน" (บุคคลธรรมดา → ภ.ง.ด.3, นิติบุคคล → ภ.ง.ด.53).
 *
 * Persona: admin (demo-admin co2). บทนี้ "สร้างผู้ขายบุคคลธรรมดาจริง" (idempotent ด้วย
 * suffix) แล้วจ่าย + หัก ณ ที่จ่าย + โพสต์ เพื่อแสดง 50ทวิ ที่ระบุ ภ.ง.ด.3.
 * Data (co2): ประเภทเงินได้ "ค่าบริการ (3%)" (จ้างฟรีแลนซ์ทำงานบริการ).
 * ⚠️ สร้าง+อนุมัติ+โพสต์ใบสำคัญจ่ายจริงบน company 2 (ออก 50ทวิ จริง + กระทบ P&L).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '05.06',
  title: 'ผู้ขายบุคคลธรรมดา + 50ทวิ แบบ ภ.ง.ด.3',
  chapter: '5. งานซื้อ',
  persona: 'admin',
  intro: `
แบบนำส่งภาษีหัก ณ ที่จ่าย (50ทวิ) ขึ้นกับ **ประเภทของผู้รับเงิน**:

| ผู้รับเงิน | แบบนำส่ง |
|---|---|
| **นิติบุคคล** (บริษัท/ห้างฯ) | **ภ.ง.ด.53** |
| **บุคคลธรรมดา** (ฟรีแลนซ์/บุคคลทั่วไป) | **ภ.ง.ด.3** |

อัตราการหักเหมือนกัน (เช่น ค่าบริการ 3%, ค่าเช่า 5%) — ต่างกันแค่ "แบบที่นำส่งสรรพากร".
ระบบดู **ประเภทของผู้ขาย** (ตั้งที่ข้อมูลหลัก — บุคคลธรรมดา / นิติบุคคล) แล้วเลือกแบบให้เอง.

บทนี้: สร้างผู้ขาย **บุคคลธรรมดา** (ฟรีแลนซ์) → จ่ายค่าบริการ + หัก ณ ที่จ่าย 3% →
ระบบออก 50ทวิ แบบ **ภ.ง.ด.3** ให้อัตโนมัติ (เทียบ 05.03 ที่จ่ายนิติบุคคล = ภ.ง.ด.53).
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ master.vendor.manage + payment_voucher create/approve/post)',
    'มีประเภทเงินได้ 50ทวิ (ค่าบริการ) ในระบบ',
  ],
}, async ({ page, capture }) => {
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);
  const personName = `นายสมศักดิ์ ฟรีแลนซ์ ${suffix}`;

  // ─── Step 1: create vendor — type "บุคคลธรรมดา" ──────────────────────
  await page.goto('/vendors/new');
  await page.getByLabel('รหัสผู้ขาย').waitFor({ state: 'visible' });
  await page.getByLabel('รหัสผู้ขาย').fill(`MV-IND-${suffix}`);
  await page.getByLabel('ประเภท').selectOption('Individual');
  await page.getByLabel('ชื่อ (ไทย)').fill(personName);
  await capture('step-01-vendor-type', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: สร้างผู้ขายใหม่ — ช่อง "ประเภท" เลือก "บุคคลธรรมดา" (ฟรีแลนซ์/บุคคลทั่วไป).' +
      ' ประเภทนี้เป็นตัวกำหนดว่า 50ทวิ จะเป็นแบบ ภ.ง.ด.3 (บุคคล) หรือ ภ.ง.ด.53 (นิติบุคคล)',
  });

  // ─── Step 2: non-VAT individual → tax id not required ────────────────
  // Default toggle is ON; an individual freelancer is non-VAT → switch off.
  await page.getByText('Vendor จดทะเบียน VAT').click();
  await capture('step-02-nonvat', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 2: ปิดสวิตช์ "Vendor จดทะเบียน VAT" (ฟรีแลนซ์บุคคลธรรมดาทั่วไปไม่จด VAT) →' +
      ' ระบบแจ้งว่าเคลมภาษีซื้อไม่ได้ แต่ "WHT ยังหักได้ปกติ". เลขผู้เสียภาษีไม่บังคับ',
  });

  // ─── Step 3: save vendor ─────────────────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกผู้ขาย' }).click();
  await page.waitForURL('http://localhost:3000/vendors', { timeout: 15_000 });
  await capture('step-03-saved', {
    highlight: 'table',
    caption:
      `ขั้นที่ 3: บันทึกแล้ว — ผู้ขายบุคคลธรรมดา "${personName}" เข้า master` +
      ' (ประเภท = บุคคลธรรมดา). พร้อมใช้เป็นผู้รับเงินในใบสำคัญจ่าย',
  });

  // ─── Step 4: payment voucher to the individual + WHT 3% service ──────
  await page.goto('/payment-vouchers/new');
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const vDialog = page.getByRole('dialog');
  await vDialog.getByRole('textbox').fill(`MV-IND-${suffix}`);
  await vDialog.getByRole('button', { name: /สมศักดิ์/ }).first().click();
  // SVC service category — pick by label (robust). The select has a disabled
  // placeholder at index 0, so SVC is DOM index 5; selecting by label avoids the
  // off-by-one that would mis-book this freelance fee under RENT.
  await page.getByTestId('expense-category-select').selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await page.getByLabel('รายละเอียด 1').fill('ค่าจ้างฟรีแลนซ์ออกแบบกราฟิก');
  await page.getByLabel(/มูลค่าก่อนภาษี/).first().fill('20000');
  // service income type → 3% (robust by label, not index).
  await page.getByTestId('pv-line-wht-type').selectOption({ label: 'ค่าบริการ (3%)' });
  await capture('step-04-pv-wht', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 4: ใบสำคัญจ่ายให้ฟรีแลนซ์ — ค่าบริการ 20,000 + ประเภทเงินได้ "ค่าบริการ (3%)".' +
      ' ระบบหัก ณ ที่จ่าย 600 (ผู้ขายไม่จด VAT จึงไม่มีภาษีซื้อ) — จ่ายสุทธิ = 20,000 − 600',
  });

  // ─── Step 5: save → approve → post (generates 50ทวิ) ─────────────────
  await page.getByRole('button', { name: 'บันทึก' }).first().click();
  await page.waitForURL(/\/payment-vouchers\/\d+/, { timeout: 15_000 });
  await page.getByTestId('pv-approve').waitFor({ state: 'visible', timeout: 10_000 });
  await page.getByTestId('pv-approve').click();
  await page.getByTestId('pv-post').waitFor({ state: 'visible', timeout: 15_000 });
  // The approve success toast (sonner) overlays the Post button and intercepts the
  // click ("element is not stable"). Remove any toasts, then click (force skips the
  // stability/occlusion checks the lingering toast animation otherwise trips).
  await page.evaluate(() => document.querySelectorAll('[data-sonner-toast],[data-sonner-toaster]').forEach((n) => n.remove()));
  await page.getByTestId('pv-post').click({ force: true });
  await page.waitForTimeout(2500); // settle post → GL + 50ทวิ generation
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: อนุมัติ + บันทึก (Post) → ระบบลงบัญชีและออกหนังสือรับรอง 50ทวิ ให้.' +
      ' เพราะผู้รับเงินเป็นบุคคลธรรมดา ระบบจะออกแบบ ภ.ง.ด.3 (ไม่ใช่ ภ.ง.ด.53)',
  });

  // ─── Step 6: the generated 50ทวิ — form type = ภ.ง.ด.3 ───────────────
  await page.goto('/wht-certificates');
  const certLink = page.locator('td a[href^="/wht-certificates/"]').first();
  await certLink.waitFor({ state: 'visible', timeout: 10_000 });
  await certLink.click();
  await page.waitForURL(/\/wht-certificates\/\d+/, { timeout: 15_000 });
  await capture('step-06-pnd3-cert', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 6: หนังสือรับรอง 50ทวิ ที่ระบบออกให้ — ช่อง "แบบนำส่ง" = ภ.ง.ด.3' +
      ' (เพราะผู้รับเงินเป็นบุคคลธรรมดา). เทียบกับ 05.03 ที่จ่ายให้นิติบุคคล = ภ.ง.ด.53.' +
      ' แบบนำส่งจึงตามประเภทของผู้รับเงินโดยอัตโนมัติ',
  });

});

/**
 * 05.02 — บันทึกใบกำกับภาษีซื้อ (Vendor Invoice) — ภาษีซื้อ + งวดเครดิต ม.82/4
 *
 * Chapter: 5. งานซื้อ
 * Story: เมื่อรับใบกำกับภาษีจากผู้ขาย → บันทึกเข้าระบบเพื่อขอเครดิต "ภาษีซื้อ".
 *        ต้องระบุเลขที่/วันที่ใบกำกับฯ ของผู้ขาย + งวดที่จะใช้เครดิต (ม.82/4) +
 *        หมวดค่าใช้จ่าย (กำหนดว่าภาษีซื้อเครดิตได้หรือเป็นภาษีซื้อต้องห้าม).
 *
 * Persona: admin (purchase.vendor_invoice.create + .post).
 * Captured against /vendor-invoices/new (PartySelectBox vendor + ExpenseCategorySelector
 * per บรรทัด). Selectors via Thai labels.
 *
 * Data (co2): ผู้ขาย "บริษัท ออฟฟิศ ซัพพลาย จำกัด" (จด VAT → มีภาษีซื้อให้เครดิต).
 * ⚠️ สร้าง+โพสต์เอกสารจริงบน company 2.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '05.02',
  title: 'บันทึกใบกำกับภาษีซื้อ',
  chapter: '5. งานซื้อ',
  persona: 'admin',
  intro: `
ใบกำกับภาษีซื้อ (Vendor Invoice) คือการ **บันทึกใบกำกับภาษีที่ได้รับจากผู้ขาย** เข้าระบบ
เพื่อขอเครดิต **ภาษีซื้อ (Input VAT)** ไปหักกับภาษีขายในแบบ ภ.พ.30.

**สิ่งที่ต้องระบุ:**

- **เลขที่ + วันที่ใบกำกับภาษีของผู้ขาย** — เลขเอกสารต้นฉบับจากผู้ขาย (ไม่ใช่เลขของเรา).
- **งวดเครดิตภาษีซื้อ (ม.82/4)** — ภาษีซื้อใช้เครดิตได้ตั้งแต่เดือนของใบกำกับฯ ถึง +6 เดือน.
- **หมวดค่าใช้จ่าย** — กำหนดว่าภาษีซื้อ "เครดิตได้" หรือเป็น **"ภาษีซื้อต้องห้าม"**
  (เช่น ค่ารับรอง — เครดิตไม่ได้ตามกฎหมาย).

ถ้าเคยออกใบสั่งซื้อให้ผู้ขายรายนี้ (05.01) ระบบมีตัวเลือก "เชื่อมกับใบสั่งซื้อ" เพื่อดึง
รายการมาให้ — ที่นี่แสดงการบันทึกแบบกรอกเอง.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ vendor_invoice create + post)',
    'ได้รับใบกำกับภาษีจากผู้ขายจริง (เลขที่/วันที่)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: blank VI form ───────────────────────────────────────────
  await page.goto('/vendor-invoices/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "บันทึกใบกำกับภาษีซื้อ" — ① ผู้ขาย, ② ข้อมูลเอกสาร (เลขที่/วันที่' +
      ' ใบกำกับฯ ของผู้ขาย + งวดเครดิต ม.82/4), ③ รายการพร้อมหมวดค่าใช้จ่าย',
  });

  // ─── Step 2: vendor + vendor TI no + BU ──────────────────────────────
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const vDialog = page.getByRole('dialog');
  await vDialog.getByRole('textbox').fill('ออฟฟิศ');
  await vDialog.getByRole('button', { name: /ออฟฟิศ/ }).first().click();
  await page.getByLabel(/เลขที่ใบกำกับภาษีของผู้ขาย/).fill('IV-OS-25060123');
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await capture('step-02-vendor', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: เลือกผู้ขาย + กรอก "เลขที่ใบกำกับภาษีของผู้ขาย" (เลขต้นฉบับจากผู้ขาย).' +
      ' "งวดเครดิตภาษีซื้อ (ม.82/4)" ตั้งค่าเริ่มต้นเป็นเดือนของใบกำกับฯ — เลือกได้ถึง +6 เดือน',
  });

  // ─── Step 3: fill a line (category drives recoverable VAT) ───────────
  // Index 3 = OFFICE (categories list code-sorted: ADS, ENT, OFFICE, RENT, SVC) —
  // matches the office-supply line so the screenshot reads consistently.
  await page.getByTestId('expense-category-select').selectOption({ index: 3 });
  await page.getByLabel(/^รายละเอียด/).fill('กระดาษถ่ายเอกสาร A4 80 แกรม (20 รีม)');
  await page.getByLabel(/จำนวนเงิน/).fill('2400');
  await capture('step-03-line', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เลือก "หมวดค่าใช้จ่าย" + กรอกรายละเอียด + จำนวนเงินก่อน VAT 2,400.' +
      ' กล่องสรุปแยก "ภาษีซื้อ (เครดิตได้)" 168 ออกจาก "ภาษีซื้อต้องห้าม" ตามหมวดที่เลือก',
  });

  // ─── Step 4: post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกเอกสาร (Post)' }).click();
  const viConfirm = page.getByRole('dialog');
  await viConfirm.waitFor({ state: 'visible' });
  await capture('step-04-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 4: กด "บันทึกเอกสาร (Post)" → กล่องยืนยันสรุปยอดและภาษีซื้อ. การโพสต์' +
      ' บันทึกภาษีซื้อเข้าระบบ ภ.พ.30 ของงวดที่เลือก',
  });

  // ─── Step 5: posted detail ───────────────────────────────────────────
  await viConfirm.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/vendor-invoices\/\d+/, { timeout: 15_000 });
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 5: บันทึกใบกำกับภาษีซื้อเรียบร้อย — ภาษีซื้อถูกบันทึกเข้าระบบเพื่อใช้เครดิต' +
      ' ในแบบ ภ.พ.30. ขั้นถัดไปคือจ่ายเงินผู้ขายด้วย "ใบสำคัญจ่าย" (05.03)',
  });

});

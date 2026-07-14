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
 *
 * cont. 2026-07-14 (prod UX test F14/F15/F18/F20): เพิ่ม step สาธิต "เชื่อมกับใบสั่งซื้อ"
 * (ปรากฏเมื่อผู้ขายมี PO อนุมัติแล้ว — จาก 05.01) + คำเตือนช่อง "อัตรา VAT" เป็นเศษส่วน
 * (0.07 ไม่ใช่ 7) + ขั้นแนบไฟล์ใบกำกับภาษีเพื่อแก้สถานะ "ไม่สมบูรณ์" หลัง Post.
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

ถ้าเคยออกใบสั่งซื้อให้ผู้ขายรายนี้ (05.01) ระบบมีตัวเลือก "เชื่อมกับใบสั่งซื้อ (ไม่บังคับ)"
เพื่อดึงรายการมาให้ (ดูขั้นที่ 8 ท้ายบทนี้) — ส่วนหลักของบทนี้แสดงการบันทึกแบบกรอกเอง.

> ⚠️ **คำเตือน — ช่อง "อัตรา VAT" รับค่าเป็นเศษส่วน ไม่ใช่เปอร์เซ็นต์**: พิมพ์ "0.07"
> สำหรับ VAT 7% — ถ้าพิมพ์ "7" เฉยๆ ระบบจะตีความเป็น 700% ทันทีโดยไม่มีคำเตือน/ตรวจสอบ
> ค่า (0–1). รายการที่ดึงมาจาก PO ("เชื่อมกับใบสั่งซื้อ") อาจตั้งอัตรานี้มาให้ไม่ตรง —
> ตรวจสอบทุกครั้งก่อนบันทึก.

หลัง Post แล้ว ถ้ายังไม่แนบไฟล์ใบกำกับภาษีจากผู้ขาย เอกสารจะขึ้นสถานะ "ไม่สมบูรณ์"
(หลักฐานตรวจสรรพากร ม.86/4, ม.82/4) — ดูขั้นที่ 7 การแนบไฟล์.
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

  // ─── Step 3b: the "อัตรา VAT" field — a raw fraction, not a percent ─────
  const vatRateField = page.locator('input[step="0.01"]').first();
  const vatRateVal = await vatRateField.inputValue();
  await capture('step-03b-vatrate', {
    highlight: 'input[step="0.01"]',
    arrow: 'up',
    caption:
      `ช่อง "อัตรา VAT" ปัจจุบันแสดง ${vatRateVal} — ช่องนี้รับค่าเป็น` +
      ' "เศษส่วน" (0.07 = 7%) ไม่ใช่เปอร์เซ็นต์ และไม่ตรวจสอบค่าเกินจริง. พิมพ์ "7" เฉยๆ' +
      ' จะกลายเป็น VAT 700% ทันทีโดยไม่มีคำเตือน — ตรวจสอบค่านี้ทุกครั้งก่อนบันทึก',
  });

  // ─── Step 4: post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: 'บันทึกเอกสาร (Post)' }).click();
  const viConfirm = page.getByRole('dialog');
  await viConfirm.waitFor({ state: 'visible' });
  await capture('step-04-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 5: กด "บันทึกเอกสาร (Post)" → กล่องยืนยันเตือนว่า "การบันทึก (Post)' +
      ' ไม่สามารถแก้ไขหรือลบได้ — แก้ไขต้องออกใบลดหนี้ + ออกใหม่ (ม.86/4 / ม.86/12)"' +
      ' พร้อมสรุปยอด VAT/รวม. การโพสต์บันทึกภาษีซื้อเข้าระบบ ภ.พ.30 ของงวดที่เลือก',
  });

  // ─── Step 5: posted detail ───────────────────────────────────────────
  await viConfirm.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/vendor-invoices\/\d+/, { timeout: 15_000 });
  await capture('step-05-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 6: บันทึกใบกำกับภาษีซื้อเรียบร้อย — ภาษีซื้อถูกบันทึกเข้าระบบเพื่อใช้เครดิต' +
      ' ในแบบ ภ.พ.30. เอกสารขึ้นป้าย "ไม่สมบูรณ์ / ขาดไฟล์ใบกำกับภาษีจากผู้ขาย" จนกว่าจะ' +
      ' แนบไฟล์ (ขั้นที่ 7). มุมขวามีปุ่ม "ชำระด้วยใบสำคัญจ่าย" ไปสร้างใบสำคัญจ่าย (05.03)',
  });

  // ─── Step 6: attach the vendor's tax-invoice file → clears "ไม่สมบูรณ์" ──
  await capture('step-06-attach', {
    highlight: '[data-testid="attachments-section"]',
    caption:
      'ขั้นที่ 7: เลื่อนลงมาที่ส่วน "หลักฐาน" ท้ายเอกสาร แล้วกด "อัปโหลด" — แนบไฟล์ใบกำกับภาษี' +
      ' ตัวจริงที่ผู้ขายออกให้ (หลักฐานตรวจสรรพากร ม.86/4, ม.82/4) เพื่อให้เอกสารพ้นสถานะ "ไม่สมบูรณ์"',
  });

  // ─── Step 7: optional "เชื่อมกับใบสั่งซื้อ" — pulls PO lines, VAT rate ────
  // needs a fresh form (page.goto resets all local state cleanly rather than
  // toggling the select back, which does NOT reset already-pulled rows).
  // Only shows up once the vendor has an Approved PO (created by 05.01).
  await page.goto('/vendor-invoices/new');
  await page.getByRole('button', { name: /^เลือกผู้ขาย$|ค้นหาชื่อ หรือรหัสผู้ขาย/ }).first().click();
  const vDialog2 = page.getByRole('dialog');
  await vDialog2.getByRole('textbox').fill('ออฟฟิศ');
  await vDialog2.getByRole('button', { name: /ออฟฟิศ/ }).first().click();
  const poSelect = page.getByTestId('vi-po-select');
  await poSelect.waitFor({ state: 'visible', timeout: 8000 }).catch(() => {});
  if (await poSelect.count()) {
    await poSelect.selectOption({ index: 1 });
    await page.waitForTimeout(500); // PO detail fetch + row prefill
    const pulledRate = await page.locator('input[step="0.01"]').first().inputValue();
    await capture('step-07-po-link', {
      highlight: 'input[step="0.01"]',
      arrow: 'up',
      caption:
        '(ขั้นทางเลือก) เลือก "เชื่อมกับใบสั่งซื้อ (ไม่บังคับ)" → ระบบดึงรายการจาก' +
        ` PO ที่อนุมัติแล้วมาให้อัตโนมัติ (ยังต้องเลือก "หมวดค่าใช้จ่าย" เอง). อัตรา VAT ที่` +
        ` ดึงมาคือ ${pulledRate} — ตรวจสอบค่านี้เสมอก่อนบันทึก (ดูคำเตือนขั้นก่อนหน้า)`,
    });
  }

});

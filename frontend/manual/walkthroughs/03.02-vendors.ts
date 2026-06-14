/**
 * 03.02 — สร้างผู้ขาย (master data)
 *
 * Chapter: 3. ข้อมูลหลัก
 * Story: ผู้ขาย (Vendor) ใช้เป็น "ผู้ขาย/ผู้รับเงิน" ในงานซื้อ (ใบสั่งซื้อ →
 *        ใบกำกับภาษีซื้อ → ใบสำคัญจ่าย + หัก ณ ที่จ่าย).
 *
 * Captured against /vendors + /vendors/new (VendorForm, namespace 'ven').
 * Persona: admin. co2 seed มีผู้ขาย ~45 ราย.
 *
 * หมายเหตุ: กรอกตัวอย่าง 2 ช่อง โชว์ฟอร์ม ไม่กดบันทึก (เหมือน 03.01).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '03.02',
  title: 'สร้างผู้ขาย',
  chapter: '3. ข้อมูลหลัก',
  persona: 'admin',
  intro: `
ผู้ขาย (Vendor) คือ master data ฝั่งซื้อ — ใช้เป็นคู่ค้าในใบสั่งซื้อ,
ใบกำกับภาษีซื้อ, และใบสำคัญจ่าย. การตั้งค่าผู้ขายให้ถูกต้องสำคัญต่อ
**ภาษีซื้อ (Input VAT)** ที่ขอคืนได้ และ **ภาษีหัก ณ ที่จ่าย (WHT)**.

ฟอร์มรองรับเคสพิเศษที่กระทบภาษีอัตโนมัติ:

- **ผู้ขายจดทะเบียน VAT / ไม่จด** — non-VAT (รายได้ < 1.8 ล้าน หรือบุคคลธรรมดา)
  ออกได้แค่ใบเสร็จ, เคลม Input VAT ไม่ได้, แต่ยังหัก WHT ได้ปกติ
- **ผู้ขายต่างประเทศ** — ถ้ายังไม่จด VAT-D ในไทย ระบบจะ default หัก WHT 15% (ม.70)
  + self-assess VAT 7% (ภ.พ.36) ตอนสร้าง PV/VI ให้อัตโนมัติ

กรอกข้อมูลให้ถูกตั้งแต่สร้าง → ระบบคิดภาษีตอนทำเอกสารซื้อให้ถูกเอง.
  `.trim(),
  prerequisites: [
    'login ในฐานะผู้มีสิทธิ์ master.vendor.manage (admin)',
  ],
}, async ({ page, capture }) => {
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);

  // ─── Step 1: vendor list ─────────────────────────────────────────────
  await page.goto('/vendors');
  await capture('step-01-list', {
    highlight: 'table',
    caption:
      'ขั้นที่ 1: หน้า "ผู้ขาย" — รายการผู้ขายทั้งหมด. ปุ่ม "เพิ่มผู้ขาย"' +
      ' มุมขวาบนเปิดฟอร์มสร้างใหม่',
  });

  // ─── Step 2: create form overview ────────────────────────────────────
  await page.goto('/vendors/new');
  await page.getByLabel('รหัสผู้ขาย').waitFor({ state: 'visible' });
  await capture('step-02-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: ฟอร์ม "เพิ่มผู้ขาย" — รหัสผู้ขาย*, ประเภท (นิติบุคคล/บุคคลธรรมดา),' +
      ' ชื่อ*, เลขผู้เสียภาษี, รหัสสาขา, เครดิต, ที่อยู่ + ข้อมูลการชำระเงิน' +
      ' (ธนาคาร/เลขบัญชี). มีสวิตช์ "Vendor ต่างประเทศ" / "จด VAT" สำหรับเคสพิเศษ',
  });

  // ─── Step 3: fill identity fields (no save — demo only) ──────────────
  await page.getByLabel('รหัสผู้ขาย').fill(`MV-${suffix}`);
  await page.getByLabel('ชื่อ (ไทย)').fill(`ห้างหุ้นส่วน ผู้ขายตัวอย่าง ${suffix}`);
  await capture('step-03-fill', {
    highlight: 'main',
    caption:
      `ขั้นที่ 3: กรอกรหัส "MV-${suffix}" + ชื่อไทย. เลือกประเภท/สวิตช์ VAT ให้ตรง` +
      ' กับสถานะจริงของผู้ขาย เพราะมีผลต่อการคิด Input VAT + WHT ตอนทำเอกสารซื้อ.' +
      ' กด "บันทึกผู้ขาย" เพื่อเพิ่มเข้า master',
  });

});

/**
 * 00.01 — ตั้งค่าครั้งแรก: สร้างบริษัทแรกด้วย Setup Wizard (Onboarding)
 *
 * Chapter: 0. ติดตั้งและผู้ดูแลระบบ
 * Story: ผู้ดูแลสูงสุด (Super Admin) ที่ยัง "ไม่มีบริษัทผูกกับบัญชี" เข้าสู่ระบบครั้งแรก →
 *        ระบบนำเข้าหน้า /onboarding ให้สร้างบริษัทแรกก่อนใช้งานหน้าอื่น.
 *
 * กลไก (spec 2026-06-16-onboarding-switcher-nonvat-ch0):
 *   - บัญชี super-admin ที่ไม่มี role assignment → LoginService คืน company_id=0 ใน JWT.
 *   - (dashboard)/layout.tsx เห็น companyId===0 && isSuperAdmin → redirect ไป /onboarding
 *     (route ระดับบนสุด ไม่มี chrome/เมนู).
 *
 * SELF_BOOTSTRAP: walkthrough นี้ login เองในตัว body (persona 'setup-admin' ลงจอด
 *   /onboarding ไม่ใช่ /) — จึงต้องอยู่ใน SELF_BOOTSTRAP_IDS (run-capture จะไม่ login ให้
 *   เพราะตัว driver รอ '/' ซึ่ง user คนนี้ไปไม่ถึง).
 *
 * ⚠️ ไม่กดปุ่ม "สร้างบริษัท" — submit จะสร้างบริษัทจริงทุกครั้งที่ re-capture (ไม่ idempotent).
 *    บันทึกเฉพาะ "ฟอร์มเปล่า" + "ฟอร์มที่กรอกตัวอย่างแล้ว" เท่านั้น.
 *
 * Pre-condition:
 *   - Backend :5080 + Frontend :3000 healthy
 *   - seed 562 applied → user 'setup-admin' / 'Setup@1234', is_super_admin=TRUE, no role
 *     (login → company_id=0)
 */
import { walkthrough } from '../lib/walkthrough';
import { personas } from '../lib/personas';

walkthrough({
  id: '00.01',
  title: 'สร้างบริษัทแรกด้วย Setup Wizard',
  chapter: '0. ติดตั้งและผู้ดูแลระบบ',
  intro: `
หลังติดตั้งระบบและเข้าสู่ระบบด้วยผู้ดูแลสูงสุด (Super Admin) ครั้งแรก — หากบัญชีนั้น
**ยังไม่มีบริษัทผูกอยู่** ระบบจะนำเข้าสู่ **หน้า Setup Wizard** โดยอัตโนมัติ เพื่อ
สร้าง "บริษัทแรก" ก่อนเข้าใช้งานหน้าอื่น ๆ.

ระบบดูจากการที่บัญชียัง **ไม่ถูกมอบหมายให้บริษัทใด** (ไม่ใช่ดูจากการที่ฐานข้อมูลว่าง) —
Super Admin ที่ไม่มี role จะเข้าสู่ระบบในสถานะ "ยังไม่มีบริษัท" และถูกพาเข้าหน้านี้.

ในบทนี้จะเห็น:
- หน้า Wizard เปล่าเมื่อเข้าครั้งแรก
- ตัวอย่างการกรอกข้อมูลตั้งบริษัท: ชื่อ (ไทย/อังกฤษ), เลขผู้เสียภาษี, สถานะ VAT +
  อัตรา + โหมด ภ.พ.30, และที่อยู่จดทะเบียนแบบแยกช่อง (ตามหนังสือรับรอง / ม.86/4)
  `.trim(),
  prerequisites: [
    'มีบัญชี Super Admin ที่ยังไม่มีบริษัทผูกอยู่',
    'รู้ข้อมูลนิติบุคคล: ชื่อ, เลขผู้เสียภาษี 13 หลัก, สถานะ VAT, ที่อยู่จดทะเบียน',
  ],
}, async ({ page, capture }) => {

  // ─── self-bootstrap login: setup-admin lands on /onboarding (company_id=0) ──
  const p = personas['setup-admin'];
  await page.goto('/login');
  await page.getByLabel('ชื่อผู้ใช้').fill(p.username);
  await page.getByLabel('รหัสผ่าน').fill(p.password);
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click();
  // The whole point: a no-company super-admin is routed to the onboarding wizard,
  // NOT the dashboard. Assert that redirect before capturing.
  await page.waitForURL('**/onboarding', { timeout: 30_000 });
  await page.waitForLoadState('networkidle');

  // ─── Step 1: empty wizard — the "no company yet" gate ──────────────────
  await capture('step-01-wizard-empty', {
    highlight: 'form',
    caption:
      'Super Admin ที่ยังไม่มีบริษัทผูกอยู่จะถูกพาเข้าหน้า Setup Wizard นี้โดยอัตโนมัติ —' +
      ' เป็นหน้าแบบ "เต็มจอ" ไม่มีเมนู เพราะต้องสร้างบริษัทแรกให้เสร็จก่อนจึงเข้าใช้งานส่วนอื่นได้.' +
      ' ฟอร์มแบ่งเป็น: ข้อมูลนิติบุคคล, ภาษี (VAT), ที่อยู่จดทะเบียน, และรอบปีบัญชี',
  });

  // ─── fill example values (form NOT submitted — idempotent re-capture) ───
  // Selectors use the RHF field names (input[name=...]) so we never depend on i18n
  // label text. The VAT section renders by default (vatRegistered defaults to true),
  // so vatRate / pnd30 / vatRegisterDate are visible to fill.
  await page.locator('input[name="nameTh"]').fill('บริษัท ตัวอย่างการค้า จำกัด');
  await page.locator('input[name="nameEn"]').fill('Example Trading Co., Ltd.');
  await page.locator('input[name="taxId"]').fill('0105560000000');
  // legalEntityType keeps its default (บริษัทจำกัด).
  await page.locator('input[name="vatRate"]').fill('0.07');
  // pnd30SubmissionMode: 'manual' is the only enabled option (auto is disabled in
  // onboarding until the RD e-filing API is wired) — leave the default 'manual'.
  await page.locator('input[name="vatRegisterDate"]').fill('2024-01-15');
  // Registered address — province + 5-digit postal are required; the street-level
  // parts are structured-but-optional (ม.86/4).
  await page.locator('input[name="addrHouseNo"]').fill('123/45');
  await page.locator('input[name="addrMoo"]').fill('4');
  await page.locator('input[name="addrSoi"]').fill('ลาดพร้าว 10');
  await page.locator('input[name="addrStreet"]').fill('ถนนลาดพร้าว');
  await page.locator('input[name="addrSubDistrict"]').fill('จอมพล');
  await page.locator('input[name="addrDistrict"]').fill('จตุจักร');
  await page.locator('input[name="addrProvince"]').fill('กรุงเทพมหานคร');
  await page.locator('input[name="addrPostalCode"]').fill('10900');
  // Open the optional address group so the granular fields show in the screenshot.
  await page.getByText('ที่อยู่ส่วนเพิ่มเติม', { exact: false }).click().catch(async () => {
    await page.locator('details summary').first().click();
  });
  await page.locator('input[name="addrBuilding"]').fill('อาคารตัวอย่างทาวเวอร์');
  await page.locator('input[name="addrFloor"]').fill('12');

  // ─── Step 2: filled-in wizard — example data, NOT submitted ────────────
  await capture('step-02-wizard-filled', {
    highlight: 'form',
    caption:
      'ตัวอย่างการกรอก: ชื่อนิติบุคคล (ไทย/อังกฤษ), เลขประจำตัวผู้เสียภาษี 13 หลัก,' +
      ' สถานะจด VAT พร้อมอัตรา (เช่น 0.07) และโหมดยื่น ภ.พ.30 —' +
      ' หมายเหตุ: โหมด "อัตโนมัติ (RD API)" ยังถูกปิดไว้ในขั้นตั้งค่า เลือกได้เฉพาะ "ด้วยตนเอง".' +
      ' ที่อยู่จดทะเบียนกรอกแยกช่อง (บ้านเลขที่/หมู่/ซอย/ถนน/ตำบล/อำเภอ/จังหวัด/รหัสไปรษณีย์)' +
      ' ให้ตรงหนังสือรับรอง เพื่อนำไปขึ้นหัวใบกำกับภาษี (ม.86/4).' +
      ' เมื่อกด "สร้างบริษัท" ระบบจะสร้างบริษัท สลับ context เข้าบริษัทนั้น แล้วพาเข้า Dashboard',
  });

});

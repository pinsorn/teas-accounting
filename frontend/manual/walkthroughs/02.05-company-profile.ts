/**
 * 02.05 — Company Profile (hybrid lock — recommended FIRST for new tenant)
 *
 * Chapter: 2. ตั้งค่าระบบ
 * Story: ตั้งค่าข้อมูลบริษัทที่จะปรากฏใน header ของทุกใบกำกับภาษี / ใบเสร็จ /
 *        CN / DN. Hard fields (เลขผู้เสียภาษี, ที่อยู่จดทะเบียน) read-only
 *        ใน Phase 1; soft fields (โลโก้, เบอร์, banking) edit ได้
 *
 * Verified live via Chrome MCP at http://localhost:3000/settings/company
 * (2026-05-19, post-Sprint 13d-P6).
 *
 * Verified:
 *   - GET /api/proxy/company-profile → 200 (any authenticated user can read,
 *     used by document headers)
 *   - PUT /api/proxy/company-profile/soft → 204 (admin only,
 *     master.company.manage scope)
 *   - PUT /api/proxy/company-profile/hard → 501 +
 *     {type:"urn:teas:error:company_profile.hard_locked", detail:"...ภ.พ.09..."}
 *   - 11 hard input fields all have disabled=true AND readOnly=true
 *     (defense in depth) + tooltip "การเปลี่ยนข้อมูลนี้ต้องผ่านขั้นตอน
 *     พิเศษ — ติดต่อผู้ดูแลระบบหรือยื่น ภ.พ.09 ก่อน"
 *   - Banner ⚠️ warning at top of page
 *   - Sidebar link "ข้อมูลบริษัท" first in "ตั้งค่า" group
 *
 * ⚠️ **Recommended order**: do this walkthrough FIRST when setting up a new
 *    tenant. ข้อมูลบริษัทถูก embed ในทุกใบกำกับภาษีที่ออก — ต้องถูกก่อน
 *    เริ่มออกเอกสาร.
 *
 * Pre-condition:
 *   - Logged in as ADMIN (demo-admin) — accountant can READ but not edit soft fields
 *   - manual-demo seed รัน DbInitializer + 410_seed_manual_demo_company_profile.sql
 *     (auto-populates company_id=2 profile)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '02.05',
  title: 'ตั้งค่าข้อมูลบริษัท (Company Profile — ทำก่อนเป็นอันแรก)',
  chapter: '2. ตั้งค่าระบบ',
  intro: `
ข้อมูลบริษัทถูก embed ในทุกเอกสารทางภาษี (Tax Invoice / Receipt / CN / DN
header). ตามกฎหมาย ข้อมูลที่พิมพ์ในเอกสารต้องตรงกับ ภ.พ.20 ที่จดทะเบียน
VAT กับกรมสรรพากร.

**Hybrid lock model** (plan §6.7):

**Hard fields (อ่านอย่างเดียวใน Phase 1):**
- ชื่อนิติบุคคล, เลขผู้เสียภาษี, เลขทะเบียนนิติบุคคล, รหัสสาขา
- ที่อยู่จดทะเบียน (line 1+2, แขวง, เขต, จังหวัด, ไปรษณีย์)
- วันที่จดทะเบียน VAT

→ ต้องแก้ผ่าน ops + ยื่น ภ.พ.09 ก่อน. Phase 2 จะมี 2-person approval +
attachment upload ของ ภ.พ.09.

**Soft fields (admin role แก้ได้):**
- ชื่อทางการค้า (Brand name), โลโก้, เบอร์, อีเมล, เว็บไซต์, ผู้ติดต่อ
- Banking info (สำหรับ payment instructions)

**สำคัญ**: ทำ walkthrough นี้ **ก่อน** walkthrough อื่นใน chapter 2 หาก
เพิ่งตั้ง tenant ใหม่ — เพราะข้อมูลถูก embed ในเอกสารที่ออกหลังจากนั้น.

**Role**:
- Read: ทุก authenticated user (ใช้ render document headers)
- Update soft: ADMIN only (master.company.manage scope)
- Update hard: returns 501 — Phase 2 feature
  `.trim(),
  prerequisites: [
    'login ในฐานะ ADMIN (demo-admin) สำหรับ edit',
    'manual-demo seed (รัน 410_seed_manual_demo_company_profile.sql)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: page tour + warning banner ──────────────────────────────
  await page.goto('/settings/company');
  await capture('step-01-page-tour', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: หน้า "ข้อมูลบริษัท" (sidebar "ตั้งค่า" → ลิงก์แรก).' +
      ' 2 sections: "ข้อมูลทางกฎหมาย" (hard, locked) + "ข้อมูลติดต่อ +' +
      ' การชำระเงิน" (soft, editable). Banner ⚠️ ส้มด้านบนเตือนเรื่อง ภ.พ.09',
  });

  // ─── Step 2: banner explanation ──────────────────────────────────────
  await capture('step-02-banner', {
    highlight: '.alert-warning, [role="alert"]',
    arrow: 'down',
    caption:
      'ขั้นที่ 2: Banner ⚠️ — "การเปลี่ยนข้อมูลทางกฎหมายของบริษัทควรอัปเดต' +
      ' ภ.พ.20 ที่กรมสรรพากรก่อน (ยื่น ภ.พ.09)". ผู้ใช้ต้องไปสรรพากรก่อน' +
      ' จึงจะมาแก้ในระบบ',
  });

  // ─── Step 3: hard fields are locked ──────────────────────────────────
  await capture('step-03-hard-fields-locked', {
    highlight: 'section:has(h2:has-text("ข้อมูลทางกฎหมาย"))',
    arrow: 'right',
    caption:
      'ขั้นที่ 3: Section "ข้อมูลทางกฎหมาย" 🔒 — 11 fields ทั้งหมด disabled+' +
      'readOnly. ตัวอย่าง: ชื่อนิติบุคคล "บริษัท แมนนวล เดโม จำกัด",' +
      ' เลขผู้เสียภาษี 0000000000002, ที่อยู่ "199 อาคารเดโม ชั้น 9 ถนนสุขุมวิท",' +
      ' จังหวัดกรุงเทพ, รหัสไปรษณีย์ 10110, วันที่จดทะเบียน VAT 2020-01-01.' +
      ' ไม่มีปุ่ม Save section นี้ (read-only by design)',
  });

  // ─── Step 4: tooltip on hover ────────────────────────────────────────
  await capture('step-04-tooltip', {
    highlight: 'input[disabled]:first-of-type',
    arrow: 'down',
    caption:
      'ขั้นที่ 4: hover hard field → tooltip: "การเปลี่ยนข้อมูลนี้ต้องผ่าน' +
      'ขั้นตอนพิเศษ — ติดต่อผู้ดูแลระบบหรือยื่น ภ.พ.09 ก่อน". อธิบาย' +
      'workaround ให้ผู้ใช้ทราบทันทีโดยไม่ต้องไปอ่านคู่มือ',
  });

  // ─── Step 5: soft fields section ─────────────────────────────────────
  await capture('step-05-soft-fields', {
    highlight: 'section:has(h2:has-text("ข้อมูลติดต่อ"))',
    arrow: 'down',
    caption:
      'ขั้นที่ 5: Section "ข้อมูลติดต่อ + การชำระเงิน" — fields editable:' +
      ' ชื่อทางการค้า, โลโก้ URL, เบอร์ติดต่อ, อีเมล, เว็บไซต์, ผู้ติดต่อ,' +
      ' Banking (ธนาคาร / เลขที่บัญชี / ชื่อบัญชี). ปุ่ม "บันทึก" สำหรับ' +
      ' section นี้แยกต่างหาก',
  });

  // ─── Step 6: edit + save flow ────────────────────────────────────────
  await page.getByLabel('ชื่อทางการค้า').fill('TEAS Manual Demo');
  await page.getByLabel('เบอร์ติดต่อ').fill('02-555-1234');
  await page.getByLabel('อีเมล').fill('contact@manualdemo.example.com');
  await capture('step-06-fill-soft', {
    highlight: 'section:has(h2:has-text("ข้อมูลติดต่อ"))',
    caption:
      'ขั้นที่ 6: กรอกตัวอย่าง — ชื่อทางการค้า "TEAS Manual Demo",' +
      ' เบอร์ "02-555-1234", อีเมล "contact@manualdemo.example.com"',
  });

  // The page now has TWO "บันทึก" buttons (soft section + paid-up-capital
  // section), so target the soft-save by test-id to avoid a strict violation.
  await Promise.all([
    page.waitForResponse(r =>
      r.url().includes('/api/proxy/company-profile') &&
      r.request().method() === 'PUT' &&
      r.ok()
    ),
    page.getByTestId('cp-soft-save').click(),
  ]);
  // Settle by waiting for the input to reflect the new value
  await page.waitForFunction(
    () => {
      const inp = Array.from(document.querySelectorAll('input')).find(
        (el) => (el as HTMLInputElement).value === 'TEAS Manual Demo',
      );
      return !!inp;
    },
    { timeout: 5000 },
  );
  await capture('step-07-saved', {
    highlight: 'section:has(h2:has-text("ข้อมูลติดต่อ"))',
    caption:
      'ขั้นที่ 7: กด "บันทึก" → PUT /api/proxy/company-profile/soft → 204' +
      ' → toast เขียว → fields ใหม่ persistent. Hard fields ไม่กระทบ' +
      ' (ดู section "ข้อมูลทางกฎหมาย" ด้านบน — ค่าเดิม)',
  });

  // ─── Step 8: logo upload ─────────────────────────────────────────────
  await capture('step-08-logo-upload', {
    highlight: '[data-testid="cp-logo-upload"]',
    arrow: 'up',
    caption:
      'ขั้นที่ 8: "โลโก้บริษัท" — อัปโหลดไฟล์ภาพได้โดยตรง (PNG / JPEG / SVG /' +
      ' WebP, ไม่เกิน 1 MB). ระบบเก็บเป็น attachment แล้ว embed โลโก้นี้ใน' +
      ' หัวกระดาษ PDF ของทุกใบกำกับภาษี / ใบเสร็จ / CN / DN. มีช่อง "URL โลโก้"' +
      ' และตัวอย่าง (preview) ให้ตรวจก่อนบันทึก',
  });

  // ─── Step 9: paid-up capital (CIT SME classification) ────────────────
  await capture('step-09-paid-up-capital', {
    highlight: '[data-testid="cp-paid-up-capital"]',
    arrow: 'up',
    caption:
      'ขั้นที่ 9: "ทุนจดทะเบียนที่ชำระแล้ว" — ใช้จัดประเภท SME สำหรับภาษีเงินได้' +
      ' นิติบุคคล (CIT): ทุน ≤ 5 ล้านบาท และรายได้ ≤ 30 ล้านบาท/ปี ได้อัตรา' +
      ' ลดหย่อนแบบขั้นบันได. ค่านี้อยู่บนข้อมูลหลักบริษัท — แก้ได้โดย super-admin',
  });

});

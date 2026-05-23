/**
 * 02.01 — Business Units (CRUD cycle)
 *
 * Chapter: 2. ตั้งค่าระบบ
 * Story: Admin/Accountant ตั้ง BU + สาธิต CRUD ครบวงจร (Add/Edit/Disable/Restore)
 *
 * Verified live via Chrome MCP at http://localhost:3000/settings/business-units
 * (2026-05-19 Sana session, post-Sprint 13d+13f). Tested as demo-admin AND
 * demo-accountant — both have `master.business_unit.manage` scope so see
 * all CRUD buttons.
 *
 * Verified behaviors:
 *   - POST /api/proxy/business-units {code, nameTh, nameEn} → 200 + toast
 *     "บันทึก" + table refresh
 *   - PUT /api/proxy/business-units/{id} → 204 (full replace, isActive included)
 *   - DELETE /api/proxy/business-units/{id} → 204 (soft delete via isActive=false)
 *   - Sprint 13d-P1: ปิดใช้งาน triggers custom AlertDialog (Thai "⚠️ ยืนยันการทำรายการ"
 *     + red "ยืนยัน" button) — NOT native window.confirm
 *   - Sprint 13d-P4: inactive rows show "↺ เปิดใช้งานใหม่" instead of "ปิดใช้งาน"
 *   - Sprint 13d-P5: blank/invalid POST returns
 *     {type:"urn:teas:error:validation", fieldErrors:[{field,messages}]}
 *   - Duplicate code returns urn:teas:error:bu.duplicate (422)
 *
 * Pre-condition:
 *   - Logged in (demo-admin OR demo-accountant — both have BU scope)
 *   - manual-demo seed: 3 BU rows ECOM/LAB/REPT
 *
 * Acceptance: see "Verified behaviors" above.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '02.01',
  title: 'ตั้งค่าหน่วยธุรกิจ (Business Unit)',
  chapter: '2. ตั้งค่าระบบ',
  intro: `
หน่วยธุรกิจ (BU) ใช้แบ่งกลุ่มภายในบริษัทเดียวกัน — รหัส BU (อักษรพิมพ์ใหญ่
A-Z + ตัวเลข ≤20 ตัว) จะถูกแทรกใน prefix เลขเอกสาร เช่น
\`12-2026-TI-ECOM-0001\`.

ตั้งให้เรียบร้อยก่อนเริ่มออกเอกสาร — เปลี่ยนรหัสภายหลังจะกระทบเอกสารเก่า
ที่อ้างรหัสเดิม.

ในบทนี้คุณจะได้สาธิตทั้ง 4 actions: เพิ่ม → แก้ไข → ปิดใช้งาน → เปิดใช้งานใหม่.
  `.trim(),
  prerequisites: [
    'login ในฐานะ admin หรือ accountant (ทั้งคู่มีสิทธิ์ master.business_unit.manage)',
    'manual-demo seed applied (ECOM/LAB/REPT)',
  ],
}, async ({ page, capture }) => {

  // Random suffix per run — keeps walkthrough idempotent across pilot re-runs
  // (CLAUDE.md §15 test data discipline). BU code limit = ≤20 chars, A-Z+0-9.
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);  // e.g. "1K3A2"
  const code = `T${suffix}`;             // e.g. "T1K3A2"
  const nameTh = `ทดสอบ ${suffix}`;
  const nameEn = `Test BU ${suffix}`;

  // ─── Step 1: list view ───────────────────────────────────────────────
  await page.goto('/settings/business-units');
  await capture('step-01-list', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: หน้า "หน่วยธุรกิจ (Business Unit)" — 3 BUs จาก seed' +
      ' (ECOM อีคอมเมิร์ซ / LAB แล็บ / REPT สัตว์เลื้อยคลาน). คอลัมน์: รหัส,' +
      ' ชื่อ (ไทย), ชื่อ (อังกฤษ), ใช้งาน, [✏️ แก้ไข] [ปิดใช้งาน]',
  });

  // ─── Step 2: enforce-BU toggle ───────────────────────────────────────
  await capture('step-02-enforce-toggle', {
    highlight: 'input[type="checkbox"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 2: toggle "บังคับระบุหน่วยธุรกิจในเอกสารรายได้" — เมื่อเปิด:' +
      ' ออกใบกำกับภาษี/ใบเสร็จ/ใบลดหนี้/ใบเพิ่มหนี้ ต้องเลือก BU ทุกครั้ง' +
      ' (กดยังต่อไม่ได้ถ้าไม่เลือก)',
  });

  // ─── Step 3: Add new BU ──────────────────────────────────────────────
  await page.getByRole('button', { name: 'เพิ่มหน่วยธุรกิจ' }).click();
  await capture('step-03-add-modal', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 3: คลิก "+ เพิ่มหน่วยธุรกิจ" → modal เปิด. Fields: รหัส*' +
      ' (A-Z + 0-9, ≤20), ชื่อ (ไทย)*, ชื่อ (อังกฤษ). ปุ่ม "บันทึก" จะ enable' +
      ' เมื่อกรอก required ครบ',
  });

  await page.getByLabel('รหัส').fill(code);
  await page.getByLabel('ชื่อ (ไทย)').fill(nameTh);
  await page.getByLabel('ชื่อ (อังกฤษ)').fill(nameEn);
  // DOM-assert pattern (Sprint 13g lesson): click + wait for table row to appear.
  // Avoid waitForResponse races on URL/method/ok() filters.
  await page.getByRole('button', { name: 'บันทึก' }).click();
  await page.getByRole('row', { name: new RegExp(code) }).waitFor({ state: 'visible', timeout: 10000 });
  await capture('step-04-saved', {
    highlight: `table tr:has-text("${code}")`,
    caption:
      `ขั้นที่ 4: กด "บันทึก" → POST /api/proxy/business-units → toast เขียว` +
      ` "บันทึก" มุมขวาบน → modal ปิด → row ใหม่ "${code} / ${nameTh} / ${nameEn}"` +
      ` ปรากฏพร้อม ✓ ใช้งาน`,
  });

  // ─── Step 5: Edit existing row ───────────────────────────────────────
  // Edit button is icon-only (lucide Pencil) — no aria-label, no text →
  // fallback to first button in the action column. Tracked as a11y bug
  // (task #69, recommended Backend fix to add aria-label="แก้ไข").
  const editedNameEn = `${nameEn} (Main)`;
  await page.getByRole('row', { name: new RegExp(code) }).locator('button').first().click();
  await capture('step-05-edit', {
    highlight: '[role="dialog"]',
    caption:
      `ขั้นที่ 5: คลิก ✏️ แก้ไข ใน row "${code}" → modal เปิดพร้อมค่าปัจจุบัน.` +
      ` ตัวอย่าง: แก้ "ชื่ออังกฤษ" เป็น "${editedNameEn}" → กด "บันทึก" → PUT 204 →` +
      ` table refresh. หมายเหตุ: ห้ามแก้รหัส (code) หลังออกเอกสารแล้ว`,
  });
  await page.getByLabel('ชื่อ (อังกฤษ)').fill(editedNameEn);
  await page.getByRole('button', { name: 'บันทึก' }).click();
  // DOM-assert: edited cell appears
  await page.getByRole('cell', { name: editedNameEn }).waitFor({ state: 'visible', timeout: 10000 });

  // ─── Step 6: Disable → AlertDialog (custom modal, not native) ────────
  await page.getByRole('row', { name: new RegExp(code) }).getByRole('button', { name: /ปิดใช้งาน/ }).click();
  await capture('step-06-confirm-dialog', {
    highlight: '[role="alertdialog"]',
    caption:
      'ขั้นที่ 6: กด "ปิดใช้งาน" → custom AlertDialog เปิด (Sprint 13d-P1):' +
      ' "⚠️ ยืนยันการทำรายการ — ปิดใช้งานหน่วยธุรกิจนี้? เอกสารเดิมยังอ้างอิงได้".' +
      ' 2 ปุ่ม: "ยกเลิก" (เทา) + "ยืนยัน" (สีแดง — destructive variant)',
  });
  // DOM-assert pattern
  await page.getByRole('button', { name: 'ยืนยัน' }).click();
  await page.getByRole('row', { name: new RegExp(code) })
    .getByRole('button', { name: /เปิดใช้งานใหม่/ })
    .waitFor({ state: 'visible', timeout: 10000 });
  await capture('step-07-disabled', {
    highlight: `table tr:has-text("${code}")`,
    caption:
      `ขั้นที่ 7: ยืนยัน → DELETE 204 (soft) → "${code}" row ใช้งาน column เป็น "—"` +
      ` (inactive). ปุ่ม action เปลี่ยน: "ปิดใช้งาน" → "↺ เปิดใช้งานใหม่"` +
      ` (Sprint 13d-P4)`,
  });

  // ─── Step 8: Restore (Sprint 13d-P4) ─────────────────────────────────
  // DOM-assert pattern
  await page.getByRole('row', { name: new RegExp(code) })
    .getByRole('button', { name: /เปิดใช้งานใหม่/ })
    .click();
  await page.getByRole('row', { name: new RegExp(code) })
    .getByRole('button', { name: /ปิดใช้งาน/ })
    .waitFor({ state: 'visible', timeout: 10000 });
  await capture('step-08-restored', {
    highlight: `table tr:has-text("${code}")`,
    caption:
      `ขั้นที่ 8: คลิก "↺ เปิดใช้งานใหม่" → PUT isActive=true → toast` +
      ` "เปิดใช้งานใหม่" → row "${code}" กลับเป็น ✓ ใช้งาน + ปุ่มกลับเป็น "ปิดใช้งาน"`,
  });

});

/**
 * 02.02 — Products / Services (master data)
 *
 * Chapter: 2. ตั้งค่าระบบ
 * Story: Admin/Accountant ตั้ง master สินค้า/บริการ + กำหนดประเภทให้ถูกเพื่อ
 *        คิด VAT 7% ถูกต้อง
 *
 * Verified live via Chrome MCP at http://localhost:3000/settings/products
 * (2026-05-19, post-Sprint 13d+13f). Tested as demo-admin AND demo-accountant
 * — both have `master.product.manage` scope.
 *
 * Verified:
 *   - manual-demo seeds 10 rows: MP-EXM-001..003 (EXEMPT_GOOD), MP-GD-001..004
 *     (GOOD), MP-SVC-001..003 (SERVICE)
 *   - Modal dropdown "ประเภท" has 4 options (not 3): GOOD, SERVICE,
 *     EXEMPT_GOOD, EXEMPT_SERVICE
 *   - Sprint 13d-P1: ปิดใช้งาน triggers AlertDialog
 *   - Sprint 13d-P4: Restore button "↺ เปิดใช้งานใหม่" for inactive rows
 *   - Sprint 13d-P5: validation returns urn:teas:error:validation envelope
 *   - Duplicate productCode → urn:teas:error:product.duplicate (422,
 *     case-insensitive check confirmed via curl)
 *
 * Pre-condition:
 *   - Logged in (admin OR accountant)
 *   - manual-demo seed applied (10 product rows)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '02.02',
  title: 'ตั้งค่าสินค้า/บริการ',
  chapter: '2. ตั้งค่าระบบ',
  intro: `
สินค้า/บริการคือ master data ที่ใช้ในทุก line item ของเอกสาร
(Quotation, Sales Order, Tax Invoice, Vendor Invoice). การตั้ง
**ประเภท** ให้ถูกต้องสำคัญที่สุด — กระทบการคิด VAT 7% ทันที.

ระบบรองรับ 4 ประเภท:

| ประเภท | VAT 7% | ตัวอย่าง |
|---|---|---|
| GOOD | ✓ ต้องเสีย | ตู้เลี้ยงปลา, เครื่องกรองน้ำ |
| SERVICE | ✓ ต้องเสีย | ค่าที่ปรึกษา, ค่าบริการตรวจ |
| EXEMPT_GOOD | — ยกเว้น | สัตว์มีชีวิต, อาหารสัตว์ |
| EXEMPT_SERVICE | — ยกเว้น | บริการการศึกษา, ค่ารักษาพยาบาล |

ตั้งประเภทผิด → คิด VAT ผิด → ภ.พ.30 ยื่นผิด → โดนเบี้ยปรับ.

รหัส (SKU) จะ lock หลังจากมีการใช้ใน document แล้ว — ตั้งให้ดีตั้งแต่แรก.
"ราคาตั้งต้น" คือ default ที่ pre-fill ตอนเลือกสินค้าในเอกสาร — ผู้ใช้
แก้ราคาในเอกสารได้ตามจริง.
  `.trim(),
  prerequisites: [
    'login (admin หรือ accountant)',
    'manual-demo seed (10 product rows)',
  ],
}, async ({ page, capture }) => {

  // Random suffix per run — keeps walkthrough idempotent across pilot re-runs
  // (CLAUDE.md §15 test data discipline). Product code = MP-SVC-<suffix>.
  const suffix = Date.now().toString(36).toUpperCase().slice(-5);
  const sku = `MP-SVC-${suffix}`;
  const nameTh = `ค่าออกแบบเว็บไซต์ ${suffix}`;
  const unitPrice = '15000';

  // ─── Step 1: list view ───────────────────────────────────────────────
  await page.goto('/settings/products');
  await capture('step-01-list', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: หน้า "สินค้า/บริการ" — 10 รายการจาก seed.' +
      ' คอลัมน์: รหัส (SKU), ชื่อ (ไทย), ประเภท, ราคาตั้งต้น, สถานะ,' +
      ' [✏️ แก้ไข] [ปิดใช้งาน]',
  });

  // ─── Step 2: highlight product types ─────────────────────────────────
  await capture('step-02-types-overview', {
    highlight: 'table',
    arrow: 'down',
    caption:
      'ขั้นที่ 2: สังเกตคอลัมน์ "ประเภท" — แต่ละ row บอก VAT rule:' +
      ' EXEMPT_GOOD (MP-EXM-* ปลา/อาหารสัตว์ = VAT 0%), GOOD (MP-GD-*' +
      ' อุปกรณ์ = VAT 7%), SERVICE (MP-SVC-* บริการ = VAT 7% + อาจ WHT)',
  });

  // ─── Step 3: Add new product ─────────────────────────────────────────
  await page.getByRole('button', { name: 'เพิ่มสินค้า/บริการ' }).click();
  await capture('step-03-add-modal', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 3: คลิก "+ เพิ่มสินค้า/บริการ" → modal เปิด. Fields: รหัส (SKU)*,' +
      ' ชื่อ (ไทย)*, ชื่อ (อังกฤษ), ประเภท (dropdown 4 options), หน่วยนับ,' +
      ' ราคาตั้งต้น',
  });

  await capture('step-04-type-dropdown', {
    highlight: 'select',
    arrow: 'right',
    caption:
      'ขั้นที่ 4: dropdown "ประเภท" มี 4 options: GOOD (default), SERVICE,' +
      ' EXEMPT_GOOD, EXEMPT_SERVICE. ตัวเลือกที่ขึ้นต้น "EXEMPT_" = ไม่คิด VAT 7%',
  });

  // Fill + save (real save — test DB writable per user instruction; sku random per run)
  await page.getByLabel('รหัส (SKU)').fill(sku);
  await page.getByLabel('ชื่อ (ไทย)').fill(nameTh);
  await page.getByLabel('ประเภท').selectOption('SERVICE');
  await page.getByLabel('หน่วยนับ').fill('งาน');
  await page.getByLabel('ราคาตั้งต้น').fill(unitPrice);
  // DOM-assert pattern (Sprint 13g lesson): click + wait for row to appear
  await page.getByRole('button', { name: 'บันทึก' }).click();
  await page.getByRole('row', { name: new RegExp(sku) }).waitFor({ state: 'visible', timeout: 10000 });
  await capture('step-05-saved', {
    highlight: `table tr:has-text("${sku}")`,
    caption:
      `ขั้นที่ 5: กด "บันทึก" → POST → toast "บันทึก" → row ใหม่ "${sku}` +
      ` / ${nameTh} / SERVICE / 15,000.00 / ใช้งาน" ปรากฏ. ระบบจะคิด` +
      ` VAT 7% และเตือนหัก ณ ที่จ่ายอัตโนมัติเมื่อใช้ในเอกสาร`,
  });

  // ─── Step 6: Disable + AlertDialog ───────────────────────────────────
  await page.getByRole('row', { name: new RegExp(sku) }).getByRole('button', { name: /ปิดใช้งาน/ }).click();
  await capture('step-06-confirm-dialog', {
    highlight: '[role="alertdialog"]',
    caption:
      'ขั้นที่ 6: กด "ปิดใช้งาน" → AlertDialog เปิด (เหมือน BU 02.01).' +
      ' ถ้าเอกสารเดิมอ้างสินค้านี้ — ยังอ้างได้ (soft delete + audit trail)',
  });
  // DOM-assert pattern (recommended in Sprint 13g v0.4 lessons): drop the
  // waitForResponse race entirely, rely on UI settle. Framework's per-walkthrough
  // try/catch surfaces errors anyway if the mutation actually fails.
  await page.getByRole('button', { name: 'ยืนยัน' }).click();
  await page.getByRole('row', { name: new RegExp(sku) })
    .getByRole('button', { name: /เปิดใช้งานใหม่/ })
    .waitFor({ state: 'visible', timeout: 10000 });
  await capture('step-07-disabled', {
    highlight: `table tr:has-text("${sku}")`,
    caption:
      `ขั้นที่ 7: row "${sku}" สถานะเปลี่ยนเป็น "—" + action เป็น "↺ เปิดใช้งานใหม่".` +
      ` Restore = PUT isActive=true (Sprint 13d-P4)`,
  });

});

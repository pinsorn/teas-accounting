/**
 * 02.03 — WHT Types (admin-only)
 *
 * Chapter: 2. ตั้งค่าระบบ
 * Story: Admin ตั้งประเภทหัก ณ ที่จ่าย + อัตรา + แบบยื่น + ม.40 income type
 *
 * Verified live via Chrome MCP at http://localhost:3000/settings/wht-types
 * (2026-05-19, post-Sprint 13f cross-tenant fix).
 *
 * Verified:
 *   - manual-demo tenant has 3 WHT types: ADS (2% PND53 ม.40(4)),
 *     RENT (5% PND3 ม.40(5)), SVC (3% PND53 ม.40(3))
 *   - Sprint 13f-P1 fixed cross-tenant leak: admin GET /wht-types returns
 *     3 rows (was 18 with company-1 rows leaked in). Service now has
 *     explicit CompanyId filter — defense-in-depth (CLAUDE.md §4.7)
 *   - Sprint 13f-P2 added POST /wht-types/{id}/reactivate (Option A
 *     dedicated endpoint, no DTO conflation)
 *   - Sprint 13d-P3 PermissionGate: "+ เพิ่มประเภท" / edit / disable /
 *     เปลี่ยนอัตรา / restore buttons all hidden for non-admin
 *
 * ⚠️ Role requirement: this page works for any authenticated user (READ),
 *    but ALL write actions require `tax.wht_type.manage` scope = ADMIN role.
 *    demo-accountant sees the list but no action buttons.
 *
 * ⚠️ Known UX gap (task #58, defer to next housekeeping):
 *    WHT disable does NOT show AlertDialog confirm (unlike BU/Product
 *    in Sprint 13d-P1 which covered 7 callers; WHT was not on that list).
 *    Click "ปิดใช้งาน" → immediate disable (no confirm). Recommend caution.
 *
 * Pre-condition:
 *   - Logged in as ADMIN (demo-admin) — accountant has read-only access
 *   - manual-demo seed (3 WHT rows)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '02.03',
  title: 'ตั้งค่าประเภทหัก ณ ที่จ่าย (Admin only)',
  chapter: '2. ตั้งค่าระบบ',
  intro: `
ระบบหัก ณ ที่จ่าย (WHT) ในไทยต้องระบุ 3 อย่างพร้อมกัน:

1. **ประเภทเงินได้ (มาตรา 40)** — บอกประเภทของรายได้
   (ม.40(2) ค่าจ้าง, ม.40(3) ค่าลิขสิทธิ์, ม.40(4) ดอกเบี้ย/ปันผล,
   ม.40(5) ค่าเช่า, ม.40(6) วิชาชีพอิสระ, ม.40(7) รับเหมา, ม.40(8) อื่น ๆ)
2. **อัตรา (%)** — เปอร์เซ็นต์หัก ณ ที่จ่าย (เช่น 3%, 5%, 15%)
3. **แบบยื่น** — ภ.ง.ด.1 (เงินเดือน) / ภ.ง.ด.3 (จ่ายให้บุคคล) /
   ภ.ง.ด.53 (จ่ายให้นิติบุคคล) / ภ.ง.ด.54 (จ่ายต่างประเทศ)

**Effective-date pattern**: อัตรา WHT เปลี่ยนตามกฎหมายเป็นบางช่วง — ระบบ
เก็บประวัติอัตราผ่านปุ่ม "เปลี่ยนอัตรา" (ดู step 4). ระบบจะใช้อัตรา
ที่ถูกต้องตามวันที่เอกสารจริง ไม่ใช่อัตราล่าสุด — สอดคล้องกับเอกสารเก่า
ที่ออกก่อนเปลี่ยนกฎหมาย.

**ต้องเป็น admin role** (\`tax.wht_type.manage\` scope) เพื่อจะ CRUD —
accountant อ่านได้แต่ไม่เห็นปุ่ม action.
  `.trim(),
  prerequisites: [
    'login ในฐานะ ADMIN (demo-admin) — ไม่ใช่ accountant',
    'manual-demo seed (3 WHT rows: ADS/RENT/SVC)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: list view (clean, 3 rows after Sprint 13f fix) ──────────
  await page.goto('/settings/wht-types');
  await capture('step-01-list', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: หน้า "ประเภทหัก ณ ที่จ่าย" — 3 ประเภทจาก seed สำหรับ tenant นี้:' +
      ' ADS (ค่าโฆษณา) 2% PND53, RENT (ค่าเช่า) 5% PND3, SVC (ค่าบริการ) 3% PND53.' +
      ' หมายเหตุ: Sprint 13f แก้ cross-tenant leak — ก่อนหน้านี้ admin เห็น 18 rows' +
      ' รวมข้อมูล tenant อื่น',
  });

  // ─── Step 2: row anatomy ─────────────────────────────────────────────
  await capture('step-02-row-columns', {
    highlight: 'tr:has(td:has-text("RENT"))',
    arrow: 'down',
    caption:
      'ขั้นที่ 2: แต่ละ row บอก รหัส, ชื่อ, อัตรา, แบบยื่น (PND3/PND53),' +
      ' ประเภทเงินได้ (ม.40), ช่วงมีผล (มีผลตั้งแต่ — ถึง), สถานะ,' +
      ' actions: [✏️ แก้ไข] [% เปลี่ยนอัตรา] [ปิดใช้งาน]',
  });

  // ─── Step 3: effective-date column ───────────────────────────────────
  await capture('step-03-effective-dates', {
    highlight: 'th:has-text("มีผลตั้งแต่"), th:has-text("ถึง")',
    arrow: 'down',
    caption:
      'ขั้นที่ 3: คอลัมน์ "มีผลตั้งแต่ / ถึง" — แสดงช่วงเวลาที่อัตรานี้ใช้.' +
      ' "ปัจจุบัน" = row ที่ใช้อยู่ตอนนี้. row เก่ามี "ถึง" เป็นวันที่ — ใช้กับ' +
      ' เอกสารที่ออกก่อนวันนั้น (เก็บประวัติเพื่อ render เอกสารเก่าถูกต้อง)',
  });

  // ─── Step 4: change rate button ──────────────────────────────────────
  await capture('step-04-change-rate-btn', {
    highlight: 'button:has-text("เปลี่ยนอัตรา")',
    arrow: 'right',
    caption:
      'ขั้นที่ 4: ปุ่ม "% เปลี่ยนอัตรา" — ใช้เมื่อกฎหมายเปลี่ยนอัตรา' +
      ' (เช่น สรรพากรลด WHT 3% → 1.5%). ระบบจะสร้าง row ใหม่ + set "ถึง"' +
      ' ของ row เก่าเป็นวันก่อนหน้า → เอกสารเก่าใช้อัตราเดิม, เอกสารใหม่' +
      ' ใช้อัตราใหม่ (effective-date pattern, plan §16.4)',
  });

  // ─── Step 5: Add new WHT type ────────────────────────────────────────
  await page.getByRole('button', { name: 'เพิ่มประเภท' }).click();
  await capture('step-05-add-modal', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 5: คลิก "+ เพิ่มประเภท" → modal เปิด. Fields: รหัส*, ชื่อ (ไทย)*,' +
      ' ชื่อ (อังกฤษ), ประเภทเงินได้ (ม.40)*, แบบยื่น (dropdown), อัตรา (%)*',
  });

  await capture('step-06-form-dropdown', {
    highlight: 'select',
    arrow: 'right',
    caption:
      'ขั้นที่ 6: dropdown "แบบยื่น" — ภ.ง.ด.1 (เงินเดือน), ภ.ง.ด.3' +
      ' (บุคคลธรรมดา), ภ.ง.ด.53 (นิติบุคคล — ใช้บ่อยสุด)',
  });

  // Cancel — keep seed clean (this is a manual demo session)
  await page.getByRole('button', { name: 'ยกเลิก' }).click();

  // ─── Step 7: Disable (⚠️ no AlertDialog yet — see task #58) ──────────
  await page.getByRole('row', { name: /SVC/ }).getByRole('button', { name: /ปิดใช้งาน/ }).click();
  await capture('step-07-disabled-no-confirm', {
    highlight: 'table tr:has-text("SVC")',
    caption:
      'ขั้นที่ 7: ⚠️ คลิก "ปิดใช้งาน" SVC row → disable ทันที (no confirm).' +
      ' Sprint 13d-P1 ได้ migrate 7 callers จาก window.confirm → AlertDialog' +
      ' แต่ WHT-types ยังไม่ในชุดนั้น (task #58). ระวังการคลิกผิด',
  });

  // ─── Step 8: Restore (Sprint 13f-P2) ─────────────────────────────────
  await page.getByRole('row', { name: /SVC/ }).getByRole('button', { name: /เปิดใช้งานใหม่/ }).click();
  await capture('step-08-restored', {
    highlight: 'table tr:has-text("SVC")',
    caption:
      'ขั้นที่ 8: SVC row inactive แล้ว action เปลี่ยนเป็น "↺ เปิดใช้งานใหม่".' +
      ' คลิก → POST /wht-types/{id}/reactivate → 204 → toast "เปิดใช้งานใหม่"' +
      ' → row กลับมา active (Sprint 13f-P2 Option A: dedicated endpoint)',
  });

});

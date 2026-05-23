/**
 * 01.03 — Language toggle (TH / EN)
 *
 * Chapter: 1. เริ่มต้นใช้งาน
 * Story: User สลับภาษา UI ระหว่าง ไทย ↔ English จากปุ่มมุมล่างซ้าย sidebar
 *
 * Verified live via Chrome MCP at http://localhost:3000/ (2026-05-19 Sana session).
 * Confirmed: คลิก "TH / EN" → toast notification "English" / "ภาษาไทย" ขึ้นมุมขวาบน,
 * sidebar + main content แปลทั้งหมด (Dashboard, Quotations, Tax Invoices, ...).
 * Note: ศัพท์เฉพาะภาษีไทย เช่น "WHT Certificates (50 ทวิ)" และ "ภ.พ.30 VAT Return"
 * คงรูปแบบ hybrid ไว้ — เป็นการตั้งใจให้ตรงกับมาตรฐานกรมสรรพากร.
 *
 * Pre-condition:
 *   - Logged in as demo-accountant
 *   - ปัจจุบันอยู่ที่ภาษาไทย (default)
 *
 * Acceptance:
 *   - Toast notification ปรากฏมุมขวาบนหลังคลิก
 *   - Sidebar labels เปลี่ยน (เช่น "ใบเสนอราคา" → "Quotations")
 *   - Heading หน้า dashboard เปลี่ยน ("แดชบอร์ด" → "Dashboard")
 *   - Stat card labels เปลี่ยน ("ใบกำกับภาษีเดือนนี้" → "Tax Invoices this month")
 *   - คลิกอีกครั้ง → กลับเป็นไทย
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '01.03',
  title: 'เปลี่ยนภาษา TH / EN',
  chapter: '1. เริ่มต้นใช้งาน',
  intro: `
TEAS รองรับ 2 ภาษาในหน้าเดียวกัน — ไทย (default) และ English. การสลับ
จะกระทบทั้ง sidebar, headings, labels, และ stat cards ทันที (client-side
locale switch — ไม่ต้อง reload หน้า).

ศัพท์เฉพาะกฎหมายภาษีไทย เช่น "ภ.พ.30", "50 ทวิ", "ภ.ง.ด." จะคงรูปไว้
แม้สลับเป็น English เพราะเป็นชื่อทางการตามมาตรฐานกรมสรรพากร
(เช่น "ภ.พ.30 VAT Return", "WHT Certificates (50 ทวิ)").

User preference จะถูกเก็บไว้ใน cookie ของ browser — ครั้งต่อไปที่ login
ระบบจะจำภาษาที่เลือกล่าสุด.
  `.trim(),
  prerequisites: [
    'login แล้ว (walkthrough 01.01)',
    'อยู่ที่ภาษาไทย (default)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: locate language toggle ──────────────────────────────────
  await page.goto('/');
  await capture('step-01-locate-toggle', {
    highlight: 'button:has-text("TH / EN")',
    arrow: 'right',
    caption:
      'ขั้นที่ 1: ปุ่ม "TH / EN" อยู่ล่างสุดของ sidebar (เหนือปุ่ม "ออกจากระบบ")' +
      ' — เป็น toggle 1-คลิกสลับภาษา',
  });

  // ─── Step 2: click to switch to English ──────────────────────────────
  await page.getByRole('button', { name: 'TH / EN' }).click();
  // Wait for toast + re-render
  await page.waitForTimeout(300);
  await capture('step-02-switched-english', {
    // Highlight sidebar after translation
    highlight: 'nav',
    caption:
      'ขั้นที่ 2: หลังคลิก → toast "English" ปรากฏมุมขวาบน. Sidebar แปลทั้งหมด:' +
      ' "ใบเสนอราคา" → "Quotations", "ใบกำกับภาษี" → "Tax Invoices",' +
      ' "งบทดลอง" → "Trial Balance" ฯลฯ',
  });

  // ─── Step 3: confirm main content also translated ───────────────────
  await capture('step-03-main-translated', {
    highlight: 'main',
    caption:
      'ขั้นที่ 3: หน้า dashboard แปลด้วย — "แดชบอร์ด" → "Dashboard",' +
      ' "ภาพรวมระบบ" → "System overview", stat cards: "Tax Invoices this month",' +
      ' "Sales this month", "Output VAT this month", "Number gaps"',
  });

  // ─── Step 4: switch back to Thai ─────────────────────────────────────
  await page.getByRole('button', { name: 'TH / EN' }).click();
  await page.waitForTimeout(300);
  await capture('step-04-switched-thai', {
    highlight: 'nav',
    caption:
      'ขั้นที่ 4: คลิกอีกครั้ง → กลับเป็นภาษาไทย. Toast "ภาษาไทย" ปรากฏ.' +
      ' ค่าที่เลือกล่าสุดจะถูก save ลง cookie อัตโนมัติ',
  });

});

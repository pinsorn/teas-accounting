/**
 * 01.01 — Login
 *
 * Chapter: 1. เริ่มต้นใช้งาน
 * Story: User เปิด TEAS ครั้งแรก → login ด้วย username + password → เห็น dashboard
 *
 * Verified live via Chrome MCP at http://localhost:3000/login (2026-05-19 Sana session).
 * Selectors confirmed against running app: 2 textbox + submit button, Thai labels.
 *
 * Pre-condition:
 *   - Backend :5080 + Frontend :3000 healthy
 *   - manual-demo seed applied (company_id=2)
 *   - User `demo-accountant` exists with password `Demo@1234` and roles
 *     [ACCOUNTANT, AP_CLERK, AR_CLERK]
 *
 * Acceptance:
 *   - After submit, URL navigates from /login to / (dashboard root)
 *   - Page title becomes "TEAS — ระบบบัญชี Enterprise"
 *   - Sidebar visible with "แดชบอร์ด" highlighted
 *   - 4 stat cards render (ใบกำกับภาษีเดือนนี้ / ยอดขายเดือนนี้ / ภาษีขายเดือนนี้ / เลขเอกสารขาดช่วง)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '01.01',
  title: 'เข้าสู่ระบบ',
  chapter: '1. เริ่มต้นใช้งาน',
  intro: `
TEAS ใช้รหัสผ่านปกติร่วมกับ MFA (ถ้าเปิดใช้) ผ่านหน้า login เพียงหน้าเดียว.
ระบบจะออก JWT เก็บเป็น httpOnly cookie — ไม่เก็บใน localStorage ของ browser
เพื่อความปลอดภัย (BFF cookie pattern).

ในบทนี้คุณจะได้เรียนรู้:
- หน้าตา login screen
- การกรอก username + password
- หน้า dashboard ที่ปรากฏหลัง login สำเร็จ
  `.trim(),
  prerequisites: [
    'มี user account ที่ผู้ดูแลระบบสร้างให้แล้ว',
    'รู้ username + password ของตัวเอง',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: navigate to login ───────────────────────────────────────
  await page.goto('/login');
  await capture('step-01-login-page', {
    // Highlight the entire login card to orient the user
    highlight: 'form',
    caption:
      'ขั้นที่ 1: เปิด browser ไปที่ URL ของระบบ — หน้าเข้าสู่ระบบจะแสดงพร้อม' +
      ' ช่อง "ชื่อผู้ใช้" และ "รหัสผ่าน"',
  });

  // ─── Step 2: enter username ──────────────────────────────────────────
  await capture('step-02-enter-username', {
    highlight: 'input[type="text"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 2: กรอก "ชื่อผู้ใช้" ที่ผู้ดูแลระบบให้มา (ตัวอย่างคู่มือนี้ใช้' +
      ' demo-accountant)',
  });
  await page.getByLabel('ชื่อผู้ใช้').fill('demo-accountant');

  // ─── Step 3: enter password ──────────────────────────────────────────
  await capture('step-03-enter-password', {
    highlight: 'input[type="password"]',
    arrow: 'right',
    caption:
      'ขั้นที่ 3: กรอก "รหัสผ่าน" — ตัวอักษรจะถูกซ่อนเป็นจุด.' +
      ' หากตั้ง MFA ไว้ ระบบจะถามรหัส OTP ในขั้นถัดไป',
  });
  await page.getByLabel('รหัสผ่าน').fill('Demo@1234');

  // ─── Step 4: submit ──────────────────────────────────────────────────
  await capture('step-04-submit', {
    highlight: 'button[type="submit"]',
    arrow: 'down',
    caption:
      'ขั้นที่ 4: คลิกปุ่ม "เข้าสู่ระบบ" — ระบบจะตรวจสอบ credentials' +
      ' และเปิด session ผ่าน httpOnly cookie',
  });
  await page.getByRole('button', { name: 'เข้าสู่ระบบ' }).click();

  // ─── Step 5: dashboard loads ─────────────────────────────────────────
  // Wait for navigation to dashboard root
  await page.waitForURL('http://localhost:3000/');
  await capture('step-05-dashboard', {
    // Highlight the welcome header to anchor the user
    highlight: 'h1, h2',
    caption:
      'ขั้นที่ 5: เข้าสู่ระบบสำเร็จ — หน้า "แดชบอร์ด" ปรากฏพร้อมภาพรวมระบบ:' +
      ' ยอดขายเดือนนี้, ภาษีขาย, ใบกำกับภาษี, เลขเอกสารขาดช่วง.' +
      ' เมนูทางซ้ายแสดงโมดูลหลัก: ขาย, ซื้อ, รายงาน',
  });

});

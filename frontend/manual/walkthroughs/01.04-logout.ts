/**
 * 01.04 — Logout
 *
 * Chapter: 1. เริ่มต้นใช้งาน
 * Story: User คลิก "ออกจากระบบ" → BFF เรียก /api/auth/logout (POST) →
 *        httpOnly cookie ถูก clear → redirect กลับ /login
 *
 * Verified live via Chrome MCP at http://localhost:3000/ (2026-05-19 Sana session).
 * Confirmed:
 *   - POST /api/auth/logout → 200 OK
 *   - URL จาก / → /login
 *   - หน้า login form ปรากฏใหม่ (TEAS — เข้าสู่ระบบ)
 *
 * Note: เดิม chapter 1 มี walkthrough 01.04 (Dark mode) ด้วย แต่ฟีเจอร์
 * dark mode ยังไม่ implement ในระบบปัจจุบัน — Logout จึงย้ายจาก 01.05 → 01.04.
 * Dark mode จะอยู่ใน backlog Phase 2.
 *
 * Pre-condition:
 *   - Logged in as demo-accountant
 *   - อยู่ที่หน้าใดก็ได้หลัง login
 *
 * Acceptance:
 *   - หลังคลิก: URL = /login
 *   - access_token cookie ถูก clear (httpOnly cookie ไม่อยู่)
 *   - หน้า login form ปรากฏ (TEAS — เข้าสู่ระบบ + 2 inputs + ปุ่ม "เข้าสู่ระบบ")
 *   - กดปุ่ม Back ของ browser → ระบบยังต้อง redirect กลับ /login (middleware gate)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '01.04',
  title: 'ออกจากระบบ',
  chapter: '1. เริ่มต้นใช้งาน',
  intro: `
การ "ออกจากระบบ" (logout) จะส่ง POST ไปที่ /api/auth/logout — BFF จะ
ลบ httpOnly cookie ที่เก็บ access_token แล้ว redirect กลับ /login.

ความปลอดภัย: เนื่องจาก JWT เก็บเป็น httpOnly cookie (BFF pattern) —
JavaScript ของหน้าเว็บอ่านไม่ได้อยู่แล้ว, การ logout ฝั่ง browser จึง
เพียงพอแม้ยังไม่ revoke token ฝั่ง server (token จะ expire ตามอายุปกติ).

หากต้องการ revoke ทันที (เช่นกรณี mobile device หาย) — admin สามารถ
กดบังคับ logout จาก User Management (ดูบทที่ 6 → "ผู้ใช้งาน" → revoke session).

หมายเหตุ: chapter index เดิมระบุ walkthrough "Dark mode" ที่ 01.04 — แต่
ฟีเจอร์ยังไม่ implement, logout จึงเลื่อนมาเป็น 01.04.
  `.trim(),
  prerequisites: [
    'login แล้ว (walkthrough 01.01)',
    'อยู่ที่หน้าใดก็ได้หลัง login (ส่วนใหญ่ใช้จาก dashboard)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: locate logout button ────────────────────────────────────
  await page.goto('/');
  await capture('step-01-locate-logout', {
    highlight: 'button:has-text("ออกจากระบบ")',
    arrow: 'right',
    caption:
      'ขั้นที่ 1: ปุ่ม "ออกจากระบบ" อยู่ล่างสุดของ sidebar (ใต้ปุ่ม "TH / EN")' +
      ' — สีข้อความเป็นสีแดง บอกว่าเป็น destructive action',
  });

  // ─── Step 2: click logout ────────────────────────────────────────────
  await page.getByRole('button', { name: 'ออกจากระบบ' }).click();
  // Wait for navigation back to /login
  await page.waitForURL('http://localhost:3000/login');
  await capture('step-02-redirected-login', {
    highlight: 'form',
    caption:
      'ขั้นที่ 2: หลังคลิก → ระบบจะส่ง POST /api/auth/logout → cookie ถูก clear' +
      ' → redirect กลับหน้า /login. ตอนนี้ session สิ้นสุดแล้ว',
  });

  // ─── Step 3: verify cookie cleared (Back button test) ────────────────
  // Press browser back — middleware should redirect back to /login since
  // access_token cookie is gone.
  await page.goBack();
  await page.waitForURL('http://localhost:3000/login');
  await capture('step-03-back-button-blocked', {
    highlight: 'form',
    caption:
      'ขั้นที่ 3: ลองกดปุ่ม "Back" ของ browser → ระบบจะ redirect กลับ /login' +
      ' เสมอ (middleware ตรวจ cookie ทุก request). พิสูจน์ว่า session ถูก clear จริง',
  });

});

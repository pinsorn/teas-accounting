/**
 * 08.01 — งบกำไรขาดทุน (P&L) + สรุปยอดขาย (Sales Summary)
 *
 * Chapter: 8. รายงาน
 * Story: ทุกเอกสารที่ "บันทึกบัญชี" (post) ลง GL อัตโนมัติ → ดึงเป็นรายงานได้ทันที.
 *        **งบกำไรขาดทุน** = รายได้ − รายจ่าย = กำไรสุทธิ (แยกตามหน่วยธุรกิจได้);
 *        **สรุปยอดขาย** = ยอดขายจัดกลุ่มตามลูกค้า/สินค้า/หน่วยธุรกิจ.
 *
 * Persona: admin (report read). Captured against /reports/* (co2). READ-ONLY.
 * NOTE: P&L กรองหน่วยธุรกิจ ECOM เพื่อโชว์ฟีเจอร์ "แยกตามหน่วยธุรกิจ" ด้วยข้อมูลชุดสะอาด
 * (เลือก "ทุกหน่วยธุรกิจ" จะเห็นยอดรวม). งบทดลองยังไม่ลงคู่มือ — ดู progress.md (ผังบัญชี co2
 * มีรหัสซ้ำจากการ seed ทดสอบ ทำให้ตารางดูซ้ำ; รอ demo data สะอาด).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '08.01',
  title: 'งบกำไรขาดทุน (P&L) + สรุปยอดขาย',
  chapter: '8. รายงาน',
  persona: 'admin',
  intro: `
ระบบ **ลงบัญชีแยกประเภท (GL) ให้อัตโนมัติ** ทุกครั้งที่บันทึกบัญชีเอกสาร → รายงานพร้อมใช้ทันที
ไม่ต้องคีย์ซ้ำ:

- **งบกำไรขาดทุน (P&L)** — **รายได้ − รายจ่าย = กำไรสุทธิ** ในช่วงวันที่ที่เลือก
  แยกตาม **หน่วยธุรกิจ** ได้ (เทียบผลแต่ละแผนก/สาขา).
- **สรุปยอดขาย (Sales Summary)** — ยอดขายในช่วงเวลา **จัดกลุ่มได้ตาม ลูกค้า / สินค้า /
  หน่วยธุรกิจ** พร้อมจำนวนเอกสาร · ยอดก่อน VAT · VAT · ยอดรวม.

ทุกรายงาน **ดูอย่างเดียว** — เลือกช่วงวันที่/ตัวกรองแล้วแสดงผลจาก GL จริงทันที.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ดูรายงาน)',
    'มีเอกสารที่บันทึกบัญชีแล้ว (ดู บท 4–6)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: Profit & Loss (filtered to ECOM for a clean BU demo) ─────
  await page.goto('/reports/profit-loss');
  await page.locator('input[type=date]').nth(0).fill('2026-06-01');
  await page.locator('input[type=date]').nth(1).fill('2026-06-30');
  const ecomVal = await page.locator('select option')
    .filter({ hasText: /ECOM/ }).first().getAttribute('value');
  await page.locator('select').selectOption(ecomVal ?? '');
  await page.locator('tfoot').waitFor({ state: 'visible', timeout: 15_000 });
  await capture('step-01-profit-loss', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 1: งบกำไรขาดทุน — เลือกช่วงวันที่ + หน่วยธุรกิจ (ตัวอย่างกรอง ECOM) → รายได้/รายจ่าย/' +
      'กำไรสุทธิ. เลือก "ทุกหน่วยธุรกิจ" เพื่อดูรวมและเทียบทุกหน่วย. ใช้ดูผลประกอบการรายเดือน/ไตรมาส/ปี',
  });

  // ─── Step 2: Sales Summary (grouped by customer) ─────────────────────
  await page.goto('/reports/sales-summary');
  await page.locator('input[type=date]').nth(0).fill('2026-06-01');
  await page.locator('input[type=date]').nth(1).fill('2026-06-30');
  await page.locator('tfoot').waitFor({ state: 'visible', timeout: 15_000 });
  await capture('step-02-sales-summary', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: สรุปยอดขาย — เลือกช่วงวันที่ + "จัดกลุ่มตาม" (ลูกค้า/สินค้า/หน่วยธุรกิจ) →' +
      ' ตารางจำนวนเอกสาร · ยอดก่อน VAT · VAT · ยอดรวม ต่อกลุ่ม + แถวรวม',
  });

});

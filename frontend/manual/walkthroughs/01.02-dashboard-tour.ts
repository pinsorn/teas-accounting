/**
 * 01.02 — Dashboard tour
 *
 * Chapter: 1. เริ่มต้นใช้งาน
 * Story: User เพิ่ง login เสร็จ → ทำความรู้จัก dashboard + sidebar structure
 *
 * Verified live via Chrome MCP at http://localhost:3000/ (2026-05-19 Sana session).
 * Confirmed: 4 stat cards render with manual-demo seed (company_id=2, empty data → 0 values).
 * Sidebar groups confirmed: (ขาย implicit) → ซื้อ → รายงาน → ตั้งค่า.
 *
 * Pre-condition:
 *   - Logged in as demo-accountant (via walkthrough 01.01)
 *   - Currently on URL / (dashboard root)
 *
 * Acceptance:
 *   - Heading "แดชบอร์ด" + subtitle "ภาพรวมระบบ" visible
 *   - 4 stat cards labeled: ใบกำกับภาษีเดือนนี้ / ยอดขายเดือนนี้ /
 *     ภาษีขายเดือนนี้ / เลขเอกสารขาดช่วง
 *   - Sidebar shows 4 groups: (ขาย — implicit, no label) / ซื้อ / รายงาน / ตั้งค่า
 *   - Footer of sidebar: TH/EN toggle + ออกจากระบบ button
 *
 * Note: ปัจจุบัน TEAS ยังไม่มี header bar ด้านบน — ทุก navigation อยู่ใน sidebar.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '01.02',
  title: 'สำรวจ Dashboard',
  chapter: '1. เริ่มต้นใช้งาน',
  intro: `
หลัง login สำเร็จ ผู้ใช้จะมาที่ "แดชบอร์ด" — หน้าแรกที่สรุปภาพรวมระบบ
และเป็นจุดเริ่มต้นในการเข้าทุกโมดูล. ในบทนี้คุณจะรู้จัก:

- 4 stat cards ที่สรุปสถานะการเงินของเดือนนี้
- โครงสร้าง sidebar ที่แบ่งเป็น 4 กลุ่ม: ขาย / ซื้อ / รายงาน / ตั้งค่า
- ปุ่ม TH/EN สำหรับสลับภาษา และปุ่ม "ออกจากระบบ"

หมายเหตุ: TEAS ใช้ navigation แบบ sidebar เพียงอย่างเดียว — ไม่มี top header bar.
  `.trim(),
  prerequisites: [
    'login แล้ว (walkthrough 01.01)',
    'อยู่ที่ URL / (dashboard root)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: overview — heading + 4 stat cards ───────────────────────
  await page.goto('/');
  await capture('step-01-overview', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: นี่คือหน้า "แดชบอร์ด" — สรุปภาพรวมระบบของเดือนปัจจุบัน.' +
      ' หัวเรื่อง "แดชบอร์ด" + คำอธิบาย "ภาพรวมระบบ" + 4 stat cards ด้านบน',
  });

  // ─── Step 2: stat card — ใบกำกับภาษีเดือนนี้ ─────────────────────────
  await capture('step-02-card-tax-invoices', {
    // First card: index-based since each card is structurally identical
    highlight: 'main > div:nth-of-type(1) > div:nth-of-type(1)',
    arrow: 'down',
    caption:
      'ขั้นที่ 2: card แรก "ใบกำกับภาษีเดือนนี้" — จำนวนใบกำกับภาษีขายที่ Post' +
      ' แล้วในเดือนปัจจุบัน. เมื่อยังไม่มีข้อมูลจะแสดง "—"',
  });

  // ─── Step 3: stat card — ยอดขายเดือนนี้ ──────────────────────────────
  await capture('step-03-card-sales', {
    highlight: 'main > div:nth-of-type(1) > div:nth-of-type(2)',
    arrow: 'down',
    caption:
      'ขั้นที่ 3: card "ยอดขายเดือนนี้" — ผลรวม net amount ของใบกำกับภาษีขาย' +
      ' ที่ Post แล้ว (ไม่รวมที่ยกเลิก). หน่วยเป็นบาท (฿)',
  });

  // ─── Step 4: stat card — ภาษีขายเดือนนี้ ─────────────────────────────
  await capture('step-04-card-output-vat', {
    highlight: 'main > div:nth-of-type(1) > div:nth-of-type(3)',
    arrow: 'down',
    caption:
      'ขั้นที่ 4: card "ภาษีขายเดือนนี้" — ผลรวม VAT 7% (Output VAT) ที่' +
      ' เรียกเก็บจากลูกค้า. เป็นตัวเลขเดียวกับที่จะยื่น ภ.พ.30 ปลายเดือน',
  });

  // ─── Step 5: stat card — เลขเอกสารขาดช่วง ────────────────────────────
  await capture('step-05-card-number-gaps', {
    highlight: 'main > div:nth-of-type(1) > div:nth-of-type(4)',
    arrow: 'down',
    caption:
      'ขั้นที่ 5: card "เลขเอกสารขาดช่วง" — จำนวนช่วงของเลขเอกสารที่ขาด' +
      ' (ตามกฎบัญชี TEAS ต้องเป็น gapless). ถ้าไม่ใช่ 0 ต้องรีบตรวจ — ดูบทที่ 5',
  });

  // ─── Step 6: sidebar — ขาย group ─────────────────────────────────────
  await capture('step-06-sidebar-sales', {
    // Sales group is implicit — first 9 nav links (no group header above)
    highlight: 'nav',
    arrow: 'right',
    caption:
      'ขั้นที่ 6: sidebar กลุ่มแรก (ไม่มีหัวข้อ) คือ "ขาย" — รวม' +
      ' แดชบอร์ด, ใบเสนอราคา, ใบสั่งขาย, ใบส่งของ, ใบกำกับภาษี, ใบเสร็จรับเงิน,' +
      ' ใบลดหนี้, ใบเพิ่มหนี้, ตรวจเลขเอกสารขาดช่วง',
  });

  // ─── Step 7: sidebar — ซื้อ group ─────────────────────────────────────
  await capture('step-07-sidebar-purchase', {
    // Scroll to the "ซื้อ" section header
    highlight: 'nav',
    arrow: 'right',
    caption:
      'ขั้นที่ 7: กลุ่ม "ซื้อ" — ผู้ขาย, บันทึกใบกำกับภาษีซื้อ, ใบสั่งซื้อ,' +
      ' ใบสำคัญจ่าย, หนังสือรับรองหัก ณ ที่จ่าย (50 ทวิ)',
  });

  // ─── Step 8: sidebar — รายงาน group ──────────────────────────────────
  await capture('step-08-sidebar-reports', {
    highlight: 'nav',
    arrow: 'right',
    caption:
      'ขั้นที่ 8: กลุ่ม "รายงาน" — งบทดลอง, กำไรขาดทุน, สรุปยอดขาย, ภ.พ.30,' +
      ' PO ค้าง, แบบยื่นภาษี, ภาษีหัก ณ ที่จ่ายค้างรับ',
  });

  // ─── Step 9: sidebar — ตั้งค่า group ─────────────────────────────────
  await capture('step-09-sidebar-settings', {
    highlight: 'nav',
    arrow: 'right',
    caption:
      'ขั้นที่ 9: กลุ่ม "ตั้งค่า" — 5 รายการ. ลิงก์แรก "ข้อมูลบริษัท"' +
      ' (Sprint 13d-P6, ทำก่อนเป็นอันดับแรกสำหรับ tenant ใหม่ — ดูบท 02.05)' +
      ' ตามด้วย สินค้า/บริการ, หน่วยธุรกิจ (Business Unit),' +
      ' ประเภทหัก ณ ที่จ่าย (admin), API Keys (admin)',
  });

  // ─── Step 10: sidebar footer — TH/EN + logout ────────────────────────
  await capture('step-10-sidebar-footer', {
    // Bottom of sidebar — TH/EN button + logout button
    highlight: 'aside button',
    arrow: 'right',
    caption:
      'ขั้นที่ 10: ด้านล่าง sidebar มี 2 ปุ่ม — "TH / EN" สลับภาษา (ดูบท 01.03)' +
      ' และ "ออกจากระบบ" สำหรับ logout (ดูบท 01.04)',
  });

});

/**
 * 01.02 — Dashboard tour (redesigned home, cont.97d)
 *
 * Chapter: 1. เริ่มต้นใช้งาน
 * Story: User เพิ่ง login เสร็จ → ทำความรู้จักหน้าแรก (แดชบอร์ดที่ออกแบบใหม่)
 *        + โครงสร้าง sidebar
 *
 * ⚠️ Re-captured 2026-06-14 against the REDESIGNED dashboard
 * (app/(dashboard)/page.tsx, cont.97d). The previous version documented the
 * old "4 stat cards" home — that page no longer exists. New home:
 *   - header: greeting + company name (from profile) + "ภาพรวมเดือน…"
 *   - KPI section "ตัวเลขสำคัญเดือนนี้": 5 tiles (รายได้ / รายจ่าย / กำไรสุทธิ /
 *     VAT สุทธิ [VAT companies only] / ภาษีหัก ณ ที่จ่าย)
 *   - trend chart "รายรับ-รายจ่าย ปี <ปี>" (2/3) + alerts panel "ต้องทำ / แจ้งเตือน" (1/3)
 *   - quick actions "ทางลัด" — buttons gated by the user's permissions
 *
 * Persona: demo-accountant (default for 01.02). The quick-actions row and the
 * sidebar are permission-gated, so what renders depends on this persona —
 * captions describe them generically ("ตามสิทธิ์ของผู้ใช้") rather than
 * enumerating every button, so the doc survives RBAC changes.
 *
 * Data note: manual-demo company (company_id=2) is a fresh tenant with no
 * posted sales yet → all KPIs show ฿0 and the trend is empty. That is the
 * legitimate "new tenant" view. co2 is VAT-registered (vat_mode=true, 7%) so
 * the VAT สุทธิ tile renders and (on/before the 15th) a ภ.พ.30 alert appears.
 *
 * Pre-condition:
 *   - Logged in as demo-accountant (run-capture pre-logs in)
 *   - Lands on URL / (dashboard root)
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '01.02',
  title: 'สำรวจหน้าแรก (แดชบอร์ด)',
  chapter: '1. เริ่มต้นใช้งาน',
  intro: `
หลัง login สำเร็จ ผู้ใช้จะมาที่ "แดชบอร์ด" — หน้าแรกที่สรุปสถานะการเงิน
ของเดือนปัจจุบัน และเป็นจุดเริ่มต้นเข้าทุกโมดูล. ในบทนี้คุณจะรู้จัก:

- **ส่วนหัว** — คำทักทาย + ชื่อบริษัท (ดึงจากข้อมูลบริษัท) + เดือนที่กำลังดู
- **ตัวเลขสำคัญเดือนนี้ (KPI)** — 5 ช่อง: รายได้ / รายจ่าย / กำไรสุทธิ /
  VAT สุทธิ (เฉพาะบริษัทจด VAT) / ภาษีหัก ณ ที่จ่าย
- **กราฟรายรับ-รายจ่ายทั้งปี** + แผง **"ต้องทำ / แจ้งเตือน"** ที่รวมงานค้าง
  (เลขเอกสารขาดช่วง, เอกสารซื้อไม่ครบ, ภ.พ.30 ใกล้ครบกำหนด ฯลฯ)
- **ทางลัด** — ปุ่มสร้างเอกสารที่ใช้บ่อย (แสดงตามสิทธิ์ของผู้ใช้)
- โครงสร้าง **sidebar** ที่แบ่งเป็นกลุ่ม: ขาย / ซื้อ / เงินเดือน / รายงาน / ตั้งค่า

หมายเหตุ: บริษัทตัวอย่างนี้เป็น tenant ใหม่ ยังไม่มีเอกสารที่โพสต์ →
ตัวเลขทุกช่องจึงเป็น ฿0 และกราฟยังว่าง. เมื่อเริ่มออกเอกสารจริง ตัวเลข
จะอัปเดตอัตโนมัติ.
  `.trim(),
  prerequisites: [
    'login แล้ว (walkthrough 01.01)',
    'อยู่ที่ URL / (dashboard root)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: clean overview (no spotlight) ───────────────────────────
  await page.goto('/');
  // settle KPI/trend queries before the first shot
  await page.getByRole('heading', { level: 1 }).first().waitFor({ state: 'visible' });
  await capture('step-01-overview', {
    caption:
      'ขั้นที่ 1: หน้า "แดชบอร์ด" — หน้าแรกหลัง login. แถบบนสุดมีช่องค้นหาเอกสาร/' +
      'ลูกค้า (กด ⌘K), กระดิ่งแจ้งเตือน และไอคอนตั้งค่า. ถัดมาเป็นส่วนหัว (คำทักทาย' +
      ' + ชื่อบริษัท + เดือนที่กำลังดู) ตามด้วยตัวเลขสำคัญ, กราฟ, แผงแจ้งเตือน' +
      ' และปุ่มทางลัด — ทั้งหมดของเดือนปัจจุบัน',
  });

  // ─── Step 2: header — company name + month ───────────────────────────
  await capture('step-02-header', {
    highlight: 'header',
    arrow: 'down',
    caption:
      'ขั้นที่ 2: ส่วนหัว — "สวัสดี 👋" + ชื่อบริษัท (ดึงจากข้อมูลบริษัทที่ตั้งไว้' +
      ' ในเมนู "ตั้งค่า → ข้อมูลบริษัท" — ดูบท 02.05) และข้อความ "ภาพรวมเดือน…"' +
      ' บอกว่ากำลังดูข้อมูลของเดือนใด',
  });

  // ─── Step 3: KPI tiles ───────────────────────────────────────────────
  await capture('step-03-kpi', {
    highlight: 'section[aria-label]',
    caption:
      'ขั้นที่ 3: "ตัวเลขสำคัญเดือนนี้" — 5 ช่อง: รายได้, รายจ่าย, กำไรสุทธิ,' +
      ' VAT สุทธิ (เฉพาะบริษัทจด VAT), ภาษีหัก ณ ที่จ่าย. บริษัทใหม่ที่ยังไม่ออก' +
      ' เอกสารจะแสดง ฿0 ทุกช่อง แล้วอัปเดตเองเมื่อโพสต์เอกสาร',
  });

  // ─── Step 4: VAT net tile (VAT companies only) ───────────────────────
  await capture('step-04-kpi-vat', {
    highlight: 'section[aria-label] > div:nth-of-type(4)',
    arrow: 'down',
    caption:
      'ขั้นที่ 4: ช่อง "VAT สุทธิ" = ภาษีขาย − ภาษีซื้อของเดือนนี้ (ตัวเลขที่จะยื่น' +
      ' ภ.พ.30). มีกำกับ "ต้องชำระ" หรือ "ขอคืนได้" ตามผลลัพธ์. ช่องนี้แสดงเฉพาะ' +
      ' บริษัทที่จดทะเบียน VAT — บริษัทไม่จด VAT จะไม่เห็นช่องนี้',
  });

  // ─── Step 5: trend chart ─────────────────────────────────────────────
  await capture('step-05-trend', {
    highlight: 'div.grid > section:nth-of-type(1)',
    arrow: 'up',
    caption:
      'ขั้นที่ 5: กราฟ "รายรับ-รายจ่าย" ทั้งปี — แท่งคู่รายเดือน (เขียว=รายได้,' +
      ' แดง=รายจ่าย) ครบทั้ง 12 เดือน. tenant ใหม่ยังไม่ออกเอกสาร แท่งจึงเตี้ย' +
      ' (เป็น 0) ทุกเดือน แล้วจะสูงขึ้นเองเมื่อมีรายการจริง. ลิงก์ "ดูสรุปภาษี"' +
      ' มุมขวาบนพาไปหน้าสรุปภาษีรายเดือนแบบละเอียด (บท 07.03)',
  });

  // ─── Step 6: alerts panel ────────────────────────────────────────────
  await capture('step-06-alerts', {
    highlight: 'div.grid > section:nth-of-type(2)',
    arrow: 'up',
    caption:
      'ขั้นที่ 6: แผง "ต้องทำ / แจ้งเตือน" — รวมงานค้างที่ต้องจัดการ เช่น' +
      ' เลขเอกสารขาดช่วง, เอกสารซื้อยังไม่ครบ, ภ.พ.30 ใกล้ครบกำหนด. แต่ละรายการ' +
      ' คลิกไปหน้าที่เกี่ยวข้องได้ทันที. ถ้าไม่มีงานค้างจะขึ้น "เรียบร้อยดี" ✓',
  });

  // ─── Step 7: quick actions (permission-gated) ────────────────────────
  await capture('step-07-quick', {
    highlight: 'main section:last-of-type',
    arrow: 'up',
    caption:
      'ขั้นที่ 7: "ทางลัด" — ปุ่มสร้างเอกสารที่ใช้บ่อย (ออกใบกำกับภาษี, ออกใบเสร็จ,' +
      ' สร้างใบสำคัญจ่าย, เพิ่มลูกค้า/ผู้ขาย). ระบบแสดงเฉพาะปุ่มที่ผู้ใช้มีสิทธิ์ —' +
      ' ผู้ใช้แต่ละบทบาทจึงเห็นปุ่มไม่เหมือนกัน',
  });

  // ─── Step 8: sidebar groups + footer ─────────────────────────────────
  await capture('step-08-sidebar', {
    highlight: 'aside, nav',
    arrow: 'right',
    caption:
      'ขั้นที่ 8: แถบเมนูซ้าย (sidebar) คือ navigation หลัก แบ่งเป็นกลุ่ม:' +
      ' ขาย / ซื้อ / เงินเดือน / รายงาน / ตั้งค่า (เมนูที่เห็นขึ้นกับสิทธิ์ของผู้ใช้).' +
      ' ด้านล่างสุดมีปุ่มสลับภาษา TH/EN (บท 01.03) และ "ออกจากระบบ" (บท 01.04)',
  });

});

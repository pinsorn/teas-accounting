/**
 * 07.04 — ภาษีจากการจ่ายเงินไปต่างประเทศ: ภ.พ.36 (reverse charge) + ภ.ง.ด.54
 *
 * Chapter: 7. ภาษี
 * Story: ซื้อบริการจากผู้ขายต่างประเทศที่ไม่มีเลขประจำตัวผู้เสีย VAT ในไทย (VAT-D) →
 *        ผู้จ่ายในไทยต้อง **"นำส่ง VAT แทน" (reverse charge, ม.83/6) = ภ.พ.36** และถ้ามี
 *        การหักภาษี ณ ที่จ่ายตาม ม.70 ก็ยื่น **ภ.ง.ด.54**. นำส่งภายในวันที่ 7 ของเดือนถัดไป.
 *
 * Persona: admin (report/tf read). Captured against /tax-filings/pnd36 (co2 มี vendor
 * ต่างประเทศ Amazon — ดู 05.05). READ-ONLY: กด "แสดงตัวอย่าง" (คำนวณ + ดูรายการ ไม่ปิดงวด).
 * ภ.ง.ด.54: เดโม co2 ยังไม่มีรายการ ม.70 → อธิบายเป็นข้อความ (หน้าจอเดียวกับ ภ.ง.ด.3/53).
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '07.04',
  title: 'ภ.พ.36 (reverse charge) + ภ.ง.ด.54 — จ่ายต่างประเทศ',
  chapter: '7. ภาษี',
  persona: 'admin',
  intro: `
เวลาจ่ายเงินไป **ต่างประเทศ** มีภาษี 2 ตัวที่อาจต้องนำส่ง (ภายในวันที่ 7 ของเดือนถัดไป):

- **ภ.พ.36 (นำส่ง VAT แทน — reverse charge, ม.83/6)** — ซื้อ **บริการ** จากผู้ขายต่างประเทศ
  ที่ **ไม่มี VAT-D ในไทย** (เช่น ค่าโฆษณา/คลาวด์/ซอฟต์แวร์) → ผู้จ่ายต้องนำส่ง VAT 7% แทน
  ผู้ขาย. ระบบ **ลงบัญชี (JV) ให้อัตโนมัติ** และยอด VAT นี้ใช้เป็น **ภาษีซื้อ** ในเดือนถัดไปได้
  (ดู 05.05 การเลือกผู้ขายต่างประเทศ).
- **ภ.ง.ด.54 (หัก ณ ที่จ่าย จ่ายต่างประเทศ — ม.70)** — ถ้าจ่ายค่าสิทธิ/ดอกเบี้ย/ค่าบริการบางชนิด
  ให้ผู้รับในต่างประเทศ ต้องหักภาษีนำส่ง. ใช้หน้าจอแบบเดียวกับ ภ.ง.ด.3/53 (period → แสดงตัวอย่าง).

หน้านี้สาธิต **ภ.พ.36** (กิจการมีรายการจริง). **ภ.ง.ด.54** ในชุดข้อมูลตัวอย่างยังไม่มีรายการ ม.70
จึงอธิบายไว้เป็นข้อความ.
  `.trim(),
  prerequisites: [
    'login admin (สิทธิ์ report/tf)',
    'มีใบกำกับภาษีซื้อจากผู้ขายต่างประเทศ (ไม่มี VAT-D) ในงวด — ดู 05.05',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: ภ.พ.36 preview — reverse-charge VAT on foreign services ──
  await page.goto('/tax-filings/pnd36');
  await page.locator('input[type=month]').fill('2026-06');
  await page.getByRole('button', { name: 'แสดงตัวอย่าง' }).click();
  await page.getByText('กำหนดยื่นภายใน').waitFor({ state: 'visible', timeout: 15_000 });
  // Guard the live dependency (06.01 lesson): the value of this step IS the rows.
  // An empty placeholder is a single <td colSpan=6>; real rows have 6 cells each.
  const bodyRows = page.locator('main table tbody tr');
  if ((await bodyRows.count()) === 1 && (await bodyRows.first().locator('td').count()) === 1) {
    throw new Error(
      '07.04 ภ.พ.36: 0 rows for 2026-06 — needs seeded foreign-vendor (no VAT-D) ' +
      'service purchases in that period. Re-seed before capturing (do not ship an empty table).',
    );
  }
  await capture('step-01-pnd36-preview', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ภ.พ.36 แสดงตัวอย่าง — ตารางรวมการซื้อบริการจากต่างประเทศที่ต้องนำส่ง VAT แทน' +
      ' (ผู้ขาย · ประเทศ · เอกสารอ้างอิง · มูลค่าบริการ · VAT 7%) + ยอดรวม. ระบบลงบัญชี reverse charge' +
      ' (JV) ให้อัตโนมัติตามหมายเหตุท้ายตาราง — VAT ที่นำส่งนี้ใช้เป็นภาษีซื้อเดือนถัดไปได้',
  });

});

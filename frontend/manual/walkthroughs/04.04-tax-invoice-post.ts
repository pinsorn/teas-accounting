/**
 * 04.04 — ออก + โพสต์ใบกำกับภาษี (Tax Invoice) — หัวใจ compliance ม.86/4
 *
 * Chapter: 4. งานขาย
 * Story: สร้างใบกำกับภาษีเต็มรูป → เลือกลูกค้า + หน่วยธุรกิจ + รายการ → Post.
 *        การ Post = ออกเลขเอกสารตามลำดับ (no gap) + ตรึงข้อมูลถาวร (immutable).
 *
 * Persona: admin (ต้องมี sales.tax_invoice.create + sales.tax_invoice.post).
 * Captured against /tax-invoices/new (PartySelectBox + BusinessUnitSelector +
 * LineItemsTable). Selectors mirror frontend/e2e/_helpers.ts (proven).
 *
 * Data (co2): pick "บริษัท แอคมี จำกัด" (MC-COR-001, จด VAT, taxId 13 หลัก) →
 * ใบกำกับภาษีเต็มรูปต้องมีเลขผู้เสียภาษีผู้ซื้อเมื่อผู้ซื้อจด VAT (ม.86/4 #3).
 *
 * ⚠️ การ Post สร้างเอกสารจริงบน company 2 (มีเลขถาวร, ตรึงไม่ลบ). ทุกครั้งที่
 *    re-capture จะได้ใบใหม่ (เลขเดินต่อ) — เป็นพฤติกรรม gapless ที่ถูกต้อง.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.04',
  title: 'ออกและโพสต์ใบกำกับภาษี',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ใบกำกับภาษี (Tax Invoice) คือเอกสารทางภาษีที่สำคัญที่สุดของกิจการ VAT —
ออกเมื่อขายสินค้า/บริการให้ผู้ซื้อ และเป็นหลักฐานภาษีขาย (Output VAT).

**กฎหมาย ม.86/4 — ใบกำกับภาษีเต็มรูปต้องมีครบ 8 รายการ:**

1. คำว่า "ใบกำกับภาษี" เด่นชัด
2. ชื่อ-ที่อยู่-เลขผู้เสียภาษี (13 หลัก) + สาขาของผู้ขาย
3. ชื่อ-ที่อยู่-เลขผู้เสียภาษีของผู้ซื้อ **เมื่อผู้ซื้อจด VAT**
4. เลขที่เอกสารเรียงลำดับ **ห้ามขาดช่วง (gapless)**
5. ชื่อ/ชนิด/ปริมาณ/มูลค่าของสินค้าแต่ละบรรทัด
6. **แยกแสดงภาษีมูลค่าเพิ่ม (VAT) ออกจากมูลค่าสินค้า**
7. วันที่ออก = วันที่จุดความรับผิดทางภาษี (tax point)
8. ข้อความอื่นตามที่กำหนด

**การ Post (โพสต์):** ตอนกด Post ระบบจะออกเลขที่เอกสารตามลำดับเดือน (ไม่ขาดช่วง)
และ **ตรึงข้อมูลถาวร** — แก้/ลบไม่ได้อีก (ม.86 + พรบ.การบัญชี). หากออกผิด ต้อง
แก้ด้วย **ใบลดหนี้ (Credit Note)** แล้วออกใบใหม่ ไม่ใช่แก้ใบเดิม.
  `.trim(),
  prerequisites: [
    'login ในฐานะผู้มีสิทธิ์ sales.tax_invoice.create + .post (admin)',
    'มีลูกค้าจด VAT + หน่วยธุรกิจ ในระบบ (บทที่ 2–3)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: blank form (3 sections) ─────────────────────────────────
  await page.goto('/tax-invoices/new');
  await capture('step-01-form', {
    highlight: 'main',
    caption:
      'ขั้นที่ 1: ฟอร์ม "สร้างใบกำกับภาษี" — 3 ส่วน: ① ลูกค้า, ② ข้อมูลเอกสาร' +
      ' (วันที่ออก = วันนี้ ล็อกไว้ตามกฎ tax point + หน่วยธุรกิจ), ③ รายการสินค้า' +
      ' /บริการ พร้อมยอดรวมและภาษีแยกบรรทัด',
  });

  // ─── Step 2: pick a VAT-registered customer ──────────────────────────
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const custDialog = page.getByRole('dialog');
  await custDialog.getByRole('textbox').fill('แอคมี');
  await custDialog.getByRole('button', { name: /แอคมี/ }).first().click();
  await capture('step-02-customer', {
    highlight: 'main',
    caption:
      'ขั้นที่ 2: กด "เลือกลูกค้า" → ค้นหาด้วยชื่อหรือเลขผู้เสียภาษี → เลือก' +
      ' "บริษัท แอคมี จำกัด" (ลูกค้าจด VAT). ระบบดึงชื่อ + เลขผู้เสียภาษี 13 หลัก' +
      ' ของผู้ซื้อมาแสดง — ข้อมูลนี้จะพิมพ์ลงใบกำกับภาษี (ม.86/4 #3)',
  });

  // ─── Step 3: choose business unit (required on income docs for co2) ──
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ index: 1 });
  await capture('step-03-bu', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เลือก "หน่วยธุรกิจ" (บริษัทนี้ตั้งค่าให้บังคับระบุในเอกสารรายได้' +
      ' — ดูบท 02.01). หน่วยธุรกิจช่วยแยกยอดขาย/รายงานตามสายงาน',
  });

  // ─── Step 4: add a line item — VAT computed automatically ────────────
  await page.getByLabel('รายละเอียด 1').fill('ค่าบริการที่ปรึกษาระบบบัญชี');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('10000');
  await capture('step-04-line-vat', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 4: กรอกรายการ — "ค่าบริการที่ปรึกษาระบบบัญชี" จำนวน 1 ราคา 10,000.' +
      ' กล่องยอดรวมคำนวณให้อัตโนมัติ: มูลค่าก่อนภาษี 10,000 + ภาษีมูลค่าเพิ่ม 7%' +
      ' = 700 → รวมทั้งสิ้น 10,700. สังเกตว่า VAT แสดง "แยก" ตาม ม.86/4 #6',
  });

  // ─── Step 5: Post → confirm dialog ───────────────────────────────────
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).first().click();
  const postDialog = page.getByRole('dialog');
  await postDialog.waitFor({ state: 'visible' });
  await capture('step-05-confirm', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 5: กด "บันทึกเอกสาร" → กล่องยืนยันเตือนว่า การโพสต์จะออกเลขที่' +
      ' เอกสารถาวรและ "แก้ไขไม่ได้อีก". ตรวจข้อมูลให้ครบถูกก่อนยืนยัน เพราะแก้ทีหลัง' +
      ' ต้องทำผ่านใบลดหนี้เท่านั้น',
  });

  // ─── Step 6: posted — number assigned, immutable ─────────────────────
  await postDialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+/, { timeout: 15_000 });
  await capture('step-06-posted', {
    highlight: 'main',
    caption:
      'ขั้นที่ 6: โพสต์สำเร็จ → ระบบออกเลขที่เอกสารตามลำดับเดือน (รูปแบบ' +
      ' MM-YYYY-PREFIX-NNNN, ไม่ขาดช่วง) และเปลี่ยนสถานะเป็น "โพสต์แล้ว".' +
      ' เอกสารนี้ตรึงถาวร — พิมพ์ PDF / ส่ง e-Tax / ออกใบเสร็จอ้างอิงได้ต่อไป',
  });

});

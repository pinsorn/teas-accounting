/**
 * 04.08 — เลือกสินค้า/บริการในเอกสาร (Product / Service picker) + ผลต่อ VAT
 *
 * Chapter: 4. งานขาย
 * Story: ทุกเอกสารซื้อ-ขายที่มีรายการ (ใบเสนอราคา/ใบกำกับภาษี/ใบสั่งซื้อ/ใบสำคัญจ่าย ฯลฯ)
 *        ใช้ตัวเลือกสินค้า/บริการตัวเดียวกัน — กดเลือกจาก "ข้อมูลหลักสินค้า" (master) เพื่อ
 *        ดึงชื่อ/ราคา/ประเภท มาให้อัตโนมัติ, หรือพิมพ์เองสำหรับรายการเฉพาะกิจ.
 *        **ประเภทสินค้า** เป็นตัวกำหนดว่ารายการนั้นคิด VAT 7% หรือ "ยกเว้น VAT" (ม.81).
 *
 * Persona: admin. Captured against /quotations/new (LineItemsTable enableProduct →
 * ProductPicker → ProductSearchModal). ตัวเลือกเดียวกันใช้ในเอกสารฝั่งซื้อด้วย.
 *
 * Data (co2 products): บริการ MP-SVC-* (ค่าที่ปรึกษา ฯลฯ), สินค้า MP-GD-*, สินค้ายกเว้น
 * VAT MP-EXM-* (เต่าบก/ปลาทอง — สัตว์มีชีวิต ม.81). ไม่ submit — สาธิตการเลือกเท่านั้น.
 */
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '04.08',
  title: 'เลือกสินค้า/บริการในเอกสาร',
  chapter: '4. งานขาย',
  persona: 'admin',
  intro: `
ทุกเอกสารที่มี "รายการสินค้า/บริการ" (ใบเสนอราคา, ใบกำกับภาษี, ใบสั่งซื้อ, ใบสำคัญจ่าย ฯลฯ)
ใช้ช่องเลือกสินค้า/บริการแบบเดียวกัน — แต่ละบรรทัดมีปุ่ม **"เลือกจากรายการ" (🔍)** เปิด
หน้าต่างค้นหาจาก **ข้อมูลหลักสินค้า (Product Master, ดู 02.02)**.

**เลือกจาก master ได้อะไร:** ระบบดึง **ชื่อ + รหัส + ราคาตั้งต้น + หน่วยนับ + ประเภท** มาเติม
ให้อัตโนมัติ ไม่ต้องพิมพ์ซ้ำและลดความผิดพลาด. ถ้าเป็นรายการเฉพาะกิจที่ไม่มีใน master
ก็ **พิมพ์รายละเอียดเองได้** (ad-hoc) โดยไม่ต้องสร้างสินค้าใหม่.

**ประเภทสินค้า/บริการ → ผลทาง VAT:**

| ประเภท | ป้าย | VAT |
|---|---|---|
| สินค้า (GOOD) | 🟦 สินค้า | 7% |
| บริการ (SERVICE) | 🟪 บริการ | 7% |
| สินค้า/บริการยกเว้น (EXEMPT) | 🟧 ยกเว้น VAT | **0% (ม.81)** เช่น สัตว์มีชีวิต พืชผลเกษตร |

ระบบ **คิด VAT ให้ตามประเภทอัตโนมัติ** — เลือกสินค้ายกเว้นแล้วบรรทัดนั้นจะไม่มี VAT ทันที
โดยผู้ใช้ไม่ต้องตั้งเอง (กันออกภาษีผิดประเภท).
  `.trim(),
  prerequisites: [
    'login admin',
    'มีข้อมูลหลักสินค้า/บริการในระบบ (02.02)',
  ],
}, async ({ page, capture }) => {

  // ─── Step 1: a document with a line table ────────────────────────────
  await page.goto('/quotations/new');
  await capture('step-01-line-table', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 1: ในส่วน "รายการ" ของเอกสาร แต่ละบรรทัดมีช่องรายละเอียด + ปุ่มแว่นขยาย' +
      ' "เลือกจากรายการ" สำหรับดึงสินค้า/บริการจากข้อมูลหลัก (หรือจะพิมพ์เองก็ได้)',
  });

  // ─── Step 2: open the product search modal ───────────────────────────
  await page.getByRole('button', { name: 'เลือกจากรายการ' }).first().click();
  const modal = page.getByRole('dialog');
  await modal.waitFor({ state: 'visible' });
  await page.waitForTimeout(800); // debounced product list (250ms) + render
  await capture('step-02-modal', {
    highlight: '[role="dialog"]',
    caption:
      'ขั้นที่ 2: หน้าต่าง "เลือกสินค้า / บริการ" — ค้นหาด้วยชื่อหรือรหัส. แต่ละรายการ' +
      ' แสดง รหัส · ชื่อ · ป้ายประเภท (สินค้า/บริการ/ยกเว้น VAT) · ราคาตั้งต้น',
  });

  // ─── Step 3: pick a SERVICE → auto-fills name + price, VAT 7% ─────────
  await modal.getByRole('textbox').fill('ที่ปรึกษา');
  await page.waitForTimeout(600);
  await modal.getByRole('button', { name: /ที่ปรึกษา/ }).first().click();
  await capture('step-03-service', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 3: เลือกบริการ "ค่าที่ปรึกษา" → ระบบเติมชื่อ + ราคาตั้งต้น (2,000) +' +
      ' หน่วยนับให้อัตโนมัติ และตั้งอัตราภาษีเป็น 7% เพราะเป็นประเภท "บริการ"',
  });

  // ─── Step 4: re-pick an EXEMPT good → VAT auto-becomes 0 (ม.81) ──────
  await page.getByRole('button', { name: 'เลือกจากรายการ' }).first().click();
  const modal2 = page.getByRole('dialog');
  await modal2.waitFor({ state: 'visible' });
  await modal2.getByRole('textbox').fill('เต่า');
  await page.waitForTimeout(600);
  await modal2.getByRole('button', { name: /เต่า/ }).first().click();
  await capture('step-04-exempt', {
    highlight: 'main',
    arrow: 'up',
    caption:
      'ขั้นที่ 4: เลือกสินค้ายกเว้น VAT "เต่าบก (สัตว์มีชีวิต)" → อัตราภาษีของบรรทัด' +
      ' เปลี่ยนเป็น 0 อัตโนมัติ (ยกเว้น VAT ตาม ม.81). ระบบกันการคิดภาษีผิดประเภทให้เอง.' +
      ' รายการที่ไม่มีใน master ให้พิมพ์รายละเอียดเองได้',
  });

});

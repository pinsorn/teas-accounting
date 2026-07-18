# HANDOFF — สร้าง Company Dummy (VAT-enabled) + เทสต์สายที่ยังไม่เคยถูกรันจริง

Session ถัดไป. สั่งโดย Ham 2026-07-18 หลังปิดรอบ payroll+reports (v1.21.5 live, smoke 7/7).
เหตุผล: สองสายใหญ่ยังไม่เคยถูก e2e จริงเพราะทำบน Repttown (บัญชีจริง + non-VAT) ไม่ได้:
(1) payroll หลัง Post ทั้งสาย (JE จริง ลบไม่ได้) (2) ทุกอย่างฝั่ง VAT (ภ.พ.30, ใบกำกับภาษี,
sales-summary). Dummy company แยก tenant → JE/เอกสารข้างในไม่กระทบใคร เทสต์ได้สุดทาง.

## Context ที่ต้องรู้ก่อนเริ่ม
- Prod: https://teas.kazaki-rio.com (v1.21.5) — Chrome session ของ Ham (token 8 ชม.
  หมดแล้วต้องขอ login ใหม่; Claude ไม่แตะ password เด็ดขาด)
- อ้างอิงผลรอบก่อน: `REPORT-payroll-reports-uxtest.md` (20 findings + fix ทั้งหมด),
  `specs/fix-payroll-reports-findings-2026-07-16.md`, manual บท 6+8 (`docs/manual/chapters/`)
- **Footgun สำคัญ (memory: seed-cos-bypass-createasync-taxcodes):** company ที่สร้างด้วย
  raw SQL จะข้าม `CreateAsync` → ไม่มี DefaultTaxCodes → VAT คำนวณเป็น 0 เงียบๆ.
  Dummy ต้องสร้างผ่าน **UI/API ปกติเท่านั้น** แล้ว verify tax_codes ถูก seed ทันทีหลังสร้าง
- MCP connector "TEAS-Repttown" ชี้ Repttown — **ห้ามใช้สร้างเอกสารในเทสต์นี้** (ผิดบริษัท);
  ใช้ Chrome UI ล้วน หรือถ้าจะใช้ MCP ต้องเช็ค company scope ก่อนทุกครั้ง
- co2/co3 (demo เดิม) ห้ามยุ่ง — co2 P&L เป็น load-bearing ของ walkthrough (memory)
- Deploy ล่าสุดไม่มี migration; `applied_sql_scripts` = 69

## Step 0 — Setup (ต้องมี Ham ช่วยตอนเริ่ม)
1. Ham login Chrome + บอกว่า user ที่ใช้มีสิทธิ์สร้างบริษัทไหม (superadmin?) —
   investigate ก่อน: ระบบสร้าง company ใหม่ทางไหน (onboarding flow / superadmin UI /
   settings) — ดู specs/superadmin-tenant-scope.md + FE routes ประกอบ
2. สร้างบริษัท dummy: ชื่อชัดๆ ว่า dummy เช่น "บจก. ทดสอบ VAT (DUMMY)" เลขผู้เสียภาษี
   ปลอม 13 หลัก, **จด VAT = ON** (vatMode), ที่อยู่ครบ (ใช้พิมพ์หัวใบกำกับ/ภ.ง.ด.)
3. Verify หลังสร้างทันที: tax_codes ถูก seed (list ใน settings หรือ SQL ผ่าน SSH:
   `select count(*) from ... tax_codes where company_id=<new>`), VAT mode สะท้อนใน
   /system/info, sidebar โชว์เมนู VAT (ภ.พ.30 ฯลฯ)
4. Setup ขั้นต่ำ: BU 1-2 ตัว, ลูกค้า 2 (นิติบุคคล 1 บุคคล 1 — ฝั่ง WHT ต่างกัน),
   ผู้ขาย 1, สินค้า/บริการ 2-3 ตัว (มี VAT), บัญชีธนาคาร 1 (ปลดล็อกเทสต์ bank-recon)

## Test Plan

### A. Payroll เต็มสาย จนสุด (หัวใจของรอบนี้ — ยังไม่เคยรันจริง)
พนักงาน 3 คน ครอบ scenario:
- A1: เงินเดือน 80,000, เริ่มงานปีที่แล้ว (เช่น 2025-01-01), โสด, ปกส. ON
  → **ภาษีต้องไม่เป็น 0** (คำนวณมือเทียบ: 960k/ปี − expense 100k − allowance 60k
  − ปกส. → ขั้นบันได → /12) + ปกส. ต้องชน cap **875** (WageCeiling 17,500 @5%)
- A2: เงินเดือน 30,000, สมรส + บุตร 1, ปกส. ON → ตรวจลดหย่อนสะท้อนใน breakdown modal
- A3: เงินเดือน 15,000 (ต่ำกว่า ceiling), ปกส. ON → ปกส. = 750 (5% ของจริง ไม่ใช่ cap)
ขั้นทดสอบ:
1. สร้างรอบจ่าย → ตรวจ breakdown modal ทั้ง 3 คน (ตัวเลขต้องตรงคำนวณมือ — จุดนี้คือ
   การ audit เอนจินภาษีครั้งแรกกับค่า nonzero)
2. อนุมัติ → **Post** (ครั้งแรกในประวัติระบบ!) → ตรวจ: เลขเอกสารออก, JE เกิด —
   เปิด GL/งบทดลอง ของ dummy: 5400 เงินเดือน Dr, 5410 ปกส.นายจ้าง Dr, 2153 ภ.ง.ด.1
   ค้างนำส่ง Cr, 2160 ปกส.ค้างนำส่ง Cr, 2170 เงินเดือนค้างจ่าย Cr — ยอดต้อง tie กับหน้ารอบจ่าย
3. เอกสารราชการหลัง Post ทุกตัว: ภ.ง.ด.1 PDF (ตัวเลข+ชื่อครบ), สปส.1-10 (.txt format
   ถูกไหม — เปิดดูเนื้อไฟล์ + PDF), สลิปรายคน, 50ทวิ รายคน, ภ.ง.ด.1ก (ปุ่มหน้า list, ปีนี้)
4. Pay → สถานะจ่ายแล้ว → ตรวจงบทดลอง: 2170 ล้าง, เงินฝากธนาคารลด (ถ้า flow ตัดธนาคาร)
5. เดือนถัดไป: สร้างรอบ 202608 → ภาษีสะสมต้องต่อเนื่อง (ม.50(1) คิดใหม่ตามงวดเหลือ)
6. Negative: ลองสร้างรอบเดือนเดิมซ้ำหลัง Post (ต้อง 422), ลอง Post รอบว่าง (ไม่มีพนักงาน?)

### B. VAT sales chain + รายงานฝั่ง VAT (Repttown ทดสอบไม่ได้เลย)
1. QT → SO → Invoice → **ใบกำกับภาษี (TI)** → RC เต็มสาย — ตรวจ VAT 7% คำนวณถูกทุก hop,
   เลขที่เอกสารรัน, PDF ใบกำกับภาษีมีข้อมูล ภ.พ.20 ครบ
2. **sales-summary ต้องมีข้อมูลแล้ว** (บริษัทนี้มี TI — เทียบกับ Repttown ที่ว่างตลอด;
   ยืนยัน basis footnote ทำงานสองทาง) — จัดกลุ่มทั้ง 3 แบบ
3. **ภ.พ.30**: หน้า report ต้องโชว์แบบจริง (ไม่ใช่ข้อความ non-VAT) — ตรวจยอดขาย/ซื้อ/VAT
   สุทธิตรงกับเอกสาร; ซื้อ 1 ใบ (VI มี VAT ซื้อ) ให้มีทั้งสองฝั่ง
4. tax-summary ของ dummy: คอลัมน์ VAT ขาย/ซื้อ/สุทธิ ต้อง populate
5. AR aging กับ TI ค้างชำระ (ออก TI ไม่รับเงิน 1 ใบ) → bucket ถูก, tie-out 1130 ทำงาน
   กับข้อมูลจริง (Repttown เห็นแต่ 0)
6. Credit Note ถ้ามีเวลา: ลดหนี้ TI → ภ.พ.30 ต้องสะท้อน

### C. เก็บตก
- bank-reconciliation กับบัญชีธนาคารจริงของ dummy (Repttown ไม่มีบัญชี — หน้านี้ยังไม่เคย
  เห็นข้อมูลจริง): import/จับคู่ statement ตาม flow ที่มี
- R10 first-click: จดทุกครั้งที่เจอ (หน้าไหน ปุ่มไหน ครั้งที่เท่าไหร่) — สะสม pattern
- R6 decision input: หลังเห็น sales-summary ทำงานบน VAT company แล้ว สรุปให้ Ham ตัดสิน:
  ควรขยาย basis ให้รวม receipt-based sales ไหม หรือ footnote พอ
- Manual: เติม walkthrough/เนื้อหา posted-state ของบท 6 จากของจริง (ตอนนี้เขียนจากโค้ด)
  + capture หน้าที่ยังติด 🚧 ถ้าทำ capture pipeline ไหว

## กติกา (เหมือนทุกรอบ)
- ทุกอย่างทำใน dummy company เท่านั้น — สลับบริษัทแล้ว **ยืนยัน company badge ทุกครั้ง**
  ก่อนสร้างเอกสาร (สลับพลาด = เอกสารหลงเข้า Repttown = งานใหญ่)
- JE ใน dummy ลบไม่ได้เหมือนกัน — ไม่เป็นไร แต่ตั้งชื่อเอกสาร/หมายเหตุให้รู้ว่าเทสต์
- Findings → report ไฟล์ใหม่ `REPORT-vat-dummy-test.md` ตาม pattern เดิม (ตาราง severity
  + evidence) → spec fix ถ้ามี → รอ Ham อนุมัติก่อนแก้ (pattern เดิม)
- Quota guard: >85% → checkpoint + PROGRESS + wakeup chain (protocol เดิมใน CLAUDE.md)
- ถ้า logged out ระหว่างทาง: หยุด + push แจ้ง Ham + wakeup chain รอ login (ห้ามแตะ password)

## นิยาม "เสร็จ"
A ครบ (Post+Pay+เอกสารราชการครบ 5 ชนิด + JE tie) + B ครบ (TI chain + ภ.พ.30 มีข้อมูลถูก)
+ REPORT เขียน + STATUS อัพเดต. ถ้าเจอ bug เอนจินภาษี/VAT = สำคัญสุด รายงานทันทีพร้อมเลขเทียบมือ.

# UX SWARM — 10 role-accounts รุมใช้ co5 พร้อมกัน (2026-07-19 ~17:3x)

Ham: ไล่ใช้งานต่อ แต่รอบนี้ 10 accounts คนละหน้าที่ + Sonnet 10 ตัวใช้พร้อมกัน.
Target: **https://teas.kazaki-rio.com** (prod v1.22.5), company = บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5).
Goal: RBAC holes, race conditions (doc numbering/approvals under concurrency), 500s/crashes,
raw i18n keys, tenant leaks, UX pain — per role.

## Accounts (all password pattern `UxSwarm-2026-<suffix>`)
| user | role | suffix |
|---|---|---|
| sales01 | Sales Staff | A1 |
| acct01 | Accountant | A2 |
| appr01 | Approver | A3 |
| ap01 | AP Clerk | A4 |
| ar01 | AR Clerk | A5 |
| audit01 | Auditor | A6 |
| chief01 | Chief Accountant | A7 |
| admin01 | Company Admin | A8 |
| purch01 | Purchasing Staff | A9 |
| tax01 | Tax Officer | B1 |

## HARD RULES (ทุก agent)
1. co5 เท่านั้น. ถ้า UI/data ของบริษัทอื่นโผล่ (นาย พงศ์สันต์ / เรปทาวน์) = **CRITICAL tenant-leak
   finding** — screenshot + หยุดส่วนนั้นทันที. ห้ามพยายาม "ใช้" ข้อมูลบริษัทอื่นต่อ.
2. ห้ามเด็ดขาด: ปิดงวด/ยืนยัน ภ.พ.30 (ยืนยัน/ปิดงวด button), year-end closing, payroll mutations
   (payroll = READ-ONLY สำหรับทุกคน), ลบ/แก้ master data เดิม (P001, S001, C001, C002, V001,
   บัญชี KBANK, users ใดๆ). สร้างของใหม่ได้ตามใจ (เอกสาร, สินค้าใหม่, ลูกค้าใหม่) — co5 คือ playground.
3. RBAC probing = คาดหวัง clean deny (ปุ่มไม่โชว์ / 403 / redirect + ข้อความไทย). 500/crash/stack
   trace/blank page = finding เสมอ. ทำ deny-probe ผ่าน UI URL ตรง (พิมพ์ URL ฟอร์ม) และปุ่มที่โผล่.
4. เครื่องมือ: Playwright headless จาก frontend/ ของ repo (Y:\ClaudePlayground\TEAS-Project\frontend —
   มี @playwright/test + e2e/_helpers.ts เป็น pattern login). เขียน script ชั่วคราวชื่อไม่ชนกัน
   (เช่น frontend/swarm-<user>.mjs) รันด้วย node จาก frontend dir แล้ว **ลบ script ตอนจบ**.
   ห้ามแก้ไฟล์ repo อื่นใดทั้งสิ้น, ห้าม git ทุกคำสั่ง, ห้าม dotnet/build/test.
5. Output เดียวที่เขียน: `swarm-findings/<user>.md` (repo root) + screenshots
   `swarm-findings/shots/<user>-*.png`. Format: ## Done (สิ่งที่ทำ+ผล) / ## Findings (ตาราง:
   severity CRIT/HIGH/MED/LOW, พื้นที่, อาการ, repro, screenshot) / ## Denied-as-expected (RBAC ที่ deny ถูกต้อง).
6. Timebox ~30 นาทีของการขับ UI แล้วเขียนสรุปจบ. Login fail 3 ครั้ง / 503 ติดๆกัน → บันทึกแล้วหยุด.
7. จังหวะคน: click แบบ human-pace (มี wait สั้นๆ), ไม่ยิง loop ถี่ๆ — concurrency มาจาก 10 คนพร้อมกัน
   ไม่ใช่คนเดียวรัว. Console errors เก็บทุกหน้า (page.on('console')/pageerror).

## Missions (ต่างคนต่างสาย)
- **sales01**: สายขายเต็ม: QT ใหม่ (สินค้า P001) → ออก → ตอบรับ → แปลงเป็น SO → DO → IV ถ้าทำได้;
  ทำ 2 รอบขนานเวลากับคนอื่นเพื่อชน doc numbering. Probe: สร้าง/โพสต์ TI, เข้าหน้า settings/users,
  หน้า PV, payroll — คาด deny.
- **ar01**: TI ตรง (ลูกค้า C002 บุคคล) → post → RC; ดู AR aging + เช็ค tie banner = ตารางรวม;
  CN draft (ห้าม post ถ้าระบบเปิดให้ก็ post ได้แต่จดผล). Probe: PO/VI/PV, ปิดงวด ภ.พ.30.
- **purch01**: PO ใหม่ (P001 จำนวนแปลกๆ เช่น 7) → ใครอนุมัติได้? ถ้าตัวเองได้ก็อนุมัติ → mark-sent →
  ปิด PO. ทำ 2 ใบ. Probe: VI post, PV, reports การเงิน, users.
- **ap01**: จาก PO ที่ purch01 อนุมัติ (poll รอ) หรือ PO เดิมในระบบ → VI (หมวด COGS) → PV draft;
  **SoD check สำคัญ: พยายาม approve PV ที่ตัวเองสร้าง — คาดถูกกัน (super-admin เท่านั้นที่ข้ามได้)**.
  ลอง PV มี หัก ณ ที่จ่าย (บริการ S001, WHT 3%). Probe: TI, CN, payroll.
- **appr01**: วนหา drafts ที่คนอื่นสร้าง (PO/PV) แล้วอนุมัติ — นี่คือ race test ตัวจริง (อนุมัติแข่งกับ
  เจ้าของที่กำลังแก้). จดว่าเห็นคิวจากไหน (มี approval inbox ไหม หรือต้องเปิดทีละใบ = UX finding).
  Probe: สร้างเอกสารเอง — role นี้ควรสร้างได้ไหม จดพฤติกรรม.
- **acct01**: งบทดลอง (Dr=Cr ต้องคงอยู่ตลอดที่ฝูงโพสต์เอกสาร — refresh 3-4 รอบระหว่าง session แล้วจด
  ว่า tie ไม่หลุด), JE/GL drill-down, bank recon read, ภ.พ.30 preview (ห้ามยืนยัน). Probe: แก้ master,
  users, payroll mutations.
- **chief01**: รายงานทุกตัว (TB, P&L/BS ถ้ามี, sales-summary, tax-summary, aging ทั้ง AR/AP, bank
  recon) — จดตัวเลขขัดกันข้ามรายงาน ถ้าเจอ. Probe: ปุ่ม admin-only.
- **audit01**: read-only sweep ทุก module; ยืนยันว่าปุ่ม create/แก้/ลบ ไม่โชว์ทั้งหมด; พิมพ์ URL ฟอร์ม
  ตรง (/quotations/new, /payment-vouchers/new, /settings/users ฯลฯ) คาด deny/redirect สวยๆ;
  เปิดเอกสารเดิมดู + พิมพ์ PDF ได้ไหม (ควรได้? จดพฤติกรรม).
- **tax01**: ภ.พ.30 preview เดือน July + ไฟล์ .txt download, ภ.ง.ด.1/3/53 + ใบแนบ views, tax-summary,
  เอกสารแบบฟอร์ม RD. ตรวจเลขกับที่รู้: ภ.พ.30 July = ขาย 13,000/910, ซื้อ 15,000/1,050, เครดิตยกไป 140
  (ถ้าฝูงโพสต์ TI/VI ใหม่ระหว่างนี้ เลขขยับ — จดว่าขยับสอดคล้องเอกสารใหม่ไหม). ห้ามยืนยัน/ปิดงวด.
- **admin01**: master data: สร้างสินค้าใหม่ P002 (ราคา 250), ลูกค้าใหม่ C003, ผู้ขายใหม่ V002; แก้เฉพาะ
  ของที่ตัวเองสร้าง; ดู settings ทุกหน้า; users page — เห็น/ทำอะไรได้บ้างในฐานะ Company Admin (ไม่ใช่
  super) จดให้ละเอียด. Probe: ข้ามบริษัท (company switcher ควรมีแค่ co5).

## Consolidation (Fable)
- [ ] 10 findings files รวม → REPORT-uxswarm-co5.md, dedupe, triage CRIT/HIGH → fix arc ถัดไป
- [ ] Post-swarm sanity (Fable): TB Dr=Cr, ภ.พ.30 สอดคล้องเอกสารใหม่, ไม่มีเอกสารบริษัทอื่นแปลกปลอม
- [ ] cleanup: ลบ swarm scripts ค้าง (ถ้า agent ลืมลบ)

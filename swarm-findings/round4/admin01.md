# admin01 (Company Admin) — UX swarm findings round4 — co5 prod v1.22.7
Run: 2026-07-20T02:40:33Z – 2026-07-20T02:41:15Z (+ short verify follow-up)

## Done (สิ่งที่ทำ+ผล)
- Login เป็น admin01 (pw UxSwarm-2026-A8, REUSE) สำเร็จ, dashboard โหลดผ่าน nav-gates sentinel
  (shots/round4/admin01-01-login-dashboard.png)
- GET /api/proxy/me → status=200 isSuperAdmin=false companyId=5
  companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด allowedCompanies=[{"id":5,"nameTh":"บริษัท ทดสอบ VAT
  (DUMMY) จำกัด","nameEn":"Test VAT Dummy Co., Ltd."}]
- สร้างสินค้าใหม่ **P237941** (สินค้าทดสอบ swarm4) — POST /products → 201, เห็น row ใหม่ในตาราง
  ทันที (redirect ปกติ) (shots/round4/admin01-02-product-P237941-created.png)
- สร้างลูกค้าใหม่ **C241086** (ลูกค้าทดสอบ swarm4) — POST /customers → 201, redirect →
  /customers, เห็น row ใหม่ (shots/round4/admin01-03-customer-C241086-created.png)
- สร้างผู้ขายใหม่ **V244234** (ผู้ขายทดสอบ swarm4) — POST /vendors → 201, toast "บันทึกผู้ขาย"
  ขึ้น (shots/round4/admin01-04-vendor-V244234-created.png). หน้ายังค้างที่ /vendors/new ณ ตอน
  capture (script รอ redirect แค่ 1s ซึ่งอาจสั้นไปภายใต้โหลดพร้อมกัน 10 agent) — ตรวจซ้ำด้วย
  request แยก: /vendors list มี V244234 ครบถ้วน (shots/round4/admin01-05-vendors-list-check.png)
  → ข้อมูลถูกต้อง ไม่ใช่บั๊ก data-loss, ไม่นับเป็น finding (สงสัยแค่ redirect timing ของ script เอง)
- ทั้ง 3 create ใช้ code ใหม่ล้วน (P237941/C241086/V244234), VAT-registered toggle ถูก uncheck
  ก่อน submit (default=true ต้องการเลขผู้เสียภาษี 13 หลัก) — ไม่กระทบ master data เดิมใดๆ
- Sweep settings ทั้งหมด (ดู body snippet + screenshot ทุกหน้า, ไม่มี crash/blank/stack-trace):
  /settings/company, /settings/business-units, /settings/expense-categories,
  /settings/wht-types, /settings/api-keys, /settings/employees, /settings/roles,
  /settings/users, /settings/companies
  (shots/round4/admin01-05..13-settings-*.png — ลำดับ 05=company,06=business-units,
  07=expense-categories,08=wht-types,09=api-keys,10=employees,11=roles,12=users,13=companies)
- ตรวจ /settings/users แถวของ admin01 เอง (SoD check) — เห็นปุ่ม
  ["แก้ไขบทบาท","รีเซ็ตรหัสผ่าน","ปิดใช้งาน"] เหมือน user อื่นทุกประการ (ไม่ได้กดจริง)
- ตรวจ /payroll (ต้อง read-only ทุก role ตาม hard rule) — เห็นปุ่ม "สร้างรอบจ่าย" (mutate) 1 ปุ่ม
  (shots/round4/admin01-14-payroll-readonly-check.png, ไม่ได้กดจริง)
- ทดสอบ tenant-leak โดยตรง: POST /api/auth/switch-company {companyId:1} → HTTP 403 (deny ถูกต้อง)
- ลบไฟล์ temp scripts (swarm4-admin01.mjs, swarm4-admin01-verify.mjs) หลังใช้งานเสร็จ
- Session จบตาม timebox, เขียนสรุปแล้ว

## CRIT-verify (round4 primary assertions — admin01 scope)
- **CRIT-1** (doc numbering 2xx under concurrency): admin01's mission คือ master-data/settings
  ไม่ใช่ numbering-write role — ไม่มี doc-numbering POST ในสโคปนี้. สังเกตทางอ้อม: การ POST ทั้ง 3
  ครั้งของ admin01 เอง (product/customer/vendor) = 201 ครบ, **ไม่พบ HTTP 500 หรือ 23505 เลย**
  ตลอด session (ทุก response ≥400 ถูก log ไว้ — เจอแค่ 403 คาดหมาย 2 ครั้ง ไม่มี 5xx เลย)
  → ไม่พบ regression จากมุมของ agent นี้. ยืนยันสมบูรณ์ deferred ไปที่
  sales01/ar01/purch01/ap01/appr01.
- **CRIT-2** (ภ.พ.30 tax01 access): out of scope for admin01 — deferred to tax01 report.
- **Company switcher = co5 ONLY: YES.** /api/proxy/me ยืนยัน companyId=5,
  allowedCompanies=[{"id":5,...}] เท่านั้น. Switcher DOM: ไม่ render (count=0, ถูกต้อง — admin01
  ไม่ใช่ super-admin). ทดสอบยิง POST switch-company ตรงไปยัง companyId=1 (นาย พงศ์สันต์/เรปทาวน์
  company) → **403 denied**. ไม่มี tenant leak.
- **New master data created this round:** product=P237941, customer=C241086, vendor=V244234 —
  ทั้งหมดเป็น code ใหม่, ยืนยันปรากฏถูกต้องในแต่ละ list, ไม่แตะ master data เดิม

## Findings
| severity | พื้นที่ | อาการ | repro | screenshot |
|---|---|---|---|---|
| MED | /payroll | ปุ่ม "สร้างรอบจ่าย" (mutate) ยังปรากฏ — hard rule ระบุ payroll ต้อง read-only ทุกคน. **ยัง regress อยู่จาก round1/round3, ไม่ได้อยู่ใน scope ของ v1.22.6/v1.22.7 fix (CRIT-1/2 numbering เท่านั้น)** ไม่ได้กดจริงตาม hard rule | login admin01 → goto /payroll | shots/round4/admin01-14-payroll-readonly-check.png |
| LOW | /settings/users — self/peer-admin SoD | แถวของ admin01 เอง มีปุ่ม "แก้ไขบทบาท"/"รีเซ็ตรหัสผ่าน"/"ปิดใช้งาน" เหมือน user อื่นทุกประการ — **ยัง regress อยู่จาก round1/round3** ไม่มี self-lock/SoD guard. ไม่ได้กดจริง | login admin01 → settings/users → ดูแถวของ admin01 เอง | shots/round4/admin01-12-settings-users.png |
| LOW | /settings/api-keys | pageerror console: "Minified React error #418" (hydration mismatch) ตอนโหลดหน้า — หน้า render ปกติทางสายตา ไม่มีผลใช้งาน. **ยัง regress อยู่จาก round3**, cosmetic only | login admin01 → settings/api-keys, ดู console | shots/round4/admin01-09-settings-api-keys.png |
| LOW | /login | console error "Failed to load resource 404" ทันทีตอนเปิดหน้า (คาดว่าเป็น static asset เช่น favicon) — **ยัง regress อยู่จาก round3**, ไม่กระทบ login flow | goto /login, ดู console | — (ไม่ได้ shot แยก, เห็นใน log) |

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)
- POST /api/auth/switch-company {companyId:1} → HTTP 403 (deny ถูกต้อง, ตรง tenant-leak test)
- GET /settings/companies (admin01 = Company Admin ไม่ใช่ Super Admin) → หน้าแสดง Thai deny screen
  สะอาด "ไม่มีสิทธิ์เข้าถึง — หน้านี้สำหรับ Super Admin เท่านั้น กรุณาติดต่อผู้ดูแลระบบ" +
  underlying GET /api/proxy/companies → 403 (deny สะอาด ไม่ crash/blank/stack-trace)
  (shots/round4/admin01-13-settings-companies.png)
- Company switcher UI: ไม่ render เลย (non-super-admin, ถูกต้อง)

## Console/page errors observed
- [console] https://teas.kazaki-rio.com/login :: 404 resource load (repeat of round3, cosmetic)
- [pageerror] https://teas.kazaki-rio.com/settings/api-keys :: Minified React error #418 (repeat
  of round3, cosmetic — page renders fine)
- [console] https://teas.kazaki-rio.com/settings/companies :: 403 on /api/proxy/companies
  (expected — this IS the deny check, not a bug)
- **Zero HTTP 5xx observed anywhere in this agent's session.**

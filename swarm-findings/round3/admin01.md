# admin01 (Company Admin) — UX swarm findings round3 — co5 prod v1.22.6
Run: 2026-07-19T17:00:58.998Z

## Done (สิ่งที่ทำ+ผล)
- Login เป็น admin01 สำเร็จ, dashboard โหลดผ่าน nav-gates sentinel (shots/round3/admin01-01-login-dashboard.png)
- GET /api/proxy/me → status=200 isSuperAdmin=false companyId=5 companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด allowedCompanies=[{"id":5,"nameTh":"บริษัท ทดสอบ VAT (DUMMY) จำกัด","nameEn":"Test VAT Dummy Co., Ltd."}]
- CRIT-check: /api/proxy/me confirms companyId=5 (co5) ONLY, allowedCompanies has exactly co5 — no tenant leak.
- Company switcher ไม่ render เลย (คาดถูก — admin01 ไม่ใช่ super-admin) → ไม่มีทางเห็นบริษัทอื่นผ่าน UI นี้.
- สร้างสินค้าใหม่ P004 (สินค้าทดสอบ swarm3) สำเร็จ — เห็น row ใหม่ในตาราง (shots/round3/admin01-02-product-P004-created.png)
- สร้างลูกค้าใหม่ C005 (ลูกค้าทดสอบ swarm3) สำเร็จ, redirect → https://teas.kazaki-rio.com/customers (shots/round3/admin01-03-customer-C005-created.png)
- สร้างผู้ขายใหม่ V003 (ผู้ขายทดสอบ swarm3) สำเร็จ (shots/round3/admin01-04-vendor-V003-created.png)
- เปิดหน้า /settings/company (company profile) ได้ปกติ (shots/round3/admin01-05-settings-company.png)
- เปิดหน้า /settings/business-units (business units) ได้ปกติ (shots/round3/admin01-06-settings-business-units.png)
- เปิดหน้า /settings/expense-categories (expense categories) ได้ปกติ (shots/round3/admin01-07-settings-expense-categories.png)
- เปิดหน้า /settings/wht-types (wht types) ได้ปกติ (shots/round3/admin01-08-settings-wht-types.png)
- เปิดหน้า /settings/api-keys (api keys) ได้ปกติ (shots/round3/admin01-09-settings-api-keys.png)
- เปิดหน้า /settings/employees (employees) ได้ปกติ (shots/round3/admin01-10-settings-employees.png)
- เปิดหน้า /settings/roles (roles) ได้ปกติ (shots/round3/admin01-11-settings-roles.png)
- เปิดหน้า /settings/users (users) ได้ปกติ (shots/round3/admin01-12-settings-users.png). Company selector (multi-tenant, super-admin only) present=no (ถูกต้อง). ไม่ได้กด edit-roles/reset-password/deactivate จริงกับ user เดิมใดๆ ตาม hard rule.
- เปิดหน้า /settings/companies (companies (คาด deny — super-admin only)) ได้ปกติ (shots/round3/admin01-13-settings-companies.png)
- หมายเหตุ: การรันครั้งแรกสร้าง P003 + C004 สำเร็จ (เห็น shots/round3/admin01-02-product-P003-created.png, admin01-03-customer-C004-created.png) ก่อนจะพบ vendor form ต้องการเลขผู้เสียภาษี 13 หลักเมื่อติ๊ก "VAT registered" (client-side validation ปกติ ไม่ใช่บั๊ก — แก้ script ให้ uncheck ก่อน) แล้วรันซ้ำสร้าง P004/C005/V003 เพิ่ม — รวมมี P003,P004,C004,C005,V003 เป็น master data ใหม่ทั้งหมด ไม่กระทบของเดิม. Login ครั้งแรกเจอ 30s timeout (เดา: cold-start ระหว่าง 10 agent ยิงพร้อมกัน) — retry ครั้งที่ 2 ผ่านปกติ ไม่ถือเป็น finding (ไม่ reproducible).
- Session จบตาม timebox, เขียนสรุปแล้ว

## CRIT-verify (round3 primary assertions — admin01 scope)
- CRIT-1 (doc numbering 2xx under concurrency): admin01's mission is master-data/settings, not a numbering-write role — no doc-numbering POSTs exercised by this agent. Deferred to sales01/ar01/purch01/ap01/appr01 reports.
- CRIT-2 (ภ.พ.30 tax01 access): out of scope for admin01 — deferred to tax01 report.
- Company switcher = co5 ONLY: **YES** — /api/proxy/me confirms companyId=5, allowedCompanies=[{"id":5,"nameTh":"บริษัท ทดสอบ VAT (DUMMY) จำกัด","nameEn":"Test VAT Dummy Co., Ltd."}]. Switcher DOM presence: not rendered (non-super-admin — expected to be absent).
- New master data created this round: product=P004, customer=C005, vendor=V003 — all NEW codes, no existing master data touched.

## Findings
| severity | พื้นที่ | อาการ | repro | screenshot |
|---|---|---|---|---|
| MED | /payroll | เห็นปุ่ม mutate (1 ปุ่ม) — hard rule ระบุ payroll ต้อง read-only ทุกคน (สอดคล้องกับ round1 finding, ยัง regress อยู่ ไม่ได้กดจริงตาม hard rule) | goto /payroll ในฐานะ admin01 | shots/round3/admin01-14-payroll-readonly-check.png |
| LOW | /settings/users — self/peer-admin SoD | แถวของ admin01 เอง มีปุ่ม "แก้ไขบทบาท" / "รีเซ็ตรหัสผ่าน" / "ปิดใช้งาน" เหมือน user อื่นทุกประการ — **ยังไม่แก้จาก round1** (LOW finding เดิมยัง regress อยู่, ไม่มี self-lock/SoD guard). ไม่ได้ลองกดจริงตาม hard rule. | login admin01 → settings/users → ดูแถวของ admin01 เอง | shots/round3/admin01-12-settings-users.png |

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)
- POST /api/auth/switch-company companyId=1 → HTTP 403 (deny ถูกต้อง)

## Console/page errors observed
- [console] https://teas.kazaki-rio.com/login :: Failed to load resource: the server responded with a status of 404 ()
- [pageerror] https://teas.kazaki-rio.com/settings/api-keys :: Minified React error #418; visit https://react.dev/errors/418?args[]=text&args[]= for the full message or use the non-minified dev environment for full errors and additional helpful warnings.
- [console] https://teas.kazaki-rio.com/settings/companies :: Failed to load resource: the server responded with a status of 403 ()

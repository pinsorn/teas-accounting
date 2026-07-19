# admin01 (Company Admin) — UX swarm findings — co5 prod
Run: 2026-07-19T11:11:19.810Z

## Done (สิ่งที่ทำ+ผล)
- Login เป็น admin01 สำเร็จ, dashboard โหลดผ่าน nav-gates sentinel (admin01-01-login-dashboard.png)
- GET /api/proxy/me → isSuperAdmin=false, companyId=5, companyName=บริษัท ทดสอบ VAT (DUMMY) จำกัด, allowedCompanies=[{"id":5,"nameTh":"บริษัท ทดสอบ VAT (DUMMY) จำกัด","nameEn":"Test VAT Dummy Co., Ltd."}]
- P002 มีอยู่แล้ว (จาก run ก่อนหน้า) — ข้าม create, ไปขั้น edit ต่อ
- แก้ไข P002 (ของตัวเอง): ราคา 250 → 260 สำเร็จ (admin01-02-product-p002-edited.png)
- C003 มีอยู่แล้ว (จาก run ก่อนหน้า) — ข้าม create, ไปขั้นเปิด/แก้ไขต่อ
- เปิดหน้ารายละเอียด C003 (id=7) (admin01-03-customer-c003-detail.png)
- แก้ไข C003 (ของตัวเอง): เปลี่ยนเบอร์โทรสำเร็จ (admin01-04-customer-c003-edited.png)
- V002 มีอยู่แล้ว (จาก run ก่อนหน้า) — ข้าม create, ไปขั้นเปิด/แก้ไขต่อ
- แก้ไข V002 (ของตัวเอง): เปลี่ยนชื่ออังกฤษสำเร็จ (admin01-05-vendor-v002-edited.png)
- เปิด /settings/company (company profile) ได้ปกติ (admin01-06-settings-company.png)
- เปิด /settings/business-units (business units) ได้ปกติ (admin01-07-settings-business-units.png)
- เปิด /settings/expense-categories (expense categories) ได้ปกติ (admin01-08-settings-expense-categories.png)
- เปิด /settings/wht-types (wht types) ได้ปกติ (admin01-09-settings-wht-types.png)
- เปิด /settings/api-keys (api keys) ได้ปกติ (admin01-10-settings-api-keys.png)
- เปิด /settings/employees (employees) ได้ปกติ (admin01-11-settings-employees.png)
- เปิด /settings/roles (roles) ได้ปกติ (admin01-12-settings-roles.png)
- /settings/users: admin01 (Company Admin, ไม่ใช่ super) เห็นหน้านี้ได้ (admin01-14-settings-users-list.png). Company selector (multi-tenant, super-admin only) present=no (ถูกต้อง — เห็นแค่บริษัทตัวเอง). เห็นปุ่ม "เพิ่มผู้ใช้"=true, "แก้ไขบทบาท" x10, "รีเซ็ตรหัสผ่าน" x10, "ปิด/เปิดใช้งาน" x10 ต่อ user แถวในบริษัทตัวเอง (co5). ไม่ได้กด edit-roles/reset-password/deactivate จริงกับ user เดิมใดๆ ตาม hard rule.
- เปิด dialog "เพิ่มผู้ใช้" ดู field ที่ต้องกรอก (username/password≥12/fullname/email/roles/active) แล้วกด cancel โดยไม่บันทึก (admin01-15-settings-users-add-dialog.png)
- Session จบตาม timebox, เขียนสรุปแล้ว

## Findings
| severity | พื้นที่ | อาการ | repro | screenshot |
|---|---|---|---|---|
| MED | /payroll | เห็นปุ่ม mutate (สร้างรอบจ่าย) — hard rule ระบุ payroll ต้อง read-only ทุกคน; ไม่ได้กด (ตาม hard rule), flag ให้เจ้าของระบบเช็คว่า Company Admin ควรมีสิทธิ์นี้จริงหรือเป็นช่องโหว่ RBAC | goto /payroll ในฐานะ admin01 | admin01-16-payroll-readonly-check.png |
| LOW | /settings/users — self/peer-admin SoD | แถวของ admin01 เอง (และแถว Company Admin คนอื่นถ้ามี) มีปุ่ม "แก้ไขบทบาท" / "รีเซ็ตรหัสผ่าน" / "ปิดใช้งาน" เหมือน user อื่นทุกประการ — โค้ด (`UsersSettingsPage`) ซ่อนปุ่ม deactivate เฉพาะแถวที่ `isSuperAdmin` เท่านั้น ไม่มีการกันแยกกรณี "แก้ไข/ปิดใช้งานบัญชีตัวเอง" หรือ "Company Admin คนหนึ่งปิดใช้งาน/รีเซ็ตรหัส Company Admin อีกคน" — ไม่ได้ลองกดจริง (จะกระทบ user เดิมตาม hard rule) แต่ควรตรวจสอบว่าตั้งใจให้ทำได้หรือควรมี SoD/self-lock guard | login admin01 → settings/users → ดูแถวของ admin01 เอง | admin01-14-settings-users-list.png |
| LOW | JS runtime (pageerror) | [pageerror] https://teas.kazaki-rio.com/settings/api-keys :: Minified React error #418; visit https://react.dev/errors/418?args[]=text&args[]= for the full message or use the non-minified dev environment for full errors and additional helpful warnings. | สังเกตระหว่าง swarm run — ดู Console errors section | - |

## Denied-as-expected (RBAC ที่ deny ถูกต้อง)
- Company switcher ไม่ render เลย (คาดถูก — admin01 ไม่ใช่ super-admin) → ไม่มีทางเห็นบริษัทอื่นผ่าน UI นี้
- POST /api/auth/switch-company companyId=1 → HTTP 403 (deny ถูกต้อง)
- /settings/companies (companies (คาด deny — super-admin only)) → no-access state แสดงถูกต้อง (admin01-13-settings-companies.png)

## Console/page errors observed
- [console] https://teas.kazaki-rio.com/ :: Failed to load resource: the server responded with a status of 404 ()
- [console] https://teas.kazaki-rio.com/ :: Failed to load resource: the server responded with a status of 404 ()
- [pageerror] https://teas.kazaki-rio.com/settings/api-keys :: Minified React error #418; visit https://react.dev/errors/418?args[]=text&args[]= for the full message or use the non-minified dev environment for full errors and additional helpful warnings.
- [console] https://teas.kazaki-rio.com/settings/companies :: Failed to load resource: the server responded with a status of 403 ()

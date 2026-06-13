# Plan 1 — Role / Permission Admin UI (จัดการสิทธิ์ผ่านหน้าจอ)

> **Status:** DRAFT (Opus 4.8, 2026-06-13). Part of Sprint 13k. Not started.
> **Goal:** ให้ super-admin จัดการ role + grant permission + มอบ role ให้ user ผ่านหน้าจอ
> แทนการแก้ seed SQL มือ. เปลี่ยนสิทธิ์ทุกครั้ง = audit log (§4.8).

## 0. สถานะปัจจุบัน (verified 2026-06-13)
- Entity ครบ: `Role`, `Permission`, `RolePermission`, `User`, `UserRole` (`Domain/Entities/Identity/`).
- ตาราง: `sys.roles`, `sys.permissions`, `sys.role_permissions`, `sys.user_roles`.
- **ไม่มี service/endpoint จัดการเลย** — มีแค่ `GET /me/permissions` (อ่านของตัวเอง).
- Perm `sys.role.manage` + `sys.user.manage` ถูก seed ไว้แล้ว (ยังไม่มีหน้าใช้).
- 12 roles, 66 permission constants (`Authorization/Permissions.cs`).
- เปลี่ยน role/permission ตอนนี้ = แก้ไฟล์ `Migrations/SqlScripts/*.sql` มือ.

## 1. มติ Ham (2026-06-13) — LOCKED
1. ☑ **Role/permission = PER-COMPANY.** แต่ละบริษัทมีชุด role + grant ของตัวเอง (ไม่ใช่ global catalog).
   → ต้องเพิ่ม `company_id` บน `sys.roles` + migration + RLS + copy template ตอนสร้างบริษัท (ดู §1a).
2. ☑ **ขอบเขตการแก้:**
   - **super-admin** → จัดการ role/grant/user ได้**ทุกบริษัท** (เลือกบริษัทที่จะจัดการ).
   - **company-admin** (perm `sys.role.manage` + ไม่ใช่ super) → จัดการได้**เฉพาะบริษัทตัวเอง** (company_id ของ JWT).
3. ◐ **แก้ seeded role ได้ไหม** (Ham ยังไม่เคาะ — ใช้ค่าเริ่มนี้ไปก่อน): role ที่ copy มาเป็นของบริษัทแล้ว
   แก้ grant ได้อิสระ (กระทบเฉพาะบริษัทนั้น). **SUPER_ADMIN role = lock** เสมอ (ห้ามถอด perm/ลบ — กัน lockout).

## 1a. Schema + migration (หัวใจของ per-company)
- **`sys.permissions` = global catalog** คงเดิม (66 codes = app-defined, ไม่ใช่ข้อมูลบริษัท).
- **`sys.roles` += `company_id INT NOT NULL`** (FK `master.companies`) + RLS ENABLE/FORCE + unique
  `(company_id, role_code)`. + `is_system BOOLEAN` (role 12 ตัวที่ copy มาจาก template = true; กัน
  ลบ/rename ตัวหลัก).
- **`sys.role_permissions`** scope ตาม role (role มี company_id แล้ว) → เพิ่ม RLS ผ่าน join หรือ
  denormalize `company_id` ลงไปด้วยเพื่อ RLS ตรง (แนะนำ denormalize + RLS — เร็วกว่า + ตรง §4.7).
- **`sys.user_roles`** — user เป็น per-company อยู่แล้ว; role ก็ per-company → FK ต้องชี้ role ของ
  บริษัทเดียวกัน (CHECK/guard ใน service).
- **Copy-on-create:** `CompanyService.CreateAsync` copy 12 template roles + grants ให้บริษัทใหม่
  (mirror `DefaultWhtTypes`/`DefaultTaxCodes` pattern ที่มีอยู่). Template = ค่า seed ปัจจุบัน
  (ย้ายไป `sys.role_templates`/`role_permission_templates` หรือ hardcode ใน CompanyService).
- ⚠️ **Backfill ข้อมูลเดิม (เสี่ยงสุด — ทำใน migration ระวัง):** roles ปัจจุบัน global, `user_roles`
  ชี้ global role_id. migration ต้อง: (1) สำหรับแต่ละบริษัทที่มี user → สร้าง role copy ของบริษัทนั้น
  จาก global set; (2) remap `user_roles.role_id` เดิม → role copy ของบริษัทที่ user สังกัด; (3) ลบ/retire
  global roles เก่า. **ต้องเขียน data migration + ทดสอบบน snapshot dev DB ก่อน** (มี backup nightly แล้ว).
- **super-admin (มติ Ham 2026-06-13: ☑ ไม่เป็น per-company role):** เป็น system-level ข้ามบริษัท —
  ผูกกับ `is_super_admin` flag บน user/JWT (มีอยู่แล้ว, `PermissionHandler` bypass ทุก perm). ตอน copy
  template ให้บริษัทใหม่ → **ข้าม SUPER_ADMIN role** (ไม่ duplicate ต่อบริษัท). การมอบสถานะ super-admin
  = set flag บน user (นอกขอบเขตหน้า role per-company นี้ — ทำผ่าน user management ระดับ system).

## 2. Phases

> **หลักการ scope ทุก endpoint:** super-admin ส่ง `companyId` มาเลือกบริษัทได้; company-admin —
> service บังคับ `companyId = JWT company` เสมอ (ถ้าส่ง companyId อื่น → 403). ใช้ helper เดียว
> `ResolveTargetCompany(requestedCompanyId)`.

### Phase 0 — Schema + migration + copy-on-create (BE, เสี่ยงสุด — ทำก่อน)
- Migration `AddPerCompanyRoles`: `sys.roles += company_id, is_system` + RLS + denormalize
  `company_id` บน `sys.role_permissions` + RLS. unique `(company_id, role_code)`.
- Data migration (backfill §1a): สร้าง role copy ต่อบริษัทจาก global set + remap `user_roles`.
  **เขียน + รันบน dev DB snapshot (มี nightly backup) ก่อน apply จริง.**
- `CompanyService.CreateAsync` copy template roles+grants ให้บริษัทใหม่ (mirror DefaultWhtTypes).
- **Gate 0:** build 0/0 (สร้าง migration หลัง build, §6) · เดิม Api ≥ baseline · RLS test (role บริษัท A
  มองไม่เห็นจากบริษัท B) · `PermissionLookup` ยัง resolve perm ถูกหลัง remap (login admin ได้ perm ครบ).

### Phase A — BE read surface
- `IRbacAdminService` + `RbacAdminService` (`Application`/`Infrastructure/Identity/`).
- `GET /permissions` → permission catalog global (code + module + ภาษาไทย label). static map ใน C#
  (mirror `Permissions.cs`; label = presentation, ไม่ต้อง migration).
- `GET /roles?companyId` → list ของบริษัทเป้าหมาย (roleCode, nameTh, #users, #perms, isSystem).
- `GET /roles/{id}` → detail + permission codes (role id ผูก company; ตรวจ scope).
- Perm: `sys.role.manage`. **Tenant filter = ResolveTargetCompany** (RLS เป็น backstop).
- **Tests:** company-admin เห็นแค่ role บริษัทตัวเอง · super เห็นได้ทุกบริษัท · non-perm 403 · pass 2×.

### Phase B — BE write (grant/revoke + role CRUD) — per company
- `PUT /roles/{id}/permissions` → whole-set replace. diff → audit `rbac_grant_change` (+company_id).
- `POST /roles` (custom role ในบริษัทเป้าหมาย) · `PUT /roles/{id}` (nameTh) · soft-delete (ถ้าไม่มี user ผูก
  + ไม่ใช่ is_system).
- **Guards:** SUPER_ADMIN role lock; is_system ลบไม่ได้; company-admin ตั้ง role นอกบริษัทตัวเอง → 403.
- **Tests:** grant→revoke audit ถูก+มี company_id · super-admin lock เด้ง · cross-company write 403 · pass 2×.

### Phase C — BE user-role assignment — per company
- `GET /users?companyId` → list users (super: ระบุบริษัท; admin: บริษัทตัวเอง).
- `PUT /users/{id}/roles` → ชุด role ids (whole-set, ต้องเป็น role ของบริษัทเดียวกับ user). audit `user_role_change`.
- Perm: `sys.user.manage`. **company_id filter บังคับ** (§4.7).
- Guard: ห้ามถอด role สุดท้ายของตัวเอง / ห้าม self-downgrade ออกจาก super-admin (กัน lockout).
- **Tests:** cross-company assignment 403 · role-company mismatch เด้ง · self-lockout เด้ง · pass 2×.

### Phase D — FE `/settings/roles` + `/settings/users`
- **Company selector ด้านบน:** super-admin = dropdown เลือกบริษัท; company-admin = ล็อกบริษัทตัวเอง (ซ่อน selector).
- `/settings/roles`: list → detail = checkbox grid จัดกลุ่มตาม module (Master/Sales/Purchase/Tax/Report/Sys),
  Save (whole-set). is_system โชว์ warning; SUPER_ADMIN read-only.
- `/settings/users` (perm `sys.user.manage`): list user + role chips, dialog แก้ role (multi-select, เฉพาะ role
  ของบริษัทนั้น).
- nav: `/settings/roles` + `/settings/users` — โชว์เมื่อมี perm (super หรือ company-admin), ไม่ใช่ superAdminOnly.
- ใช้ pattern `/settings/companies` (page gate ผ่าน `useMePermissions`). i18n th/en + permission label ไทยครบ 66.
- **Gate:** tsc 0 · next build 0/0 · visual (super เลือกบริษัท + admin ล็อกบริษัท).

### Phase E — E2E + gate
- e2e: super-admin สร้าง custom role → grant 2 perms → มอบให้ user → user login เห็น/ทำได้ตามนั้น;
  non-super เปิด `/settings/roles` → ถูก guard.
- openapi: `/permissions`, `/roles*`, `/users*`.
- **Final:** build 0/0 · Api +tests 2× · tsc 0 · next 0/0 · audit rows ยืนยัน · openapi valid.

## 3. Compliance rails
- **§4.7 RLS บน `sys.roles` + `sys.role_permissions`** (company_id) — role บริษัท A ห้ามหลุดไป B
  (backstop นอกเหนือ service filter). เพิ่ม RLS test.
- ทุกการเปลี่ยน grant/role/user-role → `audit.activity_log` + company_id (§4.8, ห้ามลบ).
- SUPER_ADMIN ห้าม lockout (lock perm + self-downgrade guard).
- company-admin เขียนได้เฉพาะบริษัทตัวเอง; super-admin ข้ามได้ — บังคับที่ service (`ResolveTargetCompany`).
- `sys.role.manage`/`sys.user.manage` เท่านั้น.
- ห้าม `dotnet ef --no-build` (§6); migration + data-backfill commit คู่ code; ทดสอบ backfill บน dev snapshot ก่อน.

## 4. ประมาณการ: 2–3 session (Phase 0 migration+backfill ~1 [เสี่ยงสุด], BE A–C ~1, FE D ~1).
   **ใหญ่กว่าเดิมเพราะ per-company = schema + data migration + copy-on-create + dual-scope auth.**
## 5. เกี่ยวกับ Plan 2: หน้านี้ทำให้ matrix แก้ได้ผ่าน UI — Plan 2 (Cartesian audit) คือตัว
   **ทดสอบ** ว่า matrix ที่ตั้ง (ไม่ว่าจาก seed หรือ UI นี้) ถูกบังคับใช้จริงทุกคู่.

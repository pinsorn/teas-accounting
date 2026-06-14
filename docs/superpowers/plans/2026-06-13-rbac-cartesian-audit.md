# Plan 2 — RBAC Full Cartesian Audit (ไล่เช็คทุกคู่ บทบาท × endpoint)

> **Status:** ✅ EXECUTED (Opus 4.8, 2026-06-14). Phases A–F done. Artefacts: `docs/rbac/endpoint-permission-map.generated.md`
> (255 routes), `docs/rbac/role-permission-matrix.md` (source of truth), tests `RbacAuthMapTests`/`RbacMatrixTests`/
> `RbacCartesianTests` (green ×2 on teas_test), seed `530_seed_rbac_grant_reconcile.sql`, periods-status auth fix, FE nav
> permission-gating. Findings for Ham: see §0a strike-through + the matrix doc SoD section. See `progress.md` (top).
> **Goal:** พิสูจน์ว่า **ทุกคู่ (role × endpoint ที่มี permission)** บังคับสิทธิ์ถูกต้อง —
> allow ที่ควร allow, 403 ที่ควรปฏิเสธ. หาช่องโหว่ (over-grant / missing-grant) แล้วแก้.

## 0. สถานะ (verified 2026-06-13)
- 12 roles · 66 permission constants · **63 endpoints** ติด `RequireAuthorization(perm:…)`.
- Matrix เต็ม = 12 × 63 ≈ **756 คู่** ที่ต้องมีคำตอบ allow/deny ชัดเจน.
- การ enforce: `PermissionHandler` — super-admin bypass; ไม่งั้นต้องมี claim `perm` ตรง.
- Test เดิม = จุดๆ (`payment-voucher-non-super-rbac`, `pv-approval-permission`, `rbac-chapter3`) —
  ไม่ครบ ไม่ใช่ Cartesian.
- **ยังไม่มี matrix ที่เป็น source of truth** ว่า role ไหน "ควร" ได้ endpoint ไหน.

## 0a. Deltas จาก Plan 1 (shipped 2026-06-14, commit b8b4773 — ต้องอัพเดทก่อนเริ่ม)
- **Endpoints เพิ่ม:** +9 operations `/admin/rbac/*` (GET permissions · GET/POST roles · GET/PUT/DELETE roles/{id} ·
  PUT roles/{id}/permissions · GET users · PUT users/{id}/roles) gated `sys.role.manage` / `sys.user.manage`.
  → endpoint count **63 → ~72**, matrix **~756 → ~864 คู่**. Phase A generator จะ scan เจอเอง (ไม่ต้องแก้มือ).
- **`sys.permissions` 52 → 66:** `520_seed_missing_permission_codes.sql` insert 14 codes ที่ enforce บน endpoint
  แต่เดิมไม่เคย seed (`gl.period.close`, `sales.{receipt,credit_note,debit_note}.{create,post}`, `purchase.wht.read`,
  `tax.{vat_register,pnd30,pnd3,pnd53}.read`, `report.{trial_balance,profit_loss}.read`). ตอนนี้ catalog grantable ครบ.
- ~~**⚠️ มติ Ham 2026-06-14 — 14 perm นี้ grant ให้ SUPER_ADMIN เท่านั้น (ไม่ผูก default role อื่น):** การจัดการ
  ทำผ่าน admin UI (Plan 1). **Phase B/D ต้องถือว่า expected = ✗ สำหรับ non-super** — ไม่ flag เป็น under-grant
  finding (เช่น AR_CLERK สร้าง receipt ผ่าน HTTP ไม่ได้ = by design จนกว่า admin จะ grant ใน UI). matrix doc
  ต้อง encode ข้อนี้ชัด.~~
  > **❗STRUCK 2026-06-14 (Phase D execution) — this was a MISDIAGNOSIS, not a real policy.** The audit
  > traced the super-only state to a **seed-ordering bug**: `320`/`330` *intended* to grant the 6 sales
  > receipt/CN/DN create+post pairs to ACCOUNTANT/AR_CLERK/CHIEF_ACCOUNTANT/COMPANY_ADMIN, but their
  > grant statements ran **before** `520` inserted the permission codes into `sys.permissions`, so the
  > `JOIN sys.permissions` matched nothing and the grants silently no-op'd. `520` then granted the codes
  > to SUPER_ADMIN only, which got rationalised as "default-unassigned, grant via UI". The other 8 codes
  > (period.close, wht.read, the 4 tax reads, the 2 report reads) plus `sales.tax_invoice.create/post`,
  > `gl.journal.*`, `master.vendor.manage`, `report.audit.read`, PO-create were simply **never granted to
  > any role** — e.g. TAX_OFFICER could read zero tax reports, AR_CLERK could not issue a tax invoice.
  > **`530_seed_rbac_grant_reconcile.sql` restores all of these per each role's documented purpose**
  > (every grant justified in the file header + `docs/rbac/role-permission-matrix.md`). The ONLY
  > permission left SUPER_ADMIN-only is `master.company.manage` (§4.6). Ham authorised this 2026-06-14
  > ("แก้ได้เลย ถ้าอันไหนไม่สมเหตุสมผล"); **Ham may still veto** any specific grant on return — see the
  > matrix doc's SoD section + the `sys.role.manage`/`sys.user.manage`→COMPANY_ADMIN line item (530 §D).
- **403 ใช้ได้จริงบน root/BFF endpoints แล้ว:** `DomainExceptionMiddleware` เปลี่ยนเป็น `StatusFor(code)` →
  `*.scope_required`→403, `*.not_found`→404 (เดิม root hardcode 422). cross-company isolation assertion (§2a)
  ตอนนี้คืน 403 จริง. (permission-gate denial ยังเป็น 403 จาก ASP.NET auth เหมือนเดิม — ไม่กระทบ Phase C core.)
- **Roles เป็น per-company จริงแล้ว** (Plan 1): SUPER_ADMIN = global เดียว (company_id NULL), 11 roles/บริษัท
  copy จาก template เดียว. matrix canonical = `role_code × permission` (template). Cartesian สร้าง JWT จากบริษัทอ้างอิง.
- **Test ใหม่ที่มีแล้ว:** `RbacAdminServiceTests` (24, service+DB) + e2e `rbac-admin.spec.ts` — ยังเป็น CRUD/scope ของ
  admin API ไม่ใช่ Cartesian enforcement; Plan 2 ยังต้องทำ.

## 1. แนวทาง (data-driven test, ไม่ใช่ไล่มือ 756 ครั้ง)
หัวใจ = สร้าง **matrix เดียวเป็น source of truth** แล้วเขียน test ตัวเดียวที่วน matrix นั้น.

### Phase A — สกัด endpoint→permission map อัตโนมัติ
- เขียน reflection/scan: ไล่ทุก endpoint หา `RequireAuthorization("perm:X")` → ได้ตาราง
  `(method, route, requiredPermission)`. Output → `docs/rbac/endpoint-permission-map.generated.md`.
- วิธี: integration test ที่อ่าน `EndpointDataSource` (DI) → ดึง `AuthorizeAttribute`/policy ของแต่ละ
  endpoint → dump. (ทำครั้งเดียว เป็น generator; ไม่ผูกกับ runtime).
- **ประโยชน์:** จับ endpoint ที่ "ลืมใส่ permission" (เหลือแค่ `RequireAuthorization()` เปล่า = แค่ login ก็เข้าได้)
  → flag เป็น finding ทันที.

### Phase B — สร้าง expected matrix (role × permission)
- Source = `sys.role_permissions` (ของจริงใน DB) — ดึงออกมาเป็น `role → [permissions]`.
- ประกาศ **expected matrix** เป็นไฟล์ data ใน repo: `docs/rbac/role-permission-matrix.md`
  (12 roles × 66 perms, ✓/✗) — เป็นเอกสารที่ "มนุษย์รีวิวได้" + Ham เคาะได้.
- เทียบ expected (ไฟล์) vs actual (DB seed) → mismatch = finding (seed ผิด หรือ matrix ผิด).

### Phase C — Cartesian enforcement test (ตัวเอก)
- `RbacCartesianTests.cs` (integration, teas_test). สำหรับ **แต่ละ role**:
  1. สร้าง JWT ของ role นั้น (perms จาก DB) ผ่าน `JwtTokenIssuer` — ไม่ต้อง seed user 12 ตัว.
  2. ยิงทุก endpoint จาก map (Phase A) ด้วย request ที่ valid พอจะผ่าน auth-layer
     (ใช้ payload ขั้นต่ำ / id ที่ไม่มีจริง — สนใจแค่ **401/403 vs ไม่ใช่ 403**, ไม่สน 200/404/422).
  3. assert: endpoint ที่ role มี perm → **ไม่ใช่ 403**; ที่ไม่มี perm → **403**.
- super-admin: ทุก endpoint ไม่ใช่ 403.
- API-key path แยกเทส (scopes ไม่ใช่ role; ไม่มี super bypass).
- **กับดักที่ต้องระวัง:** บาง endpoint เช็คสิทธิ์แบบ `RequireAssertion` (OR หลาย perm เช่น CN/DN
  create|post) → map ต้องเก็บเป็น "set ใด ๆ" ไม่ใช่ perm เดียว. Phase A ต้อง handle assertion-based.

### Phase D — แก้ของที่เจอ
- Finding ที่คาดจะเจอ:
  - endpoint ลืม permission (เปล่า/แค่ authn) → ใส่ perm ให้ถูก.
  - role ได้ perm เกิน (over-grant) → ถอดใน seed ใหม่.
  - role ขาด perm ที่ควรได้ → เพิ่ม seed. **แต่ระวัง:** 14 perm จาก §0a (receipt/CN/DN create+post ฯลฯ)
    เป็น default-unassigned ตั้งใจ (มติ Ham) — ไม่นับเป็น under-grant; grant ผ่าน UI ถ้าต้องการ.
- ทุกการแก้ grant = seed SQL ใหม่ (idempotent) + migration ถ้าจำเป็น.
- แก้แล้ว rerun Cartesian test จนเขียว.

### Phase E — FE gate sweep (ผูกกับผล Cartesian)
- ตอนนี้ FE ใช้ `useMePermissions()` แค่ 3 ที่ → ปุ่ม/เมนูหลายที่โชว์ให้คนไม่มีสิทธิ์ (กดแล้ว 403).
- ใช้ endpoint-permission map (Phase A) เป็น checklist: ทุกปุ่มที่ยิง endpoint มี-perm ควรซ่อน/disable
  เมื่อ user ไม่มี perm. ทำ helper `useHasPerm(code)` + ไล่ใส่ปุ่มหลัก (create/post/approve/delete).
- ขอบเขต: ปุ่ม action หลัก + nav (ไม่ต้องครบ 100% ทุก micro-control — UX-grade, BE คือ enforcement จริง).

### Phase F — gate + เอกสาร
- `docs/rbac/role-permission-matrix.md` = source of truth (Ham รีวิว/เคาะ).
- `RbacCartesianTests` เขียว = guard ถาวร (CI จับ regression ถ้าใครเพิ่ม endpoint ลืม perm).
- **Final:** build 0/0 · Cartesian test pass 2× บน teas_test · endpoint map generated · matrix doc reviewed ·
  FE tsc 0/next 0/0 · seed แก้ commit คู่.

## 2. Compliance / SoD ที่ต้องยืนยันใน matrix
- SoD: `payment.voucher.approve` ≠ `payment.voucher.create` role เดียวกันห้ามมีทั้งคู่ (ม.SoD §12.1) —
  matrix ต้อง flag ถ้าเจอ role ถือทั้งสอง.
- TI/CN/DN post = เฉพาะ accountant tier; sales-staff อ่านได้ post ไม่ได้.
- tax filing finalize (`tax.filing.finalize`) = chief/tax-officer เท่านั้น.
- super-admin bypass = by design (CLAUDE.md §4.1) — test ยืนยัน bypass แต่ audit ทุก sensitive action.

## 2a. ผลกระทบจาก per-company roles (มติ Ham 2026-06-13)
- Role เป็น per-company แล้ว แต่ทุกบริษัท copy จาก **template เดียวกัน** → matrix canonical ยังเป็น
  ระดับ template (`role_code × permission`). Cartesian test สร้าง JWT จาก grant ของบริษัทอ้างอิงหนึ่ง.
- **เพิ่ม dimension ที่ต้องเทสต์:** cross-company isolation (§4.7) — company-admin บริษัท A เรียก
  `/roles?companyId=B`, `PUT /users/{userของB}/roles` → ต้อง 403. รวมใน Cartesian suite.
- ถ้าทำ Plan 1 (per-company) ก่อน: matrix doc = template; Cartesian test ครอบทั้ง role-perm enforcement
  + company-scope enforcement.

## 3. ลำดับกับ Plan 1
- **Plan 1 (UI) ☑ DONE แล้ว (2026-06-14).** Plan 2 ทำได้เลยบน per-company schema ปัจจุบัน — กลายเป็น
  regression guard ให้ UI: แก้ grant ผ่าน UI แล้วถ้า matrix เพี้ยน Cartesian test จับได้.
- matrix doc (Phase B) = source of truth ที่ "ของจริงควรเป็น"; เทียบกับ `sys.role_permissions` (ที่ UI/seed เซ็ต).
- **ทำตามลำดับ Phase A → B → C → D → E → F** ได้เลย (ไม่มี dependency ค้างจาก Plan 1 อีก).

## 4. ประมาณการ: 2 session (A–D ~1.5, E–F ~0.5). ใหญ่สุดคือ matrix review + แก้ over/under-grant.

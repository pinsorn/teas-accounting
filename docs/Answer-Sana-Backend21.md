# Sprint 13d — Settings UX hardening + Company Profile (Phase 1)

**Owner**: Claude Code
**Spec author**: Sana (via Sprint 13b live capture findings, 2026-05-19)
**Runs**: Parallel with Sprint 13b (manual capture continues; this sprint is independent FE+BE work)
**ROI**: 2-3 days

---

## Background

ระหว่าง Sprint 13b live capture ผ่าน Chrome MCP ที่หน้า `/settings/*` —
Sana ทดสอบ submit จริงทุก modal และเจอ 5 bugs + 1 design gap. Sprint 13d
รวมทุกอย่างเข้าด้วยกัน + เพิ่ม Company Profile (Phase 1) เพราะเป็น
prerequisite ของหลายอย่าง (เลขผู้เสียภาษีบริษัทจะถูก embed ในใบกำกับภาษี
ทุกใบ — ปัจจุบันต้อง seed ผ่าน DB ตอน deploy).

**Live evidence**: ทุก bug ถูก reproduce จริงผ่าน Chrome MCP บน
`http://localhost:3000`. cURL traces + screenshots พร้อมใน Sprint 13b
session log.

---

## P1 — Replace `window.confirm()` with custom AlertDialog

### Problem
ปุ่ม "ปิดใช้งาน" ใน `/settings/business-units` (และน่าจะอีกหลายหน้า
master data) trigger `window.confirm()` แบบ native ของ browser.

```
Browser dialog: "localhost:3000 says
                 ปิดใช้งานหน่วยธุรกิจนี้? เอกสารเดิมยังอ้างอิงได้
                 [OK] [Cancel]"
```

3 ปัญหา:
1. **Block automation**: Chrome MCP / Playwright ค้างเพราะ native dialog
   ไม่ใช่ DOM element. Sprint 13b walkthroughs จะ run ไม่ได้.
2. **Brand violation**: "localhost:3000 says" prefix browser ใส่เอง —
   แก้/แปลไม่ได้. ดูไม่ professional.
3. **i18n + a11y**: Native dialog ใช้ภาษา OS — ผู้ใช้ EN/ลูกค้าต่างประเทศ
   จะเห็นปุ่ม "OK"/"Cancel" เป็นภาษา system. Screen reader support
   ก็ต่างกันตาม browser.

### Fix
สร้าง shared `AlertDialog` component (ใช้ shadcn/ui มีอยู่แล้ว) ที่
รองรับ:
- title + description (รับ ReactNode สำหรับ markup ที่ซับซ้อน)
- 2 buttons: `confirmText` (default "ตกลง" / "Confirm") + `cancelText`
  (default "ยกเลิก" / "Cancel")
- `variant: 'default' | 'destructive'` (destructive = ปุ่ม confirm สีแดง)
- onConfirm callback (async — แสดง loading spinner ระหว่างรอ response)
- Escape key + click overlay = cancel

### Files (FE)
- `frontend/components/ui/alert-dialog.tsx` (new — ใช้ shadcn primitives)
- `frontend/hooks/useConfirm.ts` (new — promise-based wrapper:
  `const ok = await confirm({title, description, variant: 'destructive'})`)
- Audit all `window.confirm(` callers — replace with `useConfirm`. ที่
  Sana เห็นแน่ ๆ ใน Sprint 13b: `/settings/business-units` (disable row).
  ตรวจเพิ่ม: products, wht-types, vendors, products list, vendor-invoices
  "ลบ draft", payment-vouchers "ปฏิเสธ approval", api-keys "revoke".

### Acceptance
- `grep -r "window.confirm\|confirm(" frontend/app frontend/components` →
  0 hits (ยกเว้น dev tooling / tests)
- Chrome MCP สามารถคลิกปุ่ม destructive ได้โดยไม่ค้าง
- Sprint 13b walkthrough script `02.01-business-units.ts` step 6 ทำงาน
  ผ่าน (page.on('dialog') workaround ถอดออก)

---

## P2 — Show proper error state for 403 (RBAC denied)

### Problem
หน้า `/settings/api-keys` กับ ACCOUNTANT role (ไม่มี admin scope):
- GET `/api/proxy/api-keys` → **403** (4 ครั้งติด)
- UI render: **"ไม่มีข้อมูล"** (เหมือนว่ารายการว่าง)
- ผู้ใช้คิดว่ายังไม่มี key ในระบบ → ไปกด "+ สร้าง API key" → modal เปิด →
  กรอกเสร็จกด save → 403 อีก (ใช้เวลาฟรี ๆ)

นี่เป็น pattern ที่หลายหน้าน่าจะมี — TanStack Query ที่ fail จะคืน
empty data ให้ component ถ้าไม่ check error state.

### Fix
สร้าง shared `<RoleGuardedTable>` หรือ HOC ที่:
- ตรวจ query error: ถ้า `error.status === 403` → render `<NoAccessState>`
  ที่อธิบายว่า "ต้องสิทธิ์ admin เพื่อดู/แก้ไขข้อมูลในหน้านี้ — ติดต่อผู้ดูแลระบบ"
- ถ้า `error.status === 401` → trigger logout (token expired)
- ถ้า error อื่น → render `<ErrorState>` พร้อมปุ่ม retry
- empty data + 200 success → render `<EmptyState>` ปกติ ("ไม่มีข้อมูล")

### Files
- `frontend/components/states/NoAccessState.tsx` (new)
- `frontend/components/states/ErrorState.tsx` (new — ถ้ายังไม่มี)
- `frontend/components/states/EmptyState.tsx` (อาจมีอยู่แล้ว — รวม pattern)
- Update list pages ที่ใช้ query: api-keys, wht-types (กรณี admin-only
  list ในอนาคต), users (เมื่อมี), audit-log

### Acceptance
- Login เป็น demo-accountant + เข้า `/settings/api-keys` → เห็น
  "ต้องสิทธิ์ admin" + ปุ่ม "+ สร้าง API key" disabled/ซ่อน
- Network tab: GET 403 ครั้งเดียว (ไม่ retry infinite loop)
- E2E test ใหม่: `403_shows_no_access_state.spec.ts`

---

## P3 — Hide create/edit buttons when user lacks scope

### Problem
หน้า `/settings/wht-types`:
- GET ทำงาน (203 returns rows)
- "+ เพิ่มประเภท" button **ปรากฏให้ทุก role**
- modal เปิดได้ทุกคน → user กรอกครบ → กด "บันทึก" → **POST 403**
- ผู้ใช้เสียเวลากรอกฟรี ๆ + ไม่รู้ว่าต้องรู้ใคร / ติดต่อ admin ที่ไหน

นี่ต่างจาก P2 (403 ที่ GET) — อันนี้ 403 ที่ POST/PUT/DELETE.

### Fix
1. BE: ออก endpoint `/api/proxy/me/permissions` ที่คืน scopes ของ user
   ปัจจุบัน เช่น `["bu.read", "bu.write", "wht-types.read", ...]`
   (ดึงจาก roles ที่ผูกกับ user → flatten เป็น scopes)
2. FE: shared hook `usePermissions()` ที่ cache permissions ใน
   TanStack Query (long-lived, invalidate ตอน logout)
3. FE: ทุก action button ที่ trigger write → wrap ด้วย:
   ```tsx
   <PermissionGate scope="wht-types.write">
     <Button>+ เพิ่มประเภท</Button>
   </PermissionGate>
   ```
   ถ้า scope ไม่มี → button hidden completely (ไม่ใช่ disabled — กัน user
   inspect element + click)
4. FE: ปุ่ม "✏️ แก้ไข" + "ปิดใช้งาน" ใน table rows ก็ต้องผ่าน gate

### Files
- `backend/.../Api/Controllers/MeController.cs` (new — `/api/v1/me/permissions`)
- `frontend/hooks/usePermissions.ts` (new)
- `frontend/components/PermissionGate.tsx` (new)
- Update `/settings/wht-types`, `/settings/api-keys`, `/settings/business-units`,
  `/settings/products` — wrap create/edit/disable buttons

### Acceptance
- demo-accountant ที่ `/settings/wht-types` → ไม่เห็นปุ่ม "+ เพิ่มประเภท"
- demo-accountant ที่ `/settings/business-units` → เห็นปุ่ม + กดได้ปกติ
- E2E test ใหม่: `permission_gate_hides_buttons.spec.ts`

---

## P4 — Restore button for inactive rows

### Problem
หลังกด "ปิดใช้งาน" ที่ row ใน BU/Product/WHT:
- Row ยังโชว์ในตาราง (status column = "—")
- Action column เหลือเฉพาะ "✏️ แก้ไข" — **ไม่มีปุ่ม "เปิดใช้งานใหม่"**
- ผู้ใช้ที่ disable ผิด → กลับมา enable ผ่าน UI ไม่ได้ — ต้องเรียก API
  เอง: `PUT /business-units/{id}` พร้อม `{isActive: true}` หรือ
  ติดต่อ DBA

### Fix
Action column ตรวจ `row.isActive`:
- `isActive: true` → แสดง: `[✏️ แก้ไข] [ปิดใช้งาน]`
- `isActive: false` → แสดง: `[✏️ แก้ไข] [↺ เปิดใช้งานใหม่]`
  (ใช้ใหม่ → PUT isActive=true → toast "เปิดใช้งานแล้ว" → table refresh)

BE: PUT รับ isActive flag อยู่แล้ว (verified live). ไม่ต้องเพิ่ม endpoint.

### Files (FE only)
- `frontend/app/(dashboard)/settings/business-units/page.tsx`
- `frontend/app/(dashboard)/settings/products/page.tsx`
- `frontend/app/(dashboard)/settings/wht-types/page.tsx`
- (และ vendors / products list ถ้ามี pattern เดียวกัน)

### Acceptance
- กด "ปิดใช้งาน" บน SHOP row → row ยังโชว์, action เปลี่ยนเป็น
  "↺ เปิดใช้งานใหม่"
- กด "↺ เปิดใช้งานใหม่" → toast + row กลับเป็น ✓ ใช้งาน
- ไม่ใช้ window.confirm (P1) — ใช้ AlertDialog ถ้าจะ confirm

---

## P5 — Unify error envelope (FluentValidation → v1)

### Problem
ปัจจุบันมี 2 envelope shapes:

**(a) Business rule errors** — ใช้ ErrorEnvelopeV1 ตาม plan §20.7 ✓
```json
{
  "type": "urn:teas:error:bu.duplicate",
  "title": "bu.duplicate",
  "detail": "Business Unit code 'SHOP' already exists.",
  "status": 422
}
```

**(b) Validation errors** — ใช้ default ASP.NET ModelState (RFC 9110) ✗
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Code": ["'Code' must not be empty.",
             "Code must be uppercase letters/digits, ≤20 chars."]
  }
}
```

Frontend ต้อง handle 2 shapes → branching code ทุก mutation. risk
ของ missing field-level errors ใน UI สูง.

### Design
แก้ที่ BE — wrap ModelState error เป็น v1 envelope พร้อม extension
`fieldErrors`:

```json
{
  "type": "urn:teas:error:validation",
  "title": "validation",
  "detail": "Request validation failed (2 fields).",
  "status": 400,
  "fieldErrors": [
    { "field": "code", "messages": ["validation.required", "validation.code.format"] },
    { "field": "nameTh", "messages": ["validation.required"] }
  ]
}
```

- `field` ใช้ camelCase (ตรงกับ JSON shape ที่ FE ส่ง — ปัจจุบัน BE
  ส่งกลับเป็น PascalCase "Code" ต้อง map)
- `messages` เป็น **i18n keys** ไม่ใช่ literal English text — frontend
  จะ resolve เป็นภาษาที่เลือก (ปัจจุบัน FluentValidation hardcode EN)

### Files (BE)
- `backend/.../Api/Middleware/ValidationErrorEnvelopeMiddleware.cs` (new)
  — intercept ProblemDetails จาก ApiController + reshape เป็น v1
- `backend/.../Api/Filters/ValidationFilter.cs` หรือ Program.cs:
  `services.AddControllers().ConfigureApiBehaviorOptions(o => o.SuppressModelStateInvalidFilter = true)` แล้ว validate manually ผ่าน
  middleware
- FluentValidation messages — เปลี่ยนเป็น i18n keys ทุกที่:
  ```csharp
  RuleFor(x => x.Code).NotEmpty().WithMessage("validation.required");
  ```
- `frontend/lib/api/errors.ts` — ปัจจุบันต้อง handle 2 shapes;
  หลัง fix → handle แค่ v1
- `frontend/lib/i18n/validation.ts` — เพิ่ม dictionary สำหรับ i18n keys

### Files (Sana owns — to be updated AFTER merge)
- `docs/accounting-system-plan.md` §20.7 — เพิ่ม `fieldErrors[]` schema
  ใน ErrorEnvelopeV1
- `docs/api/openapi.yaml` — components/schemas/ErrorEnvelopeV1 เพิ่ม
  fieldErrors

### Acceptance
- POST `/business-units` พร้อม blank code → 400 + v1 envelope พร้อม
  `fieldErrors[]`
- E2E: blank submit → toast แสดงข้อความที่ถูก resolve เป็นภาษาไทย
  ("กรุณากรอกรหัส", ไม่ใช่ "'Code' must not be empty.")
- E2E: invalid format → field-level error แสดงใต้ input ตรง field

---

## P6 — Company Profile (Phase 1: hybrid lock model)

### Problem
`/settings/company` ปัจจุบัน **return 404**. ข้อมูลบริษัท (legal name,
tax ID, registered address) ต้อง seed ผ่าน DB script ตอน deploy —
**ผู้ใช้ปกติเปลี่ยนแก้ผ่าน UI ไม่ได้เลย**. แต่ tax ID + ที่อยู่ของบริษัท
ถูก embed ในใบกำกับภาษีทุกใบ (กฎหมายบังคับให้ตรงกับ ภ.พ.20).

### Design rationale

Sana พิจารณา 4 ทางเลือก:

| ทางเลือก | ข้อดี | ข้อเสีย |
|---|---|---|
| (a) Read-only ตลอด | Compliance ง่ายสุด | ไม่ practical — บริษัทย้ายจริง, แก้พิมพ์ผิด |
| (b) Full effective-date history (เหมือน WHT rate) | Audit-perfect | Over-engineered สำหรับ Phase 1 |
| (c) Edit ได้แต่ต้อง 2-person approval | Strict + practical | UX ซับซ้อน |
| (d) แยก soft/hard fields | Pragmatic | ต้อง classify fields |

**Sana เลือก: (d) + (c) hybrid — Phase 1 ทำ (d) ก่อน**:

**Soft fields** (admin role แก้ได้เลย):
- ชื่อทางการค้า (Trade name — ไม่ใช่ legal name)
- โลโก้บริษัท (logo image, ใช้บนเอกสาร)
- เบอร์ติดต่อ, อีเมล, เว็บไซต์
- ชื่อผู้ติดต่อ (contact name สำหรับลูกค้า)
- Banking info (สำหรับ payment instructions)

**Hard fields** (read-only ใน Phase 1 — Phase 2 จะเพิ่ม 2-person approval):
- ชื่อนิติบุคคล (legal entity name ตาม DBD)
- เลขทะเบียนนิติบุคคล (13-digit)
- เลขประจำตัวผู้เสียภาษี (13-digit — ส่วนใหญ่ = เลขทะเบียน)
- ที่อยู่จดทะเบียน (registered address ตาม ภ.พ.20)
- VAT registration date
- สาขา (head office vs branch — บางบริษัทมีหลายสาขา)

UI strategy:
- Hard fields: render เป็น **disabled inputs** พร้อม tooltip
  "การเปลี่ยนข้อมูลนี้ต้องผ่านขั้นตอนพิเศษ — ติดต่อผู้ดูแลระบบหรือยื่น
  ภ.พ.09 ก่อน" (i18n key)
- Soft fields: editable normal
- Banner ด้านบน: "การเปลี่ยนข้อมูลทางกฎหมายของบริษัทควรอัปเดต ภ.พ.20
  ที่กรมสรรพากรก่อน (ภ.พ.09)"

### Phase 1 scope (this sprint)

**DB**:
```sql
-- One row per company_id (1:1)
CREATE TABLE company_profile (
  company_id INT PRIMARY KEY REFERENCES companies(company_id),
  -- Hard fields (read-only via UI in Phase 1)
  legal_name VARCHAR(200) NOT NULL,
  tax_id CHAR(13) NOT NULL,
  registration_number CHAR(13),
  registered_address_line1 VARCHAR(200) NOT NULL,
  registered_address_line2 VARCHAR(200),
  registered_subdistrict VARCHAR(100),
  registered_district VARCHAR(100),
  registered_province VARCHAR(100) NOT NULL,
  registered_postal_code CHAR(5) NOT NULL,
  vat_registration_date DATE,
  branch_code VARCHAR(10) DEFAULT '00000',  -- head office
  -- Soft fields (editable)
  trade_name VARCHAR(200),
  logo_url VARCHAR(500),
  phone VARCHAR(50),
  email VARCHAR(200),
  website VARCHAR(200),
  contact_name VARCHAR(200),
  bank_name VARCHAR(100),
  bank_account_no VARCHAR(50),
  bank_account_name VARCHAR(200),
  -- Audit
  created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
  updated_by_user_id INT
);
```

Migration: convert existing company-row data ที่อยู่ใน `companies` table
(ถ้ามี) มา populate `company_profile`. ถ้ายังไม่มี — seed default จาก
manual-demo (ใช้ข้อมูลตัวอย่างของ "บริษัท ปลาทอง จำกัด" หรือ name อะไรก็ได้
ที่ตรงกับ context manual-demo).

**Backend endpoints**:
- `GET /api/v1/company-profile` — returns full profile (auth required,
  any role can read for header rendering)
- `PUT /api/v1/company-profile/soft` — update soft fields (admin role)
- `PUT /api/v1/company-profile/hard` — **return 501 Not Implemented** ใน
  Phase 1, พร้อม body อธิบายว่า Phase 2 จะรองรับ + ต้องผ่าน 2-person flow

**Frontend page**: `/settings/company`
- 2 sections ในหน้าเดียว: "ข้อมูลทางกฎหมาย" (hard, disabled) +
  "ข้อมูลติดต่อ + การชำระเงิน" (soft, editable)
- Logo upload (ผ่าน Sprint 11 file-attachment infra)
- Save แยกฟอร์ม (ฟอร์ม hard ไม่มีปุ่ม save — read-only)

**Sidebar**:
- เพิ่ม link "ข้อมูลบริษัท" ในกลุ่ม "ตั้งค่า" — เป็น link แรกของกลุ่ม

### Files
- `backend/.../Migrations/{timestamp}_AddCompanyProfile.cs` + `.sql`
- `backend/.../Models/CompanyProfile.cs` + Entity config
- `backend/.../Api/Controllers/CompanyProfileController.cs`
- `frontend/app/(dashboard)/settings/company/page.tsx` (new)
- `frontend/app/api/proxy/company-profile/route.ts` + sub-routes
- `frontend/components/Sidebar.tsx` — เพิ่ม link
- Seed script update — populate `company_profile` for `manual-demo`

### Sana owns (update AFTER merge):
- `docs/accounting-system-plan.md` — เพิ่ม §6.X "Company Profile model"
  พร้อม hard/soft field classification + Phase 2 plan
- `docs/api/openapi.yaml` — เพิ่ม `/company-profile/*` paths
- `docs/manual/chapters/02-ตั้งค่าระบบ.md` — เพิ่ม walkthrough 02.05
  "ข้อมูลบริษัท"
- `frontend/manual/walkthroughs/02.05-company-profile.ts` (new — Sana
  จะเขียนเอง รอ build เสร็จก่อน)

### Acceptance
- GET `/api/v1/company-profile` → 200 + profile object
- PUT soft → 204 + table refresh + toast
- PUT hard → 501 + body อธิบายชัดว่า Phase 2 จะรองรับ + วิธี workaround
  ปัจจุบัน (DB script + audit log entry manual)
- หน้า `/settings/company` render hard fields เป็น disabled +
  tooltip ภาษาไทย
- Logo upload ผ่าน → URL update ใน profile + แสดง preview

---

## Out of scope (Phase 2 follow-ups)

- 2-person approval flow สำหรับ hard fields edit
- Effective-date history สำหรับ company profile (render เอกสารเก่า
  ด้วย profile ที่ตรงวันที่)
- Multi-branch — แต่ละสาขามี profile + เลขสาขาแยก
- ภ.พ.09 attachment workflow (upload เอกสารแจ้งเปลี่ยนแปลง + admin approve)
- Audit log viewer สำหรับ company profile changes

---

## Test plan

### Unit / Integration
- BU + Product + WHT CRUD: ทุก endpoint ต้อง return v1 envelope (P5)
- Permission gate: roles → scopes mapping (P3)
- Company profile soft PUT vs hard PUT (P6)
- AlertDialog component: confirm/cancel/escape/overlay-click (P1)
- 403 → NoAccessState rendering (P2)
- Inactive row → Restore button visible (P4)

### E2E (Playwright)
- `settings/business-units` full CRUD via UI (no window.confirm fallback)
- `settings/wht-types` as ACCOUNTANT → buttons hidden
- `settings/api-keys` as ACCOUNTANT → no-access state
- `settings/company` as ADMIN → soft fields editable, hard fields disabled
- Logo upload → render in nav bar / invoice header

### Acceptance demo
ผม Sana จะ verify ผ่าน Chrome MCP ทุก fix หลัง merge — เป็น part ของ
Sprint 13b re-run (manual capture จะต้องทดสอบใหม่ทั้ง chapter 2 ด้วย
walkthrough ที่ผ่านการ exercise จริง). Report จะมาเป็น
`Report-Backend21.md`.

---

## File ownership reminder

Claude Code edits: source code, tests, migrations, seed scripts.

Sana owns (DO NOT edit — provide proposed text in Report-Backend21.md
แทน, ผมจะ apply):
- `CLAUDE.md`
- `docs/accounting-system-plan.md`
- `docs/runtime-gotchas.md`
- `docs/api/openapi.yaml`
- `frontend/manual/walkthroughs/*` (Sprint 13b — Sana จะ rewrite chapter 2
  หลัง fix)
- `docs/manual/chapters/*`

---

## Dependencies / sequencing

- P1, P2, P3, P4 — independent, ทำขนานได้
- P5 — touch ทุก endpoint, ทำเป็น last หลัง P1-P4 stable
- P6 — independent ของ P1-P5 ทั้งหมด, ทำขนานได้ตั้งแต่วันแรก

แนะนำ: 2 dev สลับ work → 1 ทำ P1+P2+P3+P4 (FE-heavy), อีก 1 ทำ P5+P6
(BE-heavy). ถ้า 1 dev: P1 → P6 → P2 → P3 → P4 → P5 (เรียงตาม impact:
unblock automation ก่อน, ปลด Company Profile gap, แล้วค่อย polish).

---

## Reporting back

`Report-Backend21.md` ใน root — รวมทุก phase ที่ทำเสร็จ, test results,
breaking change ไหน (น่าจะมี: error envelope shape change → frontend
client ต้องอัพเดต concurrent — เป็น breaking ที่กระทบ test suite ทุกตัว
ที่ assert error shape).

Sana จะ apply doc changes + Sprint 13b chapter 2 re-test ตาม findings.

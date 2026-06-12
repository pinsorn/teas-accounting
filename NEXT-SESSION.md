# NEXT SESSION — kickoff (เขียน 2026-06-11 ปลายเซสชัน cont.87a-e)

> อ่านลำดับ: `CLAUDE.md` → ไฟล์นี้ → `progress.md` (entry cont.87d/87e บนสุด) → `plan.md`.
> ไฟล์นี้คือคิวงานพร้อมรัน — ทำเสร็จข้อไหน ติ๊ก + อัปเดต progress.md แล้วลบไฟล์นี้ทิ้งเมื่อหมดคิว.

## สถานะเครื่อง ณ จบเซสชันก่อน

- **Remote:** `github.com/pinsorn/teas-accounting` (PRIVATE) — **push หลังทุก commit** (นโยบายตั้งแล้ว).
  HEAD ล่าสุด `50cf185`. ห้ามเปลี่ยน public โดยไม่ล้าง history (dev creds ใน history).
- Servers ตอนปิดเซสชัน: API :5080 + `next dev` :3000 รันอยู่ (อาจโดนปิดไปแล้ว — เช็คก่อน).
- Gates ล่าสุด: Domain **137/137** · Api **251/251** · tsc **0** · e2e **50 pass / 7 skip มีเหตุ / 0 แดง**.
- DB: migration `AddCitYearStoresAndPaidUpCapital` apply แล้วทั้ง dev + teas_test; RLS script 500 รันแล้ว.

## คิวงาน (เรียงตามที่ตกลง)

### 1. ☑ DONE (cont.88) — M13 bug: API-key path มองไม่เห็น business_units (`bu.invalid` 422)

> **ปิดแล้ว 2026-06-11.** Root cause ไม่ใช่ RLS/FORCE — สาเหตุจริง: (1) EF tenant filter มี
> super-admin bypass → admin (company 1) mint key ผูก BU 3 (REPT ของ company 2) ได้ →
> key principal (non-super) มองไม่เห็น BU นั้น = enforce ถูกต้อง; (2) ApiKey principal ไม่มี
> Branch claim → BranchId=0 → JE numbering ชน ix_journal_entries. แก้: BU validation/list/mint
> เป็น company-explicit ทุกจุด + principal ถือ head-office branch. ดู progress cont.88.
> **มติ Ham (2026-06-11 เช้า):** BYPASSRLS บน dev = ยอมรับได้ (มอง dev role เป็น super admin;
> ระบบ permission ยังกำกับ — โปรดักชันจริงไม่ใช้ account dev). ✗ ไม่แก้ role.
> dev keys เก่า (id 2,3,4 company 1 + BU 3 ข้าม company) — อธิบายให้ Ham แล้ว รอเคาะ revoke.

**อาการ (reproduce แล้วสด 2026-06-10, ดู progress cont.87d):**
- POST `/api/v1/tax-invoices` + `X-Api-Key` + `businessUnitId` ใด ๆ → 422 `bu.invalid`
- ไม่ส่ง BU → 201 ✓ · GET v1 list ผ่าน key เห็น TI ครบ ✓ · BU เดียวกันผ่าน JWT ✓
- จุดโยน: `TaxInvoiceService.cs:144` `_db.BusinessUnits.AnyAsync(...)`

**Smoking gun:** `master.business_units` = ENABLE+**FORCE** RLS (script `200_add_business_units.sql`)
แต่ตารางฐานใน `010_rls_policies.sql` ไม่ FORCE → owner (`accounting`) bypass ได้.
⇒ บน ApiKey path, connection ที่รัน BU query **ไม่มี `app.company_id` pin** — เห็นเฉพาะตารางไม่ FORCE.

**Probe ขั้นแรก (เลือกอย่างใดอย่างหนึ่ง):**
- (ก) integration test: ApiKey principal + `SELECT current_setting('app.company_id', true)` บน
  scoped `AccountingDbContext` ระหว่าง request — เทียบ JWT path
- (ข) log ชั่วคราวใน `TenantMiddleware.InvokeAsync` (`tenant.IsAuthenticated`, `CompanyId`,
  connection state) แล้วยิงด้วย key จริง

**เช็คลิสต์สาเหตุที่แคบแล้ว:** TenantMiddleware (`Api/Middleware/TenantMiddleware.cs`) รัน "หลัง"
`UseAuthorization` (Program.cs 120-122) — คำถามคือ ctx.User เป็น ApiKey principal ตอนนั้นจริงไหม
(policy-scheme auth merge) และ BU query ใช้ connection เดียวกับที่ pin ไหม (IdempotencyMiddleware /
scope แยก?). Claims ตรงกันแล้ว (`TenantClaims.CompanyId` — `Authorization/ApiKeyAuthentication.cs:51`).

**เมื่อแก้แล้ว:** ปลด `test.skip(true, …)` ใน `frontend/e2e/external-api-microservice.spec.ts:67`
→ spec ต้อง green. พิจารณาด้วยว่าควร FORCE RLS ให้สม่ำเสมอทุกตาราง (010) หรือไม่ — ถ้าทำ = แตะ
compliance §4.7 → **ถาม Ham ก่อน**.

### 2. ☑ DONE (cont.88) — ภ.ง.ด.50 Phase C-C FORM FILL (งานใหญ่หลัก)

> **เสร็จ 2026-06-11 (v1: p1 + p2 รายการที่ 1).** Plan: `docs/superpowers/plans/2026-06-11-pnd50-form-fill.md`.
> geometry → RdRadio on-state → BuildSheet+guard → filler → visual gate (crops ส่ง Ham แล้ว) →
> service + `GET /tax-filings/pnd50/pdf` + FE card ที่ /tax-filings/cit + openapi.
> Gates: Api 277/277 ×2 · Domain 137/137 · tsc 0 · live smoke 200/422. รอ Ham ยืนยัน crops ก่อนยื่นจริง.

ทุกอย่างพร้อม build แล้ว — mapping จบ ไม่ต้อง recon ซ้ำ:
- Spec: `docs/superpowers/specs/pnd50-fieldmap-recon.md` (โครง 7 หน้า + draft map + v1 scope)
- Radio map render-confirm ครบ: `docs/RD-Forms/pnd50/pnd50_radiomap.md` (ห้ามเดาเพิ่ม — confirm แล้วทุก choice)
- เครื่องมือ: `docs/RD-Forms/pnd50/fieldmap/{probe,zfill,radio_confirm}.py` · pymupdf มีในเครื่อง
- Data layer เสร็จ (cont.87): `CitYearDataService.ProfileAsync` (SME/loss-c/f/adjustments),
  `cit_year_summaries.pnd51_prepaid`, `CitCalculator.Compute`/`UnderEstimatePenalty`, `BalanceSheetAsync`

**ลำดับ build (จาก spec §Next-session method):**
1. สกัด cell-centres กล่อง comb p1+p2 (generalise `_pnd51_geo.py` ที่อยู่ root) → embed `Pdf/Templates/pnd50_cells.json`
2. `Pnd50FormFiller` (mirror `Pnd51FormFiller`): p1 header + p2 รายการที่ 1, THB เท่านั้น,
   **refuse-on-unrenderable guard แบบ pnd51 §4** (ช่องว่าง = ศูนย์ — ห้าม default เงียบ ๆ)
3. TDD structural tests → visual gate (raster ทุก box/radio + ส่ง crops ให้ Ham)
4. `Pnd50FilingService` + endpoint `GET /tax-filings/pnd50/pdf?year&isSme…` + FE + openapi
5. เขียนแผนผ่าน superpowers:writing-plans ก่อนลงมือ (แตก task ให้ subagent ได้)

### 3. ☑ DONE (cont.88) — FE: SO/DO list status filter persist ลง URL (`urlFilters` prop, 2 specs ปลด skip)

### 6. ☑ DONE (cont.92, 2026-06-12) — ภ.ง.ด.50 Phase C-D BUILD

> **Shipped:** p4 zeros-by-design + p5 รายการที่ 7/8 (GL partition + adjustments, foot-guard vs
> ladder) + p7 header + ม.71ทวิ informational refusal + dashboard cards + openapi. Api 314/0/1.
> Commits `9a314b0`…`eae09ed` (push แล้ว). ดู progress cont.92 + plan C-D ☑.
> **ค้างจาก C-D:** (1) Ham ยืนยัน crops `_review/pnd50cd/` ก่อนยื่นจริง · (2) ม.71ทวิ ตีความเป็น
> informational (PDF ยัง render) — Ham เห็นต่างแจ้งได้ · (3) ต้นทุนทางการเงิน p4[108] vs p5[121]
> รออ่านคำแนะนำ RD ก่อน map บัญชีดอกเบี้ยจ่าย (ตอนนี้ตกข้อ 22).

### 7. ☑ DONE (cont.92b, 2026-06-12) — ภ.พ.01/09 v1 identity prefill

> **Shipped:** `GET /tax-filings/pp01/pdf` + `/pp09/pdf` (header หน้า 1 จาก CompanyProfile,
> print-and-sign, ไม่ tick radio) + ปุ่ม prefill บน `/documents` + maps/cells commit แล้ว.
> commit `2d52a7e`. C-D decisions ปิดด้วย: finance cost = 5500-5599 → ร.7 ข้อ 12 (`ad691c6`) ·
> ม.71ทวิ = informational ยืนตามเดิม. **ค้าง Ham:** ยืนยัน crops `_review/pnd50cd/` + `_review/vatreg/`.

### 8. ☑ DONE (cont.92c) — Dev DB ล้างบาง + M15 + Reptify key + e2e re-baseline

> Backups `Y:\TEAS-backups\` · task "TEAS dev DB dump" 03:30 ทุกวัน · key ใหม่ใน
> `Y:\TEAS-backups\reptify-dev-key.json` (company 2, BU REPT) · e2e 55/2/0 บน next start.
> spec SO/DO filter แก้แล้วไม่พึ่งข้อมูลเก่า (`71d51a4`).

### 9. ค้างรอ Ham (ไม่มีงาน build เหลือในคิว)

- ยืนยัน visual crops 2 ชุด: `_review/pnd50cd/` (ภ.ง.ด.50 C-D) + `_review/vatreg/` (ภ.พ.01/09)
- RD PDF ~60MB commit เข้า repo? — irreversible repo-size, รอเคาะ
- สปส.1-10 upload e-Service จริง — ต้องมือ Ham (external)
- เอา Reptify key ใหม่ไปใส่ `.env` ฝั่ง Reptify เมื่อเริ่ม wire integration จริง

### 5. 🆕 คิวใหม่ (มติ Ham 2026-06-11 เช้า — spec: `docs/superpowers/specs/pnd50-v2-dashboard.md`)

1. ☑ **DONE (cont.89) — ภ.ง.ด.50 v2 = default + CIT filing dashboard.** recon p3/p6 + ladder/งบฐานะ
   render จริง + `GET /tax-filings/pnd50/preview` (single-source) + dashboard บน `/tax-filings/cit`
   (ladder/WHT-cert/งบฐานะ cards + refusal warnings). v1 adjustments/loss guard หายไป; แก้ double-count
   (`AccountingNetProfit` ไม่ใช่ `EffectiveNetProfit`). Visual gate ผ่าน — crops ส่ง Ham, **รอยืนยันยื่นจริง**.
   Api 294/294 ×2 · Domain 137/137 · tsc 0. plan `2026-06-11-pnd50-v2-dashboard.md`.
2. ☑ **DONE (cont.89) — หน้า `/documents`** — ตารางฟอร์ม RD จัดกลุ่มตามหมวด (VAT/WHT/CIT/PIT/SBT/Stamp)
   + กำหนดยื่น + tier badge + ปุ่มลิงก์ฟอร์มทางการ. **มติ Ham: ลิงก์ official RD URL** (เปิด tab ใหม่
   ได้ version ล่าสุด) ไม่ serve PDF ที่ commit. `lib/rd-forms.ts` + i18n th/en (86 keys parity) +
   nav item. Rendered + visual-verified. commit `b2295c2`.
3. **Dev DB ล้างบาง** ("หลอนหมดแล้ว"): pg_dump backup → drop/recreate accounting_dev →
   reseed → mint Reptify key ใต้ company 2 → e2e re-baseline. ทำคู่ M15 dump อัตโนมัติ.
   (keys 2,3,4 revoke แล้ว 2026-06-11)

### 4. เก็บเล็ก (ทำแทรกได้)

- ☑ `pv-sod-violations.spec.ts` → `pv-approval-permission.spec.ts` (cont.88)
- ☑ กอง untracked root คัดแล้ว (cont.88 ย้าย `_pnd51_*` เข้า fieldmap + cont.90c ลบ scratch 33 ไฟล์
  + log 32 ไฟล์; `_review/` เหลือชุด validated)
- ☑ `docs/RD-Forms/INDEX.md`/`REPORT.md` commit แล้ว (cont.88)
- ☐ M15: DB dump `accounting_dev` อัตโนมัติ (โค้ดมี remote แล้ว แต่ data ยังเครื่องเดียว) —
  ทำคู่ item 5.3 (dev DB ล้างบาง)
- ☐ RD PDF binaries ~60MB ยัง untracked — **รอ Ham เคาะ** commit เข้า repo หรือเก็บนอก git
- ☐ สปส.1-10 ทดสอบ upload e-Service จริง (external, ต้องมือ Ham)
- ☐ ภ.ง.ด.1 หมายเหตุ: เดือนฟอร์มตอนนี้ตาม `PayDate` (ม.52/59, cont.91) — ถ้า Ham เห็นต่างแจ้งได้

## กฎที่ต้องไม่ลืม (จากบทเรียนเซสชันนี้)

- e2e suite ออกแบบให้รันกับ `next start` — บน `next dev` flow หนักใกล้เพดาน 30s (สองตัวขยายเป็น 60s แล้ว)
- ตัวเลขปี/period ใน test ที่แตะตาราง unique บน shared DB → ใช้ `TestIds.*` + เช็คประวัติ DB
  (ดู `FreshYearAsync` ใน `PayrollRunServiceTests` เป็นแบบ)
- ตัวอักษร ม (มาตรา) ระวัง glyph เบงกาลี ম ปลอม — `grep -rln "ম"` ก่อน commit
- kill :5080 ก่อน build เต็ม · ห้าม `ef --no-build` · push ทุก commit

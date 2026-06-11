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
> **หมายเหตุถึง Ham:** role `accounting` บน dev มี BYPASSRLS → RLS ทั้งระบบไม่ทำงานจริง (§4.7) —
> ต้องตัดสินใจระดับ infra; และ dev keys เก่า (id 2,3,4 "Reptify Shopify"/dbg, company 1 + BU 3
> ข้าม company) ยังค้างใน DB — ควร revoke/remint ภายใต้ company ที่ถูก.

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

### 3. 🟠 FE: SO/DO list status filter ไม่ persist ลง URL (regression จาก DataTable redesign)

- 2 specs skip รออยู่: `sales-order-flow.spec.ts:13`, `delivery-order-flow.spec.ts:14`
- แก้ FE ให้ filter เขียน query param เหมือนเดิม (เทียบ pattern หน้า list อื่นที่ยัง persist) → ปลด skip
- อยู่ในอำนาจแก้เอง (bug ของ feature ที่เคย ship)

### 4. เก็บเล็ก (ทำแทรกได้)

- ☐ ลบ/เขียนใหม่ `pv-sod-violations.spec.ts` เป็น permission-based test (SoD ถูกถอดโดยมติ Ham cont.77 — test เก่า skip ค้าง)
- ☐ คัดกอง untracked ที่ root (`_pnd51_*.py`, `_taxid_*.png`, `_review/`, `comb_uniformity.txt` ฯลฯ):
  เก็บเข้า `docs/RD-Forms/pnd51/fieldmap/` หรือลบ — ถาม Ham ถ้าไม่แน่ใจ
- ☐ `docs/RD-Forms/INDEX.md` + `REPORT.md` แก้ค้างอยู่ (untracked diff เก่าก่อน cont.87) — review แล้ว commit หรือ revert
- ☐ M15: DB dump `accounting_dev` อัตโนมัติ (โค้ดมี remote แล้ว แต่ data ยังเครื่องเดียว)

## กฎที่ต้องไม่ลืม (จากบทเรียนเซสชันนี้)

- e2e suite ออกแบบให้รันกับ `next start` — บน `next dev` flow หนักใกล้เพดาน 30s (สองตัวขยายเป็น 60s แล้ว)
- ตัวเลขปี/period ใน test ที่แตะตาราง unique บน shared DB → ใช้ `TestIds.*` + เช็คประวัติ DB
  (ดู `FreshYearAsync` ใน `PayrollRunServiceTests` เป็นแบบ)
- ตัวอักษร ม (มาตรา) ระวัง glyph เบงกาลี ম ปลอม — `grep -rln "ম"` ก่อน commit
- kill :5080 ก่อน build เต็ม · ห้าม `ef --no-build` · push ทุก commit

# TEAS — Project Status & Module Plan (2026-06-10)

> **เอกสารสรุปสถานะ ณ วันย้ายบ้านโปรเจกต์ → `Y:\ClaudePlayground\TEAS-Project`**
> แหล่งความจริงรายละเอียด: `progress.md` (log รายเซสชัน) + `plan.md` (แผนเดินหน้า) + `docs/accounting-system-plan.md` (spec หลัก)
> สถานะ: ☑ เสร็จ+ทดสอบ · ◐ บางส่วน · ☐ ยังไม่เริ่ม · ⏸ ตั้งใจพัก

---

## ⚖️ 0. Compliance ก่อนอื่นใด (ผิด = โทษอาญา — ห้ามข้ามเด็ดขาด)

ระบบนี้ต้องผ่านการตรวจสรรพากรได้ตลอดเวลา. กฎเหล็กที่ **โค้ดบังคับอยู่แล้ว** และห้ามใครแก้อ่อนลง:

| กฎ | กฎหมาย | บังคับที่ |
|---|---|---|
| ใบกำกับภาษีครบ 8 องค์ประกอบ | ม.86/4 | TI service + PDF + ทดสอบ |
| เอกสาร Post แล้ว **ห้ามแก้/ลบ** — แก้ = ใบลดหนี้+ออกใหม่ | ม.86/9,86/10 | DB trigger + app layer |
| เลขเอกสารต่อเนื่อง ไม่มีรู ออกเลขตอน Post เท่านั้น Void ไม่ reuse | — | NumberSequence + monthly reset |
| VAT แสดงแยกจากมูลค่าสินค้าเสมอ | ม.86/4(6) | PDF + GL |
| ภาษีซื้อต้องห้าม/non-VAT ห้าม claim (ลง 5350) | ม.82/5, 83/6 | GL branching |
| Multi-tenant: ทุกตารางมี `company_id` + PostgreSQL RLS + EF filter | — | ทุก query |
| Audit trail ทุก state change, ห้ามลบ log, เก็บ 5 ปี | พรบ.บัญชี ม.14 | `audit.activity_log` append-only |
| VAT rate/mode อยู่ใน config เท่านั้น ห้ามโผล่ UI | — | `.env`/appsettings |
| ปฏิทิน ค.ศ. ภายในระบบเสมอ, เงิน = `decimal(4dp)` | — | ทั่วทั้งระบบ |
| แบบภาษี: **ช่องว่าง = ศูนย์** → กรอกเฉพาะที่ยืนยันได้ (attestation guard) | ภ.ง.ด.51 §4 | `pnd51.worksheet_not_attestable` throw |
| ห้ามเดา mapping แบบฟอร์มราชการ — ต้อง render-confirm ทุก field/radio | §11 | radio map + visual gate |

**บทเรียนที่จ่ายแพงแล้ว (อย่าให้ซ้ำ):** radio map ภ.ง.ด.51 ฉบับแรก sort กลับด้าน → ถ้าใช้จะติ๊ก "ยกเว้นภาษี" แทน "กรณีทั่วไป" บนแบบจริง. จับได้เพราะกฎ "never guess a radio" + render-confirm. **ทุกฟอร์มราชการใหม่ต้องผ่าน visual gate เสมอ.**

---

## 1. สถานะรวม

**Phase-1 backbone = production-ready foundation: เสร็จ.** ขายครบวงจร, ซื้อครบวงจร, GL, ภาษีรายเดือน (ภ.พ.30/36, ภ.ง.ด.1/1ก/3/53/54), payroll+ประกันสังคม, ภ.ง.ด.51 ครึ่งปีพร้อมหน้า 2, non-VAT mode, external API, e-Tax โครงพร้อม (จงใจปิดไว้).

**งานใหญ่ที่เหลือ:** ภ.ง.ด.50 (ยื่นปีนิติบุคคล — ใหญ่สุด), e-Tax เปิดใช้จริง (รอ cert + ETDA), sprint คุณภาพ 13k/13L, Fixed Assets, manual ของ Sana.

ตัวเลขล่าสุด: BE build 0/0 · Api.Tests 226+/226+ · Domain 89/89 · FE tsc 0 · Playwright ~31 specs.
Commit ล่าสุด: `47bd37f` (ภ.ง.ด.51 page-2 ครบ). **Git อยู่ local เท่านั้น — ยังไม่มี remote!** (ดู §14)

---

## 2. รายโมดูล

### M1 — Identity / RBAC / Multi-tenant ☑
- ☑ OAuth2+JWT, บทบาท (SUPER_ADMIN→AP_CLERK), SoD (ผู้สร้าง≠ผู้อนุมัติ มี DB CHECK), RLS ทุกตาราง, API key per-BU
- **ต่อ:** ☐ Sprint 13k = RBAC full-Cartesian audit + security + perf + a11y (queued, Answer-30)

### M2 — Master data ☑
- ☑ Customer/Vendor (รวม foreign vendor + VAT-D), Product (GOOD/SERVICE + DefaultWhtType), Business Units (GL dimension + เลขเอกสาร sub-prefix), WHT types (effective-date), Tax codes (ม.81 exempt/zero), Expense categories
- **ต่อ:** ☐ sprint "line product typing + inline product modal" (`docs/sprint-line-product-wht-plan.md`) · ☐ `bank_account` master (debt)

### M3 — Sales chain ☑
- ☑ Q→SO→DO→Invoice→TI→RC ครบ + CN/DN + Billing Note (join table), combined DO→TI, print ต้นฉบับ/สำเนา + tracking, document chain ทุกหน้า, QuestPDF ทั้ง 8 doctype (Thai/Sarabun), WHT ฝั่ง AR (ลูกค้าหัก เรา = 1180 + 50ทวิ Direction R + ใบขาดรายงาน)
- **ต่อ (เล็ก):** ☐ ซ่อนปุ่ม DO→Invoice หลังสร้าง · ☐ CN/DN chain-row routing heuristic · ☐ BP-08/BP-10 (e2e test-side) · ☐ fix RED `Wht_base_suggest_splits_service_and_goods`

### M4 — Purchase / AP ☑
- ☑ PO (SoD+auto-close ≥95%) → VI (บังคับแนบใบกำกับผู้ขาย ก่อน Post) → PV (Draft→Approved→Posted) + WHT 50ทวิ 2 ฉบับ + self-withhold gross-up (foreign/auto-charge) + AP aging
- **ต่อ:** ☐ 3-way match PR→PO→GR (Phase 2 — SME ไม่ใช้, ตั้งใจตัด) · ☐ self-withhold สำหรับ VI-linked PV

### M5 — GL / สมุดบัญชี ☑
- ☑ posting service ทุกเอกสาร (สมดุล Dr=Cr ทุก JV, ทดสอบ invariant), BU snapshot ลงทุก journal_line, period close, audit ทุก transition
- **ต่อ (test depth):** ☐ NumberSequence concurrency test · ☐ period-close gating integration test · ☐ PV+WHT flow integration test

### M6 — VAT filings (ภ.พ.30 / ภ.พ.36) ☑ (ยื่นจริงยัง mock)
- ☑ ภ.พ.30 preview/finalize → `tax.tax_filings` immutable, ทะเบียนภาษีซื้อ/ขาย, ม.82/6 proportional, ภ.พ.36 reverse-charge + auto-JV (Dr 1170/Cr 2151)
- ⚠️ **auto-mode = `MockRdEfilingClient`** — การยื่น RD Open API จริงยังไม่เชื่อม (HTTP skeleton พร้อม). ก่อน production: เชื่อม API จริง + UAT
- **ต่อ:** ☐ RD API จริง · ☐ per-line direct/shared input-VAT (Phase 2)

### M7 — WHT filings (ภ.ง.ด.3/53/54) ☑
- ☑ generators ทั้งสาม (จาก WhtCertificate Direction P, route ตาม payee/Pnd54), immutable store, หน้า UI
- **ต่อ:** ☐ ไฟล์ e-filing TEXT format ของ ภ.ง.ด.3/53 (แบบเดียวกับที่ทำ สปส.1-10 — ต่อยอด `WhtBatchFormat`/`FormatPND1V2_0.pdf`)

### M8 — e-Tax Invoice ⏸ (จงใจ inert — มติ Ham 2026-05-16)
- ☑ XAdES-BES signer ครบตาม spec (RSA-SHA512, C14N inclusive, ทดสอบโครงสร้าง), pipeline build→sign→validate→send + retry worker + append-only `etax.submissions`, Tier-1 mock (MailHog/MockServer), recipient whitelist
- **Blocked production (ตามลำดับ):** 1) cert จริง NRCA/TUC ผ่าน `.env` 2) ETDA sandbox UAT 3) ปริศนา C14N round-trip (.NET CheckSignature ใช้ไม่ได้ — ต้องตัดสิน: ETDA validator / custom canonicalizer / ยืนยัน Excl-C14N กับ ETDA — **ห้ามเดา, ต้อง Ham+ETDA**)

### M9 — Payroll + ประกันสังคม ☑
- ☑ Employee master, `ThaiPitCalculator` (golden 12), PayrollRun→Payslip (immutable หลัง Post, SoD), GL posting, payslip PDF + zip, ภ.ง.ด.1/ภ.ง.ด.1ก AcroForm fill, สปส.1-10 e-Service TEXT (135-char TIS-620)
- **ต่อ:** 🟠 ยืนยันเพดาน SSO 2569 (฿15,000→17,500 phased — config) · ☐ ทดสอบ upload สปส.1-10 กับ e-Service จริง · ☐ ภ.ง.ด.1 ใน format ไฟล์ e-filing (มี PDF แล้ว)

### M10 — CIT (ภ.ง.ด.51 / ภ.ง.ด.50) ◐ ← **งานปัจจุบัน**
- ☑ **เครื่องคิด CIT บริสุทธิ์**: `CitRateSchedule` General 20%/SME 0-15-20 + `CitCalculator` (ม.67ทวิ prepay, ม.67ตรี penalty) — golden 18/18
- ☑ **ภ.ง.ด.51 ครบทั้งฟอร์ม** (commit `bf45143`→`47bd37f` วันนี้): หน้า 1 + หน้า 2 worksheet Method A หลัง **attestation guard 7 เงื่อนไข** (5 คำรับรอง + estimate>0 + tax≥WHT → ทุก worksheet ที่ออก foot เสมอ; ไม่ผ่าน = 422 + หน้า 2 ว่าง [ถูกกฎหมาย]); page-aware `RdAcroFormFiller` (ฟอร์มเดิม pixel-identical); FE 5-checkbox gate
- **ต่อ (ภ.ง.ด.51 ส่วนเหลือ — เล็ก):** ☐ SME % radio Button20 (ต้องถาม Ham ก่อนติ๊ก) · ☐ Method B + ชำระไว้เกิน · ☐ เก็บ estimate ลง DB → เช็ค ম.67ตรี ปลายปี · ☐ worked-example จาก `pnd51_instructions.pdf` พิสูจน์ code≡law
- **ต่อ (ภ.ง.ด.50 — Phase C-C ใหญ่, ยังไม่เริ่ม ❗):** ☐ adjustment-entry model (ম.65ตรี บวกกลับ/หักออก, manual UI — มติล็อกแล้ว) · ☐ `Company.PaidUpCapital` + migration + auto-SME · ☐ loss carry-forward store (per-year, override ได้) · ☐ `BalanceSheetAsync` ของจริง · ☐ `Pnd50FormFiller` (ฟอร์ม `pnd50_050369.pdf` อยู่ใน docs แล้ว) + service (P&L FY + WHT credit + เครดิต 51) + endpoint + FE
- **ต่อ (Phase C-D — ใหญ่สุด ทำท้าย):** ☐ ใบแนบ ภ.ง.ด.50 ทั้ง 5 + disclosure form (ม.71ทวิ) + งบแสดงฐานะ

### M11 — Reports / งบการเงิน ◐
- ☑ Trial Balance (Σ Dr=Cr invariant), P&L (flat, by BU), sales summary (customer/BU/product), AR/AP aging, WHT-receivable aging + missing-cert
- **ต่อ:** ☐ **Balance Sheet จริง** (ตัวปลดล็อก ภ.ง.ด.50) · ☐ GP/COGS layer (Phase 2 — ตอนนี้ flat + disclosure note) · ☐ งบทดลองแบบช่วงเวลา/เทียบงวด

### M12 — Non-VAT mode ☑
- ☑ ครบ: ซ่อน VAT artifacts + route guard, block TI (ม.86/13), ใบเสร็จ standalone/apply-DO (Cr Sales 4000 cash basis), ภ.พ.36 sunk VAT → 5350, threshold ม.85/1 banner
- **ต่อ:** ☐ openapi delta ชุด non-VAT + chain ให้ Sana (ค้างนาน)

### M13 — External API ☑
- ☑ `X-Api-Key` + bcrypt + scopes + idempotency middleware + v1 envelope + per-key BU binding + `/api/v1/*`
- **ต่อ:** — (รอ use case จริง)

### M14 — FE / Design system ◐
- ☑ Claude Design swap (sales ครบ), PaperDocument suite, DataTable/FilterBar/StatusBadge, i18n th/en เคร่ง parity
- **ต่อ:** ☐ 13j-PDF polish (watermark visual + seller จาก CompanyProfile + Sana 1:1 sign-off) · ☐ design swap ฝั่ง Purchase/Settings · ☐ BFF `/api/proxy/[...path]`

### M15 — DevOps / คุณภาพ ☐ ← **เสี่ยงสุดเชิงปฏิบัติการ**
- ☐ Sprint 13k: security scan + RBAC Cartesian + perf + a11y
- ☐ Sprint 13L: migration rollback plan + build pipeline (CI!) + test-skip audit
- ☐ **git remote + backup strategy** — ตอนนี้ commit อยู่บนเครื่องเดียว ❗
- ☐ TenantIsolationTests idempotent fix

### M16 — Manual (Sana) ⏸
- ⏸ Chapter 3+ รอ 13i✅→13j◐→13k☐→13L☐ ครบ + RE-VALIDATE

### M17 — นอก scope (ยืนยันแล้ว)
- ⏸ Inventory tracking (จนกว่าจะสั่ง) · ⏸ audited FS/DBD filing · ⏸ Fixed Assets register ☐ (ยังไม่จัด sprint)

---

## 3. ลำดับแนะนำ (เรียงตาม impact × risk)

1. **git remote + backup** (M15) — งานทั้งหมดอยู่เครื่องเดียว เสี่ยงสูงสุดแบบไร้เหตุผล ทำได้ใน 1 ชม.
2. **ภ.ง.ด.50 Phase C-C** (M10) — งานใหญ่ถัดไปตามมติ; เริ่มจาก `Company.PaidUpCapital` + loss-c/f store + adjustment model → `BalanceSheetAsync` → form filler (เครื่องมือครบแล้ว: RdAcroFormFiller page-aware + CitCalculator + วินัย visual gate)
3. **ภ.ง.ด.51 เก็บตก** — store-estimate ม.67ตรี (ผูกกับ C-C) + worked-example
4. **Sprint 13k → 13L** — ก่อน production ใช้จริง
5. **e-Tax เปิดจริง** — เมื่อ Ham พร้อมเรื่อง cert + ETDA
6. M3/M5 รายการเล็กเก็บระหว่างทาง

---

## 4. การรันที่บ้านใหม่ (Y:)

```powershell
# Backend (path สั้น — ไม่ต้อง subst W: อีกแล้ว!)
cd Y:\ClaudePlayground\TEAS-Project\backend
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
dotnet run --project src\Accounting.Api

# Frontend (pnpm relink แล้ว; รันจาก path จริง Y: ได้เลย)
cd Y:\ClaudePlayground\TEAS-Project\frontend
node node_modules\next\dist\bin\next dev

# Tests (dotnet test ตรงจาก Y: ได้ — พิสูจน์แล้ว 13/13)
$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'
dotnet test Y:\ClaudePlayground\TEAS-Project\backend\tests\Accounting.Api.Tests
```
- Login dev: `admin / Admin@1234` · DB: `accounting_dev` (Host=localhost:5432, accounting/accounting_dev_password)
- ⚠️ kill API ก่อน full build (exe lock) · ห้าม `ef --no-build` · ห้าม `next build` ขณะ dev รัน
- Verified ณ วันย้าย: build 0/0 · tsc 0 · Pnd51WorksheetTests 13/13 จาก Y: ตรง ๆ

---

*จัดทำ 2026-06-10 ระหว่างย้ายโปรเจกต์ → Y: · อัปเดตไฟล์นี้เมื่อ phase ใหญ่เปลี่ยน; รายละเอียดรายวันอยู่ progress.md*

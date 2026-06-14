# TEAS — Forward Plan

> Living plan of what is left. Update when scope/priority changes (see CLAUDE.md §13).
> Status legend: ☐ not started · ◐ in progress · ☑ done · ⏸ blocked/deferred

---

## ▶ Sprint 13k RBAC (plans: `docs/superpowers/plans/2026-06-13-rbac-{admin-ui,cartesian-audit}.md`)

- ☑ **Plan 1 — per-company roles + admin UI** (cont.95, 2026-06-14, commit b8b4773) — see progress.md.
- ☑ **Plan 2 — Full Cartesian audit** (cont.96, 2026-06-14) — endpoint→perm map generator (255 routes),
  role×perm matrix doc (source of truth), `RbacCartesianTests` (role×endpoint HTTP enforcement + super
  bypass + cross-company isolation + API-key scope, green ×2 on teas_test). **Found+fixed a seed-ordering
  bug** that left core operational perms (sales create/post, TI issuance, tax/report reads, journals,
  vendor master, PO-create, period-close) SUPER_ADMIN-only → `530_seed_rbac_grant_reconcile.sql` restores
  them per role purpose; `master.company.manage` stays super-only (§4.6). Also fixed an unauth
  `GET /periods/.../status` leak. Api 360/0/3 ×2 · FE tsc 0. **NOT committed (รอ Ham).**
  - ⚠️ **Ham to เคาะ on return:** (a) SoD overlaps CHIEF/COMPANY_ADMIN PV create+approve (matrix doc §SoD);
    (b) `sys.role.manage`+`sys.user.manage`→COMPANY_ADMIN (530 §D — keep or switch to assigning SUPER_ADMIN);
    (c) `/company-profile/*` shares `master.company.manage` with §4.6 tax config → needs its own perm.
  - ☑ **Plan 2 Phase E (FE gating)** — nav fully permission-gated + main action buttons wrapped in
    `PermissionGate` (create on 8 list pages; lifecycle on PV/TI/PO/VI detail; payroll already gated).
    Remaining nit: receipt post lives in the shared `DocActionBar` component (gate there if wanted);
    settings/* create buttons are already nav-gated by their manage perm. UX-grade — BE is the real
    enforcement (proven by `RbacCartesianTests`).

## ▶ Per-company VAT mode (spec: `docs/superpowers/specs/per-company-vat-mode.md`, Ham 2026-06-11)

- ☑ **§4.6 amendment + BE refactor** (cont.90, 2026-06-11) — VAT mode/rate/ภ.พ.30 mode moved from env
  `Tax:*` config to `master.companies` (`vat_registered` + new `vat_rate`/`pnd30_submission_mode`,
  migration `CompanyTaxConfig` + ck constraints). `ICompanyTaxConfigService` (scoped, per-request cache)
  feeds 11 Infra services + `/system/info` (now authed) + `/api/v1/system/info`. PUT /companies validates
  + audits `tax_config_change` to activity_log. Tests: `TestCompanyFactory` (fresh company per non-VAT
  scenario — never flips company 1) + `CompanyTaxConfigTests`. FE unchanged (`useSystemInfo()` same shape).
  Build 0/0 · Domain 137/137 · affected classes 24/24 ×2 · Api full 299/0/1 · live smoke 200/401.
- ☑ **FE super-admin companies page** (cont.90, 2026-06-11) — `/settings/companies` (super-admin only,
  nav `superAdminOnly` flag + page gate via `useMePermissions().isSuperAdmin`): list + create + edit
  dialogs; tax section (จด VAT switch, อัตรา VAT เป็น %, วันที่จด, โหมด ภ.พ.30) แยก block + §4.6 warning
  callout. BE เพิ่ม `GET /companies/{id}` (CompanyDetailDto — PUT เป็น whole-row replace ต้อง prefill ครบ).
  🔴 fixed: `PaidUpCapitalCard` PUT เดิมไม่ส่ง vatRate/pnd30 → จะ reset tax config เป็น default เงียบๆ.
  Company admin แก้ข้อมูลบริษัทตัวเอง = หน้า `/settings/company` เดิม (soft fields + registered address).
  tsc 0 · i18n parity 45/45 · CompanyTaxConfigTests 6/6 ×2 · Api full 300/0/1 ×2 · visual-verified.
- ☑ **e2e (Playwright) pass over non-VAT flows** (cont.91, 2026-06-12) — `non-vat-mode-pdf.spec.ts`
  rewritten per-company: seed 440 non-VAT tenant (company 3, `nonvat-admin`), runs FIRST-CLASS in the
  normal pass (no second env-toggle pass). Full baseline 55/2/0 (cont.92c).

---

## ▶ Payroll module (spec: `docs/superpowers/specs/payroll-module-design-2026-05-31.md` + `payroll-next-session-plan.md`)

- ☑ **P-A — Employee master** (cont.82.1) — entity/CRUD/FE/RLS/perm, committed `f9e65ee`.
- ☑ **P-B — PIT engine** (cont.82.1) — pure `ThaiPitCalculator` + `PitSchedule`, 12 golden, committed.
- ☑ **P-C — PayrollRun → Payslip** (cont.82.2, 2026-05-31) — `PayrollRun`/`Payslip` entities (immutable
  after Post, lifecycle Draft→Approved→Posted + Paid stamp), pure `PayrollMath` (allowances + SSO ม.33),
  config `Payroll:Sso`/`Payroll:Allowances`, `PayrollRunService` (ม.50(1) months-remaining=13−month, YTD
  from prior posted runs same calendar year), GL `PostPayrollRunAsync` (Dr salary+er-sso / Cr pit+sso+net,
  accounts 5400/5410/2153/2160/2170), `/payroll/runs` endpoints + `payroll.run.manage/.post/.pay` SoD perms,
  audit on every transition (§4.8) + `IsActive` inclusion gate, migration `AddPayrollRun` + RLS/seed 480–482.
  Domain 8/8 · Api.Tests **218/218** (+5 payroll ×2) · build 0/0 · live smoke create→approve→post→pay POSTED+Paid
  (`03-2099-PR-0001`, balanced JV, 4 audit rows). **NOT committed — pending Ham.**
- ☑ **P-D — outputs (ALL SHIPPED — last item employee 50ทวิ, 2026-06-12):**
  - ☑ **payslip / payment-evidence PDF** (cont.82.2) — `PayslipPdf` (QuestPDF, self-registers license+Sarabun)
    + `PayslipPdfService` (per-employee + run-zip), endpoints `/payroll/runs/{id}/payslips/{employeeId}/pdf`
    + `/payslips/pdf`. Api.Tests 219/219 ×2 · live PDF+zip smoke. Sample sent to Ham.
  - ☑ **FE payroll UI** (cont.82.2) — `/payroll` list (DataTable, status badge, create modal) +
    `/payroll/[id]` detail (totals, approve/post/pay/delete gated by SoD perms, payslip table +
    per-row PDF + run-zip download) · nav section + i18n th/en (35-key parity) · FE tsc 0.
    (Not visually spot-checked — tsc gate per §6; mirrors the employee/DataTable patterns.)
  - ☑ **ภ.ง.ด.1 / ภ.ง.ด.1ก** — **AcroForm fill** (Ham: fillable → via `RdAcroFormFiller`, not bespoke).
    - ☑ **ภ.ง.ด.1 monthly** (cont.82.2) — field map (`Pdf/Templates/pnd1_fieldmap.md`, decoded from /Rect) →
      `Pnd1FormFiller` (main + ใบแนบ, comb taxid, 8/sheet, PdfSharp merge) + `Pnd1FilingService` +
      `GET /payroll/runs/{id}/pnd1/pdf` + FE button. Api.Tests 220/220 ×2 · live 3-page render + sample sent.
      ☑ **visual-validated 2026-06-11 (Claude, Ham delegated)** — v5: name split ✓ (นาย สมชาย / ใจดี แยกช่อง),
      month radio ✓ (☑ มีนาคม), address ✓, taxid comb ✓, ยอด p1↔ใบแนบ ตรง. ~~🟠 radio เดือน sourced from
      `PeriodYearMonth` (งวด) but ม.59 files by PAYMENT month~~ → **✅ FIXED (cont.91, commit `983897c`):**
      month/year now from `run.PayDate` per ม.52/ม.59 (สปส.1-10 stays wage-period month — different law).
    - ☑ **ภ.ง.ด.1ก annual** (cont.82.2) — `Pnd1aFormFiller` (landscape ใบแนบ + address col) +
      `BuildPnd1aAnnualAsync(year)` (aggregate posted runs/year/employee) + `GET /payroll/pnd1a/pdf?year` +
      FE button. Live render 2099 OK. Also: registered address now editable (DBD/ภ.พ.09 warning gate).
    - ☑ **SSO contribution file (สปส.1-10)** (cont.82.3) — **BUILT end-to-end + live-verified.** Official PDF is FLAT
      (RdAcroFormFiller can't reuse) → Ham chose the **e-Service TEXT file**. `ISsoFilingService`→`SsoMonthlyModel` +
      `SsoFilingService` + `SpsBatchFormat` (135-char fixed-width, TIS-620, header"1"/detail"2") + endpoint
      `GET /payroll/runs/{id}/sso/file` + FE button + i18n. Verified vs Ham's `docs/SSO-Forms/` spec: ค่าจ้าง=actual
      (un-capped), BE dates, 10-digit SSO reg no, numeric prefix codes. Api.Tests **226/226 ×2** · build 0/0 · FE tsc 0
      · live smoke run 2 (50k wage / 750+750). ~~⚠️ verify by a real e-Service upload~~ → **มติ Ham 2026-06-12:
      ไม่ต้อง upload จริง — fill form แทน.** ☑ **สปส.1-10 ส่วนที่ 1 PDF (cont.92d, commit `ebf2761`):**
      `GET /payroll/runs/{id}/sso/pdf` — flat-form overlay via new `RdAcroFormFiller.RenderFlat`/`FlatComb`
      at marker-verified coordinates (`sps110_boxes.json`, 21 keys); account 10-cell comb (blank stays
      blank), branch 6-cell, Thai month/BE year, contribution table + BahtText words; FE button. Visual
      crops read-verified. ~~`EmployerAccountNo` config stopgap~~ → already profile-first:
      `CompanyProfile.SsoEmployerAccountNo` (migration `AddCompanyProfileSsoAccount` 2026-06-01) wins,
      `Payroll:Sso:EmployerAccountNo` config is only the fallback (SsoFilingService.cs:65).
      Spec: `docs/superpowers/specs/sps-1-10-fileformat.md`.
  - ☑ **employee 50ทวิ annual** — SHIPPED 2026-06-12, commit `f66f57c` (see the detailed ☑ entry in
    the Purchase/WHT section: `GET /payroll/employees/{id}/wht50tawi/pdf?year`, 2 copies, SSO box).
  (Stale duplicates removed 2026-06-11: "FE payroll run UI not yet built" — built+☑ above cont.82.2;
  e-Filing `WhtBatchFormat`/`FormatPND1V2_0.pdf` path — superseded by Ham's AcroForm decision.)
- ☑ **2569 SSO `WageCeiling` — RESOLVED (Ham 2026-06-12):** config `Payroll:Sso:WageCeiling`,
  default **17,500** (2569–2571 ceiling → ฿875 @ 5%, `PayrollOptions.cs:15` + appsettings.json);
  override per env via `Payroll__Sso__WageCeiling`.
- ☑ **openapi: payroll group** — DONE (cont.93, 2026-06-12): `/employees` (5 ops) + `/payroll/runs`
  lifecycle + all PDF/file outputs (payslips/pnd1/pnd1a/sso text+pdf/50ทวิ) documented; also added
  SO/DO/BN pdf + BN CRUD, and **fixed a stale contract**: documented `/credit-notes`+`/debit-notes`
  never existed → replaced with the real `/tax-adjustment-notes` surface (+`CreateTaxAdjustmentNoteRequest`
  schema, old CN/DN schemas dropped). 107 paths, YAML parses. **Sana delta — flag on next sync.**

---

## ▶ Corporate Income Tax — ภ.ง.ด.50 + ภ.ง.ด.51 (kickoff: `docs/superpowers/specs/pnd50-51-cit-next-session.md`)

- ☑ **8 decisions LOCKED w/ Ham (2026-06-01, cont.83)** — see spec §"LOCKED DECISIONS": manual adjustment-entry
  UI · order ภ.ง.ด.51→50→ใบแนบ · build real `BalanceSheetAsync` · add `Company` paid-up-capital + auto-SME ·
  loss c/f = per-year override-able store · ภ.ง.ด.51 method A only · PDF-fill only v1 · audited FS/DBD out of scope.
- ☑ **Phase C-A — pure engine (cont.83):** `Domain/Tax/CitRateSchedule` (`General()` 20% flat / `Sme()` 0-15-20,
  legal cite, §4.6) + `CitComputation` (ladder record + `RefundDue`, `LossApplied` exposed for roll-forward) +
  `CitCalculator` (`TaxOnProfit`/`Compute` [statutory order: loss→base, credits→tax] / `HalfYearPrepayment` ม.67ทวิ /
  `UnderEstimatePenalty` ม.67ตรี). Golden `CitCalculatorTests` **18/18** (+2 for 51 prepay/penalty). Domain 0/0. **Committed cont.84.**
  ⚠️ owed: a worked-example anchor from `pnd50/51_instructions.pdf` (rates currently trace to `_meta.md`).
- ☑ **Phase C-B — ภ.ง.ด.51** (ม.67ทวิ mid-year prepay, do FIRST): probe `pnd51_020768.pdf` `/Fields` → `Pnd51FormFiller`
  + `Pnd51FilingService` (P&L H1 + Company header + SME classify) + endpoint `GET /tax-filings/pnd51/pdf?year=YYYY`
  + FE button; store the estimate for the year-end penalty check. Source PDF fillable (probed in kickoff).
  **BUILT + committed cont.84 (2026-06-06):** `IPnd51FilingService` + `Pnd51FilingService` (fiscal-year H1 estimate ×2,
  caller override, `isSme` picks SME/General schedule, header from CompanyProfile) + `Pnd51FormFiller` (RdAcroFormFiller,
  embedded `pnd51_main.pdf`) + endpoint `GET /tax-filings/pnd51/pdf?year&estimatedProfit&whtH1&isSme` (FilingPreview) +
  FE `tax-filings/pnd51` page + i18n. Build 0/0 · Domain 18/18 · Api Pnd51 2/2. ☑ visual-validated 2026-06-11
  (Claude, Ham delegated — live render: header/period ✓, worksheet foots: estimate 1,234,567.89 → half 617,283.94
  → carried to รายการที่ 1 ✓); auto-SME deferred to C-C (needs `PaidUpCapital`); store-the-estimate wired in C-C.
  - ☑ **page-2 Task 2 — page-aware Render (cont.85, committed `bf45143`):** overlay each field onto its own widget's
    page; per-page sizes + `/Annots`→page map; flatten all pages; `copies` = full set. No new API / no pnd51 branch —
    single-page output pixel-identical (50ทวิ + ภ.ง.ด.1 crops + pnd51 p1). `RdAcroFormFillerMultiPageTests` 2/2 · suite 62/62.
  - ☑ **page-2 Tasks 3+4 — worksheet model + attestation guard (cont.86, 2026-06-10):** `Pnd51Worksheet` record + `Pnd51Attestation`
    (5 flags) + `BuildPnd51Async(…, fillWorksheet, attest)`; pure `Pnd51FilingService.BuildWorksheet` computes Method-A boxes and
    **throws unless clean + footing** (added `estimate>0` & `tax≥WHT` over the plan's 5 flags — `HalfYearPrepayment` clamps, so
    WHT>tax would non-foot; general-rate only in v1). `Pnd51WorksheetTests` 12/12 (pure) · `Pnd51` 14/14 ×2. **Page 2 not rendered yet.**
  - ☑ **page-2 Tasks 1+5 — radio map + render (cont.86b, 2026-06-10):** draft radio map was sorted bottom→top (REVERSE of
    `BuildRadioCells`) → rebuilt C#-true (`pnd51_p2radio_cs.txt`), corrected `pnd51_p2_radiomap.md`, render-confirmed every tick.
    `Fill` now renders worksheet boxes (51/52/53-54/57-58/59-60/28-29/32/33/35/36-37/39-40) + 9 radios when attested.
    `Pnd51` 15/15 ×2 · visual gate passed (foots end-to-end), crops sent to Ham.
  - ☑ **page-2 Task 6 — endpoint params + FE attestation gate (cont.86c, 2026-06-10):** `fillWorksheet`+5 attest query params
    → `Pnd51Attestation`; openapi `/tax-filings/pnd51/pdf` added (rest of `/tax-filings/*` still undocumented — Sana delta);
    FE toggle + 5 checkboxes (download gated; SME blocked). Live e2e 4/4 (200/422/200/422) · FE tsc 0 · Pnd51 15/15 ×2.
    **PAGE-2 PLAN COMPLETE.** Deferred: SME % radio (ask Ham), Method B, ชำระไว้เกิน, store-estimate ม.67ตรี.
- ☑ **Phase C-C — ภ.ง.ด.50 main (COMPLETE — v2 default, Ham validated 2026-06-11):**
  - ☑ **C-C FOUNDATIONS (cont.87, 2026-06-10 — plan `2026-06-10-pnd50-cc-foundations.md`):** `CitLossCarryForward`
    ม.65ตรี(12) golden 12/12 · `tax.cit_year_summaries` (per-FY override-able store + ภ.ง.ด.51 estimate/prepaid,
    persisted via `POST /tax-filings/pnd51/estimate`) + `tax.cit_adjustments` (signed ม.65ตรี lines) + RLS
    · `Company.PaidUpCapital` (+migration `AddCitYearStoresAndPaidUpCapital`) · auto-SME `ProfileAsync` (≤5M ∧ ≤30M,
    null→General) · **real `BalanceSheetAsync`** + `GET /reports/balance-sheet` · `CitEndpoints` + FE `/tax-filings/cit`
    + settings paid-up-capital + i18n. Build 0/0 · Domain 137/137 · Api 23/23 ×2 · tsc 0. openapi +9 paths.
  - ☑ **C-C FORM FILL:**
    - ☑ recon (cont.87b): `pnd50_050369.pdf` = **7 pages, 478 widgets** (not 120/192 — those were
      main-p1/attach probes); per-page label-joined dumps in `docs/RD-Forms/pnd50/fieldmap/` + draft map
      + v1 scope in `docs/superpowers/specs/pnd50-fieldmap-recon.md`. p2 = รายการที่ 1 คำนวณภาษี
      (662/665/666/670/672 + Group4/5/21/6/7/8); p6 = งบฐานะ (`BalanceSheetAsync` ready).
      ❗Open: กำไรสุทธิ box 46-47 widget not found on its row — resolve with 0-fill raster FIRST.
    - ☑ **build v1 (cont.88, 2026-06-11 — plan `2026-06-11-pnd50-form-fill.md`):** `pnd50_cells.json`
      geometry (taxid 13 cells, boxes 661-672 = 11+2) → `RdRadio` on-state selection (unknown state
      throws) → pure `BuildSheet` + §4 guard `pnd50.not_attestable` (11 tests) → `Pnd50FormFiller`
      (p1 header + p2 รายการที่ 1, radios by on-state per radiomap) → **visual gate passed** →
      ☑ **validated 2026-06-11 (Claude, Ham delegated):** profit/loss crops — header/taxid/address/period/
      radios/amount-pairs all correct, p1↔p2 foots. (v1 SME crop was an alignment artifact carrying General
      figures — superseded; v2 SME PDF re-validated: taxable 90,000 → tax 0 per SME schedule ✓.) →
      `Pnd50FilingService` (CitProfile + store estimate/prepaid + FY WHT register +
      `CitCalculator.Compute`/`UnderEstimatePenalty`) + `GET /tax-filings/pnd50/pdf` + openapi +
      FE card on `/tax-filings/cit`. Api 277/277 ×2 · tsc 0 · live smoke 200/422.
    - ☑ **v2 = default (cont.89, 2026-06-11 — plan `2026-06-11-pnd50-v2-dashboard.md`):** p3 รายการที่ 2
      ladder (col ③ only, `BuildLadder` pure + sign-flip refusal) + p3 รายการที่ 3 zeros + p6 งบฐานะ
      (`MapBalanceSheet` account-code classifier) rendered from real data; v1 adjustments/loss refusals
      DROPPED. Fixed latent double-count (feed `AccountingNetProfit` not `EffectiveNetProfit`). New
      refusals: override-breaks-ladder, ladder sign-flip, surcharge+overpaid → `pnd50.not_renderable`.
      `ComposeAsync` single-source → `GET /tax-filings/pnd50/preview` + CIT dashboard on `/tax-filings/cit`
      (ladder/WHT-cert/balance-sheet cards + refusal warnings). p3/p6 radiomap render-confirmed. Visual
      gate passed. ☑ **validated 2026-06-11 (Claude, Ham delegated):** p3 ladder profit foots ทุกแถว
      (5M→100k→+50k→−20k→−40k→90k, radios ถูก) · p3 loss signs ✓ (ขาดทุนสุทธิ ×2, abs amounts) · p6 balanced
      811,111.10 ทั้งสองฝั่ง, ทุนจดทะเบียนเว้นว่าง, กำไรสะสม tick ✓ · SME math ✓ (tax 0, overpaid 15,003.25).
      **พร้อมใช้ยื่นจริง** (Ham ยังควร spot-check ก่อนยื่นครั้งแรกกับ RD). Api 294/294 ×2 · Domain 137/137 ·
      tsc 0 · build 0/0. DEFER ยื่นเพิ่มเติม path + p4-5/p7 detail.
- ☑ **Phase C-D — ภ.ง.ด.50 p4/p5/p7 schedules (cont.92, 2026-06-12 — plan `2026-06-12-pnd50-cd-schedules.md`):**
  recon consolidation (maps `pnd50_p{4,5,7}_map.md`, cells.json 69→141, p7 radios render-confirmed,
  traps Text35.2011 / no-Text35.188) · `ExpenseByAccountAsync` (per-account FY expenses, same basis as
  ProfileAsync) · pure `BuildExpenseSchedule` (รายการที่ 7 = partition by code convention
  5400/5100/5300/5350/5200→lines 1/6/9/11/19, rest→22) + `BuildDisallowedSchedule` (รายการที่ 8 ←
  positive adjustments by exact LegalRefCode/label) — both foot-guard vs ladder rows 8/11
  (`pnd50.schedule_breaks_ladder`) · filler p4 zeros-by-design + ร.5/ร.6 totals tied to ladder ·
  p5 ร.7/ร.8 col ③ explicit zeros · p7 header only (ชื่อ + รอบบัญชี พ.ศ.; director Q/sign NEVER
  auto-ticked) · ม.71ทวิ → informational refusal `pnd50.disclosure_required` (revenue >200M warns,
  PDF still renders) · preview + CIT dashboard ร.7/ร.8 cards + openapi (2 schemas). Visual gate
  passed (crops sent to Ham 2026-06-12 — **☑ Ham approved 2026-06-12 "ดูดีเลย" — พร้อมใช้ยื่นจริง**). Api 314/0/1 · Domain 137/137 ·
  tsc 0 · i18n 98/98. **ใบแนบ ก-จ separate PDFs = OUT (market fills none; stay attest-blank).**
  ~~DEFER: finance-cost line placement~~ → **RESOLVED (cont.92b):** 5500-5599 → ร.7 ข้อ 12 only —
  forced by the flat-P&L foot invariants (ร.6 must foot ladder row 6 == 0), not RD interpretation.
- ☑ **WHT self-withhold gross-up + 50ทวิ เงื่อนไข (cont.93b, 2026-06-12 — Ham "แก้เลย พร้อม FE"; spec
  `2026-06-12-wht-grossup-design.md`):** ปิด 2 ช่องโหว่ scenario "ผู้รับไม่ให้หัก" (บริการออนไลน์/foreign) —
  (1) ฐานภาษีไม่ gross-up: `WhtPayerModes` (Domain pure: DEDUCT / GROSS_UP_FOREVER ตลอดไป net÷(1−r) /
  GROSS_UP_ONCE ครั้งเดียว net×(1+r)) + `PaymentVoucher.WhtPayerMode` (+ck) — legacy selfWithhold=true และ
  foreign auto ⇒ FOREVER (RD-safe), cert income = grossed, GL ใช้ยอด gross อัตโนมัติ; (2) 50ทวิ ติ๊กช่อง
  ผู้จ่ายเงิน: `WhtCertificate.WhtCondition` 1|2|3 (+ck) → filler chk8/chk9/chk10 (template probe-verified).
  Migration `AddWhtPayerModeAndCondition`. FE: PV form toggle ใหม่ "ผู้รับเงินไม่ให้หัก (ออกภาษีให้เอง)" +
  radio 2 วิธี + live chip นำส่งจริง/อัตราแท้จริง + paper preview ไม่ลบ WHT (จ่ายเต็ม) + detail badge + cert
  เงื่อนไข badge; BE PV PDF เลิกแสดง WHT เป็นรายการหัก → disclose เป็น note. openapi `whtPayerMode`/`selfWithholdMode`.
  Gates: Domain **9/9** golden · Sprint87 **8/8 ×2** · **Api full 325/0/3** · tsc 0 · i18n parity ·
  live smoke PV 06-2026-PV-CAPEX-0001 (wht 309.28, จ่ายเต็ม 10,700, cert income 10,309.28 cond 2) ·
  visual ✓ (50ทวิ ☒(2)ออกให้ตลอดไป + PV PDF + FE form ส่งให้ Ham).
- ☑ **PV product-driven lines + derived VAT (cont.94c, 2026-06-13 — Ham "หมวดซื้อไม่ควรตั้ง VAT manual +
  ดึงจาก products + vendor ไม่จด VAT ห้าม VAT"; spec `2026-06-13-pv-product-picker-vat.md`):** PV form
  เคยให้ตั้ง VAT มือ + free-text + ProductTypeSelect (ผิด pattern — sales ใช้ ProductPicker+derived
  มานานแล้ว). FE: เปลี่ยนเป็น `ProductPicker (purpose="purchase")` + VAT read-only derived
  (`vendorVat ? taxRateForProductType(type) : 0`); master seed ราคาเฉพาะ line ว่าง (ไม่ lock ราคา).
  BE guard `pv.vendor_not_vat_registered` (ม.82/5 — vendor ไม่จด VAT + VatRate>0 → 422; foreign ไม่โดน
  เพราะ VatRegistered=true ผ่าน ภ.พ.36). WHT คงเดิม (Ham OK). +2 tests (Sprint87 10/10 ×2). Api full
  **331/0/3** · tsc 0 · i18n parity · openapi +422 · visual ✓ (7% / 0%-ผู้ขายไม่จด VAT).
- ☑ **Tax Summary Dashboard — สรุปภาษีรายเดือน (cont.94, 2026-06-13 — Opus 4.8; Ham "สร้างเลย พร้อม
  dashboard + detail + visualization"; spec `2026-06-13-tax-summary-dashboard.md`):** หน้าเดียวรวม
  ภาษีที่เคยกระจาย 5–6 หน้า. `ITaxSummaryService` (`GET /reports/tax-summary?year=`, perm
  `report.profit_loss.read`) aggregates per-month: รายได้/รายจ่าย (GL Posted), VAT (reuse `GetPnd30Async`
  ×12 — DRY, respect claim-period), WHT นำส่ง split ภ.ง.ด.3/53/54/1 + WHT ถูกหัก (Direction P/R) จาก
  wht_certificates. FE `/reports/tax-summary`: year selector + 6 KPI cards + 2 inline-SVG bar charts
  (รายได้-vs-รายจ่าย, VAT/WHT — no chart dep) + ตาราง 12 เดือน + แถวรวม + drill-down (VAT→ภ.พ.30,
  WHT→tax-filings, WHT-recv→wht-receivable); nav PieChart + i18n th/en. `TaxSummaryTests` 3 ×2.
  **Api 328/0/3** · tsc 0 · i18n parity · openapi +1 (108) · live smoke 2026 ✓ · visual gate ✓.
  - ☑ **BU filter (cont.94b, 2026-06-13 — Ham "มี Filter BU ยังนะ"):** optional analytical BU lens
    threaded end-to-end — GL revenue/expense (journal_line.BusinessUnitId, exact), VAT (added
    `int? businessUnitId` to `GetPnd30Async`/`GetRegisterAsync` → filter TI/CN-DN/VI by BU), WHT
    (cert has no BU → join source PV `Direction='P'` / Receipt `Direction='R'` header BU; cross-BU
    receipts fall outside). FE BU dropdown + amber note (ภ.พ.30/ภ.ง.ด.50 filed company-level). `+1`
    test `Business_unit_filter_isolates_revenue_and_expense`. **Api 329/0/3** · tsc 0 · live smoke
    BU REPT (rev 3,000/VAT 210 vs company 22,600) · visual ✓.
  Out v1: ภ.พ.36 line (net-zero, อยู่ใน VAT register) · CIT estimate (dashboard เอง) · PDF export.
- ☑ **ภ.พ.01/09 v1 identity prefill (cont.92b, 2026-06-12):** `GET /tax-filings/pp01/pdf` + `/pp09/pdf`
  — page-1 company header only (taxid 13-comb, postal 5-comb, address, email/website), print-and-sign,
  no radio ever ticked (form-defect groups recorded in `pp01_map.md`/`pp09_map.md`, all PREFILL fields
  raster-traced). pp09 email comb-flag defect stripped on the embedded template. FE prefill buttons on
  `/documents`. Api 319/0/2 · live smoke 200/200 · visual gate passed (crops `_review/vatreg/`)
  **☑ Ham approved 2026-06-12.**

---

## ▶ Next focus (2026-05-27): Purchase Phase 1 ☑ — then E2E tail + Question-Backend36

**Sales chain CLOSED** (cont.64–69): Q→SO→DO→Invoice→TI→RC + CN/DN, non-VAT mode, full
document chain, universal print — shipped, tested, committed (`7e58d9d`/`65db075`).

☑ **Sprint 13j-PURCH — Purchase / AP Phase 1 (cont.71, 2026-05-27)** — UX parity with Sales:
Purchase audit hooks (PO/VI/PV + WHT), AP Aging report + `/reports/ap-aging`, PO+PV PaperDocumentPdf
consolidation (+ `AddPrintTrackingToPurchaseChain` migration), FE PaperDocument/chain/PrintMenu on
PO/VI/PV/WHT, AP Aging page, PO `/new` lift, expense-category list. BE 174/174 (run 1) · FE tsc 0 ·
build 0/0 (54 routes). NOT committed. Detail: `docs/Report-Backend35.md` + `progress.md` cont.71.
- ☑ **tail (2026-05-27):** E2E `purchase-chain.spec.ts` written + PASS ×2 · Flag-1 (VI on-screen PaperDocument) · Flag-2/BP-05 (bidirectional chain via downward read-DTO refs) · BP-07 (pnd30 full-suite-2× flake fixed — `FuturePeriod` widened + test self-clean → 174/174 ×3). Full BE suite green, FE build 0/0 (66 routes).
- ☑ **wrap (2026-05-28, cont.72):** AFK-batch follow-ups closed — **WAGE WHT default** (seed 460, ม.40(2) PND3 3% + map; SAL stays NULL — payroll subsystem deferred) · **C — VI mandatory vendor-TI attachment** (Post throws `vi.attachment_required` when no attachment, FE banner + disabled Post; all 5 BE VI-post tests + 2 e2e specs updated, new positive guard test) · **F — Question-Backend36** ☑ shipped (new `IPurchaseChainService` + `GET /documents/purchase-chain` own DTO, FE `PurchaseDocumentChain` swapped to single `usePurchaseChain` hook). Suite **178/178 ×2** on teas_test, Domain 89/89, Purchase + RBAC e2e green, FE tsc 0. 9 commits local on `main`, awaiting remote URL to push.
- ☑ **WHT 50ทวิ 2-copy (cont.74, 2026-05-29):** `Wht50TawiFormFiller.FillCopies` → 2-page PDF (ฉบับ1+ฉบับ2, byte-identical; template pre-prints both labels) via page-tree `/Kids` duplication (preserves catalog AcroForm + NeedAppearances); `WhtCertificateService.BuildPdfAsync` wired to it; dropped the broken `CopyLabel→item` write. BE 0/0 · Api.Tests 180/180 ×2. **NOT committed.**
  - ☑ **50ทวิ Thai-font render — RESOLVED (cont.75):** the FLAG was real (PdfSharp can't shape Thai → mai ek dropped in all non-Acrobat viewers). Rewrote render from AcroForm `/V`+NeedAppearances to a **QuestPDF/Skia overlay + flatten** via new generic `RdAcroFormFiller` (reads field `/Rect`, embeds Sarabun, viewer-independent — verified in headless pdfium). `Wht50TawiFormFiller` now a thin mapper. BE 0/0 · Api.Tests 180/180 ×2. **NOT committed.**
  - ☑ **50ทวิ FE download** — already shipped (`PrintMenu` on the cert detail page → `/wht-certificates/{id}/pdf`); cont.73 item was stale. Verified cont.75.
  - ☑ **50ทวิ PDF persistence — DECIDED: REGENERATE** (2026-06-12, Ham delegated "ส่วนที่เหลือทำเลย"):
    no `PdfStoragePath` column/storage — the cert is deterministic from the immutable posted snapshot
    (same engine, same template, same data ⇒ same PDF), so storing adds infra + drift risk for zero
    audit value. Revisit only if RD/auditor demands byte-identical archived copies.
  - ☑ **employee 50ทวิ annual (P-D #4, 2026-06-12, commit `f66f57c`):** `GET /payroll/employees/{id}/
    wht50tawi/pdf?year` — aggregates posted slips by PAYMENT year into one ม.40(1) row, 2 copies,
    SSO-contribution box filled (new optional `Wht50TawiData.SsoContribution` — vendor certs unaffected);
    FE per-row button on posted run detail. Test ×2 + live visual (ภ.ง.ด.1ก ✓ ศูนย์บาทถ้วน ✓ สปส 750.00 ✓).
- ☑ **RD-Forms PDF-fill scoping (cont.75):** generic `/Rect`-driven engine (no per-form coord tuning); `docs/RD-Forms/TEAS-FORM-FILL-PLAN.md` written. **Finding:** monthly returns file via RD Open API (Strategy B, already in `TaxFilings`), NOT PDF-fill → only 50ทวิ needs official-PDF-fill.
  - ☑ **ภ.พ.01/ภ.พ.09 print-and-sign — SHIPPED (cont.92b, 2026-06-12, commit `2d52a7e`):** see the
    dedicated entry above (v1 identity prefill, fieldmap discipline followed, Ham approved).
- ☑ **Sales track (not Purchase scope, Req §6) — both resolved (verified 2026-06-12):** BP-08 — spec now
  resolves the expense category AS ap_clerk (company-scoped, seed 440, picks active PROF) · BP-10 —
  `q-status`/`so-status`/`bn-status` data-testids live on quotations/sales-orders/invoices detail pages;
  full e2e baseline 55/2/0 (cont.92c).

Then Reports depth. See `docs/accounting-system-plan.md` §7 + §17.3. Carry the cont.69
follow-ups below into the purchase work where they overlap.

## Now / Next (highest impact)

1. ☑ **Real EF migration** — `20260516021710_Initial` generated; `IDesignTimeDbContextFactory`
   added; `DbInitializer`/`PostgresFixture` now `MigrateAsync()`. (2026-05-16)
2. ☑ **Integration vs real Postgres** — native PG 16.4 portable (port 5433, no Docker);
   tenant-isolation test PASS. Deeper service pack (NumberSequence concurrency, PV+WHT,
   period gating) still ☐ — see "Test depth" below; TI immutability + GL balance proven via #3.
3. ☑ **Runtime smoke** — full login→post-TI→GL→immutability verified end-to-end. (2026-05-16)

### Test depth — ☑ ALL DONE (stale list; reconciled 2026-06-12)
All four exist in `Hardening/Sprint1HardeningTests.cs`: NumberSequence gapless+unique under
25-way parallel · period-close gating (`period.closed`) · PV+WHT 3% → 50ทวิ + balanced JV ·
service-level providers built via full `AddInfrastructure` (same pattern as `TestCompanyFactory`).

## Non-VAT mode completion (cont. 67, 2026-05-23)

Spec: `docs/superpowers/specs/2026-05-23-non-vat-mode-design.md`. 4 decisions locked w/ Ham (async).
- ☑ **Phase 1** — VAT-artifact hiding: `PaperSummary.ShowVat` (BE+FE), `PaperFoot` single-Total row,
  `LineItemsTable` VAT column hidden, `SidebarNav` ภ.พ.30 hidden (ภ.ง.ด.3/53 kept), `/reports/pnd30`
  route guarded. e-Tax covered by existing TI-detail gate.
- ☑ **Phase 2** — Block TI (`TaxInvoiceService.EnsureVatRegistered` in Create+Post; live-verified 422
  `ti.non_vat_blocked`) + FE create-buttons gated. taxRate>0 on pre-sale docs = scope decision (FE-hidden;
  not BE-enforced — VAT realized only via TI which is blocked).
- ☑ **Phase 3a (BE)** — non-VAT billing path. `ReceiptApplication.TaxInvoiceId` nullable + `DeliveryOrderId`
  (exactly-one check); standalone `ReceiptLine` table; `MarkPosted` source = TI/DO apps OR own lines; GL
  `PostReceiptAsync` branches Cr Sales 4000 (cash basis) for DO/standalone vs Cr AR for TI. Migration
  `AddReceiptWhtAndNonVatBilling` applied. Live-smoked: standalone receipt create+post 200 (RC-0002).
  Ham confirmed GL (Cr Sales 4000) + that taxInvoiceId nullable is schema correctness (ม.86/13).
- ☑ **Phase 3b (BE)** — ภ.พ.36 non-VAT sunk VAT. `GlAccountsOptions.IrrecoverableVatExpenseAccount` (5350,
  seeded via 240.sql) + `WhtFilingService` branches Dr 5350 / Cr 2151 (non-VAT can't reclaim, ม.83/6) vs
  Dr 1170 / Cr 2151 (VAT). Menu kept visible.
- ☑ **Phase 3 — FE (cont. 68)** — `receipts/new` non-VAT mode shipped: mode selector (standalone / apply-DO)
  when `vatMode=false`; standalone line editor (ProductPicker + qty/price/amount → `Lines[]`); `DeliveryOrderPicker`
  (mirrors TaxInvoicePicker, scoped to customer, Issued+Delivered, excludes TI-combined) → `Applications[].deliveryOrderId`;
  manual WHT rows for non-VAT (no TI to auto-suggest from). VAT mode UI unchanged. BE: `DeliveryOrderListItem` +CustomerId/+TotalAmount.
- ☑ **Tests (cont. 68)** — `NonVatBillingTests` (4): standalone→Cr Sales 4000, DO-apply→Cr Sales 4000 (assert account, not
  just balance), ภ.พ.36 non-VAT→Dr 5350, ภ.พ.36 VAT→Dr 1170. Pass 3× consecutive on shared `teas_test`. Also fixed silent
  WHT-loss (WhtAmount>0 + null type now rejected `rc.wht_type_invalid` — was dropped after cont.66 multi-WHT refactor).
- ☑ **Verify (cont. 68):** FE tsc 0 · next build 0/0 (52 pages) · dotnet build 0/0 · Domain 89/89 · NonVat 4/4 ·
  live-smoke both modes on :5080 (RC-0003 standalone, RC-0004 DO-apply [VatMode=false]; RC-0005 TI-apply [VatMode=true]).
  ⚠️ **VatMode restored to true** in appsettings.Development.json (non-VAT work done; flip to false to re-test non-VAT).
- ☑ **WHT auto-sync (cont. 68b)** — non-VAT receipt WHT table mirrors line items (standalone own lines / DO detail
  lines); base auto, user picks income type per row, goods → ไม่หัก. `WhtTypeSelect` trigger truncate+center fix.
- ☑ **Hide VAT-only features in non-VAT FE (cont. 68b, Ham "ซ่อนทั้งหมด + route guard")** — nav TI/CN/DN `vatOnly`;
  DO→TI button + tax-filings ภ.พ.30 link gated; `NonVatGuard` route guards on /tax-invoices, /credit-notes, /debit-notes
  (list/new/[id]). Kept: Q/SO/DO/BN/RC, purchase, WHT certs, ภ.ง.ด.3/53/54, ภ.พ.36, threshold banner, customer VAT checkbox.
- ☑ **openapi delta for Sana (2026-06-12):** stale `/customer-receipts` → real `/receipts` (+post/+pdf) with the
  shipped `CreateReceiptRequest` (camelCase: `lines[]`, `applications[].deliveryOrderId`/`billingNoteId`, `whtLines[]`)
  + `GET /documents/chain` + `POST /delivery-orders/{id}/create-invoice` + `POST /billing-notes/{id}/create-tax-invoice`
  + `mark-printed` (Q/SO/DO/Invoice). YAML parses, 82 paths. **Sana delta — flag on next sync.**

## Invoice flow + full chain + universal print (cont. 69, 2026-05-23) — SHIPPED via sub-agents

Spec: `docs/superpowers/specs/2026-05-23-invoice-flow-related-docs-print-design.md`. Flow: VAT `Q→SO→DO→Invoice→TI→RC`, non-VAT `Q→SO→DO→Invoice→RC`.
- ☑ **Phase 1 (BE)** — drop combined-TI auto (fix 422); `BillingNote.DeliveryOrderId` + CreateFromDeliveryOrder; `TaxInvoice.BillingNoteId` + CreateFromBillingNote (VAT-only); receipt apply-Invoice (Cr Sales 4000); migration `AddInvoiceFlowLinks`.
- ☑ **Phase 2a (FE)** — DO→Invoice + Invoice→TI buttons; receipt InvoicePicker (non-VAT). **2b** — rename → Invoice/ใบแจ้งหนี้, route `/invoices`.
- ☑ **Phase 3** — `GetChainAsync` + `GET /documents/chain` + FE `<DocumentChain>` (full Q→RC) on all 8 detail pages.
- ☑ **Phase 4** — print ต้นฉบับ/สำเนา + tracking on Q/SO/DO/Invoice (migration `AddPrintTrackingToSalesChain`); universal `PrintMenu` + `ChainRowPrint`.
- ☑ **Follow-ups:** D5–D8 — shipped exactly as assumed (rename depth / FK links / full chain / universal
  print) — **☑ Ham confirmed "ตามนั้น" 2026-06-12**;
  ~~Sprint10ProductTests RED~~ (passes since ≤cont.90); ~~hide DO→Invoice button~~ (done —
  `d.billingNoteId == null` gate); ~~CN/DN chain-row routing heuristic~~ → **FIXED (cont.93):**
  `ChainNode.NoteType` ("Credit"/"Debit") added BE-side (`DocumentCrossRefService` projection) +
  FE `isDebitNote` reads it (docNo sniff kept only as legacy-cache fallback). Chain tests 11/11 ×2 ·
  full Api 322/0/3.
- ⚠️ **Commit the (currently untracked) Migrations/** with the code — an `ef remove --no-build` on a stale
  build reverted an untracked migration's Down on the dev DB this sprint. Never `dotnet ef` with `--no-build`
  after entity edits.

## Compliance hardening (before any production use)

4. ⏸ **e-Tax XAdES-BES** — see TECHNICAL DEBT below. Decision (Ham, 2026-05-16): do NOT
   attempt real e-Tax now; continue all other work. **มติ Ham 2026-06-12:** actionable plan
   moved to its own file `docs/superpowers/plans/etax-xades-production-plan.md` — GATED,
   do NOT execute until Ham orders (activation trigger: company revenue approaching ฿30M).

---

## ⚠️ TECHNICAL DEBT — e-Tax XAdES-BES implemented (inert); round-trip verify open

**2026-05-16 update:** `docs/etax-xades-spec.md` supplied by coworker (resolved the
schema/profile blocker). Ham authorized "implement + dev-cert test, keep inert".
**Implemented** per spec §1/§5: `XadesNs`, `QualifyingPropertiesBuilder`, `XadesBesSigner`
(RSA-SHA512, SHA-512 digests, C14N inclusive, XAdES v1.3.2, 2 signed References incl
`SignedProperties`, decimal X509SerialNumber, BOM-free), `X509CertificateLoader`, custom
`XadesSignedXml.GetIdElement` to resolve `#SignedProperties`. Pipeline still inert
(`ETaxBehaviorOptions.Enabled = false` — never signs/sends at runtime).

**OPEN ITEM — flag to Ham (decision needed):**
- `Emits_mandatory_xades_profile_per_spec` ✅ proves structure + algorithms.
- Round-trip self-verify (spec §5 "Self-verify with CheckSignature") **cannot pass** with
  .NET `SignedXml`: it canonicalizes the XAdES `SignedProperties` as a standalone DataObject
  fragment at sign time vs an in-tree node at verify time; spec §1's **inclusive C14N**
  then captures ancestor-scope namespaces at verify → SignedProperties digest mismatch.
  Exclusive C14N would fix it but **violates spec §1** (non-negotiable) → NOT done
  (CLAUDE.md §8: no improvising on compliance). 3 round-trip tests are `Skip`-ped with
  reason; no misleading-green security tests shipped.
- **Resolution options for Ham:** (a) validate signatures with ETDA's official reference
  validator / `xmlsec1` instead of .NET CheckSignature; (b) write a custom canonicalizer
  that fixes the namespace context; (c) confirm with ETDA whether exclusive C14N is in
  fact accepted (some ETDA samples use Excl). Needs Ham + ETDA confirmation — do not guess.

**Still blocked for PRODUCTION (unchanged):**
1. **Signing cert** — CA-issued `.pfx` (prod: Thailand NRCA/TUC; sandbox: ETDA test cert)
   via `.env` `ETax:Signing:PfxPath/PfxPassword`, never committed. (Dev/test uses an
   in-memory self-signed cert — code & structure verified, no real cert needed for that.)
2. **ETDA sandbox UAT** — submit a signed test invoice; confirm they parse
   `xades:SigningCertificate` / `SigningTime`; resolve the C14N question above there.
3. Flip `Enabled` only in a non-prod env first.

Do NOT touch `docs/Design(Architect).md` (per Ham).

### Test depth (add)
- ☑ `TenantIsolationTests` idempotency — resolved: uses `"ISO-" + Guid` randomized codes
  (`TenantIsolationTests.cs:27`); safe on a re-used DB (verified 2026-06-12).
5. ☑ **WHT certificate split by income type** — `PaymentVoucherService` groups WHT lines by
   `WhtTypeId`, one 50ทวิ per income type w/ own WT doc no + effective rate. (2026-05-16)
6. ☑ **Security package CVEs** — MailKit 4.16.0, Sec.Cryptography.Xml 10.0.8, OpenTelemetry.*
   removed (unused + CVE). NU1902/NU1903 re-enabled as build errors; builds 0/0. (2026-05-16)

## Frontend

7. ☑ **Auth mechanism unification** — BFF: `app/api/auth/{login,logout}/route.ts` set/clear
   httpOnly cookie; `lib/auth.ts` same-origin. Middleware cookie-gate now coherent. (2026-05-16)
   - ☑ ~~Follow-up: generic `/api/proxy/[...path]` BFF~~ — already built and in use
     (`app/api/proxy/[...path]/route.ts`: httpOnly cookie → Bearer, binary passthrough;
     `lib/api.ts` routes through it). Stale marker cleared 2026-06-12; the `lib/api-client.ts`
     header comment still says TODO — cosmetic.
8. ◐ Build out dashboard screens per `docs/Design(UI).md`.
   - ☑ **Receipt itemization + multi-category WHT** (cont. 66, 2026-05-22) — receipt now
     lists derived goods/service line items (TI no in notes) + WHT split per income type
     (rent 5% / service 3% / ads 2%), pro-rata to partial payment; one 50ทวิ → N
     `WhtCertificate` R rows; WHT not printed on receipt. New `ReceiptWhtLine` +
     migration `AddReceiptWhtLines` + pure allocator (8 tests). Spec
     `docs/superpowers/specs/2026-05-22-receipt-itemize-multi-wht-design.md`. Gates green.
     **Open (PG-integration, Ham/Sana live):** multi-cert post, GL balance, openapi delta.
   - ☑ Sprint 2-4: TI/Receipt/CN/DN list+detail+create.
   - ☑ **Sprint 5 (Purchase UI — partial):** sidebar "ซื้อ"; `/vendors`
     list+new+detail; `/payment-vouchers` & `/wht-certificates` list+detail (read);
     `VendorSelector`, `ExpenseCategorySelector`; backend PV/WHT/vendor read surface
     + 50ทวิ QuestPDF; gotcha#2 `/vendors` nullable fix. Gates 6/6 green. (2026-05-16)
   - ☑ ~~Sprint 5 paused (Question-Backend5)~~ — superseded: Sprint 5.5 delivered B1 (VendorInvoice
     backend) + Sprint 6 delivered B2 (PV approve/post UI) + the blocked e2e specs (8/8). See the
     Phase 2/3 backlog entries below. (Stale marker cleared 2026-06-12.)

## Phase 2/3 backlog (per docs/accounting-system-plan.md §22)

- ☑ Sales pre-fiscal flow: Quotation → SO → DO — shipped Sprint 10 Part B/C (§23.8) + lifecycle/forms
  polish 13e/13h/13i (stale marker cleared 2026-06-12)
- ☑ Purchase: Vendor Invoice (PI) → Payment Voucher (Sprint 5.5 + 6 shipped; only the cosmetic
  sonner-toast UX note below stays open).
  - ☑ **Sprint 5.5 backend DONE** (signed off): VI entity/EF/migration/GL/endpoints;
    PV B2 Draft→Approved→Posted (`ck_pv_sod`); ม.82/4 window + §5 closed-claim
    rejection; 060/140 SqlScripts; 6 new tests green. (2026-05-16)
  - ☑ **Sprint 6 DONE** (4 phases, gated): 6A PV-settles-VI GL (Dr AP) +
    settled_amount roll-up UNPAID→PARTIAL→PAID + concurrency; 6B VatReportService
    purchase side re-pointed → `VendorInvoice.vat_claim_period`; 6C `/vendor-
    invoices` list+new+detail + PV create + PV approve/post UI; 6D e2e 8/8 +
    5 screenshots. Backend Api 27/27 + Domain 32/32, tsc 0, next build 0, 0
    regression. Seeds 150/160/170 (expense categories, approver user, SVC→WHT).
    PV line ExpenseAccountId/WhtTypeId category-default fallback. (2026-05-16)
  - ☑ ~~**Follow-up — Purchase RBAC seed gap (KI-01):** `110` never inserted
    `purchase.payment_voucher.{create,post,read}` rows/grants for non-super
    roles.~~ **✅ resolved Sprint 7-half** — `180_seed_pv_purchase_perms.sql`
    (3 perms + grants SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/
    AP_CLERK; + ap_clerk/sales_staff DEV users). e2e
    `payment-voucher-non-super-rbac` 2/2 green; perm count = 4. (2026-05-16)
    See §23.1.
  - ☐ **Minor UX — sonner toast overlaps the action bar** briefly after save/
    approve (caused an e2e flake; worked around with force-click). Consider a
    top offset / shorter duration. Cosmetic; Sana UX call.
- ☑ **Sprint 8 DONE** (Business Units — first wired GL dimension; 4 phases, gated):
  `master.business_units` + `companies.requires_business_unit` opt-in + nullable
  `business_unit_id` on TI/Receipt/TaxAdjustmentNote/JournalLine; numbering
  `MM-YYYY-PREFIX[-BU]-NNNN` (reused PV sub-prefix infra); GlPostingService
  snapshots doc BU → every journal_line; Receipt cross-BU = header NULL + per-line
  BU + `crosses_business_units` warn (no block); ONE additive idempotent
  `200_add_business_units.sql` + EF `20260517021031_AddBusinessUnits` (no model
  drift); `210_seed_business_unit_perm.sql`; IBusinessUnitService CRUD+endpoints+
  `master.business_unit.manage`; report filter `business_unit_id`+
  `include_unspecified` on `/tax-invoices` & `/receipts`; UI /settings/business-
  units + company toggle + 4-form dropdowns + list filter chips + detail BU chips
  + cross-BU warn chip + i18n th/en. NO backfill. 4 mid-sprint design flags all
  ACCEPTED by Sana (see Report-Backend10). Gates: backend 0/0, Domain 34/34
  (32+2), Api 37/37 (27+10, 0 regression, 0 skip), tsc 0, next build 0,
  **Playwright 15/15** (13+2), no EF drift, DbInitializer idempotent. See §23.3.
  (2026-05-17)
- ☑ **Sprint 8.5 DONE** (VAT-mode polish for non-VAT companies; small surgical):
  `DocumentLabels` resolver + TI/CN/DN PDF branching on `Tax:VatMode` (ม.86 /
  ม.82/9); e-Tax CTA gated behind `useSystemInfo().vatMode`; `IVatThresholdService`
  + `GET /system/vat-threshold-status` + ม.85/1 dashboard banner; `TaxConfig`/
  `VatModeOptions` + `NonVatDocLabelTh/En`. Gates: backend 0/0, Domain 41/41
  (34+7), Api 41/41 (37+4, 0 regression), tsc 0, next build 0, **Playwright
  16/16** (15 @VatMode=true + 1 @VatMode=false). DoD #9 manual ×8 = agent-
  infeasible (substituted by deterministic unit + e2e; human spot-check
  recommended). See §23.4. (2026-05-17)
- ☑ **Sprint 8.6 DONE** (AR-side WHT — customer withholds from us; spec-first
  gate Question-Backend12 then phased P1–P6): Receipt WHT capture + GL
  `Dr Bank cash_received + Dr 1180 = Cr AR` + `WhtCertificate` Direction='R';
  `IWhtTypeService` effective-date + change-rate; 13 WHT types (220) + 1180
  CoA (230); `/settings/wht-types` + Receipt form WHT + detail/list/PDF +
  `/reports/wht-receivable`. R-B1a manual base (no Product master → Sprint 10).
  Gates: build 0/0, Domain 45/45, Api 48/48 (0 regr), tsc 0, next build 0,
  **Playwright 18/18**, no EF drift. Bug caught by gate: WhtCert (company,
  doc_no) unique wrong for Direction='R' → filtered + migration. See §23.5.
  (2026-05-17)
- ☑ **Sprint 8.7 DONE** (online subscriptions + foreign vendor; phased P1–P4):
  Vendor IsForeign/HasThaiVatDReg/CountryCode (+2 CHECKs); PV self-withhold
  gross-up GL + auto-detect; VI receipt-only GL (VAT lumped, ม.82/5);
  RequiresPnd36ReverseCharge auto-set for Sprint-9 ภ.พ.36; vendor/PV/VI form
  chips + PV detail badge. `is_vat_registered`=existing VatRegistered (reused).
  Gates: build 0/0, Domain 53/53, Api 53/53 (0 regr), tsc 0, next build 0,
  **Playwright 20/20**, no EF drift, GL balance + CHECK + pnd36 asserted.
  Data side only — ภ.พ.36/ภ.ง.ด.54 generators = Sprint 9. See §23.6. (2026-05-17)
- ☑ **Sprint 9 DONE & shipped (2026-05-17)** — Reports + Tax Filings (the big
  one; 3 Parts, gate between each; Q-Backend13 R-Q1a+R-Q2+R-Q3 all ACCEPTED).
  25/25 DoD. Final gate **Playwright 25/25**, Domain 60/60, Api 66/66 (0 skip/
  regr), build 0/0, no EF drift, mirror synced. See §23.7 + Report-Backend14.
  - ☑ **Part A DONE & gated** (Financial Reports): A1 `GET /reports/trial-balance`
    (as-of, normal_balance, **Σ Dr == Σ Cr invariant** badge), A2 `GET
    /reports/profit-loss` (flat Revenue−Expense=NetProfit by BU + payload `note`
    disclosing GP/COGS Phase-2 deferral — R-Q1a, not silently omitted), A3 `GET
    /reports/sales-summary` (customer|business_unit; product→400 till Sprint 10 —
    R-Q2), A4 WHT-Receivable aging buckets (current/30/60/90+) + CertReceived/
    Reconciled flags. 3 UI routes + sidebar Reports section + i18n. Gates: build
    0/0, no EF drift, Domain 53/53, Api **58/58** (53+5 Sprint9, 0 skip/regr),
    tsc 0, next build 0, **Playwright 22/22** (21 @ VatMode=true incl. new
    trial-balance + profit-loss; 1 @ VatMode=false). Mirror synced. (2026-05-17)
  - ☑ **Part B DONE & gated** (VAT compliance): TaxCode `[NotMapped] Category`
    (derived from IsExempt/IsZeroRated — R-Q3) + `LegalRef` col + EF migration
    `Sprint9TaxFilingAndLegalRef`; `EnsureValid()` exempt⊕zero invariant; seed
    `240` default VAT set (ม.81 exempt + ม.80/1 zero + taxable) + idempotent;
    `CompanyService.CreateAsync` `DefaultTaxCodes` copy (mirrors WHT-type
    pattern); `IProportionalInputVatService` (ม.82/6 ratio = taxable/total);
    `ITaxFilingService` — ภ.พ.30 preview/finalize (immutable `tax.tax_filings`
    pulled forward from C8; auto-mode RD stub), input/output VAT registers;
    perms `tax.filing.preview/finalize/read` (seed `241`); single
    `SalesCategorizer` (no dup category logic); UI `/reports/pnd30` + nav +
    i18n. Gates: build 0/0, no EF drift, Domain **60/60** (+7), Api **63/63**
    (+5, 0 skip/regr), tsc 0, next 0, **Playwright 23/23**. Mirror synced.
    (2026-05-17) — tax_code line-badge deferred (no tax_code picker in TI/RC
    form; category fully covered backend + on ภ.พ.30 page — mechanism note).
  - ☑ **Part A** Financial Reports — TB (Σ Dr==Cr invariant), P&L by BU
    (flat + Phase-2 note), sales-summary, WHT-recv aging buckets. Pw 22/22.
  - ☑ **Part B** VAT compliance — TaxCode R-Q3 Category/LegalRef, seed 240,
    ม.82/6 proportional, ภ.พ.30 preview/finalize + immutable tax_filings,
    in/out VAT registers, tax.filing.* perms. Pw 23/23.
  - ☑ **Part C** WHT compliance — `WhtFormType.Pnd54` (8.7-deferred enum
    extension); seed 250 FOR-SVC/FOR-ROYAL + CompanyService copy; ภ.ง.ด.3/53/54
    generators (Direction='P', payee-type/Pnd54 routed); ภ.พ.36 reverse-charge
    + auto-JV (Dr 1170 / Cr 2151, net 0, balanced — integration-verified);
    shared `TaxFilingStore` immutability; `/tax-filings` index + 4 sub-pages +
    i18n + nav. Gates: build 0/0, no EF drift, Domain **60/60**, Api **66/66**
    (+3, 0 skip/regr), tsc 0, next 0 (+5 routes), **Playwright 25/25** (24 @
    VatMode=true incl. pnd3-generation + pnd36-reverse-charge; 1 @ false).
    (2026-05-17)
- ☑ **Sprint 10 DONE & shipped (2026-05-18)** — Quotation chain + Product
  master (3 Parts, gate between each). 25/25 DoD. Final gate **Playwright
  27/27**, Domain 67/67, Api 74/74 (0 skip/regr), build 0/0, no EF drift
  (`AddProductMasterAndFk` + `AddQuotationChain`), mirror synced. See §23.8 +
  Report-Backend15. Spec-first survey confirmed clean-additive: ProductId/QT/
  SO/DO scaffolds pre-exist (Sprint 1); only TaxInvoiceLine carries the product
  scaffold (Receipt=ReceiptApplication, CN/DN=header — FK/snapshot/auto-pickup
  TI-line-scoped; mechanism note).
  - ☑ **Part A DONE & gated** (Product master): `master.products` entity +
    `ProductType` enum + `ProductConfiguration` (screaming-snake CHECK) + EF
    migration `AddProductMasterAndFk` (FK `tax_invoice_lines.product_id →
    products`, Restrict); `EnsureValid()` wht-on-goods invariant;
    `IProductService` CRUD + `/products` endpoints + `master.product.manage|
    read` perms (seed 260); ProductCode snapshot at TI POST (immutability);
    **retro-enables**: wht-base-suggest service/goods split (8.6 R-B1a
    reversed, +ServiceSubtotal/GoodsSubtotal, base defaults to service),
    sales-summary `group_by=product` (Sprint 9 R-Q2 reversed, line-level);
    `/settings/products` UI + nav + i18n. Gates: build 0/0, no EF drift,
    Domain **67/67** (+7), Api **71/71** (+5; Sprint-9 product-reject test
    repurposed by-design — A6 reverses it), tsc 0, next 0, **Playwright
    26/26**. Mirror synced. (2026-05-18) — gate caught: CA1304/1311 ToUpper
    → `EF.Functions.ILike`; record-vendor §14 data-accumulation fragility
    (6th instance) → search-filter robust.
  - ☑ **Part B** Quotation chain — Quotation/SalesOrder/DeliveryOrder entities
    (+6 tables) + `AddQuotationChain`; Q/SO/DO numbering on POST-equivalent
    (Q=Send) with BU sub-prefix (QT/SO/DO prefixes pre-seeded); Q→SO convert,
    SO→DO partial + auto-close, DO→TI Pattern X (combined auto-TI) + Y; BU
    cascade Q→SO→DO→TI; `sales.{quotation,sales_order,delivery_order}.manage`
    perms (seed 270). Api **74/74** (+3), Pw 27/27.
  - ☑ **Part C** chain UI (quotations/sales-orders/delivery-orders list+new+
    detail), sales-summary `product` chip, sidebar Sales section, i18n th/en;
    Q/SO/DO PDFs (`ISalesChainPdfService`, Q WHT note B4, DO combined dual
    label); 2 e2e (products-crud, quotation-chain-flow). Gates: tsc 0, next 0,
    **Playwright 27/27**, mirror. (2026-05-18) — TI/RC line auto-pickup UI
    pre-fill deferred (backend A5 link works; pre-fill is a non-compliance
    convenience on the existing TI form — mechanism note, same class as the
    Sprint-9 tax_code-badge deferral).
- ☑ **Sprint 11 DONE & shipped (2026-05-18)** — File Attachment (polymorphic).
  14/14 DoD. Single phase. `sys.attachments` (parent_type/category enums,
  soft-delete, filtered indexes) + `AddAttachmentSystem`; `IFileStorageService`
  + `LocalDiskFileStorage` (sanitize + path-traversal block); `IAttachmentService`
  (upload/list/download/soft-delete + parent-existence resolve + mime/size +
  parent .read inheritance); endpoints (multipart via BFF proxy unchanged);
  `sys.attachment.upload|read|delete` (seed 280); `AttachmentsSection` reused on
  9 detail pages. Gates: build 0/0, no EF drift, Domain **67/67**, Api **82/82**
  (+8, 0 skip/regr), tsc 0, next 0 (no new routes), **Playwright 28/28**. Mirror
  synced. See §23.9 + Report-Backend16. — JV detail page deferred (no journals
  route in FE; backend supports JOURNAL_ENTRY); list-row count chip deferred
  (needs a batch-count endpoint to avoid N+1 — Phase 2; count shown on every
  detail page). Mechanism notes flagged.
- ☑ **Sprint 12 DONE & shipped (2026-05-18)** — Internal Purchase Order.
  18/18 DoD. Single phase. `purchase.purchase_orders` + lines
  (Draft→Approved→Closed|Cancelled) + `ck_po_sod` DB CHECK (mirrors
  `ck_pv_sod`); `vendor_invoices.purchase_order_id` nullable FK; pure
  `PoSettlement` (auto-close when linked Posted-VI total ≥95% of PO total;
  >105% = HTTP-200 over-receipt chip, not an error); `PO-NNNN` numbering +BU
  sub-prefix allocated on approve; SoD approver≠creator (entity + DB CHECK);
  Outstanding-PO report (aging Current/1-7/8-14/15-30/30+); `AttachmentsSection`
  on PO detail (`PURCHASE_ORDER` parent_type, fwd-compat from Sprint 11); VI
  form optional PO-link dropdown + auto-fill + VI-detail linked-PO badge.
  4 perms (seed 290 — `PO` prefix was NOT pre-seeded, added there). Gates:
  build 0/0, no EF drift (`AddInternalPurchaseOrder`), Domain **79/79**, Api
  **87/87** (0 skip/regr), tsc 0, next 0 (+3 PO routes +1 report route),
  **Playwright 29/29** (28 @ VatMode=true incl. `purchase-order-flow`; 1 @
  false). Mirror synced. See §23.10 + Report-Backend17. **Phase-1 backbone
  complete.**
- ☑ **Sprint 13c DONE & shipped (2026-05-18)** — e-Tax production-readiness +
  Tier 1 mock infra. 15/15 DoD. Single phase, 8 ordered steps. P1 config drift
  removed (`Tax:EtaxEnabled`/`EtaxDeliveryEmailCc`/`ETaxBehaviorOptions.RdCcAddress`
  deleted, grep-clean, single-source `ETax:Email:RdCcAddress`). `etax.submissions`
  append-only audit (entity + `AddETaxSubmissionsAudit` + 300 trigger,
  UPDATE/DELETE rejected). `ETaxRecipientResolver` redirect/whitelist (Tier-2
  safety). `LocalXsdValidator` (Tier-1 graceful skip; ETDA XSDs = ops/Tier-2
  prereq, flagged). `IRdEfilingClient` + `MockRdEfilingClient` + HTTP skeleton +
  DI selector; auto-mode TaxFiling wired. `IETaxSubmissionPipeline`
  (build→sign→validate→send, append-row each outcome) + `ETaxRetryWorker`
  scan (backoff 1m…24h, dead-letter @ 6) hosted in the API root (Infra stays
  hosting-free). Dev tools: `gen-test-cert.sh`, `docker-compose.dev.yml`
  (Compose `include` + MockServer), MockServer init JSON, `.gitignore`
  secrets. `GET /etax/submissions` read endpoint (audit-viewer UI = Phase 2).
  Gates: build 0/0, no EF drift, Domain **79/79**, Api **107/107** (+20,
  0 skip/regr), tsc 0, next 0 (no FE routes), **Playwright 29 pass + 1 honest
  skip / 30** (`etax-pipeline-mock` skips without the Tier-1 MailHog/Docker
  stack — runs green in Tier-1; manual "Tier 1 startup smoke" is its real
  gate). Mirror synced. See §23.11 + Report-Backend18. **Phase-1 backbone +
  production-readiness COMPLETE.**
- ☑ **Sprint 14 DONE & shipped (2026-05-19)** — External API Integration +
  Per-Key BU Binding. 12/12 DoD, 8 phases, per-phase commits
  (`6c6418d`→…→`9aXXXXX` wrap). `X-Api-Key` scheme + resolver (bcrypt, ordered
  fail codes, rate-limited LastUsed); ApiKey CRUD + `/settings/api-keys` UI
  (plaintext-once); `/api/v1/*` additive mount (delegates to existing
  services); `Idempotency-Key` middleware + `sys.idempotency_keys` +
  `AddIdempotencyKeys` + hourly cleanup; v1 error envelope (plan §20.7);
  scope enforcement (`apiperm:` policy — scheme-pinned, root JWT-isolated);
  per-key BU auto-fill/lock across TI/RC/CN-DN/QT + cross-BU receipt reject;
  `ApiKey.DefaultBusinessUnitId` + `AddApiKeyBuBinding`. Gates: build 0/0,
  no EF drift, Domain **83/83**, Api **114/114** (+11), tsc 0, next 0
  (+1 route `/settings/api-keys`), **Playwright 29 pass + 2 honest skips /
  31** (`etax-pipeline-mock` Tier-1-gated; `external-api-microservice`
  post-step §14-gated — both run green on a clean DB/CI; auth +
  idempotency + scope + BU-lock all asserted green). Two real latent bugs
  caught + fixed in P8 (lazy `HttpTenantContext`; `apiperm:` scheme pin).
  Mirror synced. See §23.12 + Report-Backend19. **Phase-1 = production-ready
  foundation (backbone + e-Tax tiers + external API) COMPLETE.**
- ☑ **Sprint 14.5 DONE (2026-05-19)** — §14 fix (the single most-re-applied
  gotcha — non-idempotent test-fixture DB state, 7+ false-positive sprint
  failures, was elevated "actively blocking sprint e2e gates"). New pure
  `Accounting.TestKit` lib + `TestIds` helper (prefix + short-Guid suffix) +
  TS mirror `frontend/e2e/helpers/test-ids.ts`; 7 known §14 sites retrofitted
  to route through the one helper (e2e `record-vendor`/`_helpers.createVendor`
  real low-entropy fix; Sprint55/85/9Vat/86 backend ad-hoc Guid/Random →
  single-sourced); `tools/dev-db-resync.sql` + `dev-tools/dev-db-resync.sh`
  (idempotent, non-destructive `current_value` resync for the Sprint-14 GL
  journal-numbering desync special case). Gates: tsc 0, backend build 0/0,
  Domain **89/89** (+6 `TestIds` meta-tests, 0 regr). **§14 now extinct** —
  no fixture in the suite plants a fixed identifier on the shared dev DB.
  DB/Docker-gated verification (Api Testcontainers re-run, 3× e2e per site,
  Playwright 31/31, one-time resync execution) deferred to the dev env with
  exact commands in `progress.md` cont. 41 — honest, not a fake pass:
  no Docker / port 5432 closed this session. Single per-step git history
  (`56c68f3`→`47ad3eb`→`62cac14`→wrap). See §23.13 + Report-Backend20.
- ☑ **Sprint 13e (started 2026-05-19; SHIPPED 2026-05-20 cont.51 — see the ☑ entry below)** — chapter 3 sales-form fix
  (Answer-Sana-Backend22 + Report-Backend28/29 + Answer-Sana-Backend26):
  - ☑ **P1** (cont. 48 / Report-Backend28) — SO/DO `/new` routing fix
    (created `sales-orders/new/page.tsx` + `delivery-orders/new/page.tsx`
    stubs; was: no static-segment file → Next.js `[id]` caught `/new` →
    `parseInt("new")=NaN` → 404 infinite spinner). Gotcha §27 logged.
  - ☑ **P3** (cont. 49 / Report-Backend29) — Shared
    `frontend/components/forms/TaxInvoicePicker.tsx` (async combobox:
    doc_no/customer search, customer/status/unpaid scoping, preview row);
    wired `/receipts/new` (per-row, customer-scoped, unpaid, auto-fills
    `appliedAmount = TI.totalAmount`) + `AdjustmentNoteForm` CN/DN
    (status=Posted). BE: `GET /tax-invoices` += `search` (DocNo/
    CustomerName ILIKE) + `unpaid` (`AmountPaid < TotalAmount`),
    3 additive files. **FE-verified** (`tsc --noEmit` → 0). **BE
    BUILD-PENDING** — env blocker §29 (Claude session cannot spawn
    `MSBuild`/`csc`). Sana doc deltas applied 2026-05-19 (cont. 50):
    openapi `GET /tax-invoices` += `search`/`unpaid`; runtime-gotchas
    §29 + ROI row.
  - ☑ **P2 / P4 / P5 + E2E** (shipped cont.51) unblocked via **R-Q1a** (Question-Backend14 →
    Ham accepted 2026-05-19; Answer-Sana-Backend26 issued same day).
    Claude Code: FE-now (Quotation form rebuild + ProductPicker +
    LineItemsTable + SO/DO forms + DocumentStatusBadge — all
    `tsc`-verifiable) + BE-code with `// BUILD-PENDING:` markers + hand-
    written migrations `AddQuotationWorkflowFields` +
    `AddSalesOrderDeliveryOrderWorkflowFields` mirroring
    `20260517180740_AddQuotationChain` shape. **Do-not-merge gate:** Ham
    must run `dotnet build` 0/0 + `dotnet ef migrations add` regen
    byte-match + `dotnet test` 0 regr on local Windows host before any
    merge. §25 prevention rules apply to Ham's local regen step
    (`--no-build` forbidden, snapshot diff reviewed before any `remove`).
  - ☐ **Chapter 3 manual** (`docs/manual/chapters/03-การขาย.md` +
    `frontend/manual/walkthroughs/03.01-03.07.ts`) — deferred per
    CLAUDE.md §16 chapter-sequential rule; authored by Sana **only after**
    P2/P4/P5 merge + Chrome MCP chapter-3 validate green. No premature
    authoring.
- ☑ **Sprint 13e SHIPPED (2026-05-20, cont. 51)** — P2 Q form rebuild, P3
  TaxInvoicePicker, P4 SO/DO forms, P5 StatusBadge MAP extend, E2E.
  Toolchain unblocked via `subst U:` short-path. FE tsc 0, BE build 0/0,
  Domain **89/89**. **No EF migration** — Sprint 10 backend already had
  the full Q→SO→DO chain (Report-Backend28's feared breaking migration
  never existed). Answer-Sana-Backend26's BUILD-PENDING / do-not-merge
  gate **MOOT**. See Report-Backend29.
- ☑ **Sprint 13h SHIPPED (2026-05-21, cont. 56)** — Chapter 3 acceptance fix
  (Answer-Sana-Backend27 — all 13 phases across 4 checkpoints; ckpt4 = sprint
  completion, see Report-Backend31). 4 BE migrations applied (DO Delivered
  stage, TI←Q FK, LineItem product_type snapshot, BillingNote); P8 cross-ref
  service + chips on TI/RC/CN/DN detail; P10 logo upload via polymorphic
  attachments (doc-header banner + PDF embed deferred to 13i); P11 XML
  0-byte fix (root cause: `using var` flush-ordering trap in `ETaxXmlBuilder`).
  8 of 8 chapter-3 E2E specs ship `tsc --noEmit` 0 + parameterised demo-
  accountant RBAC matrix. Awaiting Sana RE-VALIDATE deep mode before 13i.
  Phase index:
  - P1 RBAC seed gap (ACCOUNTANT/AR_CLERK 403 on customers/TI; split
    customer.read/manage; new seed 320; group-auth refactor)
  - P2 Picker portal (ProductPicker + TaxInvoicePicker clip/invisible
    bugs; render via portal)
  - P3 i18n sweep + Thai date locale
  - P4 Quotation lifecycle (Edit Draft, Delete Draft, Cancel Finalized,
    PDF download)
  - P5 SO + DO list filters
  - P6 TI-from-Q direct path + new Billing Note CRUD (entity + 4-state
    enum + endpoints + UI + PDF + sidebar entry)
  - P7 Product master SERVICE/GOOD type wiring through every line item;
    kill manual VAT/tax override in TI/RC/CN/DN (enum-locked from
    product tax_code)
  - P8 Receipt cleanup (PostConfirmDialog label, navigation, cross-ref
    panel)
  - P9 DO Delivered stage extension (4-state enum migration, split
    issue/mark-delivered endpoints; backfill existing Posted → Delivered)
  - P10 Company Logo upload + header display
  - P11 XML 0-byte fix (e-Tax Tier 1 pipeline verify + DO→TI signing
    path)
  - P12 `<select>` global half-render CSS fix
  - P13 Product list as DataTable
- ☑ **Sprint 13i 16/16 SHIPPED (2026-05-21, cont. 60)** — Bug fix + UX cleanup,
  first of 4 sub-sprints (`docs/Answer-Sana-Backend28.md`). Split finalised:
  13i bug/UX → 13j Print/PDF → 13k Security/RBAC/Perf/A11y → 13L DevOps.
  - **Bug block B1–B7 — ☑ ALL SHIPPED + verified-live:**
    - ☑ B1 SR2 RBAC grants (seed 330; demo-accountant Receipt+CN/DN read live)
    - ☑ B2 SR4 QueryState 403 → "ไม่มีสิทธิ์เข้าถึง" (`QueryStateRow` on 8 lists)
    - ☑ B3 SR5 CustomerSelector + VendorSelector lookup-on-mount
    - ☑ B4 SR6/SR9 form validation feedback (7 forms; `lib/forms.ts`)
    - ☑ B5 SR7 contextual edit/view link labels
    - ☑ B6 SR8 print = PDF blob (`printPdf`; TI/RC/CN/DN)
    - ☑ B7 confirm() → AlertDialog (BN draft delete)
  - **Carry-overs / enhancement — ☑ shipped:**
    - ☑ C1 Q lifecycle UI (edit page + delete/cancel/reject/PDF/print)
    - ☑ C2 readOnly tax_rate (LineItemsTable + AdjustmentNote) + RC WHT auto-base
    - ☑ C4 toast sweep tail + RC date label + Thai list headers
    - ☑ C6 BN settled auto-derive from receipts (array-based)
    - ☑ R5 cross-ref Q+SO+DO chain chips on TI detail (BE resolver)
    - ☑ L1 legacy `ti.postConfirm.*` i18n removed
  - **Tail (cont. 60) — ☑ ALL SHIPPED + verified-live:**
    - ☑ C7 BN ↔ TI join table `sales.billing_note_tax_invoices` (composite PK +
      RLS + `applied_amount`); dropped `BillingNote.TaxInvoiceIds bigint[]`;
      rewired Create/Update/Get + DocumentCrossRef + Receipt C6 to the join;
      FE multi-TI picker (chips + ×) + detail chips from join.
    - ☑ C5 product_type NOT NULL ×5 line tables (backfill NULL→GOOD idempotent +
      `AlterColumn`; entity non-nullable `= "GOOD"`; EF `.IsRequired()`; BN +
      TI service default GOOD; coalesced cascade sites).
    - ☑ C3 status+BU+customer+date filters on all 8 sales lists (shared
      `<ListFilters>` + `applyListFilters`, URL-persisted; TI server-side
      paginated, others client-side — flagged for 13j if >1000 rows).
  - Verified cont. 60: BE build 0/0, Domain 89/89, FE tsc 0, both migrations
    applied to accounting_dev, snapshot-drift check empty, API live :5080,
    psql confirms join table + RLS + product_type NOT NULL ×5 + dropped column.
- ☑ **Sprint 13j (split into 13j-FE + 13j-PDF)** — Answer-29 + ClaudeDesign-Integration-Brief.
  (Both halves closed — 13j-FE cont.61, 13j-PDF polish cont.93.)
  - ☑ **13j-FE SHIPPED (2026-05-21, cont. 61, Report-Backend34)** — Claude Design FE
    swap on SALES module. Phase A (tokens/teas-orange/fonts/mascot) + B (Sidebar/Topbar/
    StatusBadge withEn/DocActionBar/MascotGreeting/EmptyState/FilterBar) + C (PaperDocument
    suite §C4-locked + bath-text + wired 8 detail + 8 create sticky preview) + D (BE
    `GET /{docType}/{id}/activity` ×8 + ActivityLog + RelatedDocs). Build green: FE tsc 0,
    `next build` 0/0 (native path), dotnet 0/0, BE tests 112 pass, hex-grep components/app 0.
    Purchase + Settings untouched (token cascade only). §0a Gold-Standard honoured.
    - ⚠️ FLAG: `audit.activity_log` has no sales-doctype writes → ActivityLog empty until a
      backend transition-logging sprint (§4.8). See Question-Backend15.
  - ☑ **13j-FE post-ship polish (2026-05-22, cont. 62)** — live fixes/features (Ham-driven):
    Customer master CRUD (+ `CustomerDetailDto`/projection) + sidebar "ขาย" group; print
    original/copy + audit (`AddPrintTracking` migration, `PrintMenu` on 8 detail, `mark-printed`);
    ใบทวิ 50 optional + late entry (`SetWhtCertAsync` + `/receipts/{id}/wht-cert` + `ReceiptWhtCertSection`);
    LineItemsTable VAT dropdown 7%/0% + wider cols; receipt WHT rate readonly; customer master data on
    Q/SO/DO/BN paper; PaperDocument fixes (total row, watermark in-flow bug, VAT float round); middleware
    static-asset 404 fix; company-1 profile seed (420). CLAUDE.md §17 (/graphify) added.
  - ☑ **13j-PDF (COMPLETE cont.93 — see `docs/13j-pdf-plan.md`)** — QuestPDF mirror of
    `PaperDocumentProps` §C4 + `lib/paper.css`, all 8 doctypes, replaces browser-print. cont. 64 (Ham
    picked over 13k, code = source-of-truth): ☑ C# `BahtText` (9/9), ☑ Sarabun font bundled+registered,
    ☑ `PaperDocModel`/`PaperDocConfig`/`PaperDocumentPdf` renderer, ☑ all 8 doctype mappers + endpoints
    (BN endpoint new), ☑ FE PrintMenu "ดาวน์โหลด PDF" → server QuestPDF, ☑ 3 review bugs fixed (Thai
    test-encoding, logo fallback, VAT 700%→VatPercent). BE 0/0 · FE tsc 0 · next build 0/0. **☑ Polish CLOSED (cont.93, 2026-06-12 — Ham delegated the visual gate to Claude):**
    visual gate passed on live renders of Q/SO/DO/BN/TI(+copy)/RC/CN — watermark rotation ✓ (ต้นฉบับ/สำเนา
    diagonal + status marks ยืนยันแล้ว/ส่งของแล้ว; draft = no watermark **by §C4 design**, mirrors FE);
    ~~seller from CompanyProfile~~ done; ~~openapi pdf routes~~ done (cont.93 delta). **🔴 Real bug found
    by the gate + FIXED:** posted-TI seller address was snapshotted from `companies.AddressTh` (empty on
    fresh seeds) → printed TI had NO seller address (**ม.86/4 #2**). `TaxInvoiceService` snapshot now
    prefers the CompanyProfile registered address (`PaperSellerSource.ComposeRegisteredAddress`),
    AddressTh fallback. Verified live: fresh TI 06-2026-TI-0016 prints the full registered address.
    Old posted TIs keep their snapshot (§4.2 immutability — correct). Api 322/0/3 · TI+chain 11/11 ×2.
    ~~⚠️ BN PDF EN small-label "BILLING NOTE"~~ → **FIXED (Ham "ต้องเป็น Invoice สิ" 2026-06-12):**
    `PaperDocConfig` BillingNote EN → "INVOICE", now matches the FE mirror
    (`paper-doc-config.ts:49` was already INVOICE — true 1:1 restored); render-verified.
  - ☑ **13j-tail — DONE (cont. 63–64)** — (1) ☑ §4.8 audit-log writes for all sales transitions
    (cont. 63 — `IActivityRecorder` × 6 sales services; Question-Backend15 RESOLVED, verified live);
    (2) ☑ report "ใบเสร็จขาดใบทวิ 50" ใต้ **Tax filings** (Ham confirmed placement) —
    `GET /reports/wht-receivable-missing-cert?period=yyyymm` + `/tax-filings/missing-wht-cert` page +
    nav link, verified live row; (3) ☑ WHT type select → `WhtTypeSelect` (FloatingListbox) in
    receipts/new; (4) ☑ logo = Company Logo via `lib/company-logo.ts` → `useCompanyProfile().logoUrl`
    (Sidebar + PaperHead; mascot=logo, no new static asset — Ham 2026-05-22; tsc 0 + next build 0/0).
    **Bonus fix:** removed stale `CreateReceiptValidator` rule still forcing `CustomerWhtCertNo`
    required (contradicted cont. 62 deferred-cert; blocked the missing-cert scenario this report chases).
- ☑ **Sprint — Line product/service typing + service-WHT + inline product modal**
  (Ham 2026-05-22, `docs/sprint-line-product-wht-plan.md`) — **SHIPPED cont.81 (2026-05-30,
  commits `ce72a13`+`7b8d0fe`; stale marker cleared 2026-06-12):** `ProductPicker` embedded in the
  shared `LineItemsTable` (every sales line form) + `taxRateForProductType` master-driven typing ·
  `ProductQuickCreateModal` inline create from the line table · DefaultWhtType flow · price
  auto-fills on pick but stays per-line editable (master does NOT lock price, per spec) ·
  purchase/sale split + BU scope on the picker.
- ◐ **Sprint 13k (in progress)** — Security + RBAC full Cartesian + Performance +
  Accessibility audit (Answer-30; after 13j).
  - ☑ **Plan 1 — Role/Permission Admin UI (per-company)** SHIPPED 2026-06-14 (branch
    `feat/rbac-per-company-admin-ui`, cont.95): per-company roles schema+reconcile (510) +
    perm-seed-gap fix (520) + `RbacAdminService`/endpoints (23 tests) + FE `/settings/{roles,users}`.
    Api 354/0/3, tsc 0, accounting_dev converted + visual gate. Remainder: openapi 9 paths +
    formal Playwright spec (live browser e2e done); not yet committed.
  - ☐ **Plan 2 — RBAC full Cartesian audit** (`docs/superpowers/plans/2026-06-13-rbac-cartesian-audit.md`).
- ☐ **Sprint 13L (queued)** — DevOps: migration rollback + build pipeline +
  test skip audit (Answer-31; after 13k).
- ☐ **Chapter 3 manual** — re-deferred per CLAUDE.md §16, authored ONLY after
  13i + 13j + 13k + 13L all ship + Sana RE-VALIDATE deep mode green on each.
- ☐ **Tech debt — 3-way match (PR→PO→GR):** explicitly cut from Sprint 5.5
  (Answer-Sana-Question-Backend5 §B1.3). SMEs go vendor-TI → VI → PV directly.
  Phase-2 expansion.
- ☐ **Tech debt — `bank_account` master + BankAccountSelector:** Q3.1 SKIP confirmed;
  PV uses plain bank/cheque inputs + raw `bank_account_id`. Future master-data slice.
- ☑ WHT PND3/PND53 monthly return generation — shipped Sprint 9 Part C (ภ.ง.ด.3/53/54 generators,
  see §23.7; stale marker cleared 2026-06-12)
- ☐ Fixed Assets register + depreciation
- ⏸ Inventory tracking — explicitly out of scope (CLAUDE.md §8) until requested

## Environment notes (carry forward)

- Build/test from **`U:\`** (`subst U: <real_path>`). Original session path is ~230 chars
  and breaks `csc.exe` process spawn ("The parameter is incorrect"); `U:\` short-path
  is the canonical workspace.
- **No Y:\ mirror** (Ham directive 2026-05-22). The old `code/` → `Y:\AccountApp\backend`
  one-way robocopy mirror is retired — `U:\` is the single canonical tree. Sprint records
  that say "mirror synced" reflect the prior workflow; do NOT re-instate it.
- MSBuild multi-node spawn fails in sandbox → always pass `-m:1`.
- No Docker in env. Integration via `TEAS_TEST_PG` env var (any Postgres).

## Ownership Rules (Answer-Backend1 §4 — binding, 2026-05-16; mirror clause retired 2026-05-22)

- `U:\` is canonical (the `code/` of the original spec). Do NOT relocate.
- **Claude Code owns** (edit freely): `backend/`, `frontend/`, `db/`, `infra/`,
  `design/`, `tests/`.
- **Sana owns** (Claude reads only; ping via a `progress.md` line before any edit):
  `docs/`, `CLAUDE.md`, `Report-Backend*.md`, `Answer-Backend*.md`, other root-level
  `*.md`.
  - Exception: `progress.md` + `plan.md` are Claude's primary append-only log — keep
    updating those directly (Answer-Backend1 §6).
- If a doc/spec change is needed (e.g. the C14N errata), do NOT edit `docs/*`; write the
  ask in the current `Report-Backend{N}.md` / a `progress.md` line and Sana applies it.
- Reports cadence: one `Report-Backend{N}.md` per sprint. Sprint 1 wrap = `Report-Backend2.md`.
- Escalate spec/CLAUDE.md contradictions (don't silently work around) — the C14N
  escalation path worked and is the expected behavior.

## 23. Known Issues

> Doc note: Answer-Sana-Backend8 referenced "plan.md §23.1"; this section did not
> exist yet (the gap was logged as a Phase-2/3 follow-up bullet). Section added
> here so the reference resolves. Minor — flagged in Report-Backend9.

### 23.1 — KI-01: Purchase RBAC seed gap

~~`110_seed_roles_and_permissions.sql` never inserted
`purchase.payment_voucher.{create,post,read}` permission rows nor granted them to
non-super roles (only `140` added `vendor_invoice.*` + `payment_voucher.approve`).
Effect: non-super users got 403 on PV create/post/read.~~

**✅ resolved Sprint 7-half (2026-05-16).** `180_seed_pv_purchase_perms.sql` —
additive + idempotent: 3 perms + grants to
SUPER_ADMIN/COMPANY_ADMIN/CHIEF_ACCOUNTANT/ACCOUNTANT/AP_CLERK, plus
`ap_clerk`/`sales_staff` DEV/SMOKE users (`pgcrypto crypt()` hash — see
Report-Backend9 gotcha). `110`/`140` untouched, no C# change. Verified: e2e
`payment-voucher-non-super-rbac` 2/2 (ap_clerk full PV lifecycle 200s;
sales_staff 403); `SELECT COUNT(*) … LIKE 'purchase.payment_voucher.%'` = 4
(140 approve + 180 create/post/read); 180 tracked in `sys.applied_sql_scripts`
(DbInitializer re-run = no-op) + `ON CONFLICT DO NOTHING`.

### 23.2 — (reserved)

> Unused. Answer-Sana-Backend9 referenced "plan.md §23.3" for the Sprint-8
> completion strike; numbering kept aligned with that reference (§23.2 left
> reserved). Minor doc note — flagged in Report-Backend10.

### 23.3 — Sprint 8: Business Units (first wired GL dimension)

~~Pending: revenue-side Business Unit tag + first wired GL dimension
(TI/Receipt/CN/DN + journal_line), company opt-in enforcement, cross-BU receipt
handling, numbering sub-prefix, reports filter, settings UI.~~

**✅ Shipped Sprint 8 (2026-05-17).** Additive + idempotent. Delivered across 4
gated phases (P1 domain+data+migration, P2 service+endpoints+GL+reports, P3 UI,
P4 tests+gates):

- **Schema:** `master.business_units` (RLS ENABLE+FORCE, company-isolation) +
  `companies.requires_business_unit` (default false) + nullable
  `business_unit_id` FK on `tax_invoices`/`receipts`/`tax_adjustment_notes`/
  `journal_lines` (Restrict, filtered indexes). EF migration
  `20260517021031_AddBusinessUnits` (no model drift). `200_add_business_units.sql`
  = RLS + TI immutability trigger `+= business_unit_id` (schema owned by EF,
  mirrors the 060 split). `210_seed_business_unit_perm.sql` =
  `master.business_unit.manage` perm + grants (no `$`-literal — gotcha §17).
  **NO backfill** (legacy rows stay BU-NULL by design).
- **Behavior:** company-flag enforcement at the **service** layer (accepted flag
  c — avoids DbContext←ITenantContext DI cycle, always-fresh); numbering
  `MM-YYYY-PREFIX[-BU]-NNNN` via the existing PV sub-prefix infra; GlPostingService
  snapshots the document BU onto **every** journal_line; Receipt cross-BU =
  header BU NULL + per-application AR-clearing line tagged each TI's BU + cash
  line NULL + `CrossesBusinessUnits` flag (warn, **never blocks**).
- **API/UI:** `IBusinessUnitService` CRUD + `/business-units` (+ soft-deactivate)
  + `/business-units/company-setting` GET(authn)/PUT(manage); `business_unit_id`
  + `include_unspecified` filters on `/tax-invoices` & `/receipts` &
  `/tax-adjustment-notes`; `/settings/business-units` (CRUD + company toggle),
  BU dropdowns on TI/RC/CN/DN forms (required-asterisk when opted in), list
  filter chips, detail BU chips, cross-BU receipt-detail warning chip, i18n
  th/en `businessUnit.*`.
- **4 mid-sprint design flags — all ACCEPTED by Sana** (mechanism notes in
  Report-Backend10): (a) `/reports/sales-summary` filter deferred to Sprint 9
  (endpoint does not exist; scope = filter only); (b) number-gaps BU-filter
  deferred (sub-prefix already separates counters; a BU filter on the gap view
  is not meaningful); (c) `requires_business_unit` enforced at service layer
  instead of `ITenantContext`+validator (better design — no DI cycle, no stale
  JWT); (d) company toggle via `/business-units/company-setting` instead of
  reworking CompanyService.
- **Scope cuts honored (not improvised):** no AP-side BU (VI/PV), no Q/SO/DO BU,
  no full P&L-by-BU report (Sprint 9), no cost_center/project, no retroactive
  backfill, no multi-BU per doc, no BU hierarchy, no BU-level RBAC.

**Gates (all green):** backend build 0 err/0 warn; `Accounting.Domain.Tests`
**34/34** (32 baseline + 2 new); `Accounting.Api.Tests` **37/37** (27 baseline +
10 new, **0 regression, 0 skip** vs native PG :5433); frontend `tsc` 0; `next
build` 0; **Playwright 15/15** (13 prior + 2 new: `business-units-setup`,
`receipt-cross-bu-warning`) via system Edge; `dotnet ef
has-pending-model-changes` = none; DbInitializer idempotent (PostgresFixture
re-runs all SqlScripts incl. 200/210 each session with no tracking → 37/37
proves idempotency); GL snapshot integrity asserted
(`Posted_ti_snapshots_bu_onto_every_journal_line`); posted-TI BU immutability
trigger asserted. One latent P3 regression caught & fixed by the e2e gate: the
Sprint-8 BU `<select>` (ARIA role=combobox) collided with the customer
`<input role=combobox>` in the shared e2e helper → repointed customer locators
to the unique search placeholder (gotcha logged in Report-Backend10).

### 23.4 — Sprint 8.5: VAT-mode polish (non-VAT-registered companies)

> Doc note: Answer-Sana-Backend10 instructed striking "plan.md §23.3" for the
> Sprint-8.5 row; §23.3 is the Sprint-8 section, so the Sprint-8.5 record is
> added here as §23.4 (numbering kept growing, mirrors the §23.1/§23.3 pattern).
> Minor — flagged in Report-Backend11.

~~Pending: 4 gaps for `Tax:VatMode=false` companies — (1) PDF hardcodes
"ใบกำกับภาษี" (ผิด ม.86), (2) CN/DN hardcode ม.86/10·ม.86/9 (must be ม.82/9),
(3) e-Tax CTA shown, (4) no ม.85/1 revenue-threshold warning.~~

**✅ Shipped Sprint 8.5 (2026-05-17).** Small surgical sprint, additive:

- **Config:** `TaxConfig` (API) + `VatModeOptions` (Infra, bound from the same
  `Tax` section — Infra can't reference the API assembly; mirrors
  `ETaxBehaviorOptions`) gained `NonVatDocLabelTh/En`. appsettings + Development
  updated.
- **PDF branching:** pure `DocumentLabels` resolver in `Accounting.Domain`
  (unit-tested — the authoritative compliance assertion). TI PDF: header term
  swaps "ใบกำกับภาษี/TAX INVOICE" → configured neutral label, VAT subtotal/VAT
  rows hidden under non-VAT (single "ยอดรวม"). CN/DN PDF: legal-ref
  ม.86/10 (CN) · ม.86/9 (DN) → ม.82/9 under non-VAT. Receipt PDF unchanged
  (per spec §2.1). Note: PDF builders are inline `BuildPdfAsync` in
  `*Service.Read.cs` (no `*PdfService` classes; CN+DN share one NoteType-branched
  method) — mechanism-mapped, see Report-Backend11.
- **e-Tax CTA gate:** `useSystemInfo()` exposes `vatMode`; TI detail hides
  XML-download + resend when `vatMode=false` (RC/CN/DN detail have no e-Tax CTA —
  audited, nothing to gate).
- **ม.85/1 threshold:** `IVatThresholdService` (rolling-12-mo posted-TI
  `TotalAmountThb`; `NotApplicable` when VatMode; ≥1.5M Approaching, ≥1.8M
  Exceeded) + `GET /system/vat-threshold-status` (authn) + dashboard banner +
  i18n th/en.
- **Scope cuts honored:** no VatMode UI toggle, no retroactive PDF regen, no VAT
  registration wizard, no re-issue of old TIs, no per-company e-Tax override.

**Gates (all green):** backend 0/0; Domain **41/41** (34 + 7 `DocumentLabels`);
Api **41/41** (37 + 4 `VatThreshold`, 0 regression, 0 skip); tsc 0; next build 0;
**Playwright 16/16** — 15 vs the normal VatMode=true stack + 1
(`non-vat-mode-pdf`) vs a dedicated VatMode=false API instance (VatMode is
process-global env; the new spec asserts the e-Tax-CTA-hidden behavior, the
cleanest deterministic VatMode=false signal). PDF-label correctness is proven
deterministically by `DocumentLabelsTests` + the wiring by build/e2e.
**DoD #9 (manual ×8 visual PDF inspection):** not executable by an automated
agent — substituted by the deterministic `DocumentLabels` unit suite + the
e2e wiring check; recommend Ham/Sana do the visual spot-check. Flagged in
Report-Backend11 (not silently skipped). **DoD #7 `nonVat.docLabel.*` i18n:**
the doc label lives in backend `Tax` config (server-rendered into the PDF), it
has no frontend string surface — dead i18n keys were intentionally NOT added;
only the rendered `dashboard.vatThreshold.*` keys were added. Flagged.

### 23.5 — Sprint 8.6: AR-side WHT (customer withholds from us)

> Doc note: Answer-Sana-Backend11 said strike "plan.md §23.3"; that's the
> Sprint-8 section. Sprint-8.6 recorded here as §23.5 (numbering grows; same
> §23.1/§23.3/§23.4 pattern). Flagged in Report-Backend12.

~~Pending: B2B customers withhold WHT on our service receipts. Without it GL
was wrong by the WHT amount on every B2B service receipt + no ภ.ง.ด.50 credit.~~

**✅ Shipped Sprint 8.6 (2026-05-17).** Spec-first gate first (Question-Backend12:
no Product master → R-B1a manual WHT base; +4 R-defaults — all accepted).
Phased P1–P6, gated each:

- **Schema/migration `AddARWhtSupport`** (+ `ArWhtCertReceivableDocNoFilter`):
  Receipt WHT cols + `cash_received` + CHECKs; `WhtCertificate.Direction`
  ('P'/'R') + `ReceiptId` + `PaymentVoucherId`→nullable; `WhtType.EffectiveFrom/
  To` + unique-index swap `(company,code,effective_from)`; `Customer.
  DefaultWhtTypeId`; `GlAccountsOptions.WhtReceivableAccount=1180`. SQL `220`
  (13 domestic WHT types, no SALARY/foreign — R-B3) + `230` (1180 CoA +
  `tax.wht_type.manage`). Fixed seed `120` 42P10 (ON CONFLICT mismatch after
  the unique-index swap). No model drift.
- **Receipt WHT**: capture + validators (amount≥0; >0→type+certno; type
  active; wht≤amount) + GL `Dr Bank cash_received + Dr 1180 WHT-Recv =
  Cr AR Σapplied` (cross-BU: AR per-app BU, WHT-Recv/cash BU NULL) +
  `WhtCertificate` Direction='R' on post (customer cert no, no PDF) +
  `wht-base-suggest` (R-B1a degraded — full ex-VAT subtotal, manual trim).
- **`IWhtTypeService`**: CRUD + `ResolveAtDateAsync` + `ChangeRateAsync`
  (close in-force row + open new — row pair is the audit trail; explicit
  `activity_log` deferred → Phase 2, flagged) + `tax.wht_type.manage` perm.
  Replaced dead `Sys.WhtTypeManage` scaffold with `Tax.WhtTypeManage`.
  `CompanyService.CreateAsync` narrow R-B5 copy (13 WhtTypes + 1180).
- **Reports**: `/reports/wht-receivable-register|aging` (basic; no 1180
  settlement model this sprint → Phase 2/Sprint 9, flagged).
- **UI**: `/settings/wht-types` (CRUD + change-rate modal), Receipt form WHT
  collapsible (type select + auto-suggest + manual override + cash-received),
  receipt detail WHT section, receipts list WHT column, Receipt PDF WHT
  section (reuses 8.5 `DocumentLabels`), `/reports/wht-receivable`, sidebar,
  i18n th/en (`rc.wht.*` + `whtType.*` + `whtReceivable.*` — namespace `rc`
  not `receipt` for codebase consistency, flagged).
- **Scope cuts honored:** no Product master / service-goods split (→ Sprint 10),
  no foreign 15%, no ภ.ง.ด.50 UI, no 50ทวิ scan match, no bulk WHT, no AR-side
  cert numbering, no payroll/SALARY.

**Gates (all green):** backend build 0/0; Domain **45/45** (41+4); Api
**48/48** (41+7 `Sprint86ArWhtTests`, 0 regression, 0 skip vs PG :5433); tsc 0;
next build 0 (+`/settings/wht-types`, +`/reports/wht-receivable`); **Playwright
18/18** (16 prior + `receipt-customer-withholds` + `wht-type-management`; 17 @
VatMode=true + 1 @ VatMode=false two-pass); no EF drift; DbInitializer +
220/230/migrations idempotent; GL balance asserted; WhtType change-rate
snapshot asserted. **Bugs caught & fixed by the gate (honest, not masked):**
(1) WhtCertificate `(company,doc_no)` unique was wrong for Direction='R'
(customer cert no can repeat) → filtered to `direction='P'` + migration;
(2) Receipt form lacked a WHT type selector (P5 gap) → added;
(3) seed 120 42P10 after index swap → fixed;
(4) pre-existing persistent-`teas_test` / toast-race flakiness re-applied
gotcha §14/§16 (S8.5 threshold, S55 period-close, PV-WHT + receipt-confirm
e2e) — fixed deterministically.

### 23.6 — Sprint 8.7: Online subscriptions + Foreign vendor support

> Doc note: Answer-Sana-Backend12 said strike "plan.md §23.3"; that's the
> Sprint-8 section. Sprint-8.7 recorded here as §23.6 (numbering grows; same
> §23.1/§23.3/§23.4/§23.5 pattern). Minor — flagged in Report-Backend13.

~~Pending: 3 scenarios standard "withhold WHT on payment" doesn't fit —
(A) domestic auto-charge (no window → gross-up), (B) foreign no Thai VAT-D
(self-withhold 15% + ภ.พ.36), (C) foreign with VAT-D (normal + hint). Without
it GL was wrong by the WHT amount on every auto-charge/foreign service PV.~~

**✅ Shipped Sprint 8.7 (2026-05-17).** Data side only (ภ.พ.36/ภ.ง.ด.54
generators = Sprint 9). Phased P1–P4, gated each:

- **Schema/migration `AddForeignVendorSupport`** (5 cols + 2 CHECKs, no SQL
  script — defaults backfill, no model drift): Vendor `IsForeign` /
  `HasThaiVatDReg` / `CountryCode`; PV `SelfWithholdMode` /
  `RequiresPnd36ReverseCharge`; VI `HasInputVat` (default true) /
  `RequiresPnd36ReverseCharge`. CHECKs `ck_vendors_vatd_foreign`
  (has_thai_vat_d_reg→is_foreign) + `ck_vendors_foreign_vatreg`
  (is_foreign→vat_registered). **Mechanism note:** spec's `is_vat_registered`
  = the *existing* `Vendor.VatRegistered` column (reused, no duplicate boolean —
  Report-Backend13); only the 3 genuinely-new cols were added.
- **Service/GL:** Vendor DTOs/validators (+CountryCodes allowlist;
  Create+Update foreign rules mirror CHECKs; foreign ⇒ VatRegistered locked
  true). PV: `selfWithhold = req ?? (foreign && !vatD)`; auto
  `requiresPnd36`; `TotalPaid = selfWithhold ? sub+vat : sub+vat-wht`;
  validator blocks self-withhold + VendorInvoiceId (Phase 2). GL
  PostPaymentVoucher: standalone self-withhold **gross-up** (extra Dr Expense
  = wht; Cr Bank = full; Cr WHT-Payable = wht — balanced); VI-linked
  unchanged. VI: `HasInputVat = req ?? !(!VatRegistered || (foreign&&!vatD))`;
  auto `requiresPnd36`; GL `recoverable = HasInputVat && IsRecoverableVat` →
  receipt-only lumps VAT into expense (ม.82/5), no 1170, Dr Exp gross = Cr AP.
- **UI:** vendor new foreign section (toggle + country + VAT-D + info/warn
  chips + is_foreign→VatRegistered lock) + vendor detail row; PV new
  self-withhold toggle (auto/lock for foreign, manual for domestic) + chips;
  PV detail Self-withhold + ภ.พ.36 badges; VI new auto-detect chips;
  i18n th/en (`ven.foreign.*`/`pv.selfWithhold.*`/`vi.*` — codebase
  namespaces, not spec literals; mechanism note). No new routes.
- **Scope cuts honored:** no ภ.พ.36/ภ.ง.ด.54 generator (Sprint 9), no
  self-withhold for VI-linked PV (Phase 2), no DTA per-country rates, no
  rd.go.th VAT-D auto-import, no currency-conversion change, no vendor-managed
  certs. **Premise note:** spec §8 said "reuses WhtType FOR-SVC 15% seeded in
  8.6" — 8.6 R-B3 did *not* seed FOR-SVC (foreign/SALARY cut); PV-line
  `whtRate` carries 15% directly so no FOR-SVC row is required (flagged).

**Gates (all green):** backend build 0/0; Domain **53/53** (45+8); Api
**53/53** (48+5 `Sprint87ForeignVendorTests`, 0 regression, 0 skip vs PG
:5433); tsc 0; next build 0; **Playwright 20/20** (18 prior +
`foreign-vendor-aws` + `domestic-online-subscription`; 19 @ VatMode=true + 1 @
VatMode=false two-pass); no EF drift; GL balance asserted (self-withhold
gross-up + receipt-only VI); CHECK enforced; pnd36 flag integrity asserted.
Bugs caught by the gate: PV "missing WhtType" when whtRate>0 + no
category-default (test seed needed an explicit WhtTypeId); fragile e2e
label/xpath locators → switched to `select[aria-label]` / label-scoped
checkbox (gotcha §15/§16 family). See §23.6.

### 23.7 — Sprint 9: Reports + Tax Filings ✅ shipped Sprint 9 (2026-05-17)

> Numbering grows additively (same convention as §23.6). Largest Phase-1
> sprint; 3 Parts, gate between each, never bundled (per Sana §0 phasing).
> 25/25 DoD. Spec-first gate first (Question-Backend13 — 3 premise gaps, all
> R-defaults accepted).

**Shipped (Part A / B / C):**
- **A** Financial Reports: `GET /reports/trial-balance` (as-of, normal_balance,
  **Σ Dr == Σ Cr** invariant — headline assertion), `/reports/profit-loss`
  (R-Q1a flat Revenue−Expense=NetProfit by BU + payload `note` disclosing the
  GP/COGS Phase-2 deferral — "don't silently omit"), `/reports/sales-summary`
  (R-Q2 customer|business_unit; product→400 till Sprint 10), WHT-Receivable
  aging buckets + CertReceived/Reconciled. 3 UI routes.
- **B** VAT compliance: R-Q3 — `TaxCode.Category` `[NotMapped]` derived from
  IsExempt/IsZeroRated (single source, no category column) + only `LegalRef`
  added; `EnsureValid()` exempt⊕zero invariant; seed 240 + CompanyService
  default-copy; ม.82/6 `IProportionalInputVatService`; ภ.พ.30 preview/finalize
  → immutable `tax.tax_filings`; in/out VAT registers; `tax.filing.*` perms
  (seed 241). UI `/reports/pnd30`.
- **C** WHT compliance: `WhtFormType.Pnd54` enum extension (deferred from 8.7);
  seed 250 FOR-SVC/FOR-ROYAL + CompanyService copy; ภ.ง.ด.3/53/54 generators
  (Direction='P', routed by payee type / Pnd54); ภ.พ.36 reverse-charge +
  finalize auto-JV **Dr 1170 / Cr 2151, net 0, balanced** (integration-
  verified); shared `TaxFilingStore` (single-source immutability + RD
  auto-stub); `/tax-filings` index + 4 sub-pages.

**Final gate:** build 0/0, no EF drift (migration `Sprint9TaxFilingAndLegalRef`
= legal_ref + tax.tax_filings), Domain **60/60**, Api **66/66** (0 skip/regr),
tsc 0, next 0, **Playwright 25/25** (two-pass: 24 @ VatMode=true incl. the 5
new specs; 1 @ false), mirror synced.

**Mechanism notes (→ Report-Backend14 §3):** spec SQL `master.tax_codes(name_en,
rate)` illustrative → real `tax.tax_codes` (no name_en; rate in tax_rates) —
"actual schema authoritative" (accepted); pre-existing Sprint-6 `Pnd30Summary`/
`IVatReportService` flat scaffold left intact, richer `ITaxFilingService` built
alongside (GlReportDtos pattern, 5th instance of single-source-reuse
discipline); `tax.tax_filings` (C8) pulled forward to Part B (B5 finalize hard
dependency) — Part C reused table + perms; per-line direct/shared input-VAT
classification = Phase 2 (§508, shared apportionment = 0); ม.82/6 standalone
endpoint not exposed (ratio surfaces via ภ.พ.30); ภ.ง.ด.54 discriminator =
`FormType==Pnd54`; tax_code line-badge deferred (TI/RC form has a rate field,
not a code picker — no picker to badge; category fully covered backend + on
ภ.พ.30 page). **Gate-caught:** `ck_vendors_foreign_vatreg` (foreign vendor ⇒
vat_registered) — test fixed; **finalize tests must use a unique period** —
PostgresFixture persists rows across runs (not reset), so fixed-period finalize
collides on re-run → switched ภ.พ.30/ภ.พ.36/ภ.ง.ด. immutability tests to a
random far-future period (idempotency discipline, gotcha family).

### 23.8 — Sprint 10: Quotation chain + Product master ✅ shipped Sprint 10 (2026-05-18)

> Last foundational data model (Product) + the sales document chain. 3 Parts,
> gate between each, never bundled. 25/25 DoD. Spec-first survey first
> (Sana's §0 audit cross-checked: clean-additive; the "verify during impl"
> hedges resolved to TI-line-scoped because Receipt/CN/DN have no product
> lines).

**Shipped (Part A / B / C):**
- **A** Product master: `master.products` (ProductType GOOD/SERVICE/EXEMPT_*,
  CHECK, FK→tax_codes/wht_types) + `AddProductMasterAndFk` (FK on the Sprint-1
  `tax_invoice_lines.product_id` scaffold — **no new column**); `EnsureValid()`
  wht-on-goods invariant; CRUD + perms (seed 260); ProductCode POST snapshot.
  **Retro-enables**: wht-base-suggest service/goods split (8.6 R-B1a reversed,
  base→service); sales-summary `group_by=product` (Sprint 9 R-Q2 reversed,
  line-level). `/settings/products` UI.
- **B** Q→SO→DO chain: 3 entities + 6 tables + `AddQuotationChain`; numbering
  on POST-equivalent (Q=Send) + BU sub-prefix (QT/SO/DO prefixes pre-seeded);
  Q→SO convert (Accepted-gated), SO→DO partial + SO auto-close when fully
  delivered, DO→TI **Pattern X** (combined → auto-create+post linked TI) +
  **Pattern Y** (manual); BU cascade Q→SO→DO→TI; chain perms (seed 270).
- **C** chain UI (list/new/detail × Q/SO/DO), sales-summary product chip,
  sidebar Sales section, i18n; Q/SO/DO PDFs (`ISalesChainPdfService` — Q WHT
  note B4 computed on the fly, DO combined dual ใบส่งของ-ใบกำกับภาษี label);
  2 e2e (products-crud, quotation-chain-flow).

**Final gate:** build 0/0, no EF drift, Domain **67/67** (+7
`ProductValidationTests`), Api **74/74** (+5 Product +3 Chain; Sprint-9
product-reject test repurposed by-design — A6 reverses it; 0 skip/regr), tsc 0,
next 0 (16 new routes), **Playwright 27/27** (two-pass: 26 @ VatMode=true incl.
products-crud + quotation-chain-flow; 1 @ false), mirror synced.

**Mechanism notes (→ Report-Backend15 §3):** only `TaxInvoiceLine` carries the
ProductId scaffold — Receipt (`ReceiptApplication`, TI allocation) and CN/DN
(header-level) have no product lines, so A2 FK / A3 snapshot / A5 auto-pickup
are TI-line-scoped (spec's "verify during impl / if structure mirrors" hedge →
doesn't mirror; no new columns improvised). QT/SO/DO doc prefixes pre-seeded
(Sprint-1 forward scaffold, like ProductId) → numbers `MM-YYYY-{QT|SO|DO}-NNNN`
(registered code authoritative). Pre-existing scaffold catch is the emergent
"pre-audit existing scaffold/fields before spec" discipline (continued from
Sprint 9). Case-insensitive product-code uniqueness via `EF.Functions.ILike`
(EF-translatable; CA1304/1311 forbids `ToUpper` in queries). PDF templates
spec'd in BOTH B5#9 and C3 → delivered once in Part C (C3 canonical). TI/RC
line product auto-pickup UI pre-fill deferred — backend A5 link works; pre-fill
is a non-compliance convenience on the existing TI form (flagged, same class as
Sprint-9 tax_code-badge deferral). **Gate-caught:** the Sprint-9
`Sales_summary_by_product_is_rejected_until_sprint10` test was time-boxed by
its own name — A6 *is* its reversal → repurposed to the still-valid
unknown-group_by guard (not a masked regression; covered by
`Sprint10ProductTests`). `record-vendor` §14 data-accumulation fragility (6th
instance, long-lived teas_app no teardown) → made search-filter robust. e2e
stack: `next start` must run as a tracked background task, NOT PowerShell
`Start-Job` (job dies with the tool call → ERR_CONNECTION_REFUSED).

### 23.9 — Sprint 11: File Attachment (polymorphic) ✅ shipped Sprint 11 (2026-05-18)

> Last Phase-1 infrastructure piece. Single phase, 14/14 DoD. Spec-first survey
> cross-checked Sana's §0 audit: clean greenfield, no `attachment_url` strays,
> BFF proxy passes multipart + binary unchanged.

**Shipped:** `sys.attachments` polymorphic table (`parent_type`/`category`
screaming-snake enums via `AttachmentCodes` single-source map, soft-delete,
`deleted_at IS NULL` filtered indexes) + `AddAttachmentSystem`;
`IFileStorageService` + `LocalDiskFileStorage` (filename sanitize + re-rooted
path-traversal block); `IAttachmentService` (upload/list/download stream/
soft-delete; per-parent-type existence resolve; mime + 25MB size validation;
OTHER-needs-description; parent `.read` permission inheritance); endpoints
(multipart POST / list / download / DELETE / categories) through the existing
BFF proxy unchanged; `sys.attachment.upload|read|delete` perms (seed 280);
reusable `AttachmentsSection` on 9 detail pages (TI/RC/VI/PV/Q/SO/DO + CN/DN via
the shared `AdjustmentNoteDetailView`).

**Final gate:** build 0/0, no EF drift, Domain **67/67**, Api **82/82** (+4
`LocalDiskFileStorageTests` +4 `Sprint11AttachmentTests`, 0 skip/regr), tsc 0,
next 0 (no new routes — section embedded), **Playwright 28/28** (two-pass: 27 @
VatMode=true incl. `attachment-upload-flow`; 1 @ false), local-disk round-trip +
traversal-block + cross-tenant asserted. Mirror synced.

**Mechanism notes (→ Report-Backend16 §3):** EF `HasConversion` lambdas must be
expression-tree-safe — no `out var`/decl-patterns (CS8198, build-tier catch) →
added pure `AttachmentCodes.ParentFrom/CategoryFrom`. Perm-code strings are
literals in `AttachmentService` (Api `Permissions` not referenceable from Infra
— same constraint as TaxConfig/VatModeOptions split). `LocalDiskFileStorage`
storage tests moved to `Api.Tests` (Domain.Tests refs Domain only; can't see
Infrastructure). **JV detail page deferred** — no `journals` route exists in the
FE; backend fully supports `JOURNAL_ENTRY` parent_type (UI-surface gap, not a
backend gap; spec DoD#7 listed 10, 9 pages exist). **List-row 📎N count chip
(DoD#8) deferred** — a per-row count is an N+1 without a batch-count endpoint;
deferred to Phase 2; the count is shown on every detail page (honest §8 scope
flag, not silent drop). Receipt/CN-DN have no dedicated `.read` perm → rely on
`sys.attachment.read` + tenant isolation (documented). **Gate-caught:** e2e
`a[href^="/vendor-invoices/"]` matched the `/new` link → scoped to `table a…`.

### 23.10 — Sprint 12: Internal Purchase Order ✅ shipped Sprint 12 (2026-05-18)

> The last Phase-1 backbone sprint. Single phase, 18/18 DoD. Spec-first survey
> (Answer-Sana-Backend17 §0) confirmed clean greenfield: no PO scaffold, no
> `vendor_invoices.purchase_order_id`, `PO` prefix NOT in seed 100 (unlike
> QT/SO/DO), `ck_pv_sod` expr mirrored exactly for `ck_po_sod`, `APPROVER`
> role present.

**Shipped:** `purchase.purchase_orders` + `purchase_order_lines`
(Draft→Approved→Closed|Cancelled state machine on the entity:
`MarkApproved`/`MarkClosed`/`MarkCancelled`, SoD `CreatedBy==approver →
po.sod_violation`) + `ck_po_sod` DB CHECK (`approved_by IS NULL OR approved_by
<> created_by`, byte-mirror of `ck_pv_sod`); nullable
`vendor_invoices.purchase_order_id` FK (Restrict); pure Domain
`PoSettlement.Evaluate` (CloseThreshold 0.95, OverReceiptTolerance 1.05,
poTotal≤0 → no-op) unit-tested at the 94/95/105/>105% boundaries;
`IPurchaseOrderService` (CreateDraft/Update/Approve/MarkSent/Close/Cancel/List/
GetDetail/BuildPdf QuestPDF/Outstanding); `PO-NNNN` via `INumberSequenceService`
+BU sub-prefix allocated **on approve only**; VI `PostAsync` auto-closes the
linked PO when cumulative Posted-VI total ≥95% of PO total and returns a
`PoOverReceiptWarning` chip (HTTP 200) when >105% — not an error;
Outstanding-PO report with aging buckets; `AttachmentsSection` on the PO detail
page (`PURCHASE_ORDER` parent_type — forward-compat slot added in Sprint 11);
VI new-page optional "Link to PO" dropdown (Approved POs of the chosen vendor)
+ line auto-fill, VI-detail linked-PO badge. 4 perms `purchase.purchase_order.
{create,approve,read,cancel}` (seed 290 — also adds the `PO` document prefix,
which was not pre-seeded; `PURCHASING_STAFF` not in the seeded role set →
`AP_CLERK` is the purchasing analog, documented).

**Final gate:** build 0/0, no EF drift (`AddInternalPurchaseOrder`), Domain
**79/79** (+12: 5 state-machine + 4 PoSettlement Theory + 3 prior-suite), Api
**87/87** (+5 `Sprint12PurchaseOrderTests`: SoD same/diff user, `ck_po_sod`
raw-CHECK, cancel, outstanding `8-14` bucket, cross-tenant null; 0 skip/regr),
tsc 0, next 0 (+3 PO routes +1 `/reports/outstanding-po`), **Playwright 29/29**
(two-pass: 28 @ VatMode=true incl. new `purchase-order-flow` — full
create→SoD-approve→Outstanding-lists→mark-sent→linked-VI-post→auto-close→
Outstanding-drops→VI-badge chain over the BFF proxy with 3 users; 1 @ false).
Mirror synced.

**Mechanism notes (→ Report-Backend17 §3):** `PO` document prefix was NOT
pre-seeded in `100` (QT/SO/DO were Sprint-1 forward scaffold; PO was not) →
added idempotently in seed 290 (escalated as a mechanism note, not a silent
workaround). `PURCHASING_STAFF` role absent from the seeded set → `AP_CLERK`
used as the create-side analog (matches the Sprint-7½ KI-01 purchase-RBAC
convention). `PoSettlement` extracted as a pure Domain type so the
auto-close/over-receipt math is unit-testable without a full GL fixture; the
VI-link end-to-end path is proven by the `purchase-order-flow` e2e (real
DbInitializer `teas_app`, real GL post). `ck_po_sod` test must set
`ApprovedBy` = the tenant `userId` because the `IAuditable` interceptor
overwrites `CreatedBy` with `tenant.UserId` (raw-CHECK assertion, not the
entity guard). **Scope cuts honored (Answer-Sana-Backend17):** no vendor
confirmation workflow, no 3-way match, no partial GR, no PO amendments
(cancel + recreate), no email-to-vendor, no catalog/price lists, no multiple
approvers — all Phase-2 / explicitly out of scope.

### 23.11 — Sprint 13c: e-Tax production-readiness + Tier 1 mock infra ✅ shipped Sprint 13c (2026-05-18)

> Closes all 8 gaps from `docs/etax-environment-tiers.md` for a config-only
> Tier 1→2→3 swap. Single phase, 8 ordered steps, 15/15 DoD. Phase-1 backbone
> + production-readiness COMPLETE.

**Shipped:** **P1** config drift removed — `Tax:EtaxEnabled`,
`Tax:EtaxDeliveryEmailCc`, `ETaxBehaviorOptions.RdCcAddress` deleted
(grep-clean; build catches orphan reads); single-source `ETax:Email:RdCcAddress`;
full canonical `ETax`/`RdApi` config tree laid in appsettings.Development.
**P2** `etax.submissions` append-only audit (`ETaxSubmission` + EF config +
`AddETaxSubmissionsAudit` + `300_etax_submissions_appendonly.sql` trigger,
UPDATE/DELETE → `check_violation`; `IETaxSubmissionAudit`). **P3** pure
`ETaxRecipientResolver` (RedirectAllToEmail diverts To+Cc; WhitelistDomains →
`etax.email.whitelist_violation`) + `ETaxDeliveryResult` carries the actual
sent To/Cc/Redirected for the forensic audit row. **P4** `IETaxXmlValidator` +
`LocalXsdValidator` (empty dir → graceful `IsValid=true`; `etax-schemas/` ships
README only — real ETDA มกค.14-2563 XSDs are an ops/Tier-2 prereq, flagged not
fabricated). **P5** `IRdEfilingClient` + `MockRdEfilingClient` (canned ack) +
`RdHttpEfilingClient` skeleton (Bearer, parsing TODO) + `RdApi:Provider` DI
selector; `TaxFilingStore.FinalizeAsync` auto-mode now calls the client
(STUB fallback kept). **P6** `IETaxSubmissionPipeline`
(build→sign→validate→send, one append-row per outcome; retry-budget checked
first → dead-letter) + pure `ETaxBackoff` + `ETaxRetryWorker.RunDueAsync` scan;
the `BackgroundService` loop lives in `Accounting.Api`
(`ETaxRetryHostedService`) so Infrastructure stays hosting-free (Clean Arch).
`TaxInvoiceService` post-commit path now enqueues the pipeline. **P7**
`dev-tools/gen-test-cert.sh`, `docker-compose.dev.yml` (Compose `include:` of
infra + MockServer — no duplication), MockServer init JSON, `.gitignore`
secrets. **P8** tests + `GET /etax/submissions` read endpoint (audit-viewer UI
= Phase 2).

**Final gate:** build 0/0, no EF drift (`AddETaxSubmissionsAudit`), Domain
**79/79**, Api **107/107** (+20: `ETaxUnitTests` resolver/backoff/xsd/mock-RD +
`Sprint13cEtaxPipelineTests` send-ok/signer-missing/xsd-fail/whitelist/
retry/dead-letter/**append-only-trigger**; 0 skip/regr), config grep-clean,
tsc 0, next 0 (no FE routes), **Playwright 29 pass + 1 honest skip / 30**.
Mirror synced.

**Mechanism notes (→ Report-Backend18 §3):** the `etax-pipeline-mock` e2e
**skips cleanly** in the standard two-pass harness (no Docker/MailHog/openssl
to stand up the Tier-1 stack) and runs green in a real Tier-1 env — same
honest discipline as the PostgresFixture `SkipReason` / non-VAT split; its real
acceptance gate is the manual **"Tier 1 startup smoke"**. ETDA XSDs not
committed (external controlled artifact — fabricating = false validation;
graceful Tier-1 skip + ops README, flagged). `GET /etax/submissions` reuses
`tax.filing.read` (no dedicated e-Tax perm seeded — e-Tax is tax-domain).
`ETaxRetryWorker` is tenant-free (writes audit rows with explicit companyId)
because a `BackgroundService` has no JWT context. `CLAUDE.md` "e-Tax
environment switching" section (DoD#10) is **Sana-owned** — proposed text
delivered via `progress.md` + Report-Backend18 §Sana, not edited directly
(binding ownership rule). **Scope cuts honored (§10):** no HSM, no durable
queue, no real RD UAT, no e-Receipt, no status-polling job, no dead-letter UI,
no OAuth — all Phase-2 / blocked on Phase-0 registration.

### 23.12 — Sprint 14: External API Integration + Per-Key BU Binding ✅ shipped Sprint 14 (2026-05-19)

> Microservice integration (Shopify/POS/internal) via API key + per-key BU
> binding. 8 phases, per-phase commits on the Phase-1 git baseline
> (`6c6418d`). First per-sprint git history.

**Shipped:** **P1** `ApiKeyAuthenticationHandler` ("ApiKey" scheme) +
`IApiKeyResolver` (KeyPrefix lookup → bcrypt verify → ordered fail codes;
LastUsed rate-limited ≥5min) + `ApiKeyGenerator` (key_+40, plaintext-once) +
`ITenantContext` +ApiKeyId/+ApiKeyDefaultBusinessUnitId + `ErrorEnvelope` +
`ApiKey.DefaultBusinessUnitId` FK + `AddApiKeyBuBinding`. **P2**
`IApiKeyService` (list/create/revoke/rotate, secret-free `activity_log`
audit) + `/api-keys` (perm `sys.api_key.manage`, seed 310) +
`/settings/api-keys` UI (plaintext-once modal). **P3** `ApiV1Endpoints`
(`/api/v1/*` TI/RC/QT/customers/products/system-info — delegates to existing
services, additive). **P4** `IdempotencyMiddleware` + `sys.idempotency_keys`
+ `AddIdempotencyKeys` + hourly cleanup hosted service (REQUIRED on v1
mutations; replay / 409 mismatch / 5xx-not-recorded / race-arbiter UNIQUE).
**P5** namespace-branched error envelope (v1 = plan §20.7; root = RFC-7807).
**P6** `PermissionHandler` is_api_key → ScopesJson; `apiperm:` policy prefix
pins the ApiKey scheme (root keeps `perm:`/JWT — auth isolation). **P7** pure
`ApiKeyBuBinding` (auto-fill / locked_mismatch) across TaxInvoice / Receipt /
TaxAdjustmentNote / Quotation + API-key cross-BU receipt reject (SO/DO inherit
the locked parent BU). **P8** unit+integration tests + e2e.

**Final gate:** build 0/0, no EF drift (`AddApiKeyBuBinding` +
`AddIdempotencyKeys`), Domain **83/83** (+4), Api **114/114** (+11), tsc 0,
next 0 (+1 route `/settings/api-keys`), **Playwright 29 pass + 2 honest skips
/ 31**, mirror synced.

**Mechanism notes (→ Report-Backend19 §3):** (1) **Two real latent bugs caught
in P8 e2e + fixed:** `HttpTenantContext` ctor-snapshotted the pre-auth user
(the ApiKey handler resolves `IApiKeyResolver → AccountingDbContext →
ITenantContext` *during* authentication) → made it lazy/per-access — a genuine
correctness bug affecting any API-key request; a scheme-less `perm:` policy
clobbered the API-key principal with the default JWT scheme → added the
scheme-pinned `apiperm:` prefix (root stays `perm:`/JWT — the split IS the
auth isolation). (2) **`IdempotencyFilter` → middleware** (spec's
`IEndpointFilter` returns the result object before serialization → cannot
capture the byte-for-byte response; middleware owns the response stream).
(3) Postgres rejects `WHERE expires_at > NOW()` partial-index predicate
(non-IMMUTABLE) → plain btree `ix_idemp_expiry`. (4) **`external-api-microservice`
e2e post-step §14-gated:** the GL `journal_entries` doc_no sequence desyncs in
the long-lived shared `teas_app` (no teardown — documented §14 fixture tech
debt; Sprint 14 touches no GL numbering; the path passes in other suites on
cleaner state) → conditional skip with the constraint signature, same honest
discipline as the Sprint-13c Tier-1-gated skip; never a fake pass. Auth +
idempotency replay/mismatch + scope + BU-lock are all asserted green.
(5) **OpenAPI (`docs/api/openapi.yaml`) is Sana-owned** — the `/api/v1/*` +
`ApiKeyAuth` delta is delivered via `progress.md` + Report-Backend19 §Sana,
not edited directly (binding ownership rule, as with the Sprint-13c CLAUDE.md
section). **Scope cuts honored (§10):** no webhook / rate-limit / OAuth /
approve-via-key / cross-BU-receipt-via-key / file-upload / generic DELETE —
all Phase-2.

### 23.13 — Sprint 14.5: §14 fix — shared test-fixture randomization ✅ done (2026-05-19)

> Doc note: Answer-Sana-Backend20 said strike "plan §23.3"; that is the
> Sprint-8 section. Per the established pattern (§23.4/.5/… each grow the
> numbering with this note) the Sprint-14.5 record is added here as §23.13.
> Minor — flagged in Report-Backend20.

~~Pending: gotcha §14 (test fixtures plant fixed identifiers against the
long-lived shared dev DB → cross-run accumulation → false-positive failures)
re-applied 7+ times across Phase 1, elevated from a Phase-2 candidate to
"actively blocking sprint e2e gates".~~ **DONE.**

**Shipped:** new pure `Accounting.TestKit` class lib (no production / test-
framework deps) + `TestIds` (prefix + 8-hex short-Guid suffix:
`CustomerCode`/`VendorCode`/`ProductCode`/`BranchCode`/`BusinessUnitCode`/
`ExpenseCategoryCode`/`WhtTypeCode`/`Email`/`TaxId`/`FuturePeriod`/`Name`),
referenced by `Accounting.Domain.Tests` + `Accounting.Api.Tests`, in
`Accounting.sln`. 6 meta-tests (format / 1000-unique / TaxId 0000+9 /
FuturePeriod ≥ +12 mo / BU ≤20). TS mirror
`frontend/e2e/helpers/test-ids.ts` (`node:crypto` `randomBytes(4)`,
byte-aligned surface). **7 §14 sites retrofitted to the one helper:**
`record-vendor.spec.ts` + `_helpers.ts createVendor` (real fix — was
low-entropy `Date.now().slice(-7)`, shared by many specs);
`business-units-setup.spec.ts` (S2 smoke); `Sprint55VendorInvoiceTests`,
`Sprint85VatThresholdTests`, `Sprint9VatComplianceTests`, `Sprint86ArWhtTests`
(consistency refactor — behaviour already §14-safe via ephemeral
Testcontainers `teas_test`, now single-sourced; intentional ม.82/4 window /
WHT rate-change dates left fixed by design). **Sprint-14 GL special case:**
`tools/dev-db-resync.sql` + `dev-tools/dev-db-resync.sh` — idempotent,
non-destructive resync of `sys.number_sequences.current_value` →
`MAX(running no.)` for `gl.journal_entries` + `sales.tax_invoices` +
`purchase.payment_vouchers` (real schema verified against `db/schema.sql`;
guarded `current_value < max` so re-runs are no-ops; posted-doc immutability
respected — counter only advances).

**Gate (static, runnable this session):** tsc 0, backend build 0/0, Domain
**89/89** (+6, 0 skip/regr). **DB/Docker-gated (NOT runnable — no Docker,
port 5432 closed this session, honest):** Api Testcontainers suite, 3×
consecutive e2e re-run per site, Playwright 31/31, the one-time
`dev-db-resync` execution — deferred to the dev env with exact commands in
`progress.md` cont. 41. Same honest discipline as the Sprint-13c Tier-1 /
Sprint-14 §14 e2e skips; never a fake pass.

**Sana-owned doc deltas (binding ownership rule — routed, not edited
directly):** CLAUDE.md new §15 "Test data discipline" + `runtime-gotchas.md`
§14 "Resolved Sprint 14.5" note — full proposed text in `progress.md`
cont. 41 §"→ Sana" + Report-Backend20.

**§14 is now extinct:** no fixture in the suite plants a fixed identifier on
the shared dev DB; new tests use `TestIds` (enforced via CLAUDE.md §15 once
Sana applies it). Scope cuts honored: per-test DB reset, Testcontainers-
per-test, CI parallelization changes — all Phase-2.

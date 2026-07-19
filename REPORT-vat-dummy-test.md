# REPORT — VAT dummy company + untested-path round (2026-07-18)

Handoff: `HANDOFF-vat-dummy-company-test.md`. Prod v1.21.5.
Round: create VAT-enabled dummy company → payroll Post full chain → VAT sales chain → ภ.พ.30.

## Findings

| # | Severity | Area | Finding | Status |
|---|----------|------|---------|--------|
| F-1 | **CRITICAL** | Onboarding / RLS | Company creation is **broken on prod since the 600 RLS hardening (2026-07-08)**: `CompanyService.CreateAsync` was missed by the superadmin-tenant-scope Family-B inventory. It writes branch / company_profile / CoA / tax codes / WHT types / expense categories / roles for the NEW company while the DB session is still pinned to the CALLER's company → RLS 42501 on `master.branches`. Worse: the method has **no wrapping transaction**, so the first `SaveChangesAsync` (companies row — no RLS) COMMITS and everything after is lost → **half-created tenant** (company row exists; 0 branches, 0 CoA, 0 tax codes, 0 roles). Every new-customer onboarding on prod fails this way. | **FIXED + VERIFIED LIVE** — 4b92efd (tx wrap + LOCAL company_id pin; CompanyCreateRlsTests red→green; Opus APPROVE) → v1.21.6 deployed (DEPLOY_OK 10/10 probes, DB backup, sql_scripts 69 unchanged) → orphan co4 deleted → dummy recreated via UI = company 5 seeded 1 branch / 25 CoA / 12 tax codes / 15 WHT / 19 expcat / 11 roles (matches co2/co3) |
| F-2 | Low | FE | On the create-company 500, FE shows generic "An unexpected error occurred" toast and the modal stays open inviting resubmits (blocked only by the duplicate-taxId guard). Root fix is F-1; FE-side generic-500 toast is acceptable. | Log only |
| F-3 | **HIGH** | Tax engine (PIT ม.50(1)) | **Under-withholding in the onboarding year for employees hired before the system's first payroll run.** Run 202607 (first run ever), EMP001 hired 2025-01-01, salary 80,000: engine withholds **1,408.33/mo**; correct ม.50(1) for a full-year employee = **6,075/mo** (960,000 proj → taxable 789,500 → tax 72,900/yr ÷12). Engine formula (`PayrollRunService.cs:51,99`): `monthsRemaining = 13−month` (July→6), projection = YTD-in-system (0) + salary×6 = **480,000** — deliberately NOT hire-date-aware (doc comment line 17-18: "mid-year joiner is handled by YTD=0"). Correct in steady state (runs since Jan: YTD fills the gap) and for true mid-year joiners; wrong exactly when a company onboards TEAS mid-year with pre-existing staff — their Jan–Jun pay exists outside the system. Reverse-engineered lock: 480,000−100,000−60,000−10,500 = 309,500 → tax 8,450 ÷6 = 1,408.33 (matches UI to the satang). Sub-issue (b): `ssoAllowance = ssoEmp×12` (`:92`) = 10,500 full-year SSO deducted against a 6-month income projection — internally inconsistent both for this case AND for true mid-year joiners (actual SSO would be 875×6=5,250). A2 (30k, married+child) and A3 (15k) = 0 tax under both engine and hand-calc ✓; SSO per-row 875/875/750 = hand-calc exact ✓ (cap 17,500×5% confirmed). | **Needs Ham design decision**: (1) accept + document (year-end ภ.ง.ด.91 settles), (2) hire-date-aware projection (over-withholds instead, since old-system PIT isn't in priorPit), (3) proper fix = opening YTD balances (ยอดยกมา: income + PIT + SSO per employee) — recommended |
| F-4 | Low | Payroll UI | Run-header card "ประกันสังคม ฿5,000.00" = employee+employer combined (2,500+2,500) while the per-row ปกส. column shows employee-only (2,500 total) — no label hint that the header includes the employer side; reads as a discrepancy. | Suggest label "ประกันสังคม (รวมนายจ้าง)" or split the card |
| F-5 | Medium | Infra (S13 recurrence) | `POST /payroll/runs/5/approve` and `/post` both returned **503 to the browser but succeeded server-side** (UI showed success, JE created) — same CF-edge 503 signature as S13 (origin 2xx / edge 5xx). New occurrences 2026-07-18 ~13:1x ICT for Ham's CF log pull. Danger class: a user could retry a "failed" post. | Track with S13; idempotency on post protects, but CF fix pending |
| F-6 | **Medium-High** | Payroll design gap | "จ่ายแล้ว" is **status-only** — no settlement JE (Dr 2170 เงินเดือนค้างจ่าย / Cr 1120 เงินฝากธนาคาร) is created, and no manual-JE UI exists to record it → 2170 accumulates forever, bank balance never moves, balance sheet stays wrong after payday. Verified: TB identical before/after Pay. | Needs design: post a payment JE on Pay (against a chosen bank account), or provide a manual JE/payment tool |
| F-7 | Low | i18n | Payroll list status filter renders raw key `status.POSTED`; duplicate-period 422 toast shows raw English API detail ("A payroll run already exists for period 202607.") on Thai UI; create-run modal prefills the CURRENT (already-existing) period instead of the next open one. | Batch with next i18n sweep |

### Plan A payroll — PASS summary (engine-internal consistency)
Run 202607 #5 (07-2026-PR-0001): SSO rows 875/875/750 = hand-calc exact (cap 17,500 ✓ / 5% actual ✓); JE tie-out exact (5400 Dr 125,000 / 5410 Dr 2,500 / 2153 Cr 1,408.33 / 2160 Cr 5,000 / 2170 Cr 121,091.67; Dr=Cr 127,500); docs: ภ.ง.ด.1 PDF (identity+totals ✓), สปส.1-10 .txt (TIS-620 fixed-width, header 3/125,000/2,500+2,500, rows ✓ — employer SSO account = zeros, not yet configurable at create), payslip (BE dates, Thai amount text, YTD ✓), 50ทวิ (official form, both parties + ภ.ง.ด.1ก box ✓), ภ.ง.ด.1ก (2569, 3/125,000/1,408.33 ✓). Pay → status ✓ (but F-6). Month-2 run 202608: tax continuity stable at 1,408.33 (YTD catch-up mechanism ✓, still F-3-affected). Duplicate-period guard 422 ✓. PIT amounts themselves are F-3-affected (engine-internal consistent, under-withheld vs proper ม.50(1) for pre-existing staff).

### F-1 evidence
- UI: `POST /api/proxy/companies` click #1 at 11:58:12 → error toast; company row WAS created (ID 4). Retries → 422 `company.duplicate`.
- pm2 `teas-api` log 2026-07-18T11:58:12: `Npgsql.PostgresException 42501: new row violates row-level security policy for table "branches"` (DbUpdateException in `AccountingDbContext`).
- Prod DB after: `master.companies` has id=4 (บริษัท ทดสอบ VAT (DUMMY) จำกัด) but branches/coa/tax_codes per company: co2 = 1/25/12, co3 = 1/25/12, **co4 = 0/0/0**.
- Same class as the 2026-07-09 log entry `42501 ... "chart_of_accounts"` (earlier unnoticed occurrence).
- Root cause file: `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs` `CompanyService.CreateAsync` (~line 186): 4 sequential `SaveChangesAsync` + `sys.seed_company_roles()` with no transaction and no tenant-context pin. `600_superadmin_scoped_rls.sql` G1 tables it writes (chart_of_accounts, tax_codes, wht_types, expense_categories) have **no bypass arm at all**, branches (G2) has one but nothing pins it.
- Why tests never caught it: teas_test connects as Postgres SUPERUSER → RLS bypassed (memory: rls-masked-by-superuser-tests). `OnboardingFoundingAddressTests` passes vacuously.

### F-1 fix (spec `specs/fix-company-create-rls-atomic.md`)
Wrap `CreateAsync` in ONE transaction; after the companies-row `SaveChangesAsync` allocates the new id, pin `set_config('app.company_id', <newId>, true)` (LOCAL, auto-reverts at commit) so all seeding writes run AS the new tenant — passes every `company_isolation` policy naturally. Zero DDL / zero new SqlScript / zero policy weakening. Model pattern already in-repo: `VatRegisterSnapshotJob.cs:95-98`; spec 600 itself lists the company-id LOCAL pin as the "tighter alternative" mechanism. Repair: delete orphan co4 row (zero children), recreate via UI post-deploy.

| F-8 | Medium | tax-summary | ภ.ง.ด.1 column shows "—" even though payroll run 202607 posted 1,408.33 of ภ.ง.ด.1 withholding — the column reads WHT certificates only (footnote: "WHT จากหนังสือรับรอง 50ทวิ"), payroll-sourced withholding never appears. Users reconciling monthly remittances will miss it. | Include posted payroll PIT in the ภ.ง.ด.1 column (or footnote the exclusion) |
| F-9 | Medium | CoA / expense mapping | Default CoA (25 accounts) has **no ต้นทุนสินค้าขาย (COGS) account**; the COGS expense category maps to **5200 ค่าใช้จ่ายค่าบริการ** → a goods purchase for resale lands in "ค่าใช้จ่ายค่าบริการ" on TB/P&L (verified: PV-COGS-0001 10,000 → 5200). Misclassified P&L for any trading company. | Add 5000-range COGS account to default CoA + remap the COGS category |
| F-10 | Low | i18n / ภ.พ.30 | ภ.พ.30 page bottom warning renders in English on Thai UI: "Last day of filing: 2026-08-15. Run finalize at least 1 day before." | i18n batch |

### Plan B + purchase chain + reports — PASS summary (all hand-calc ties)
- Sales chain QT-0001→SO-0001→DO-0001→IV-0001→**TI-0001**→RC-0001 all Posted; 7,000+VAT 490=7,490 correct at every hop; doc numbers run; ref chain 6 docs; RC prefilled from TI with VAT 0 (correct — VAT sits on TI). Second chain IV-0002→**TI-0002** (สมชาย, 5,000+350) posted, left unpaid.
- Purchase chain PO-0001 (10,000+700) → VI-0001 (จาก PO, vendor TI no. + งวดเครดิต ม.82/4, 19 expense categories seeded by the F-1 fix ✓) → PV-COGS-0001 posted (WHT ไม่หัก — goods, correct). PO auto-closed by VI.
- **ภ.พ.30 exact**: ขาย 12,000/ภาษีขาย 840 · ซื้อ 10,000/ภาษีซื้อ 700 · **ชำระสุทธิ 140** — matches hand-calc to the satang. Real form renders (non-VAT message gone), กำหนดยื่น 2026-08-15.
- sales-summary has data for the first time (Repttown was always empty): by-customer 2 rows 12,000/840/12,840 + by-product grouping ✓ + basis footnote showing ✓.
- tax-summary: VAT สุทธิ 140 ชำระเพิ่ม, July row 12,000/137,500/−125,500/840/700/140 ✓ (but F-8).
- AR aging: สมชาย 5,350 bucket 0-30 ✓, **1130 GL-vs-subledger tie banner 5,350 = 5,350 Dr=Cr ✓** (first time with nonzero data).
- Trial balance @31/07 fully ties (169,230 = 169,230): 1120 −3,210 (7,490−10,700), 1130 5,350, 1170 700, 2110 0 (VI paid), 2151 840, payroll block unchanged, 4000 12,000 (but 10,000 in 5200 per F-9).
- Bank-reconciliation report shows real data first time: GL −3,210, deposit-in-transit RC 7,490, outstanding payment PV 10,700, **difference 0.00** — recon math correct. (Statement import flow not yet exercised — no statement file; follow-up.)

## Test progress
- [x] Step 0 complete (login/superadmin ✓, F-1 found→fixed→v1.21.6 deployed→co5 seeded ✓, master data ✓)
- [x] Plan A payroll full chain (Post #1 in history, 5 doc types, JE tie exact, Pay status, month-2 continuity, dup-guard) — F-3/F-4/F-5/F-6/F-7
- [x] Plan B VAT sales chain + purchase chain + ภ.พ.30/sales-summary/tax-summary/AR aging/TB — all money math ties; F-8/F-9/F-10
- [~] Plan C: bank-recon report ✓ (math ties); statement IMPORT flow not yet run; manual posted-state additions pending
- [ ] Fix round for findings (F-3 + F-6 need Ham's design decision; F-4/F-7/F-8/F-9/F-10 batchable) → re-test loop per Ham's instruction

## R6 decision input (for Ham)
sales-summary basis = posted TIs works correctly on a VAT company (verified). For non-VAT companies (Repttown) the page stays empty by design with the footnote. If Repttown should see receipt-based sales there, extend basis; otherwise the footnote suffices. Recommendation: footnote suffices — Repttown has ใบเสร็จ-based revenue visible in P&L/tax-summary already; mixing bases in one report invites tie-out confusion.

## R10 first-click log (this round)
None observed — no picker/modal first-click misses hit during this entire round (QT/SO/DO/IV/TI/RC/PO/VI/PV pickers all responded first click). Blob-PDF tab flakiness (known wiki entry) was the only interaction quirk.

## Fix round VERIFIED-LIVE on prod v1.22.1 (2026-07-18/19, co5 re-test — all clean)
| Finding | Live verification |
|---|---|
| F-3 | EMP001 opening YTD (2026 / 480,000 / 36,450 / 5,250) via new employees-modal section → recreated run 202608: PIT = **7,008.33 = hand-calc exact** (projection 960,000-basis, catch-up over 5 remaining months; SSO rows unchanged 875/875/750). Modal opening-year defaults empty (no spurious stamping). |
| F-6 | Pay dialog shows bank selector (KBANK preselected) → Pay posted settlement JE: TB @31/08 — 2170 Dr 115,491.67/Cr 236,583.34 = **121,091.67 remaining (July-only, pre-fix Paid)**, 1120 −118,701.67 exact, Dr=Cr 412,221.67. |
| F-9 | 5000 ต้นทุนขาย row present in TB; new VI-0002 (COGS, 2,000+140) posts to **5000** (old VI stays on 5200 — no retroactive reclass). Deploy backfill: 5000 in 3/3 companies, remap 2/2 companies-with-COGS. |
| F-8 | tax-summary ภ.ง.ด.1 column: ก.ค. 1,408.33 + ส.ค. 7,008.33 = 8,416.66 + footnote "ภ.ง.ด.1 รวมเงินเดือนที่ Post แล้ว". |
| F-4 | Run header card now "ประกันสังคม (รวมนายจ้าง)". |
| F-7 | Status filter shows "บันทึกบัญชีแล้ว" (raw key gone); create-run modal prefills NEXT open period (202608 after 202607); dup-toast Thai = code+tsc verified (not live-fired). |
| F-10 | ภ.พ.30 deadline warning now Thai; ภ.พ.30 July updated correctly after new VI (ซื้อ 12,000/840 → สุทธิ 0.00). |

Deploy trail: v1.22.0 rolled back automatically (script 625 RLS 42501 — SqlScripts run under NOBYPASSRLS app role; spec assumption wrong; wiki entry added) → 625 rewritten with per-company `set_config('app.company_id',…,true)` DO-loop → **v1.22.1 API DEPLOY_OK 9/9 + FE_DEPLOY_OK**. Standing open item: `McpServerSmokeTests.E3_create_vendor` red on CI since ~v1.21.5 era (pre-existing, needs separate root-cause round).

## Open-items round (2026-07-19 ~00:3x, co5 on prod v1.22.1)

### Statement IMPORT flow — PASS (first exercise ever)
- KBiz CSV synthetic statement (KBANK 123-4-56789-0, period 01–31/07/2026, opening 0.00,
  deposit 7,490.00 + withdrawal 10,700.00, closing −3,210.00) uploaded via
  /bank-accounts/1 → "+ นำเข้า Statement": parsed 2 รายการ, toast Thai, status Parsed,
  metadata (period/line count) rendered correctly in the imports list.
- กระทบยอดธนาคาร page: both lines Unmatched with BE dates ✓; "ค้นหารายการที่ตรงกัน"
  suggested the EXACT counterpart docs (07-2026-RC-0001 ฿7,490 / 07-2026-PV-COGS-0001
  ฿10,700) → confirmed both → Matched, ยกเลิกจับคู่ available.
- Bank-recon report @31/07 after matching: Statement −3,210.00 = GL −3,210.00,
  เงินฝากระหว่างทาง 0.00, รายการจ่ายค้าง 0.00, ยังไม่จับคู่ 0.00, **ผลต่าง 0.00** — full tie.
  (Also confirms the Aug payroll Pay JE does NOT leak into the July cutoff.)

### CN (ใบลดหนี้) → ภ.พ.30 — PASS (money math exact)
- 07-2026-CN-0001 created against TI-0001 (reason PriceReduce, ปรับ 1,000 + VAT 70 =
  1,070, Thai amount-in-words correct), posted with immutability warning (ม.86/4 /
  ม.86/12) ✓; ref chain sidebar now 7 docs.
- ภ.พ.30 July preview after CN: ขายที่ต้องเสียภาษี **11,000 → ภาษีขาย 770**
  (12,000−1,000 / 840−70 exact), ซื้อ 12,000/840, ชำระสุทธิ 0.00,
  **เครดิตยกไปงวดหน้า 70.00** — carry-forward credit logic correct.
- TB @31/07: Dr=Cr **172,440.00 = 172,440.00** ✓. CN JE correct: 4100 รับคืน/ส่วนลด
  Dr 1,000 (contra-revenue, ไม่ net 4000), 2151 Dr 70 (คงเหลือ −770), 1130 Cr 1,070
  (AR 5,350 → 4,280).

### New findings this round
| # | Sev | Area | Symptom | Recommendation |
|---|---|---|---|---|
| F-11 | Medium | CN i18n / legal doc | CN reason-code dropdown shows raw enum keys (Typo/AmountError/CustomerInfo/Return/PriceReduce/Cancel) on the Thai UI, and the raw key prints on the POSTED document line: "เหตุผล (PriceReduce): …" — ใบลดหนี้ is a legal ม.86/10 form; the reason should render in Thai (e.g. ลดราคา/รับคืนสินค้า/ยกเลิก) | Map reason enum → Thai labels in dropdown AND document template |
| F-12 | Low | UX batch | (a) statement-import modal has no format hint (which bank/format CSV is accepted); (b) CN confirm dialog identifies the ref doc as "TI #1" (internal id) instead of 07-2026-TI-0001; (c) match-confirm toast is a bare "บันทึก" | Batchable polish |

### F-11/F-12 fix arc — v1.22.2 SHIPPED + F-11 VERIFIED LIVE (2026-07-19 ~02:3x)
| Finding | Status |
|---|---|
| F-11 | **VERIFIED LIVE**: CN-0001 document line now renders "เหตุผล (ลดราคา/ส่วนลดภายหลัง): …" — raw enum key gone from the legal doc; fix covers dropdown + FE detail + PDF (single shared BuildPaperAsync source) + DN codes; e2e test that pinned the raw key now guards the label. |
| F-12 | Shipped in same release; live-verified via deploy content anchors (จำนวนเงินผิด + "รองรับไฟล์ CSV จาก KBiz" present in built FE) + tsc/next build/164 backend tests. Visual eyeball pass pending (TEAS session expired post-deploy) — optional. |

Deploy trail: 3fc7619 → CI green → PR #88 → tag v1.22.2 → MinVer 1.22.2 ✓ → DB backup
teas-pre-v1.22.2-deploy-*.sql.gz → DEPLOY_OK (version=1.22.2, sql_scripts unchanged 71)
→ FE_DEPLOY_OK → public probes (login/oauth 200, proxy auth-gate 401) → scripts archived
publish/v1.22.2/ (4fcdaaa).

S13 status: origin ruled out; CF-dashboard confirmation + scoped WAF Skip rule = waiting
on Ham (CF login lives in a different Chrome profile than the extension's).

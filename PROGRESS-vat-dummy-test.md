# PROGRESS — VAT dummy company + untested-path round (2026-07-18)

Goal: HANDOFF-vat-dummy-company-test.md (Ham /goal). Report: REPORT-vat-dummy-test.md.

**Scope expansion + fix authority (Ham, 2026-07-18 ~12:2x):** ทดสอบทั้งหมดรวมการซื้อ
การขายเต็มสายบน VAT company (ก่อนหน้านี้เทสแค่ non-VAT/Repttown) — เช็คทั้งหมด →
ระบุปัญหา → **แก้ได้เลย** → เช็คซ้ำ → วนจนไม่พบปัญหา. ไม่ต้องรอ approve รายรอบ.
Purchase side จึงยกระดับจาก "ซื้อ 1 ใบ" เป็นสายเต็ม: PO → VI (VAT ซื้อ) → PV (+WHT)
→ ภ.พ.30 ฝั่งซื้อ + tax-summary + AP side.

## Capability map (kickoff)
Chrome MCP (UI test drive) ✓ · SSH prod DB via repttown_deploy key ✓ (read-only checks
+ orphan cleanup) · sonnet-implementer / opus-reviewer (fix round) ✓ · MCP TEAS-Repttown
connector = **forbidden this round** (points at Repttown, wrong company).

## Done
- [x] Step 0.1: Ham logged in; user = Super Admin; create-company UI = /settings/companies
- [x] Dummy create attempted → **F-1 CRITICAL found** (see report): CreateAsync RLS 42501
      + no tx → half-created co4 (0 branches/CoA/tax_codes). Root-caused to 600-hardening
      Family-B inventory miss. Evidence: pm2 log 11:58:12, pg_policies, per-company counts.
- [x] REPORT-vat-dummy-test.md written (F-1 critical, F-2 low)
- [x] Fix spec specs/fix-company-create-rls-atomic.md (design: single tx + LOCAL
      set_config app.company_id=<newId>; model = VatRegisterSnapshotJob.cs:95-98)
- [x] Ham push-notified (proactive)

- [x] F-1 fix arc COMPLETE: sonnet impl (red→green CompanyCreateRlsTests, suite 890/8/1
      pre-existing E3) → Opus APPROVE (4 lenses) → Fable diff read → commit 4b92efd →
      PR #85 merge → tag v1.21.6 → build (MinVer 1.21.6 ✓) → DEPLOY_OK (10/10 probes,
      backup, scripts 69) → public probes OK → orphan co4 DELETED → dummy recreated via
      UI = **company 5** seeded 1/1/25/12/15/19/11 (branch/profile/coa/tax/wht/expcat/roles) ✓

## In-flight
- (none)

- [x] Switched to co5; VAT menus present (ใบกำกับภาษี/ใบลดหนี้/ใบเพิ่มหนี้ + dashboard VAT card)
- [x] Step 0.4 master data: BU01, C001 (นิติ VAT), C002 (บุคคล), V001 (นิติ VAT), P001 GOOD
      1,000 ขาย+ซื้อ, S001 SERVICE 5,000, KBANK 123-4-56789-0 → GL 1120
- [x] Plan A1-A2: 3 employees (EMP001 80k โสด hire 2025-01-01 / EMP002 30k สมรส+บุตร1 /
      EMP003 15k) → run 202607 created (pay 2026-07-30) → breakdown audited vs hand-calc:
      SSO rows 875/875/750 EXACT ✓; **F-3 HIGH found** (PIT 1,408.33 vs correct 6,075 —
      onboarding-year under-withholding, `13−month` not hire-aware + sso×12 inconsistency;
      REPORT updated; needs Ham decision, chain continues w/ engine numbers) + F-4 low
      (header ปกส 5,000 = รวมนายจ้าง, no label) → อนุมัติ → **POST #1 in history:
      07-2026-PR-0001** ✓ → TB @31/07: 5400 Dr125,000 / 5410 Dr2,500 / 2153 Cr1,408.33 /
      2160 Cr5,000 / 2170 Cr121,091.67, Dr=Cr 127,500 — **tie-out exact** ✓

- [x] **Plan A COMPLETE** — docs 5/5 verified (ภ.ง.ด.1 p.1 ✓ / สปส.1-10 txt TIS-620 ✓ +
      PDF opened / payslip EMP001 ✓ / 50ทวิ EMP001 ✓ / ภ.ง.ด.1ก ✓), Pay = status-only
      (**F-6**: no settlement JE, TB unchanged), 202608 draft continuity 1,408.33 ✓,
      dup-guard 422 ✓. New findings F-5 (approve/post 503-at-edge-but-succeeded, S13
      recurrence), F-6, F-7 (status.POSTED raw key + EN toast + prefill current period).
      Blob-PDF tabs are flaky (screenshot works ~50%, wiki entry exists).

- [x] Plan B core chain COMPLETE: QT-0001 (7,000+490=7,490 ✓ every hop) → accepted →
      SO-0001 Posted → DO-0001 Issued → IV-0001 Issued → **TI-0001 Posted** → RC-0001
      Posted (RC prefilled from TI, receipt VAT 0 correct). Doc numbers 07-2026-XX-0001
      pattern ✓, ref chain 6 docs ✓, BE dates ✓.

- [x] TI-0002 unpaid (สมชาย 5,350) · purchase chain PO-0001→VI-0001→PV-COGS-0001 ✓
- [x] Reports sweep ALL TIE: ภ.พ.30 (840/700/สุทธิ 140 exact), sales-summary (first data
      ever + groupings + footnote), tax-summary (140 ชำระเพิ่ม; F-8 ภ.ง.ด.1 col blank),
      AR aging (5,350 + 1130 tie banner ✓), TB @31/07 169,230=169,230 (F-9 COGS→5200),
      bank-recon report (GL −3,210 / in-transit 7,490 / outstanding 10,700 / diff 0.00 ✓)
- [x] REPORT-vat-dummy-test.md final for this round: F-1..F-10 + R6 input + R10 log (clean)

## Next (resume here if interrupted)
1. Ham decisions needed: F-3 (PIT onboarding-year projection — recommend opening-YTD
   feature), F-6 (Pay settlement JE design). Batchable fixes: F-4/F-7/F-8/F-9/F-10.
2. Fix round after Ham input → deploy → re-test loop until clean (standing instruction).
3. Plan C leftovers: bank statement IMPORT flow (needs statement file/upload UI at
   /bank-accounts/[id]/imports), TI PDF visual check (ภ.พ.20 fields — blob tab flaky),
   สปส.1-10 PDF + ภ.ง.ด.1 ใบแนบ pages visual re-check, CN (ใบลดหนี้) → ภ.พ.30 reflect,
   month-2 payroll POST (left draft 202608), manual บท 6 posted-state additions.
4. co2/co3 untouched ✓. Dummy co5 = safe playground for future rounds.
5. Test Plan A payroll (3 emps 80k/30k/15k, breakdown vs hand-calc, Post!, ภ.ง.ด.1,
   สปส.1-10, slips, 50ทวิ, ภ.ง.ด.1ก, GL tie 5400/5410/2153/2160/2170, Pay, month 2, negatives)
6. Test Plan B VAT chain (QT→SO→INV→TI→RC, sales-summary, ภ.พ.30 both sides, tax-summary,
   AR aging + 1130 tie, CN if time)
7. Plan C: bank-recon, R10 first-click log, R6 decision input, manual posted-state additions
8. REPORT update + STATUS + commits per verified unit

## Rules recap
- Dummy company only; verify company badge before every doc. co2/co3 untouchable.
- Logged out → stop + notify + wakeup chain. Quota ≥85% → checkpoint protocol.

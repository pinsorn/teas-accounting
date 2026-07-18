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
1. [x] Ham approved ALL recommendations ("แก้เลยเอาตามที่แนะนำเลย") → spec
   `specs/fix-vat-round-findings.md` (a33143b) → **Codex implementing in working tree**
   (quota arbitrage, Claude pool 92%). No commits from Codex.
   Codex background task id: `task-mrqany3j-s65krr` — check via `/codex:status
   task-mrqany3j-s65krr` (or codex CLI status) at each wakeup; evidence of completion =
   spec checklist [x] + working-tree diff.
2. [x] FIX ROUND SHIPPED — **v1.22.1 LIVE on prod** (2026-07-18 ~22:0x): Codex impl
   (2 rounds — Opus REJECT on SSO formula asymmetry → corrected to
   min(priorInSystemSso+openingSso+ssoEmp×monthsRemaining, cap), both findings
   CONFIRMED-CLOSED) → commit 7fed441 → v1.22.0 deploy FAILED (625 RLS 42501:
   SqlScripts run under NOBYPASSRLS app role, spec wrongly said superuser; rolled
   back clean, wiki entry added) → 625 rewritten per-company-pin DO block → 05e7fc5
   → v1.22.1 → API DEPLOY_OK 9/9 (scripts=71, ytd cols 4, 5000 CoA 3/3, COGS remap
   2/2 — co3 has no COGS category, probe adjusted) + FE_DEPLOY_OK + public probes 200.
   Note: E3 create_vendor has been failing CI since ~v1.21.5 era (pre-existing, open
   item, admin-merge pattern documented).

## RE-TEST on co5 (next wakeup after quota reset — v1.22.1 verification):
   (a) EMP001 employees modal: fill ยอดยกมา (ปี 2026, income 480000, pit 36450
       [=6×6075 → clean catch-up math], sso 5250) → delete draft 202608 → recreate →
       breakdown: projected should be 960,000-basis; verify PIT vs hand-calc
       (allowance = 875[Jul in-system] + 5250 + 875×5 = 10,500; taxable 789,500 →
       annual 72,900 − priorPit(36,450+1,408.33) = 35,041.67 → /5 = 7,008.33/mo)
   (b) อนุมัติ+Post 202608 → Pay w/ KBANK dropdown → TB: 2170 = 121,091.67+Aug-net
       − Aug-net...  actually: July run Paid pre-fix (status only) so 2170 still
       carries July 121,091.67; after Aug Pay-with-JE: 2170 = July only; 1120 −Aug net
   (c) new VI (V001, P001, COGS) → posts to 5000 ต้นทุนขาย not 5200 (check TB)
   (d) tax-summary: ภ.ง.ด.1 column July = 1,408.33 (payroll PIT now included)
   (e) i18n: payroll filter no raw status.POSTED; dup-period toast Thai; create-run
       prefill = next open period; ภ.พ.30 warning Thai; SSO header "(รวมนายจ้าง)"
   (f) employees modal: new-employee create → no spurious opening year persisted
   Then REPORT update (F-3..F-10 verified-live column) + STATUS + Ham summary.
3. OPEN-ITEMS ROUND (2026-07-19 ~00:3x):
   [x] statement IMPORT flow — PASS: synthetic KBiz CSV (7,490 in / 10,700 out,
       closing −3,210) → parsed 2 lines → match suggestions EXACT (RC-0001/
       PV-COGS-0001) → both Matched → recon report ผลต่าง 0.00 full tie.
   [x] CN → ภ.พ.30 — PASS: 07-2026-CN-0001 (PriceReduce 1,000+70) posted vs TI-0001;
       ภ.พ.30 ขาย 11,000/770, เครดิตยกไป 70; TB 172,440 Dr=Cr; CN JE = 4100 contra
       Dr 1,000 / 2151 Dr 70 / 1130 Cr 1,070. New findings F-11 (CN reason raw enum
       key on UI+printed doc, medium) + F-12 (low UX batch) — in REPORT.
   [x] E3 create_vendor ROOT-CAUSED + FIXED (ac048e8): stale test fixture — E3 sent
       vatRegistered=true + taxId=null, WP1 65b9b2b (2026-07-14) added the domestic-VAT
       tax-id validation; fix = valid checksum constant. Suite 897/8/0. CI watch
       running (bg). Wiki entry rewritten [FIXED].
   [x] S13 CF-edge 503 investigation COMPLETE (db29473): origin 100% ruled out (zero
       503s ever logged, no resource pressure). Top hypothesis HIGH = CF Bot Fight
       Mode scoring the automation browser; NEEDS HAM: CF dashboard checklist (8 items)
       in specs/fix-s13-cf-edge-503.md; proposed scoped WAF Skip rule drafted.
   [x] manual บท 6 posted-state additions DONE: Post-JE table (5400/5410/2153/2160/
       2170 + worked example 127,500), Pay-with-bank + settlement JE, opening-YTD
       section, dup-guard toast + next-open-period prefill, TIS-620 footnote. All
       facts code-cited; mkdocs build clean; ম grep 0. Screenshots deferred (🚧
       convention — walkthrough 06.01 keeps run in DRAFT by design; posted-state
       captures need full local stack, noted in-manual).
   [ ] leftover: TI PDF visual check (ภ.พ.20 fields — blob tab flaky), สปส.1-10 PDF
       + ภ.ง.ด.1 ใบแนบ visual re-check
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

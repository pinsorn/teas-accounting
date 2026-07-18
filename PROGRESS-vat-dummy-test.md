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

## In-flight
- [~] sonnet-implementer: implement spec (test-first, CompanyCreateRlsTests red→green,
      full suite, no commit)

## Next (resume here if interrupted)
1. Worker returns → Opus Tier-2 review of diff (RLS/security lens) → Fable reads full diff
2. Commit fix → release (release-please PR admin-merge → tag → build from tag worktree,
   REAL path not subst — MinVer) → deploy API (DB backup first; sql_scripts stays 69 —
   no new script) → public E2E probe
3. Delete orphan co4 row (verify zero children first) → recreate dummy via UI →
   verify tax_codes=12, branch 00000, CoA 25, sidebar VAT menus
4. Handoff Step 0.4: BU, 2 customers (นิติ+บุคคล), 1 vendor, 2-3 VAT products, 1 bank acct
5. Test Plan A payroll (3 emps 80k/30k/15k, breakdown vs hand-calc, Post!, ภ.ง.ด.1,
   สปส.1-10, slips, 50ทวิ, ภ.ง.ด.1ก, GL tie 5400/5410/2153/2160/2170, Pay, month 2, negatives)
6. Test Plan B VAT chain (QT→SO→INV→TI→RC, sales-summary, ภ.พ.30 both sides, tax-summary,
   AR aging + 1130 tie, CN if time)
7. Plan C: bank-recon, R10 first-click log, R6 decision input, manual posted-state additions
8. REPORT update + STATUS + commits per verified unit

## Rules recap
- Dummy company only; verify company badge before every doc. co2/co3 untouchable.
- Logged out → stop + notify + wakeup chain. Quota ≥85% → checkpoint protocol.

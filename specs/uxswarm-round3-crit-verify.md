# UX SWARM ROUND 3 — verify CRIT-1/CRIT-2 closed under concurrency (2026-07-19 ~23:5x)

Ham /goal: "แก้ไขบัคให้เสร็จ จากนั้นส่ง Sonnet 10 ตัวไปลองทดสอบระบบด้วย Playwright เหมือนเดิม".
Fix shipped = v1.22.6 (626 seq-drift reconcile + retry guard, 627 TAX_OFFICER grant). This round
RE-RUNS the 10-role concurrent swarm to PROVE the CRITs are closed on prod + catch any regression.

Target: **https://teas.kazaki-rio.com** (prod v1.22.6), company = บริษัท ทดสอบ VAT (DUMMY) จำกัด (co5).
Accounts already exist from round 2 (REUSE — do NOT recreate): sales01/acct01/appr01/ap01/ar01/
audit01/chief01/admin01/purch01/tax01, password pattern `UxSwarm-2026-<suffix>` (A1 sales, A2 acct,
A3 appr, A4 ap, A5 ar, A6 audit, A7 chief, A8 admin, A9 purch, B1 tax).

## PRIMARY ASSERTIONS (this round's reason to exist)
- **CRIT-1 CLOSED:** every doc-numbering write — QT send, TI post, RC post, VI post, PV post,
  PO approve — must return **2xx, ZERO HTTP 500 / 23505**, even with all 10 agents hammering co5
  concurrently. Round 2 saw these 500 deterministically; a single 500 on any of these now = CRIT-1
  NOT closed → CRITICAL, screenshot + capture the failing response body + note the doc type.
  Push HARDER than round 2 on the numbering path: sales01/ar01/purch01/ap01/appr01 should each run
  2–3 full post/approve cycles so numbering buckets get real contention.
- **CRIT-2 CLOSED:** tax01 (Tax Officer) must successfully open **ภ.พ.30 preview + PDF + .txt export**
  (was 403 in round 2). 403 now = CRIT-2 NOT closed → CRITICAL. Also confirm tax01 still CANNOT
  finalize/close a period (finalize button either absent or 403 — SoD must hold).

## HARD RULES (unchanged from round 2 — obey all)
1. co5 ONLY. Any other company's data (นาย พงศ์สันต์ / เรปทาวน์) = CRITICAL tenant-leak, screenshot + stop that thread.
2. FORBIDDEN: ยืนยัน/ปิดงวด ภ.พ.30, year-end close, payroll mutations (READ-ONLY for all), delete/edit
   EXISTING master data or any user. Creating NEW docs/products/customers = fine (co5 = playground).
3. RBAC deny = clean (button hidden / 403 / Thai deny screen). 500/crash/blank/stack-trace = finding.
4. Tool: Playwright headless from Y:\ClaudePlayground\TEAS-Project\frontend (e2e/_helpers.ts login
   pattern). Temp script frontend/swarm3-<user>.mjs — DELETE when done. No repo source edits, no git,
   no dotnet/build.
5. Output ONLY: swarm-findings/round3/<user>.md + shots/round3/<user>-*.png. Sections: Done /
   CRIT-verify (explicit: did the numbering writes 2xx? did ภ.พ.30 open? — yes/no + evidence) /
   Findings (sev table) / Denied-as-expected.
6. ~25-min timebox of UI driving then write up. Login-fail ×3 or repeated 503 → log + stop.
7. Human-paced clicks (short waits); concurrency comes from 10 agents at once, not one rapid loop.
   Capture console/pageerror per page.

## Missions (same role split as round 2; numbering-write roles push harder)
- **sales01**: 2-3× full QT→(issue)→accept→SO→DO→IV cycles (P001). PRIMARY: QT issue must 2xx.
- **ar01**: 2-3× TI(C002)→post→RC cycles. PRIMARY: TI post + RC post must 2xx. AR aging tie check.
- **purch01**: 2-3× PO(P001, odd qty)→approve→mark-sent→close. PRIMARY: PO approve must 2xx.
- **ap01**: VI(COGS)→PV(+WHT S001) from purch01's approved POs. PRIMARY: VI post + PV post 2xx.
  SoD: self-approve own PV still denied.
- **appr01**: race-approve other agents' fresh PO/PV drafts (the concurrency stressor). PRIMARY:
  PO/PV approve 2xx under race. Note inbox UX (still no working inbox = known HIGH, just confirm).
- **acct01**: TB Dr=Cr must stay balanced across 4-5 refreshes while the swarm posts. JE/GL, bank recon.
- **chief01**: all reports; re-confirm TB/BS-vs-P&L cutoff mismatch (known HIGH) still present (not fixed
  this round — just note it stands). No ยืนยัน.
- **audit01**: read-only sweep; confirm mutations still denied; note the known FE-route-gating HIGH
  (16 /new forms render) still stands (next batch, not this fix).
- **tax01**: PRIMARY CRIT-2 — open ภ.พ.30 preview + PDF + .txt (must work now). Verify July numbers
  vs baseline (sales 13,000/910, purchase drift ok as swarm posts). Confirm finalize still denied.
- **admin01**: new master data (P00x/C00x/V00x); company switcher = co5 only (else CRITICAL).

## Consolidation (Fable)
- [x] 10 round3 files → verdict: BOTH CRITs closed? (all numbering writes 2xx + ภ.พ.30 opens).
      If any CRIT reappears → new fix arc BEFORE declaring done. — closed by triage 2026-08-19
      (round3/ committed; CRIT-1/2 → fix-swarm-crit-numbering-rbac.md)
- [x] Post-swarm sanity: TB Dr=Cr, ภ.พ.30 consistent, no cross-tenant docs, prod seq-drift delta≥0
      for the buckets the swarm hit (SSH spot-check), zero 500 in pm2 log for the window. —
      OBSOLETE per triage 2026-08-19 (host retired; concerns fold into migration project)
- [x] cleanup leftover frontend/swarm3-*.mjs. Commit round3 evidence + REPORT. — closed by triage
      2026-08-19 (no swarm3-*.mjs found in repo)

# PROGRESS — VAT co5 usage drive (2026-07-19 ~13:1x)

## ✅ CRIT-1 FIX v1.22.7 LIVE + PROD-VERIFIED (2026-07-20 ~05:xx)
- v1.22.7 API deploy DEPLOY_OK: version 1.22.7, health 200, scripts UNCHANGED 73 (no new SqlScript),
  DB backup, public probes 200, prod drift = **0 buckets below max** (correct query). (First deploy
  attempt rolled back on a BUGGY belt-and-braces drift-probe SQL — negative-substring; the binary was
  fine; removed the probe, re-deployed clean. Lesson: keep deploy probes simple.)
- **CLOSING CHECK PASSED (the one skipped last time): real TI post on prod co5 → TI-0004 Posted,
  "บันทึก (Post) สำเร็จ"** — the exact TI→JE path that 500'd 3/3 in round 3 now succeeds on v1.22.7.
- REMAINING for goal: (1) swarm round 4 — concurrency proof that TI/RC/VI post stay 2xx under load
  (do AFTER quota reset; 10-agent swarm is the expensive bit). (2) THEN finding batch
  specs/fix-swarm-findings-all.md WP1-5. (3) swarm round 5. Deploy script archived
  publish/v1.22.7/deploy-api-v1227.sh.

## 🟢 CRIT-1 REAL ROOT CAUSE FOUND + FIXED (2026-07-20 ~04:xx) — commit af5ab8a
opus-debugger corrected BOTH the Sonnet's and Fable's diagnosis. Real bug = **off-by-one retry cap**
in NumberedDocumentWriter: `when (attempt < MaxAttempts ...)` left a collision on the FINAL attempt
uncaught → raw 500, and doc.number_alloc_exhausted was dead code. In an ambient tx the escape rolls
the seq bump back → never climbs → deterministic 3/3 on any bucket drifted >cap(5). PO approve (no
ambient tx) unaffected = the exact round-3 split. Fix: catch every attempt + explicit savepoint after
allocate/before SaveChanges (bump survives) + cap 5→50. Tests now drive REAL TaxInvoice/Receipt
PostAsync (drift=8 + N-parallel), RED→GREEN. Suite 148/0/0 + 909/0/8. Fable diff-reviewed the one
file personally (money, attempt 2). Prod JV NOW = delta=0, 16 rows 16 distinct (healthy, no dup) —
626 DID run (Opus's "626 didn't run" inference was wrong; my deploy proof was right).
NEXT: v1.22.7 deploy (API-only) → re-run 626 idempotent as belt-and-braces → **Fable real TI post on
prod co5 = the closing check** → swarm round 4 (TI/RC/VI post must 2xx under concurrency) → THEN the
finding batch (specs/fix-swarm-findings-all.md WP1-5) → swarm round 5.

## 🔴 CRIT-1 REOPENED (2026-07-20 ~03:xx) — v1.22.6 fix INCOMPLETE  [superseded by fix above]
Round-3 swarm verdict: **PO approve 200 3/3 (FIXED)** but **TI post 500 3/3 (STILL BROKEN)**.
Root cause of the incompleteness (Fable, confirmed from source + pm2 + prod DB):
- The v1.22.6 fix (626 reconcile + retry guard) only works for the **no-ambient-transaction** path
  (PO approve — NextAsync auto-commits, bump climbs durably, self-heals). CONFIRMED working.
- The **ambient-transaction** posts (TI/RC/VI: TaxInvoiceService.PostAsync:483 BeginTransactionAsync,
  JE NextAsync enrolled via cmd.Transaction=CurrentTransaction) STILL 500: a 23505 on the JE insert
  ABORTS the whole tx; the retry helper's next NextAsync/SaveChanges hit an aborted tx (25P02) so it
  can't recover — surfaces as raw internal_error 500 (NOT the clean doc.number_alloc_exhausted, the
  tell that retry never engaged), and the seq bump rolls back with the tx so the counter never climbs.
- Test gap that let it ship green: NumberSequenceRetryGuardTests used GlPostingService.PostManualEntryAsync
  = the auto-commit path, NOT the real ambient-tx PostTaxInvoice/Receipt/VendorInvoice path prod uses.
  → troubles-wiki lesson: test the REAL posting path, not a convenience path.
- Prod DB now: all co5 buckets delta≥0 (626 healed history). So a collision requires seq<max at the
  moment — row-lock makes seq=max collision-proof → the round-3 collisions imply transient sub-max
  drift under concurrency that the fragile ambient-tx retry turned into a hard 500 instead of healing.

### CORRECTED FIX DESIGN (Fable) — for the next dispatch
- **NumberedDocumentWriter: explicit savepoint, placed AFTER allocate() and BEFORE SaveChanges().**
  On a doc_no 23505: RollbackToSavepoint (undoes ONLY the failed insert; the NextAsync bump BEFORE the
  savepoint survives → tx restored to usable → next iteration's NextAsync climbs from the survived
  value). Do NOT rely on EF AutoSavepoints (empirically not recovering here). No-ambient-tx path keeps
  its current working behaviour.
- Verify: which posting paths open an ambient tx (TI/RC/VI/PV/expense/adjustment via GlPosting) —
  every one must get the savepoint retry. PO approve stays as-is (works).
- **TEST MUST exercise the real ambient-tx path** (PostTaxInvoiceAsync/PostReceiptAsync etc.) under a
  seeded-behind bucket AND under N-parallel, asserting 2xx + climb, not PostManualEntryAsync.
- Keep 626 reconcile. Re-run reconcile is idempotent.
- NEW finding to fold: tax01 .txt export 422 pp30_batch.missing_address (co5 profile missing registered
  house-no — data gap; either fill co5 address or surface a clear message).
- Round-3 other results: tenant isolation clean all agents; TB Dr=Cr held; SoD holds; company switcher
  co5-only; known HIGH/MED (FE route-gating, cutoff mismatch [now spans Jul/Aug/Sep payroll], AP-aging
  tie, AR negatives, api-keys, payroll admin button, users self-lock) reconfirmed for the WP batch.



## ROUND 2 — UX SWARM (17:3x, Ham สั่ง 10 accounts + Sonnet 10 ตัวรุม)
- [x] 10 accounts created on co5 (sales01/acct01/appr01/ap01/ar01/audit01/chief01/admin01/purch01/tax01,
      1 role each, pwd pattern UxSwarm-2026-*) — spec specs/uxswarm-multirole-co5.md
- [x] 10 sonnet agents dispatched concurrently (Playwright headless vs prod). Findings land in
      swarm-findings/<user>.md as each finishes.
- [x] ALL 10 RETURNED (~18:5x). Findings in swarm-findings/*.md + shots/. Zero tenant leaks (4 agents
      verified independently). TB Dr=Cr held 9/9 refreshes under load. SoD self-approve deny ✓.
- [x] **ROOT CAUSE of the 500 family CONFIRMED from pm2 log (Fable, 18:5x): Npgsql 23505 duplicate key
      on ix_journal_entries_company_id_doc_no AND ix_purchase_orders_company_id_doc_no** — doc-number
      generation is not concurrency-safe (read-max+1, no lock/sequence). Hits: QT send, TI post, RC post,
      VI post, PO approve (everything that assigns a doc no). PV approve (no numbering) raced fine 3/3.
      sales01's 4/4 deterministic failure ⇒ a sequence/counter can go permanently behind existing rows
      (does NOT self-heal). Single-user flows never collide → invisible to all previous rounds + CI.

## SWARM TRIAGE (draft, for REPORT-uxswarm-co5.md)
- CRIT-1 doc-numbering race (above) — fix arc: serialize per (company,prefix) via DB sequence or
  SELECT..FOR UPDATE counters row + retry-on-23505; footgun/money → Fable designs, Codex types
  (Claude pool 86%), Opus reviews after reset if needed.
- CRIT-2 TAX_OFFICER can't run ภ.พ.30 at all — endpoints gated tax.filing.preview, seed 530 never
  grants it (tax01 root-caused to TaxFilingEndpoints.cs). Seed grant fix + RbacAuthMapTests.
- HIGH-1 systemic FE route gating: all 16 /new forms render for unauthorized roles (audit01 full list);
  CN/DN even show create button in nav; /period-close renders for AR Clerk; silent-403 → forms look
  usable/lists look empty; the ONE correct pattern = /settings/users deny screen. One shared
  route-guard fix.
- HIGH-2 report cutoff mismatch TB/BS(as-of today) vs P&L(full month incl future-dated payroll 30/07)
  — no UI warning (chief01).
- HIGH-3 approver has no working inbox; pending-agent-approvals widget 403s and shows false
  "all clear" (appr01).
- HIGH-4 AUDITOR role missing read perms on ~10 modules → "no data" instead of "no access".
- MED batch: AP-aging no tie banner; /settings/api-keys renders past deny + React #418 (×3 agents);
  bank-recon diff no badge; payroll create button for Company Admin; review-context 403s on doc
  detail (activity log/vendor/BU); BU read perm missing for AUDITOR (~25 console 403s); EN toast.
- Litter to clean: draft QTs #11-13, TI drafts #4-5, RC #9, PO #7-8 (purch01), PVs #8-10 approved.

## Next (resume here)
1. [x] swarm evidence committed (26768c4). Fix design spec committed (81099bb):
   specs/fix-swarm-crit-numbering-rbac.md — root causes CONFIRMED (not hypothesised):
   CRIT-1 = number_sequences.current_value drift below MAX(doc_no); NextAsync itself is atomic;
   NO live code leak (1a audit done — all writes via NextAsync, no SqlScript inserts doc_no).
   CRIT-2 = TAX_OFFICER missing tax.filing.preview grant (seed 530).
2. [~] Sonnet impl DISPATCHED (~19:3x, quota reset) — task a73a107e3365c2157. Building:
   626_reconcile_number_sequences.sql (GREATEST-only UPSERT, RLS-safe) + retry-guard helper wired
   to ~10 alloc sites + 627 tax_officer grant + 4 tests (drift/parallel/reconcile/rbac). No commit.
   NEXT on return: Opus review (concurrency+SQL+RLS) → Fable diff review (626 SQL line-by-line,
   never skip) → gate → commit → release → FULL deploy (API + 2 SqlScripts, DB backup,
   applied_sql_scripts +2 not unchanged) → re-swarm-probe co5 (post/approve under concurrency = no 500).
3. HIGH batch AFTER CRITs: FE route-guard shared fix (16 /new forms + CN/DN nav + period-close) +
   report cutoff warning + approver inbox + auditor read-perms. Own spec.
4. co2/co3 untouched. Swarm doc litter on co5 (draft QT #11-13, TI #4-5, RC #9) — harmless, leave.
- v1.22.5 fix arc (F-1/F-3) shipped earlier this same day — see STATUS.


Ham: "ใช้ Claude in Chrome ลองใช้งานการซื้อ การขาย Payroll รายงาน ของบริษัท ทดสอบ VAT"
= live UX drive on prod v1.22.3, company 5 (บริษัท ทดสอบ VAT (DUMMY) จำกัด). No code changes; findings → report to Ham.

## Done (all on co5, verified by screenshot)
- [x] Purchase chain NEW docs: PO #6 → approved **07-2026-PO-0002** (3 × P001 = 3,000 + VAT 210 = 3,210)
      → VI **07-2026-VI-0003** Posted (COGS category, vendor inv VT-2026-0002, PO auto-ปิดแล้ว)
      → PV **07-2026-PV-COGS-0002** Posted (Transfer, WHT 0). Ref chain 3 docs ✓.
- [x] Sales chain NEW docs: QT **07-2026-QT-0002** (2 × P001 = 2,000 + VAT 140 = 2,140) → Accepted
      → direct-TI shortcut → TI **07-2026-TI-0003** Posted → RC **07-2026-RC-0002** Posted (VAT 0 on receipt ✓).
- [x] Payroll: 08/2026 run verified live — PIT EMP001 **7,008.33 = hand-calc EXACT** (RE-TEST (a) closed),
      SSO header "(รวมนายจ้าง)" fix live. Created 09/2026 draft (prefill 202609 = next-open ✓,
      net 115,491.66, PIT 7,008.34 — satang rounding drift, ok).

- [x] Payroll 09/2026 FULL CYCLE: created → อนุมัติ → บันทึกบัญชี (Post, 09-2026-PR-0001) → จ่ายแล้ว
      via KBANK dropdown (ธนาคารกสิกรไทย — 123-4-56789-0 prefilled ✓). RE-TEST (b) UI part done.
      PIT continuity 7,008.34, net 115,491.66.

- [x] Reports sweep (2026-07-19 ~15:1x, after quota reset):
      - ภ.พ.30 July: ขาย 13,000/910 ✓ ซื้อ 15,000/1,050 ✓ ชำระสุทธิ 0, เครดิตยกไป 140 (=1,050−910) ✓
        (ซื้อรวม 2,000/140 VI จาก RE-TEST (c) รอบก่อน — ไม่ใช่ discrepancy)
      - Dashboard "VAT 70 ขอคืนได้" เมื่อเช้า = ถูกต้อง (ตอนนั้นซื้อ 840 > ขาย 770) — sign-bug hypothesis REFUTED
      - TB ณ 19/07: Dr=Cr ✓; 1170=1,050 tie ภ.พ.30 ✓; 1130=4,280 ✓ (5,350−CN 1,070); 1120=−4,280 ✓;
        **5000 ต้นทุนขาย 5,000 (2,000 old re-test + 3,000 VI-0003 วันนี้) — RE-TEST (c) CONFIRMED**;
        5200 คง 10,000 = VI-0001 legacy pre-v1.22.1 (ตามคาด ไม่ remap ย้อนหลัง); payroll accts 0 เพราะ JE ลง 30/07
      - AR aging: สมชาย 5,350 bucket 0-30 ✓; TI-0003 เคลียร์ ✓; tie banner 1130=ทะเบียนย่อย 4,280 ✓

## DONE — drive complete. Findings for Ham (all minor):
1. AR aging: ตารางรวม 5,350 ≠ ยอดคุม 4,280 บนแบนเนอร์ — ลูกค้าที่มี net credit (C001 −1,070 จาก CN-0001)
   ไม่แสดงเป็นแถว ทำให้เลขสองที่บนจอไม่ตรงกันทั้งที่ tie จริงผ่าน (LOW-MED, display consistency)
2. PO detail: print preview ยังโชว์ "(ร่าง)" ทันทีหลังอนุมัติ (ยังไม่ refresh) (LOW)
3. QT→TI convert ทำหน่วยนับ "ชิ้น" หาย → TI พิมพ์ "หน่วย" (LOW)
4. Payroll list/row click ทำ renderer ค้าง ~30s บางครั้ง (CDP screenshot timeout ×3) — recovers เอง (perf, LOW)
2. Findings so far (minor, for report): (i) PO detail print preview still shows "(ร่าง)" right after approve
   (until refresh?), (ii) QT→TI convert drops หน่วยนับ "ชิ้น" → TI prints "หน่วย", (iii) payroll list/row click
   sometimes freezes renderer ~30s (screenshot CDP timeout ×3 this session — UI heavy, recovers alone).
3. REPORT update + STATUS + Ham summary. No commits needed unless findings fixed.

## Rules
- co5 only; co2/co3 untouchable. Docs posted here are throwaway by design (JE immutable).
- Quota: 85% crossed ~13:1x; wakeup chained to reset (~15:1x). Resume = read this file, continue In-flight.

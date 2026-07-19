# PROGRESS — VAT co5 usage drive (2026-07-19 ~13:1x)

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
1. REPORT-uxswarm-co5.md consolidation (read 10 findings files) + commit swarm evidence.
2. Fix spec CRIT-1 (Fable designs from numbering code read) → dispatch Codex → gates → deploy.
3. CRIT-2 seed fix (needs new SqlScript → DB backup SOP at deploy).
4. HIGH-1 route-guard shared fix spec (one pattern, many routes) — next batch after CRITs.
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

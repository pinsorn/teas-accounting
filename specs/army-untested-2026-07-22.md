# ARMY — untested areas, VAT + non-VAT, vision/playwright (2026-07-22)

Ham: "เทสส่วนที่เรายังไม่เคยเทสทั้งหมด ทั้ง Vat และ Non vat ด้วย army แบบเบิ้ม ๆ ด้วย vision/playwright".
Source: HANDOFF-untested-army.md. Target: **https://teas.kazaki-rio.com** (prod v1.22.10).
Companies: co5 = บริษัท ทดสอบ VAT (DUMMY) จำกัด (playground). co2/co3 REAL — untouchable.
co6 (non-VAT dummy) DOES NOT EXIST yet — blocked on Ham (super-admin only), see BLOCKERS.

Accounts (reuse, pw `UxSwarm-2026-<suffix>`): sales01/A1 acct01/A2 appr01/A3 ap01/A4 ar01/A5
audit01/A6 chief01/A7 admin01/A8 purch01/A9 tax01/B1 — all granted on co5 only (today).

## BLOCKERS (Ham, super-admin only — push sent)
- [ ] B-1: create non-VAT dummy co: ชื่อ "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด", **vatRegistered=false**,
      via /settings/companies UI (CreateAsync path — NEVER raw SQL, tax-codes footgun). Fill address
      (ภ.พ.30-txt lesson). Then grant the 10 UxSwarm users same roles on it (or tell us to drive if
      Chrome connected).
- [ ] B-2: create 1 API key on co5 (/settings/api-keys is super-admin-gated) for the MCP-surface leg.
      Scope: agent/create-draft tools. Drop the key in a local untracked file, tell us the path.

## Wave A — data prep + recon, co5 (no super-admin needed) — CAN RUN NOW
- [ ] A1 (sonnet, browser): login purch01 (fallback admin01) on prod co5.
      (a) Create FOREIGN vendor per `frontend/e2e/foreign-vendor-aws.spec.ts` field pattern
          (e.g. Amazon Web Services, Inc. — foreign flag, no Thai tax-id). Screenshot + list-verify.
      (b) READ-ONLY recon for Wave B dispatches — record what exists vs missing:
          fixed-assets master (categories/accounts? any FA yet?), bank-recon (which bank accounts,
          statement-import formats offered incl. K-Plus PDF), expense-claims prerequisites
          (categories exist? approval chain?), billing-note + WHT-cert entry points, e-Tax menu
          location, ภ.พ.36/ภ.ง.ด.54 page locations. NO mutations besides the vendor.
      Output: swarm-findings/army/A1-prep.md (+ shots/army/A1-*.png).

## Wave B — flows army, per-topic agents (post quota reset)
co5 legs (B-x, parallel — browser-only, no test-DB conflict):
- [ ] B-rc (tax01+ap01 creds): **ภ.พ.36 + ภ.ง.ด.54 reverse charge** — foreign vendor (from A1) →
      service VI reverse charge → ภ.พ.36 นำส่ง + ภ.ง.ด.54 vs HAND-CALC (agent computes expected
      VAT/WHT itself and compares). GL: ม.83/6 posting. PDF artifacts saved for Wave C.
- [ ] B-ec (ap01/appr01): **expense claims full cycle** create→approve→pay on co5 (VAT side):
      JE correctness (1170 present for VAT co), attachment, deny-paths. Check specs/expense-claims.md
      8 open items — classify unbuilt vs untested, DON'T file unbuilt as bug.
- [ ] B-fa (acct01/admin01): **fixed assets** register→activate (FA numbering)→depreciation run→
      disposal. JE + TB tie after each step.
- [ ] B-br (acct01): **bank reconciliation FULL** — statement import variants (K-Plus PDF adapter
      esp. — sample pw 06121996 per PROGRESS-cycle-b), suggest/confirm/unmatch, reconcile journal.
- [ ] B-et (tax01): **e-Tax pipeline** mock RD e-filing per etax-pipeline-mock.spec.ts flow, live.
- [ ] B-bn (ar01/sales01): **billing notes (ใบวางบิล)** flow + **WHT cert (50ทวิ direction P)** print
      → PDF artifacts for Wave C.
- [ ] B-mcp (needs B-2 key): **MCP agent surface** — create-draft tools via API key →
      pending-agent-approvals widget lights up for appr01 → approve → doc proceeds. End-to-end.
co6 legs (B2-x, needs B-1; SEQUENTIAL on co6 in this order — later steps lock periods):
- [ ] B2-nv: **non-VAT FULL drive** — master data → purchase/sales/expense cycle: NO VAT UI anywhere
      (F-B live check), VI VAT-to-cost posting, non-VAT PDF layouts (non-vat-mode-pdf.spec.ts ref),
      expense claim: JE has NO 1170, VAT folds into cost. TB ties.
- [ ] B2-pr: **ภ.ง.ด.1/1ก edge cases** — employees on co6: mid-month hire, mid-month leave, negative
      adjustment → ภ.ง.ด.1/1ก vs hand-calc. (co6 not co5 — co5 payroll stays READ-ONLY.)
- [ ] B2-ye: **year-end closing** LAST, co6 ONLY — closing entries, period locks, post-close deny.
      NEVER co2/co3/co5.

## Wave C — vision (AGY primary, Claude-vision fallback), after B artifacts exist
- [ ] C1: collect Wave B PDF/print artifacts (ภ.พ.30, ภ.พ.36, ภ.ง.ด.1/3/53/54, 50ทวิ, สปส.1-10) →
      AGY: fetch official RD/SSO form layouts (web) → field-placement compare per form → report
      per-form: match/mismatch table. AGY sandbox only (agy-out), no secrets.

## HARD RULES (all agents — unchanged from round 5)
1. Own company only (co5 or co6 per leg). Other company's data visible = CRITICAL tenant-leak,
   screenshot + stop that thread.
2. co5: FORBIDDEN ยืนยัน/ปิดงวด ภ.พ.30, year-end close, payroll mutations, delete/edit EXISTING
   master/users. co6: everything allowed EXCEPT closing before B2-ye's turn.
3. Any 500/crash/stack/blank/raw-i18n-key/wrong-number = finding: screenshot + evidence + repro.
   Hand-calc mismatches on tax forms = HIGH.
4. Playwright headless from Y:\ClaudePlayground\TEAS-Project\frontend, temp army-<leg>.mjs, DELETE
   after. No repo source edits, no git, no builds. grep troubles-wiki.md FIRST on weird errors
   (login 30s cold-cache, picker debounce, ConfirmActionDialog).
5. Output ONLY: swarm-findings/army/<leg>.md + shots/army/<leg>-*.png. Sections: Done / Evidence /
   Findings (sev + repro) / Unbuilt-vs-untested classification.
6. ~30-min timebox per leg. Human-paced clicks.

## Consolidation (Fable)
- [ ] Wave A verified → Wave B dispatched (co5 legs parallel; co6 legs sequential)
- [ ] All leg reports → dedupe → severity triage → fix arc spec → fix → re-verify (the 5-round loop)
- [ ] Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window
- [ ] Cleanup frontend/army-*.mjs

## Attempt log
- 2026-07-22 12:00 spec written; quota 81% → plan: A1 now, push Ham re B-1/B-2, wakeup at reset for Wave B.

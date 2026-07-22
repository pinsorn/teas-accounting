# HANDOFF — army test of everything UNTESTED (VAT + non-VAT, vision/playwright)

Ham's next-session plan: "เทสส่วนที่เรายังไม่เคยเทสทั้งหมด ทั้ง Vat และ Non vat ด้วย army แบบเบิ้ม ๆ
ด้วย vision/playwright". Prepared 2026-07-22 by the previous session. Prod = **v1.22.10** (all swarm
findings WP1-6 + non-VAT F-A..F-D fixed + verified; see STATUS.md).

## State
- Companies: co5 = บริษัท ทดสอบ VAT (DUMMY) — VAT playground, safe to litter. co2/co3 (Repttown ฯลฯ)
  = REAL, untouchable. **NO non-VAT dummy company exists yet** — create one first (Step 0) so non-VAT
  tests don't touch Repttown. Company-create is fixed + atomic since v1.21.6.
- 10 swarm accounts on co5 (UxSwarm-2026-*, see specs/uxswarm-round5-finding-verify.md header).
  A non-VAT dummy co will need its own user grants (or reuse super-admin to seed).
- co5 profile address filled (reg_house_no 99/9) → ภ.พ.30 .txt export works now.

## NEVER TESTED by any swarm round (the army's target list)
1. **ภ.พ.36 + ภ.ง.ด.54 (foreign vendor / reverse charge)** — pages+PDF+GL exist
   (`Pnd54FormFiller`, ม.83/6 reverse-charge in GlAccountsOptions; e2e `foreign-vendor-aws.spec.ts`
   as reference). Drive: foreign vendor → service VI (reverse charge) → ภ.พ.36 นำส่ง + ภ.ง.ด.54
   vs hand-calc. Never clicked live.
2. **Non-VAT company FULL drive** — create non-VAT dummy co → the whole purchase/sales/expense
   cycle in non-VAT mode: no VAT UI anywhere (F-B just shipped — verify live), VI VAT-to-cost
   posting, non-VAT PDF layouts (non-vat-mode-pdf.spec.ts exists as reference), TB ties.
3. **Expense claims full cycle** — create→approve→pay + the new non-VAT guard live (JE has no 1170
   in non-VAT co; VAT folds into cost). Both companies. (spec specs/expense-claims.md has 8 open
   items — check what's unbuilt vs untested.)
4. **Fixed assets** — register→activate (FA doc numbering)→depreciation runs→disposal. Never driven.
5. **Year-end closing** — closing entries, period locks (specs/year-end-closing.md 1 open). DANGER:
   only on a dummy co, never co2/co3.
6. **Bank reconciliation FULL** — statement import UI variants (KBiz done once in round 1; K-Plus
   PDF adapter never driven), suggest/confirm/unmatch, reconcile journal.
7. **ภ.ง.ด.1/1ก special cases** — done once manually (payroll round); never under army/edge cases
   (mid-month hire/leave, negative adjustments).
8. **e-Tax pipeline** — mock RD e-filing submission flow (etax-pipeline-mock.spec.ts) never live.
9. **Billing notes (ใบวางบิล) + หนังสือรับรองหักภาษี ณ ที่จ่าย certificates** — BN flow partially
   covered in walkthroughs, never swarmed; WHT cert print (direction P) never verified vs form.
10. **MCP agent surface** — create-draft tools via API key (pending-agent-approvals widget is
    agent-scoped — an MCP-created draft would light it up; never tested end-to-end).
11. **Vision checks** (Ham wants vision): PDF layout correctness vs official RD forms (ภ.พ.30,
    ภ.ง.ด.1/3/53/54, 50ทวิ, สปส.1-10) — screenshot → vision compare field placement. Never done
    systematically.

## Known-issue context for the army (don't re-file)
- Cutoff-basis labels/banners are BY DESIGN now (WP3). Approver widget is agent-draft-scoped by
  design (WP4). Auditor sees data since WP2/WP6. LOW residuals already fixed (1550e39/c1f54d8).
- Pnd50 full-suite single-test flakiness = documented shared-teas_test issue (troubles-wiki).
- Playwright footguns: customer-picker debounce race (anchor on network response), ConfirmActionDialog
  on q-accept/q-send/so-post, login 30s cold-cache timeout (troubles-wiki entry), human-paced clicks.
- Test-DB rule: ONE dotnet-test runner at a time. Swarm agents on prod = no test DB conflict.

## Suggested army shape (next session decides)
- Wave A (build data): non-VAT co creation + master data + foreign vendor on co5.
- Wave B (flows): per-topic agents (list above) on both companies concurrently.
- Wave C (vision): PDF/form-layout vision agents over the artifacts Wave B produced.
- Consolidate → fix arc → re-verify (the loop that worked 5 rounds).

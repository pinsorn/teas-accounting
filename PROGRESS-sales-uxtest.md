# PROGRESS — Sales-side UX/UI test on prod (2026-07-15 ~23:30 start)

Goal (Ham /goal): Claude Chrome test PRODUCTION **sales side** of teas.kazaki-rio.com,
UX-focused. Record findings needing fixes; if area is clean, write/refresh the Thai
manual for it. Loop/ScheduleWakeup so work continues across token reset autonomously.

## Ground rules (carried from purchase test 2026-07-14, PROGRESS-purchase-uxtest.md)
- Prod URL: https://teas.kazaki-rio.com — Chrome session assumed logged in (8h token
  since v1.21.x). Claude NEVER handles passwords; if logged out → checkpoint + wakeup,
  ask Ham to log in.
- Test company: Repttown, BU **TEST** — posting real docs allowed (BU TEST sandbox).
  co2 is load-bearing demo — NEVER post there.
- Non-VAT company (vatMode=false): no VAT row on docs is BY DESIGN; VAT advisory
  ภาษีซื้อต้องห้าม expected. Fraction-vs-percent: fixed to percent UI on VI/PV (v1.21.0);
  CHECK sales side still fraction or fixed.
- Screenshots inline-only; manual PNGs come from Playwright pipeline (frontend/manual/
  run-capture.spec.ts), not this session.
- Quota: ~83% at start. 85% = no new Claude dispatches (browser work OK). ≥95% or dying →
  PROGRESS write + ScheduleWakeup ONLY, chain 60-min wakeups until 5h window resets.

## Sales chain to test (per manual ch.4 + MCP workflow guide)
ลูกค้า (customer master) → ใบเสนอราคา (quotation) → ใบสั่งขาย (sales order) →
ใบส่งของ (delivery order) → ใบแจ้งหนี้ (invoice) → ใบกำกับภาษี (tax invoice) →
ใบวางบิล (billing note) → ใบเสร็จ (receipt) — + list pages, filters, PDF, activity log,
BU column (v1.21.3 fix regression watch), confirm dialogs, i18n, date locale.

## Plan
- [ ] Phase 0: orient — tabs_context, prod login check, company=Repttown, nav ขาย sections.
- [ ] Phase 1: customer master — create BUTEST customer (corporate+VAT validation check
      like F13), list/filter UX.
- [ ] Phase 2: quotation — create draft (BU TEST), edit draft (docDate preserve check =
      R2 Option B parity), approve (confirm dialog?), PDF, mark accepted/rejected UX.
- [ ] Phase 3: sales order from quotation (CTA chain), approve.
- [ ] Phase 4: delivery order, invoice from SO (CTA), tax invoice, posting + JE check.
- [ ] Phase 5: billing note + receipt, settlement loop closes (AR side), customer
      statement/AR aging spot-check via MCP.
- [ ] Phase 6: findings triage — file findings list; if clean → manual ch.4 refresh
      (local Playwright pipeline, dispatch worker — quota permitting).
- [ ] Final: findings report for Ham + commit.

## Findings log (S-numbers)
(none yet)

## Prod test-doc state (BU TEST, Repttown)
(none yet this round; purchase-round docs listed in PROGRESS-purchase-uxtest.md)

## Attempt log
- 2026-07-15 23:30 session start (post /clear), quota ~83%. PROGRESS created, Chrome
  tools loaded. Wakeup insurance to be scheduled.

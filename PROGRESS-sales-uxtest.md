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
- [x] Phase 0: orient — DONE. Session live (8h token), company=Repttown last-used ✓,
      nav ขาย = ใบเสนอราคา/ใบสั่งขาย/ใบส่งของ/ใบแจ้งหนี้/ใบเสร็จรับเงิน (NO billing-note
      or tax-invoice items in sidebar — verify whether hidden for non-VAT co or missing).
      Footer v1.21.2 = API version (FE-only v1.21.3 deploy) — NOT a bug.
- [x] Phase 1: customer master — DONE (validation check). BUTEST-CUST exists, reused.
      เพิ่มลูกค้า form: VAT toggle ON default, save blocked w/ inline Thai error
      "ลูกค้า VAT ต้องระบุเลขผู้เสียภาษี + รหัสสาขา (ม.86/4 #3)" ✓ F13-parity PASS.
      Form abandoned, no new customer created.
- [~] Phase 2: quotation — list page inspected (findings S3/S4 below). NEXT: click
      สร้างใบเสนอราคา (ref /quotations/new), create draft (customer BUTEST-CUST, BU TEST,
      1 line), check date-lock/BE-hint parity, save → edit draft (docDate preserve = R2
      Option B parity) → approve (confirm dialog?) → PDF → mark accepted UX.
      Note: 2 agent drafts #5/#6 pending approval exist (MCP-created) — good fixtures for
      approve-flow test WITHOUT creating new docs. Existing accepted QT-TEST-0001 (500)
      already converted to SO id 4 (convertedToSoId=4) — chain fixtures exist too.
- [ ] Phase 3: sales order from quotation (CTA chain), approve.
- [ ] Phase 4: delivery order, invoice from SO (CTA), tax invoice, posting + JE check.
- [ ] Phase 5: billing note + receipt, settlement loop closes (AR side), customer
      statement/AR aging spot-check via MCP.
- [ ] Phase 6: findings triage — file findings list; if clean → manual ch.4 refresh
      (local Playwright pipeline, dispatch worker — quota permitting).
- [ ] Final: findings report for Ham + commit.

## Findings log (S-numbers)
- S1 (UX minor): first paint of dashboard pre-hydration shows ฿0.00 stat cards + a
  "VAT สุทธิ" card (wrong for non-VAT co, disappears after hydrate) + empty nav section
  headers (ขาย/ซื้อ with no items) — flash of wrong state ~1-2s. Related to old F1.
- S2 (i18n minor): breadcrumb on /customers = "แดชบอร์ด > customers" (EN slug); but
  /quotations breadcrumb = "แดชบอร์ด > ใบเสนอราคา" (Thai) — inconsistent per page.
- S3 (i18n minor): /quotations สถานะ filter dropdown options are EN raw enum
  ("Accepted", "Draft") while table badges are Thai (ตอบรับแล้ว/ร่าง).
- S4 (BUG, backend, R8-family): GET /api/proxy/quotations list DTO has NO businessUnitId
  field (keys: quotationId, docNo, status, docDate, validUntilDate, customerName,
  totalAmount, convertedToSoId, createdViaApiKey) → หน่วยธุรกิจ column renders "—" on
  every row even when BU is set (docNo embeds TEST), and the หน่วยธุรกิจ filter on this
  page presumably can't work. v1.21.3 FE cell fix is moot here — data never arrives.
  Fix = add BusinessUnitId to quotation list projection (compare BillingNoteListItem).
  CHECK SAME GAP on: sales-orders, delivery-orders, invoices, tax-invoices, receipts
  list DTOs during their phases.
- S5 (UX minor, F2-residual): /quotations date-range filter inputs still native
  mm/dd/yyyy (CE) with no BE hint, while table shows Thai BE dates (13 ก.ค. 2569).
  (Purchase-side fix WP4.1 added BE hints to FORM date inputs only, not list filters.)
- S4 EXPANDED (Explore audit 2026-07-16, full evidence in agent report): list DTO +
  ListAsync projection missing BusinessUnitId on THREE sales-chain endpoints —
  * QuotationListItem: SalesChainDtos.cs:70-75 + QuotationChainServices.cs:289-292
  * SalesOrderListItem: SalesChainDtos.cs:86-88 + SalesOrderDeliveryServices.cs:186-188
  * DeliveryOrderListItem: SalesChainDtos.cs:100-105 + SalesOrderDeliveryServices.cs:368-371
  Entities ALL have BusinessUnitId; Detail DTOs have it; FE pages/types all expect
  businessUnitId (types.ts declares it non-optional `number | null` — JSON just lacks the
  key, TS can't catch). Invoice(=BillingNote)/TaxInvoice/Receipt list endpoints OK.
  Fix = add field to 3 DTOs + 3 projections, pattern identical to BillingNoteListItem
  (BillingNoteDtos.cs:39, BillingNoteService.cs:352). Small blast radius, backend-only,
  needs API deploy (unlike v1.21.3 FE-only).
- S6 RESOLVED (not a bug, manual-relevant): sidebar ขาย has no ใบวางบิล item because
  BillingNote IS ใบแจ้งหนี้ (/invoices, nav key billingNotes, SidebarNav.tsx:55 — no
  separate /billing-notes route; doc chain comment line 54). ใบกำกับภาษี/credit-notes/
  debit-notes are vatOnly:true (line 56,59,60; filter line 263) → hidden on non-VAT co
  (Repttown). On VAT co the ขาย section shows 6+ items. Manual must explain both.

## Prod test-doc state (BU TEST, Repttown)
(none yet this round; purchase-round docs listed in PROGRESS-purchase-uxtest.md)

## Attempt log
- 2026-07-15 23:30 session start (post /clear), quota ~83%. PROGRESS created + committed
  231c6e4, Chrome tools loaded, wakeup insurance scheduled 00:31 (+60min chain).
- 2026-07-15 ~23:59 QUOTA 95% CLIFF — paused mid-Phase-2 after list inspection.
  Browser tab 2004757528 on /quotations, logged in, no form in progress, no unsaved
  state. Findings S1–S5 logged. Reset ETA ~03:1x — wakeup at 00:31 will re-chain.

- 2026-07-16 ~03:1x wakeup: quota RESET (0%). Checkpoint committed d412de9. Browser:
  session EXPIRED → /login redirect. Ham notified (push). While blocked: Explore agent
  dispatched to audit ALL sales list DTOs for missing BusinessUnitId (S4 scope) + FE
  sidebar nav BN/TI question. Next wakeup re-checks login.

## Resume steps (next wakeup, in order)
1. Check quota state (~/.claude/quota-guard/state.json); if 5h window still >90%,
   re-schedule 60-min wakeup + stop.
2. git add PROGRESS-sales-uxtest.md; commit checkpoint (pending from cliff).
3. Chrome: tabs_context (fresh tab IDs — old ID stale), navigate
   https://teas.kazaki-rio.com/quotations — if logged out, STOP + wakeup chain +
   note for Ham to log in (never handle passwords).
4. Continue Phase 2 per plan above (create QT draft → edit → approve #5 or #6 agent
   draft to test approve dialog → PDF). Then Phases 3–5 (SO → DO → INV → TI? →
   BN? → RC; note sidebar lacks BN/TI items — investigate).
5. Keep findings in this file (S-numbers). Screenshot budget: 1 per new page type,
   zoom for details, get_page_text otherwise.
6. Phase 6 findings triage + report for Ham; manual refresh only if sales side clean
   (it is NOT — S4 backend bug already found, so manual refresh likely deferred;
   focus report + fix specs instead).

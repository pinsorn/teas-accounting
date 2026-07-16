# Fix spec — sales-side UX findings (2026-07-16, from PROGRESS-sales-uxtest.md)

Status: TEST COMPLETE 2026-07-16 — findings final (S1–S16, see REPORT-sales-uxtest.md).
Do NOT dispatch until Ham approves the fix round.

## S13 — intermittent 503 on prod writes that still APPLY (INFRA, investigate FIRST) [ ]
PUT /api/proxy/quotations/6, POST .../send, POST /api/proxy/sales-orders/5/post + one
RSC GET all returned 503 (~05:3x-06:0x 2026-07-16) yet every operation applied; FE
retry/refetch masked it. Investigate nginx/proxy timeout+buffering vs Kestrel; risk =
duplicate apply on non-idempotent POST retry. FE: surface a real error when final
retry fails; consider idempotency keys on send/post endpoints.

## S11 — no confirm dialog on QT ส่ง (issues doc number!), QT ตอบรับ, SO post, INV ออก [ ]
Only RC post has the WP3.6-style dialog. Add parity dialogs (totals + consequence
text) at minimum on number-issuing/immutable hops: QT send, SO post, INV issue.

## S12 — side panels stale after actions on sales detail pages (F10-parity) [ ]
Refs/activity panels don't refetch after send/accept/post; edit writes no activity
entry; "ส่งแล้ว → ส่งแล้ว" wording redundant (R6-parity).

## S16 — receipt-from-invoice doesn't prefill BU from upstream invoice [ ]
/receipts/new?bn=5 leaves BU "— ต้องระบุ —" though invoice has businessUnitId=3.

## S15 — converted drafts (SO-from-QT, INV-from-SO) have no แก้ไข [ ]
Add edit route/button parity with QT draft (F6-parity), or explicit design ruling.

## S9 — API allows BU-null drafts while FE requires BU [ ]
MCP-created QT #5/#6 had businessUnitId=null. Enforce server-side on create/send
(company requires BU), align MCP tool validation.

## S10 — QT detail page doesn't display หน่วยธุรกิจ [ ]
## S14 — verify invoice due-date default vs customer credit term [ ]
## S7/S5 — BE hints + date-locale on QT form dates & all list filters (merge into S5) [ ]
## S8 — customer picker modal: add inline "สร้างลูกค้าใหม่" (F4-parity) [ ]

## S4 — BU column "—" on 3 sales list pages (BUG, backend, R8-family) [ ]
Root cause (audited, evidence in PROGRESS): list DTO + ListAsync projection omit
BusinessUnitId; entity + Detail DTO + FE all have/expect it.
- [ ] QuotationListItem add `int? BusinessUnitId`
      (backend/src/Accounting.Application/Sales/SalesChainDtos.cs:70-75) + projection
      select x.BusinessUnitId (Accounting.Infrastructure/Sales/QuotationChainServices.cs:289-292)
- [ ] SalesOrderListItem same (SalesChainDtos.cs:86-88 +
      SalesOrderDeliveryServices.cs:186-188)
- [ ] DeliveryOrderListItem same (SalesChainDtos.cs:100-105 +
      SalesOrderDeliveryServices.cs:368-371)
- Pattern to copy: BillingNoteDtos.cs:39 + BillingNoteService.cs:352 (Sprint 13i C3).
- FE: NO change needed (pages already render businessUnitId; v1.21.3 cell fix live).
- Gate: integration test asserting list items carry businessUnitId when set (one per
  endpoint, follow existing BillingNote list test if present); dotnet build + test green.
- Deploy: API deploy required (not FE-only). DB backup per deploy SOP (no schema change,
  but SOP mandates backup).
- Also verify after fix: หน่วยธุรกิจ FILTER on /quotations /sales-orders /delivery-orders
  actually filters (it operates on the same missing field today).

## S1 — dashboard first-paint flash (UX minor, FE) [ ]
Pre-hydration paint shows ฿0.00 stat cards + "VAT สุทธิ" card (wrong for non-VAT co) +
empty nav section headers ~1-2s before company context loads.
- [ ] Show skeleton/shimmer (or hide cards) until company + sysInfo loaded; never render
      the vatOnly card before vatMode known; nav sections render only with their items.

## S2 — breadcrumb i18n inconsistency (i18n minor, FE) [ ]
/customers breadcrumb = "แดชบอร์ด > customers" (EN slug) while /quotations shows Thai.
- [ ] Audit breadcrumb source; map all route segments through nav i18n keys (th.json).

## S3 — list status-filter options raw EN enum (i18n minor, FE) [ ]
/quotations สถานะ dropdown shows "Accepted"/"Draft"; table badges are Thai.
- [ ] Localize status options via existing status-label map; sweep ALL sales list pages
      (and purchase pages for parity) for the same dropdown pattern.

## S5 — list date-range filters native mm/dd/yyyy, no BE hint (UX minor, FE) [ ]
WP4.1 added BE hints to form date inputs only; list filters lack them.
- [ ] Reuse the WP4.1 hint component/pattern on list filter date inputs (all list pages).

## Non-fixes (documented for manual instead)
- S6: ใบวางบิล = ใบแจ้งหนี้ (/invoices) by design; ใบกำกับภาษี/CN/DN hidden on non-VAT co
  (vatOnly flag). Manual must explain the chain + non-VAT visibility rule (ม.86/4).

## Attempt log
- 2026-07-16 ~04:0x: spec drafted from Phase 0–2 findings + Explore DTO audit. Test
  paused at prod login (session + MCP token both expired, awaiting Ham).

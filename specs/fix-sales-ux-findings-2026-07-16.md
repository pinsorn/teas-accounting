# Fix spec — sales-side UX findings (2026-07-16, from PROGRESS-sales-uxtest.md)

Status: DRAFT — test still in progress (Phases 3–5 pending prod login); S-numbers may
grow. Do NOT dispatch until Ham approves the fix round.

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

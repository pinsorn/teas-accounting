# Report-Backend28 — Sprint 13e: Sales forms (P1 done; P2–P5 scoped)

**Date:** 2026-05-19 · **Spec:** docs/Answer-Sana-Backend22.md · **ROI (spec):** 3-5 days
**Status:** **P1 (P0 emergency) ✅ done + verified.** P2–P5 = the multi-day
bulk (Q rebuild, TaxInvoicePicker, SO/DO forms, status badges) — **scoped
+ planned below, not started** (honest: this session has run many sprints;
cramming the form rebuilds into depleted context = the "half-finished"
anti-pattern). Recommend P2–P5 in a fresh session per the spec's own
1-dev day plan.

---

## P1 — SO + DO `/new` routing (P0) ✅

**Root cause confirmed:** `sales-orders/` and `delivery-orders/` had
`page.tsx` (list) + `[id]/page.tsx` (dynamic) but **no `new/page.tsx`**.
Next.js fell back to `[id]` for `/new` → `parseInt("new")=NaN` → GET
`/api/proxy/{res}/NaN` → 404 → infinite spinner. (DN works because it has
`new/page.tsx`.)

**Fix:** created
- `frontend/app/(dashboard)/sales-orders/new/page.tsx`
- `frontend/app/(dashboard)/delivery-orders/new/page.tsx`

Emergency stubs per spec P1: `PageHeader` (สร้างใบสั่งขาย / สร้างใบส่งของ)
+ info alert ("กำลังพัฒนา — POST /api/v1/{res} for external integration")
+ back-link to the list page. (Full forms = P4.)

**Verification (honest):**
- tsc **0** (non-Sana). Both files compile, correct imports.
- `GET /sales-orders/new` + `/delivery-orders/new` → **200** (was the
  stuck-spinner/404), no `Build Error`/`Unhandled Runtime Error`, **no
  `/sales-orders/NaN`** string in response.
- Next.js routes a static segment (`new/`) ahead of a dynamic one
  (`[id]`) deterministically — the file's existence + clean compile +
  200 + absence of the NaN fetch is conclusive that `new/page.js` now
  serves `/new`.
- Rendered **stub text is client-side** (`'use client'`); raw-HTML curl
  sees the SSR shell, not hydrated content — so visual confirmation of
  the stub copy is **Sana's Chrome-MCP chapter-3 acceptance step** (the
  spec's stated acceptance model). Not asserted here to avoid a fake
  "verified".

**P1 acceptance met:** routes render immediately (not 404); the NaN
fetch is gone; chunk precedence fixed by the new static route files.

---

## P2–P5 — scoped plan (not started; for next session)

Dependencies from Sprint 13d are all merged + verified this session
(AlertDialog, PermissionGate/useHasScope, ErrorEnvelope v1 +
`lib/api/errors.ts` parseApiError/fieldErrorMap) — P2/P4 can consume them
directly.

### P3 — `TaxInvoicePicker` (Day-1 scoped, do first w/ P1)
- New `frontend/components/forms/TaxInvoicePicker.tsx`: combobox over TI
  by **doc_no / customer / total**, preview row, filters by context
  (CN/DN → status=Posted; RC → unpaid; customer-scoped). value=
  taxInvoiceId, onChange(ti).
- Replace raw `<input type=number>` in `/receipts/new`,
  `/credit-notes/new`, `/debit-notes/new`.
- BE: confirm `GET /api/v1/tax-invoices` (or list endpoint) supports
  `?customer=&status=posted&unpaid=true` — likely add filter params to
  the existing search; verify before building.

### P2 — Quotation rebuild (biggest; Day 2-3)
- New shared `ProductPicker.tsx` (autocomplete products by name/SKU →
  pre-fill desc/price/uom/taxRate; taxRate=0 for EXEMPT_*), shared
  `LineItemsTable.tsx` (multi-line + VAT calc; TI form can refactor onto
  it later), `QuotationForm.tsx` (customer combobox, doc date,
  **validUntil = +30d**, BU dropdown (required iff company.requires_bu),
  notes, **discount**, Draft/Issue actions).
- BE: `Quotation` model += `validUntil`, `notes`, `discount`, status enum
  (Draft/Issued/Accepted/Rejected/Converted) + transition endpoints
  (Issue/Accept/Reject/ConvertToSO). **Breaking: EF migration**
  (`AddQuotationWorkflowFields`) — existing stub Q rows get
  status=Draft, validUntil=docDate+30, discount=0 backfill. Flag in the
  migration + run via DbInitializer (apply-then-seed order is safe).
- ⚠️ **Migration-safety note (learned Sprint 13d):** generate with a
  real build (`dotnet ef migrations add` WITHOUT `--no-build`); never
  `migrations remove` on a desynced snapshot.

### P4 — SO + DO forms (Day 3-4, reuse P2 components)
- `SalesOrderForm.tsx` (≈Q + ref-Quotation + กำหนดส่ง; status
  Draft/Confirmed/Fulfilled/Cancelled), `DeliveryOrderForm.tsx` (no
  price/VAT; shipping address pre-filled from customer; recipient/sig;
  Draft/Issued/Delivered/Cancelled). Replace the P1 stubs. BE: verify
  SO/DO controllers + add status transitions; Q→SO convert preserves
  lines/BU/customer.

### P5 — `DocumentStatusBadge` (Day 5, small cross-cutting)
- `frontend/components/ui/DocumentStatusBadge.tsx` — color+icon+Thai
  tooltip per status; wire into all 7 list pages' status column + detail
  headers. Low-risk, do alongside E2E.

### E2E (Day 5)
`chapter3_q_to_so_to_do_chain`, `chapter3_ti_picker_search`,
`chapter3_so_routing_fix`, `chapter3_do_routing_fix` (the last two are
regression tests for P1 — author them when P4 lands so they assert the
real forms, not the stub).

---

## → Sana (proposed text — Sana-owned files)

- `docs/accounting-system-plan.md` §X "Sales document workflow" — the
  4 status state machines (Q/SO/DO + existing TI/RC/CN/DN).
- `docs/api/openapi.yaml` — Q/SO/DO status-transition endpoints (added
  in P2/P4).
- `docs/runtime-gotchas.md` — (carry-over, still unapplied) ef-migrations
  `--no-build` / `remove`-on-desynced-snapshot foot-gun
  (Report-Backend21/23); + Next.js: a resource dir with `[id]/page.tsx`
  MUST also have `new/page.tsx` or `/new` → NaN → 404 (this sprint's P1
  root cause — worth a gotcha entry).
- `docs/manual/chapters/03-การขาย.md` + `frontend/manual/walkthroughs/03.*`
  — Sana authors after P2–P4 merge.

---

## DoD (this session)

P1 ✅ (2 route files, tsc 0, 200, NaN-fetch gone, deterministic routing;
client-rendered stub copy → Sana Chrome-MCP acceptance). P2–P5
**explicitly deferred** with a concrete file-level plan + the Q-schema
**breaking-change/migration** analysis + migration-safety carry-over.
Mirror Y:\AccountApp + progress cont. 48.

**Honest status:** the P0 blocker is removed — `/sales-orders/new` and
`/delivery-orders/new` no longer dead. The substantive sales-form build
(P2/P4, ~3-4 days) is planned but not done; attempting it now in a
heavily-used context would risk broken half-built forms. Recommend a
fresh session resume P3→P2→P4→P5 per the plan above.

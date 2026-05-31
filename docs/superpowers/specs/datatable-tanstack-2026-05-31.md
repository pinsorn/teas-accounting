# Spec — Unified TanStack DataTable across all list pages (2026-05-31, Ham)

## Intent (Ham)
ทุกหน้า list ตารางสไตล์เดียวกัน (shared CSS/component) · ใช้ `@tanstack/react-table` ·
มี filter ละเอียดต่อหน้า · ชื่อ/เลขเอกสารของแต่ละ row กดได้ → detail.

## Locked decisions (AskUserQuestion 2026-05-31)
1. **Scope = ~18 entity/document/master lists, NOT the aggregate report pages**
   (P&L / trial-balance / aging / sales-summary / pnd30 / outstanding-po / wht-receivable /
   missing-wht-cert / pnd36 stay as-is — they have no row→detail and need different filters).
2. **Cursor-paginated lists → fetch-all, client-side filter/sort/search.** The 7 lists on
   `useInfiniteQuery` (TaxInvoice/Receipt/CN/DN/PV/VI/WHT-cert) convert to fetch-all (`useQuery`
   returning a flat `T[]`) so the unified client-side table filters the WHOLE dataset, not one
   page. SME scale (bounded per company); revisit if a list grows large (note it).

## Data-loading model (verified in lib/queries.ts)
- **Fetch-all already (`useQuery` → T[])**: Quotations, SalesOrders, DeliveryOrders, BillingNotes,
  PurchaseOrders, Customers, Vendors, Products, BusinessUnits, ExpenseCategories, WhtTypes.
- **Cursor (`useInfiniteQuery` → CursorPage<T>)**: TaxInvoices, Receipts, AdjustmentNotes (CN/DN),
  PaymentVouchers, VendorInvoices, WhtCertificates. → convert via a `fetchAllPages` helper that
  loops `cursor` until `nextCursor == null` (limit 100/page), returns flat `T[]`.

## Foundation (main agent)
- `pnpm add @tanstack/react-table` (frontend).
- `lib/api.ts`: `fetchAllPages<T>(path, params)` — cursor loop → `T[]`.
- `components/ui/DataTable.tsx` — generic `<DataTable<T>>`:
  - Props: `data: T[]`, `columns: ColumnDef<T>[]`, `isLoading`, `getRowId`, optional `globalSearch`
    (default true), optional `initialSort`.
  - TanStack: `getCoreRowModel`, `getFilteredRowModel`, `getSortedRowModel`,
    `getPaginationRowModel` (client pagination, e.g. 25/page + page controls),
    `getFacetedRowModel`/`getFacetedUniqueValues` (for select filters).
  - **Style = the existing look**: wrapper `overflow-x-auto rounded-lg border border-base-300`,
    `table table-zebra`; sortable `<th>` (click → toggle, ▲▼ indicator); right-aligned numeric via
    `column.meta.align`. Keep `EmptyState` + a loading row (reuse `QueryStateRow` spirit).
  - **Global search box** + a **per-column filter row** (text input or faceted `<select>` driven by
    `column.meta.filter: 'text' | 'select'`). URL-persist the global filter + column filters
    (mirror current `applyListFilters` URL behavior so back-button/share survive) — use
    `useSearchParams` + `router.replace`.
  - **Clickable primary cell**: a `linkCell(getHref)` column helper renders the docNo/name as a
    `<Link>` to the row's detail. Every list's first meaningful column uses it.
  - Render `StatusBadge` in status columns via a `statusCell` helper.
  - Keep the existing **actions column** (view / edit-for-Draft) — ADD the clickable name, don't
    replace the actions.
- `components/ui/columns.tsx` (or inline helpers): `linkCell`, `statusCell`, `moneyCell`, `dateCell`.

## Pilot (main agent) — the HARD case
`tax-invoices` (cursor → fetch-all). Convert `useTaxInvoices` to fetch-all `T[]`; rewrite
`tax-invoices/page.tsx` onto `<DataTable>` with columns: docNo (linkCell → `/tax-invoices/{id}`),
status (statusCell), customer, docDate, total (moneyCell), actions. Per-column filters: status
(select), customer (text), date. Verify filter/sort/search work across the full set; tsc 0.
Show Ham before rollout.

## Rollout (subagents, after pilot approved) — one batch per group, files disjoint
Each page: define columns + filters, swap raw `<table>` → `<DataTable>`, keep create button +
actions + i18n. Group B also converts its hook to fetch-all.
- Sales docs: quotations, sales-orders, delivery-orders, invoices(BN), receipts, credit-notes,
  debit-notes (CN/DN via AdjustmentNoteScreens).
- Purchase docs: purchase-orders, payment-vouchers, vendor-invoices, wht-certificates.
- Masters: customers, vendors, settings/products (already has filters — fold into DataTable),
  settings/business-units, settings/expense-categories, settings/wht-types.

## Per-page detailed filters (Ham: "แยกละเอียด")
Beyond status/bu/party/date: add column filters natural to each list — e.g. doc-type for CN/DN,
payment status for invoices/receipts, vendor for purchase docs, type/usage/BU/active for products,
amount range where useful. Faceted selects auto-derive options from loaded rows.

## Gates
FE tsc 0 · i18n th/en parity 0/0 · per page: filter/sort/search work on full data, name cell
links to detail, actions preserved · pilot screenshot approved before rollout.

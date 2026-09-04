# WP-B — PO → Vendor Invoice: VAT rate + product type from the PO, not from a stale closure (GPT-5.6 review HIGH-02, frontend half)

Board: `PLAN-gpt56-review-2026-09-04.md` §2 row B. Blast cap: **7 files**. No commits (orchestrator
commits). Repo: Y:\ClaudePlayground\TEAS-Project. Depends on Round 1a having landed
`PurchaseOrderLineDto.TaxRate` (backend) + `PoLineDto.taxRate` (FE type) — verify both exist
before starting (`grep -n "TaxRate" backend/src/Accounting.Application/Purchase/PurchaseOrderDtos.cs`,
`grep -n "taxRate" frontend/lib/types.ts`). If absent → stop and report.

## 0. Headline
`frontend/app/(dashboard)/vendor-invoices/new/page.tsx:100-134` initialises VI rows from a linked
PO in the same effect that selects the vendor, so on the "arrive via PO CTA" path the VAT
derivation reads a `vendor` that has not loaded yet (the file's own comment at :79-83 admits it),
and the effect's dependency list omits `vendor`/`companyVatRegistered`/`stdRate` (eslint-disabled
at :133) so it never corrects itself. It also overwrites every line's `productType` with `'GOOD'`
(:131) although the PO DTO carries the real one. Real blast: zero-tax PO lines at a VAT-registered
company+vendor get 0% instead of the standard rate on the CTA path; exempt/service lines become
GOOD on every path. The backend trusts the client's `vatRate` (`VendorInvoiceService.cs:236-259`,
range check only), so the wrong value posts to input VAT.
Fix: (1) use the PO line's own `taxRate` (now in the DTO — first branch of `derivePoLineVatRate`
already prefers it, `lib/po-line-vat.ts:14`), (2) `productType: l.productType`, (3) split the
effect so row-init waits for the right vendor and re-runs when its inputs arrive.

## 1. Facts (VERIFIED 2026-09-04)
- `page.tsx:84` `const vendor = useVendor(vendorId ?? 0).data;` → `lib/queries.ts:403-409`
  `queryKey ['vendor', id]`, `enabled: id > 0`.
- `page.tsx:93-94` `companyVatRegistered` (default `true`) and `stdRate` (default `0.07`) come from
  async company config.
- `page.tsx:100-134` single effect: sets BU, (if `fromPoId`) `setVendorId`/`setVendorLabel`, then
  `setRows(prev => poDetail.lines.map(...))` with a categoryId MERGE by position (WP5 fix — keep),
  `recoverable` derived from the pick or `companyVatRegistered`, `vatRate: derivePoLineVatRate(l,
  companyVatRegistered, !!vendor?.vatRegistered, stdRate)`, `productType: 'GOOD'`. Deps
  `[poDetail, poId, fromPoId]` + `eslint-disable-next-line react-hooks/exhaustive-deps`.
- `lib/po-line-vat.ts:6-18` — `if (line.taxRate != null) return line.taxRate;` then reverse-derive
  from `taxAmount/lineAmount`, else `companyVat && vendorVat ? stdRate : 0`. Tests
  `lib/po-line-vat.test.ts` (4 cases).
- `lib/types.ts:1169-1175` `PoLineDto` has `productType: ProductTypeStr` (+ `taxRate` after 1a).
- Sibling that already does it right: `app/(dashboard)/payment-vouchers/new/page.tsx:107`
  `productType: l.productType`.
- No e2e covers the CTA path; `e2e/purchase-chain.spec.ts:104-112` creates the VI by direct API
  POST with `vatRate: 0.07`.
- Local stack: API :5080 + `next dev` :3000. Memory `stale-next-dev-no-hot-reload`: restart :3000
  before trusting a "fix didn't work"; rebuild + restart the API so the DTO change is live
  (memory `local-stack-boot-recipe`: boot recipe in that note — if the stack is DOWN, boot it per
  the recipe; the dev DB is `accounting_dev`, NOT teas_test).

## 2. Design (exact)
### 2.1 `page.tsx` — split the effect
Effect A (keep deps `[poDetail, poId, fromPoId]`): BU + vendor selection only (lines 101-109 as
they are). Remove the `setRows` block from it.
Effect B (new, right after A):
```ts
useEffect(() => {
  if (!poDetail || poDetail.purchaseOrderId !== poId) return;
  // CTA path: wait until the vendor that the PO names has actually loaded; manual "link a PO"
  // path: the user picked the vendor first, derive with what they picked.
  if (fromPoId && vendor?.vendorId !== poDetail.vendorId) return;
  setRows((prev) => poDetail.lines.map((l, i) => ({
    key: i + 1,
    categoryId: prev[i]?.categoryId ?? null,
    recoverable: prev[i]?.categoryId != null ? prev[i]!.recoverable : companyVatRegistered,
    description: l.descriptionTh,
    amount: l.lineAmount,
    vatRate: derivePoLineVatRate(l, companyVatRegistered, !!vendor?.vatRegistered, stdRate),
    productType: l.productType,
  })));
}, [poDetail, poId, fromPoId, vendor, companyVatRegistered, stdRate]);
```
No `eslint-disable`. Keep every existing comment that still applies (move the WP5 merge comment
and the F-1 recoverable comment with the block). Check the `vendor` object's id field name in
`lib/types.ts` (`vendorId`?) before writing the guard. If `useVendor` returns a shape where the id
is absent, guard on `vendorId === poDetail.vendorId && vendor != null` instead.
### 2.2 `lib/po-line-vat.ts`
Update the header comment: `taxRate` is now delivered by the DTO (Round 1a), the reverse-derivation
and the registered-fallback are legacy paths for callers without it. No logic change. Add ONE
test case: `taxRate: 0` with `taxAmount: 0` at a registered company+vendor returns `0` (the PO said
0 — an exempt line must NOT fall through to the std rate).
### 2.3 e2e — `frontend/e2e/purchase-chain.spec.ts` (extend, new `test(...)` block)
Through the real UI: create a PO for a VAT-registered vendor with two lines — one 7% GOOD, one 0%
SERVICE (or whatever exempt productType the PO form offers; if the PO form cannot set 0%, create
the PO via the API the spec already uses, with explicit `taxRate: 0` + `productType: 'SERVICE'`).
Navigate to `/vendor-invoices/new?fromPurchaseOrderId=<id>` (grep the exact query param name in
`page.tsx`). Assert, once the rows render: row 1 VAT rate select shows 7%, productType GOOD; row 2
shows 0%, productType SERVICE. Use the existing helpers in `e2e/helpers/*` for login + company;
follow `rbac-e2e-gating-gotchas` memory (scope locators; wait for the page's ready sentinel).
Do not post the VI (leave `accounting_dev` clean — delete the draft PO/VI at the end if the spec's
siblings do cleanup; otherwise use the same disposable naming they use).

## 3. Invariants
- I1 A VI initialised from a PO carries each line's PO `taxRate` and `productType` verbatim,
  regardless of async load order — T2 (e2e), T1 (unit).
- I2 A user's already-picked expense category on a row survives the (now possibly repeated)
  row-init — code review of the merge (unchanged) + T2 asserts the category select is still
  editable after rows settle.
- I3 Manual "link a PO from the dropdown" path still populates rows (guard is `fromPoId`-only) —
  T3 (manual e2e step in the same test: pick vendor first, choose the PO in the form, rows appear).
- I4 Nothing else changes: payload shape, `canSave`, the server-side computation.

## 4. Checklist
- [ ] Pre-check: DTO `TaxRate` + `PoLineDto.taxRate` present.
- [ ] 2.1 effect split.
- [ ] 2.2 comment + 1 unit case.
- [ ] `docs/api/openapi.yaml`: find the PO detail line schema (grep `taxAmount` near the
      purchase-orders paths) and add `taxRate: { type: number }` beside it (1 line; doc drift from
      Round 1a's DTO change). Cap becomes 7 files.
- [ ] 2.3 e2e (T2/T3) green against the local stack (API rebuilt, :3000 restarted).
- [ ] `pnpm exec tsc --noEmit` 0 · `pnpm vitest run lib/po-line-vat` green · `pnpm lint` (if the
      flat config from WP-E is present) shows no NEW error in `page.tsx`.

## 5. Tests
T1 unit (2.2) · T2 e2e CTA path · T3 e2e manual-link path (same test file).

## 6. Gates (worker)
As §4. You may rebuild/restart the local API (:5080) and `next dev` (:3000) — you are the only
worker using the local stack in this round. NO `dotnet test`. NO edits under `backend/`.

## 7. Out of scope
Server-side VAT derivation from tax code (backlog: backend trusts client rate) · the PO form's
tax-code handling · payment-vouchers page · WP-E lint burn-down.

## 8. Blast-radius cap
Max 6 files: `page.tsx`, `lib/po-line-vat.ts`, `lib/po-line-vat.test.ts`, `e2e/purchase-chain.spec.ts`
(+ ≤1 e2e helper if genuinely needed), this spec. Stop-and-re-spec if the effect split needs a
change to `useVendor`/queries, or the e2e needs a new backend endpoint.

## Attempt log
- 2026-09-04 Fable: spec written; dispatch in Round 1c after WP-C (DTO) and WP-E land.

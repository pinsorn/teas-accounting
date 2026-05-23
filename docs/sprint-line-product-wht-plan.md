# Sprint (next) — Line product/service typing + service-WHT category + inline product create

> Design + plan. Decided by Ham 2026-05-22. **Separate sprint** (feature is large:
> schema-adjacent + compliance + all sales line forms). Build in a focused session.
> Approach chosen: **Product-master driven** (not per-line free selectors).

## Goal (Ham's requirement)
Every line on every sales doctype (Quotation, Sales Order, Delivery Order, Tax Invoice,
Billing Note, Receipt context, Credit Note, Debit Note) must declare:
- **goods vs service** (ProductType), and
- if **service** → the **WHT category** (เช่า 5% / บริการ 3% / โฆษณา 2% …), because the
  withholding rate differs per service category.
- VAT 7%/0% — already on every line (keep).
At Receipt time: WHT is **recorded but NOT printed** on the receipt (done already —
`ReceiptService.Read.BuildPdfAsync` no longer shows WHT; only `WhtAmount` + the customer
50ทวิ number are stored). A **checkbox "มีหัก ณ ที่จ่าย?"** stays (individuals / under-threshold
= no WHT, no 50ทวิ) — the existing receipt `whtOn` toggle.

## Approach — Product-master driven (Ham)
- Goods/service + service-WHT-category are carried by the **Product master**
  (`Product.ProductType` + `Product.DefaultWhtTypeId`). To set them on a line you **pick a
  Product** via `ProductPicker`. The picker already locks the line's tax code + shows the rate
  (Sprint 13i C2).
- **ProductPicker must NOT prefill price or discount** — same product/service often sells at a
  different price each time. **Change:** in `LineItemsTable` `onSelectProduct`, drop
  `unitPrice: p.defaultUnitPrice ?? …` (and any discount default). Keep description + productId
  + productCode + productType + tax code/rate. Price/discount stay user-entered per line.
  → Implies the **Product master should not require/expose a default price** for this flow
  (review `Product` create/edit — price becomes optional/ignored for documents).
- **Enable `ProductPicker` (enableProduct) consistently** on the doctypes that need typing.
  Today: Quotation/BN/(TI just enabled). Decide the full set (likely all sales line forms).

## Inline "create new Product/Service" modal (Ham)
- From the line table, when no product matches, offer **"+ สร้างสินค้า/บริการใหม่"** → a modal
  (name TH, type goods/service, if service → WHT category/`WhtTypeId`, VAT code). On save it
  POSTs to the product master (`POST /products`) and selects the new product into the line.
- `ProductPicker` already shows a "no match — create" hint; wire it to open the modal instead
  of a dead hint. New `ProductQuickCreateModal` component + `useCreateProduct` mutation.

## Receipt WHT (mostly exists — verify, not rebuild)
- Receipt is **receipt-level WHT** today (one `WhtTypeId` for the receipt) + `useWhtBaseSuggest`
  derives the base from SERVICE lines of the applied TIs (`ReceiptService.SuggestWhtBaseAsync`,
  splits service vs goods by `Product.ProductType`). With product-typed lines this becomes
  accurate automatically. **No per-line WHT model change needed** under the product-driven
  approach — keep receipt-level WHT + the existing auto-suggest + the `whtOn` checkbox + the
  optional/deferred 50ทวิ cert (`SetWhtCertAsync`).
- Confirm: WHT not shown on the receipt PDF (DONE), but the 50ทวิ chase report
  (`/reports/wht-receivable-missing-cert`, Sprint 13k) covers recording.

## Work items (next sprint) — ✅ SHIPPED cont. 65 (FE-only; BE already complete)
1. ☑ `LineItemsTable.onSelectProduct`: stop prefilling price; keep type + tax code.
2. ☑ Enable `ProductPicker` on the agreed doctype line forms — already ON for all 4
   forms using `LineItemsTable` (Quotation/SO/BN/TI). DO cascades from SO (no manual lines);
   CN/DN synthesize lines.
3. ☑ `ProductQuickCreateModal` + `useCreateProduct`; picker "create" affordance wired
   (no-match → "+ สร้างสินค้า/บริการใหม่" → modal → POST → selected into line).
4. ☑ Product master: `ProductType` + `DefaultWhtTypeId` first-class on create/edit
   (`WhtTypeSelect` shown for service types; edit/restore fetch full detail so uom/WHT
   aren't nulled). Price already optional BE-side; not document-driving.
5. ☑ Verified — `SuggestWhtBaseAsync` filters `ProductType.Service||ExemptService`
   (ReceiptService.Read.cs:135-136); WHT off receipt PDF (cont. 64); 50ทวิ recorded.
6. ☑ WHT stays receipt-side — no WHT on TI/quotation face (Q has only a static note).

## Verify gate — ✅ PASS (cont. 65)
BE 0/0 (untouched, not rebuilt) · FE **tsc 0** / **next build 0/0** · ProductPicker on all 4
forms · quick-create modal end-to-end live-smoke **deferred to Ham/Sana on :3000** ·
receipt WHT auto-suggest verified by code-read · WHT never printed on receipt ·
`dotnet test` Domain not re-run (no BE change; was ≥89 cont. 64).

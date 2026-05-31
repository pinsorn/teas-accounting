# Spec — Product master: Purchase/Sale split + Business Unit + price auto-fill (2026-05-31, Ham)

## Intent (Ham)
สินค้า/บริการ แยก ซื้อ/ขาย (Purchase/Sale) + แยกตามหน่วยธุรกิจ. Picker เวลาเปิดให้ filter
ตาม Purchase/Sale และหน่วยธุรกิจที่เลือกในเอกสาร. เลือกสินค้า → เอาราคามาใส่ field (ยัง editable).

## Locked decisions (AskUserQuestion 2026-05-31)
1. **2 flags** `IsSaleable` + `IsPurchasable` — a product can be both (resale goods). Sale docs filter on
   `IsSaleable`, purchase docs on `IsPurchasable`. Master form: ≥1 must be ticked (disable Save / EnsureValid).
2. **BU = single nullable** `BusinessUnitId` on Product. **null = ใช้ได้ทุกหน่วย (shared)**. Picker filter:
   `BusinessUnitId == selectedBu OR BusinessUnitId IS NULL`.
3. **Single price** — keep `DefaultUnitPrice`; fill it on BOTH sale/purchase picks; field stays editable
   (reverses the old "NOT price" decision in LineItemsTable.tsx ~line 124).

## Verified context
- Every create form (sale AND purchase) already has a `businessUnitId` state + `BusinessUnitSelector`
  (Quotation/SO/DO/BillingNote/CN/DN/TI/Receipt/PO/PV/VI). Symmetric wiring — pass each form's
  `businessUnitId` to its `LineItemsTable`.
- e2e specs `.fill('ราคา/หน่วย 1')` on ad-hoc (free-text, productId=null) lines only — none pick a
  product then assert price; auto-fill-on-pick does NOT break them.

## Backend (main agent)
- `Product.cs`: `+ bool IsSaleable {get;set;}=true`, `+ bool IsPurchasable {get;set;}` (default false),
  `+ int? BusinessUnitId`. `EnsureValid()`: throw `product.no_purpose` if `!IsSaleable && !IsPurchasable`.
- `ProductConfiguration.cs`: bool props; `business_unit_id` FK→business_units (Restrict)+index;
  ck `(is_saleable OR is_purchasable)`.
- Migration `AddProductPurchaseSaleAndBusinessUnit` — `is_saleable bool NOT NULL DEFAULT true`,
  `is_purchasable bool NOT NULL DEFAULT false`, `business_unit_id int NULL`. Backfill
  `is_purchasable=true WHERE default_input_tax_code_id IS NOT NULL` (already purchase-configured).
  Apply dev + teas_test. (Kill :5080, build solution, `ef … WITH build` from W:.)
- `ProductDtos.cs`: add 3 fields to Create/Update/ListItem/Detail; validator `≥1 flag`;
  `ListAsync(includeInactive, search, purpose, businessUnitId, ct)` — `purpose` = "sale"|"purchase"|null.
- `ProductService.cs`: map fields + EnsureValid; create/update validate BU belongs to company (tenant);
  ListAsync filter: purpose→flag, BU→`(bu==id || bu==null)`. Return new fields.
- `ProductEndpoints.cs`: GET `/` add `[FromQuery] string? purpose, [FromQuery] int? businessUnitId`.
- OpenAPI delta for Sana (§9): GET `/products` +purpose/+businessUnitId; product DTOs +3 fields.

## Frontend shared contract (main agent)
- `lib/types.ts`: ProductListItem/Detail/Create/Update + `ProductPick` add `isSaleable/isPurchasable/businessUnitId`.
- `ProductSearchModal`: props `purpose?: 'sale'|'purchase'`, `businessUnitId?: number|null`; pass into
  `qs({ search, purpose, businessUnitId })`.
- `ProductPicker`: pass `purpose`/`businessUnitId` through to the modal.
- `ProductQuickCreateModal`: seed new product with `isSaleable = purpose!=='purchase'`,
  `isPurchasable = purpose==='purchase'`, `businessUnitId` from context (so it shows in the picker that made it).
- `LineItemsTable`: props `purpose?`, `businessUnitId?` → ProductPicker; on `onSelectProduct`, ALSO set
  `unitPrice: p.defaultUnitPrice ?? 0` (editable input unchanged).

## Frontend per-form wiring (subagent, after contract compiles)
Pass `purpose` + `businessUnitId` into each `<LineItemsTable>`:
- **purpose="sale"**: QuotationForm, SalesOrderForm, DeliveryOrderForm, BillingNoteForm,
  AdjustmentNoteForm (CN/DN), tax-invoices/new, receipts/new.
- **purpose="purchase"**: purchase-orders/new, payment-vouchers/new, vendor-invoices/new.
- `businessUnitId={businessUnitId}` (the form's existing state var) in every case.
- Product master form (`settings/products/page.tsx`): add ขายได้/ซื้อได้ checkboxes (≥1, disable Save if
  both off) + `BusinessUnitSelector` (optional; ว่าง=ทุกหน่วย). Send the 3 fields on create/update +
  the restore path. Add i18n th/en.

## Gates
- BE build 0/0; Api.Tests ≥ baseline (198) ×2 on teas_test; FE tsc 0; i18n th/en parity 0/0.
- Visual: open a sale form + a purchase form, pick a product → price fills + editable; picker list
  respects purpose + BU filter.

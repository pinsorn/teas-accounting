# Answer-Sana-Backend15 — Sprint 10: Quotation Chain + Product Master

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Q→SO→DO sales chain + Product master (foundational + retroactive Sprint 8.6/9 enables)
**Gate:** **3-part phased — Part A Product master → Part B Quotation chain → Part C UI + retroactive enables**
**Estimate:** ~8-10 days human-equivalent

> Sprint 10 lands the **last foundational data model** (Product master) + the sales
> document chain (Q→SO→DO). Once shipped, the Sprint 8.6 service/goods auto-split
> and Sprint 9 sales-summary-by-product features come back online (additive — no
> breaking changes).

---

## 0. Pre-spec audit (Sana — applied emergent discipline)

Before writing this spec, audited the codebase for collision/scaffold:

| Check | Result | Sprint 10 impact |
|---|---|---|
| `Product` entity in Domain | ❌ doesn't exist | Build from scratch |
| `Quotation/SalesOrder/DeliveryOrder` entities | ❌ don't exist | Build from scratch |
| `TaxInvoiceLine.ProductId` (long? nullable) | ✅ EXISTS — forward-compat scaffold from Sprint 1 | **Add FK constraint to new Product entity. No new column.** |
| `TaxInvoiceLine.ProductCode` (string? nullable) | ✅ EXISTS — denorm fallback | Keep — snapshot at POST for immutability |
| `TaxInvoiceLine.UomId` (int) + `UomText` (string) | ✅ EXISTS — string-based, no UoM master | Keep as-is. UoM master = Phase 2. |
| `ReceiptLine` / `TaxAdjustmentNoteLine` | (assume similar pattern; verify during P1) | Likely same: add FK if existing nullable Id columns; build columns if not |
| `IFinancialReportService` / similar | ✅ exist (Sprint 9) | No collision — Sprint 10 adds `IQuotationService` etc., distinct namespace |

**Outcome:** Sprint 10 is **clean additive** — no duplicate-source drift hazard surfaced by audit. ProductId scaffold pre-exists; we connect it to a real Product entity. Quotation chain is greenfield (no scaffold to collide with).

---

## Phasing

| Part | Theme | Estimate |
|---|---|---|
| **A** | Product master (entity + FK + retroactive enables for 8.6 + 9) | ~3-4 days |
| **B** | Quotation → SalesOrder → DeliveryOrder chain | ~4-5 days |
| **C** | UI + i18n + tests + wrap | ~1-2 days |

Gate between each. Don't bundle.

---

# PART A — Product master (foundational)

## A1. `master.products` entity

```
master.products
  product_id           BIGINT IDENTITY PK
  company_id           INT NN              -- multi-tenant, RLS
  product_code         VARCHAR(50) NN      -- SKU/code unique per company
  name_th              VARCHAR(255) NN
  name_en              VARCHAR(255) NULL

  -- Type taxonomy (drives WHT base aggregation, exempt handling)
  product_type         VARCHAR(20) NN      -- 'GOOD' | 'SERVICE' | 'EXEMPT_GOOD' | 'EXEMPT_SERVICE'
                                            -- EXEMPT_* = quick-tag for ม.81 items;
                                            -- actual exempt status driven by tax_code linkage

  -- Defaults (auto-fill on TI/RC/CN/DN line)
  default_uom_text     VARCHAR(50) NULL    -- e.g. "ชิ้น", "ชม.", "ครั้ง"
  default_unit_price   NUMERIC(19,4) NULL
  default_output_tax_code_id  INT NULL FK tax.tax_codes  -- for sale
  default_input_tax_code_id   INT NULL FK tax.tax_codes  -- for purchase (future use)
  default_wht_type_id  INT NULL FK tax.wht_types         -- for SERVICE products that attract WHT

  -- Optional
  description_th       VARCHAR(1000) NULL
  is_active            BOOL NN DEFAULT true
  notes                VARCHAR(500) NULL

  -- Audit
  created_at, created_by, updated_at, updated_by   -- IAuditable
  version              INT NN DEFAULT 1             -- IConcurrencyVersioned

  UNIQUE(company_id, product_code)
  ITenantOwned (CompanyId)
```

CHECK constraint: `product_type IN ('GOOD', 'SERVICE', 'EXEMPT_GOOD', 'EXEMPT_SERVICE')`

Validation:
- `product_code` unique per company, case-insensitive comparison
- `default_wht_type_id` allowed only when `product_type IN ('SERVICE', 'EXEMPT_SERVICE')` (goods don't attract service WHT)
- `default_output_tax_code_id`: if linked tax_code has `IsExempt=true` → suggest `product_type=EXEMPT_*` (UI warning, not blocker)

## A2. Connect `TaxInvoiceLine.ProductId` → `Product.ProductId`

**Migration `AddProductMasterAndFk`:**
1. Create `master.products` table
2. Add FK: `ALTER tax_invoice_lines ADD CONSTRAINT fk_til_product FOREIGN KEY (product_id) REFERENCES master.products(product_id)`
3. Existing rows: `product_id` is already nullable, all current rows have it NULL or unrelated long — FK accepts NULL or valid IDs
4. Same FK on `receipt_application_lines` if structure mirrors (verify during impl)
5. Same FK on `tax_adjustment_note_lines`

**Backward compat:** existing rows with `ProductId=NULL` continue to work (use `ProductCode` string fallback). No data migration required.

## A3. ProductCode snapshot at POST (immutability)

When TI/RC/CN/DN line is POSTED:
- If `product_id` is set → snapshot `Product.product_code` into the line's `product_code` field (denorm copy)
- This keeps POSTED documents immutable even if Product master row is edited/deactivated later
- Mirror of Sprint 5.5 Vendor snapshot pattern (vendor_name/address/tax_id snapshot on VI POST)

## A4. Retroactive enable #1 — Sprint 8.6 service/goods WHT-base auto-split

Sprint 8.6 R-B1a degraded `GET /receipts/wht-base-suggest` to "manual base entry"
because Product master didn't exist. Now it does → extend additively:

```
GET /receipts/wht-base-suggest?taxInvoiceIds=...&customerId=...

Response (additive — old fields stay):
{
  "total_subtotal_ex_vat": 10000,              // already there
  "service_subtotal": 4000,                    // NEW
  "goods_subtotal": 6000,                      // NEW
  "suggested_wht_type_id": 12,
  "suggested_wht_rate": 0.03,
  "suggested_wht_base": 4000,                  // changed: now defaults to service_subtotal instead of total
  "suggested_wht_amount": 120,
  "note": "WHT คำนวณจากส่วนบริการ (4,000) เท่านั้น × 3% = 120"
}
```

**Logic:**
- Sum applied TI lines' `subtotal` grouped by `Product.ProductType`
- SERVICE + EXEMPT_SERVICE → `service_subtotal`
- GOOD + EXEMPT_GOOD → `goods_subtotal`
- Lines with `product_id IS NULL` (legacy, no product link) → classified by manual UX hint or treated as service (conservative for WHT)
- `suggested_wht_base` defaults to `service_subtotal` (not total)
- User still override-able

**Test:** Receipt POST with mixed-product TIs → wht-base-suggest returns correct split → assert.

## A5. Retroactive enable #2 — Line tax_code auto-pickup

When creating TI/RC/CN/DN line, if user selects a `product_id` → auto-pre-fill:
- `tax_code_id ← product.default_output_tax_code_id`
- `unit_price ← product.default_unit_price` (only if line `unit_price` blank)
- `description_th ← product.name_th` (only if line description blank)
- `uom_text ← product.default_uom_text`

User can override all of these per line.

## A6. Retroactive enable #3 — Sales summary group_by=product

Sprint 9 R-Q2 removed `product` from `group_by` enum. Now restore:

```
GET /reports/sales-summary?from=...&to=...&group_by=customer|product|business_unit
```

Add `product` back. Group joins TI line's `product_id` → `master.products`. Lines with `product_id=NULL` shown as "(no product)" group.

## A7. `IProductService` (master CRUD)

```csharp
Task<long> CreateAsync(CreateProductRequest req, CancellationToken ct);
Task UpdateAsync(long id, UpdateProductRequest req, CancellationToken ct);  // edits don't propagate to posted docs (snapshot)
Task DeactivateAsync(long id, CancellationToken ct);                          // soft; refuse if any draft line references
Task<IReadOnlyList<ProductListItem>> ListAsync(bool includeInactive, string? search, CancellationToken ct);
Task<ProductDetail?> GetAsync(long id, CancellationToken ct);
```

Permissions:
- `master.product.manage` (CRUD) → COMPANY_ADMIN + CHIEF_ACCOUNTANT + AR_CLERK
- `master.product.read` → all roles

Seed: optional minimal demo set for new companies (a few SERVICE + GOOD examples).

## A8. Part A — Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (Product validation, type+wht_type compatibility check) |
| Api tests | +N (CRUD, FK constraint enforced, line auto-pickup, wht-base-suggest extension, sales-summary product group) |
| EF migration | `AddProductMasterAndFk` clean, no drift |
| tsc / next build | 0 / 0 |
| Playwright | 25 existing + 1 (products-crud spec) = 26/26 |
| Retroactive verification | Sprint 8.6 wht-base-suggest now returns service_subtotal; Sprint 9 sales-summary accepts group_by=product |

---

# PART B — Quotation chain (Q→SO→DO)

## B1. Three new entities (mirror TI shape)

### Quotation (สำหรับเสนอราคา — non-fiscal)

```
sales.quotations
  quotation_id          BIGINT IDENTITY PK
  company_id, branch_id (RLS)
  doc_no                VARCHAR  NULL   -- Q-NNNN, allocated on Send (not POST)
  status                ENUM     Draft | Sent | Accepted | Rejected | Expired | Cancelled
  doc_date              DATE NN          -- doc date (today, Asia/Bangkok)
  valid_until_date      DATE NN          -- expiry

  customer_id, customer snapshot (name, address, tax_id, type)
  business_unit_id      INT NULL FK master.business_units  -- Sprint 8 BU

  currency_code, exchange_rate
  subtotal_amount, vat_amount, total_amount  NUMERIC(19,4)

  notes                 VARCHAR NULL
  internal_notes        VARCHAR NULL    -- not shown on PDF

  -- Optional WHT info display (Sprint 8.6 + Q chain link)
  show_wht_note         BOOL NN DEFAULT  -- auto from customer.customer_type: CORPORATE=true, INDIVIDUAL=false
  -- WHT calc on-the-fly at PDF gen time; not stored

  -- Conversion tracking
  converted_to_so_id    BIGINT NULL FK sales.sales_orders   -- set when Accepted → SO created
  rejected_reason       VARCHAR NULL                          -- captured on Rejected
  cancelled_reason      VARCHAR NULL

  sent_at, accepted_at, expired_at  TIMESTAMPTZ NULL
  created_at/by, updated_at/by, version

sales.quotation_lines
  -- mirror TI line shape (line_no, product_id, product_code, description_th,
  -- quantity, uom_text, unit_price, discount_percent, discount_amount,
  -- line_amount, tax_code_id, tax_rate, tax_amount, total_amount)
```

### SalesOrder (สั่งซื้อจากลูกค้า — internal commitment)

```
sales.sales_orders
  sales_order_id        BIGINT IDENTITY PK
  doc_no                VARCHAR NULL    -- SO-NNNN, allocated on POST
  status                ENUM Draft | Posted | Closed | Cancelled
  doc_date, expected_delivery_date  DATE
  customer + snapshot, BU
  quotation_id          BIGINT NULL FK sales.quotations  -- optional source

  -- amount snapshot from Quotation if linked
  currency, exchange_rate, subtotal, vat, total

  lines (mirror Quotation line shape)
  notes
  posted_at/by, closed_at, cancelled_reason

sales.sales_order_lines (mirror)
```

### DeliveryOrder (ใบส่งของ — physical/digital delivery confirmation)

```
sales.delivery_orders
  delivery_order_id     BIGINT IDENTITY PK
  doc_no                VARCHAR NULL    -- DO-NNNN
  status                ENUM Draft | Posted | Cancelled
  doc_date, delivered_at  DATE/TIMESTAMPTZ
  customer + snapshot, BU
  sales_order_id        BIGINT NULL FK sales.sales_orders

  -- if delivery is also the tax-invoice trigger (Thai "ใบส่งของ-ใบกำกับภาษี" combined)
  is_combined_with_ti   BOOL NN DEFAULT false
  tax_invoice_id        BIGINT NULL FK sales.tax_invoices  -- linked TI if combined or generated from this DO

  lines (mirror SO line shape)
  notes, posted_at/by
```

### Numbering

All allocated on POST (gapless per CLAUDE.md §4.5):
- Quotation: `MM-YYYY-Q-NNNN`, sub-prefix can be BU code (Q-LAB-NNNN if BU set)
- SalesOrder: `MM-YYYY-SO-NNNN` + BU sub-prefix
- DeliveryOrder: `MM-YYYY-DO-NNNN` + BU sub-prefix

Sequence per `(company, doc_type, sub_prefix, year_month)` via existing `number_sequences` table.

## B2. Conversion services

### Quotation → SalesOrder

```csharp
// IQuotationService
Task<long> ConvertToSalesOrderAsync(long quotationId, CancellationToken ct);
// Validates: quotation status must be Accepted; creates SO with snapshotted lines + customer + BU + amounts;
// sets quotation.converted_to_so_id; returns new SO id.
```

### SalesOrder → DeliveryOrder

```csharp
// ISalesOrderService
Task<long> CreateDeliveryOrderAsync(long salesOrderId, CreateDeliveryOrderRequest req, CancellationToken ct);
// req specifies which lines + quantities (may be partial — for split deliveries);
// creates DO referencing SO; mark SO Closed when all SO lines delivered.
```

### DeliveryOrder → TaxInvoice

Two patterns Thai businesses use:

**Pattern X — Combined "ใบส่งของ-ใบกำกับภาษี":** DO and TI on same document (common for SME).
- `delivery_order.is_combined_with_ti=true` → on DO POST, system also creates linked TI
- DO PDF shows both "ใบส่งของ" + "ใบกำกับภาษี" labels
- `delivery_order.tax_invoice_id` set to the auto-created TI

**Pattern Y — Separate DO then TI:** DO first, TI generated later (common when delivery date ≠ invoice date).
- `delivery_order.is_combined_with_ti=false`
- Separate manual step: from DO detail → "สร้างใบกำกับภาษี" button → creates TI with snapshot

Both supported. Default = Pattern X (faster for SME).

## B3. BU cascade

When source doc has BU:
- Quotation → SO: SO inherits Quotation's BU
- SO → DO: DO inherits SO's BU
- DO → TI: TI inherits DO's BU
- Customer override at any step (user can change if needed)

## B4. Optional WHT informational note on Quotation PDF

When `show_wht_note=true` AND customer is CORPORATE AND any line has SERVICE product:
- PDF footer adds informational section:
  ```
  หมายเหตุ (สำหรับลูกค้านิติบุคคล):
    หัก ภาษี ณ ที่จ่าย 3% ของส่วนบริการ: 120
    ยอดสุทธิที่ชำระ: 10,580
  ```
- Computed from product line types (SERVICE lines × default WHT rate from product.default_wht_type)
- NOT stored anywhere — pure PDF presentation
- Per Ham consult: helps customer plan cash flow; safe default for B2B

## B5. Part B — Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (Quotation status machine, conversion validation) |
| Api tests | +N (CRUD per entity, Q→SO conversion, SO→DO with partial qty, DO→TI both patterns, BU cascade) |
| EF migration | `AddQuotationChain` clean |
| tsc / next build | 0 / 0 |
| Playwright | 26 + 1 (quotation-chain-flow spec) = 27/27 |
| Numbering | Q/SO/DO sequences allocated on POST + sub-prefix per BU + no gap |

---

# PART C — UI + i18n + tests + wrap

## C1. UI scope

### New pages
- `/settings/products` — Product master CRUD (list, create, edit, deactivate)
- `/quotations` — list (filter status, customer, BU, date range)
- `/quotations/new` — form
- `/quotations/{id}` — detail (PDF download, status actions, "Convert to SO" button when Accepted)
- `/sales-orders` — list + filter
- `/sales-orders/new` — form (optional: pick Quotation to clone from)
- `/sales-orders/{id}` — detail ("Create DO" button)
- `/delivery-orders` — list + filter
- `/delivery-orders/new` — form (optional: pick SO to clone from)
- `/delivery-orders/{id}` — detail ("Create TI" button when not combined; or shows linked TI if combined)

### Modified pages
- `/tax-invoices/new` — line: when picking `product_id` → auto-pre-fill (per A5)
- `/receipts/new` — same: line product → tax_code default; WHT base now auto-suggests service_subtotal (A4)
- `/reports/sales-summary` — add `product` to group_by chip

### Sidebar nav update
Add "ขาย" section: Quotations, Sales Orders, Delivery Orders (under existing Tax Invoices area)

## C2. i18n (TH primary, EN parity)

Keys: `product.*`, `quotation.*`, `salesOrder.*`, `deliveryOrder.*` per established codebase namespace convention.

## C3. PDF templates

- Quotation PDF (with optional WHT note section)
- SalesOrder PDF (internal — not legal)
- DeliveryOrder PDF (both modes: standalone, or combined-with-TI when is_combined_with_ti=true)

All use Sprint 8.5 PDF-branching foundation (VatMode toggle for label conventions).

## C4. Tests

### Unit
- Product validation (code unique, type+wht_type compat)
- Quotation status machine (Draft → Sent → Accepted/Rejected/Expired)
- Conversion logic (Q→SO line snapshot, SO→DO partial qty, DO→TI both patterns)

### Integration
- CRUD per entity + permission gate
- Q→SO conversion creates correct SO
- SO→DO partial: 2 DOs from 1 SO with split qty → SO closed when total delivered ≥ SO qty
- DO→TI Pattern X: combined DO POST creates linked TI
- DO→TI Pattern Y: separate flow creates TI from DO snapshot
- BU cascade preserved
- Retroactive verification:
  - wht-base-suggest now returns service_subtotal/goods_subtotal split
  - sales-summary group_by=product works
  - Line tax_code auto-pickup from Product

### e2e (Playwright)
- `products-crud.spec.ts` (Part A)
- `quotation-chain-flow.spec.ts` (Part B) — full Q→SO→DO→TI happy path
- Total 25 prior + 2 new = **27/27**

## C5. Wrap

1. All gates green at sprint close
2. Mirror sync `Y:\AccountApp`
3. plan.md §23.3 — strike Sprint 10
4. `Report-Backend15.md`

---

## Scope cuts — explicitly OUT

- ❌ **UoM master** (master.uoms) — string-based UoM stays; Phase 2 if customer asks
- ❌ **Product variants / serialization** — Phase 2 (inventory module territory)
- ❌ **Inventory tracking** (stock on hand) — explicitly out per Phase 1 §8 "Out of Scope"
- ❌ **Pricing tiers / customer-specific pricing** — Phase 2
- ❌ **Bulk import of Products** — manual CRUD only this sprint
- ❌ **Quotation versioning** (Q-001-v2) — Cancel-and-recreate this sprint; versioning Phase 2
- ❌ **Multi-currency line items within single Quotation** — single currency per Q (existing multi-currency at header level only)
- ❌ **Vendor-side products** (for VI/PV) — only sales-side this sprint; if vendor-side wanted Phase 2

If any surface as blocker → escalate per §8.

---

## Cross-sprint dependencies

- Consumes Sprint 8 BU (cascade Q→SO→DO→TI)
- Consumes Sprint 8.6 wht_types (Product.default_wht_type for SERVICE)
- Retroactively enables:
  - Sprint 8.6 wht-base-suggest service split (auto-suggestion comes back online)
  - Sprint 9 sales-summary group_by=product

## DoD (3 Parts × items)

**Part A (10 items):**
1. master.products entity + EF config + migration
2. FK constraint on TaxInvoiceLine.ProductId (and Receipt/CN/DN line counterparts)
3. ProductCode snapshot at POST (immutability pattern)
4. wht-base-suggest extension with service/goods split (A4)
5. Line tax_code/price/desc/uom auto-pickup from Product (A5)
6. sales-summary group_by=product re-enabled (A6)
7. IProductService + endpoints + perms
8. /settings/products UI
9. Product tests (unit + integration)
10. Part A gates green

**Part B (10 items):**
1. Quotation entity + migration
2. SalesOrder entity + migration
3. DeliveryOrder entity + migration
4. Numbering: Q/SO/DO sequences with BU sub-prefix
5. IQuotationService + conversion to SO
6. ISalesOrderService + conversion to DO (with partial qty support)
7. IDeliveryOrderService + conversion to TI (both Pattern X + Y)
8. BU cascade preserved across chain
9. PDF templates (Quotation with optional WHT note, SO, DO standalone, DO+TI combined)
10. Part B gates green

**Part C (5 items):**
1. All UI pages (per C1)
2. i18n th + en
3. Tests (unit + integration + 2 e2e per C4)
4. plan.md §23.3 strike Sprint 10
5. Report-Backend15.md

**Total: 25 DoD items.**

---

**Build it. Part A → gate → Part B → gate → Part C → gate → wrap. Don't bundle Parts. Report back via Report-Backend15.**

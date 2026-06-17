# Sales

งานขายตลอดสายเอกสาร: ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → ใบกำกับภาษี → ใบเสร็จ, ใบลด/เพิ่มหนี้, ใบแจ้งหนี้ และการอ้างอิงข้ามเอกสาร.

The full sales document chain plus credit/debit notes, billing notes, cross-references, the activity rail, and print tracking. All line-bearing create bodies take a `lines` array of line inputs (`description`, `quantity`, `unitPrice`, tax code, etc. — see `openapi.yaml`). Posted fiscal documents are immutable (§4.2); document numbers are assigned on issue/post only (§4.3).

## Quotations
Gated by `sales.quotation.manage`.
- `POST /quotations` — create draft. Body: `docDate`, `validUntilDate`, `customerId` (required), `businessUnitId?`, `currencyCode`, `exchangeRate`, `notes?`, `internalNotes?`, `lines[]` (`CreateQuotationRequest`). → `201` `{ quotation_id }`.
- `PUT /quotations/{id}` — edit (draft only). → `204`.
- `DELETE /quotations/{id}` — hard-delete (draft only). → `204`.
- `POST /quotations/{id}/send` — mark sent. → `204`.
- `POST /quotations/{id}/accept` — mark accepted. → `204`.
- `POST /quotations/{id}/reject` — reject. Body: `{ reason }`. → `204`.
- `POST /quotations/{id}/cancel` — cancel. Body: `{ reason }`. → `204`.
- `POST /quotations/{id}/convert-to-so` — create a sales order from the quotation. → `200` `{ sales_order_id }`.
- `GET /quotations` — list. Query: `status?`. → `200`.
- `GET /quotations/{id}` — detail. → `200` / `404`.
- `GET /quotations/{id}/pdf` — PDF. Query: `copy?` (bool). → `application/pdf`.

## Sales Orders
Gated by `sales.sales_order.manage`.
- `POST /sales-orders` — create draft. Body: `docDate`, `expectedDeliveryDate?`, `customerId` (required), `businessUnitId?`, `currencyCode`, `exchangeRate`, `notes?`, `fromQuotationId?`, `lines[]` (`CreateSalesOrderRequest`). → `201` `{ sales_order_id }`.
- `POST /sales-orders/{id}/post` — post. → `204`.
- `POST /sales-orders/{id}/delivery-orders` — create a delivery order from this SO. Body: `CreateDeliveryOrderRequest`. → `200` `{ delivery_order_id }`.
- `GET /sales-orders` — list. Query: `status?`. → `200`.
- `GET /sales-orders/{id}` — detail. → `200` / `404`.
- `GET /sales-orders/{id}/pdf` — PDF. Query: `copy?`. → `application/pdf`.

## Delivery Orders
Gated by `sales.delivery_order.manage`. 4-state machine (draft → issued → delivered).
- `POST /delivery-orders` — create draft. Body: `docDate`, `customerId` (required), `businessUnitId?`, `isCombinedWithTi` (bool), `notes?`, `fromSalesOrderId?`, `lines[]` (`CreateDeliveryOrderRequest`). → `201` `{ delivery_order_id }`.
- `POST /delivery-orders/{id}/issue` — issue. → `204`.
- `POST /delivery-orders/{id}/mark-delivered` — mark delivered. → `204`.
- `POST /delivery-orders/{id}/create-ti` — create a Tax Invoice from the DO. → `200` `{ tax_invoice_id }`.
- `POST /delivery-orders/{id}/create-invoice` — create a billing note (ใบแจ้งหนี้) from the DO. → `200` `{ billing_note_id }`.
- `GET /delivery-orders` — list. Query: `status?`. → `200`.
- `GET /delivery-orders/{id}` — detail. → `200` / `404`.
- `GET /delivery-orders/{id}/pdf` — PDF. Query: `copy?`. → `application/pdf`.

## Tax Invoices (ใบกำกับภาษี)
Compliance core (ม.86/4). Posted = immutable; number assigned on post.
- `POST /tax-invoices` — create draft. **Auth:** `sales.tax_invoice.create`. Body: `docDate`, `customerId` (required), `isTaxInclusive` (bool), `currencyCode`, `exchangeRate`, `notes?`, `paymentTerms?`, `dueDate?`, `lines[]`, `businessUnitId?`, `quotationId?` (`CreateTaxInvoiceRequest`). → `201`.
- `POST /tax-invoices/{id}/post` — post (assigns the sequential number, triggers e-Tax if configured). **Auth:** `sales.tax_invoice.post`. → `200`.
- `GET /tax-invoices` — list. **Auth:** `sales.tax_invoice.read`. → `200`.
- `GET /tax-invoices/{id}` — detail. **Auth:** `sales.tax_invoice.read`. → `200` / `404`.
- `GET /tax-invoices/{id}/xml` — signed e-Tax XML. **Auth:** `sales.tax_invoice.read`. → XML.
- `GET /tax-invoices/{id}/pdf` — PDF. **Auth:** `sales.tax_invoice.read`. → `application/pdf`.
- `POST /tax-invoices/{id}/resend` — re-send the e-Tax email. **Auth:** `sales.tax_invoice.post`.

## Receipts (ใบเสร็จรับเงิน)
- `POST /receipts` — create. **Auth:** `sales.receipt.create`. Body: `docDate`, `customerId` (required), `paymentMethod`, `chequeNo?`, `chequeDate?`, `bankAccountId?`, `currencyCode`, `exchangeRate`, `notes?`, `applications[]` (TI allocations), `businessUnitId?`, WHT fields (`whtAmount`, `whtTypeId?`, `customerWhtCertNo?`, `customerWhtCertDate?`, or multi-category `whtLines[]`), and standalone `lines[]` for non-VAT cash bills (`CreateReceiptRequest`). → `201`.
- `POST /receipts/{id}/post` — post. **Auth:** `sales.receipt.post`. → `200`/`204`.
- `POST /receipts/{id}/wht-cert` — issue a WHT certificate (หนังสือรับรองการหัก ณ ที่จ่าย) for the receipt. **Auth:** `sales.receipt.create`.
- `POST /receipts/wht-base-suggest` — suggest WHT base amounts for the form. **Auth:** `sales.receipt.read`.
- `GET /receipts` — list. **Auth:** `sales.receipt.read`. → `200`.
- `GET /receipts/{id}` — detail. **Auth:** `sales.receipt.read`. → `200` / `404`.
- `GET /receipts/{id}/pdf` — PDF. **Auth:** `sales.receipt.read`. → `application/pdf`.

## Credit / Debit Notes (ใบลดหนี้ / ใบเพิ่มหนี้)
Routed under `/tax-adjustment-notes` (one surface for both note types). Auth uses a custom assertion: any of `sales.credit_note.*` / `sales.debit_note.*` (create/post/read) or super-admin.
- `POST /tax-adjustment-notes` — create draft. **Auth:** `sales.credit_note.create` OR `sales.debit_note.create`. Body: `noteType`, `docDate`, `originalTaxInvoiceId` (required), `reasonCode`, `reason`, `adjustmentSubtotal` (decimal), `taxRate`, `currencyCode`, `exchangeRate`, `notes?`, `businessUnitId?` (`CreateTaxAdjustmentNoteRequest`). → `201` `{ note_id }`.
- `POST /tax-adjustment-notes/{id}/post` — post. **Auth:** `sales.credit_note.post` OR `sales.debit_note.post`. → `200`.
- `GET /tax-adjustment-notes` — list. **Auth:** any CN/DN read/create/post perm. Query: `noteType?`, `cursor?`, `limit?`, `businessUnitId?`, `includeUnspecified?`. → `200`.
- `GET /tax-adjustment-notes/{id}` — detail. **Auth:** any CN/DN read perm. → `200` / `404`.
- `GET /tax-adjustment-notes/{id}/pdf` — PDF. **Auth:** any CN/DN read perm. Query: `copy?`. → `application/pdf`.

## Billing Notes (ใบแจ้งหนี้ / ใบวางบิล)
Write gated by `sales.billing_note.manage`, read by `sales.billing_note.read`.
- `POST /billing-notes` — create. Body: `docDate`, `dueDate`, `customerId` (required), `businessUnitId?`, `quotationId?`, `taxInvoiceIds?[]` (group N TIs), `currencyCode`, `exchangeRate`, `notes?`, `internalNotes?`, `lines[]` (`CreateBillingNoteRequest`). → `201`.
- `PUT /billing-notes/{id}` — edit. → `204`.
- `DELETE /billing-notes/{id}` — delete. → `204`.
- `POST /billing-notes/{id}/issue` — issue. → `204`.
- `POST /billing-notes/{id}/cancel` — cancel. → `204`.
- `POST /billing-notes/{id}/mark-settled` — mark settled. → `204`.
- `POST /billing-notes/{id}/create-tax-invoice` — create a TI from the billing note. → `200`.
- `GET /billing-notes` — list. **Auth:** `sales.billing_note.read`. → `200`.
- `GET /billing-notes/{id}` — detail. **Auth:** `sales.billing_note.read`. → `200` / `404`.
- `GET /billing-notes/{id}/pdf` — PDF. **Auth:** `sales.billing_note.read`. → `application/pdf`.

## Document Cross-References
Read-only resolvers for the FE cross-reference hooks. Tenant-scoped.
- `GET /document-cross-refs/tax-invoice/{id}` — refs for a TI. **Auth:** `sales.tax_invoice.read`. → `200` / `404`.
- `GET /document-cross-refs/receipt/{id}` — refs for a receipt. **Auth:** `sales.receipt.create`. → `200` / `404`.
- `GET /document-cross-refs/adjustment-note/{id}` — refs for a CN/DN. **Auth:** `sales.credit_note.create`. → `200` / `404`.
- `GET /documents/chain` — full sales chain (Q→SO→DO→Invoice→TI→RC + CN/DN). **Auth:** Authenticated. Query: `type`, `id`. → `200` / `404`.
- `GET /documents/purchase-chain` — full purchase chain (PO→VI→PV→WHT). **Auth:** Authenticated. Query: `type`, `id`. → `200` / `404`.

## Activity (audit rail)
Read-only chronological audit trail per document (`audit.activity_log`). All share `report.audit.read`.

`GET /{docType}/{id}/activity` — **Auth:** `report.audit.read`. → `200` list of activity entries.

`docType` ∈ `quotations`, `sales-orders`, `delivery-orders`, `tax-invoices`, `receipts`, `credit-notes`, `debit-notes`, `billing-notes`, `purchase-orders`, `vendor-invoices`, `payment-vouchers`, `wht-certificates`, `payroll-runs`.

## Print Tracking
Records original/copy printing (stamps `OriginalPrintedAt`, writes audit). Read perm per doctype.

`POST /{docType}/{id}/mark-printed` — Query: `copy?` (bool). → `200` / `404`.

| docType | Auth |
|---|---|
| `tax-invoices` | `sales.tax_invoice.read` |
| `receipts` | `sales.receipt.read` |
| `credit-notes` | `sales.credit_note.read` |
| `debit-notes` | `sales.debit_note.read` |
| `quotations` | `sales.quotation.manage` |
| `sales-orders` | `sales.sales_order.manage` |
| `delivery-orders` | `sales.delivery_order.manage` |
| `billing-notes` | `sales.billing_note.read` |
| `purchase-orders` | `purchase.purchase_order.read` |
| `payment-vouchers` | `purchase.payment_voucher.read` |

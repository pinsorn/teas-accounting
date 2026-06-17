# Purchases

งานซื้อ: ใบสั่งซื้อ ใบแจ้งหนี้ผู้ขาย ใบสำคัญจ่าย และหนังสือรับรองหัก ณ ที่จ่าย.

The accounts-payable chain: Purchase Order → Vendor Invoice → Payment Voucher, plus WHT certificates. (The two PO `/reports/*` routes are documented in `reports.md`.)

## Purchase Orders
- `POST /purchase-orders` — create. **Auth:** `purchase.purchase_order.create`. Body: `docDate`, `expectedDeliveryDate?`, `vendorId` (required), `businessUnitId?`, `currencyCode`, `exchangeRate`, `notes?`, `internalNotes?`, `lines[]` (`CreatePurchaseOrderRequest`). → `201`.
- `PUT /purchase-orders/{id}` — edit. **Auth:** `purchase.purchase_order.create`. → `200`/`204`.
- `POST /purchase-orders/{id}/approve` — approve. **Auth:** `purchase.purchase_order.approve`. → `204`.
- `POST /purchase-orders/{id}/mark-sent` — mark sent to vendor. **Auth:** `purchase.purchase_order.create`. → `204`.
- `POST /purchase-orders/{id}/close` — close. **Auth:** `purchase.purchase_order.cancel`. → `204`.
- `POST /purchase-orders/{id}/cancel` — cancel. **Auth:** `purchase.purchase_order.cancel`. → `204`.
- `GET /purchase-orders` — list. **Auth:** `purchase.purchase_order.read`. → `200`.
- `GET /purchase-orders/{id}` — detail. **Auth:** `purchase.purchase_order.read`. → `200` / `404`.
- `GET /purchase-orders/{id}/pdf` — PDF. **Auth:** `purchase.purchase_order.read`. → `application/pdf`.

## Vendor Invoices (ใบแจ้งหนี้ผู้ขาย)
Input-VAT claim source; claim period defaults to the period of the vendor's tax-invoice date (§4).
- `POST /vendor-invoices` — create. **Auth:** `purchase.vendor_invoice.create`. Body: `docDate`, `vendorId` (required), `vendorTaxInvoiceNo`, `vendorTaxInvoiceDate`, `vatClaimPeriod?` (YYYYMM), `currencyCode`, `exchangeRate`, `notes?`, `lines[]`, `hasInputVat?` (null = auto-derive), `purchaseOrderId?`, `businessUnitId?` (`CreateVendorInvoiceRequest`). → `201`.
- `PUT /vendor-invoices/{id}` — edit (draft). **Auth:** `purchase.vendor_invoice.create`. → `204`.
- `POST /vendor-invoices/{id}/claim-period` — set/override the input-VAT claim period. **Auth:** `purchase.vendor_invoice.create`. → `200`/`204`.
- `POST /vendor-invoices/{id}/post` — post. **Auth:** `purchase.vendor_invoice.post`. → `200`/`204`.
- `GET /vendor-invoices` — list. **Auth:** `purchase.vendor_invoice.read`. → `200`.
- `GET /vendor-invoices/{id}` — detail. **Auth:** `purchase.vendor_invoice.read`. → `200` / `404`.

## Payment Vouchers (ใบสำคัญจ่าย)
SoD split: create / approve / post. PV number embeds the expense category (`MM-YYYY-PV-CATEGORY-NNNN`).
- `POST /payment-vouchers` — create. **Auth:** `purchase.payment_voucher.create`. Body: `docDate`, `vendorId` (required), `expenseCategoryId` (int), `paymentMethod`, `chequeNo?`, `chequeDate?`, `bankAccountId?`, `currencyCode`, `exchangeRate`, `description?`, `notes?`, `lines[]`, `vendorInvoiceId?` (settle a posted VI), WHT auto-derive fields (`CreatePaymentVoucherRequest`). → `201`.
- `POST /payment-vouchers/{id}/approve` — approve. **Auth:** `purchase.payment_voucher.approve`. → `204`.
- `POST /payment-vouchers/{id}/post` — post. **Auth:** `purchase.payment_voucher.post`. → `204`.
- `POST /payment-vouchers/{id}/vendor-invoice` — create a vendor invoice from this PV. **Auth:** `purchase.vendor_invoice.create`. → `200`.
- `GET /payment-vouchers` — list. **Auth:** `purchase.payment_voucher.read`. → `200`.
- `GET /payment-vouchers/{id}` — detail. **Auth:** `purchase.payment_voucher.read`. → `200` / `404`.
- `GET /payment-vouchers/{id}/pdf` — PDF. **Auth:** `purchase.payment_voucher.read`. → `application/pdf`.

## WHT Certificates (หนังสือรับรองการหัก ณ ที่จ่าย, 50ทวิ)
Read-only here; certificates are issued from receipts/PVs. Gated by `purchase.wht.read`.
- `GET /wht-certificates` — list. → `200`.
- `GET /wht-certificates/{id}` — detail. → `200` / `404`.
- `GET /wht-certificates/{id}/pdf` — 50ทวิ PDF. → `application/pdf`.

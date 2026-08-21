# Reports

รายงาน: งบกำไรขาดทุน งบดุล งบทดลอง สรุปภาษี/ยอดขาย ทะเบียนภาษีซื้อ-ขาย รายงานหัก ณ ที่จ่ายค้างรับ อายุหนี้เจ้าหนี้ และเลขเอกสารที่ขาดหาย.

Read-only reporting endpoints. Routes are spread across three modules (`ReportEndpoints`, `TaxFilingEndpoints`, `PurchaseOrderEndpoints`) but are consolidated here. Many take a `period` (YYYYMM) or a date range — see `openapi.yaml` for exact query params.

## Financial statements
- `GET /reports/trial-balance` — งบทดลอง. **Auth:** `report.trial_balance.read`. Returns `200`.
- `GET /reports/balance-sheet` — งบแสดงฐานะการเงิน. **Auth:** `report.trial_balance.read`. Returns `200`.
- `GET /reports/profit-loss` — งบกำไรขาดทุน. **Auth:** `report.profit_loss.read`. Returns `200`.

## Sales & tax summaries
- `GET /reports/sales-summary` — sales summary. **Auth:** `report.profit_loss.read`. Returns `200`.
- `GET /reports/tax-summary` — tax summary. **Auth:** `report.profit_loss.read`. Returns `200`.
- `GET /reports/vat-register` — VAT register. **Auth:** `tax.vat_register.read`. Returns `200`.
- `GET /reports/pnd30` — ภ.พ.30 view. **Auth:** `tax.pnd30.read`. Returns `200`.

## VAT registers (period)
- `GET /reports/input-vat-register` — input VAT (ภาษีซื้อ). **Auth:** `tax.vat_register.read`. Query: `period`. Returns `200`.
- `GET /reports/output-vat-register` — output VAT (ภาษีขาย). **Auth:** `tax.vat_register.read`. Query: `period`. Returns `200`.

## WHT receivable
All gated by `tax.pnd53.read`.
- `GET /reports/wht-receivable-register` — WHT-receivable register. Returns `200`.
- `GET /reports/wht-receivable-aging` — aging of WHT receivable. Returns `200`.
- `GET /reports/wht-receivable-missing-cert` — receivables missing a WHT certificate. Returns `200`.

## Accounts payable
Served by `PurchaseOrderEndpoints` but mounted at the bare `/reports/*` prefix (not under `/purchase-orders`).
- `GET /reports/ap-aging` — AP aging. **Auth:** `purchase.purchase_order.read`. Query: `asOf?`, `vendorId?`. Returns `200`.
- `GET /reports/outstanding-po` — outstanding purchase orders. **Auth:** `purchase.purchase_order.read`. Query: `as_of?`, `vendorId?`, `overdue_only?`. Returns `200`.

## General ledger & subledgers
- `GET /reports/general-ledger` — per-account GL drill-down. **Auth:** `report.general_ledger.read`. Query: `accountId`, `fromDate`, `toDate`. Returns `200`.
- `GET /reports/general-ledger/accounts` — account picker for the GL screen. **Auth:** `report.general_ledger.read`. Returns `200`.
- `GET /reports/general-ledger/export` — export GL to PDF or CSV. **Auth:** `report.general_ledger.read`. Query: `accountId`, `fromDate`, `toDate`, `format` (`pdf`|`csv`). Returns the file.
- `GET /reports/ar-aging` — AR aging (specs/subledgers.md). **Auth:** `sales.tax_invoice.read`. Query: `asOf?`, `customerId?`. Returns `200`.
- `GET /reports/ar-aging/export` — AR aging as UTF-8-BOM CSV (formula-injection-safe). **Auth:** `sales.tax_invoice.read`. Returns `text/csv`.
- `GET /reports/customer-statement` — statement for one customer. **Auth:** `sales.tax_invoice.read`. Query: `customerId`, `fromDate`, `toDate`. Returns `200`.
- `GET /reports/vendor-ledger` — subledger for one vendor. **Auth:** `purchase.vendor_invoice.read`. Query: `vendorId`, `fromDate`, `toDate`. Returns `200`.

## Audit / numbering
- `GET /reports/number-gaps` — document-number gap audit (ม.86/4 #4, sequential no-gaps). **Auth:** `report.audit.read`. Returns `200`.
- `GET /reports/pending-agent-approvals` — count of Draft documents created via an MCP API key awaiting human approval (badge count). **Auth:** any permission that reads tax invoices. Returns `200` `{ count, taxInvoices, quotations, receipts, purchaseOrders, vendorInvoices, paymentVouchers }`.

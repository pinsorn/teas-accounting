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
- `GET /purchase-orders/reports/ap-aging` — AP aging. **Auth:** `purchase.purchase_order.read`. Returns `200`.
- `GET /purchase-orders/reports/outstanding-po` — outstanding purchase orders. **Auth:** `purchase.purchase_order.read`. Returns `200`.

## Audit / numbering
- `GET /reports/number-gaps` — document-number gap audit (ม.86/4 #4, sequential no-gaps). **Auth:** `report.audit.read`. Returns `200`.

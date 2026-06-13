# PV (ใบสำคัญจ่าย): product-driven lines + derived VAT (Ham 2026-06-13)

## Problem
The PV create form let the user type VAT by hand and pick a free-text description + a manual
GOOD/SERVICE type. It should match the sales forms: pull items from `/settings/products` and
**derive** VAT — never let the user set it. And a non-VAT-registered vendor must never carry VAT
on a purchase (ม.82/5). WHT stays exactly as-is (Ham: "ส่วนพวกหัก ณ ที่จ่ายให้อิงตามปัจจุบัน ดีแล้ว").

## Changes
### FE (`payment-vouchers/new`)
- Replace the free-text description input + `ProductTypeSelect` + manual VAT `<input>` with the
  shared `ProductPicker` (`purpose="purchase"`, scoped to the doc BU) — same component sales uses.
  Picking a product fills description / productType / (seeds price only if the line is still 0,
  master never locks price per Ham cont.81). Free text → ad-hoc line, productType resets to GOOD.
- VAT is a **read-only derived display**: `vendorVat ? taxRateForProductType(productType) : 0`
  (EXEMPT_* → 0% ม.81; non-VAT vendor → 0% ม.82/5; else 7%). Totals + save use the derived rate.
- PO-prefill no longer copies a VAT rate (derived).

### BE (`PaymentVoucherService.CreateDraftAsync`)
- Guard: a non-VAT-registered vendor with any line `VatRate > 0` → `DomainException
  "pv.vendor_not_vat_registered"` (ม.82/5). Foreign vendors stay `VatRegistered=true` and route
  VAT via ภ.พ.36 reverse charge, so they are unaffected. (FE already forces 0, this is defense +
  the API/external-caller contract.)

## Out of scope
PV lines do not persist a product FK (no column; the existing `productType` snapshot already drives
VAT). Vendor-Invoice (VI) is the input-VAT-claim document and is untouched. WHT untouched.

## Gates
Api ≥ baseline + 2 guard tests (`Non_vat_registered_vendor_rejects_a_vat_line`,
`…_allows_a_zero_vat_line`) ×2 · tsc 0 · i18n parity · visual gate (7% VAT vendor + 0% non-VAT).

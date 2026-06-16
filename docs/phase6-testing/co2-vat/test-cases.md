# Phase 6 Acceptance Test — Company 2 (co2) VAT-Registered

**Target:** Company 2 — "Manual Demo Co., Ltd." (บริษัท แมนนวล เดโม จำกัด), VAT-registered @ 7%
**Persona:** `demo-admin` (company-2 scoped, RLS enforced)
**API:** `http://localhost:5080` (ASPNETCORE_ENVIRONMENT=Development), already running — not restarted
**Run date:** 2026-06-16 (Asia/Bangkok). Accounting period **2026-06 OPEN**, 2026-05 CLOSED. All documents dated **2026-06-16**; tax-filing PDFs `period=202606` / `year=2026`.
**Driver:** `docs/phase6-testing/co2-vat/driver.py` (Python 3.10, resumable via `state.json`; raw results in `results.json`).

---

## Summary

| Metric | Count |
|---|---|
| Total test cases | **49** |
| PASS | **42** |
| FAIL | **7** (all genuine findings/limitations — see below) |
| PDFs downloaded (`%PDF`/`PK` validated) | **23** (22 PDF + 1 ZIP payslip bundle) |

**Headline result:** The full VAT feature surface is exercised end-to-end — sales chain (Quotation → SO → DO → Tax Invoice → Receipt), Credit/Debit Notes, Purchase chain (Vendor Invoice → Payment Voucher → **WHT 50ทวิ certificate**), payroll (PND1 / payslips / SSO / PND1A / 50ทวิ), and **all 8 tax-filing PDFs** (PND30, PND3, PND53, PND54, PND51, PND50, PP01, PP09). The posted Tax Invoice carries correct sequential numbering and a real June tax-point date.

**One blocking-class defect found** (not fixable via API): co2's seed lacks company-2 VAT tax codes, so standard sales compute **0 output VAT**. See *Found Defects* §A — this is the most important takeaway for a VAT company.

---

## Created Document Numbers (posted/issued)

| Document | Number | ID |
|---|---|---|
| Tax Invoice | `06-2026-TI-ECOM-0001` | 3 |
| Receipt | `06-2026-RC-ECOM-0002` | 16 |
| Credit Note | `06-2026-CN-ECOM-0001` | 1 |
| Debit Note | *(issued; number not surfaced in create response — DN id 3)* | 3 |
| Vendor Invoice | `06-2026-VI-ECOM-0002` | 4 |
| Payment Voucher | `06-2026-PV-ECOM-ADS-0001` | 3 |
| WHT 50ทวิ Certificate | `06-2026-WT-0001` | 4 |

Quotation (id 10), Sales Order (id 1), Delivery Order (id 1) remain **DRAFT** — by design a document number is assigned only on POST/Issue (ม.86/4 #4), so these correctly have no number yet; their PDFs still render.

---

## 1. Master Data Sanity

| ID | Endpoint | Expected | Status | Result |
|---|---|---|---|---|
| MD-01 | GET /customers | list | 200 — 8 customers | PASS |
| MD-02 | GET /products | list | 200 — 12 products (GOOD/SERVICE/EXEMPT_GOOD) | PASS |
| MD-03 | GET /vendors | list | 200 — 6 vendors | PASS |
| MD-04 | GET /business-units | list | 200 — 3 BUs (ECOM/…); **BU is required on every co2 document** | PASS |
| MD-05 | GET /wht-types | list | 200 — 3 WHT types (ADS 2% PND53, …) | PASS |
| MD-06 | GET /expense-categories | list | 200 — 5 categories | PASS |
| MD-07 | GET /document-prefixes | list | 200 — 15 prefixes | PASS |

Chosen for the chain: customer 1 (VAT-registered Corporate — gives buyer TaxID on the TI, ม.86/4 #3), product 4 (GOOD), vendor 1 (VAT-registered), WHT type 21 (ADS 2%), expense category 8 (ADS), business unit 1 (ECOM).

## 2. Sales Chain (June dates)

| ID | Method + Endpoint | Inputs | Expected | Status | PDF | Result |
|---|---|---|---|---|---|---|
| SAL-01 | POST /quotations | cust 1, 2 lines | 201 draft | 201 | — | PASS |
| SAL-02 | POST /sales-orders | from quotation 10 | 201 draft | 201 | — | PASS |
| SAL-02b | POST /sales-orders/1/confirm | — | confirm or N/A | 404 (route N/A — SO auto-progresses) | — | PASS |
| SAL-03 | POST /delivery-orders | from SO 1 | 201 draft | 201 | — | PASS |
| SAL-03b | POST /delivery-orders/1/post | — | post or N/A | 404 (route N/A) | — | PASS |
| SAL-04 | POST /tax-invoices | cust 1, doc_date 2026-06-16, 2 lines | 201 draft | 201 | — | PASS |
| SAL-04b | POST /tax-invoices/3/post | — | posted + sequential number | 200 → `06-2026-TI-ECOM-0001` | — | PASS |
| SAL-04c | GET /tax-invoices/3 | — | totals present | 200 | — | PASS |
| SAL-05 | POST /receipts | applied 8,750 to TI 3 (Cash) | 201 | 201 | — | PASS |
| SAL-05b | POST /receipts/16/post | — | posted | 200 → `06-2026-RC-ECOM-0002` | — | PASS |
| PDF-01 | GET /quotations/10/pdf | — | %PDF | 200 (116 KB) | quotation-None.pdf | PASS |
| PDF-02 | GET /sales-orders/1/pdf | — | %PDF | 200 (114 KB) | sales-order-None.pdf | PASS |
| PDF-03 | GET /delivery-orders/1/pdf | — | %PDF | 200 (114 KB) | delivery-order-None.pdf | PASS |
| PDF-04 | GET /tax-invoices/3/pdf | — | %PDF | 200 (119 KB) | tax-invoice-06-2026-TI-ECOM-0001.pdf | PASS |
| PDF-05 | GET /receipts/16/pdf | — | %PDF | 200 (122 KB) | receipt-06-2026-RC-ECOM-0002.pdf | PASS |

### Compliance assertions (ม.86/4)

| ID | Check | Expected | Actual | Result |
|---|---|---|---|---|
| CMP-01 | VAT shown separately @7% (ม.86/4 #6) | tax = 7% of subtotal | subtotal **8,750**, taxable **0**, VAT **0**, total **8,750**; tax-point **2026-06-16** | **FAIL** — see Defect §A |
| CMP-02 | Sequential doc number on issue (ม.86/4 #4) | number assigned on POST | `06-2026-TI-ECOM-0001` present after issue | PASS |

The TI PDF also carries seller name/TaxID/branch + buyer name/TaxID/branch (customer 1 is VAT-registered) and a real issue date — the structural ม.86/4 fields are present; only the VAT *amount* is wrong because of the seed gap.

## 3. Credit Note / Debit Note (against posted TI 3)

| ID | Method + Endpoint | Inputs | Status | PDF | Result |
|---|---|---|---|---|---|
| ADJ-01 | POST /tax-adjustment-notes | noteType **Credit**, reasonCode **Return**, taxRate **0.07**, adj 1,750 | 201 | — | PASS |
| ADJ-01b | POST /tax-adjustment-notes/1/post | — | 200 → `06-2026-CN-ECOM-0001` | — | PASS |
| ADJ-02 | POST /tax-adjustment-notes | noteType **Debit**, reasonCode **PriceIncrease**, taxRate 0.07, adj 200 | 201 | — | PASS |
| ADJ-02b | POST /tax-adjustment-notes/3/post | — | 200 (DN issued, id 3) | — | PASS |
| PDF-06 | GET /tax-adjustment-notes/1/pdf | CN | 200 (122 KB) | credit-note-06-2026-CN-ECOM-0001.pdf | PASS |
| PDF-07 | GET /tax-adjustment-notes/3/pdf | DN | 200 (121 KB) | debit-note-dn3.pdf | PASS |

Note: `noteType` is an enum bound by **member name** (`Credit`/`Debit`, not `CN`/`DN`); `reasonCode` must be a valid `CreditNoteReasonCode`/`DebitNoteReasonCode` (e.g. `Return`, `PriceIncrease`); `taxRate` is a **fraction** (0.07), not 7.

## 4. Purchase Chain → WHT 50ทวิ

| ID | Method + Endpoint | Inputs | Status | PDF | Result |
|---|---|---|---|---|---|
| PUR-01 | POST /vendor-invoices | vendor 1, line 10,000 vat_rate 0.07, BU 1 | 201 | — | PASS |
| PUR-01b | POST /vendor-invoices/4/post | — | 200 → `06-2026-VI-ECOM-0002` | — | PASS |
| PUR-02 | POST /payment-vouchers | vendor 1, line 10,000 + WHT type 21 @0.02, Cash, BU 1 | 201 | — | PASS |
| PUR-02b | POST /payment-vouchers/3/approve | B2 workflow Draft→**Approved** | 200 | — | PASS |
| PUR-02c | POST /payment-vouchers/3/post | Approved→**Posted** | 200 → `06-2026-PV-ECOM-ADS-0001` | — | PASS |
| PUR-02d | GET /payment-vouchers/3 | read cert | 200 — whtCertificates[0].id = **4** | — | PASS |
| PDF-08 | GET /payment-vouchers/3/pdf | — | 200 (124 KB) | payment-voucher-06-2026-PV-ECOM-ADS-0001.pdf | PASS |
| PDF-09 | GET /wht-certificates/4/pdf | 50ทวิ | 200 (145 KB) | wht-cert-4.pdf | PASS |

The PV withheld **200 THB** (2% of 10,000) and issued WHT certificate `06-2026-WT-0001`. Workflow is strictly **Draft → Approved → Posted**; the 50ทวิ certificate is generated on POST (not on approve). `/wht-certificates/{id}/pdf` works even though it is absent from `openapi.yaml`.

## 5. Payroll (existing DRAFT run 202602, run id 2, 2 employees)

| ID | Method + Endpoint | Status | File | Result |
|---|---|---|---|---|
| PAY-01 | GET /payroll/runs/2/pnd1/pdf | 200 (317 KB) | payroll-pnd1-run2.pdf | PASS |
| PAY-02 | GET /payroll/runs/2/payslips/pdf | 200 (32 KB) | payroll-payslips-run2.**zip** | PASS |
| PAY-03 | GET /payroll/runs/2/payslips/3/pdf | 200 (35 KB) | payroll-payslip-run2-emp3.pdf | PASS |
| PAY-04 | GET /payroll/runs/2/sso/pdf | 200 (252 KB) | payroll-sso-run2.pdf | PASS |
| PAY-05 | GET /payroll/pnd1a/pdf?year=2026 | 200 (510 KB) | payroll-pnd1a-2026.pdf | PASS |
| PAY-06 | GET /payroll/employees/3/wht50tawi/pdf?year=2026 | 200 (146 KB) | payroll-wht50tawi-emp3-2026.pdf | PASS |

Notes: `payslips/pdf` returns a **ZIP bundle** of per-employee PDFs (magic `PK`), not a single PDF — saved as `.zip`. The per-employee endpoints take the **employeeId present in the run's payslips array** (employee 3, not 1/2); the run detail's `payslips[]` was read to discover it. PAY-06 worked because run 2 has posted/paid payroll data for those employees in 2026.

## 6. Tax-Filing PDFs (the headline)

| ID | Endpoint | Params | Status | File | Result |
|---|---|---|---|---|---|
| TAX-01 | /tax-filings/pnd30/pdf (VAT return ภ.พ.30) | period=202606 | 200 (296 KB) | pnd30-202606.pdf | PASS |
| TAX-02 | /tax-filings/pnd3/pdf | period=202606 | 200 (262 KB) | pnd3-202606.pdf | PASS |
| TAX-03 | /tax-filings/pnd53/pdf | period=202606 | 200 (912 KB) | pnd53-202606.pdf | PASS |
| TAX-04 | /tax-filings/pnd54/pdf | period=202606 | 200 (289 KB) | pnd54-202606.pdf | PASS |
| TAX-05 | /tax-filings/pnd51/pdf (CIT half-year) | year=2026 | 200 (278 KB) | pnd51-2026.pdf | PASS |
| TAX-06 | /tax-filings/pnd50/pdf (CIT annual) | year=2026, attestFirstFiling, attestBlankSchedules | 200 (1.31 MB) | pnd50-2026.pdf | PASS |
| TAX-07 | /tax-filings/pp01/pdf | — | 200 (358 KB) | pp01.pdf | PASS |
| TAX-08 | /tax-filings/pp09/pdf | — | 200 (331 KB) | pp09.pdf | PASS |

CIT prerequisite: `POST /tax-filings/cit/years/2026/compute` (CIT-01) was run first so PND50/51 had a computed year. ภ.พ.36 (reverse-charge) has **no dedicated route** in the contract (only PND54); the pre-seeded reverse-charge AWS vendor invoice `06-2026-VI-ECOM-0001` (vat_claim_period 202606) is its source data.

## 7. Reports (read-only)

| ID | Endpoint | Status | Key figures / note | Result |
|---|---|---|---|---|
| RPT-02 | /reports/ap-aging?asOf=2026-06-16 | 200 | returned | PASS |
| RPT-03 | /reports/balance-sheet?asOfDate=2026-06-16 | 200 | **balanced=true**, L&E total **27,741.50**, current-period earnings **-13,050.00** | PASS |
| RPT-05 | /reports/number-gaps | 200 | **hasGaps=false** (no numbering gaps — ม.86/4 #4) | PASS |
| RPT-09 | POST /tax-filings/pnd51/estimate?year=2026&estimatedProfit=100000 | 200 | computedNetProfit **-57,875**, pnd51Prepaid **10,000** | PASS |
| RPT-01 | /reports/tax-summary?year=2026 | **500** | backend bug — see Defect §B | FAIL |
| RPT-04 | /reports/expense-by-category | **404** | route in openapi but not served — Limitation §L1 | FAIL |
| RPT-06 | /reports/vat-output-register | **404** | route in openapi but not served — Limitation §L1 | FAIL |
| RPT-07 | /reports/vat-input-register | **404** | route in openapi but not served — Limitation §L1 | FAIL |
| RPT-08 | /reports/pnd30/preview | **404** | route in openapi but not served — Limitation §L1 | FAIL |
| RPT-10 | /documents/chain?type=TAX_INVOICE&id=3 | **404** | doc-chain lookup returned 404 — Limitation §L2 | FAIL |

---

## Found Defects (action items for Ham)

### A. (HIGH) co2 has zero output VAT — VAT-registered company computes 0 VAT on every standard sale
- **Symptom:** Tax Invoice `06-2026-TI-ECOM-0001` (subtotal 8,750) reports `taxableAmount=0`, `taxAmount=0`, `nonTaxableAmount=8,750`. Expected VAT @7% = **612.50**. Same for every tax-code string tried (`VAT7`, `VAT-OUT-7`, `SR`, `S7`) and for `taxCodeId=1`.
- **Root cause:** `tax.tax_codes` / `tax.tax_rates` are seeded **for company 1 only** (`240_seed_exempt_tax_codes.sql` rows are all `(1, …)`). The co2 seed `400_seed_manual_demo_company.sql` (see its own comment at line 24: *"a DRAFT TI adds a fragile FK chain (tax_codes/uom per company 2)"*) inserts **no** `tax.tax_codes` / `tax.tax_rates` rows for company 2, and co2 products have `defaultOutputTaxCodeId = null`. With no matching tax code, every sales line is classified non-taxable → zero output VAT.
- **Impact:** ภ.พ.30 output VAT for co2 is effectively 0; co2 cannot demonstrate a compliant VAT tax invoice (ม.86/4 #6). This is the single most important gap for a "VAT-registered demo company".
- **Not fixable via API** (no `/tax-codes` POST; seeding `tax.tax_codes` is out of scope for this test). Needs a migration/seed adding company-2 output/input VAT codes + rates and (ideally) product default tax-code ids.

### B. (MED) GET /reports/tax-summary → 500 "integer out of range"
- `Npgsql 22003: integer out of range`. A column overflow in the tax-summary query (likely an `int` SUM/cast that should be `bigint`/`numeric`). Reproducible with `?year=2026` after posting the chain. Backend bug, independent of the tax inputs.

---

## Limitations / Expected-Unavailable

- **L1 — openapi/route drift (404):** `/reports/expense-by-category`, `/reports/vat-output-register`, `/reports/vat-input-register`, `/reports/pnd30/preview` are documented in `openapi.yaml` but return 404 from the running API. Either removed/renamed or not wired — the contract is stale here. (VAT register data is still obtainable via the PND30 PDF.)
- **L2 — /documents/chain (404):** with `?type=TAX_INVOICE&id=3` returns 404; the document-chain lookup needs a different discriminator value or isn't populated for this doc. Low priority.
- **L3 — ภ.พ.36 (PP36):** no dedicated endpoint exists (only PND54). Expected-unavailable; reverse-charge data lives on the pre-seeded foreign VI.
- **L4 — Debit Note number not surfaced:** the tax-adjustment-note **create** response returns only `note_id` (no doc number); the DN posts and its PDF renders, but its issued number was not captured in the create payload.

---

## Contract-drift findings (openapi.yaml vs running API) — for Sana

These cost the most debugging time and should be corrected in `openapi.yaml`:

1. **Request casing:** the 6 create endpoints (`/quotations`, `/sales-orders`, `/delivery-orders`, `/tax-invoices`, `/vendor-invoices`, `/payment-vouchers`) are documented with **snake_case** fields but the API binds **camelCase** (System.Text.Json camelCase policy; snake_case is silently ignored → "must be > 0 / must not be empty" validation). Responses, however, are **snake_case** (`tax_invoice_id`, etc.). Mixed convention.
2. **Missing required fields in schemas:** `/tax-invoices` requires `docDate` (omitted in schema — without it the tax point becomes `0001-01-01`, breaking numbering and VAT-rate lookup); `/sales-orders` requires `currencyCode` + `exchangeRate` (omitted). All co2 documents also require `businessUnitId` (not marked required).
3. **Line field names:** sales-doc lines need `descriptionTh` + `taxCode` + `uomText` (the documented generic `LineItem.description` maps to the wrong column → NOT-NULL violation on `description_th`).
4. **Rate units:** VI/PV line `vatRate`/`whtRate` and CN/DN `taxRate` are **fractions** (0.07 / 0.02), validated `InclusiveBetween(0,1)` — the docs imply whole-number percentages.
5. **Enums by member name:** CN/DN `noteType` = `Credit`/`Debit` (not `CN`/`DN`).
6. `/wht-certificates` (+ `/{id}/pdf`) is served but absent from `openapi.yaml`.

---

## Notes on data impact

This test **posted** real documents into co2 (one Tax Invoice, Receipt, CN, DN, Vendor Invoice, Payment Voucher + WHT cert) and ran the CIT-2026 compute. Posted documents are immutable. Memory flags co2's P&L as load-bearing for the user manual (ch7/8); the task explicitly directed posting into co2 on a fresh seed, so period figures have moved accordingly. A few orphan **DRAFT** tax invoices (ids 4–8) were created while probing the VAT tax-code behaviour — they are unposted and carry no number; they can be ignored or cleaned up.

**Deliverables:** PDFs in `docs/phase6-testing/co2-vat/pdfs/` (23 files); driver + raw logs (`driver.py`, `state.json`, `results.json`) alongside this report.

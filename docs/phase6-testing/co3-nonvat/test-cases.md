# Phase 6 Acceptance Test — Company 3 (Non-VAT Demo Shop)

**Target company:** Company 3 — "Non-VAT Demo Shop" (ร้านนอนแวต เดโม), **NOT VAT-registered**
**Persona:** `rbac_nv_company_admin` (company-3 admin, RLS-scoped to company 3)
**API base:** `http://localhost:5080` (Development)
**Test date:** 2026-06-16 (Asia/Bangkok). All documents dated **2026-06-16** (June = open period; May may be closed).
**Driver:** `run_phase6.py` (idempotent; re-login on 401; saves PDFs + `results.json`).

---

## 1. Summary

| Metric | Value |
|---|---|
| Total test cases | **54** |
| PASS | **47** |
| FAIL | **0** |
| INFO (expected-unavailable / environment gap / known doc behaviour) | **7** |
| PDFs / archives downloaded | **16** (15 PDF + 1 ZIP of payslips) |

**Verdict:** The full non-VAT feature surface was exercised end-to-end. The sales chain
(Quotation -> Billing Note -> Receipt) produces correctly with **zero VAT applied anywhere**
(verified inside the receipt PDF) and **no Tax Invoice issuable** — exactly as a non-VAT company
must behave. Payroll, WHT-filing (ภ.ง.ด.3/53/54), and CIT (ภ.ง.ด.51/50) PDFs all generate; note
that the ภ.ง.ด.3/53/54 forms render with **zero WHT content** this run because no Payment Voucher
could be posted (see the CoA gap below). The only items not fully completed are **purchase posting (Vendor Invoice / Payment
Voucher POST)**, which is blocked by an **incomplete seeded Chart of Accounts for company 3**
(configured GL account `1170` absent) — an environment data gap, not a code defect. The test agent
is forbidden to seed the CoA / edit appsettings, so these are recorded as INFO findings with full
evidence below.

### Created documents (this campaign)

| Document | Doc number | Notes |
|---|---|---|
| Quotation | `06-2026-QT-0003` | non-VAT; tax_amount = 0 |
| Billing Note (ใบแจ้งหนี้/ใบวางบิล) | `06-2026-IV-0007` (latest) | issued; total 2,000 THB; no VAT line. Prefix `IV` |
| Receipt (ใบเสร็จรับเงิน) — applied to Billing Note | `06-2026-RC-0013` | posted; 2,000 THB; no VAT line (verified in PDF) |
| Receipt — standalone cash bill | `06-2026-RC-0014` | posted; 1,000 THB, no VAT |
| Vendor Invoice (individual vendor) | draft id assigned | POST blocked by co3 CoA gap (acct 1170) |
| Payment Voucher (WHT, individual) | draft + approved | POST blocked by co3 CoA gap (acct 1170); PDF rendered at DRAFT state |

> Doc numbers reflect the cumulative state of the shared dev DB (the driver was run several times
> while iterating). Each clean run re-creates the chain with the next sequential number; numbering
> is gap-free and monthly-reset per `MM-YYYY-PREFIX-NNNN` (compliance §4.3).

---

## 2. Non-VAT semantics — confirmed evidence

A non-VAT company must NOT issue Tax Invoices and must NOT charge VAT. Confirmed from live data:

- **`/reports/tax-summary?year=2026`** — every month returns `outputVat=0, inputVat=0,
  vatPayable=0, vatRefundable=0`. No VAT anywhere.
- **Quotation** create returned `tax_amount = 0`. The **Receipt PDF was visually inspected**
  (`03-receipt.pdf`): titled "ใบเสร็จรับเงิน / RECEIPT" (NOT a tax invoice), header "Non-VAT Demo",
  doc `06-2026-RC-0013`, totals block shows only "รวมทั้งสิ้น / Total ฿2,000.00" with **NO VAT
  line** — confirmed empirically (ม.86 — no VAT for a non-registrant). The PV PDF
  (`05-payment-voucher-wht.pdf`) likewise shows "ภาษีมูลค่าเพิ่ม 7% / VAT 0.00".
- **Trial balance** account `2151 ภาษีขายค้างจ่าย` (output VAT payable) net = **0** — no output VAT
  ever booked.
- **`POST /tax-invoices`** -> **HTTP 400 rejected** (a non-VAT company cannot issue a Tax Invoice).
- **`/reports/pnd30/preview`** and **`/reports/vat-output-register`** -> **HTTP 404** (no VAT
  register exists for a non-VAT company).

---

## 3. Correctly UNAVAILABLE for non-VAT (the important section)

| Feature | Endpoint | Result | Interpretation |
|---|---|---|---|
| **Issue a Tax Invoice** | `POST /tax-invoices` | **400 rejected** | PASS — Tax Invoice is correctly unavailable for a non-VAT company. |
| **ภ.พ.30 preview** | `GET /reports/pnd30/preview?period=202606` | **404** | PASS — no VAT return data for a non-VAT company. |
| **Output-VAT register** | `GET /reports/vat-output-register?year=2026&month=6` | **404** | PASS — no output-VAT register exists. |
| **ภ.พ.30 PDF filler** | `GET /tax-filings/pnd30/pdf` | 200, PDF renders **blank/zero** | INFO — the form *filler* still renders, but carries zero VAT (the data-layer gate above is the real guard). |
| **ภ.พ.01 PDF** (VAT registration request) | `GET /tax-filings/pp01/pdf` | 200, PDF renders | INFO — registration-application form generates regardless of registration state (by design). |
| **ภ.พ.09 PDF** (VAT change request) | `GET /tax-filings/pp09/pdf` | 200, PDF renders | INFO — same as ภ.พ.01. |
| **ภ.พ.36** (task-named) | `GET /tax-filings/pp36/pdf` (and `/pnd36/pdf`) | **404 — endpoint does not exist** | PASS — no ภ.พ.36 form is implemented in this build, so it is inherently unavailable for any company including non-VAT. (The task named ภ.พ.36; it is simply not a mounted route. ภ.พ.01/09 were exercised as the closest VAT-registration forms.) |

**Finding (cosmetic, optional):** the `/tax-filings/pnd30|pp01|pp09/pdf` *PDF fillers* do not
hard-refuse for a non-VAT company; they render a blank/zero form. The meaningful non-VAT enforcement
is correctly at the **data layer** (404 on the VAT preview/register, 400 on Tax Invoice, 0 VAT in
tax-summary). If a stricter UX is desired, these PDF endpoints could 422 for a non-VAT company —
not required for compliance.

---

## 4. Environment finding — company 3 Chart of Accounts is incomplete

**Purchase posting cannot complete for company 3** because a configured GL account is missing:

```
422 gl.account_missing —
"Configured GL account '1170' is missing from chart_of_accounts for company 3.
 Seed the CoA or update GlAccounts in appsettings."
```

- `POST /vendor-invoices` (create) — **201 OK** (draft created).
- `POST /vendor-invoices/{id}/post` — **422** `gl.account_missing` (acct 1170 absent).
- `POST /payment-vouchers` (create) — **201 OK**; `POST /{id}/approve` — **200 OK**.
- `POST /payment-vouchers/{id}/post` — **422** `gl.account_missing` (acct 1170 absent), even as a
  **standalone** PV (so it is not the VI-settle path; the PV posting routine itself needs 1170).

Company 3's active CoA contains only 13 accounts (1110/1120/1130, 2110/2151/2153/2160/2170,
4000, 5200/5350/5400/5410). It has cash/bank and WHT-payable accounts, but **not 1170**.

**Impact:** the Payment-Voucher PDF (with inline 50ทวิ WHT) was still produced from the
**approved** PV, but the WHT certificate number is assigned only on POST, so the standalone
`/wht-certificates/{id}/pdf` certificate could not be generated. This is a **seed/config gap to fix
in `440_seed_nonvat_demo_company.sql` (or the GlAccounts appsettings mapping)**, after which the
WHT (ภ.ง.ด.3) end-to-end for an individual vendor will complete. The test agent did not modify
seeds/appsettings per its rules.

---

## 5. Test cases by feature area

Columns: `id | method + endpoint | inputs | HTTP | PDF produced | result`.

### 5.1 Master data sanity
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|M-customers|GET /customers|-|200|-|PASS|
|M-vendors|GET /vendors|-|200|-|PASS|
|M-products|GET /products|-|200|-|PASS|
|M-expense-categories|GET /expense-categories|-|200|-|PASS|
|M-wht-types|GET /wht-types|-|200|-|PASS|
|M-business-units|GET /business-units|-|200|-|PASS|
|M-accounts|GET /accounts|activeOnly=true|200|-|PASS|

> co3 is thin: it seeds **0 expense-categories** and **0 WHT-types**. The driver created a SERVICE
> expense category (`SVC`) and a ภ.ง.ด.3 WHT type (`WHT-SVC3`, 3%) via `POST /expense-categories`
> and `POST /wht-types` to exercise the purchase flow. Customer/product/employee were created on the
> first run and reused thereafter.

### 5.2 Sales — Quotation -> Billing Note -> Receipt (non-VAT)
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|S-QT-create|POST /quotations|docDate,validUntilDate,customerId,lines (camelCase)|201|-|PASS|
|S-QT-send|POST /quotations/{id}/send|-|204|-|PASS|
|S-QT-accept|POST /quotations/{id}/accept|-|204|-|PASS|
|S-QT-pdf|GET /quotations/{id}/pdf|-|200|01-quotation.pdf (~112 KB)|PASS|
|S-BN-create|POST /billing-notes|docDate,dueDate,customerId,lines (camelCase)|201|-|PASS|
|S-BN-issue|POST /billing-notes/{id}/issue|-|204|-|PASS|
|S-BN-pdf|GET /billing-notes/{id}/pdf|-|200|02-billing-note.pdf (~115 KB)|PASS|
|S-RC-create|POST /receipts|applications:[{billingNoteId, appliedAmount}]|201|-|PASS|
|S-RC-post|POST /receipts/{id}/post|-|200|doc 06-2026-RC-xxxx|PASS|
|S-RC-pdf|GET /receipts/{id}/pdf|-|200|03-receipt.pdf (~116 KB)|PASS|
|S-RC2-create|POST /receipts|standalone cash bill (applications:[], lines:[...])|201|-|PASS|
|S-RC2-post|POST /receipts/{id}/post|-|200|-|PASS|
|S-RC2-pdf|GET /receipts/{id}/pdf|-|200|04-receipt-cashbill.pdf (~115 KB)|PASS|

**Non-VAT confirmation:** Quotation `tax_amount=0`; Billing Note + both Receipts carry no VAT line.
The chain is Quotation -> Billing Note (ใบแจ้งหนี้) -> Receipt — **no Tax Invoice**, as required.

### 5.3 Tax Invoice (must be unavailable)
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|X-TI-create|POST /tax-invoices|customer + line|400|-|PASS (correctly rejected)|

### 5.4 Purchase + WHT (individual vendor -> ภ.ง.ด.3)
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|M-vend-create / reuse|POST/GET /vendors|INDIVIDUAL vendor, valid Thai tax-id checksum|201/200|-|PASS|
|M-wht-create|POST /wht-types|WHT-SVC3, PND3, 3%|201|-|PASS|
|M-expcat-create|POST /expense-categories|SVC, defaultExpenseAccountId=5xxx|201|-|PASS|
|P-VI-create|POST /vendor-invoices|vendorId,vendorTaxInvoiceNo,lines (camelCase)|201|-|PASS|
|P-VI-post|POST /vendor-invoices/{id}/post|-|422|-|INFO (co3 CoA gap: acct 1170)|
|P-PV-create|POST /payment-vouchers|vendorId,expenseCategoryId,line whtTypeId+whtRate (camelCase)|201|-|PASS|
|P-PV-approve|POST /payment-vouchers/{id}/approve|-|200|-|PASS|
|P-PV-post|POST /payment-vouchers/{id}/post|-|422|-|INFO (co3 CoA gap: acct 1170)|
|P-PV-pdf|GET /payment-vouchers/{id}/pdf|-|200|05-payment-voucher-wht.pdf (~121 KB)|PASS*|
|P-WHT-pdf|GET /wht-certificates/{id}/pdf|-|n/a|-|INFO (cert no. assigned on POST; POST blocked)|

> *The PV PDF was visually inspected: it renders correctly and shows the WHT computation
> (Subtotal 10,000 · VAT 0.00 · **WHT −300.00 (3%)** · Net Paid ฿9,700), but it is stamped
> **"(ร่าง)" = DRAFT** because the PV could not be posted (co3 CoA gap, acct 1170). The inline
> 50ทวิ WHT certificate and its certificate number are produced only on POST, so the standalone
> `/wht-certificates/{id}/pdf` certificate could not be generated this run.

### 5.5 Payroll
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|PR-emp create/reuse|POST/GET /employees|EmployeeCode, BaseSalary 30,000, SSO (camelCase)|201/200|-|PASS|
|PR-run create/reuse|POST/GET /payroll/runs|periodYearMonth=202606, payDate|201/200|-|PASS|
|PR-run-approve|POST /payroll/runs/{id}/approve|-|200/422*|-|PASS|
|PR-run-post|POST /payroll/runs/{id}/post|-|200/422*|-|PASS|
|PR-payslips-pdf|GET /payroll/runs/{id}/payslips/pdf|-|200|07-payslips.zip (~32 KB, ZIP of per-employee PDFs)|PASS|
|PR-pnd1-pdf|GET /payroll/runs/{id}/pnd1/pdf|-|200|08-pnd1.pdf (~317 KB)|PASS|
|PR-sso-pdf|GET /payroll/runs/{id}/sso/pdf|-|200|09-sso-spec1-10.pdf (~252 KB)|PASS|

> *422 on approve/post on later runs = the run for period 202606 was already posted from an earlier
> run (one run per period). The driver reuses the already-posted run; the PDFs render correctly from
> it. The payroll JV is reflected in the trial balance (salary 30,000; SSO employer 875; SSO/PIT
> withholdings).
> **Note:** `/payroll/runs/{id}/payslips/pdf` returns a **ZIP** archive (PK magic) of per-employee
> payslip PDFs, not a single PDF — expected and saved as `.zip`.

### 5.6 WHT tax filings + CIT
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|F-PND3|GET /tax-filings/pnd3/pdf|period=202606|200|10-pnd3.pdf (~262 KB)|PASS|
|F-PND53|GET /tax-filings/pnd53/pdf|period=202606|200|11-pnd53.pdf (~813 KB)|PASS|
|F-PND54|GET /tax-filings/pnd54/pdf|period=202606|200|12-pnd54.pdf (~289 KB)|PASS|
|F-PND51|GET /tax-filings/pnd51/pdf|year=2026|200|13-pnd51.pdf (~278 KB)|PASS|
|F-PND50|GET /tax-filings/pnd50/pdf|year=2026, attestFirstFiling=true, attestBlankSchedules=true|200|14-pnd50.pdf (~1.31 MB)|PASS|

> All five forms **generate as PDFs** for a non-VAT company — correct, since WHT and CIT apply
> regardless of VAT registration. **Caveat (content):** because no Payment Voucher could be posted
> (co3 CoA gap, section 4) and `tax-summary` reports `whtPaidPnd3/53/54 = 0`, the ภ.ง.ด.3/53/54
> forms render with **zero WHT lines** this run — the PDF *generation* passes, but the *content* is
> blank pending a posted PV. Once co3's CoA is seeded, posting the individual-vendor PV will
> populate ภ.ง.ด.3. ภ.ง.ด.51/50 (CIT) render normally.

### 5.7 VAT forms — expected unavailable / zero (see section 3)
| id | endpoint | inputs | HTTP | PDF | result |
|---|---|---|---|---|---|
|I-PND30-pdf|GET /tax-filings/pnd30/pdf|period=202606|200|X-pnd30-blank.pdf|INFO (renders blank/zero)|
|I-PP01-pdf|GET /tax-filings/pp01/pdf|-|200|X-pp01.pdf|INFO|
|I-PP09-pdf|GET /tax-filings/pp09/pdf|-|200|X-pp09.pdf|INFO|
|X-PND30-preview|GET /reports/pnd30/preview|period=202606|404|-|PASS (correctly unavailable)|
|X-VATOUT-register|GET /reports/vat-output-register|year=2026,month=6|404|-|PASS (correctly unavailable)|

### 5.8 Reports (read-only)
| id | endpoint | inputs | HTTP | result | key figures |
|---|---|---|---|---|---|
|R-PL|GET /reports/profit-loss|from=2026-06-01,to=2026-06-30|200|PASS|with `includeUnspecified=true`: **revenue 21,000 · expense 30,875 · netProfit −9,875** (reconciles with TB). Default (`includeUnspecified=false`) returns 0 because sales/payroll are booked to the unspecified BU.|
|R-TB|GET /reports/trial-balance|asOfDate=2026-06-30|200|PASS|**Dr = Cr = 51,875** (balanced); cash 7,000 + bank 14,000 = 21,000 sales; salary 30,000|
|R-BS|GET /reports/balance-sheet|asOfDate=2026-06-30|200|PASS|assets total 21,000 (cash 7,000 + bank 14,000)|
|R-TAX|GET /reports/tax-summary|year=2026|200|PASS|**all VAT fields = 0** (outputVat/inputVat/vatPayable) — non-VAT confirmed|
|R-APAGE|GET /reports/ap-aging|-|200|PASS|empty (no posted AP — VI/PV unposted)|
|R-NUMGAP|GET /reports/number-gaps|year=2026,month=6|200|PASS|no gaps reported|
|R-SALES|GET /reports/sales-summary|from=2026-06-01,to=2026-06-30|200|PASS|grouped by customer; subtotal/vat/total (vat=0)|
|R-EXPCAT|GET /reports/expense-by-category|year=2026|404|INFO|route documented in openapi but not mounted in this build (known gap)|

> Trial balance is fully balanced (Dr=Cr=51,875), confirming every posted document produced a
> balanced GL JV. The 4000 รายได้จากการขาย (sales revenue) net of −21,000 matches the receipts
> posted (no VAT split), and the P&L (with `includeUnspecified=true`) reconciles to it
> (revenue 21,000 − expense 30,875 = netProfit −9,875). Output-VAT account `2151` net = 0 — the
> strongest non-VAT proof at the GL.

---

## 6. Notable findings / recommendations

1. **co3 Chart of Accounts is incomplete (HIGH).** Configured GL account `1170` is absent from
   company 3, blocking ALL purchase posting (Vendor Invoice + Payment Voucher POST -> 422
   `gl.account_missing`). Fix the non-VAT demo seed (`440_seed_nonvat_demo_company.sql`) or the
   GlAccounts appsettings mapping so co3's CoA carries every configured posting account. After that,
   the individual-vendor WHT (ภ.ง.ด.3) chain and `/wht-certificates/{id}/pdf` will complete.

2. **OpenAPI is stale for several request bodies (MEDIUM).** Quotation, Billing Note, Vendor Invoice,
   Payment Voucher, and Receipt endpoints bind **camelCase** JSON, but `openapi.yaml` documents
   `QuotationCreateRequest` / `CreateVendorInvoiceRequest` / `CreatePaymentVoucherRequest` in
   **snake_case** with stale field names (`valid_until`, `uom_id`, `description`). The actual
   contracts are the C# records (`CreateQuotationRequest`/`ChainLineInput`, etc.). Recommend
   regenerating these schemas from the DTOs.

3. **Chart-of-accounts route mismatch (LOW).** The CoA list is served at `GET /accounts?activeOnly=true`,
   not `/chart-of-accounts`. `/reports/expense-by-category` is documented in openapi but returns 404
   in this build. No `ภ.พ.36` form endpoint exists (`/tax-filings/pp36/pdf` -> 404).

6. **`/reports/profit-loss` defaults to `includeUnspecified=false` (LOW).** Documents booked to the
   unspecified business unit (the default when no BU is supplied, as here) are excluded by default,
   so P&L shows 0 unless `includeUnspecified=true` is passed. Trial balance and balance sheet have no
   such default and show the figures directly. Consider defaulting P&L to include unspecified, or
   surfacing the toggle prominently in the UI.

4. **VAT-form PDF fillers do not hard-refuse for non-VAT (LOW / cosmetic).** `pnd30/pp01/pp09` PDFs
   render even for a non-VAT company (blank/zero). The real non-VAT gate is correctly at the data
   layer. Optionally 422 these for non-VAT companies for a tighter UX.

5. **Payslips endpoint returns a ZIP, not a PDF (informational).**
   `/payroll/runs/{id}/payslips/pdf` returns a ZIP archive of per-employee payslip PDFs.

---

## 7. Artifacts

- **Driver script:** `Y:\ClaudePlayground\TEAS-Project\docs\phase6-testing\co3-nonvat\run_phase6.py`
- **Raw results (evidence, per-case JSON):** `Y:\ClaudePlayground\TEAS-Project\docs\phase6-testing\co3-nonvat\results.json`
- **PDFs / archives (16):** `Y:\ClaudePlayground\TEAS-Project\docs\phase6-testing\co3-nonvat\pdfs\`
  - `01-quotation.pdf`, `02-billing-note.pdf`, `03-receipt.pdf`, `04-receipt-cashbill.pdf`,
    `05-payment-voucher-wht.pdf`, `07-payslips.zip`, `08-pnd1.pdf`, `09-sso-spec1-10.pdf`,
    `10-pnd3.pdf`, `11-pnd53.pdf`, `12-pnd54.pdf`, `13-pnd51.pdf`, `14-pnd50.pdf`,
    `X-pnd30-blank.pdf`, `X-pp01.pdf`, `X-pp09.pdf`

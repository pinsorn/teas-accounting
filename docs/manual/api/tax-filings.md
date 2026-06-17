# Tax Filings

แบบยื่นภาษี: ภ.พ.30, ภ.ง.ด.3/53/54/36, ภ.ง.ด.50/51, ภ.พ.01/09, CIT (ภาษีเงินได้นิติบุคคล), e-Tax และทะเบียนภาษีซื้อ/ขาย.

Generation and PDF/batch export of statutory returns. POST routes that compute a return accept a `?mode=preview|finalize` query param — `mode=finalize` additionally requires `tax.filing.finalize` (super-admin bypasses); `preview` (default) needs only `tax.filing.preview`. `period` is `YYYYMM`; `year` is CE. PDFs are print-and-file (no automatic RD submission unless noted). Most routes use `tax.filing.preview`.

## VAT — ภ.พ.30
- `POST /tax-filings/pnd30` — generate the VAT return. **Auth:** `tax.filing.preview` (+`finalize` for `mode=finalize`). Query: `period`, `mode?`. → `200` filing data.
- `GET /tax-filings/pnd30/pdf` — filled ภ.พ.30 PDF. **Auth:** `tax.filing.preview`. Query: `period`. → `application/pdf`.

## WHT — ภ.ง.ด.3 / 53 / 54 / 36
Each POST: **Auth:** `tax.filing.preview` (+`finalize`); Query: `period`, `mode?`; → `200`.
- `POST /tax-filings/pnd3` — ภ.ง.ด.3 (WHT on individuals).
- `POST /tax-filings/pnd53` — ภ.ง.ด.53 (WHT on juristic persons).
- `POST /tax-filings/pnd54` — ภ.ง.ด.54 (WHT/VAT on foreign payments).
- `POST /tax-filings/pnd36` — ภ.พ.36 reverse-charge (auto-JV on finalize).

PDFs (Query: `period`; → `application/pdf`; **Auth:** `tax.filing.preview`):
- `GET /tax-filings/pnd3/pdf`
- `GET /tax-filings/pnd53/pdf`
- `GET /tax-filings/pnd54/pdf`

RD batch-upload files (pipe-delimited UTF-8 `.txt`; Query: `period`; → `text/plain`; **Auth:** `tax.filing.preview`):
- `GET /tax-filings/pnd53/batch-file`
- `GET /tax-filings/pnd3/batch-file`

## CIT — ภ.ง.ด.50 / 51
- `GET /tax-filings/pnd51/pdf` — ภ.ง.ด.51 (mid-year CIT prepayment, ม.67ทวิ method A). **Auth:** `tax.filing.preview`. Query: `year`, `estimatedProfit?`, `whtH1?`, `isSme?`, `fillWorksheet?`, and `attest*` flags (`attestFirstFiling`, `attestNoLossCf`, `attestNoExemption`, `attestNoRateReduction`, `attestNoSurcharge`). → `application/pdf` (422 if the worksheet is requested without clean attestation).
- `POST /tax-filings/pnd51/estimate` — persist the method-A estimate (ม.67ตรี check). **Auth:** `tax.filing.preview`. Query: `year`, `estimatedProfit`, `whtH1?`, `isSme?`. → `200`.
- `GET /tax-filings/pnd50/pdf` — ภ.ง.ด.50 (annual CIT return). **Auth:** `tax.filing.preview`. Query: `year`, `isSme?`, `hasRelatedParty?`, `attestFirstFiling?`, `attestBlankSchedules?`. → `application/pdf` (throws `pnd50.not_attestable` / `pnd50.not_renderable` per the rules).
- `GET /tax-filings/pnd50/preview` — dashboard dry-run (every figure the filler will print + refusal codes). **Auth:** `tax.filing.preview`. Query: `year`, `isSme?`. → `200`.

## VAT registration forms — ภ.พ.01 / ภ.พ.09
Page-1 identity header prefilled from CompanyProfile; substantive answers stay blank (applications, not computed returns).
- `GET /tax-filings/pp01/pdf` — ภ.พ.01. **Auth:** `tax.filing.preview`. → `application/pdf`.
- `GET /tax-filings/pp09/pdf` — ภ.พ.09. **Auth:** `tax.filing.preview`. → `application/pdf`.

## CIT year data (`/tax-filings/cit`)
Phase C-C store: loss carry-forward, ม.65ตรี adjustments, SME profile. Read = `tax.filing.preview`; write = `tax.filing.finalize`.
- `GET /tax-filings/cit/years` — list years. **Auth:** read. → `200`.
- `PUT /tax-filings/cit/years/{year}` — upsert year data. **Auth:** write. Body: `UpsertCitYearRequest`. → `200`.
- `POST /tax-filings/cit/years/{year}/compute` — recompute the year. **Auth:** write. → `200`.
- `GET /tax-filings/cit/adjustments` — list adjustments. **Auth:** read. Query: `year`. → `200`.
- `POST /tax-filings/cit/adjustments` — add adjustment. **Auth:** write. Query: `year`. Body: `UpsertCitAdjustmentRequest`. → `200`.
- `PUT /tax-filings/cit/adjustments/{id}` — edit adjustment. **Auth:** write. Body: `UpsertCitAdjustmentRequest`. → `200`.
- `DELETE /tax-filings/cit/adjustments/{id}` — delete adjustment. **Auth:** write. → `204`.
- `GET /tax-filings/cit/profile` — SME/CIT profile for a year. **Auth:** read. Query: `year`. → `200`.

## Filing history
- `GET /tax-filings` — immutable filing history index. **Auth:** `tax.filing.read`. → `200`.

## e-Tax
- `GET /etax/submissions` — read-only e-Tax submission audit for a tax invoice. **Auth:** `tax.filing.read`. Query: `tax_invoice_id` (long). → `200` (no storage paths projected).

## VAT registers
> The two VAT register endpoints (`/reports/input-vat-register`, `/reports/output-vat-register`) live in the Tax Filings module but are documented in [reports.md](reports.md) alongside the other registers.

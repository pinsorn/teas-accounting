# RD Prep "Format กลาง" WHT text exporter — STATUS: ALREADY SHIPPED (cont.82.1)

> ⚠️ Correction (2026-06-21): an earlier draft of this file said "NOT STARTED". **Wrong** — the exporter
> was built in **cont.82.1** and is wired end-to-end. Verified 2026-06-21: `WhtBatchFormatTests` **7/7 pass**.
> Format research + official spec PDFs: `docs/RD-Forms/rd-prep-efiling-research.md` + `docs/RD-Forms/rd-format-specs/`.

## As-built (where it lives)
- **Pure builder:** `backend/src/Accounting.Infrastructure/TaxFilings/WhtBatchFormat.cs` — FORMAT กลาง V2.0
  (Header 25 fields + Detail 38 fields, pipe `|`, UTF-8 no-BOM, CR/LF, N(15,2), BE dates, ≤3 income triples/SEQ,
  forbidden-char strip). Golden tests `WhtBatchFormatTests.cs` (7).
- **Service:** `WhtBatchExportService.cs` — groups the period's posted 50ทวิ (`WhtCertificate`, Direction='P',
  `AsNoTracking`) by payee; M/O guard throws `wht_batch.missing_tax_id` rather than emit a blank-NID row.
  Tests `WhtBatchExportServiceTests.cs`.
- **Endpoints (live, gated on FilingPreview):** `GET /tax-filings/pnd53/batch-file?period=` and
  `GET /tax-filings/pnd3/batch-file?period=` → `text/plain` `.txt`, RD filename convention.
- **FE:** `frontend/components/tax-filings/WhtFilingClient.tsx` download button + th/en i18n.

## What documents come out (Format กลาง .txt)
- ✅ **ภ.ง.ด.53** (corporate payees) — complete.
- ✅ **ภ.ง.ด.3** (individual payees) — emitted, BUT the RD-mandatory address (AMPHUR/PROVINCE/POSTAL_CODE)
  is **blank** — Vendor master holds only a single free-text address; user fills it in RD Prep.
- ❌ **ภ.ง.ด.54** (foreign ม.70) — deliberately excluded (not an RD Prep batch form; obs 7708).
- Not built as batch text (have other paths or no data): ภ.ง.ด.1/1ก (payroll AcroForm PDF), ภ.ง.ด.2, ภ.พ.30/36.

## RD Prep × TEAS Format-กลาง coverage matrix (2026-06-21)
`-trn` plugin = RD Prep accepts a bulk Format-กลาง `.txt` for that form (verified from the extracted app tree).
No `-trn` ⇒ no text-import target ⇒ a TEAS exporter would produce a file nothing can consume.

| Form | RD Prep `-trn`? | TEAS data source | Verdict |
|---|---|---|---|
| ภ.ง.ด.3 (indiv WHT) | ✅ pnd3-trn | `WhtCertificate` (Individual) | ✅ **SHIPPED** |
| ภ.ง.ด.53 (corp WHT) | ✅ pnd53-trn | `WhtCertificate` (Corporate) | ✅ **SHIPPED** |
| **ภ.พ.30 (VAT return)** | ✅ pp30-trn | `GetPnd30Async`/`VatReportService` (figures exist) | 🟢 **BUILD NOW** |
| ภ.ง.ด.1 / 1ก (payroll WHT) | ✅ | Payroll (`PayrollRun`/`Payslip`) | 🟡 feasible later (today = AcroForm PDF) |
| ภ.ง.ด.3ก (annual WHT) | ✅ | `WhtCertificate` (annual aggregate) | 🟡 feasible later |
| ภ.ง.ด.2 / 2ก (ม.40(3)(4)) | ✅ | — no investment-WHT data | ⚪ no data |
| ภ.ธ.40 (SBT) | ✅ | — SBT out of scope | ⚪ no data |
| ภ.ง.ด.90/91 (PIT annual) | ✅ | — not a PIT filer | ⚪ no data |
| **ภ.ง.ด.50 (CIT annual)** | 🔴 **NO -trn** (form filler only) | `Pnd50FilingService` (PDF) | 🔴 **INFEASIBLE** — RD Prep has no .txt import; filed via its form GUI / e-Filing web |
| **ภ.ง.ด.51 (CIT mid-year)** | 🔴 **no plugin at all** | `Pnd51FilingService` (PDF) | 🔴 **INFEASIBLE** |

**Ham note:** you asked for ภ.พ.30 **and ภ.ง.ด.50** (+ ภ.ง.ด.51). 50 & 51 have **no Format-กลาง import path in RD Prep**
(cit dir = `master`,`pnd50` only; no `-trn`). A `.txt` exporter for them would be dead output. They stay on the existing
PDF path (already shipped). Proceeding with **ภ.พ.30 only**. Re-open 50/51 only if RD adds a bulk path or you want a
different (web-API) route.

## ▶ ภ.พ.30 exporter — ✅ BUILT + overseer-verified (cont.104, 2026-06-21; UNCOMMITTED)
`Pp30BatchFormat.cs` (pure, DETAIL-only/no-H, 16 fields) + `Pp30BatchExportService.cs` (`pp30_batch.no_data`/`missing_address`)
+ `GET /tax-filings/pnd30/batch-file` + FE `reports/pnd30/page.tsx` button + i18n + openapi. Tests 13 (10+3) pass 2× on teas_test.
**Layout authoritatively verified** against RD Prep's own SQLite `offline.db` → `MASTER_PP30_TRN_CONFIG` (START_POINT 0–15): 16 fields,
no header record, `salexp`=ข้อ3 + sales/purchase under/over symmetry, **END_POINT=0 = the 4 amended boxes (ข้อ1.1/1.2/6.1/6.2) emitted empty**.
ข้อ4 & ข้อ8/9 derived from rounded components (importer identity foots). Gates: build 0/0 · 13/13 ×2 · FE tsc 0 · th/en 1472=1472.
Residual: ONE real RD-Prep import test (no-header + ข้อ2/3-empty-when-0 + Branch HQ "0" vs "00000" are SQLite-derived, not portal-proven).

### Original build brief (kept for reference)
Mirror the shipped `WhtBatchFormat`/`WhtBatchExportService` pattern exactly (same muscle, no new deps).
- **Import shape (from `vat/pp30-trn`, source-verified):** ภ.พ.30 is a **per-branch summary** (not a payee list).
  Detail row fields ≈ the form boxes: `SEQ · BRANCH_NO(≤5) · NUMBER(addr no,≤20) · POSTAL_CODE(5) · SALE_AMT[ข้อ1>0] ·
  SALE_OUT_AMT[0%,ข้อ2] · SALE_OVER_AMT[exempt,ข้อ3] · SALE_EXP_AMT[taxable,ข้อ4] · SALE_VAT[output,ข้อ5] ·
  PURCHASE_AMT[ซื้อ] · PURCHASE_OUT_AMT[แจ้งขาด,ข้อ6.1] · PURCHASE_OVER_AMT[แจ้งเกิน,ข้อ6.2] · PURCHASE_VAT[input,ข้อ7] ·
  VAT_AMT[net payable/overpaid,ข้อ8/9]` (+ code/flag fields). Header ≈ WHT header (ID13, NAME, BRANCH_NO, BRANCH_TYPE,
  TAX_YEAR พ.ศ., TAX_MONTH, FILING_TYPE). **Authoritative widths/order/rules = the official `FormatPP30*.pdf`** — the
  subagent MUST build golden values FROM that PDF (download first), not guess. Same file rules as WHT (pipe, UTF-8 no-BOM,
  CR/LF, N(15,2), BE year, forbidden-char strip) — confirm against the PP30 PDF (it may differ).
- **Compliance (the whole point):** ข้อ 1-9 box mapping must be EXACT; ภ.พ.30 is filed **per branch** (one detail row per
  branch) — TEAS `GetPnd30Async` is company-level, so confirm branch handling (single HQ row 000000 unless TEAS tracks
  branch VAT). Net line ข้อ8/9 sign (payable vs overpaid). Reuse `GetPnd30Async` figures — do NOT recompute VAT.
- **Slices:** `Pp30BatchFormat` (pure, Domain/Infra like WhtBatchFormat) + golden tests from the PDF · `Pp30BatchExportService`
  (reads GetPnd30Async, AsNoTracking, M/O guards, `pp30_batch.no_data`) + tests · endpoint `GET /tax-filings/pnd30/batch-file?period=`
  (gate = same FilingPreview perm) · FE button in `WhtFilingClient.tsx`/the ภ.พ.30 page + th/en i18n · openapi (+Sana delta).
- **Gates:** build 0/0 · Domain ≥ baseline · new tests pass **2× on teas_test** · FE tsc 0 · ม glyph clean · no `company_id` leak.

## Genuine residual work (needs a decision, not "just build it")
1. **ภ.ง.ด.3 structured address** — add structured AMPHUR/PROVINCE/POSTAL to Vendor master (schema change → ASK Ham,
   §11) so PND3 files are complete without manual RD-Prep editing. Header flag review (SECTION3 ม.3เตรส default=on)
   is already sound for everyday WHT.
2. **Real-portal validation** — run a generated `.txt` through real RD Prep (validate → `.rdx`) once, end-to-end.
   The golden tests are built FROM the spec PDFs, not yet cross-checked against an actual upload (test header note).
3. **`Tax:Rd:UserId`** (รหัสลงทะเบียน e-Filing) is read from config and blank by default — set per company if needed.
4. (Lower value) extend to other forms — each needs its own Format spec + data mapping.

## Out of scope: emitting `.rdx` directly
`.rdx` = AES-256 zip with a reverse-engineered hardcoded password (`[redacted: hardcoded in the RD Prep binary]`, see research doc) —
technically reproducible but brittle (RD can rotate it), skips RD Prep's validation, compliance-dubious. Stop at
the published `.txt`; RD Prep packs it.

# TEAS ↔ RD/DBD Forms — Gap Review & Build Plan (2026-05-31, for Ham)

> What TEAS already does vs what an SME actually has to *file*, and what we must build to
> close the gap. Source catalog: `INDEX.md` / `REPORT.md` (75 RD PDFs, by Sana).

## TL;DR
TEAS already **computes, stores, and (mock-)submits** the core monthly tax data
(ภ.พ.30, ภ.ง.ด.3/53/54, ภ.พ.36) + VAT registers, and **already produces the 50 ทวิ PDF**.
The real gap is the **fileable OUTPUT artifact** for the return forms: today there is **no
RD-format PDF and no Excel/text upload file** — so a user can compute the numbers in TEAS but
still has to re-key them into the RD portal. That output layer is the build.

---

## A. What exists today (verified in code)
| Capability | State | Where |
|---|---|---|
| ภ.พ.30 (VAT return) compute + store | ✅ | `TaxFilingService`, `/tax-filings/pnd30`, `reports/pnd30` |
| ภ.ง.ด.3 / 53 / 54 (WHT returns) compute + store | ✅ | `WhtFilingService`, `/tax-filings/pnd{3,53,54}`, FE pages |
| ภ.พ.36 (self-assess import-service VAT) | ✅ | `/tax-filings/pnd36`, FE page |
| Input/Output VAT register reports | ✅ | `VatReportService`, `/reports/{in,out}put-vat-register` |
| 50 ทวิ (WHT certificate) **PDF output** | ✅ | `WhtCertificateService` (+ `pdf_storage_path` migration) |
| Submit to RD e-Filing (interface) | ✅ mock | `IRdEfilingClient` → `MockRdEfilingClient` / `RdHttpEfilingClient` |
| e-Tax Invoice (XAdES XML) | ✅ spec'd | `docs/etax-xades-spec.md` |
| missing-50ทวิ report | ✅ | FE `tax-filings/missing-wht-cert` |

## B. The gap — fileable output for the returns (no PDF / no Excel today)
For every return above, TEAS has the numbers but **emits no artifact the user can file**:
- **No RD-layout PDF** (the paper/`พิมพ์แบบ` a user prints or attaches).
- **No Excel / tab-delimited "ใบแนบ" upload file** — RD e-Filing's batch path for ภ.ง.ด.1/2/3/53/54
  uses a prescribed **โปรแกรมโอนย้ายข้อมูล** text/Excel format (one row per payee). This is the
  single highest-value deliverable for a WHT-heavy SME (dozens of payees/month).

## C. Forms with NO compute layer yet (bigger builds)
| Form | Needs | Note |
|---|---|---|
| ภ.ง.ด.1 / 1ก / 2 | **Payroll module** (no employee/salary entities exist — only `User`) | Employee/dividend WHT. Out until payroll is built. |
| ภ.ง.ด.50 / 51 (CIT annual/mid-year) | Full P&L + Balance Sheet + book-to-tax adjustments | Large. Annual. TEAS has GL → feasible later. |
| **DBD งบการเงิน (annual F/S filing)** | Balance Sheet + P&L + notes → **DBD XBRL / e-Filing (DBD SmartForm)** taxonomy | Separate agency (`efiling.dbd.go.th`). Big, annual, distinct from RD. |
| ภ.ธ.40 (SBT) | SBT-specific revenue | Only for SBT businesses (banking/realty) — likely out of SME scope. |

## D. Admin / one-time forms (fill-and-download PDF, low effort)
ภ.พ.01 (+1.1/1.2/02/04/08), ภ.พ.09, ป.ป.01/02, อ.ส.4 — onboarding/correction wizards that
output a filled PDF the user signs & files. Phase 2+.

---

## E. Recommended build plan (priority order)
Maps to Sana's strategy §5.1 (A=bespoke QuestPDF PDF · C=Excel/text upload · D=filled PDF · B=Open API).

1. **P1 — Return PDFs (Strategy A).** Bespoke QuestPDF mirroring the RD layout for the 5 returns
   TEAS already computes: **ภ.พ.30, ภ.ง.ด.3, ภ.ง.ด.53, ภ.ง.ด.54, ภ.พ.36**. Reuse the QuestPDF
   stack already built for documents + 50ทวิ. Layout reference = `docs/RD-Forms/<form>/*.pdf`.
   → A user can print/attach a correct form immediately.
2. **P2 — WHT batch upload file (Strategy C).** Generate the RD **โปรแกรมโอนย้ายข้อมูล** tab-delimited
   text (and/or Excel) for **ภ.ง.ด.3 / 53 / 54** — one row per payee from the period's PV/WHT data.
   This is what makes monthly WHT filing actually fast. (ภ.ง.ด.1 needs payroll — defer.)
3. **P3 — Open API submission (Strategy B).** Wire `RdHttpEfilingClient` to the real RD Open API
   for ภ.พ.30 + WHT (replaces mock). Config-gated (Tier env). Depends on RD Service-Provider auth.
4. **P4 — Admin form wizards (Strategy D).** ภ.พ.01 / ภ.พ.09 / ป.ป.01-02 → filled-PDF download.
5. **P5 — Annual (large, separate efforts):** CIT ภ.ง.ด.50/51 from GL; **DBD F/S XBRL** filing.
   Each is its own mini-project (book-to-tax mapping / DBD taxonomy).

## F. Decisions for Ham (drive priority)
- **Q1 — Start where?** Recommend **P1 return PDFs** (fast, reuses QuestPDF, immediate value) →
  then **P2 WHT batch upload**. OR jump to P2 first if portal upload matters more than paper.
- **Q2 — Output format for returns:** PDF only (P1), Excel/text upload only (P2), or both? (Both is
  the eventual goal; pick the first.)
- **Q3 — Payroll:** in scope soon (unlocks ภ.ง.ด.1/1ก/2), or explicitly out for now?
- **Q4 — DBD F/S + CIT ภ.ง.ด.50:** this sprint, or park as a later annual-filing epic?
- **Q5 — Form version pinning:** pin to 2568 PDFs (per REPORT §5.3) in config — confirm.

## F.1 LOCKED (Ham, 2026-05-31)
- **Start = P2** — RD batch-upload file (Excel/text "โปรแกรมโอนย้ายข้อมูล") for **ภ.ง.ด.3 / 53 / 54**,
  one row per payee from the period's PV/WHT data, for portal upload.
- **Also plan Payroll** — design the payroll module (employee/salary/PIT-WHT) that unlocks
  ภ.ง.ด.1 / 1ก / 2; brainstorm/spec it in parallel (build later).
- P1 return-PDFs, P3 Open API, P4 admin forms, P5 CIT/DBD = after.

### Next-session kickoff (P2)
1. **Research the RD โปรแกรมโอนย้ายข้อมูล format FIRST** — fixed field order, delimiter (tab/`|`),
   encoding (TIS-620 vs UTF-8), header/trailer, decimal/date conventions, per ภ.ง.ด.3/53/54.
   It is NOT in the downloaded PDFs (those are the printed forms). Source: RD e-Filing
   "โปรแกรมโอนย้ายข้อมูล" / มาตรฐานการนำส่งข้อมูล spec. Confirm exact layout before coding.
2. Build a `WhtBatchExportService` (Infrastructure) producing the file from `WhtFilingService`
   data; endpoint `/tax-filings/pnd{3,53,54}/batch-file`; FE download button on each filing page.
   Encoding-correct (likely TIS-620); one row per 50ทวิ/payee line. Tests with `TestIds`.
3. Payroll design: separate spec in `docs/superpowers/specs/`.

## G. Notes
- e-Tax XML, sales/purchase VAT registers, and the tax-invoice/CN/DN layouts are **not** RD "forms"
  (REPORT §3) — already handled or bespoke.
- อ.ส.9 e-Stamp + DBD F/S are **portal/agency-specific** (no offline PDF to fill) — submission, not
  a printable form.
- Form PDFs to mirror live in `docs/RD-Forms/<code>/` with per-form `_meta.md` field mapping.

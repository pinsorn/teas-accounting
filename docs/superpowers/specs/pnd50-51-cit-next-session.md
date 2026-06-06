# ภ.ง.ด.50 + ภ.ง.ด.51 (Corporate Income Tax) — NEXT SESSION kickoff

> Read order: CLAUDE.md → progress.md (top) → this. Payroll + SSO + WHT + VAT (ภ.พ.30) all shipped.
> This is **Corporate Income Tax (CIT)** — the year-end pieces the gap-review flagged as missing.
> **Recon already done** (this spec is grounded, not guessed): source PDFs probed, reuse surface mapped.

## Goal
Fill the official RD AcroForm PDFs from TEAS data:
1. **ภ.ง.ด.51** (ม.67ทวิ) — mid-year CIT **prepayment**, due 2 months after the first 6 months of the FY
   (calendar FY → due 31 Aug; +8 days e-Filing). Simpler — do this FIRST.
2. **ภ.ง.ด.50** (ม.68/69) — annual CIT return, due **150 days** after FY-end (calendar FY → 30 May; +8d
   e-Filing) + **5 ใบแนบ** + disclosure form (ม.71ทวิ related-party). Bigger.

Output = **filled official AcroForm PDF** (the RD forms ARE fillable — see §"Confirmed"), exactly like
ภ.ง.ด.1 / 50ทวิ via `RdAcroFormFiller`. (NOT the SSO flat-PDF problem — those forms are fillable.)

## Confirmed by recon (don't re-discover)
- **All source PDFs are fillable AcroForms** (PdfSharp/ASCII probe: `pnd50_050369.pdf`, `pnd51_020768.pdf`,
  `pnd50_attach1.pdf` all have `/AcroForm` + `/Fields` + widgets 120/192). → `RdAcroFormFiller` `/Rect`-driven
  playbook transfers verbatim. Templates already downloaded in `docs/RD-Forms/pnd50/` + `pnd51/`.
- **CIT base is derivable** from the shipped `FinancialReportService.ProfitLossAsync` (net accounting profit
  for a date range) + `TrialBalanceAsync`.
- **FY-end is known:** `Company.FiscalYearStartMonth` (short, default 1 = calendar). H1 / FY boundaries derive
  from it — do NOT assume calendar year.
- **WHT credit is available:** `IWhtReceivableReportService` (WHT suffered, from 50ทวิ received) → the ภ.ง.ด.50
  tax-credit line.
- **CIT rates** (`pnd50/_meta.md`): general **20% flat**; **SME** (paid-up ≤ ฿5M AND revenue ≤ ฿30M):
  0–300k = 0% · 300k–3M = 15% · >3M = 20%.
- **Deadlines + penalty:** ภ.ง.ด.51 under-estimate (actual > estimate +25%) → **20% เบี้ยปรับ** of the shortfall.

## Reuse surface (study these before building)
- `Pdf/RdAcroFormFiller` (overlay+flatten, Thai-safe Sarabun, comb fields, `RdRadio` for same-name groups).
- `Pdf/Pnd1FormFiller` + `Payroll/Pnd1FilingService` — the **exact pattern to mirror** (field map → filler →
  filing service → endpoint → FE button). `docs/.../pnd1-acroform-fill-2026-05-31.md` documents the method.
- `Reports/FinancialReportService` (P&L + trial balance) + `Reports/WhtReceivableReportService`.
- `Domain/Payroll/ThaiPitCalculator` + `PitSchedule` — the **template for a pure, golden-tested `CitCalculator`
  + `CitRateSchedule`** (config/seed, §4.6; NOT hardcoded — like the SSO/PIT rates).
- `Entities/Tax/TaxFiling` + `TaxFilings/TaxFilingService` + `IRdEfilingClient` (mock RD submit, as ภ.พ.30).

## Gaps to build (NOT in place)
1. **`CitCalculator` (pure, golden):** accounting profit → ± tax adjustments → taxable profit → apply
   general/SME schedule → CIT; minus credits (ภ.ง.ด.51 prepay + WHT suffered). Mirror `ThaiPitCalculator`.
2. **Tax-adjustment model (ม.65ทวิ/65ตรี):** accounting≠taxable profit (non-deductibles, exempt income,
   depreciation diffs, donations cap, etc.). Cannot be fully auto-derived from GL → **needs an adjustment-entry
   model** (line: legal-ref code + label + ± amount) layered on the auto P&L net profit. **The hard part.**
3. **Balance-sheet report (งบแสดงฐานะการเงิน):** TEAS has only P&L + TB, NOT a balance sheet. ภ.ง.ด.50's
   financial-position section + DBD submission need it. Build a `BalanceSheetAsync` (assets/liabilities/equity
   from GL account classes) OR accept manual entry of that page (decision below).
4. **SME inputs:** `Company` has no **paid-up / registered capital** field; revenue test = from P&L. Add the
   capital field (+ migration) to auto-classify SME vs general.
5. **Loss carry-forward (ม.65ตรี, 5-year):** no store. Needs a carry-in figure (manual field or a computed
   prior-year store).
6. **ภ.ง.ด.51 estimate input + penalty:** the full-year profit ESTIMATE is a user input (method A); store it
   so the year-end ภ.ง.ด.50 can compute the under-estimate 20% penalty.

## Methodology (proven on ภ.ง.ด.1 — reuse verbatim)
1. Throwaway PdfSharp `/Fields` dump test (names + `/Rect` grouped by row + radio widget order) → delete after.
2. Map generic `Text{block}.{idx}` → boxes via marker-render OR `/Rect` coords cross-checked against a
   Playwright screenshot (serve the PDF over a tiny node http server — `file:` is blocked).
3. Write `Pdf/Templates/pnd51_fieldmap.md` / `pnd50_fieldmap.md`, embed templates as `EmbeddedResource`, build
   the filler. Iterate visually with Ham (50ทวิ took ~3 rounds; ภ.ง.ด.50 is large → expect more).
4. `CitCalculator` golden-tested against worked examples in `pnd50_instructions.pdf` / `pnd51_instructions.pdf`.

## Build plan (phased — recommend ภ.ง.ด.51 first)
**Phase C-A — `CitCalculator` + `CitRateSchedule`** (pure Domain, golden tests): general 20% + SME progressive;
taxable = accountingProfit + Σadjustments − lossCarryForward; tax − credits. No DB.
**Phase C-B — ภ.ง.ด.51** (smaller, establishes the pipeline): estimate input → `CitCalculator` (×50%) →
`Pnd51FormFiller` (probe fields first) → `Pnd51FilingService` (P&L H1 + Company header) → endpoint
`GET /tax-filings/pnd51/pdf?year=YYYY` → FE button. Store the estimate for the year-end penalty check.
**Phase C-C — ภ.ง.ด.50 main form:** adjustment model + `CitCalculator` full → `Pnd50FormFiller` → service
(P&L FY + WHT credit + ภ.ง.ด.51 prepay + loss c/f) → endpoint + FE.
**Phase C-D — ภ.ง.ด.50 attachments (5) + disclosure (ม.71ทวิ)** + balance-sheet section. Largest; do last.

## LOCKED DECISIONS (Ham, 2026-06-01) — build to these
1. **Tax adjustments → manual adjustment-entry UI** on top of auto P&L net profit (line: legal-ref code + label + ± amount).
2. **Scope/order → ภ.ง.ด.51 first → ภ.ง.ด.50 main → attachments/disclosure** (phased, as below).
3. **Balance sheet → build a real `BalanceSheetAsync`** report now (GL account classes; reusable for 50 + DBD).
4. **SME → add `Company` paid-up/registered-capital field (+migration), auto-detect** (capital ≤5M ∧ revenue ≤30M from P&L).
5. **Loss carry-forward → per-year summary store**, recorded at year-end close (profit/loss accumulated per FY),
   **override-able** (manual override of any year's figure). Computed default + manual override. 5-yr expiry tracked.
6. **ภ.ง.ด.51 → method A only** (full-year estimate ×50%). Method B (actual half-year, public co/CPA) skipped v1.
7. **e-Filing → PDF-fill only** v1. No RD submission record yet.
8. **Audited FS / DBD → out of scope.** TEAS emits RD forms only; statutory audited FS + DBD = external CPA.

## OPEN DECISIONS (original — now resolved above; kept for trace) (§11)
1. **Tax adjustments:** manual adjustment-entry UI on top of the auto P&L net profit (**recommended v1** — GL
   can't express ม.65ตรี add-backs) vs attempt partial GL auto-mapping. → defines the core data model.
2. **Scope/order:** ภ.ง.ด.51 first, then ภ.ง.ด.50 main, then attachments/disclosure? (recommended) Or 50 main only?
3. **Balance sheet:** build a real `BalanceSheetAsync` report now (reusable, needed for 50 + DBD) vs manual
   entry of the financial-position page for v1.
4. **SME classification:** add a paid-up-capital field to `Company` + auto-detect (capital ≤5M ∧ revenue ≤30M),
   or a manual "is SME" flag? (revenue comes from P&L either way.)
5. **Loss carry-forward:** manual carry-in field per filing vs a computed prior-year store (5-yr expiry).
6. **ภ.ง.ด.51 method:** A (estimate ×50%) only for v1? (B = actual-half-year, public cos + CPA — rare, skip.)
7. **e-Filing:** PDF-fill only, or also wire an RD e-Filing submission record (like ภ.พ.30's mock)? PDF first.
8. **Audited financials / DBD filing:** out of TEAS scope (external CPA + DBD) — confirm we only emit the RD
   forms, not the statutory audited FS.

## Compliance anchors (cite in code/tests)
- ม.65 (net-profit basis) · ม.65ทวิ (computation conditions) · ม.65ตรี (non-deductibles) · ม.67ทวิ (ภ.ง.ด.51)
  · ม.68/69 (ภ.ง.ด.50) · ม.71ทวิ (disclosure / related-party).
- Rates: general 20%; SME 0/15/20 (paid-up ≤5M ∧ revenue ≤30M). Deadlines: 50 = 150d post-FY; 51 = 2mo post-H1
  (+8d e-Filing each). Loss c/f = 5 years. ภ.ง.ด.51 under-estimate (>25%) → 20% penalty on shortfall.
- Source PDFs (downloaded): `docs/RD-Forms/pnd50/` (main + attach1–5 + disclosure_form/explanatory +
  instructions) · `docs/RD-Forms/pnd51/` (main + instructions). RD pages: rd.go.th/62375.html.

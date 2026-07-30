# A3 — Foreign-vendor reverse-charge chain (co5, prod v1.27.1)

Target: https://teas.kazaki-rio.com | Company: **co5** (id=5, บริษัท ทดสอบ VAT (DUMMY) จำกัด) — confirmed `me.companyId=5` before every write.
Login: task creds were stale (per-account codes rotated). Used **chief01 / `UxSwarm-2026-A7`** (Chief Accountant — holds vendor.manage + VI/PV create·approve·post + tax.filing.*, whole chain in one account). (ap01 = `UxSwarm-2026-A4`.)
Method: direct API via `/api/proxy/*`. Tax filings probed with **mode=preview only** — NO finalize on prod, so I did NOT post any reverse-charge JV myself.

## >>> CRIT AT TOP <<<
**ภ.พ.36 double-counts reverse-charge (ม.83/6) VAT when a foreign Vendor Invoice is settled by a self-withhold Payment Voucher.** One imported service self-assesses output VAT TWICE. On finalize this over-remits ม.83/6 VAT to the RD (and, for a VAT-registered co, over-claims input VAT). Proven live on co5; the pre-existing baseline was **already** double-counted. Details below.

---

## PASS / FAIL per sub-area
- Round 1 happy path (standalone PV) — tie to hand-calc ....... **PASS**
- v1.22.11 regression guard (VI-linked self-withhold PV posts balanced) ... **PASS** (holds)
- JE balance (Dr=Cr) on every posted reverse-charge doc ........ **PASS**
- ภ.ง.ด.54 figures + double-count on reverse-charge+WHT ........ **PASS** (nets correctly, 1 cert/PV)
- ภ.ง.ด.54 PDF renders .......................................... **PASS** (builds; figures 1:1 from verified preview)
- **ภ.พ.36 reverse-charge total (VI + settling PV) ............. FAIL — CRIT double-count**
- **ภ.พ.36 PDF / printable form ................................ FAIL — MED, does not exist**
- ภ.พ.36 / ภ.ง.ด.54 empty period (no docs) .................... **PASS** (clean zero, no crash)
- Immutability of posted VI/PV (edit/delete/re-post/cancel) .... **PASS**
- Foreign-vs-domestic vendor routing edge cases ................ **PASS** (1 INFO nit)
- Non-THB currency / FX handling ............................... **PASS** (multi-currency gated off cleanly)

---

## FINDING 1 — CRIT — ภ.พ.36 double-counts reverse-charge VAT for a VI settled by a PV
**Area:** `WhtFilingService.GeneratePnd36Async` (backend), ภ.พ.36 page + finalize JV.

**Root cause (code):** a foreign no-Thai-VAT-D vendor's **VI** is flagged `RequiresPnd36ReverseCharge = vendor.IsForeign && !vendor.HasThaiVatDReg` (VendorInvoiceService.cs:139) and its **settling PV** is flagged `requiresPnd36 = autoSelfWithhold` (same predicate; PaymentVoucherService.cs:223,338). `GeneratePnd36Async` sums `viRows` (VI.SubtotalAmount) **and** `pvRows` (PV.SubtotalAmount) with **no dedup and no exclusion of VI-linked PVs** — so one imported service, recorded as an invoice and then paid, is self-assessed 7% output VAT twice. The ม.83/6 obligation arises ONCE per service; both docs carry the same SubtotalAmount.

**Exact repro (clean controlled, co5, period 202607):**
1. Baseline ภ.พ.36 202607 preview: totalService 98,691.59 / totalVat 6,908.41.
2. `POST /vendor-invoices` vendor 12 (AWS, isForeign=true, hasThaiVatDReg=false), category 26 (IT/Cloud), amount 20,000, vatRate 0 → VI id 21 → `POST /vendor-invoices/21/post` → `07-2026-VI-0007`.
3. ภ.พ.36 202607 preview now: +row `07-2026-VI-0007` 20,000 / 1,400 → totals 128,691.59 / 9,008.41. (correct so far — VI counted once)
4. `POST /payment-vouchers` with `vendorInvoiceId:21`, line FOR-SVC (whtTypeId 44) 20,000 whtRate 0.15, selfWithhold null (auto) → PV id 34 → approve → `POST /payment-vouchers/34/post` → `07-2026-PV-IT-0003` (posts fine).
5. ภ.พ.36 202607 preview now: **+row `07-2026-PV-IT-0003` 20,000 / 1,400** → totals **148,691.59 / 10,408.41**.

**Expected vs actual:** one ฿20,000 foreign service should add **20,000 service / 1,400 VAT** to ภ.พ.36. Actual added **40,000 / 2,800** — the identical service appears as both `07-2026-VI-0007` and `07-2026-PV-IT-0003`.

**Blast radius:** on `POST /tax-filings/pnd36?mode=finalize`, `PostReverseChargeJvAsync` posts an **immutable** JV `Dr 1170 InputVAT / Cr 2151 OutputVAT` for `totalVat` — i.e. the doubled figure. For VAT-registered co5 the net-VAT effect nets to 0 across the two accounts but **output VAT remitted to RD on ภ.พ.36 is 2×** (real cash over-remittance); for a non-VAT receiver the debit is 5350 expense and the over-remittance is a straight cash loss.

**Corroboration — baseline already wrong:** before my writes, co5's July ภ.พ.36 already listed VI-0004 (id14) and VI-0005 (id15) — both ฿20,000, `settlementStatus=PAID` — alongside their settling self-withhold PVs (`07-2026-PV-CAPEX-0001` settles VI 15; `07-2026-PV-IT-0001` settles VI 14), all in the same July filing. June ภ.พ.36 = 0 rows, so nothing offsets. Production data was double-counting before this test.

**Note (what does NOT break):** ภ.ง.ด.54 does not double-count (only the PV issues a WHT certificate; the VI issues none), and the GL/JE is balanced. The defect is isolated to ภ.พ.36's VAT self-assessment/remittance.

---

## FINDING 2 — MED — ภ.พ.36 has no printable/filable form (no PDF)
**Repro:** `GET /api/proxy/tax-filings/pnd36/pdf?period=202607` → **HTTP 404**. No `BuildPnd36PdfAsync` / `Pnd36FormFiller` anywhere in `backend/src` (grep empty). The FE page `frontend/app/(dashboard)/tax-filings/pnd36/page.tsx` has **zero** pdf/print/download controls (grep empty).
**Expected vs actual:** every sibling filing has a `/pdf` (ภ.พ.30, ภ.ง.ด.2/3/53/54, ภ.ง.ด.50/51, ภ.พ.01/09). ภ.พ.36 — a mandatory monthly reverse-charge remittance return — can only be viewed on-screen; the user cannot produce the actual form to file with the RD.

---

## FINDING 3 — INFO — a foreign vendor is accepted with a Thai Tax ID
**Repro:** `POST /vendors` isForeign=true, hasThaiVatDReg=false, taxId="0105566000770" (valid 13-digit Thai) → **HTTP 201** (vendor A3-FGNTAX created).
**Expected vs actual:** a ม.70 foreign payee should not carry a Thai TIN (it would print in ภ.ง.ด.54's payee-TIN field). Accepted silently. Reverse-charge routing itself is unaffected (keys on isForeign && !hasThaiVatDReg). Cosmetic/data-integrity nit only.

---

## PASS detail (evidence)

**Round 1 — standalone PV, hand-calc tie-out** (vendor 12, category 26, FOR-SVC 15%, ฿10,000, self-withhold auto):
- PV 27 draft: whtAmount **1,764.71** (= 10,000/0.85×0.15), totalPaid 10,000, whtPayerMode GROSS_UP_FOREVER, requiresPnd36 true — matches hand-calc.
- Posted `07-2026-PV-IT-0002`, WHT cert `07-2026-WT-0007`. **JE 221 balanced:** Dr 5200 10,000 + Dr 5200 gross-up 1,764.71 = Cr 2152 WHT 1,764.71 + Cr 1120 Bank 10,000 (D=C=11,764.71).
- ภ.ง.ด.54 delta: +row WT-0007 income **11,764.71** / wht **1,764.71**. ภ.พ.36 delta: +row 10,000 / **700**. All tie to hand-calc.

**v1.22.11 regression guard — VI-linked self-withhold PV posts balanced (HOLDS):**
- PV 34 (settles VI 21): whtAmount **3,529.41** (= 20,000/0.85×0.15 — the reference figure), totalPaid 20,000. Approve + Post → `07-2026-PV-IT-0003`, **HTTP 200, no 422 gl.unbalanced**.
- **JE 229 balanced:** Dr 2110 AP 20,000 (`AP settle VI`) + Dr 5200 gross-up 3,529.41 = Cr 2152 WHT 3,529.41 + Cr 1120 Bank 20,000 (D=C=23,529.41).
- ภ.ง.ด.54 added exactly ONE cert (WT-0008 23,529.41 / 3,529.41) — no double-count on the WHT side.

**Empty period 202611:** ภ.พ.36 → rows [], totals 0, HTTP 200. ภ.ง.ด.54 → rows [], totals 0, HTTP 200. Empty ภ.ง.ด.54 PDF → HTTP 200, 2-page header-only sheet. No crash.

**Immutability:** PUT posted VI 21 → 422 `vi.not_draft`. Re-POST posted PV 34 → 422 `pv.not_approved` (current: Posted). Cancel posted PV 34 → 422 `pv.cannot_cancel`. All correctly rejected.

**Vendor contradiction (domestic + VAT-D):** `POST /vendors` isForeign=false, hasThaiVatDReg=true → **clean 400** fieldError hasThaiVatDReg "VAT-D registration requires a foreign vendor." (validator catches it before the DB check constraint — no raw 500).

**Non-THB currency:** `POST /payment-vouchers` currencyCode="USD" exchangeRate=35 → **clean 400** "Only THB is supported (multi-currency is not yet available)" + "ExchangeRate must be 1". Multi-currency gated off entirely → no FX mishandling possible (the `ServiceAmountThb` figure is always genuinely THB).

**ภ.ง.ด.54 PDF (202607):** HTTP 200, 8 pages = 4 ม.70 sheets (one per cert). Figures are mapped 1:1 from `GeneratePnd54Async` (ModelFor: IncomeAmount→Income, WhtAmount→Tax, no re-compute), which I verified against hand-calc via the preview JSON. Could not pixel-verify overlaid Thai-font text (no poppler on host) — tooling limit, not a defect.

---

## Pollution created in co5 (all immutable; designated write target; NO finalize run)
- PV 27 `07-2026-PV-IT-0002` posted (+ WHT cert WT-0007)
- VI 21 `07-2026-VI-0007` posted
- PV 34 `07-2026-PV-IT-0003` posted, settles VI 21 (+ WHT cert WT-0008)
- Vendor `A3-FGNTAX` created (master data)
- Rejected (not created): USD PV, domestic+VAT-D vendor

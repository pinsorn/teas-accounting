# R2 — compliance & filings (documents that go to the government)

Release plan: `PLAN-fix-breakit-v1271.md` §R2 · Findings: `VERDICT-breakit-v1271.md` (C2, C4, H16, H13,
H8, H9 + the filing MEDIUMs) · Ham's binding answers: `specs/doc-lifecycle-cancel-reissue-backdate.md` §6.
Predecessor: `specs/fix-breakit-r1-ledger-integrity.md` — **shipped as v1.28.0, live.**

Status: **DESIGN COMPLETE — pre-flight probes (WP-0) and two Ham checkpoints gate the code WPs.**

---

## 0. Headline

R2 fixes eight defects on documents filed with the Revenue Department and the Social Security Office,
plus deletes the "customer has paid" button that R1 turned into an active money hole. Three things the
design discovered that change the shape of the work:

1. **C4 is not a three-field fix — the ภ.ง.ด.1 field map has rotted away from the code, in both
   directions.** `pnd1_fieldmap.md:21` says the sheet-count field is `Text1.21`; `Pnd1FormFiller.cs:96`
   writes `Text1.19`. The map says `Text1.11 = ตำบล/แขวง`, `Text1.13 = จังหวัด`; the code writes
   `Text1.11 = Street`, `Text1.13 = District`, `Text1.14 = Province`. And **`pnd1a` has no field map file
   at all** — `Pnd1aFormFiller` was written from an ad-hoc `/Rect` dump that was never committed. The
   whole main page of both forms is unvalidated, not just the totals row. So C4 = *decode the template
   factually → Ham validates a render → then fix*, and the decode covers every field.

2. **C2's correct side is the PAYMENT VOUCHER, and that is a filing-PERIOD change, not just an amount
   change.** Under ม.83/6 the reverse-charge liability arises on payment to the overseas provider, so
   ภ.พ.36 for a VI dated June that is paid in July belongs in **July's** return. Fixing the double-count
   by dropping the VI side is therefore a tax decision with a real consequence, not a dedup. **Escalated
   — §10 E1.**

3. **"สปส.1-10 ส่วนที่ 2 prints blank" is a re-discovery of a known, already-decided blocker, and the
   swarm walked into the exact trap the earlier investigation documented.** Measured, not assumed: pages
   3 and 4 of `sps110_main.pdf` both carry the printed title **`สปส.1-10/1`**
   (`docs/RD-Forms/_fills/_sps110_p3_words.txt:115`, `_sps110_p4_words.txt:114`) — a *different* form
   (branch-consolidation, rows are per-BRANCH). The template contains no ส่วนที่ 2 at all. Ham already
   decided the workaround (O11-alt, on-screen schedule) and it **shipped**
   (`PayrollEndpoints.cs:131-134`). R2 does **not** re-design O11. What R2 owes is a decision on the two
   foreign blank pages riding in the filing packet — **§10 E4.**

Everything else in R2 is a guard the sibling code path already implements correctly and this one never
adopted — the VERDICT's own "who else does this?" pattern. Each fix below names its sibling.

---

## 1. Facts established in code

Every line reference below was read, not inferred. Line numbers are as of `main` after v1.28.0
(2026-08-12). **If a line number does not match what you find, stop and re-locate by symbol — do not
edit by line number.**

### 1.1 C2 — ภ.พ.36 double-count (VERIFIED)

| Fact | Where |
|---|---|
| `GeneratePnd36Async` builds `viRows` (posted VIs with `RequiresPnd36ReverseCharge` in the period) and `pvRows` (same for PVs), then `.Concat(...)` with **no dedup key of any kind**. | `WhtFilingService.cs:242-266` |
| VAT = `decimal.Round(SubtotalAmount * 0.07m, 2)` per row, on **both** sides. | `WhtFilingService.cs:257-264` |
| The VI flag is set unconditionally for a foreign vendor without Thai VAT-D: `RequiresPnd36ReverseCharge = vendor.IsForeign && !vendor.HasThaiVatDReg` | `VendorInvoiceService.cs:139` |
| The PV flag is set from `autoSelfWithhold` — the same foreign/no-VAT-D derivation — and applies to **both** standalone and VI-linked PVs (`PaymentVoucherService.cs:333-339` states this explicitly). | `PaymentVoucherService.cs:339`, `:359` |
| A PV carries `VendorInvoiceId` (nullable) — the link to the VI it settles exists on the PV. | `PaymentVoucherService.cs:356` |
| On `mode=finalize`, `PostReverseChargeJvAsync` posts an **immutable** JV `Dr 1170 (or the irrecoverable-VAT expense on a non-VAT company) / Cr 2151` for `totalVat`, then `TaxFilingStore.FinalizeAsync` records the filing. | `WhtFilingService.cs:274-295`, `:303-334` |
| Re-finalize is already blocked (`tax_filing.already_finalized`) — so a bad finalize **cannot be corrected in-place**. | `WhtFilingService.cs:277-280` |
| `RequiresPnd36ReverseCharge` has exactly **one** consumer outside the entities/configs/migrations: this query. It is also surfaced read-only on the VI DTO. | `PurchaseReadDtos.cs:41`, `PaymentVoucherService.Read.cs:133` |
| **`PostReverseChargeJvAsync` is NOT VAT-mode-gated and must never be** — it branches internally (`vatMode ? "1170" : IrrecoverableVatExpenseAccount`) because a non-VAT company still owes reverse-charge VAT under ม.83/6. | `WhtFilingService.cs:303-323` |

**Footgun, folded in — do NOT drive-by fix it:** `troubles-wiki.md:67` — the reverse-charge JV lands on
`TodayInBangkok()`, not the filing period date, because `JournalService.CreateDraftAsync` silently
discards `req.DocDate`. That is a **known, deliberately deferred** defect
(`specs/manual-jv-and-coa-management.md` §B0 puts it out of scope: changing the pin would silently move
every existing ภ.พ.36 JV's date). **Out of scope for R2 — §9.**

### 1.2 C4 — ภ.ง.ด.1 / 1ก totals on the wrong row (VERIFIED, reproduced on 2 companies)

| Fact | Where |
|---|---|
| Monthly: summary row 1 (ม.40(1) กรณีทั่วไป) written to `Text2.1/2.2/2.3`; the "รวม" triple written to `Text2.18/19/20`; `Text2.22` = รวมทั้งสิ้น. Comment claims `Text2.18/19/20` is "Row 6 รวม". | `Pnd1FormFiller.cs:97-105` |
| Annual 1ก: same claim, same triple. | `Pnd1aFormFiller.cs:65-67` |
| Observed on prod (2 companies, 3 monthly runs + the annual): the totals land on **row 5 = ม.40(2) ผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย**, and "6. รวม" / "8. รวมยอด" print blank. | `VERDICT-breakit-v1271.md:97-110` |
| The field map's own header carries **"Ham visual-validation pending"** and names the summary-table column order as a high-risk spot. | `Templates/pnd1_fieldmap.md:5-7` |
| **The map and the code already disagree elsewhere**: map `Text1.21` = จำนวนใบแนบ vs code `Text1.19`; map `Text1.11` = ตำบล/แขวง vs code `Text1.11` = Street, and the whole ตำบล/อำเภอ/จังหวัด block is shifted by one. | `pnd1_fieldmap.md:17-21` vs `Pnd1FormFiller.cs:91-96` |
| **`pnd1a` has no field map file.** `Templates/` holds `pnd1_fieldmap.md`, `pnd30_fieldmap.md`, `pnd53_fieldmap.md`, `wht_50tawi_fieldmap.md` — nothing for 1ก. The class doc says the map was "self-decoded from /Rect (`_Pnd1aDump`)"; that dump is not in the repo. | `ls Templates/`, `Pnd1aFormFiller.cs:22-23` |
| A positional text extractor exists and is already used on tax templates: `KPlusPdfTextExtractor.Extract(Stream, password)` → `PositionedWord(PageNo, Text, Left, Right, Top, Bottom)`. | `Accounting.Infrastructure/Bank/Pdf/KPlusPdfTextExtractor.cs`, used at `TaxFormFillDiagnostic.cs:33` |
| A `TEAS_DIAG=1`-gated diagnostic harness exists that fills every box of a form and writes the PDF to `docs/RD-Forms/_fills/`. It is `[SkippableFact]`, so CI is unaffected. | `TaxFormFillDiagnostic.cs:10-47` |
| The renderer is overlay+flatten, so a filled value becomes page content and **is** extractable with coordinates. | `RdAcroFormFiller` (used by both fillers) |

**Two PDF-testing footguns, folded in (`troubles-wiki.md`):**
- **`:847` — re-rendering the same RD PDF is not byte-deterministic.** Never assert on PDF bytes. Compare
  extracted page text / positions.
- **`:859` (+ its 2026-07-30 escalation) — PDF text extraction drops Thai combining marks, and the
  artefact is not even stable run-to-run** (one run emits U+0020 where a mark was dropped, another
  U+0000, same input, same renderer). **Before any equality comparison: strip every Unicode
  nonspacing-mark (category `Mn`) and every `char.IsControl` character, then collapse whitespace runs
  (`Regex.Replace(s, @"\s+", " ")`).** Anchors chosen in §3.2 are mark-free by construction, but apply
  the normalisation anyway.
- **`:1029` — `blob:` PDF tabs are not screenshot-able in the browser sandbox.** The working route for
  getting a prod-rendered PDF in front of a human: `javascript_tool` → `fetch(...)` + anchor-click
  download → GUID `.tmp` in `C:\Users\ham_c\Downloads` → copy/rename to `.pdf` → **the `Read` tool
  renders PDF pages as images.** That is how the Tier-4 leg produces Ham's picture.

### 1.3 H16 — non-VAT company can render AND finalize a ภ.พ.30 (VERIFIED)

| Fact | Where |
|---|---|
| `GeneratePnd30Async` checks authentication only. **No `VatMode` check anywhere in the method.** | `TaxFilingService.cs:25-100` |
| It is the **single chokepoint for all four ภ.พ.30 surfaces**: JSON (`POST /tax-filings/pnd30`), PDF (`BuildPnd30PdfAsync:108` calls it), batch file (`Pp30BatchExportService.cs:32` calls it), and finalize (same method, `mode` param). **One guard closes all four.** | `TaxFilingEndpoints.cs:39-55`, `:145-150`; `Pp30BatchExportService.cs:32` |
| The sibling that does it right: `TaxInvoiceService.EnsureVatRegisteredAsync` → `DomainException("ti.non_vat_blocked", …)`, described in its own comment as "the single chokepoint for ALL TI creation". | `TaxInvoiceService.cs:74-81` |
| co7 (non-VAT) now carries `filingId 1`, a finalized PND30 with the real tax id on a 290 KB PDF. | `VERDICT-breakit-v1271.md:178` |
| `VatMode` comes from `ICompanyTaxConfigService.GetAsync(ct)` — per-company, DB-backed, not env config. | `ICompanyTaxConfigService.cs:13`, `CompanyTaxConfigService.cs:11-14` |

### 1.4 H13 — filing artifacts render from an unapproved DRAFT payroll run (VERIFIED)

`PayrollRun.Status` is the shared `DocumentStatus` enum — **`Draft, Approved, Posted, Voided`**
(`DocumentStatus.cs:9-15`). There is **no `Submitted` and no `Paid` member**; "paid" is a sub-state of
Posted stamped by `PayrollRun.MarkPaid` (`PayrollRun.cs:117-125`, `IsPaid => PaidAt is not null` at
`:62-63`). `JournalId` (**not** `JournalEntryId`) is assigned in exactly one place —
`PayrollRunService.cs:226`, inside `PostAsync` — so it is **null for every Draft and Approved run**.
The period is a single string `PeriodYearMonth` (`yyyymm`, CE) — `PayrollRun.cs:22`; there are no
`PeriodYear`/`PeriodMonth` properties on the entity.

| Artifact | Loader | Status filter today |
|---|---|---|
| ภ.ง.ด.1 monthly PDF | `Pnd1FilingService.BuildPnd1MonthlyAsync` — `Pnd1FilingService.cs:16-22` | **NONE** — only `r.PayrollRunId == runId` + "has payslips" |
| สปส.1-10 batch `.txt` | `SsoFilingService.BuildMonthlyAsync` — `SsoFilingService.cs:20-26` → `BuildMonthlyFileAsync:73-77` | **NONE** |
| สปส.1-10 ส่วนที่ 1 PDF | same loader → `BuildMonthlyPdfAsync:70-71` | **NONE** |
| สปส.1-10 ส่วนที่ 2 on-screen JSON | same loader → `PayrollEndpoints.cs:131-134` | **NONE** |
| Payslip PDF + run ZIP | `PayslipPdfService.LoadRunAsync` — `PayslipPdfService.cs:52-55` | **NONE** |
| ภ.ง.ด.1ก annual | `Pnd1FilingService.cs:79-82` | ✅ `p.Run!.Status == DocumentStatus.Posted` |
| 50ทวิ per employee | `Pnd1FilingService.cs:118-120` | ✅ `p.Run!.Status == DocumentStatus.Posted` |

**The doc comments already claim the guard that isn't there** — `SsoFilingService.cs:10-11` says "from a
posted `PayrollRun`", `ISsoFilingService.cs:14` says "for a posted run". Fixing the code makes the
comments true; do not delete them.

Two consequences worth stating: `PayslipPdfService.cs:78` prints `run.DocNo`, which is **null until
Post** — a draft payslip prints a blank document number. And `BuildMonthlyAsync` is the single loader
for all three SSO artifacts, so where the guard goes matters (§3.4).

### 1.5 H8 / H9 — สปส.1-10 employer account and payee names (VERIFIED)

| Fact | Where |
|---|---|
| A blank employer account is emitted as **`"0000000000"`**: `Digits(m.EmployerAccountNo, 10)` left-pads with `'0'`. No guard, no warning. | `SpsBatchFormat.cs:67`, `Digits` at `:122-127` |
| **Asymmetry**: the PDF prints *nothing* for a blank account (`Comb` early-returns) while the file prints zeros. Neither throws. | `Sps110FormFiller.cs:65-67`, `:41-42` |
| The value can legitimately be null: profile value → config fallback → `null`. | `SsoFilingService.cs:64-66`, `PayrollOptions.cs:20-23` |
| Entry validation is **conditional** — 10 digits *when present*; null/blank passes. | `CompanyProfileDtos.cs:148-154` |
| Encoder: `Encoding.GetEncoding(874).GetBytes(...)` with the **default replacement fallback** — any non-cp874 character silently becomes `?`. This is the only `GetEncoding(874)` in `backend/src`. | `SpsBatchFormat.cs:54` (code page const at `:32`) |
| **Second silent-corruption class, same file:** `Txt` truncates over-long values with no error — ชื่อ 30, ชื่อสกุล 35, ชื่อสถานประกอบการ 45 — while the employee validator permits **150**. | `SpsBatchFormat.cs:115-119` vs `EmployeeDtos.cs:80-81` |
| There is **no character validation on employee names anywhere**: `NotEmpty()` + `MaximumLength(150)` only; no regex, no charset, no encodability check. `TitleTh` has no rules at all. | `EmployeeDtos.cs:80-81`, `:101-102`, `:115-116` |
| Names reach ภ.ง.ด.1 through `Pnd1FilingService.NameMapAsync` (`:164-174`), 1ก through the same, 50ทวิ at `:136`, SSO through `SsoFilingService.cs:85-90`. All read `Employee.FirstNameTh/LastNameTh`, with a payslip-snapshot fallback that splits on the last space. | as cited |
| **The sibling that does it right, twice**: `Pp30BatchExportService.cs:39-52` → `pp30_batch.missing_address` (accumulates a `missing` list, names the Thai fields, ends with the remediation); `WhtBatchExportService.cs:72-80` → `wht_batch.missing_tax_id` (**names the offending payees**, `Take(10)`). The SSO exporter has neither. | as cited |

### 1.6 สปส.1-10 pages 3–4 — settled by measurement

`docs/RD-Forms/_fills/_sps110_p3_words.txt:115` and `_sps110_p4_words.txt:114` both extract the printed
title **`สปส.1-10/1`**. p3 also carries `…ำระเงินสมทบรวมของสาขา)` (`:110`) — the branch-consolidation
wording. So `sps110_main.pdf` = [p1 ส่วนที่ 1 · p2 คำชี้แจง · p3–p4 สปส.1-10/1]. **There is no ส่วนที่ 2
page in the template**, `sps110_boxes.json` holds 20 keys all belonging to p1, and
`docs/RD-Forms/sps1-10/fieldmap/sps110_map.md` maps page 1 only (its source PDF is literally
`sps1_10_part1.pdf`). The "Fact 6" paragraph in `specs/sps110-part2-o11.md` that reads p3/p4 as ส่วนที่ 2
is **superseded** — it is the trap that spec's own blocker banner warns about ("page 2 of the PDF ≠
ส่วนที่ 2 of the form"). O11-alt shipped on the correct reading (`PayrollEndpoints.cs:126-134`,
`PayrollDtos.cs:48-50`).

### 1.7 Feature C — delete "customer has paid" (VERIFIED, exhaustive sweep)

**The line numbers in `doc-lifecycle-cancel-reissue-backdate.md` §3.1 are stale (pre-R1). Use these.**

```csharp
// BillingNoteService.cs:360-371 — the whole method
public async Task MarkSettledAsync(long id, CancellationToken ct)
{
    Auth();
    var bn = await LoadAsync(id, ct);
    if (bn.Status != BillingNoteStatus.Issued)
        throw new DomainException("billing_note.bad_status",
            "Only an Issued billing note can be marked Settled.");
    bn.Status = BillingNoteStatus.Settled;
    bn.SettledAt = clock.UtcNow;
    activity.Record("BillingNote", bn.BillingNoteId, bn.DocNo, bn.CompanyId, "Settled", "Issued", "Settled");
    await db.SaveChangesAsync(ct);
}
```

- **It posts nothing.** No `gl.` call, no period guard, no transaction, no `JournalEntryId` write —
  contrast `IssueAsync` (tx at `:306`, period guard `:317`, `gl.PostBillingNoteAsync` at `:335`).
- **`BillingNote` has no `AmountPaid` and no `PaymentStatus`.** Settlement is binary
  `Issued`/`Settled` (`SalesChainStatus.cs:36-42`); there is no `SettledBy`. Partial payment is not
  representable on a BN — `ReceiptService.cs:541-543` says so explicitly ("a BN carries no AmountPaid
  column, so 'already paid' is a SUM over `sales.receipt_applications` of POSTED receipts for that BN").
- **`Status` is persisted as an UPPER string** (`SalesChainConfigurations.cs:174-176`, file header
  comment at `:9`) — i.e. `status = 'SETTLED'`. Confirm with `SELECT DISTINCT status` before writing the
  probe's `WHERE`.
- **R1/C6 raised the stakes**: `BillingNote.JournalEntryId` (`BillingNote.cs:65`) is set at Issue for a
  non-VAT company and is "the single source of truth for 'has this invoice accrued'". A `MarkSettled`
  after R1 leaves AR debited and never credited.
- **The only discriminator between a manual settle and a receipt settle is the activity-log `note`**:
  `MarkSettledAsync` omits it (null); `ReceiptService` writes `note: $"ชำระครบจากใบเสร็จ {rcNo}"`
  (`ReceiptService.cs:530-531`, `:575-576`). Same action string `"Settled"`, same from/to. That is a
  weak signal — the probe in WP-0 uses the **money** test as primary and this as corroboration.

Full consumer list (dead vs survives) is the WP-7 checklist, §5.

### 1.8 Environment / process footguns

- **`TEAS_TEST_PG` dies between PowerShell calls** — a skipped test is not a passing test. Check the
  skip count against baseline (`memory: teas-test-pg-env-per-shell`).
- **`TEAS_REPO_ROOT`** must be set or `RbacAuthMap`/`RbacMatrix` tests throw "Could not locate the TEAS
  repo root" from a `subst` drive. Both diagnostics in this spec also read `TEAS_REPO_ROOT`
  (`TaxFormFillDiagnostic.cs:28-29`).
- **Payroll tests self-exhaust a finite year pool on the shared `teas_test`** (`troubles-wiki.md:1087`) —
  "No employees are active in this period" out of nowhere. Relevant to WP-4/WP-5's payroll tests.
- **Only ONE worker may run the integration suite at a time** — the test DB is shared. This includes the
  Tier-3 gate runner.
- **Do not hand-edit `docs/_site/**` or `docs/rbac/endpoint-permission-map.generated.md`** — generated.
- **Windows/PowerShell 5.1**: no `&&`; write files with `-Encoding utf8`.
- **Thai glyph check before any commit**: the Bengali letter at **U+09AE** creeps into Thai strings in
  place of Thai `ม` (U+0E21) — they look near-identical. Every WP here writes Thai error messages, so
  grep the diff for U+09AE. (The character is deliberately NOT written literally in this spec, so a
  repo-wide grep stays clean.) In Git Bash `grep -P` fails with "supports only unibyte and UTF-8
  locales" (`troubles-wiki.md:51`) — use the workaround recorded there.
- **`teas_test` fixture applies each SQL seed ONCE** (tracked); a new seed cannot assume earlier ones
  replay. No new seeds are needed in R2, but do not add one casually.

---

## 2. Consumer sweep

R2 widens no enum and adds no discriminator value. Three seams are **narrowed** (a previously-accepted
input becomes refused), and one surface is **deleted**. Both need the same sweep discipline.

### 2.1 Seam narrowed: ภ.พ.30 becomes VAT-registrant-only (WP-3)

| Consumer (file:line) | What it does | Disposition |
|---|---|---|
| `TaxFilingEndpoints.cs:39-47` `POST /tax-filings/pnd30` | preview + finalize | **Covered** — calls `GeneratePnd30Async` |
| `TaxFilingEndpoints.cs:50-55` `GET /tax-filings/pnd30/pdf` | filled PDF | **Covered** — `BuildPnd30PdfAsync:108` calls it |
| `TaxFilingEndpoints.cs:145-150` `GET /tax-filings/pnd30/batch-file` | RD batch file | **Covered** — `Pp30BatchExportService.cs:32` calls it |
| `Pnd30DeadlineAlertJob.cs` | scheduled ภ.พ.30 deadline alert | **VERIFY, then extend** — if it calls `GeneratePnd30Async` it will now throw for non-VAT companies and must catch/skip instead of erroring the job. Implementer reads it and reports before changing anything. |
| `TaxFilingEndpoints.cs:267-275` input/output VAT registers | VAT registers | **Deliberately skipped** — read-only reports, not a filed document. Out of scope (§9). |
| `TaxFilingEndpoints.cs:153-161` `POST /tax-filings/pnd36` | ภ.พ.36 | **MUST NOT be gated.** ม.83/6 binds non-VAT payers too; `PostReverseChargeJvAsync:306-323` branches on `VatMode` precisely because of this. A "consistency fix" here is a defect. |
| FE `frontend/app/(dashboard)/tax-filings/` | the ภ.พ.30 page | **Already FE-gated** (VERDICT: "the UI gate is front-end only"). No FE change needed; the BE guard is the real one. |
| `frontend/lib/i18n/problems.ts` | maps DomainException code → Thai toast | **EXTEND** — add `pp30.non_vat_blocked`. Without it the raw English `detail` surfaces on an all-Thai UI (that file's own header explains this). |

### 2.2 Seam narrowed: filing artifacts require a Posted run (WP-4)

| Consumer (file:line) | Disposition |
|---|---|
| `Pnd1FilingService.BuildPnd1MonthlyAsync:16-22` | **EXTEND** — add the guard |
| `SsoFilingService.BuildMonthlyPdfAsync:70-71` | **EXTEND** |
| `SsoFilingService.BuildMonthlyFileAsync:73-77` | **EXTEND** |
| `PayrollEndpoints.cs:131-134` `sso-schedule` JSON | **EXTEND** — transcribing unposted numbers onto the paper form is the same hole |
| `PayslipPdfService.BuildAsync:24` / `BuildRunZipAsync:33` | **PRODUCT CALL — §10 E3.** Recommended: refuse from `Draft`, allow `Approved`+`Posted`. |
| `Pnd1FilingService.cs:79-82` (1ก) and `:118-120` (50ทวิ) | **Verified already correct** → add a regression test, do not add a second guard |
| `frontend/app/(dashboard)/payroll/[id]/page.tsx` | **VERIFY + EXTEND** — the buttons that hit these routes must be disabled (not just error) for a non-Posted run. Implementer reports what the page does today before changing it. |
| `frontend/lib/i18n/problems.ts` | **EXTEND** — new code(s) |

### 2.3 Seam narrowed: a payee name must be filable (WP-5)

| Consumer (file:line) | Disposition |
|---|---|
| `SpsBatchFormat.DetailRecord:83-95` (ชื่อ 30 / ชื่อสกุล 35) | **EXTEND** via a guard upstream in `SsoFilingService` |
| `SpsBatchFormat.HeaderRecord:63-79` (ชื่อสถานประกอบการ 45) | **EXTEND** — same class, employer name |
| `Pnd1FilingService.NameMapAsync:164-174` (ภ.ง.ด.1 + 1ก) | **EXTEND** |
| `Pnd1FilingService.cs:136` (50ทวิ) | **EXTEND** |
| `Sps110FormFiller.Fill` | **Skip** — shrink-to-fit, no truncation, no encoding step |
| `PayslipPdfService.cs:79` (snapshot name on the payslip) | **Deliberately skipped** — internal document, not a government filing |
| `EmployeeDtos.cs:80-81` create/update validators | **DEFER — §10 E5.** Entry-time validation is the better long-term fix but widens blast radius into master data + FE. Recommended as an R4 follow-up with a `troubles-wiki` entry. |
| `frontend/lib/i18n/problems.ts` | **EXTEND** |

### 2.4 Surface deleted: `MarkSettledAsync` (WP-7)

Exhaustive; every path was grepped (`MarkSettled`, `mark-settled`, `markSettled`, `bn-mark-settled`)
across backend src + tests, frontend, MCP tools, scripts, db, docs.

**DEAD — must be removed or rewritten**

| Consumer | Disposition |
|---|---|
| `BillingNoteService.cs:360-371` | DELETE the method |
| `BillingNoteDtos.cs:76` (`IBillingNoteService`) | DELETE the declaration |
| `BillingNoteEndpoints.cs:49-51` (`POST /billing-notes/{id}/mark-settled`, 204, `sales.billing_note.manage`) | DELETE the mapping. **The permission itself is NOT orphaned** — it still gates create/update/delete/issue/cancel/create-tax-invoice at `:25/:34/:38/:42/:47/:57`. Do not touch `Permissions.cs` or the RBAC catalog. |
| `frontend/app/(dashboard)/invoices/[id]/page.tsx:47` state, `:131-133` button, `:207-216` dialog | DELETE all three |
| `frontend/messages/th.json:100-103` + `:1872`, `en.json:100-103` + `:1872` | DELETE 3 keys per locale (`billingNote.markSettled`, `confirmAction.bnMarkSettled.title/.warning`). `bnMarkSettled` is the **last** member of the `confirmAction` object — mind the trailing comma on the preceding sibling. |
| `frontend/e2e/billing-note-flow.spec.ts:8-34` (test `'billing note: create → issue → mark settled'`) | **REWRITE to settle via a posted receipt**, not delete. (It also never interacts with the confirm dialog it opens — a pre-existing hole.) |
| `McpDocumentChainTests.cs:919-950` (`MarkSettled_on_an_issued_billing_note_flips_to_settled_h3_repro`) | DELETE — it tests only the deleted path |
| `McpDocumentChainTests.cs:524` (arrange step inside `Dedup_guard_rejects_a_receipt_on_a_settled_billing_note`) | **REWRITE to reach Settled through a full posted receipt** — the real transition. The assertion target (`rc.invoice_already_settled`) survives. Never seed the target state. |
| `docs/api/openapi.yaml:811-818` | DELETE the path block. (It also documents `200`; the endpoint returns `204` — the drift dies with it.) |
| `docs/manual/api/sales.md:75` | DELETE the line |
| Stale comments: `BillingNoteService.cs:19` · `ReceiptService.cs:499-500` · `page.tsx:22`, `:44`, `:109-112`, `:122-123` | UPDATE the wording (they name "manual MarkSettled" / "ยืนยันชำระครบแล้ว" as a live path) |

**SURVIVES — do not touch**: the `Settled` enum member (`SalesChainStatus.cs:40`) · `BillingNote.Status`
/ `SettledAt` (`:21`, `:57`) · both `ReceiptService` settle blocks (`:497-535`, `:545-580`) ·
`rc.invoice_already_settled` (`ReceiptService.cs:218-219`) · the MCP **read** guard
(`TeasMcpTools.cs:530-535` + description `:479`) · `useBillingNoteAction` (`queries.ts:1840-1852`, shared
with issue/cancel) · `run()` (`page.tsx:50-57`) · Settled-gated FE buttons (`page.tsx:113`, `:124`) ·
`StatusBadge.tsx:43` · status labels `th/en.json:2042` · `NonVatArBackfillService.cs:115`, `:179` ·
`docs/_site/**` + `endpoint-permission-map.generated.md` (regenerate, never hand-edit).

---

## 3. Design

### 3.0 Release-wide constraints

- **NO EF migration and NO SqlScript anywhere in R2.** C2 is query-side only — `RequiresPnd36ReverseCharge`
  stays on both entities, nothing is dropped or backfilled. Both prod probes (WP-0) are **read-only**.
  If any WP appears to need a schema change, **stop and re-spec**.
- **Both real tenants (co2 Repttown, co3) are non-VAT**, with a January fiscal year
  (`PLAN-fix-breakit-v1271.md:113-127`). So ภ.พ.30 (WP-3) protects them by refusing a document they must
  never file, while **ภ.ง.ด.1 / สปส.1-10 (WP-4, WP-5) affect every company that runs payroll — including
  both real tenants.** WP-4/WP-5 are the highest-blast-radius work in this release.
- **ภ.พ.36 (WP-2) is NOT VAT-registrant-only** — see §1.1 and §2.1.
- Every new error code needs a `frontend/lib/i18n/problems.ts` entry (TH dict; EN falls through to the
  backend `detail` by design — that file's header explains it).

### 3.1 C2 — ภ.พ.36 declares the payment, once (WP-2)

**INVARIANT I1 — state this before reading the rule.**
> For one foreign service of ฿X, across the entire document chain, ภ.พ.36 declares service ฿X **exactly
> once** and remits `round(0.07·X, 2)` **exactly once** — in exactly one filing period — regardless of
> the chain's shape: (a) VI + settling PV, (b) standalone PV with no VI, (c) VI that is never paid.
> Cash paid to the vendor is unchanged by this fix. The reverse-charge JV's Dr/Cr accounts and the
> non-VAT branch are unchanged.

**THE RULE (binding):**

```
ภ.พ.36 rows come from POSTED PAYMENT VOUCHERS ONLY.
  → GeneratePnd36Async drops `viRows` entirely; `rows` = pvRows only.
  → VendorInvoice.RequiresPnd36ReverseCharge stays on the entity and stays on the read DTO
    as an INFORMATIONAL flag ("this invoice will trigger ภ.พ.36 when paid"). It is never
    a source of a filing row again.
```

Checked against I1:
- (a) VI + settling PV → the PV alone produces one row. ✔ once.
- (b) standalone PV → one row. ✔ once. (The old code also covered this — via the PV side, which is why
  dropping the PV side instead would be wrong.)
- (c) VI never paid → **zero rows.** ✔ Correct: no payment, no ม.83/6 liability yet.

**Why the PV and not the VI.** The reverse-charge liability under ม.83/6 arises when the payer *pays*
the overseas service provider, and ภ.พ.36 is due within 7 days of the end of the month **of payment**.
The PV is the payment. Dropping the PV side instead would declare an invoice that has not been paid and
would miss every standalone PV.

**The consequence that makes this a tax decision, not a dedup.** A VI dated 2026-06-20 settled by a PV
dated 2026-07-03 moves from June's ภ.พ.36 to **July's**. The amount is right in both designs; the
**period changes**. That is a filing-period change on a return already being filed monthly. → **§10 E1.
Do not dispatch WP-2 before E1 is answered.**

**Rejected alternative — keep both flags and dedup on `pv.VendorInvoiceId`.** One line, and wrong: it
fixes only the same-period case. VI in June / PV in July still yields two declarations (June from the
VI, July from the PV) — the double-count survives, split across months, where it is harder to see. It
also declares unpaid invoices. Not adopted.

**Blocking verification the implementer owes before touching the query** (paste the evidence into the
attempt log): grep for **every** path that can settle a `VendorInvoice`. If any settlement route exists
that does not create a `PaymentVoucher`, PV-only under-declares and this design is wrong →
**stop and re-spec.** (Expected result: `PaymentVoucher` is the only one — `PaymentVoucherService.cs:356`
carries `VendorInvoiceId`, and a `vi.pv_exists` guard blocks a second PV. Prove it, do not assume it.)

**Do NOT touch** `PostReverseChargeJvAsync`. Its accounts, its `VatMode` branch, its
`tax_filing.already_finalized` guard and its (wrong) doc date are all out of scope — the doc-date issue
is `troubles-wiki.md:67`, deliberately deferred elsewhere.

**Already-double-counted history** — see §10 E2. It is not an engineering call.

### 3.2 C4 — decode the template, let Ham see it, then fix (WP-1)

**INVARIANT I2.**
> The ภ.ง.ด.1 / ภ.ง.ด.1ก money and count values are **already correct and must not change by one satang**.
> This work moves **where they print**, nothing else. After the fix, the multiset of money/count strings
> printed on the main page is **identical** to before; only their coordinates differ.

**INVARIANT I3.**
> The payroll totals print on the row labelled **รวม**, and on no row belonging to an income category the
> data does not fall under. Salary stays on row 1 (ม.40(1) กรณีทั่วไป). No summary row is filled that the
> data does not populate.

**This is a four-stage work package with a mandatory human checkpoint between B and C. Guessing
coordinates is forbidden — that unvalidated map is what produced the defect.**

**Stage A — decode (no production code changes).**
Add two `TEAS_DIAG=1`-gated `[SkippableFact]`s to `TaxFormFillDiagnostic.cs`, alongside the existing
`Fill_every_box_*` cases:
1. **Marker render.** For `pnd1_main.pdf` and `pnd1a_main.pdf`: enumerate every AcroForm text field on
   the template and fill each with **its own field id as the value** (`Text2.18` prints the literal
   string `Text2.18`). Render + flatten through `RdAcroFormFiller` exactly as production does.
2. **Extract.** Run `KPlusPdfTextExtractor.Extract` over the marker render and dump every
   `PositionedWord` (`Text`, `Left`, `Right`, `Top`, `Bottom`) ordered by `Top` then `Left` to
   `docs/RD-Forms/_fills/_pnd1_marker_words.txt` / `_pnd1a_marker_words.txt`.
3. **Also dump the blank template's own words** (the printed labels/row numbers) to
   `_pnd1_template_words.txt` / `_pnd1a_template_words.txt`.

Joining the two dumps by `Top` band gives a **factual** field-id → row-label map. Deliverable: rewrite
`Templates/pnd1_fieldmap.md` and create `Templates/pnd1a_fieldmap.md`, each with the measured
coordinates, the derived row map, and the dump filename as evidence. **Delete the "Ham
visual-validation pending" note only after stage B passes** — replace it with the validation date.

Stage A must also settle, in writing:
- which triple is row 6 **รวม** on each form, and which triple is row 5 (ม.40(2) ผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย);
- whether the sheet-count field is `Text1.19` (code) or `Text1.21` (map);
- whether the address block (`Text1.3`…`Text1.15`) as written by the code matches the template;
- for `pnd1_main`, the identity of `Text2.21` (เงินเพิ่ม) and `Text2.22` (รวมทั้งสิ้น), which the code
  writes at `:105`;
- for `pnd1a_main`, whether a `รวมทั้งสิ้น` equivalent exists and is currently unfilled.

**Note the numbering is not uniformly three-per-row**: the map claims row 2 carries two extra fields
(`Text2.4` เลขที่ / `Text2.5` ลงวันที่). The observed defect (totals on row 5) is not explained by a naive
3-per-row model — which is exactly why this is measured, not reasoned.

**Stage B — Ham's visual gate (BLOCKING, human).**
Produce a **fully-synthetic** render of both forms with every box populated with a distinguishable value
(the `TaxFormFillDiagnostic` `Fill_every_box_*` style — a full render shows box positions far better
than sparse real data). Add `Fill_every_box_pnd1` / `Fill_every_box_pnd1a` cases if they do not exist.
Output goes to `docs/RD-Forms/_fills/_diag_pnd1.pdf` / `_diag_pnd1a.pdf`.

Get the picture in front of Ham: the `Read` tool renders PDF pages as images (`pages` parameter). Fable
reads the diagnostic PDFs and shows Ham the main page of each form. **Ham confirms, box by box, that
each value sits where its label says.** His answer is recorded verbatim in this spec's attempt log with
the date. **Stage C does not start until it is.**

**Stage C — fix.** Apply the confirmed map to `Pnd1FormFiller.MainFields` (`:73-108`) and
`Pnd1aFormFiller.MainFields` (`:53-68`). Fix every field the decode found wrong, not only the totals
triple. Keep the values themselves untouched (I2). Replace each stale inline comment with a pointer to
the field map and the validation date.

**Stage D — the regression test that makes it durable** (new file, e.g.
`backend/tests/Accounting.Api.Tests/Payroll/Pnd1FormRowPlacementTests.cs`):

- **T5 (geometry).** Render a monthly ภ.ง.ด.1 with a known set of employees. Extract positioned words.
  Locate the label token **`รวม`** — after normalisation it is *exactly* `รวม`, while row 8's
  `รวมทั้งสิ้น` normalises to something else, so exact equality disambiguates them. Assert the total
  income string's `Top` is within ±6 pt of that label's `Top`.
- **T6 (negative).** Assert **no** money/count token sits in the `Top` band of the row-5 label. Anchor
  on `ประเทศไทย` (mark-free) or on the row's printed numeral, whichever the stage-A dump shows is
  reliably extracted. **RED-first requirement: the worker pastes the actual extracted tokens for both
  anchors into the attempt log BEFORE writing either assertion.** If an anchor is not present in the
  extraction, say so and pick another from the dump — never invent one.
- **T7 (I2, value-preservation) — TWO artefacts, do not conflate them:**
  - **(a) one-time verification, at fix time.** Dump the extracted numeric tokens of the main page
    *before* Stage C and *after*, and assert by inspection that the multisets are identical. Both dumps
    are pasted into §12 as the I2 evidence. This is **not** a CI test — after the fix lands there is no
    pre-fix renderer to compare against.
  - **(b) the durable regression.** Assert the numeric tokens printed on the main page equal the values
    computed from the model input (count, Σincome, Σtax, formatted `#,##0.00` in `th-TH`). This runs
    forever and catches a future value change, which is what I2 actually protects.
- **T8.** T5, T6 and T7(b) again for ภ.ง.ด.1ก.

Apply the `troubles-wiki.md:859` normalisation (strip `Mn` + control chars, collapse whitespace) before
every string comparison. **Never assert on PDF bytes** (`troubles-wiki.md:847`).

**Stage E — Tier-4.** After deploy, re-render ภ.ง.ด.1 from **real prod data through the public domain**
(`troubles-wiki.md:1029` route: fetch + anchor-click download → rename → `Read`), and Ham confirms once
more on real numbers. A release is not done until this reports.

### 3.3 H16 — ภ.พ.30 is for VAT registrants only (WP-3)

One guard, first statement in `GeneratePnd30Async` after the auth check (`TaxFilingService.cs:28-29`),
mirroring `TaxInvoiceService.cs:74-81` verbatim in shape:

```csharp
// R2/H16 — a company with no VAT registration must never produce, let alone finalize, a ภ.พ.30.
// Single chokepoint: the JSON preview, BuildPnd30PdfAsync (:108), the RD batch file
// (Pp30BatchExportService.cs:32) and mode=finalize all funnel through this method.
// Mirrors TaxInvoiceService.EnsureVatRegisteredAsync (ti.non_vat_blocked, TaxInvoiceService.cs:74-81).
// NOTE: ภ.พ.36 must NOT get this guard — ม.83/6 reverse charge binds non-VAT payers too
// (WhtFilingService.PostReverseChargeJvAsync:306-323 branches on VatMode for exactly that reason).
if (!(await taxCfg.GetAsync(ct)).VatMode)
    throw new DomainException("pp30.non_vat_blocked",
        "บริษัทที่ไม่ได้จดทะเบียนภาษีมูลค่าเพิ่มไม่ต้องยื่น ภ.พ.30 " +
        "[VAT-not-registered companies do not file ภ.พ.30 (ภ.พ.30 is a VAT return). " +
        "If this company is VAT-registered, set VAT mode in company tax settings first.]");
```

**All four surfaces are blocked, preview included** — the VERDICT's harm is the fully identity-filled
PDF bearing the real 13-digit tax id, which a preview produces. A company that registers for VAT flips
`VatMode` and is unblocked immediately, so there is no lockout. `→ 422` via the middleware default.

Also required:
- `frontend/lib/i18n/problems.ts` entry for `pp30.non_vat_blocked`.
- **`Pnd30DeadlineAlertJob`**: read it first. If it calls `GeneratePnd30Async` for every company, it will
  now throw on non-VAT tenants — it must skip them, not fail. Report what you find before changing it.
- **Backing out co7's bogus `filingId 1`**: co7 is scheduled for wipe+reseed after R4
  (`PLAN-fix-breakit-v1271.md:183-185`), which clears it. **No prod data operation in R2.** WP-0's probe
  P2 confirms no *real* tenant carries one; if it does, that is an escalation, not a drive-by fix.

### 3.4 H13 — a filing artifact only comes from a posted run (WP-4)

**INVARIANT I4.**
> Every artifact that can be signed and handed to the RD or the SSO is derived from a payroll run whose
> journal entry exists. `PayrollRun.JournalId` is non-null **iff** `Status == Posted`
> (`PayrollRunService.cs:226` is the only assignment) — so "Posted" and "has ledger backing" are the same
> statement, and the guard may test either. Test `Status`, and assert `JournalId != null` in the test to
> pin the equivalence.

One shared guard, called by each artifact builder:

```csharp
// R2/H13 — an RD/SSO filing artifact must never be produced from an unposted run: a signable
// return with no ledger behind it (VERDICT H13; co5 rendered ภ.ง.ด.1 for ฿1,103.02 of PIT from a
// run with journalId:null, then the run was deleted). ภ.ง.ด.1ก and 50ทวิ already do this —
// Pnd1FilingService.cs:79-82 and :118-120 filter p.Run!.Status == DocumentStatus.Posted.
// This makes the three ungated paths match, and makes SsoFilingService's own doc comment
// ("from a posted PayrollRun", :10-11) true.
if (run.Status != DocumentStatus.Posted)
    throw new DomainException("payroll.not_posted_for_filing",
        $"งวดเงินเดือน {run.PeriodYearMonth} ยังไม่ได้ลงบัญชี (สถานะ {run.Status}) — " +
        $"ต้องอนุมัติและลงบัญชีก่อนจึงจะออกแบบยื่นได้ " +
        $"[Payroll run {run.PeriodYearMonth} is {run.Status}; a filing artifact requires a Posted run.]");
```

Call sites:
1. `Pnd1FilingService.BuildPnd1MonthlyAsync` (`:16-22`) — after the load, before the payslip-count check.
2. `SsoFilingService.BuildMonthlyPdfAsync` (`:70-71`).
3. `SsoFilingService.BuildMonthlyFileAsync` (`:73-77`).
4. The `sso-schedule` route (`PayrollEndpoints.cs:131-134`).

**Where the SSO guard goes matters.** `BuildMonthlyAsync` (`:20-26`) is the shared loader for all three
SSO artifacts. Putting the guard inside it is simplest and covers all three — **do that**, since all
three are filing surfaces (the on-screen schedule is transcribed onto the paper form; that is the same
hole). If a non-filing consumer of `BuildMonthlyAsync` is discovered, move the guard to the three
callers instead and say so in the attempt log.

**Payslips** (`PayslipPdfService.LoadRunAsync:52-55`, used by `BuildAsync:24` and `BuildRunZipAsync:33`):
**product call, §10 E3.** Implement the recommendation — refuse from `Draft`, allow `Approved` and
`Posted` — as a **separate, clearly-marked commit** so it can be reverted alone if Ham decides
otherwise. A payslip is an internal document, not a government filing, and previewing after approval is
normal practice; but rendering one from an unapproved draft is not. Note `PayslipPdfService.cs:78`
prints `run.DocNo`, null until Post — an Approved-run payslip prints a blank document number. Flag it,
do not fix it here.

Regression coverage must include **T13: 1ก and 50ทวิ still exclude a draft run's payslips** — they are
correct today (`Pnd1FilingService.cs:79-82`, `:118-120`) and must stay correct.

### 3.5 H8 / H9 — nothing silently wrong goes into a government file (WP-5)

**INVARIANT I5.**
> A byte in a สปส.1-10 upload file or an RD form either faithfully represents the source datum, or the
> export **refuses and names what is wrong**. No silent character substitution (`?`), no silent
> zero-fill (`0000000000`).
> *(Field-width truncation is deliberately NOT in this invariant — see (c) below: 30/35/45 is the
> format's own capacity, not a defect, and refusing it would create a dead end with no exit.)*

**INVARIANT I6.**
> The fix is refuse-or-emit-unchanged. **For valid input the emitted bytes are identical to today's** —
> the existing fixed-width byte-equality tests on `SpsBatchFormat` must stay green untouched. That is the
> proof this change is behaviour-neutral on clean data.

Three parts.

**(a) H8 — missing employer account.** Mirror `Pp30BatchExportService.cs:39-52` in shape and
`WhtBatchExportService.cs:72-80` in the "name the offenders" style. Guard in `SsoFilingService`, in
**both** `BuildMonthlyFileAsync` and `BuildMonthlyPdfAsync` (the two artifacts) — **not** in
`BuildMonthlyAsync`, so the on-screen schedule still renders and shows the user what is missing:

```csharp
// R2/H8 — เลขที่บัญชีนายจ้าง is mandatory on สปส.1-10. Today a blank one is emitted as
// "0000000000" by SpsBatchFormat.Digits (:67/:122-127) and the user discovers it at the SSO
// portal. Mirrors the ภ.พ.30 exporter's refusal (Pp30BatchExportService.cs:39-52,
// pp30_batch.missing_address) — the sibling that already does this right.
if (string.IsNullOrWhiteSpace(model.EmployerAccountNo))
    throw new DomainException("sso_batch.missing_employer_account",
        "ยังไม่ได้ตั้งค่าเลขที่บัญชีนายจ้าง (10 หลัก) — กรอกในข้อมูลบริษัทก่อนจึงจะออกไฟล์ สปส.1-10 ได้ " +
        "[SSO employer account number is required for สปส.1-10. Set it on the company profile " +
        "(CompanyProfile.SsoEmployerAccountNo) first.]");
```

`CompanyProfileDtos.cs:148-154` already enforces exactly-10-digits *when present*; this closes the
"absent" case at the export boundary. Do **not** make the field `NotEmpty` at entry — that would block
saving a profile for a company that has not yet received its SSO number.

**(b) H9 — payee/employer names.** New shared helper, e.g.
`backend/src/Accounting.Infrastructure/Payroll/FilingNameRules.cs`:

```csharp
/// R2/H9 — nothing enters a government file that the file cannot REPRESENT. SpsBatchFormat.cs:54
/// encodes cp874 with the DEFAULT replacement fallback, so any non-cp874 char becomes a literal
/// '?' (co5 shipped 37 '?' bytes as an insured person's name) and the SAME character is silently
/// DROPPED from the ภ.ง.ด.1 ใบแนบ. Style mirrors WhtBatchExportService.cs:72-80, which already
/// refuses and names its offenders. NOTE: field-width truncation is deliberately NOT handled here
/// — see the spec's §3.5(c).
public static void EnsureFilable(string? value, string fieldLabel, string who)
```

Rules: reject null/whitespace; reject any character not encodable in cp874 (round-trip test:
`Encoding.GetEncoding(874, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)`, catch
`EncoderFallbackException`). One error code: `sso_batch.unencodable_name`. **The message must name the
employee and the offending character's code point** (the character itself may not render in a log).

Called from:
- `SsoFilingService` before building the file: each line's `Title`/`FirstName`/`LastName`, and the
  employer name;
- `Pnd1FilingService.NameMapAsync` (`:164-174`) and the 50ทวิ composition (`:136`). This is the cheap
  systemic close of the H9-note: the same character that becomes `?` in the SSO file is silently
  *dropped* from the ภ.ง.ด.1 ใบแนบ.

Keep `EncoderExceptionFallback` on the actual `GetBytes` call in `SpsBatchFormat.BuildBytes` (`:54`) as a
backstop, so a future unguarded path fails loudly instead of substituting. The upstream guard is what
produces the *useful* message (it knows the employee); the fallback is the net.

**Stated behaviour change**: a company with an employee name containing a non-cp874 character can no
longer produce the SSO file until the name is corrected. That is the point — today it ships a corrupted
name to the government. Call it out in the release notes.

**(c) Field-width truncation stays as-is — deliberately, and this is the reasoning.** `SpsBatchFormat.Txt`
(`:115-119`) truncates ชื่อ to 30, ชื่อสกุล to 35, ชื่อสถานประกอบการ to 45. Refusing instead would
create **a dead end with no exit**: the 135-char fixed-width record physically cannot carry more, so a
company whose legal name is 46 Thai characters could never file at all, and "shorten the legal name" is
not a remedy. Truncation here is the format's capacity, not corruption — every filer truncates. This is
the R1 lesson applied (`specs/fix-breakit-r1-ledger-integrity.md` §3.3 amendment: a guard that leaves a
shape with no way out has broken a real capability). **T17 pins the current truncation behaviour as
deliberate** so a future reviewer does not "fix" it into a refusal. If Ham wants visibility, the right
answer is a warning surfaced on screen, not an export refusal — noted in §8, not built here.

**(d) Deferred, deliberately**: entry-time name validation on `Employee` create/update
(`EmployeeDtos.cs:80-81`). **§10 E5** — better long-term, but it widens blast radius into master data and
the FE, and it cannot fix the rows already in the database. The export guard covers those; entry-time
validation does not.

### 3.6 pnd50 / pnd51 PDF — HTTP 500 on an out-of-range year (WP-6)

Today `GET /tax-filings/pnd50/pdf?year=0` and `?year=9999` throw an unmapped
`ArgumentOutOfRangeException` → 500. The sibling for `period` already exists and returns a clean 422:

```csharp
// ProportionalInputVatService.cs:40-47 — TaxFilingPeriod.MonthRange
if (m is < 1 or > 12 || y < 2000 || y > 9999)
    throw new DomainException("tax_filing.bad_period", $"Period '{period}' must be yyyymm (e.g. 202605).");
```

Mirror it for `year`, as a small shared helper next to `MonthRange` (`ProportionalInputVatService.cs`,
the `TaxFilingPeriod` static class):

```csharp
/// R2 — pnd50/pnd51 returned a raw 500 for year <= 0 or >= 9999 while the period-based siblings
/// return a clean 422 (tax_filing.bad_period, :40-47). Same convention, same bounds.
public static void EnsureYear(int year)
{
    if (year is < 2000 or > 9999)
        throw new DomainException("tax_filing.bad_year", $"Year '{year}' must be a CE year (e.g. 2026).");
}
```

Call it first in `Pnd50FilingService.BuildPnd50Async`, `PreviewAsync`, and
`Pnd51FilingService.BuildPnd51Async` — **in the services, not the endpoints**, so the MCP/any future
caller inherits it. Add the `problems.ts` entry.

**R3 boundary — state it so the two passes do not collide:** R3 owns the *global* exception-mapping pass
(`PLAN-fix-breakit-v1271.md:162`). This is a **targeted domain validation**, not exception mapping; when
R3 runs, `tax_filing.bad_year` is already a clean 422 and needs no further handling. R3 must not
re-implement it.

### 3.7 ภ.พ.36 and ภ.ง.ด.2 have no PDF route — blocked on templates (NOT a WP)

`Templates/` contains no `pnd36_main.pdf` and no `pnd2_main.pdf`. Both routes are **asset asks to Ham**
(§10 E6), not work that can be dispatched. The FE already links ภ.ง.ด.2 and ภ.พ.36 pages
(`frontend/app/(dashboard)/tax-filings/page.tsx:12`, `:16`) and `WhtFilingClient.tsx:43` explicitly
disables the PDF button for `pnd2` (`canPdf = form !== 'pnd2'`) — so no button 404s today; ภ.พ.36 has
its own page (`tax-filings/pnd36/page.tsx`) with no PDF affordance. **No dead FE consumer. No FE change.**

When the templates land, the filler work **must go through the same stage A→B loop as C4** (decode from
/Rect + marker render → Ham validates a render → then implement). Shipping a third form from an
unvalidated map is the mistake this release exists to stop repeating. Record that in the ask.

### 3.8 Feature C — the receipt becomes the only proof of settlement (WP-7)

**INVARIANT I7.**
> After R2 there is exactly **one** way a BillingNote becomes `Settled`: a posted Receipt whose applied
> amount covers the invoice total (`ReceiptService.cs:523-533` via linked TIs, `:564-578` direct). No
> route flips the status without cash and without a journal entry.

**INVARIANT I8.**
> Deleting the path changes **no existing row**. Historical `Settled` invoices keep their status,
> `SettledAt` and their (R1) `JournalEntryId`. What happens to those rows is decided from WP-0's report
> — **§10 E7** — not by this deletion.

Why it is urgent: R1 (v1.28.0, live) makes a non-VAT invoice accrue `Dr 1130 AR / Cr Revenue` at Issue
(`BillingNoteService.cs:335`). `MarkSettledAsync` then flips the status **without crediting AR and
without debiting cash** — AR stays overstated, no cash entry, no audit trail of a payment that never
happened. Every press of that button today opens a hole.

Execute the §2.4 table exactly. Two test rewrites, not deletions:
- `McpDocumentChainTests.cs:524` → reach `Settled` by posting a receipt for the full BN amount. **Never
  seed the target state** (R1 lesson; the spec skeleton's §6 rule).
- `frontend/e2e/billing-note-flow.spec.ts:8-34` → rename to `create → issue → receipt → settled` and
  drive the receipt flow. While you are in there, the test asserts the status immediately after a click
  that (used to) open a confirm dialog it never interacted with — the rewritten test must actually
  complete the flow.

`docs/api/openapi.yaml` and `docs/manual/api/sales.md` are hand-maintained → edit. `docs/_site/**` and
`docs/rbac/endpoint-permission-map.generated.md` are **generated** → regenerate if the repo has a
generator step, otherwise leave and note it.

### 3.9 WP-0 — the two read-only prod probes

Both run against **prod** over the existing `plink`/`ssh -i repttown_deploy` route
(memory: `teas-prod-deploy-plink`). **READ-ONLY: `SELECT` only. No `INSERT`, `UPDATE`, `DELETE`, no
migration, no SqlScript.** Report **row counts and the rows themselves** — never an exit code
(2026-07-09 lesson). Run for **co2 and co3 (the real tenants) first**, then co5/co7 for completeness.

**P1 — C2 history (blocks §10 E1/E2).**
- Count of `VendorInvoices` with `RequiresPnd36ReverseCharge = true`, `Status = Posted`, grouped by
  company and by `to_char(doc_date,'YYYYMM')`.
- Same for `PaymentVouchers`.
- Every `TaxFilings` row with `form_type = 'PND36'`: company, period, status, the recorded totals, and
  the linked JV id.
- For each such filing: how many VI rows and how many PV rows fell in its period → **the exact
  over-declared amount per filing.**

**P2 — H16 history (informational, blocks nothing).**
Every `TaxFilings` row with `form_type = 'PND30'`, joined to the company's `VatMode`. Expected: only
co7's `filingId 1`. **If a real tenant appears, stop and escalate.**

**P3 — Feature C (BLOCKS WP-7; Ham must read it — Ham's §6 item 3 and doc-lifecycle §3.3).**
Billing notes with `status = 'SETTLED'` (confirm the literal with `SELECT DISTINCT status` first —
`SalesChainConfigurations.cs:174-176` stores the enum as an UPPER string) where the amount applied from
**posted** receipts is less than `total_amount`. Applied amount = the sum over `sales.receipt_applications`
of posted receipts, counting **both** linkage shapes: applications direct to the BN, and applications to
TaxInvoices linked to the BN via `sales.billing_note_tax_invoices` (`ReceiptService.cs:497-535` and
`:545-580` are the two code paths — derive the exact table/column names from the EF configurations, do
not guess). Per row report: company, `doc_no`, `doc_date`, `total_amount`, applied, the shortfall,
`settled_at`, **`journal_entry_id`** (non-null ⇒ R1 accrued AR that was never cleared ⇒ a live AR
overstatement), and whether the `ActivityLog` "Settled" row has a **null note** (the manual-settle
fingerprint — `BillingNoteService.cs:369` omits it, `ReceiptService.cs:530-531`/`:575-576` set it).

Deliverable: a table appended to this spec's §12, plus the ฿ total of AR overstatement per company. That
is what Ham reads for §10 E7.

---

## 4. Invariants

| # | Invariant | Proved by |
|---|---|---|
| **I1** | One foreign service of ฿X is declared on ภ.พ.36 exactly once, for `round(0.07·X,2)`, in exactly one period — across all three chain shapes. Cash to the vendor unchanged. | T1, T2, T3, T4 |
| **I2** | ภ.ง.ด.1/1ก values do not change by one satang; only their placement moves. The multiset of printed numeric tokens is identical before and after. | T7 |
| **I3** | The totals print on the **รวม** row, and on no income-category row the data does not populate. | T5, T6, T8 |
| **I4** | A signable RD/SSO artifact only ever derives from a run with ledger backing (`Status == Posted` ⟺ `JournalId != null`). | T9, T10, T11, T12, T13 |
| **I5** | No silent substitution, zero-fill or truncation reaches a government file — the export refuses and names the problem. | T14, T15, T16, T17 |
| **I6** | For valid input the สปส.1-10 bytes are **unchanged** — the existing fixed-width byte-equality tests stay green untouched. | existing suite + T18 |
| **I7** | Exactly one route flips a BillingNote to `Settled`: a posted receipt covering the total. | T19, T20 |
| **I8** | Deleting `MarkSettledAsync` changes no existing row's status, `SettledAt` or `JournalEntryId`. | T21 (+ WP-0 P3 as the before-picture) |
| **I9** | A company with no VAT registration cannot produce or finalize a ภ.พ.30 on any of the four surfaces. | T22, T23, T24 |
| **I10** | **What is NOT changing**: the reverse-charge JV's accounts and its `VatMode` branch; ภ.พ.36 remains available to non-VAT companies; ภ.ง.ด.1ก and 50ทวิ keep their existing Posted filter; the `sales.billing_note.manage` permission and every other endpoint it gates; all sales/purchase money math. | T4, T13, T24, T25 |

---

## 5. Requirements checklist

Marks: `[ ]` not started · `[~]` partial + note · `[x]` done + evidence.

### WP-0 — pre-flight prod probes *(read-only; no dependencies; blocks WP-2 and WP-7)*
- [ ] P1 C2 history: flagged posted VIs and PVs per company per period + every `PND36` filing with its
      over-declared amount. Results appended to §12. → §10 E1/E2
- [ ] P2 H16 history: every `PND30` filing joined to `VatMode`. Escalate immediately if a real tenant
      appears.
- [ ] P3 Feature C: `SETTLED` BNs with posted-receipt coverage < total, with `journal_entry_id` and the
      activity-note fingerprint; ฿ AR overstatement per company. Results appended to §12. → §10 E7
- [ ] All three reported as **row counts + rows**, never exit codes. Confirm the `status` literal with
      `SELECT DISTINCT status` before the `WHERE`.
- **Blast cap: 0 source files.** SQL is read-only. Any write = stop and re-spec.

### WP-1 — C4 ภ.ง.ด.1 / 1ก row placement *(no dependencies; contains a BLOCKING human checkpoint — start it FIRST)*
- [ ] **Stage A** marker-render + extraction diagnostics added to `TaxFormFillDiagnostic.cs`
      (`TEAS_DIAG=1`, `[SkippableFact]`, CI untouched); four dump files written to
      `docs/RD-Forms/_fills/`.
- [ ] `Templates/pnd1_fieldmap.md` rewritten from the measurement; `Templates/pnd1a_fieldmap.md`
      **created**. Both answer the five stage-A questions in §3.2 explicitly.
- [ ] **Stage B** `_diag_pnd1.pdf` / `_diag_pnd1a.pdf` produced fully-populated; Ham's box-by-box
      confirmation recorded verbatim in §12 with the date. **HARD GATE — Stage C blocked until then.**
- [ ] **Stage C** `Pnd1FormFiller.MainFields` (`:73-108`) and `Pnd1aFormFiller.MainFields` (`:53-68`)
      corrected to the confirmed map — every wrong field, not just the totals triple. Stale comments
      replaced with a field-map pointer + validation date. "Ham visual-validation pending" removed.
- [ ] **Stage D** T5–T8 written; RED first (each fails before Stage C); extracted anchor tokens pasted
      into §12 **before** the assertions were written.
- [ ] **Stage E** Tier-4: prod render + Ham's second confirmation (post-deploy).
- **Blast cap: 6 files** (2 fillers · 2 field maps · 1 diagnostic · 1 new test file). Public API: none.
  Stop-and-re-spec if the decode shows the *ใบแนบ* row map is also wrong (that is a bigger change).

### WP-2 — C2 ภ.พ.36 declares the payment *(BLOCKED on §10 E1; independent files)*
- [ ] Blocking pre-check done: grep proves `PaymentVoucher` is the only settlement route for a
      `VendorInvoice`; evidence in §12. Any other route ⇒ **stop and re-spec.**
- [ ] `WhtFilingService.GeneratePnd36Async` (`:242-266`): `viRows` query and its `Concat` removed; rows
      come from posted PVs only. Comment states I1, the ม.83/6 payment tax point, and the E1 decision
      date.
- [ ] `VendorInvoice.RequiresPnd36ReverseCharge` left in place, on the entity and the read DTO
      (`PurchaseReadDtos.cs:41`), with a comment marking it informational-only.
- [ ] `PostReverseChargeJvAsync` untouched. No `VatMode` gate added to any ภ.พ.36 path.
- [ ] T1–T4 green, T1 RED first.
- **Blast cap: 3 files** (`WhtFilingService.cs` · `PurchaseReadDtos.cs` comment · 1 test file).
  No migration. No new endpoint.

### WP-3 — H16 ภ.พ.30 VAT-registrant-only *(no dependencies; independent files)*
- [ ] Guard added at the top of `GeneratePnd30Async` (`TaxFilingService.cs:28-29`), exact shape §3.3.
- [ ] `Pnd30DeadlineAlertJob` read and reported; skips non-VAT companies rather than throwing.
- [ ] `problems.ts` entry for `pp30.non_vat_blocked`.
- [ ] T22–T24 green (T22 RED first). T24 pins that **pnd36 is NOT blocked** for a non-VAT company.
- [ ] No prod data operation; co7's `filingId 1` left to the post-R4 reseed.
- **Blast cap: 4 files** (`TaxFilingService.cs` · `Pnd30DeadlineAlertJob.cs` · `problems.ts` · 1 test
  file). Public API: no new route; one route family gains a 422.

### WP-4 — H13 filing artifacts require a Posted run *(shares `SsoFilingService.cs` and `Pnd1FilingService.cs` with WP-5 → run WP-4 BEFORE WP-5, same warm worker)*
- [ ] Shared guard (§3.4) called from `Pnd1FilingService.BuildPnd1MonthlyAsync` and
      `SsoFilingService.BuildMonthlyAsync`.
- [ ] `sso-schedule` covered (via the shared loader) — verified, not assumed.
- [ ] Payslips: `Draft` refused, `Approved`+`Posted` allowed — **separate commit**, per §10 E3.
- [ ] `problems.ts` entry for `payroll.not_posted_for_filing`.
- [ ] FE payroll run page (`frontend/app/(dashboard)/payroll/[id]/page.tsx`): **report current behaviour
      first**, then gate **both** (a) the auto-firing query — `useSsoSchedule` is imported at `:14` and,
      if it fires on page load, every Draft run's page will toast a 422 the moment WP-4 ships, so it
      needs `enabled: status === 'POSTED'` (or the hook's equivalent) — and (b) the artifact buttons,
      which must be disabled rather than erroring.
- [ ] T9–T13 green (T9 RED first). T13 proves 1ก + 50ทวิ still refuse a draft run.
- **Blast cap: 6 files** (`Pnd1FilingService.cs` · `SsoFilingService.cs` · `PayslipPdfService.cs` ·
  `problems.ts` · FE payroll page · 1 test file). No migration.

### WP-5 — H8/H9 nothing silently wrong in a government file *(after WP-4 — shares `SsoFilingService.cs` and `Pnd1FilingService.cs`; SAME warm worker)*
- [ ] `sso_batch.missing_employer_account` in `BuildMonthlyFileAsync` **and** `BuildMonthlyPdfAsync`
      (not in `BuildMonthlyAsync` — the on-screen schedule must still render).
- [ ] `FilingNameRules.EnsureFilable` created (**encodability only — no length rule**, §3.5(c)); called
      from the SSO build (each line's title/first/last + the employer name) and from
      `Pnd1FilingService.NameMapAsync` + the 50ทวิ composition.
- [ ] `SpsBatchFormat.BuildBytes` (`:54`) switched to `EncoderFallback.ExceptionFallback` as a backstop.
- [ ] `problems.ts` entries for `sso_batch.missing_employer_account` and `sso_batch.unencodable_name`.
- [ ] T14–T18 green (T14 RED first). **T18 = the existing fixed-width byte-equality tests, unchanged and
      still green** — the I6 proof. **T17 pins truncation as deliberate, not as a bug to fix.**
- **Blast cap: 7 files** (`SsoFilingService.cs` · `SpsBatchFormat.cs` · new `FilingNameRules.cs` ·
  `Pnd1FilingService.cs` · `problems.ts` · 2 test files). No migration. No entry-time validator change.

### WP-6 — pnd50/51 year range *(no dependencies; independent files)*
- [ ] `TaxFilingPeriod.EnsureYear` added next to `MonthRange` (`ProportionalInputVatService.cs:40-47`).
- [ ] Called first in `Pnd50FilingService.BuildPnd50Async`, `Pnd50FilingService.PreviewAsync`,
      `Pnd51FilingService.BuildPnd51Async` — in the **services**, not the endpoints.
- [ ] `problems.ts` entry for `tax_filing.bad_year`.
- [ ] T26 green: `year=0`, `year=9999`, `year=-1` → **422**, not 500; `year=2026` unaffected.
- **Blast cap: 5 files** (`ProportionalInputVatService.cs` · 2 filing services · `problems.ts` · 1 test
  file).

### WP-7 — Feature C: delete "customer has paid" *(BLOCKED on WP-0 P3 + §10 E7; file set otherwise disjoint → parallel-safe except the one-test-runner rule)*
- [ ] Every DEAD row in §2.4 removed; every SURVIVES row untouched (re-read that table at the end and
      tick both columns).
- [ ] `McpDocumentChainTests.cs:524` rewritten to reach `Settled` through a posted receipt — the real
      transition, never a seeded state.
- [ ] `frontend/e2e/billing-note-flow.spec.ts:8-34` rewritten as create → issue → receipt → settled, and
      it actually completes the flow it clicks.
- [ ] i18n: 3 keys removed per locale; JSON stays valid (trailing-comma trap at `th/en.json:99-103`).
- [ ] `openapi.yaml:811-818` and `docs/manual/api/sales.md:75` updated. Generated docs regenerated or
      noted.
- [ ] Stale comments updated (`BillingNoteService.cs:19`, `ReceiptService.cs:499-500`, `page.tsx:22`,
      `:44`, `:109-112`, `:122-123`).
- [ ] T19–T21 green (T19 RED first: `POST /billing-notes/{id}/mark-settled` → 404/405).
- [ ] `corepack pnpm run build` (FE) clean — no unused-import / unused-state lint failures from the
      deletions.
- **Blast cap: 12 files** (`BillingNoteService.cs` · `BillingNoteDtos.cs` · `BillingNoteEndpoints.cs` ·
  `ReceiptService.cs` comment · `page.tsx` · `th.json` · `en.json` · `billing-note-flow.spec.ts` ·
  `McpDocumentChainTests.cs` · `openapi.yaml` · `docs/manual/api/sales.md` · 1 new/updated BE test file).
  **Public API: one endpoint REMOVED — that is the point; it is the only public-API change in R2.**

---

## 6. Test list

Behavioural tests exercise the **real** transition. Never seed the target state.

**C2 (WP-2)**
- **T1** *(RED first)* — one foreign VI ฿20,000 posted + its settling self-withhold PV, same period:
  ภ.พ.36 preview returns **1 row**, service `20,000.00`, VAT `1,400.00`. (Today: 2 rows / 40,000 / 2,800.)
- **T2** — standalone PV for a foreign vendor, no VI: **1 row**, correct amounts.
- **T3** — VI in period P, PV in period P+1: P declares **nothing**; P+1 declares it **once**. (Pins the
  E1 decision in a test.)
- **T4** — non-VAT company, `mode=finalize`: the JV still posts, debit = the irrecoverable-VAT expense
  account, `Cr 2151`, Dr = Cr, and the JV total equals the **single** declared VAT. (I10 — ภ.พ.36 is not
  VAT-gated.)

**C4 (WP-1)**
- **T5** *(RED first)* — monthly ภ.ง.ด.1: the total-income token's `Top` is within ±6 pt of the
  normalised `รวม` label's `Top`.
- **T6** — no money/count token sits in the row-5 (ม.40(2) non-resident) band.
- **T7** — I2, split in two (§3.2 Stage D): **(a)** a one-time before/after token dump pasted into §12 as
  evidence — *not* a CI test; **(b)** the durable regression: printed numeric tokens equal the values
  computed from the model input.
- **T8** — T5, T6 and T7(b) for ภ.ง.ด.1ก.

**H13 (WP-4)**
- **T9** *(RED first)* — Draft run → `GET /payroll/runs/{id}/pnd1/pdf` → **422**
  `payroll.not_posted_for_filing`.
- **T10** — Draft run → `sso/file` and `sso/pdf` → 422.
- **T11** — Draft run → `sso-schedule` → 422.
- **T12** — Posted run → all four succeed, and the run's `JournalId != null` (pins I4's equivalence).
- **T13** — Draft run's payslips are excluded from ภ.ง.ด.1ก and 50ทวิ (already true — regression pin).

**H8/H9 (WP-5)**
- **T14** *(RED first)* — company profile with no `SsoEmployerAccountNo` → `sso/file` **422**
  `sso_batch.missing_employer_account`; the emitted file today would contain `0000000000`.
- **T15** — same for `sso/pdf`; `sso-schedule` still returns 200.
- **T16** — an employee whose name contains a non-cp874 character → 422 naming that employee and the
  code point; **no file bytes produced**.
- **T17** *(pins a deliberate behaviour, §3.5(c))* — an employee name of 31+ characters: the file **is
  produced**, the name is truncated to the field width, no exception. This test exists so a future
  reviewer does not turn the format's own capacity limit into a dead-end refusal.
- **T18** — **the existing `SpsBatchFormatTests` byte-equality tests, unchanged, still green** (I6).

**Feature C (WP-7)**
- **T19** *(RED first)* — `POST /billing-notes/{id}/mark-settled` → 404/405 (route gone).
- **T20** — issue a non-VAT invoice, post a receipt covering it in full → BN flips to `Settled`, AR
  (1130) net movement for that sale is **0.00**, Dr = Cr. (I7 + the R1/C6 tie-back.)
- **T21** — a pre-existing `Settled` BN keeps its `Status`, `SettledAt` and `JournalEntryId` after the
  deletion (I8).

**H16 (WP-3)**
- **T22** *(RED first)* — non-VAT company: `POST /tax-filings/pnd30` (preview **and** finalize) → 422
  `pp30.non_vat_blocked`.
- **T23** — same company: `/pnd30/pdf` and `/pnd30/batch-file` → 422.
- **T24** — same company: `POST /tax-filings/pnd36` still **succeeds** (I10).
- **T25** — VAT company: every ภ.พ.30 surface unchanged (regression).

**WP-6**
- **T26** — `pnd50/pdf?year=0`, `?year=9999`, `?year=-1` → **422** `tax_filing.bad_year`; `?year=2026`
  behaves as before. Same for `pnd51/pdf` and `pnd50/preview`.

**Cannot be automated — reported honestly, never silently skipped**
- Ham's stage-B box-by-box confirmation (WP-1) — a human judgement on a rendered image.
- The Tier-4 prod re-render (WP-1 stage E).
- A real SSO e-Service upload of the corrected file. Nobody has done one; `SpsBatchFormat.cs:16-22` still
  lists four "verify on a real upload" constants. **R2 does not change any of them** and does not claim
  the file is portal-accepted — only that it no longer contains silently-corrupted data.

---

## 7. Verification gates

**Per-worker (Tier 1), run before reporting done:**

```
# Backend build — from the REAL path, never a subst drive (MinVer stamping)
dotnet build Y:\ClaudePlayground\TEAS-Project\backend\Accounting.sln   → 0 errors, 0 warnings

# Targeted tests for the WP (smoke, NOT the gate)
$env:TEAS_TEST_PG='<conn>'; $env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'
dotnet test backend\tests\Accounting.Api.Tests --filter FullyQualifiedName~<WPFilter>
   → all green; SKIP COUNT compared against baseline (a skipped test is not a passing test)
```

Frontend WPs (WP-4 FE, WP-7):
```
cd frontend; corepack pnpm run build        → clean
cd frontend; corepack pnpm exec tsc --noEmit → 0 errors
```

WP-1 diagnostics (manual, CI-unaffected):
```
$env:TEAS_DIAG='1'; $env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'
dotnet test --filter FullyQualifiedName~TaxFormFillDiagnostic
   → dump + diagnostic PDFs present under docs\RD-Forms\_fills\
```

**Orchestrator (Fable) runs, never a worker** — the long consolidated suite, once, at the end, in a
single backgrounded call:
```
dotnet test backend\tests\Accounting.Api.Tests   → green; skip count == baseline
```
**Only one worker or gate runner may run tests at any moment.** Any dispatch that would overlap gets an
explicit hold and an all-clear.

**Tier 2** — Opus reviewer, lenses: *money invariants (I1–I10, checked against the stated rules — the
2026-08-12 R1 lesson: when a rule and its invariant disagree, the invariant is the specification)* ·
*spec compliance* · *regression on the SURVIVES lists in §2* · *test quality (RED-first evidence, no
seeded target states)*.

**Tier 4 — live acceptance (MANDATORY, this is a compliance release):** on prod, through the public
domain: (a) render ภ.ง.ด.1 from a real posted run and have Ham confirm the row placement on the image;
(b) generate ภ.พ.36 **preview** for a period with a foreign-vendor chain and check the row count and
totals on screen — **preview only, never finalize** (finalize posts an immutable JV); (c) confirm a
non-VAT tenant's ภ.พ.30 route returns 422; (d) confirm the invoice screen no longer offers "ยืนยันชำระครบแล้ว"
and that a receipt still settles.

---

## 8. Out of scope

Each of these is a real, known defect. Listing it here makes a drive-by fix a **reviewable defect**, not
a judgement call.

- **The ภ.พ.36 JV's document date** — `JournalService.CreateDraftAsync` discards `req.DocDate`, so the
  reverse-charge JV lands on today rather than the filing period. `troubles-wiki.md:67`; deliberately
  deferred by `specs/manual-jv-and-coa-management.md` §B0 because changing the pin would silently move
  every existing ภ.พ.36 JV. **Do not touch.**
- **Backing out co7's bogus `filingId 1`, and co5's double-counted July data** — cleared by the post-R4
  wipe+reseed (`PLAN-fix-breakit-v1271.md:183-185`). Real-tenant history is §10 E2/E7.
- **สปส.1-10 ส่วนที่ 2 filling** — parked in `specs/sps110-part2-o11.md`, blocked on Ham supplying the
  template (§10 E8). O11-alt shipped and covers the need. **Do not re-design it inside R2.**
- **ภ.พ.36 / ภ.ง.ด.2 PDF routes** — no templates exist (§3.7, §10 E6).
- **Entry-time employee-name validation** (§10 E5).
- **Surfacing a warning when a name/employer name exceeds the สปส.1-10 field width** (30/35/45). The
  truncation itself is the format's capacity and stays (§3.5(c), pinned by T17); an on-screen warning is
  the right remedy if Ham wants visibility, and it is not built here.
- **Input/output VAT registers on a non-VAT company** — read-only reports, not filed documents (§2.1).
- **The global 500-exception-mapping pass, H1 numbering, H10/H11 period audit, conversion-route scopes,
  attachment IDOR** — R3.
- **H6 payslip YTD, H15 non-VAT PV grand total, H5 watermark, H7 aging `asOf`, H12 timezone,
  sales-summary CN/DN, template line-number wrap** — R4.
- **Features A (cancel + reissue) and B (settable doc date)** — later releases; A needs the ภ.พ.30
  cancellation research and a CPA, B is gated on R3's H1
  (`specs/doc-lifecycle-cancel-reissue-backdate.md` §5).
- **`SpsBatchFormat`'s four "verify on a real upload" constants** (`:16-22`) — unchanged.

---

## 9. Blast-radius cap (release total)

**Max 43 files.** Per-WP caps in §5 and they are the binding numbers. *(Keep this number current: any
post-review remediation updates this header line in the same edit that records the findings —
2026-07-29 lesson.)*

- **Public-API changes allowed: exactly one** — `POST /billing-notes/{id}/mark-settled` is **removed**
  (WP-7). No other route is added, renamed or removed. Several routes gain a 422 they did not have.
- **Schema changes: NONE.** No EF migration, no SqlScript, no `db/` change. Both prod probes are
  read-only `SELECT`s.
- **New permissions / RBAC matrix changes: NONE.**

**Stop-and-re-spec triggers:**
1. Any WP needs a migration, a SqlScript, or any prod write.
2. WP-2's pre-check finds a settlement route for a `VendorInvoice` that is not a `PaymentVoucher`.
3. WP-1's decode shows the **ใบแนบ** row map is also wrong (a materially bigger change than the summary
   page).
4. WP-0 P2 finds a `PND30` filing on a **real** tenant.
5. WP-0 P3's report shows an AR overstatement large enough that Ham wants corrective entries — that is a
   new work package with its own money spec, not part of WP-7.
6. Any WP exceeds its file cap.
7. Ham's stage-B answer contradicts the stage-A decode (the measurement would then be wrong, and the
   whole method needs re-thinking before any coordinate is edited).

---

## 10. Escalations — decisions that are NOT engineering

**Each row names the decider, my recommendation, and whether R2 ships a default while the answer is
pending.** Two do: **E3** ships the recommended payslip rule as a separately-revertable commit, and
**E5** ships the deferral. The rest ship nothing — **E1 and E7 hard-block their work packages.** No row
below is settled by an engineer.

- **E1 · C2: which side carries ภ.พ.36, and the period consequence — TAX (Ham + the company's CPA).**
  Recommendation: the **payment voucher**. Basis: ม.83/6's tax point is payment to the overseas provider,
  and ภ.พ.36 is due within 7 days of the end of the **payment** month. Consequence to confirm out loud:
  a VI in June paid in July declares in **July**. **Before dispatching WP-2**, run the same pattern the
  prior-period question got — a short delegated research note citing the RD's own ภ.พ.36 instruction,
  Fable filters it, Ham/CPA confirm. Do not cite the statute from memory in the code comment.
  **WP-2 is blocked on this.**
- **E2 · C2: already-double-counted history — TAX (Ham + CPA), informed by WP-0 P1.** Any finalized ภ.พ.36
  over-remitted VAT to the RD on an immutable JV, and re-finalize is blocked
  (`WhtFilingService.cs:277-280`). Options: (a) leave it (VAT over-remitted, recoverable via the next
  period or a ภ.พ.36 เพิ่มเติม); (b) amended filing + a correcting JV in the current open period,
  consistent with the retained-earnings principle already adopted for R1's backfill
  (`specs/research-thai-prior-period-correction.md`). **Expected scope: co5 only** — both real tenants
  are non-VAT with no foreign-vendor chain known — but P1 measures it rather than assuming, exactly as
  the R1 audit retired a tax exposure Fable had flagged three times on assumption.
- **E3 · H13: may a payslip render from an unposted run? — PRODUCT (Ham).** Recommendation: refuse from
  `Draft`, allow `Approved` and `Posted`. A payslip is internal, not a filing, and reviewing one after
  approval is normal; rendering one from an unapproved draft is not. Shipped as a separate commit so it
  can be reverted alone. (Note: an Approved run prints a blank `DocNo` — `PayslipPdfService.cs:78`.)
- **E4 · สปส.1-10 pages 3–4 (`สปส.1-10/1`, a different form) print blank in every filing packet — PRODUCT
  (Ham).** Measured: `_sps110_p3_words.txt:115`, `_sps110_p4_words.txt:114`. Options: (a) emit only
  ส่วนที่ 1 (+ คำชี้แจง) by default — recommended, blank pages of a foreign form do not belong in a
  filing packet; (b) keep them for multi-branch filers who genuinely file สปส.1-10/1; (c) keep them and
  add an on-screen note. If (a), the change is confined to the render's page selection — verify what
  `RdAcroFormFiller.RenderFlat` emits today before estimating it. **Not implemented in R2 unless Ham
  picks (a) or (c).**
- **E5 · Entry-time employee-name validation — PRODUCT/scope (Ham).** Recommendation: defer to R4. It
  cannot fix rows already in the database (which the WP-5 export guard does), and it widens blast radius
  into master data + FE. If deferred, it gets a `troubles-wiki.md` entry so it is not rediscovered.
- **E6 · ภ.พ.36 and ภ.ง.ด.2 PDFs — ASSET ASK (Ham).** No `pnd36_main.pdf` / `pnd2_main.pdf` exists.
  Supply the official AcroForm PDFs and both become buildable — **through the same decode → Ham-validates
  → implement loop as C4**, never from a self-decoded map.
- **E7 · Invoices already `Settled` with no receipt behind them — PRODUCT + accounting (Ham), gated on
  WP-0 P3.** Ham's §6 item 3 and `doc-lifecycle-cancel-reissue-backdate.md` §3.3 both require the report
  to be read **before** the endpoint is deleted. Options once the numbers exist: (a) leave history as-is
  with a note (fine if the shortfall is ฿0); (b) post a corrective receipt for each; (c) reverse the R1
  accrual for each. (b)/(c) are a new money spec, not part of WP-7.
- **E8 · สปส.1-10 ส่วนที่ 2 official template — ASSET ASK (Ham).** Supplying the employee-schedule sheet
  unparks `specs/sps110-part2-o11.md`, which is already designed. Until then O11-alt's on-screen
  schedule stands.

---

## 11. Sequencing and parallel safety

**Hard rule for the whole release: only ONE worker may run the integration test suite at a time** — the
`teas_test` database is shared, and that includes the Tier-3 gate runner. A dispatch that would overlap
another's test run gets an explicit hold message and an all-clear when the first finishes. "Different
area" does not make two dispatches parallel-safe.

**Wave 1 — start together; both contain a human wait, so front-load them.**

| Item | Why first |
|---|---|
| **WP-0** (three read-only prod probes) | Blocks WP-2 (via E1/E2) and WP-7 (via E7). No source files, no test run → safe alongside anything. |
| **WP-1 stages A–B** (C4 decode + Ham's image gate) | Its blocking checkpoint is Ham's eyes. Start the clock immediately. Stage A runs diagnostics only (`TEAS_DIAG=1`, `[SkippableFact]`) — **it does not run the suite**, so it is safe in parallel with WP-0. |
| **E1 research note** (ภ.พ.36 tax point) | Delegated web research, no repo files. Runs alongside everything. |

**Wave 2 — code, ordered by file sharing.**

| Order | WP | Files it owns | Parallel-safe with |
|---|---|---|---|
| A | **WP-4** → **WP-5** | `SsoFilingService.cs`, `Pnd1FilingService.cs`, `SpsBatchFormat.cs`, `PayslipPdfService.cs`, new `FilingNameRules.cs` | Nothing that touches payroll filing. **Same warm worker for both** — WP-5 lands on the files WP-4 just edited; a cold re-spawn re-derives the same context. |
| B | **WP-2** | `WhtFilingService.cs` | Disjoint from A, C, D. **Blocked until E1 is answered.** |
| C | **WP-3** → **WP-6** | `TaxFilingService.cs`, `Pnd30DeadlineAlertJob.cs`, `ProportionalInputVatService.cs`, `Pnd50/51FilingService.cs` | Disjoint from A, B, D. Sequential to each other only because both end in `problems.ts`. |
| D | **WP-7** | `BillingNoteService/Dtos/Endpoints`, `page.tsx`, i18n, e2e, `McpDocumentChainTests.cs`, docs | Disjoint from A, B, C. **Blocked until WP-0 P3 is read and E7 answered.** |

**Shared-file collisions to respect:** `frontend/lib/i18n/problems.ts` is touched by WP-3, WP-4, WP-5 and
WP-6 — a one-line append each, but four workers editing it concurrently will clobber. Either serialise
those four (the A/B/C/D ordering above already does, since A, C are internally sequential and B adds no
`problems.ts` entry), or have the last worker in each track add its entry. State which you chose.

**Wave 3 — WP-1 stages C–D** (the actual filler fix + its tests), after Ham's stage-B answer. Its files
(`Pnd1FormFiller.cs`, `Pnd1aFormFiller.cs`, the two field maps) are disjoint from every other WP, so it
can slot in wherever the test-runner slot is free.

**Wave 4** — Fable runs the consolidated suite once, reads the full diff, commits per verified unit.
Then Tier-2 (Opus, lenses in §7), then Tier-4 live acceptance including WP-1 stage E.

**Parallel-safety exception worth using:** a reviewer that only reads code, and any worker whose gate is
`tsc`/`pnpm build` with no database, is safe to run alongside a test-running worker.

## 12. Attempt log

*(Workers append here. Retry = same file, log grows. Paste evidence, not summaries: command + output,
extracted tokens, row counts.)*

- 2026-08-12 — opus-designer: spec written. Facts verified in code (§1); consumer sweeps complete for
  all four seams (§2); eight escalations raised, none decided (§10). Settled by measurement, not
  assumption: `sps110_main.pdf` p3/p4 carry the printed title `สปส.1-10/1` — the "ส่วนที่ 2 blank"
  finding is a re-discovery of a known, already-decided blocker, and `specs/sps110-part2-o11.md`'s
  "Fact 6" is superseded by its own blocker banner. Also found, unprompted: `pnd1_fieldmap.md` and
  `Pnd1FormFiller.cs` disagree on the sheet-count field and the whole address block, and `pnd1a` has no
  field map at all — C4's scope is the full main page of both forms, not three fields.

---

## 12. WP-0 PROBE RESULTS — run on prod 2026-08-12 (read-only)

### P1 / P2 — C2 and H16 history. **Both escalations largely retired.**

| probe | result |
|---|---|
| Posted VIs flagged reverse-charge | **co5 only** — 4 rows, subtotal ฿80,000, all `202607` |
| Posted PVs flagged reverse-charge | **co5 only** — 4 rows, subtotal ฿68,691.59, all `202607` |
| `PND36` filings on record | **NONE, for any company, ever** |
| `PND30` filings on record | one row: **co7, period 202607, `Finalized`** — the H16 artifact, no real tenant |

**→ E2 (already-double-counted history) is CLOSED: nothing to remediate.** ภ.พ.36 was never finalized, so
the double-count never left preview and **no VAT was ever over-remitted to the RD**. Exactly the shape of
the R1 audit that retired the assumed tax exposure — measure before designing remediation.

**→ E1 (the June-VI/July-PV period shift) loses its urgency but not its correctness.** Neither real tenant
has a single reverse-charge document; the only data is on co5, a test company. So the PV-only rule can
ship on its merits with no live filing at risk. **Still ask the CPA before a real tenant starts using
foreign services** — the rule is right, the confirmation is cheap, and it is no longer blocking.

**→ H16 leaves one artifact to back out:** co7 holds a `Finalized` ภ.พ.30 for a company with no VAT
registration. WP-3 stops new ones; this row needs deleting or marking void as part of the co7 reseed.

### P3 — Feature C. **A live AR overstatement exists, and it is on a REAL tenant.**

| company | doc_no | total | applied (posted receipts) | shortfall | `journal_entry_id` |
|---|---|---|---|---|---|
| **co3 (real)** | `08-2026-IV-0001` | 15,400.00 | **0** | **15,400.00** | **309** |
| co5 (test) | `07-2026-IV-0003` | 10,700.00 | **0** | 10,700.00 | — |

Both are `SETTLED` with **zero** posted receipts behind them — the manual-settle fingerprint. This is
`MarkSettledAsync` doing exactly what Feature C exists to stop.

**The co3 row interacts with work done TODAY and must be read carefully before anyone "fixes" it.**
`journal_entry_id 309` is the R1 backfill entry I posted this afternoon (`AR Backfill 08-2026-IV-0001`,
Dr 1130 / Cr 4000, ฿15,400). The sequence was: the invoice was issued → someone pressed "customer has
paid", flipping it to `Settled` with no receipt → R1's backfill then correctly accrued the revenue and AR
that had never been recognised.

**The ledger is now RIGHT and the document status is WRONG.** The sale happened and is unpaid, so revenue
฿15,400 and AR ฿15,400 are the truthful position — the backfill did not create the problem. What is false
is the `Settled` status, and its practical consequence is a trap: because the document claims to be
settled, **nobody will ever issue a receipt against it, so that AR can never be cleared through the normal
path.** ฿15,400 would sit in 1130 forever.

**→ E7 ANSWERED by Ham, 2026-08-12: neither invoice has been paid.** So option (b) for both —
**revert the status to `Issued`** so the UI matches the ledger. The ledger itself is already correct and
must NOT be touched: revenue ฿15,400 (co3) and the AR behind it are the truthful position for an unpaid
sale, and JE 309 stays exactly as posted. Only the document status is wrong.

Ham also confirms **everything on prod is still demo data**, which is why a status correction is
acceptable here at all; on a real customer's books this would be a documented adjustment, not an edit.

**WP-7 therefore has a defined pre-step**, and it is a status change, not a ledger change:
| company | doc_no | action |
|---|---|---|
| co3 | `08-2026-IV-0001` | `SETTLED` → `ISSUED`; leave `journal_entry_id 309` and the JE untouched; clear `settled_at` |
| co5 | `07-2026-IV-0003` | same (co5 is scheduled for wipe+reseed anyway, so this one is optional) |

After the revert, co3's invoice reads as outstanding in the UI, matches its ฿15,400 AR balance, and can be
receipted normally when payment arrives — which closes the trap. Only then does WP-7 delete
`MarkSettledAsync`, so nothing can re-create the state.
### E7 pre-step EXECUTED on prod — 2026-08-12

DB backed up first (`teas-pre-settled-revert-*.sql.gz`, verified with `gunzip -t`). Then, inside one
transaction, **status only**:

```sql
UPDATE sales.billing_notes SET status='ISSUED', settled_at=NULL
 WHERE billing_note_id IN (36, 20) AND upper(status)='SETTLED';   -- UPDATE 2
```

| id | company | doc_no | before | after | `journal_entry_id` |
|---|---|---|---|---|---|
| 36 | co3 (real) | `08-2026-IV-0001` | SETTLED | **ISSUED** | **309 — unchanged** |
| 20 | co5 (test) | `07-2026-IV-0003` | SETTLED | **ISSUED** | null — unchanged |

**Ledger verified untouched afterwards:** JE 309 is still `POSTED`, `AR Backfill 08-2026-IV-0001`,
Dr 15,400.00 = Cr 15,400.00; co3's account 1130 still nets to **15,400.00**. co2's AR aging still
reconciles (`control 8,400 = subledger 8,400, difference 0, balanced true`) — the change touched neither
company's books.

The two invoices now read as outstanding in the UI, which matches the ledger, and each can be receipted
normally when payment arrives. **WP-7 is unblocked** — deleting `MarkSettledAsync` can now proceed without
destroying the ability to reason about how this state arose.

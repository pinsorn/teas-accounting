# สปส.1-10 e-payment upload file — format spec + open items (P-D #4)

> Status: **BUILT end-to-end + live-verified; flagged for a real-upload test** before production.
> The full filing pipeline is wired: aggregation (`ISsoFilingService` → `SsoMonthlyModel`) + fixed-width
> TIS-620 builder (`SpsBatchFormat`) + endpoint `GET /payroll/runs/{id}/sso/file` + FE button. Every
> field was verified field-by-field against the authoritative cowork research at **`docs/SSO-Forms/`**
> (`spec/sps110-spec.md` + `spec/sps110-spec-Q&A.md`, sourced from a filled BusinessPlus form + Nimitr
> blog + vendor specs). The 4 items still marked ⚠️ are "verify by test upload" (SSO publishes no public
> byte-spec) — kept as single-point constants in `SpsBatchFormat` so a confirmed sample = a local edit.

## What was decided / found this session
- The official **สปส.1-10 ส่วนที่ 1 PDF is FLAT** (no AcroForm, no fillable fields — verified by a PdfSharp
  `/AcroForm` probe; 4 pages, A4 landscape 841.9×595.3pt). The proven `RdAcroFormFiller` `/Rect`-driven
  playbook (used for ภ.ง.ด.1/1ก/50ทวิ) therefore **cannot be reused** for an SSO PDF.
- Ham chose the **e-Service upload TEXT file** channel (not a coordinate-overlay PDF) — the natural
  sibling of the shipped RD WHT batch file (`WhtBatchFormat`).
- The `os4/` PDFs in `docs/RD-Forms/` are **อ.ส.4 stamp-duty**, NOT SSO (the kickoff spec's guess was
  wrong). The real form was downloaded to `docs/RD-Forms/sps1-10/sps1_10_part1.pdf`.

## Confirmed layout (sourced, internally consistent)
**Fixed-width, 135 characters/record, CRLF-terminated.** HEADER record (type `"1"`) + one DETAIL
record (type `"2"`) per insured person. Both records sum to exactly 135 (a strong internal check).

### HEADER record "1" (135)
| # | Field | Pos | Width | Notes |
|---|---|---|---|---|
| 1 | Record type | 0 | 1 | `"1"` |
| 2 | เลขที่บัญชีนายจ้าง (SSO employer account) | 1 | 10 | ⚠️ not stored — config later (`Payroll:Sso:EmployerAccountNo`) |
| 3 | ลำดับที่สาขา | 11 | 6 | from `BranchCode` (00000=HQ), padded |
| 4 | วันที่นำส่ง | 17 | 6 | `ddMMyy` — ⚠️ year base BE vs CE unconfirmed |
| 5 | งวด (period) | 23 | 4 | `MMyy` — ⚠️ year base |
| 6 | ชื่อสถานประกอบการ | 27 | 45 | left, space-padded |
| 7 | อัตราเงินสมทบ | 72 | 4 | ⚠️ `"0500"` = 5.00%? |
| 8 | จำนวนผู้ประกันตน | 76 | 6 | right, zero-filled |
| 9 | รวมค่าจ้าง | 82 | 15 | ⚠️ amount convention |
| 10 | รวมเงินสมทบทั้งสิ้น | 97 | 14 | ⚠️ amount convention |
| 11 | รวมเงินสมทบผู้ประกันตน | 111 | 12 | ⚠️ amount convention |
| 12 | รวมเงินสมทบนายจ้าง | 123 | 12 | ⚠️ amount convention |

### DETAIL record "2" (135)
| # | Field | Pos | Width | Notes |
|---|---|---|---|---|
| 1 | Record type | 0 | 1 | `"2"` |
| 2 | เลขประจำตัวประชาชน | 1 | 13 | digits |
| 3 | คำนำหน้า (prefix) | 14 | 3 | ⚠️ CODE — "รหัสตามที่ สปส. กำหนด"; table not sourced |
| 4 | ชื่อ | 17 | 30 | left, space-padded |
| 5 | ชื่อสกุล | 47 | 35 | left, space-padded |
| 6 | ค่าจ้าง | 82 | 14 | ⚠️ "ค่าจ้างที่จ่ายจริง" (actual) vs capped contributory base |
| 7 | เงินสมทบผู้ประกันตน | 96 | 12 | ⚠️ amount convention |
| 8 | filler | 108 | 27 | blanks |

## 🚧 #1 BLOCKER before ANY real use — NOT yet submittable
The pipeline produces a **structurally-correct** file, but it is **not submittable as-generated**:
`EmployerAccountNo` is unset → the header เลขที่บัญชีนายจ้าง is all zeros, which SSO rejects. The green
tests + smoke pass because nothing asserts a real account. **Set `Payroll:Sso:EmployerAccountNo` (the
10-digit SSO reg no)** — and since it is per-tenant, the proper fix is a `CompanyProfile` column (a global
appsettings value is a §4.7 multi-tenant smell, acceptable only until a 2nd company exists). Rank this
ABOVE the format-verify items below — it is a correctness gap, not a format guess.

## ⚠️ Verify by a real e-Service test upload (single-point consts in `SpsBatchFormat`)
1. **Encoding** TIS-620 / Windows-874 vs (newer portal) UTF-8 — try TIS-620 first. (`CodePage`/`BuildBytes`.)
2. **Amount format** — 2-implied-decimals (×100 สตางค์, zero-filled) vs a literal dot. (`Amt`.)
3. **คำนำหน้า code table** — `PrefixCode` maps นาย→001 / นาง→002 / นางสาว→003 / …/099 (Thai-gov convention).
4. **อัตราเงินสมทบ** `"0500"` convention. (`RateField`.)
5. **`วันที่ชำระเงิน`** currently = `run.PayDate` (the salary date). SSO likely wants the *remittance* date
   (paid by the 15th of the next month), not the pay date — confirm + switch the source if so.
6. **Trailing terminator** — we emit a final CRLF after the last detail; some systems want no trailing blank
   line. Confirm on upload.
- **2569 wage ceiling** — config `Payroll:Sso:WageCeiling` (฿15,000 → ฿17,500 phased). Does NOT affect the
  file format (runs already computed at the configured value). Confirm the effective ฿ before go-live.
- **Contribution rounding** — form footnote rounds each contribution ≥50 สตางค์ up to ฿1; we emit the posted
  payslip value (2 dp) so the file matches the GL. Rarely differs; revisit in P-C if a real upload rejects.

## Build state — DONE + live-verified
- `Application/Payroll/ISsoFilingService` (+ `SsoMonthlyModel`/`SsoLine`) — aggregation + `BuildMonthlyFileAsync`.
- `Infrastructure/Payroll/SsoFilingService` (insured payslips; employer header from `CompanyProfile`) + DI.
- `Infrastructure/Payroll/SpsBatchFormat` — 135-char fixed-width builder + TIS-620 `BuildBytes` + `FileName`
  (`CodePagesEncodingProvider` is framework-provided on .NET 10 — no extra package).
- `SsoOptions.EmployerAccountNo` (config stopgap); endpoint `GET /payroll/runs/{id}/sso/file` (`payroll.run.manage`).
- FE `/payroll/[id]` "สปส.1-10 (ไฟล์)" download button (POSTED only) + i18n th/en.
- Tests: aggregation integration test + 5 `SpsBatchFormat` golden tests (positions / totals / actual-wage / encoding).
  **BE 0/0 · Api.Tests 226/226 ×2 · FE tsc 0.** Live smoke (run 2): valid 135-char TIS-620 file — header
  totals 50,000 wage / 750+750 contribution; detail actual-wage 50,000 + capped 750; decodes cleanly in CP874.

> NOTE: the ค่าจ้าง column semantics open item is now RESOLVED — ✅ verified **actual (un-capped) wage** vs a
> filled BusinessPlus form; `SsoLine.Wage` = the payslip gross taxable, the clamp lives only in the contribution.
> The 4 ⚠️ items above are the only "verify by real upload" remainders (encoding, amount dot-vs-implied, prefix
> code table, rate field) — each a single-point constant in `SpsBatchFormat`.

## Sources
- สปส.1-10 ส่วนที่ 1 PDF — https://www.sso.go.th/wpr/assets/upload/files_storage/sso_th/918a5ffc639945c7890f199112922f09.pdf
- Text-file structure (135-char, header/detail) — https://www.panyame.com/blog/docs/others/e-filing/sso110txt/ (third-party transcription)
- SSO e-Services manual — https://www.sso.go.th/eservices/web/UserManual.pdf
- e-Service submission guide — https://www.doe.go.th/prd/assets/upload/files/bkk_th/4ef2fa878a1df0e16a7097c95343224b.pdf (scanned)

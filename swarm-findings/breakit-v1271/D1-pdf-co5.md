# D1 — PDF / printed-document sweep, co5, prod v1.27.1

Agent: **D1** (break-it QA, PDF specialty) · Date: 2026-07-31 · Target: `https://teas.kazaki-rio.com`
Tenant confirmed: `GET /api/proxy/me` → `companyId: 5` for every account used (chief01 / tax01 / admin01 / sales01).
Method: `curl` cookie-jar downloads → `pdftotext -enc UTF-8 -layout/-raw` + raw content-stream / image-XObject
parsing (poppler `pdftoppm`/`pdfimages` unavailable, so image checks were done by decompressing the
PDF image XObjects with Python/PIL). Working files: `Z:\temp\claude\…\scratchpad\pdf\`.

## Worst findings (read this first)

| # | Severity | One line |
|---|---|---|
| F1 | **CRIT** | ภ.ง.ด.1 and ภ.ง.ด.1ก print the summary **"6. รวม" row BLANK** and stamp the totals onto **row 5 = ม.40(2) ผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย** (non-resident) — the RD return declares the whole payroll twice, once in the wrong income class. |
| F2 | **HIGH** | A **VOIDED** payment voucher prints as **"ต้นฉบับ" (ORIGINAL)** with the approver's name in the signature box and **no ยกเลิก watermark** — a voided voucher is indistinguishable from a live one. |
| F3 | **HIGH** | A **DRAFT** payment voucher also prints **"ต้นฉบับ"** (while showing "(ร่าง)" as its doc number — self-contradictory). |
| F4 | **HIGH** | Two **POSTED** credit notes (noteId 1 and 5) carry the **same legal doc number `07-2026-CN-0001`**; both PDFs print it. |
| F5 | **HIGH** | The payslip's **สะสมตั้งแต่ต้นปี (YTD)** contradicts the **50ทวิ** and **ภ.ง.ด.1ก** for the same employee/year: 1,040,000.00 / 88,900.00 vs 560,000.00 / 52,450.00. |
| F6 | **MED-HIGH** | On **every** document template, line numbers ≥ 10 **wrap vertically** — "10" renders as "1" stacked over "0". Hits any invoice with 10+ lines. |

---

## Per-doctype matrix

Legend — HTTP: status of `GET …/pdf`. Numbers: every money field in the API JSON found verbatim in the
extracted PDF text. Thai: no Bengali `ম` (U+09AE), no U+FFFD, no `????` produced *by the renderer*.
State: watermark / signature block correct for the document's status.

### Trade documents

| Doctype | id (status) | HTTP | Numbers match | Thai ok | State correct |
|---|---|---|---|---|---|
| QT ใบเสนอราคา | 36 (Accepted) / 13 (Draft) / 37 (Draft, 32 lines) | 200 | ✅ | ✅ | ✅ (no status watermark by design) |
| SO ใบสั่งขาย | 19 (Posted) / 18 (Draft) | 200 | ✅ | ✅ | ✅ `ยืนยันแล้ว` on Posted only |
| DO ใบส่งของ | 16 (Issued) / 12 (Delivered) | 200 | ✅ | ✅ | ✅ `ส่งของแล้ว` on Delivered only |
| IV/BN ใบแจ้งหนี้ | 32 (Issued) / 31 (Draft) | 200 | ✅ | ✅ | ✅ `ออกแล้ว` on Issued only |
| TI ใบกำกับภาษี | 47 (Posted) / 41 (Draft) | 200 | ✅ | ⚠️ `????` in the uom cell — **stored data**, not renderer (F9) | ✅ `ต้นฉบับ` on Posted only |
| RC ใบเสร็จรับเงิน | 33 (Posted) / 9 (Draft) | 200 | ✅ | ⚠️ same `????` uom | ✅ |
| CN/DN ใบลดหนี้ | 5, 1 (Posted) / 4 (Draft) | 200 | ✅ | ✅ | ✅ watermark — ❌ **duplicate doc no (F4)** |
| PO ใบสั่งซื้อ | 25 (Closed) / 24 (Approved) | 200 | ✅ | ✅ | ⚠️ `ต้นฉบับ` hard-coded, same bug family as PV (F2/F3); no Cancelled PO exists in co5 to observe |
| PV ใบสำคัญจ่าย | 54 (Posted) / 51 (Draft) / 50 (**Voided**) / 40, 34, 35 (WHT & self-withhold) | 200 | ✅ (incl. WHT + self-withhold gross-up) | ✅ | ❌ **F2 + F3** |
| 50ทวิ (wht-certificates) | 15 (Posted) | 200 | ✅ 10,000.00 / 300.00 | ✅ | ✅ |
| **VI (vendor invoice)** | 23 | **404** | — | — | No `/pdf` route. Documented as intentional in `frontend/app/(dashboard)/vendor-invoices/[id]/page.tsx:62` (§4.6). |
| **Expense claim** | 9 | **404** | — | — | No `/pdf` and no `/paper` route (F10) |
| **JV (journal)** | 272 | **404** | — | — | No `/pdf` and no `/paper` route (F10) |
| Payslip | run 5 / emp 3 · run 15 / emp 3 · run 13 / emp 13 | 200 | ❌ **YTD (F5)**; period figures ✅ | ⚠️ `????` employee name (stored data, F9) | n/a |
| Payslip bulk (run zip) | run 5 | 200 | n/a | n/a | Returns `application/zip` (`payslips-202607.zip`) — correct, despite the `.pdf` route name |

### Tax / filing forms

| Form | Query | HTTP | Numbers match API | BE dates | Notes |
|---|---|---|---|---|---|
| ภ.พ.30 | `period=202607` | 200 | ✅ 45,033.00 / 3,152.31 / 58,050.00 / 4,063.50 / 911.19 (comb cells) | ✅ 2569 | ✅ |
| ภ.ง.ด.1 | `runs/5/pnd1/pdf` | 200 | values correct (3 ราย / 125,000.00 / 1,408.33) but **misplaced (F1)** | ✅ | ❌ F1 |
| ภ.ง.ด.1ก | `year=2026` | 200 | totals correct (6 ราย / 965,000.00 / 52,450.00 — verified against the sum of all 7 posted runs) but **misplaced (F1)** | ✅ | ❌ F1; ใบแนบ prints `?????` names (F9) |
| ภ.ง.ด.3 | `period=202607` | 200 | ✅ (0 rows — no ภ.ง.ด.3 data in the period) | ✅ | ✅ |
| ภ.ง.ด.53 | `period=202607` | 200 | ✅ 7 rows + totals 41,000.00 / 1,370.00 | ✅ | ✅ |
| ภ.ง.ด.54 | `period=202607` | 200 | ✅ 4 rows, one sheet per payee (single-payment form by design — no grand total expected) | ✅ | ✅ |
| ภ.ง.ด.51 | `year=2026` | 200 | — | ✅ | ✅ |
| ภ.ง.ด.50 | `year=2026` | **422** | — | — | `pnd50.not_attestable` — by design (filer must attest) |
| ภ.พ.01 / ภ.พ.09 | — | 200 | — | — | ✅ |
| **ภ.พ.36** | `period=202607` | **404** | — | — | Confirmed missing (known F3 from a prior sweep). Not re-investigated. |
| สปส.1-10 PDF | `runs/5/sso/pdf` | 200 | — | ✅ | ✅ |
| สปส.1-10 upload file | `runs/5/sso/file` | 200 | — | — | ✅ **TIS-620 verified byte-level** — decodes cleanly as tis-620, Thai intact, fails as UTF-8 (as expected) |
| Financial statements | `year=2026` | 200 | ✅ balanced (assets = L+E = −532,619.80) | ❌ **CE (F7)** | ❌ F7 |

---

## Findings

### F1 — CRIT — ภ.ง.ด.1 / ภ.ง.ด.1ก: the "รวม" total row is blank and the totals land on the ม.40(2) non-resident row

**Repro (ภ.ง.ด.1ก)**
```
curl -b <tax01 jar> "https://teas.kazaki-rio.com/api/proxy/payroll/pnd1a/pdf?year=2026" -o pnd1a.pdf
pdftotext -enc UTF-8 -layout pnd1a.pdf -   # page 1, summary block
```
**Actual** (page 1, lines 33 / 47 / 49 of the extraction):
```
1. เงินได้ตามมาตรา 40 (1) เงินเดือน ค่าจ้าง ฯลฯ กรณีทั่วไป . .        6   965,000.00   52,450.00
5. เงินได้ตามมาตรา 40 (2) กรณีผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย  6   965,000.00   52,450.00
6. รวม . . . . . . . . . . . . . . . .                                  (blank)
```
**Repro (ภ.ง.ด.1 monthly)** — `…/api/proxy/payroll/runs/5/pnd1/pdf`; same block:
```
1. เงินได้ตามมาตรา 40 (1) … กรณีทั่วไป . .                              3   125,000.00   1,408.33
2. เงินได้ตามมาตรา 40 (1) … อนุมัติจากกรมสรรพากรให้หักอัตราร้อยละ 3                       1,408.33
   (second label line)                                                                    1,408.33
5. เงินได้ตามมาตรา 40 (2) กรณีผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย  3   125,000.00
6. รวม . . . . . . . . . . . . . . . .                                  (blank)
8. รวมยอดภาษีที่นำส่งทั้งสิ้นและเงินเพิ่ม (6. + 7.) . . . .              (blank)
```
**Expected** — row 1 filled (correct), rows 2–5 empty, row 6 (รวม) = 3 / 125,000.00 / 1,408.33,
row 8 (รวมทั้งสิ้น) = 1,408.33.

**Evidence the values themselves are right:** the totals reconcile exactly with the payroll data —
sum of `totalGrossTaxable` over the 7 posted 2026 runs = 965,000.00 and `totalPit` = 52,450.00,
6 distinct employees. Only the *placement* is wrong.

**Where:** `backend/src/Accounting.Infrastructure/Pdf/Pnd1FormFiller.cs:98-105` and
`Pnd1aFormFiller.cs:66-67` write the totals to `Text2.18 / Text2.19 / Text2.20` (and `Text2.22` on
ภ.ง.ด.1). `Templates/pnd1_fieldmap.md` claims those are the row-6 "รวม" cells — but that field map
carries its own warning at the top of the file: *"**Ham visual-validation pending** — the
summary-table column order … are the high-risk spots; verify against the real-data render."* The
render shows the mapping is off.

**Why the row assignment is trustworthy:** `pdftotext -layout` places text by absolute baseline. Row
1's value shares an output line with row 1's label (a known-correct control, `Text2.1/2/3`), and
row 5's and row 6's labels extract as two *separate* lines — so a value sharing row 5's line is at
row 5. A visual (rendered-image) confirmation before the fix is still worth 2 minutes.

**Impact:** this is a form filed with the Revenue Department. As printed it declares the entire annual
payroll a second time under ม.40(2) non-resident income, and leaves the statutory total rows empty.

---

### F2 — HIGH — a VOIDED payment voucher prints as "ต้นฉบับ" with the approver's signature name

**Repro**
```
curl -b <chief01 jar> ".../api/proxy/payment-vouchers/50"        # → "status":"Voided"
curl -b <chief01 jar> ".../api/proxy/payment-vouchers/50/paper"  # → watermark {"text":"ต้นฉบับ","variant":"success"}
                                                                 #   signatures.middleName "ทดสอบ หัวหน้าบัญชี"
curl -b <chief01 jar> ".../api/proxy/payment-vouchers/50/pdf" -o pv50.pdf
```
**Actual (PDF):** watermark `ต้นฉบับ`; `ลงชื่อผู้อนุมัติ ( ทดสอบ หัวหน้าบัญชี )`; no cancellation mark anywhere.
**Expected:** `PaperDocConfig.Watermark` already defines `Cancelled = {Cancelled, Voided, Rejected} → ("ยกเลิก", Danger)`
(`backend/src/Accounting.Infrastructure/Pdf/PaperDocConfig.cs:50-57`), and the signature block should not
present an approver on a voided document.

**Root cause:** `backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.Read.cs:238`
bypasses that helper entirely:
```csharp
Watermark: new PaperWatermark(
    copy ? "สำเนา" : "ต้นฉบับ",
    copy ? PaperWatermarkVariant.Warning : PaperWatermarkVariant.Success),
```
and line 245 gates the signature on `d.Status != "Draft"`, which a Voided PV passes.
`backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs:325` has the identical
hard-coded watermark (no Cancelled PO exists in co5, so it is code-confirmed only).
Contrast TI / RC / CN / BN / QT / SO / DO, which all correctly call `PaperDoc.Watermark(kind, status)`.

**Impact:** a cancelled payment voucher can be printed and passed off as a live, approved one.

---

### F3 — HIGH — a DRAFT payment voucher prints "ต้นฉบับ"

Same root cause as F2.
**Repro:** `GET /api/proxy/payment-vouchers/51/paper` → `docNo: "(ร่าง)"` **and**
`watermark: {"text":"ต้นฉบับ","variant":"success"}`. The PDF prints both markings on one page.
**Expected:** no `ต้นฉบับ` on a draft (every other doctype gets this right — verified across 8 draft
documents: QT 13, SO 18, TI 41, RC 9, BN 31, CN 4 all render no status watermark).

---

### F4 — HIGH — two POSTED credit notes share the doc number `07-2026-CN-0001`

**Repro**
```
curl -b <chief01 jar> ".../api/proxy/tax-adjustment-notes?pageSize=100"
```
```json
{"noteId":5,"docNo":"07-2026-CN-0001","docDate":"2026-07-31","status":"Posted","originalTiDocNo":"07-2026-TI-0021","totalAmount":1070.0}
{"noteId":1,"docNo":"07-2026-CN-0001","docDate":"2026-07-19","status":"Posted","originalTiDocNo":"07-2026-TI-0001","totalAmount":1070.0}
```
Both PDFs (`/tax-adjustment-notes/1/pdf` and `/tax-adjustment-notes/5/pdf`) print
`07-2026-CN-0001` in the doc-number slot, both carry the `ต้นฉบับ` watermark, and they reference
different original tax invoices.
**Expected:** doc numbers are unique per company/prefix/period; a ใบลดหนี้ is a VAT document under
ม.86/10 and a duplicate number is a filing defect.
Note: the numbering counter appears to have been reset or bypassed at some point during the swarm
runs; the reproducible artefact is that the system currently serves two posted CNs with one number.

---

### F5 — HIGH — payslip YTD contradicts the 50ทวิ and ภ.ง.ด.1ก for the same employee/year

**Repro** (employee 3, tax year 2026)
```
curl -b <chief01 jar> ".../api/proxy/payroll/runs/15/payslips/3/pdf"           -o payslip.pdf
curl -b <chief01 jar> ".../api/proxy/payroll/employees/3/wht50tawi/pdf?year=2026" -o 50tawi.pdf
curl -b <chief01 jar> ".../api/proxy/payroll/pnd1a/pdf?year=2026"              -o pnd1a.pdf
```
| Document | เงินได้สะสม | ภาษีหัก ณ ที่จ่ายสะสม |
|---|---|---|
| Payslip, run 15 (ธันวาคม 2569) | **1,040,000.00** | **88,900.00** |
| 50ทวิ, emp 3, 2026 (`รวมเงินที่จ่ายและภาษีที่หักนำส่ง`) | 560,000.00 | 52,450.00 |
| ภ.ง.ด.1ก ใบแนบ, row for emp 3 | 560,000.00 | 52,450.00 |
| Ground truth (sum of the 7 posted runs) | 560,000.00 | 52,450.00 |

The stored YTD is also non-monotonic across periods — `GET /payroll/runs/{id}` for employee 3:

| Run | Period | `ytdIncome` | `ytdPit` |
|---|---|---|---|
| 5 | 202607 | 80,000.00 | 1,408.33 |
| 14 | **202606** | **560,000.00** | 43,942.86 |
| 7 | 202608 | 640,000.00 | 44,866.66 |
| 15 | 202612 | 1,040,000.00 | 88,900.00 |

June shows a larger YTD than July, and December shows 1,040,000 = 13 × 80,000 for an employee with
only 7 posted runs.

**Root cause:** `backend/src/Accounting.Infrastructure/Payroll/PayrollRunService.cs:134` freezes
`YtdIncome = priorIncome + thisMonthTaxable` into the payslip row at **draft-creation** time; the
query that supplies `priorIncome` (`LoadYtdAsync`, line 326-338) is itself correct (posted runs,
period < current), but the snapshot is never recomputed when a prior run is later deleted or when a
back-dated run is inserted afterwards. `PayslipPdf.cs:115` prints that frozen value verbatim
(`สะสมตั้งแต่ต้นปี เงินได้ … · ภาษีหัก ณ ที่จ่าย …`). The 50ทวิ and ภ.ง.ด.1ก recompute, hence the split.

**Impact:** the payslip doubles as `หนังสือรับรองการจ่ายเงินได้`; an employee reconciling it against
their 50ทวิ gets two different official numbers from the same system.

---

### F6 — MED-HIGH — line numbers ≥ 10 wrap vertically in the `#` column on every document template

**Repro** — created a 32-line draft quotation through the normal API (id 37, `POST /api/proxy/quotations`),
then `GET /api/proxy/quotations/37/pdf`.
`pdftotext -raw` (content-stream order) of page 1:
```
9 รายการทดสอบพิมพ์ที่ 9 — …
1 ชิ้น 109.00 109.00
1
0
รายการทดสอบพิมพ์ที่ 10 — …
```
**Proof from the raw content stream** (glyph runs in the `#` column, x ≈ 198.6 in the page's scaled
text space; digit glyphs `01F1`='0' … `01FA`='9'):
```
x=198.60 y=2038.65 glyphs=['01FA'] -> "9"      ← line 9, single run
x=198.60 y=2171.85 glyphs=['01F2'] -> "1"      ← line 10, first run
x=193.65 y=2182.20 glyphs=['01F1'] -> "0"      ← line 10, second run, 10.35 units LOWER
x=198.60 y=2305.05 -> "1"  /  x=193.65 y=2315.40 -> "1"   ← line 11
… same for every line 12–32
```
**Expected:** "10" on one baseline. **Actual:** the `#` column is narrower than two digits, so the
number line-wraps. Reproduces on both page 1 and page 2 of the same document.
**Impact:** cosmetic but on every printed legal document with 10+ line items — the most common real
invoice shape.

---

### F7 — MEDIUM — the financial-statements PDF prints CE years on a document it labels an RD attachment

**Repro:** `GET /api/proxy/reports/financial-statements/pdf?year=2026`
**Actual:**
```
งบการเงินประกอบการยื่นแบบ (เอกสารประกอบ)
… เพื่อใช้อ้างอิง/แนบประกอบการยื่น ภ.ง.ด.50 เท่านั้น
รอบระยะเวลาบัญชี 01/01/2026 ถึง 31/12/2026
งบแสดงฐานะการเงิน ณ วันที่ 31/12/2026
```
**Expected:** BE (2569) — every other RD-facing artefact in the system uses BE (verified: ภ.พ.30,
ภ.ง.ด.1, ภ.ง.ด.1ก, ภ.ง.ด.3/53/54/51, 50ทวิ, สปส.1-10 all print 25xx; the trade documents print
`วันที่ / Date 31/07/2569`). This is the only PDF in the system that prints 20xx.
The statement itself is arithmetically consistent (assets = liabilities + equity = −532,619.80, and
the P&L ties to retained earnings), so this is presentation only.

---

### F8 — MEDIUM — the supplier's tax ID is printed unformatted in the header of 9 of the 10 document templates

**Repro:** compare the two tax-ID lines in any document PDF.
```
ti47_posted   header: เลขประจำตัวผู้เสียภาษี: 0-1055-68000-12-2 · สาขา 00000   ← formatted
rc33_posted   header: เลขประจำตัวผู้เสียภาษี: 0105568000122 · สาขา 00000       ← NOT formatted
(same unformatted header on QT, SO, DO, BN, CN/DN, PO, PV — every template except TI)
customer/vendor block, ALL templates: เลขประจำตัวผู้เสียภาษี: 0-1055-67000-31-5  ← formatted
```
So the counterparty's 13-digit id is dash-formatted everywhere while the issuer's own is not, except
on the tax invoice. The stored value is unformatted in both cases (`supplierTaxId: "0105568000122"`,
`customerTaxId: "0105567000315"`), i.e. `Pdf.PaperFormat.TaxId(...)` is applied to the party block
but not to the seller header (only `TaxInvoiceService.Read` passes a formatted value through).

---

### F9 — MEDIUM — `?`-mangled Thai names reach statutory RD forms unvalidated (corruption is client-origin)

**Observed:** employees 13/14/15 are stored with names consisting entirely of `?`
(`"employeeName": "???????????????? ??????"`), and tax-invoice lines 41/47 carry
`descriptionTh: "??????????? A"` / `uomText: "????"` (exactly the code-point count of
`สินค้าทดสอบ A` / `ชิ้น`). These print on the **ภ.ง.ด.1ก ใบแนบ** — an RD attachment:
```
1  1-9000-00000-02-9  ชื่อ ? ? ? ? ? ? ? ? ? ? ? ? ? ? ?   ชื่อสกุล ? ? ? ? ? ?
2  1-9000-00000-01-1  ชื่อ ? ? ? ? ? ? ? ? ? ? ? ? ? ? ?   ชื่อสกุล ? ? ? ? ? ?
```
and on the payslip for run 13 / employee 13.

**Not a server bug — proven:** I posted a 32-line quotation with Thai descriptions and uom
`ชิ้น` as clean UTF-8 (`--data-binary @file`, `Content-Type: application/json; charset=utf-8`) and it
round-tripped byte-perfect through the API and into the PDF:
`'รายการทดสอบพิมพ์ที่ 1 — สินค้าตัวอย่าง ๆ ฿ ปีที่ ๒๕๖๙ (D1 pdf sweep)'` / `'ชิ้น'`.
The corruption was introduced by a sibling agent's shell codepage.

**The finding that remains:** there is no character-set sanity check on legal-name / description
fields whose only downstream consumer is a Revenue Department form. A name of pure `?` is accepted,
persisted, and filed.

---

### F10 — MEDIUM — no printable document exists for JV or expense claim

`GET /api/proxy/journals/272/pdf` → 404 · `…/journals/272/paper` → 404
`GET /api/proxy/expense-claims/9/pdf` → 404 · `…/expense-claims/9/paper` → 404
`GET /api/proxy/fixed-assets/1/pdf` → 404 · `…/depreciation-runs/1/pdf` → 404

No route is registered in `JournalEndpoints.cs` / `ExpenseClaimEndpoints.cs`, and the FE has no print
button for them (so there is no broken button — it is a straight feature gap). The vendor-invoice
case is *documented as deliberate* (`frontend/app/(dashboard)/vendor-invoices/[id]/page.tsx:62`:
"No PrintMenu (no /pdf endpoint — §4.6)"); JV and expense claim carry no such note. A journal voucher
and an expense claim are the two documents Thai accountants most often print for the approval file.

---

### F11 — LOW — `pnd30/pdf` accepts an absurd period while the sibling forms reject it

```
GET /api/proxy/tax-filings/pnd30/pdf?period=209901  → 200 (renders a form for BE 2642)
GET /api/proxy/tax-filings/pnd3/pdf?period=0        → 422 tax_filing.bad_period
GET /api/proxy/tax-filings/pnd54/pdf?period=202699  → 422 tax_filing.bad_period
```
Inconsistent validation across the filing endpoints. No crash, no wrong number — just a form nobody
should be able to render.

---

## Checks that PASSED (documented so nobody re-runs them)

- **No Bengali `ম` (U+09AE) anywhere.** Scanned all 41 extracted PDFs (trade docs, payroll, all RD
  forms, financial statements) — zero code points in the U+0980–U+09FF block. Also zero U+FFFD in any
  document the app renders itself.
- **Error paths are clean — not one 500.** 22 probes: non-existent ids on all 10 doc types, id `0`,
  id `-1`, missing payroll runs, missing payslips, garbage/out-of-range periods, year 1900. All
  return a typed 404/422/400 with a `urn:teas:error:*` body. `?copy=1` / `?copy=yes` → 400 (strict
  `bool?` binding); no crash.
- **Permission model holds for PDFs.** As `sales01` (`SALES_STAFF`, 17 permissions, no purchase /
  payroll / filing scope): QT/SO/DO/TI/RC/BN/CN PDFs → 200 (all within its read scopes); PO, PV,
  50ทวิ, all payroll PDFs, all tax-filing PDFs, the payslip zip, the SSO upload file, the ภ.ง.ด.53
  batch file and the financial statements → **403**. No IDOR analogue of F16 on document PDF routes.
- **`/public/pdf` is safe.** No token → 404. `?t=abc`, `?token=x&docType=tax_invoice&docId=47` → 404
  (doc identity comes from the signed token, never the query string). `/api/v1/*/pdf` without
  `X-Api-Key` → 401.
- **Pagination (v1.26.1) is correct.** 32-line quotation → 3 pages; company header repeated on pages
  2 and 3; column header repeated on page 2; the bottom group (notes + totals + baht-text + both
  signature boxes) stays atomic on page 3; footers read `หน้า 1 / 3`, `หน้า 2 / 3`, `หน้า 3 / 3`.
- **`?copy=true` works.** TI 47 / RC 33 / PV 54 with `copy=true` render the `สำเนา` watermark and drop
  `ต้นฉบับ`; `/paper?copy=true` returns `{"text":"สำเนา","variant":"warning"}`.
- **All trade-document money ties out.** Every `subtotalAmount` / `vatAmount` / `taxAmount` /
  `totalAmount` / `totalPaid` / `whtAmount` in the API JSON was found verbatim in the corresponding
  PDF, across 21 documents — including the WHT-deduct PV 40 (18,000 / 1,260 / 700 / 18,560), the
  self-withhold foreign-vendor PV 34 (WHT correctly shown as a note "ออกภาษีหัก ณ ที่จ่ายให้เอง
  3,529.41 บาท (นำส่งสรรพากรต่างหาก ไม่หักจากยอดจ่าย)" and **not** deducted from the payable), and the
  0.07-rounding BN 31 (999.00 / 69.93 / 1,068.93).
- **สปส.1-10 upload file is genuinely TIS-620.** 548 bytes; decodes cleanly as `tis-620` with Thai
  intact (`บริษัท ทดสอบ VAT (DUMMY) จำกัด`, `ทดสอบ`, `หนึ่ง`), raises on `utf-8` — i.e. correct, not
  double-encoded.
- **BE years on every RD form.** ภ.พ.30 / ภ.ง.ด.1 / 1ก / 3 / 53 / 54 / 51 / ภ.พ.01 / ภ.พ.09 / 50ทวิ /
  สปส.1-10 all print 25xx, none print 20xx. (Financial statements are the sole exception — F7.)
- **ภ.พ.36 → 404 confirmed** and not re-investigated (prior finding F3).

## Not testable on co5 — signature and stamp IMAGES (v1.26.1)

`GET /api/proxy/company-profile` returns `"stampUrl": null` and `"logoUrl": null`, and every
`/paper` response returns `signatures.leftUrl / middleUrl / stampUrl = null` — **co5 has no signature
or company-stamp attachment uploaded**, so the image half of the v1.26.1 feature cannot be exercised
here. Confirmed at the byte level: every document PDF embeds exactly **one** painted image XObject
(168×168 RGBA, md5 identical across all 20 documents), and decoding it yields the **TEAS product
logo**, not a stamp. The second image object in each file is that logo's `/SMask` (alpha channel),
never painted independently.

The **name** half of the feature does behave correctly and was verified in both directions on six
doctypes: Draft → `signatures: {}` and the parenthesised name falls back to the company name
(`( บริษัท ทดสอบ VAT (DUMMY) จำกัด )`) or a blank rule (`( ............................. )`);
Posted/Issued → the actual signer (`( ทดสอบ หัวหน้าบัญชี )`, `( ทดสอบ ฝ่ายขาย )`). The gate is
`PaperSignatureSource.ResolveAsync(..., isSigned)` — correct everywhere **except the Voided PV (F2)**,
where `isSigned: d.Status != "Draft"` lets a voided document present its approver.
No document prints a `ตำแหน่ง` line, because no user in co5 has a `Position` set (the renderer's
sign-off shape supports it — `PaperDocumentPdf.cs:399`).

## Known extraction artefacts — do NOT file these as defects

- `pdftotext` drops Thai combining marks (`อื่น` → `อื น`, `หน้า` → `หนา้`). Already in
  `troubles-wiki.md`; every Thai string in this report was verified against the API JSON, not the
  extraction.
- The official RD templates (`pnd30_main.pdf` etc.) extract with U+FFFD in their *static* label text
  (`สำ` → `ส�ำ`) and a Wingdings U+F0FC check glyph. This is the RD template's own font, not TEAS
  output — the app-rendered documents have zero U+FFFD.
- `-layout` reorders the two-column summary foot of the trade templates (label and amount land on
  different output rows). Confirmed harmless via `-raw` (content-stream order) on SO 19 and QT 37.

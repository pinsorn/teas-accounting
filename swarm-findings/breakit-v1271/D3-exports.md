# D3 — Data exports (CSV / TXT / RD batch files) — break-it sweep

**Target:** https://teas.kazaki-rio.com (v1.27.1, PROD) · company **co5** (`บริษัท ทดสอบ VAT (DUMMY) จำกัด`, id=5)
**Date:** 2026-07-30/31 (Bangkok) · **Users:** chief01, tax01, sales01 — all confirmed `companyId:5` via `GET /api/proxy/me` before any request.
**Method:** `curl` to a temp dir, **raw-byte inspection only** (`xxd`, byte-length, codec decode) — nothing was allowed to transcode. Repo read-only; no source edited, no commit.

> ⚠️ **Concurrency caveat.** Sibling agents were posting to co5 throughout this run. Observed live drift during the sweep: AR-aging total 19,872.31 → 19,979.31 (+107.00) and TB total 660,784.02 → 660,786.02. **Every totals-match claim below was re-taken atomically** (report JSON → export → report JSON again in one shell round) before being recorded. One apparent ภ.พ.30 CRIT (batch 45,033.00 vs report 45,133.00) was **retracted** after atomic re-test — it was a concurrent posting, not a bug.

---

## Export surface — summary table

| # | Export | Route | Gen | HTTP | Encoding | Formula guard | Totals match screen |
|---|--------|-------|-----|------|----------|---------------|---------------------|
| 1 | Trial Balance CSV | client-side Blob | FE | n/a | UTF-8 **+BOM**, **LF** | ✅ `csvCell` | ✅ (but see **D3-06**) |
| 2 | Balance Sheet CSV | client-side Blob | FE | n/a | UTF-8 +BOM, **LF** | ✅ `csvCell` | ✅ |
| 3 | P&L CSV | client-side Blob | FE | n/a | UTF-8 +BOM, **LF** | ✅ `csvCell` | ✅ |
| 4 | AP aging CSV | client-side Blob | FE | n/a | UTF-8 +BOM, **LF** | ✅ `csvCell` | ⚠️ **D3-01**, **D3-07** |
| 5 | Bank reconciliation CSV | client-side Blob | FE | n/a | UTF-8 +BOM, CRLF | ✅ `csvCell` | ✅ |
| 6 | AR aging CSV | `GET /reports/ar-aging/export` | **BE** | 200 | UTF-8 **+BOM**, CRLF | ✅ `CsvCell` (quote + `'`) | ❌ **D3-01** |
| 7 | General Ledger CSV | `GET /reports/general-ledger/export?format=csv` | **BE** | 200 | UTF-8 +BOM, CRLF | ✅ `CsvCell` | ⚠️ **D3-05** (4-dp raw) |
| 8 | General Ledger PDF | `…&format=pdf` | BE | 200 | PDF | n/a | ✅ |
| 9 | ภ.พ.30 batch (Format กลาง) | `GET /tax-filings/pnd30/batch-file` | **BE** | 200 | **UTF-8 no BOM**, CRLF ✅ | n/a — pipe-delimited; pipe stripped from values | ✅ **exact** |
| 10 | ภ.ง.ด.53 batch (Format กลาง) | `GET /tax-filings/pnd53/batch-file` | **BE** | 200 | **UTF-8 no BOM**, CRLF ✅ | n/a — `San()` strips pipe + RD-forbidden set | ✅ **exact** |
| 11 | ภ.ง.ด.3 batch | `GET /tax-filings/pnd3/batch-file` | BE | 422 (no data) | — | — | n/a |
| 12 | ภ.ง.ด.2 batch | `GET /tax-filings/pnd2/batch-file` | BE | 422 (no data) | — | — | n/a |
| 13 | สปส.1-10 SSO txt | `GET /payroll/runs/{id}/sso/file` | **BE** | 200 | **TIS-620/cp874 ✅, no BOM, CRLF, 135-byte records ✅** | n/a (fixed-width) | ✅ header totals tie | 
| 14 | ภ.ง.ด.1 / 1ก / 54 batch | — | — | **absent** | — | — | **D3-12** |
| 15 | sales-summary · tax-summary · outstanding-PO · customer-statement · vendor-ledger · wht-receivable | — | — | **no export button at all** | — | — | **D3-11** |

**Formula-injection verdict: CLOSED across the whole surface.** Both guards were verified against the live payloads, not just read:
- FE `frontend/lib/utils.ts:108 csvCell` — executed verbatim in node against the live payloads:
  ```
  csvCell("=cmd|' /C calc'!A0") → '=cmd|' /C calc'!A0
  csvCell("@SUM(1+1)*cmd")      → '@SUM(1+1)*cmd
  csvCell("-2+3+cmd")           → '-2+3+cmd
  csvCell("\tX")                → '<TAB>X
  ```
  All 5 FE CSV exports route **every** cell through it — including the first/leading cell of every row (the injection-relevant position) and the totals row.
- Backend `backend/src/Accounting.Api/Endpoints/ReportEndpoints.cs:21 CsvCell` — always `"` -quotes AND prefixes `'` on `= + - @ \t \r`. Confirmed live: AR-aging CSV emits `"บริษัท ลูกค้าทดสอบ จำกัด"` quoted.
- RD/SSO txt files are not CSV; `WhtBatchFormat.San()` strips `* + / \ ! $ % # & @ , ' " |` + CR/LF, `Pp30BatchFormat.AddressNo()` replaces the pipe, `SpsBatchFormat.Txt()` strips CR/LF/TAB and `Fit()` re-asserts the 135-byte record. No delimiter-injection vector found.

---

# FINDINGS

## 🔴 D3-01 [HIGH] AR & AP aging ignore `asOf` as a cutoff — a historical aging export returns TODAY's balances

The single worst defect found. Affects **export #6 (backend CSV)**, **export #4 (FE AP CSV)** and both JSON reports behind them. This is the exact report an auditor or a bank pulls for a prior year-end.

**Repro (prod, chief01):**
```
GET /api/proxy/reports/general-ledger?accountId=54&fromDate=2020-01-01&toDate=2026-06-30
→ {"openingBalance":0,"closingBalance":0,"rows":[]}        # AR control 1130 was ฿0.00 on 2026-06-30
GET /api/proxy/reports/ar-aging/export?asOf=2026-06-30
GET /api/proxy/reports/ar-aging/export?asOf=2020-01-01
GET /api/proxy/reports/ar-aging/export?asOf=1900-01-01
```
All three return byte-identical CSV (`md5 1d0f75e9…`, same as `asOf` omitted):
```
Customer,TaxId,Current,Bucket31To60,Bucket61To90,BucketOver90,Total
"บริษัท ลูกค้าทดสอบ จำกัด","0105567000315",13559.3100,0,0,0,13559.3100
"นายสมชาย ใจดี","1234567890121",6420.0000,0,0,0,6420.0000
"รวมทั้งหมด",,19979.3100,0,0,0,19979.3100
```
Total is **invariant across every `asOf` ever tried** — only the bucket assignment moves:

| asOf | Current | 31-60 | 61-90 | >90 | **Total** |
|---|---|---|---|---|---|
| 1900-01-01 / 2020-01-01 / 2026-01-01 / 2026-06-30 / 2026-07-01 / 2026-07-15 / 2026-07-31 | 19,979.31 | 0 | 0 | 0 | **19,979.31** |
| 9999-12-31 / 2026-12-31 | 0 | 0 | 0 | 19,979.31 | **19,979.31** |

AP aging behaves identically (`/reports/ap-aging?asOf=2020-01-01` → total **46,803.50**, same as `asOf=2026-07-31`).

**Expected:** `asOf=2026-06-30` → total ฿0.00 (the earliest posted AR document on co5 is 2026-07-18; the AR control account had no movement at all before July 2026). `asOf=1900-01-01` → ฿0.00.
**Actual:** ฿19,979.31 — i.e. a CSV that asserts ฿19,979.31 of receivables was outstanding on 1 Jan 1900, and that the company had ฿19,979.31 of AR at its 30 Jun 2026 half-year cut.

**Root cause (read-only confirmation)** — `backend/src/Accounting.Infrastructure/Reports/SubledgerReportService.cs:173`:
```csharp
var q = db.TaxInvoices.AsNoTracking()
    .Where(t => t.CompanyId == tenant.CompanyId && t.Status == DocumentStatus.Posted
             && t.PaymentStatus != "PAID");            // ← no  t.DocDate <= asOf
…
Amount = t.TotalAmount - t.AmountPaid,                 // ← CURRENT AmountPaid, not "paid as of asOf"
…
var age = asOf.DayNumber - x.DocDate.DayNumber;        // asOf used ONLY for bucketing
```
Two independent errors: (a) no document-date cutoff, so documents dated **after** `asOf` are included; (b) `AmountPaid` is the live figure, so payments received **after** `asOf` are already netted off. A historical aging is wrong in both directions.

**Not a period-state artifact** — the sibling agent's period open/close cannot change which documents exist, and the result is reproducible at 1900-01-01 where no data can exist at all.

---

## 🔴 D3-02 [HIGH] สปส.1-10 SSO upload file always ships employer account `0000000000` — no validation

**Repro (tax01):** `GET /api/proxy/payroll/runs/{7,8,12,13,15}/sso/file` → all HTTP 200.
Header record (cp874-decoded), positions 2–11 = เลขที่บัญชีนายจ้าง:
```
100000000000000002912691269บริษัท ทดสอบ VAT (DUMMY) จำกัด …
 ^^^^^^^^^^  employerAccountNo = "0000000000"
```
| run | records | employerAcct | branch | payDate | period |
|---|---|---|---|---|---|
| 7 | 4 | `0000000000` | 000000 | 300869 | 0869 |
| 8 | 4 | `0000000000` | 000000 | 290969 | 0969 |
| 12 | 4 | `0000000000` | 000000 | 291069 | 1069 |
| 13 | 6 | `0000000000` | 000000 | 281169 | 1169 |
| 15 | 5 | `0000000000` | 000000 | 291269 | 1269 |

`GET /api/proxy/company-profile` → `"ssoEmployerAccountNo": null`, and `CompanyProfileDtos.cs:152` only validates it `.When(x => !string.IsNullOrWhiteSpace(...))` — i.e. it is **optional master data with no downstream guard**.

**Expected:** the export refuses with a loud domain error naming the missing mandatory field — exactly the pattern the sibling ภ.พ.30 exporter already implements (`pp30_batch.missing_address`: *"Company registered address is incomplete; ภ.พ.30 requires: เลขที่…"*, verified live at `/tax-filings/pnd30/batch-file?period=202601`).
**Actual:** HTTP 200, a 135-byte-clean, perfectly-formed file that the SSO e-Service will reject on upload because the employer account (the submission's primary key) is all zeros. The user only discovers this at the government portal, after the filing deadline clock has started.

---

## 🔴 D3-03 [HIGH] สปส.1-10 files ship `?????????????` as insured-person names — silent lossy TIS-620 encode, zero validation

**Repro (tax01):** `GET /api/proxy/payroll/runs/15/sso/file` → 685 bytes. Raw hexdump (`xxd`), start of record 2:
```
00000080: 3033 3337 3530 300d 0a32 3139 3030 3030  0337500..2190000
00000090: 3030 3030 3031 3130 3939 3f3f 3f3f 3f3f  0000011099??????
000000a0: 3f3f 3f3f 3f3f 3f20 2020 2020 2020 2020  ???????
000000b0: 2020 2020 2020 2020 3f3f 3f3f 3f3f 2020          ??????
```
cp874-decoded (record 2, 135 bytes):
```
21900000000011099?????????????                 ??????                             00000003000000000000087500
 ^nationalId    ^099 ^ชื่อ(30)                   ^ชื่อสกุล(35)
```
(record 3 for comparison, intact: `21101700230708001ทดสอบ                         หนึ่ง …`)
| run | `?` bytes | corrupt payee names |
|---|---|---|
| 13 | **37** | `?????????????  ?????`, `????????????  ?????` |
| 15 | **19** | `?????????????  ?????` |

**Source is upstream master data, and it is already visible in the API:**
```
GET /api/proxy/employees →
{"employeeId":13,"employeeCode":"B4HIRE","fullNameTh":"???????????????? ??????","nationalId":"1900000000011",…}
{"employeeId":14,"employeeCode":"B4LEAVE","fullNameTh":"??????????????? ??????",…}
{"employeeId":15,"employeeCode":"B4ZERO","fullNameTh":"????????????????? ??????",…}
GET /api/proxy/payroll/runs/15/sso-schedule →
{"no":1,"ssoNumber":"1900000000011","title":"???","firstName":"?????????????","lastName":"??????",…}
```
This is the known **co7 one-byte-per-char class** (`troubles-wiki.md:839` — PowerShell-created Thai degraded to `?` before the request left the client). **New occurrence on co5, and it has now reached POSTED payroll runs 13 & 15**, so it flows into the SSO e-Service file, the ภ.ง.ด.1 PDF and the 50ทวิ PDF for those employees.

**Two distinct export-layer defects on top of the bad data:**
1. `SpsBatchFormat.BuildBytes` (`Encoding.GetEncoding(874).GetBytes(...)`) uses .NET's **default replacement fallback** — any character outside cp874 becomes `?` **silently**, no exception, no warning. A Bengali `ম` smuggled into a Thai name (the known TEAS glyph pitfall) would vanish into `?` here with no trace. Recommend `EncoderExceptionFallback` or an explicit pre-encode check.
2. Nothing validates that a payee name reaching a **government filing artifact** is non-empty / not all-`?`. `SpsBatchFormat.PrefixCode("???")` even resolves to `"099"` (its unknown bucket) instead of surfacing the corruption.

**Expected:** the export refuses (or at minimum warns) rather than filing `?????????????` as a real person's name to the Social Security Office.
**Actual:** HTTP 200, well-formed 135-byte records, garbage names.

*(Bengali `ম` sweep across all 34 downloaded export artifacts: **zero occurrences** — consistent with D1's PDF sweep. The `?` in `f_pnd1_15` (1021) is PDF binary noise, not text.)*

---

## 🟠 D3-04 [MED] `SALES_STAFF` — zero `report.*` permissions — can download the whole AR aging CSV

**Repro:** login `sales01` / `UxSwarm-2026-A1`, then the same 15 export/report routes:

| route | sales01 |
|---|---|
| `/reports/ar-aging/export?asOf=2026-07-31` | **200 · 334 B · text/csv** |
| `/reports/ar-aging?asOf=2026-07-31` | **200 · 838 B** |
| `/reports/ap-aging` · `/reports/trial-balance` · `/reports/balance-sheet` · `/reports/profit-loss` · `/reports/vat-register` · `/reports/pnd30` | 403 |
| `/reports/general-ledger/export` (csv **and** pdf) | 403 |
| `/reports/financial-statements/pdf` | 403 |
| `/tax-filings/pnd53/batch-file` · `/tax-filings/pnd30/batch-file` | 403 |
| `/payroll/runs/15/sso/file` · `/payroll/runs/15/pnd1/pdf` | 403 |

`GET /api/proxy/me/permissions` (sales01) → roles `["SALES_STAFF"]`, permissions contain **no `report.*` entry at all**:
```
master.business_unit.read, master.customer.read, master.product.read, sales.billing_note.*,
sales.credit_note.read, sales.debit_note.read, sales.delivery_order.*, sales.quotation.*,
sales.receipt.read, sales.sales_order.*, sales.tax_invoice.read, sys.attachment.*
```
**Cause:** `ReportEndpoints.cs:189` gates `/reports/ar-aging/export` on `Permissions.Sales.TaxInvoiceRead`, not on a `report.*` permission. It is the **only `/reports/*` route reachable without any report permission** — its own sibling AP aging requires `Purchase.VendorInvoiceRead` and correctly 403s.

This is *not* the F16 pattern (the export and the JSON share one guard, so no guard is being skipped) — it is a **privilege-boundary inconsistency**: a sales clerk exports every customer's name, 13-digit tax ID and outstanding balance by aging bucket — the company's full credit-exposure picture — while being denied every other financial report. Worth an explicit `report.ar_aging` (or `Report.*` co-requirement) decision rather than leaving it as an inherited sales permission.

---

## 🟠 D3-05 [MED] GL CSV export emits raw 4-decimal (sub-satang) money the on-screen report rounds away — CSV ≠ printed report

**Repro (chief01):**
```
GET /api/proxy/reports/general-ledger/export?accountId=53&fromDate=2026-01-01&toDate=2026-12-31&format=csv
```
tail of the file (account 1120 เงินฝากธนาคาร):
```
2026-07-31,"07-2026-JV-0141","D1 draft satang split",,33.3333,0.0000,-264502.3567
…
2026-12-31,,"ยอดยกไป",,25194.7833,636172.1400,-610977.3567
```
matching JSON: `{"totalDebit":25194.7833,"totalCredit":636174.14,"closingBalance":-610979.3567}`
On screen the same figures render through `formatTHB` → `Intl.NumberFormat('th-TH',{style:'currency',currency:'THB'})` → **฿25,194.78** and **-฿610,979.36**.

**Expected:** the export and the report agree to the satang, and no THB figure carries a third/fourth decimal.
**Actual:** the CSV carries `25194.7833` / `-610977.3567` / `33.3333` — physically impossible THB amounts (the **F22 sub-satang class, now confirmed live in the GL**, propagated verbatim into the export). A user reconciling the CSV against the printed report gets a 0.0033 break with no explanation, and the CSV's own column sums do not foot to the report footer.

Related, same class, **not raised as a separate finding** (another agent's write, attribution ambiguous): AR control account 1130 closes at **20,012.6434** for 2026-07 while the AR aging subledger totals **19,979.31** — the ฿33.33 gap is the `D1 draft satang split` JV posted straight to the control account.

---

## 🟠 D3-06 [MED] Trial-balance CSV computes the difference cell in JS floats — an unbalanced TB exports an 18-digit garbage cell

`frontend/app/(dashboard)/reports/trial-balance/page.tsx:30`:
```ts
lines.push([t('totalRow'), '', tb.data.totals.debit, tb.data.totals.credit,
  tb.data.totals.debit - tb.data.totals.credit].map(csvCell).join(','));
```
`csvCell(number)` is `String(v)` — no rounding, no formatting. Today co5's TB is balanced so the cell is a clean `0`:
```
Total,,660786.02,660786.02,0
```
But the subtraction is IEEE-754. Executed in node with the F22 skew values that made this reachable:
```
822801.785 - 822801.78  →  "0.005000000004656613"
660784.02  - 660784.01  →  "0.010000000009313226"
```
The on-screen footer computes the **identical** subtraction (`trial-balance/page.tsx:104`) but pipes it through `formatTHB` → `Intl.NumberFormat` currency, which rounds to 2 dp and hides the noise:
```tsx
<td …>{formatTHB(tb.data.totals.debit - tb.data.totals.credit)}</td>   // screen → ฿0.01
[…tb.data.totals.debit - tb.data.totals.credit].map(csvCell)           // CSV    → 0.005000000004656613
```
**Expected:** the exported difference cell equals what the report prints (a satang-rounded `0.01`).
**Actual (whenever the TB is *not* balanced — which the F22 sub-satang postings, confirmed live in D3-05, make reachable):** screen says `฿0.01`, CSV says `0.005000000004656613`. A direct screen-vs-export disagreement, in the one cell a reviewer looks at to decide whether the books balance. The CSV also drops the `balanced`/`unbalanced` badge the screen carries, so nothing in the file explains the number.

---

## 🟡 D3-07 [LOW] Three different numeric representations for the same money across the aging exports

Same figure, three renderings, all live right now:

| surface | AR/AP amount |
|---|---|
| Backend AR-aging CSV (export #6) | `13559.3100` — 4 dp, raw `decimal(19,4)` |
| FE AP-aging CSV (export #4) | `26803.5` — JS `String(26803.5)`, **1 dp** |
| On screen (both) | `฿13,559.31` / `฿26,803.50` — 2 dp |

Neither CSV is wrong arithmetically, but a Thai accountant importing both into one workbook gets three column formats, and the FE `26803.5` reads as "26,803.5 baht" to anyone scanning it. The two aging reports are a matched pair and should agree on 2 dp.

## 🟡 D3-08 [LOW] CSV line-ending inconsistency — 4 of 7 exports violate RFC 4180

| export | join |
|---|---|
| trial-balance, balance-sheet, profit-loss, ap-aging | `lines.join('\n')` — **LF** |
| bank-reconciliation | `join('\r\n')` — CRLF |
| ar-aging (BE), general-ledger (BE) | `.Append("\r\n")` — CRLF (explicit, with a code comment citing the platform-dependent-`AppendLine` lesson) |

Verified on the wire: `f_gl1120` CR=89 LF=89, `f_araging` CR=4 LF=4 (CRLF-clean). The backend got this right; four FE exports did not. Excel tolerates LF; several Thai bank/gov import tools do not.

## 🟡 D3-09 [LOW] SSO .txt is served as `text/plain` with no charset while the bytes are TIS-620

```
GET /payroll/runs/15/sso/file
Content-Type: text/plain                                   ← no charset
content-disposition: attachment; filename=sps1-10_202612.txt
bytes: single-byte cp874 (บริษัท = ba c3 d4 c9 d1 b7)
```
vs. the RD batch files which correctly declare theirs:
```
Content-Type: text/plain; charset=utf-8                    ← pnd53 / pnd30 batch-file
```
A UA that previews rather than downloads will decode the TIS-620 bytes as UTF-8 and mojibake every Thai name. `PayrollEndpoints.cs:112` should emit `text/plain; charset=windows-874`.

## 🟡 D3-10 [LOW] `format` is case-sensitive
`…/general-ledger/export?…&format=CSV` → **400** `{"detail":"format must be 'pdf' or 'csv'."}`. `format=csv` → 200. One `ToLowerInvariant()`. (`format=xlsx` → 400 and missing `format` → 400 are both correct.)

## 🟡 D3-11 [LOW] 6 of 14 report pages have **no export at all**
`sales-summary`, `tax-summary`, `outstanding-po`, `customer-statement`, `vendor-ledger`, `wht-receivable` — no export button, no backend export route (`grep -E "exportCsv|Blob|downloadFile"` over `frontend/app/(dashboard)/reports/*` returns nothing for these six). Four of them (sales-summary, tax-summary, outstanding-PO, customer-statement) were on this sweep's target list; they simply do not exist as exports. Customer-statement in particular is the artifact a customer asks to be sent.

## 🟡 D3-12 [LOW] No RD Format กลาง batch file for ภ.ง.ด.1 / 1ก / 54 — PDF only
`WhtBatchExportService.BuildAsync` accepts exactly `PND53 | PND3 | PND2` (exhaustive switch, throws otherwise); `PayrollEndpoints` exposes only `/pnd1/pdf` and `/pnd1a/pdf`; `TaxFilingEndpoints` only `/pnd54/pdf`. So monthly PIT WHT (ภ.ง.ด.1) — the highest-volume WHT return a payroll company files — must be keyed by hand into RD Prep while ภ.ง.ด.53/3/2 upload as files. Feature gap, not a defect.

## 🟡 D3-13 [LOW] Query-parameter naming drift across report routes
`/reports/profit-loss` takes **`from` / `to`** (`fromDate`/`toDate` → **400, empty body**), while `/reports/general-ledger`, `/reports/general-ledger/export`, `/reports/customer-statement`, `/reports/vendor-ledger` all take **`fromDate` / `toDate`**. Fails cleanly, but the 400 has a zero-byte body so an integrator gets no hint.

## ⚪ D3-14 [INFO] Exports are unbounded — no row cap anywhere
No `Take(...)` / paging in `Accounting.Infrastructure/Reports/`. Good news: **no silent truncation** (the stated risk). Bad news: nothing bounds an export either. co5's volume is too small to prove a timeout — the widest range tested, `0001-01-01 → 9999-12-31` on the busiest account (88 rows), returned in **0.32 s** consistently. Untested at scale; flagging so it is not read as "verified safe under load".

---

# What did NOT break (negative results, with evidence)

- **CSV formula injection — closed everywhere.** Both guard implementations verified by execution/live bytes, not by reading. All 7 CSV exports route every cell (including leading cells and totals rows) through a guard. No export was found that bypasses one.
- **Encodings correct where the spec demands it.** สปส.1-10 = TIS-620/cp874, no BOM, CRLF, **every record exactly 135 bytes** (5 files × 4–6 records, all `len=135`) — re-verified independently of D1's finding since this is a different generator. RD ภ.ง.ด./ภ.พ.30 batch files = **UTF-8 with no BOM** (first bytes `48 7c 30` = `H|0`, `31 7c 30` = `1|0`) — correct per FORMAT กลาง V2.0 note #6, which explicitly is *not* TIS-620. All CSVs carry the UTF-8 BOM (`ef bb bf`) so Thai renders in Excel.
- **ภ.พ.30 batch ties out to the on-screen return, exactly.** Atomic re-test, twice:
  `{"sales":45133.0000,"outputVat":3159.3100,"purchase":58050.0000,"inputVat":4063.5000,"netVatRefundable":904.1900}`
  → `1|0|99/9|10110|45133.00|||||45133.00|3159.31|58050.00|||4063.50|-904.19`
  ข้อ4 = ข้อ1−ข้อ2−ข้อ3 ✅ · ข้อ8/9 = ข้อ5−ข้อ7 ✅ (both derived from the emitted rounded values, so the importer's identity checks foot to the cent).
- **ภ.ง.ด.53 batch header/trailer counts foot the detail rows.** Preview totals `{"income":41000.0,"wht":1370.0}` = header `TOT_NUM=3 | TOT_AMT=41000.00 | TOT_TAX=1370.00 | SUR=0.00 | GTOT=1370.00 | TRANS=1370.00`; 3 detail rows present; per-row income sums 16,000 + 15,000 + 10,000 = 41,000 ✅, tax 380 + 690 + 300 = 1,370 ✅. Field counts: header 25, detail 38 ✅. RD-forbidden chars stripped as designed (`ค่านายหน้า / คอมมิชชั่น` → `ค่านายหน้า  คอมมิชชั่น`, the `/` removed).
- **Tenant isolation on the export routes holds.** `/reports/general-ledger/export?accountId={1,2,5,10,20,100,200}` (other companies' account ids) → **404 for every one**, `application/problem+json`, no data leak, no enumeration difference beyond body length.
- **Period / date validation is solid.** `period={0,1,202600,202613,209912,190001,999999,-202607,20260,2026071}` → **422 `tax_filing.bad_period`** with the yyyymm hint; `period=abc` / missing → 400. `fromDate > toDate` → 400 `"fromDate must be on or before toDate."`. `notadate`, `2026-02-30` → 400. `accountId=-1` → 404, `accountId=99999999999999999999` → 400. Payroll run 999 → 404 `payroll.not_found`.
- **Empty-data exports are clean, not crashes.** `pnd30/batch-file?period=202601` (no sales) → 422 `pp30_batch.no_data` with a Thai explanation; `pnd3`/`pnd2` for 202607 → 422 `wht_batch.no_data`; `ar-aging/export?customerId=999999` → **200, 117 bytes**, header + `รวมทั้งหมด` zero row. No 500 was produced by any input tried in this sweep.
- **No HTTP 500 anywhere.** Every failure mode observed was a typed 400/403/404/422.

---

## Artifacts
Raw downloads + headers kept at
`Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\188f3ba4-2441-4bcf-b448-cef644c33316\scratchpad\exp\`
(`f_*` = first sweep, `q_*`/`z_*` = edge-case sweep, `h_*` = response headers, `*.jar` = cookie jars).

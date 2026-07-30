# C1 — Period close / reopen + ledger immutability (co5, prod v1.27.1)

Target `https://teas.kazaki-rio.com` · company **co5 / id=5** (บริษัท ทดสอบ VAT (DUMMY)).
Auth `chief01 / UxSwarm-2026-A7` (+ `sales01 / UxSwarm-2026-A1` for a privilege check).
`GET /api/proxy/me` → `{"userId":17,"username":"chief01","companyId":5,...,"allowedCompanies":[{"id":5,...}]}`
confirmed before every write; chief01 is scoped to co5 only, so no co2/co3/co6 write was even reachable.
Session clock during the run: **2026-07-31 ~05:10–05:35 Asia/Bangkok = 2026-07-30 ~22:10–22:35 UTC**
(this landed the whole run inside the midnight–07:00 timezone window the dispatch asked about — see F3).

---

## CRIT (top, per dispatch)

**C1-F1 · HTTP 500 × 7 on the period endpoints — unvalidated year/month.**
Six write routes and one authenticated-read route return `500 internal_error` on out-of-range
year/month. Details + repro below. No data corruption (the exception is thrown before any DB write —
verified: no `AccountingPeriod` rows appeared), but the dispatch says any 500 is marked CRIT, and the
**GET** one is reachable by the *lowest-privilege* user in the system.

**No new posting-guard gap was found.** The sweep below is exhaustive over every GL-writing code path
(`Post*Async` caller inventory + live probes): **payroll (`F18`) is the only unguarded posting path.**
Every other posting/state-advancing endpoint refuses a closed period. No document reached `Posted`
inside a closed period other than payroll. No posted document could be mutated, deleted, voided or
re-posted (all 405/422). Trial balance stayed balanced end-to-end (final `debit 1,869,260.7850 =
credit 1,869,260.7850, balanced:true`).

---

## Guard-gap sweep — every posting / state-advancing endpoint

`period.closed` is thrown by `PeriodCloseService.EnsureOpenAsync`. Baseline for co5: **no explicit
`accounting_periods` row existed for any month of 2026**, so `IsOpenAsync` treats **2026-07 as the only
open month** and every other month as CLOSED — which let most of this sweep run with *zero* mutation of
shared period state (probes used doc dates in 2026-05 / 2026-06 / 2026-08).

| Endpoint | Guards period? | Evidence |
|---|---|---|
| `POST /tax-invoices` (create draft) | **YES** — date pinned | sent `docDate:2026-06-15` → `201 {"tax_invoice_id":48}`, stored `docDate:"2026-07-31"` (server pins to Bangkok-today, `TaxInvoiceService.cs:235`) |
| `POST /tax-invoices/{id}/post` | **YES** | `TaxInvoiceService.cs:502`; unreachable with a past date because docDate is pinned |
| `POST /receipts` | **YES** — date pinned | `ReceiptService.cs:61` |
| `POST /receipts/{id}/post` | **YES** | `ReceiptService.cs:398` |
| `POST /tax-adjustment-notes` (CN/DN) | **YES** — date pinned | sent `docDate:2026-06-15` → `201 {"note_id":6}`, stored `docDate:"2026-07-31"` |
| `POST /tax-adjustment-notes/{id}/post` | **YES** | `TaxAdjustmentNoteService.cs:136` |
| `POST /vendor-invoices` | **YES** — date pinned | sent `docDate:2026-06-15` → `201 {"vendor_invoice_id":25}`, stored `docDate:"2026-07-31"` (`VendorInvoiceService.cs:68`) |
| `POST /vendor-invoices/{id}/post` | **YES** + VAT-claim guard | VI 25 (claim period 202606) → `422 vi.claim_period_closed` — *"vat_claim_period 202606 is in a CLOSED accounting period. Set it to 202607…"*. Strongest guard in the system. |
| `POST /payment-vouchers` | **YES** (validator + service) | `docDate:2026-06-15` → `400 validation.docDateNotToday` |
| `POST /payment-vouchers/{id}/post` | **YES** | `PaymentVoucherService.cs:498` |
| `POST /journals/manual` | **YES** + fiscal-year | `docDate:2026-06-15` → `422 period.closed`; `2026-05-31` → `422 period.closed`; `2026-08-01` → `422 je.future_date` |
| `POST /journals/{id}/post` | **YES** | re-post JV 288 → `422 period.closed` |
| `POST /expense-claims/{id}/pay` | **YES** (posts at Bangkok-today) | `ExpenseClaimService.cs:251-252` — postDate = `TodayInBangkok()`, back-dating impossible |
| `POST /fixed-assets/{id}/dispose` | **YES** | `disposalDate:2026-06-15` → `422 period.closed` |
| `POST /fixed-assets/{id}/write-off` | **YES** | `date:2026-05-20` → `422 period.closed` |
| `POST /depreciation-runs` | **YES** | `{year:2026,month:6}` → `422 period.closed`; `{2026,8}` → `422 period.closed` |
| `POST /bank-accounts/{id}/lines/{lid}/journal` | **YES** (on `line.TxnDate`) | imported a K-Biz CSV with a 15-06-26 line (`statementLineId:6`) → inline JE → `422 period.closed`. This is the only genuinely user-controlled posting date and it is guarded. |
| `POST /payroll/runs/{id}/post` | **NO — F18** | live: runs `08-2026-PR-0001`…`12-2026-PR-0001` are **POSTED** although 2026-08…2026-12 all report `{"open":false}` |
| `POST /payroll/runs/{id}/pay` | **NO — F18** | same; runs 13 (202611) & 15 (202612) are posted-and-unpaid, `/pay` would post a settlement JE into a closed month |
| `POST /periods/{y}/close-year` (closing JV) | **bypass BY DESIGN** | `GlPostingService.PostClosingEntryAsync`, documented `YearCloseService.cs:17` |
| `POST /tax-filings/pnd36?mode=finalize` | inherits the JV guard | `WhtFilingService.cs:331-332` — but see F6 note: it calls `CreateDraftAsync` *then* `PostAsync`, so a closed target period leaves an orphan **draft** JV behind |
| `POST /periods/{y}/{m}/close` | n/a | see F5/F6 |
| `POST /periods/{y}/{m}/reopen` | fiscal-year guarded | `period.year_closed` (code `PeriodCloseService.cs:130-136`); not live-testable on co5 (FY2026 open, and chief01 cannot reach co6) |

**Caller inventory (exhaustive, code):** every call site of `IGlPostingService.Post*Async` outside
`GlPostingService` itself is `JournalService:203`, `BankReconciliationService:255`,
`FixedAssetService:271,391`, `ExpenseClaimService:282`, `PaymentVoucherService:658`,
`VendorInvoiceService:393`, `ReceiptService:560`, `TaxInvoiceService:545`, `TaxAdjustmentNoteService`,
`YearCloseService:155,211` — and **`PayrollRunService:262`**. All but payroll (and the by-design
year-close) sit behind an `EnsureOpenAsync`. **Answer to the dispatch's core question: no other path
shares F18's gap.**

---

## Immutability sweep — all PASS

| Attack | Result |
|---|---|
| `DELETE /tax-invoices/48` (posted) | `405` |
| `POST /tax-invoices/48/post` (re-post) | `422 ti.not_draft` |
| `DELETE /journals/288` (posted) | `405` |
| `POST /journals/288/post` (re-post) | `422` (period guard fires first — see F8) |
| `DELETE /vendor-invoices/24` | `405` (no DELETE route at all — see F7) |
| `POST /payment-vouchers/54/post` (posted) | `422 pv.not_approved` |
| `POST /payment-vouchers/54/cancel` (posted) | `422 pv.cannot_cancel` |
| `DELETE /tax-adjustment-notes/1` (posted) | `422 note.cannot_delete_after_post` |
| `POST /tax-adjustment-notes/1/post` (posted) | `422 note.not_draft` |
| Edit a posted TI / RC / VI / PV / JE | **no PUT route exists** on tax-invoices, receipts, journals, payment-vouchers |
| Mutate a journal line directly | **no route exists** |
| `POST /vendor-invoices/{id}/claim-period` on a posted VI | code: `422 vi.not_draft` — *"vat_claim_period is frozen once posted (ม.82/4)"* |
| `DELETE /payroll/runs/{id}` on a posted run | code: `422 payroll.not_draft` (draft-only) |

---

## FINDINGS

### C1-F1 · CRIT (robustness) · Unvalidated year/month → HTTP 500 on 7 period routes
`PeriodCloseService.CloseAsync:56` / `ReopenAsync:128-129` build `new DateOnly(year, month, 1)` and
`DateTime.DaysInMonth(year, month)`, and `YearCloseService.FiscalBoundsAsync:40` builds
`new DateOnly(fiscalYear, c.FiscalYearStartMonth, 1)` — **before** validating the route values.
`ArgumentOutOfRangeException` escapes as a generic 500.

Repro (all returned `{"type":"urn:teas:error:internal_error","status":500}`):
```
POST /api/proxy/periods/2026/13/close        -> 500
POST /api/proxy/periods/2026/0/close         -> 500
POST /api/proxy/periods/0/1/close            -> 500
POST /api/proxy/periods/99999/12/close       -> 500
POST /api/proxy/periods/2026/13/reopen       -> 500
POST /api/proxy/periods/99999/close-year     -> 500
GET  /api/proxy/periods/99999/year-status    -> 500      <-- read-only, RequireAuthorization() only
GET  /api/proxy/periods/0/year-status        -> 500
```
The GET is gated by bare `.RequireAuthorization()` (no permission), so **any** logged-in user can
trigger it — confirmed with the low-priv account:
`sales01` → `GET /periods/99999/year-status` = **500**, while `POST /periods/2026/13/close` = 403.
Contrast: `POST /depreciation-runs {"year":2026,"month":13}` correctly returns
`400 validation` — the period endpoints are the outlier, they have no FluentValidation.
**Expected:** 400/422 validation error. **Actual:** unhandled exception → 500 (+ stack noise in prod logs).
No state change (verified: no period rows created).

### C1-F2 · HIGH · Depreciation ⇄ period-close **deadlock** — a month can become permanently unclosable, and the fiscal year can then never be closed
Three guards form a cycle with no exit:
* `PeriodCloseService.CloseAsync:73-85` — refuses to close a month if any Active asset with remaining
  depreciable base exists and that month has no Posted `DepreciationRun` → `period.depreciation_required`.
* `FixedAssetService.GenerateDepreciationAsync:300` — refuses to *generate* that run unless the month
  is OPEN → `period.closed`.
* `PeriodCloseService.ReopenAsync:152-154` — refuses to reopen a month that is not `Closed` →
  `period.not_closed`.

A month that rolled over without its depreciation run being generated is implicitly CLOSED
(`IsOpenAsync`: a missing row for a non-current month = closed) and is therefore **locked out of all
three transitions forever**.

Live proof on co5 for **2026-08** (zero writes — all three refused):
```
POST /periods/2026/8/close      -> 422 period.depreciation_required
                                   "Depreciation for 2026-08 must be generated before closing"
POST /depreciation-runs {2026,8}-> 422 period.closed
                                   "Period 2026-08 is CLOSED. Reopen the period or correct doc_date."
POST /periods/2026/8/reopen     -> 422 period.not_closed
GET  /periods/2026/8/status     -> {"open":false}
```
Same for 2026-09 (`POST /periods/2026/9/close` → `period.depreciation_required`). Driver on co5 is
fixed asset id 3 (`07-2026-FA-0001`, cost 120,000, `depreciationStartDate 2026-07-22`,
accumulated 3,333.33 < base) — every month from 2026-08 onward demands a run that can never be created.

**Downstream consequence (verified):** `YearCloseService.CloseAsync:98-104` requires all 12 months to
carry an explicit `Closed` row, so **co5's FY2026 can never be year-closed**:
`POST /periods/2026/close-year` → `422 year.periods_not_closed — "Months still open: 2026-01 … 2026-12"`.
On a real tenant this is a hard year-end-close blocker with no in-product recovery path (there is no
"reopen a never-closed period" and no delete-period API) — it requires a DB edit.
**Expected:** either the close guard should offer/auto-run the missing depreciation, or
`GenerateDepreciationAsync` should be allowed for a period being closed, or `ReopenAsync` should accept
an implicitly-closed month. **Actual:** three-way deadlock.

### C1-F3 · HIGH · Trial Balance / Balance Sheet default as-of is **UTC**, AR/AP aging is **Bangkok** → the subledger↔control tie-out is broken for 7 hours every day (live, quantified)
Reproduced at 2026-07-31 05:30 Bangkok (= 2026-07-30 22:30 UTC), all with **default parameters**:

```
GET /api/proxy/reports/ap-aging          -> "asOf":"2026-07-31"   control 2110 = 46,803.5000
                                            reconciliation: difference 0.0000, balanced:true
GET /api/proxy/reports/trial-balance     -> account 2110 net Cr   = 36,103.5000
GET /api/proxy/reports/balance-sheet     -> "asOfDate":"2026-07-30"
GET /api/proxy/reports/ar-aging          -> "asOfDate":"2026-07-31"
```
Same moment, same company, same defaults: **AP control = 46,803.50 on the aging report but 36,103.50
on the Trial Balance — a 10,700.00 THB discrepancy**, with the aging report simultaneously claiming
`balanced:true`. Whole-TB effect:
```
GET /reports/trial-balance                     -> totals 660,786.0200
GET /reports/trial-balance?asOfDate=2026-07-30 -> totals 660,786.0200   (identical → default = UTC date)
GET /reports/trial-balance?asOfDate=2026-07-31 -> totals 822,910.7850
```
i.e. the default TB silently omits **162,124.765 THB** of the current Bangkok day. Every day between
00:00 and 07:00 ICT the TB/BS are one day behind AR/AP aging. Worst case: a month-end TB printed on
the last day of the month before 07:00 excludes that entire day's sales.
**Expected:** one clock (`IClock.TodayInBangkok()`) for every report default.
**Actual:** TB + Balance Sheet use `UtcNow`; AR + AP aging use Bangkok-today.
(This is the live, quantified form of the F5 family; filing it because the AP-control ≠ AP-aging split
is a distinct, money-visible consequence that a default-parameter report run reproduces on its own.)

### C1-F4 · MED-HIGH · A period **reopen** is unauditable — no API can read it, and the reopen erases the prior close record
1. `PeriodCloseService.ReopenAsync:157-159` writes an activity row for entity type `"AccountingPeriod"`.
   `ActivityEndpoints.cs:15-32` exposes `/{docType}/{id}/activity` for exactly 13 doc types —
   quotations, sales-orders, delivery-orders, tax-invoices, receipts, credit-notes, debit-notes,
   billing-notes, purchase-orders, vendor-invoices, payment-vouchers, wht-certificates, payroll-runs.
   **`AccountingPeriod` is not among them, and `IActivityQueryService` has no other call site.** The
   reopen audit record is write-only — unreachable through any endpoint. Confirmed live:
   `GET /activity?entityType=AccountingPeriod` → 404, `GET /accounting-periods/{id}/activity` → 404.
2. `ReopenAsync:147-151` NULLs `ClosedAt`, `ClosedBy` and `CloseNotes` on the period row, so the
   readable surface loses *who closed it, when, and why*; a subsequent re-close overwrites them with
   the new closer.
3. `CloseAsync` records **nothing** to the activity log at all (only the row fields).
4. A JE back-dated into the reopened month stores `postingDate = docDate`, not the real posting date:
   `GET /journals/288` → `{"docDate":"2026-06-15","postingDate":"2026-06-15","postedAt":"2026-07-30T22:17:30.905+00:00"}`.
   The journal *list* DTO exposes `docDate` only, not `postedAt`.

Net: I closed 2026-06, reopened it, posted two back-dated JEs into it and re-closed it in 27 seconds,
and **no readable API surface shows that 2026-06 was ever reopened**. The only residue is
`postedAt` on the individual JEs (detail endpoint only). For the single most audit-sensitive action in
a Thai statutory ledger ("who unlocked the closed books?") this is a compliance gap.
**Expected:** period close/reopen visible in an audit/activity read surface, prior close metadata
preserved (append-only). **Actual:** unreadable + overwritten.

### C1-F5 · MED · Two contradictory definitions of "period open" ship in the same API
`PeriodCloseService.IsOpenAsync` treats a **missing** period row as CLOSED for every non-current month.
`YearCloseService.GetStatusAsync:72` maps a missing row to **`"Open"`**, and `CloseAsync:98-104` uses
the same rule in its error text. Live, same moment, same company:
```
GET /periods/2026/1/status  … /periods/2026/12/status   -> {"open":false} for all except 2026-07
GET /periods/2026/year-status -> every month "status":"Open"  (except the one I explicitly closed)
POST /periods/2026/close-year -> "Months still open: 2026-01, 2026-02, … 2026-12"
```
The period screen / year-close checklist tells the accountant 11 months are Open and must be closed,
while every posting into those months is already rejected with `period.closed`. The two surfaces can
never be reconciled by the user, and the year-close instruction ("close these months") runs straight
into F2's deadlock.
**Expected:** one definition. **Actual:** two, disagreeing on 11 of 12 months.

### C1-F6 · MED · Close precondition only checks draft **TI / PV / JE** — a draft VI, CN/DN, Receipt or Expense Claim does not block close, and is then permanently stranded
`PeriodCloseService.CloseAsync:60-68` queries `TaxInvoices`, `PaymentVouchers`, `JournalEntries` only.
Not checked: `VendorInvoices`, `TaxAdjustmentNotes`, `Receipts`, `ExpenseClaims`, `PayrollRuns`,
draft `FixedAssets`, draft `DepreciationRuns`.
Because every draft's `DocDate` is server-pinned to Bangkok-today, the only period in which such a
draft can exist is the *current* month — exactly the month an accountant closes at month-end. Once it
closes, `VendorInvoiceService.PostAsync:363` re-checks `EnsureOpenAsync(vi.DocDate)` against the
**stored** (now-closed) date, so the draft VI can never be posted — and F7 means it can never be
deleted either.
Live corroboration: the guard fires correctly when a draft **TI** exists —
`POST /periods/2026/7/close` → `422 period.draft_present` ("Cannot close period — draft fiscal
documents still exist") — while draft VI id 25 and draft CN id 6, both `docDate 2026-07-31`, were
sitting in the same period and contributed nothing to that refusal. (co5 currently holds 6 draft VIs
in 2026-07 — ids 8, 9, 10, 22, 24, 25 — none of which would block a month-end close.)
**Expected:** all fiscal documents in the period block the close (or are auto-voided).
**Actual:** three of eight document families are checked.

### C1-F7 · LOW · A draft Vendor Invoice can be neither deleted nor cancelled
`VendorInvoiceEndpoints.cs` exposes only `POST /`, `PUT /{id}`, `POST /{id}/claim-period`,
`POST /{id}/post`, `GET /`, `GET /{id}`. There is no `DELETE` and no `/cancel`.
`DELETE /api/proxy/vendor-invoices/25` → **405**. Compare tax-adjustment-notes, which has a working
`DELETE` (`DELETE /tax-adjustment-notes/6` → 204). An erroneous draft VI is permanent clutter
(and, with F6, permanently unpostable once its month closes).

### C1-F8 · LOW · Period guard fires before the document-status check on `POST /journals/{id}/post`
`JournalService.PostAsync` runs `EnsurePostableDateAsync(entry.DocDate)` before validating status, so
re-posting an already-Posted JE in a closed month answers `422 period.closed — "Reopen the period or
correct doc_date"` instead of a not-draft error. Repro: `POST /journals/288/post` → `period.closed`
(JV 288 is already Posted). Misleading remediation advice; also leaks period state before the caller's
request is known to be valid. Every other doc type answers `*.not_draft` here.

### C1-F9 · INFO · `GET /periods/{y}/{m}/status` answers 200 for impossible periods
`{"open":false}` returned for `2026/13`, `2026/0`, `0/1`, `-1/5`, `99999/1` — the read path never
constructs a date, so it happily answers for month 13, while the close route on the same values 500s
(F1). Inconsistent input contract across the same resource.

### C1-F10 · INFO · Client-supplied `docDate` is silently overridden, not rejected
TI / VI / CN-DN / RC accept a request `docDate` and store Bangkok-today instead, returning `201` with
no warning: `POST /tax-invoices {"docDate":"2026-06-15"}` → `201 {"tax_invoice_id":48}`, then
`GET /tax-invoices/48` → `"docDate":"2026-07-31"`. (PV is the outlier and correctly rejects with
`400 validation.docDateNotToday`.) A caller — or an MCP agent — attempting to back-date gets a success
response and a document dated somewhere else. The pinning itself is correct per ม.86/4(7); the silent
part is the defect. Recommend PV's explicit 400 everywhere.

### Cross-corroboration (not my area, not re-filed)
* **F18 confirmed live and broader than a single run:** payroll runs `08-2026-PR-0001`,
  `09-2026-PR-0001`, `10-2026-PR-0001`, `11-2026-PR-0001`, `12-2026-PR-0001` are all `POSTED` while
  `GET /periods/2026/{8..12}/status` all return `{"open":false}`. Runs 13 (202611) and 15 (202612) are
  posted-and-unpaid, so `/pay` will post settlement JEs into closed months on demand.
* **A1's duplicate-doc-number bug also affects the VI series:** `07-2026-VI-0003` is carried by two
  distinct posted vendor invoices (ids 7 and 13).

### Ruled out (do not re-chase)
An apparent "HTTP 400 with empty body when the JSON body contains an em-dash / smart quote" was a
**local artifact** of the Windows shell transcoding the command line to CP1252, not a product bug.
Re-sent byte-identical UTF-8 (`E2 80 94`) from a file → `422 period.closed` as expected. Server-side
UTF-8 handling is fine.

---

## Period state — restored

Baseline captured before any write (`GET /periods/2026/{m}/status` for all 12 months + `year-status`):
**co5 had no explicit `accounting_periods` row for any month of 2026**; only **2026-07** was open
(implicit current month), all other months closed-by-absence; **FY2026 not closed**.

| Period | Original | What I did | Final | Restored? |
|---|---|---|---|---|
| **2026-06** | no row → `{"open":false}` | closed → double-close refused → **reopened** → 2 JEs posted → **re-closed** | explicit **Closed** row, `closedAt 2026-07-30T22:17:57Z`, `{"open":false}` | **YES (behaviour identical)** — see note |
| 2026-07 | `{"open":true}` (current) | close attempted → `422 period.draft_present` | `{"open":true}` | YES — untouched |
| 2026-08 | `{"open":false}` | close / depreciation-run / reopen all attempted → all 422 | `{"open":false}`, still no row | YES — untouched |
| 2026-09 | `{"open":false}` | close attempted ×3 → `422 period.depreciation_required` | `{"open":false}`, still no row | YES — untouched |
| 2026-05 | `{"open":false}` | reopen attempted → `422 period.not_closed`; JV probe → 422 | `{"open":false}`, still no row | YES — untouched |
| 2026-01…04, 10…12 | `{"open":false}` | read-only | `{"open":false}` | YES — untouched |
| 2026/13, 2026/0, 0/1, 99999/12 | n/a | close/reopen attempted → 500 | no rows created (verified) | YES |
| **FY2026** | not closed | `close-year` → `422 year.periods_not_closed`; `reopen-year` → `422 year.not_closed` | **not closed** | YES — **no year-end close was performed** |
| co2 / co3 / co6 | n/a | **never touched** (chief01 is scoped to co5 only) | — | YES |

Final verification (post-run):
```
2026-01..05 {"open":false} · 2026-06 {"open":false} · 2026-07 {"open":true} · 2026-08..12 {"open":false}
year-status 2026: isClosed=false, allPeriodsClosed=false
trial-balance asOf 2026-12-31: debit 1,869,260.7850 = credit 1,869,260.7850, balanced:true
```

**One residual difference I could not fully undo (disclosed):** 2026-06 previously had *no*
`accounting_periods` row; it now has an explicit `Closed` row. Posting behaviour is byte-identical
(both states reject every posting with `period.closed`), and the guard/year-close semantics are
unchanged or improved. The only visible difference is cosmetic: `GET /periods/2026/year-status` now
shows month 6 as `"Closed"` instead of `"Open"`. There is **no delete-period API**, so a row cannot be
removed once created — reverting would require a DB edit, which is out of scope for a QA run. Note
that the "Open" it previously displayed was itself wrong (F5).

## Test data created on co5 (permanent, disclosed)
* `TI 48` — posted, `07-2026-TI-0025`, 107.00 incl. VAT 7.00, docDate 2026-07-31. Posted docs are
  immutable; cannot be removed.
* `JV 288` — `06-2026-JV-0003`, **2026-06-15**, Dr 1110 เงินสด 1.00 / Cr 1120 เงินฝากธนาคาร 1.00.
* `JV 289` — `06-2026-JV-0004`, **2026-06-01**, Dr 1110 1.00 / Cr 1120 1.00.
  (Both posted during the 27-second June reopen window; combined effect on co5: 1110 +2.00, 1120 −2.00
  in June. Any June tie-out off by exactly 2.00 is mine.)
* `VI 25` — draft, `docDate 2026-07-31`, vendorTaxInvoiceNo `C1-JUN-001`, 107.00, claim period 202606
  (unpostable by design). **Undeletable** — see F7.
* Statement import `3` on bank account 1 — one unmatched June line (`statementLineId 6`, 1,000.00,
  2026-06-15). Left unmatched; no JE was created from it (the guard refused).
* `CN 6` — created then **deleted** (204). No residue.
* No customer/vendor/product/employee master rows were created.

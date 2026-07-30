# A1 — Sales Chain break-it (co5, VAT) — v1.27.1 prod

Target: https://teas.kazaki-rio.com · company **co5** (บริษัท ทดสอบ VAT (DUMMY), id=5, VAT).
Auth: sales01 / `UxSwarm-2026-A1` (dispatch pw wrong; correct suffix = agent ordinal A1, not "sales").
Posting scope via chief01 / `UxSwarm-2026-A7`. `GET /me` confirmed **companyId:5** on both before every write.
All writes on co5 only. No cross-tenant write. Date driven = 2026-07-31 (period 2026-07).

## CRIT
**NONE.** No HTTP 500, no Dr≠Cr on any posted doc, no cross-company data. Trial Balance stayed
Dr=Cr through every attack (final 514,505.64 = 514,505.64); AR subledger reconciled to control 1130.

## PASS / FAIL per sub-area
| Sub-area | Result |
|---|---|
| R1 happy path QT→SO→DO→IV→TI→RC, tie to hand-calc | **PASS** — every hop = hand-calc (2,500 / VAT 175 / 2,675) |
| R1 three-way tie ภ.พ.30 vs sales-summary vs TB | **FAIL** — sales-summary excludes CN/DN (F3) |
| R2 CN vs a PAID TI (partial) | **PASS** books (ภ.พ.30 credit-carry + TB correct); surfaced dup CN number (F1) |
| R2 billing note (ใบวางบิล) from 2 invoices | **PARTIAL** — sum exact; but TIs+manual mix drops linked TI (F4); zero-BN accepted (F5) |
| R2 VAT math edges (0%/7%, half-satang, 100% disc) | **PASS** mostly; per-line rounding +0.01 (F6); zero-total TI accepted (F7) |
| R2 over / partial receipt | **PASS** — 422 rc.overpaid both cases |
| R2 sequence / immutability (edit/delete posted, receive draft) | **PASS** — 405 / 422 |
| R2 concurrency (2 TI posts back-to-back) | **PASS** — unique numbers, no 23505 |
| Doc-numbering integrity (period-wide) | **FAIL** — 4 dup TI numbers + live-reproduced dup CN (F1); audit blind (F2) |
| RBAC (sales01 tax-invoice create) | **FAIL** — create-TI leaks via billing-note route (F8) |

---

## FINDINGS

### F1 · HIGH · Duplicate tax-document running numbers on prod co5 (ใบกำกับภาษี / ใบลดหนี้) — live-reproduced
Each ใบกำกับภาษี / ใบลดหนี้ must carry a unique running number (RD). On co5 the doc-number
sequence counters are **behind the max existing row**, so new posts reuse existing numbers.

Evidence — VAT register `GET /reports/vat-register?year=2026&month=7` (sales side), duplicate docNos:
```
07-2026-TI-0001  2026-07-18  7000/490   AND  07-2026-TI-0001  2026-07-20  1000/70
07-2026-TI-0002  2026-07-18  5000/350   AND  07-2026-TI-0002  2026-07-20  1000/70
07-2026-TI-0003  2026-07-19  2000/140   AND  07-2026-TI-0003  2026-07-20  1000/70
07-2026-TI-0004  2026-07-20  1000/70    AND  07-2026-TI-0004  2026-07-20  1000/70   (same date!)
```
**Reproduced LIVE (R2-A):** posted a credit note → `{"docNo":"07-2026-CN-0001", ...}` — identical to
the pre-existing posted CN id=1 (07-2026-CN-0001, 2026-07-19). `GET /tax-adjustment-notes` now returns
**two** posted `07-2026-CN-0001` (id=1 and id=5). The CN counter issued 0001 again instead of 0002 →
it will keep colliding on every future CN ("permanently behind" — matches known numbering bug,
PROGRESS-vat-usage-drive.md L76 / obs 18641).
Note: the TI series has since caught up (live concurrency test produced unique 0022/0023/0024), so the TI
duplicates are stale historical rows; the **CN series is still behind and actively minting duplicates**.
Expected: unique per doc-type/period. Actual: reused numbers on posted, immutable tax docs (no void).

### F2 · MED · Number-gap audit is blind to duplicates → false "compliant"
`GET /reports/number-gaps?year=2026&month=7&doc_type=TI` (and CN, RC) returns `{"gaps":[],"hasGaps":false}`
for the exact period that contains 4 duplicate TI numbers **and** a duplicate CN (F1). The compliance
control that should catch numbering problems only detects **missing** numbers, never **reused** ones, so a
period with reused ใบกำกับภาษี numbers reports clean. Expected: the audit flags duplicates too.

### F3 · MED · sales-summary report excludes credit/debit notes → cannot reconcile to ภ.พ.30 / TB
`GET /reports/sales-summary` sums **only** posted `TaxInvoiceLines` (code-confirmed:
`FinancialReportService.SalesSummaryAsync`, joins TaxInvoiceLines↔TaxInvoices where Status=Posted; no
TaxAdjustmentNotes). It never nets posted credit notes (or adds debit notes), so it disagrees with ภ.พ.30,
the VAT register and TB whenever a CN/DN exists in the period.
Evidence (2026-07, same instant):
```
PND30         net sales 44,433.00   outputVAT 3,110.31   (nets CN)
sales-summary subtotal  46,433.00   vat       3,250.31   (TIs only)
gap            +2,000.00            +140.00   == exactly the 2 posted credit notes (−1,000/−70 each)
TB: 4000 sales Cr 45,100 − 4100 returns Dr = net; 2151 output-VAT net = 3,110.31 == PND30 ✓
```
PND30 and TB agree; sales-summary is the outlier. A user reconciling "sales" to their ภ.พ.30 sees an
unexplained CN-sized gap. Expected: all three agree on net sales + output VAT.

### F4 · MED · Billing note: manual Lines silently override linked TaxInvoiceIds (O2b class)
`POST /billing-notes` with **both** `TaxInvoiceIds:[29]` and a manual `Lines` row: BN is created with
`taxInvoices:[29]` recorded in the join table, but its line items/total reflect **only** the manual row.
```
req: TaxInvoiceIds=[29 (TI-0019, total 2,140)] + manual line {qty1 × 999}
BN-31: taxInvoices=[29], lines=[only the 999 row], subtotal 999, vat 69.93, total 1,068.93
```
The ใบวางบิล claims to bill invoice TI-0019 (2,140) yet its total excludes it. Total contradicts the
invoice it references. Validator only requires `Lines` non-empty when `TaxInvoiceIds` is empty, so the
"both supplied" path is unguarded. Expected: reject, or bill TI lines + manual lines coherently (no silent drop).

### F5 · LOW · Zero-value billing note accepted
`POST /billing-notes` with a single `{Quantity:0, UnitPrice:0}` row (no TIs) → 201, BN-30 subtotal/total 0.
A ใบวางบิล for 0 baht with a phantom row is accepted. Expected: reject an all-zero billing note.

### F6 · LOW · Per-line VAT rounding overstates invoice VAT vs base×rate (half-satang)
Two lines of 2.50 each (VAT7): each line VAT = 0.175 → rounded **per line** to 0.18 (away-from-zero) →
invoice VAT = 0.36 on a taxable base of 5.00, whereas 5.00 × 7% = **0.35**. Per-line half-satang rounding
inflates invoice VAT by 0.01 vs base×rate. (Single-line 7.50 → 0.525 → 0.53 is fine.) Methodology note —
per-line rounding can diverge from the ภ.พ. base×rate a tax auditor computes.

### F7 · LOW · 100%-discount line → zero-total tax invoice accepted
`POST /tax-invoices` line `{qty1 × 1000, DiscountPercent:100}` → 201, lineAmount 0, discount 1,000,
VAT 0, total 0. A ใบกำกับภาษี for 0 baht is creatable. Expected: at minimum flag/deny a zero-total TI.

### F8 · MED · RBAC — Sales Staff denied direct TI create, but can create a TI draft via the billing-note route
sales01 (Sales Staff) lacks `TaxInvoiceCreate`/`TaxInvoicePost`:
```
sales01 POST /tax-invoices            -> 403
sales01 POST /tax-invoices/{id}/post  -> 403   (R1)
sales01 POST /receipts (+/post)       -> 403   (R1)
```
But the billing-note lifecycle route creates a Tax Invoice while gated only on `BillingNoteManage`:
```
sales01 POST /billing-notes/{id}/create-tax-invoice  -> 200  {"tax_invoice_id":40}  (draft)
```
So a role explicitly denied direct TI creation can still mint a TI draft. Blast radius limited (draft
only; posting still needs `TaxInvoicePost`; API/lifecycle-created drafts are flagged for human approval),
but the create-a-tax-invoice capability leaks past the `TaxInvoiceCreate` gate. Expected: the create-TI
lifecycle route also requires `TaxInvoiceCreate`.

---

## Defended (attacks that correctly FAILED — good)
- Over-receipt 2,000 > 1,070 → **422 `rc.overpaid`** ("exceeds outstanding 1070"); partial 600 then 600 > 470 remainder → **422** (outstanding tracked correctly).
- Edit posted TI (`PUT /tax-invoices/31`) → **405**; delete posted TI (`DELETE`) → **405** (no mutation routes; immutable).
- Receive against a DRAFT (unposted) TI → **422 `rc.ti_not_posted`** ("must be POSTED").
- VAT7 line sent with `TaxRate:0` (0-VAT injection) → normalized server-side to 0.07 → VAT 70 (documented hole is closed on TI + all chain-entry paths via SalesLineBackstop.Resolve).
- Mixed 0% (VAT-OUT-0-EXP) + 7% → correct split: taxable 1,000 / nonTaxable 1,000 / VAT 70.
- 2 concurrent TI `/post` → unique 07-2026-TI-0023 / -0024, no 23505 (live concurrency safe).
- Trial Balance Dr=Cr and AR subledger↔control reconciled after every post.

## R1 happy-path tie (hand-calc)
2 lines VAT7: (2 × 1,000) + (1 × 500) = subtotal 2,500 → VAT 175.00 → total 2,675.00.
QT-36 → SO-19 → DO-16 (07-2026-DO-0010) → BN-28 (IV) → **TI-31 (07-2026-TI-0021, posted)** →
**RC-28 (07-2026-RC-0015, posted, applied 2,675, cashReceived 2,675)**. Every doc's subtotal/VAT/total = 2,500/175/2,675; TI in VAT register = 2,500/175. paymentStatus UNPAID→PAID after RC.

## co5 artifacts created (traceability)
Posted: TI-0021, TI-0022, TI-0023, TI-0024; RC-0015, RC-0016; **CN-0001 (dup, id=5)**; DO-0010.
Drafts (unposted, no DELETE route): TI ids 32,33,34,35,36,40; BN ids 28,29,30,31,32; SO-18 (orphan Draft).

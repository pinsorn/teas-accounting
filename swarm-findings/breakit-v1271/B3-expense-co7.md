# B3 — Expense Claims on the non-VAT company (co7) — break-it QA, v1.27.1 prod

Target: `https://teas.kazaki-rio.com`, company **co7** (id=7, `VatRegistered=false`, periods open).
Users: `nvadmin02` (userId 24, COMPANY_ADMIN), `nvchief02` (userId 25, CHIEF_ACCOUNTANT).
Every write confirmed against `GET /api/proxy/me` → `companyId:7`. No writes to co2/co3/co5/co6.
Date: 2026-07-31. Sibling agent A4 was driving co7 purchases concurrently — all money assertions
below are per-JE / per-claim, never on a shared TB total.

---

## TOP LINE

> **CRIT-1 — the v1.22.10 non-VAT 1170 guard is bypassable. A posted, immutable co7
> journal entry now carries a `Dr 1170 Input VAT 535.00` line (JE 293 / `07-2026-EX-0002`).**
> The guard only forces `IsRecoverableVat=false` (which suppresses the *derived* 1170 line);
> it does not stop the caller from naming account 1170 as the claim line's *expense* account.
> Two independent routes reach it. Evidence below.

> **HIGH-4 — HTTP 500 on oversize attachment upload, reproduces on co7** (26 MB and 30 MB both 500).

No underpayment was found on any path, and no Dr≠Cr was produced.

---

## Scoreboard

| Sub-area | Result |
|---|---|
| R1 happy path — VAT-carrying receipt, create→submit→approve→pay, attachment | **PASS** |
| No 1170 line on the normal path | **PASS** |
| Full-gross reimbursement (VAT folded into cost) | **PASS** |
| `isRecoverableVat:true` forced to false on co7 (FE-hidden field / co5-shaped body) | **PASS** |
| Guard bypass via explicit `expenseAccountId` = 1170 | **FAIL — CRIT-1** |
| Guard bypass via expense-category default account = 1170 | **FAIL — HIGH-2** |
| Any account type accepted as an "expense" account (revenue / equity / AP / bank) | **FAIL — HIGH-3** |
| Underpayment (any path reimbursing < gross) | **PASS** — none found |
| Dr=Cr on every posted claim JE | **PASS** |
| Double-pay race (6 concurrent) | **PASS** |
| Reject → edit → resubmit → approve → pay | **PASS** |
| Immutability of a Paid claim (edit/approve/pay/cancel/submit/reject) | **PASS** |
| Pay an unapproved claim (Draft / Submitted) | **PASS** |
| Approve an un-submitted claim | **PASS** |
| Zero / negative amount | **PASS** |
| 3+ decimal amount reaching the immutable GL | **FAIL — MED-5** |
| Huge amount / no sanity cap | **FAIL — LOW-7** |
| Escape route from `Approved` (approve-in-error) | **FAIL — MED-6** (spec-conformant; spec gap) |
| Attachment: bad MIME / empty file / bogus parent | **PASS** |
| Attachment: oversize | **FAIL — HIGH-4 (HTTP 500)** |
| Attachment: cross-tenant download | **PASS** (RLS 404) |
| Attachment: `/download` parent-permission guard | **FAIL — LOW-9** (code-confirmed, not exploitable cross-tenant) |
| Attachment onto a Paid (immutable) claim | **FAIL — LOW-8** |
| Pay into a closed period | **PASS** |
| SoD self-approval | **PASS — by design** (Ham ruling, `specs/expense-claims.md:547`) |

---

## ROUND 1 — happy path, tied to hand-calc — PASS

Claim 10, `07-2026-EX-0001`. Receipt: net 1,000.00 + 7% VAT 70.00 = **gross 1,070.00**.
Request deliberately carried the VAT-company shape (`vatRate:0.07`, `isRecoverableVat:true`):

```
POST /api/proxy/expense-claims
{"employeeId":10,"claimDate":"2026-07-31","title":"B3 R1 happy path VAT receipt",
 "lines":[{"expenseCategoryId":66,"expenseAccountId":null,"description":"Taxi receipt with 7% VAT",
           "expenseDate":"2026-07-30","amount":1000.00,"vatRate":0.07,"isRecoverableVat":true}]}
```

Stored line (`GET /expense-claims/10`): `isRecoverableVat: false` ← **guard held on this route**,
`amount 1000.0000, vatAmount 70.0000, lineTotal 1070.0000`, header `totalAmount 1070.0000`.

Attachment `receipt.png` (image/png) uploaded to `parent_type=EXPENSE_CLAIM, parent_id=10` → 201, attachmentId 14.

submit 204 → approve 200 (by chief, userId 25) → pay CASH 200.

**JE 291 / `07-2026-JV-0010`, Posted:**

| Line | Account | Dr | Cr |
|---|---|---|---|
| 1 | 124 / **5200** ค่าใช้จ่ายค่าบริการ | **1,070.00** | 0.00 |
| 2 | 108 / 1110 เงินสด | 0.00 | **1,070.00** |
| | totals | 1,070.00 | 1,070.00 |

Hand-calc tie-out — all four invariants hold:
- **No 1170 line.** ✔
- Cost account carries the **full gross** 1,070.00 (VAT folded into cost, ภาษีซื้อต้องห้าม). ✔
- Claimant reimbursed **in full**: credit to cash = 1,070.00 = gross. ✔
- **Dr = Cr**; payable clears exactly (claim total 1,070.00 == JE credit 1,070.00, nothing stranded). ✔

---

## FINDINGS

### CRIT-1 — non-VAT 1170 guard bypassable via an explicit `expenseAccountId`; a 1170 line is now posted on co7

**Severity: CRITICAL.** Money/compliance. Posted and immutable — expense claims have no void/reversal.

**Repro (reproduced live, artefacts persist on prod):**
```
POST /api/proxy/expense-claims          # as nvadmin02, companyId 7
{"employeeId":10,"claimDate":"2026-07-31","title":"B3 A1 force 1170 via account override",
 "lines":[{"expenseCategoryId":66,"expenseAccountId":111,"description":"forced input VAT line",
           "expenseDate":"2026-07-30","amount":500.00,"vatRate":0.07,"isRecoverableVat":true}]}
→ 201 {"expense_claim_id":11}
POST /api/proxy/expense-claims/11/submit   → 204
POST /api/proxy/expense-claims/11/approve  → 200   (chief)
POST /api/proxy/expense-claims/11/pay {"paymentMethod":"CASH"}
→ 200 {"docNo":"07-2026-EX-0002","totalAmount":535.0000,"journalEntryId":293}
```
`accountId 111` = co7 account code **1170** (Input VAT), postable, active.

**Expected:** on a non-VAT company no expense-claim JE line may hit 1170 — rejected at draft
(`expense_claim.expense_account_invalid`) or at pay.

**Actual — `GET /api/proxy/journals/293`:**
```
07-2026-JV-0012  "EX 07-2026-EX-0002"  Posted
  L1 acct 111 (1170)  Dr 535.00  Cr 0.00   "forced input VAT line"
  L2 acct 108 (1110)  Dr   0.00  Cr 535.00 "Cash/Bank 07-2026-EX-0002"
```
Trial balance now reports **`1170  Dr 535.00`** on a company that files no ภ.พ.30 — a fictitious
recoverable-input-VAT asset on the balance sheet and 535.00 of expense that never hit the P&L.

**Root cause (read from source, `backend/src/Accounting.Infrastructure/Expense/ExpenseClaimService.cs`):**
the v1.22.10 guard lives in `BuildLinesAsync` and only does
`var isRecoverableVat = companyVatRegistered && input.IsRecoverableVat;` — it governs
`PostExpenseClaimAsync`'s *derived* `recoverableVatTotal` 1170 line only. The line's own account
comes from `EnsureExpenseAccountAsync`, which validates **exists / active / non-header** and
nothing else — no account-type and no 1170 exclusion. The comment at
`ExpenseClaimService.cs:76` ("...can never route an amount to GlPostingService
.PostExpenseClaimAsync's 1170 line") is accurate about the derived line and misleading about the
invariant: the *account* route is wide open. The `PayAsync` re-guard has the same blind spot —
it re-zeroes `IsRecoverableVat` but never re-checks `ExpenseAccountId`.

### HIGH-2 — same bypass with no override at all: an expense category can default to 1170, and categories cannot be deactivated

**Severity: HIGH.** Turns CRIT-1 from an attacker-shaped payload into an everyday click-path:
once the category exists, an ordinary claimant picking it from the FE dropdown posts to 1170.

**Repro:**
```
POST /api/proxy/expense-categories
{"categoryCode":"B3VATTRAP","nameTh":"B3 trap","nameEn":"B3 trap","description":"break-it probe",
 "defaultExpenseAccountId":111,"defaultIsRecoverableVat":true,"isCapex":false,"isCogs":false}
→ 201   (categoryId 78, defaultExpenseAccountId 111)

POST /api/proxy/expense-claims
{"employeeId":10,"claimDate":"2026-07-31","title":"B3 A6 category->1170",
 "lines":[{"expenseCategoryId":78,"description":"via trap category, NO override","amount":250,
           "vatRate":0,"isRecoverableVat":false}]}
→ 201, and GET /expense-claims/18 shows  resolved expenseAccountId = 111 (1170)
```
**Expected:** `POST /expense-categories` rejects a `defaultExpenseAccountId` that is not an
expense-type postable account.
**Actual:** accepted with no validation of any kind on the account. `BuildLinesAsync`'s comment
explicitly *trusts* the category default ("already validated when the category was set up") —
it never was. Both ends of that trust chain are unvalidated.

**Compounding:** `/expense-categories` exposes only `POST` and `GET`
(`backend/src/Accounting.Api/Endpoints/MasterEndpoints.cs:160-172`) — **no update, no delete, no
deactivate.** A mis-mapped category is permanent for the life of the company. `B3VATTRAP`
(categoryId 78 → 1170) is now permanently in co7's picker; I could not remove it.

### HIGH-3 — no account-type restriction whatsoever on an expense-claim line

**Severity: HIGH.** CRIT-1 generalised: any postable account is accepted as an "expense" account.

**Repro** — all four returned `201` at draft on co7 (drafts subsequently cancelled; not posted,
because CRIT-1 already demonstrates the GL outcome):

| `expenseAccountId` | Code | Type | Result | Consequence if paid |
|---|---|---|---|---|
| 111 | 1170 | Input VAT asset | 201 → **posted, CRIT-1** | fake recoverable VAT |
| 121 | 4100 | Revenue | 201 (claim 19) | Dr Revenue — P&L inversion, revenue understated |
| 119 | 3300 | Equity | 201 (claim 20) | expense booked to equity, never reaches P&L |
| 113 | 2110 | Accounts Payable | 201 (claim 21) | silently clears real AP — the 2026-07-25 stranded-payable class |
| 109 | 1120 | Bank | 201 (claim 22) | Dr Bank / Cr Cash — claim reads **Paid**, employee never reimbursed |

**Expected:** `EnsureExpenseAccountAsync` restricts to expense-type (5xxx/COGS/capex) accounts,
as its sibling `BankReconciliationService.CreateJournalAsync` contra-check intends.
**Actual:** exists + active + non-header only. The row 109 case is the one to note — it produces a
claim whose status is `Paid` and whose JE moves money between two company accounts, so the
employee is never actually reimbursed while the books say the claim is settled.

### HIGH-4 — oversize attachment upload returns HTTP 500 (reproduces on co7)

**Severity: HIGH** (raw 500 on a public route; matches the known class, confirmed present here).

**Repro:**
```
POST /api/proxy/attachments  -F file=@30MB.png;type=image/png
   -F parent_type=EXPENSE_CLAIM -F parent_id=17 -F category=RECEIPT
→ 500 {"type":"urn:teas:error:internal_error","title":"internal_error","status":500}

same with a 26 MB file → 500  (one earlier 26 MB attempt returned a Cloudflare 520)
same with a  6 MB file → 201  (accepted; configured limit is 25 MB)
```
**Expected:** `413 Payload Too Large` — the endpoint explicitly codes for it
(`AttachmentEndpoints.cs:45-48`, `Results.StatusCode(StatusCodes.Status413PayloadTooLarge)`),
and `AttachmentService.cs:112-115` has a matching `attachment.too_large` domain error.
**Actual:** neither ever runs. The request-body/multipart length limit throws before the handler
body executes, so every over-limit upload is an unhandled 500 (or a CF 520 at the edge).
The clean 413/422 path is unreachable in production.

### MED-5 — sub-satang (3+ decimal) amounts reach the immutable GL through the expense-claim path

**Severity: MEDIUM.** Same hole the sibling agent proved on the journal-draft path; the expense
path shares it on co7. No imbalance resulted (GL columns hold 4 dp), but un-representable
currency is now posted and immutable.

**Repro:**
```
POST /api/proxy/expense-claims  (two lines, 100.005 each, vatRate 0)
→ 201 claim 12;  header subtotal 200.01, total 200.01;  each line amount 100.005
submit/approve/pay CASH → 200, docNo 07-2026-EX-0003, journalEntryId 295
```
**Expected:** amounts rounded/rejected at 2 dp (satang) before reaching the GL.
**Actual — `GET /journals/295`:**
```
  L1 acct 124 (5200) Dr 100.005
  L2 acct 124 (5200) Dr 100.005
  L3 acct 108 (1110) Cr 200.01
  totals Dr 200.01 / Cr 200.01   (balanced)
```
Root cause: `BuildLinesAsync` does `Math.Round(input.Amount, 4, MidpointRounding.AwayFromZero)`
— **4** decimals, not 2 — while `VatAmount` is rounded to 2. `Dr=Cr` survives only because the
GL columns are 4 dp; the trial balance now returns 3-decimal figures (observed:
`1120 Cr 159654.951`). Any downstream consumer that rounds per-line to 2 dp before summing
(a printed TB, a CSV export, a filing) will show a 0.01 imbalance.

### MED-6 — `Approved` is a dead end: an approve-in-error can only be resolved by paying it

**Severity: MEDIUM.** Behaviour is **spec-conformant** (`specs/expense-claims.md:373` —
`Cancel: Draft/Rejected -> Cancelled`; line 460 explicitly tests `cancel-on-Approved → cannot_cancel`),
so this is a **design gap in the spec**, not a deviation from it. Flagging for a ruling.

**Repro** — claim 17 in status `Approved`, every transition attempted:
```
POST /expense-claims/17/cancel  → 422 expense_claim.cannot_cancel  "…in status Approved."
POST /expense-claims/17/reject  → 422 expense_claim.not_submitted  "…in status Approved."
POST /expense-claims/17/submit  → 422 expense_claim.not_draft
POST /expense-claims/17/approve → 422 expense_claim.not_submitted
PUT  /expense-claims/17         → 422 expense_claim.not_editable
```
The only legal exit is `pay`. `Submitted` has an escape (reject → Rejected → edit or cancel);
`Approved` has none, and a paid claim has no void/reversal. An approver who approves the wrong
claim must either pay it or leave it hanging forever.
(Confirmed left over on prod: co7 claim 17, 50.00, stuck `Approved`.)

### LOW-7 — no upper bound on claim amount; float round-trip corrupts large values

```
POST /expense-claims  line amount 99999999999999.99
→ 201 (claim 13);  GET shows totalAmount 99999999999999.98
```
Accepted at draft with no sanity cap, and the value silently changed (`.99` → `.98`) — the
request body is bound through a binary floating-point path before reaching the `decimal`.
Not paid (would have wrecked the sibling agent's TB measurements); claim cancelled.
Validator has `GreaterThan(0)` only — no `LessThan`.

### LOW-8 — attachments can be added to a Paid (immutable) claim

```
POST /attachments -F parent_type=EXPENSE_CLAIM -F parent_id=10  (claim 10 is Paid)
→ 201 attachmentId 16
```
Every other mutation on a Paid claim is refused (422); the attachment set is not covered by the
immutability rule, so a settled, GL-posted claim's supporting-document set can still be altered
after the fact. Arguably intentional (late receipt filing) — flagging for a ruling.

### LOW-9 — `GET /attachments/{id}/download` skips the parent-permission guard (co7 shares the known gap)

Code-confirmed at `backend/src/Accounting.Api/Endpoints/AttachmentEndpoints.cs:77-82`: `POST /`,
`GET /` and `GET /categories` all run `ParentGuard`; `/{id}/download` does not — it requires only
the generic `sys.attachment.read`. `ParentReadPermission` *does* map
`ExpenseClaim → "expense.claim.read"` (`AttachmentService.cs:47`), so listing a claim's
attachments is gated while downloading one by id is not: a co7 user holding `sys.attachment.read`
without `expense.claim.read` can pull any expense-claim receipt by guessing/enumerating ids.

**Not exploitable across tenants** — verified live: `GET /attachments/{1,2,3,5,8,12,13}/download`
as nvadmin02 → **404** for every id belonging to another company; only the co7-owned id 14
returned 200 (70 bytes). RLS holds. Severity therefore LOW (intra-company perm bypass only).
Could not demonstrate the intra-company arm live — no co7 account exists with
`sys.attachment.read` but without `expense.claim.read`, and creating one would have polluted prod.

---

## PASSES (evidence)

- **Non-VAT VAT-field guard.** `isRecoverableVat:true` + `vatRate:0.07` on co7 → stored
  `isRecoverableVat:false`, VAT folded into `lineTotal`. The co5-shaped request body does **not**
  produce a derived 1170 line. (Claim 10 / JE 291.)
- **No underpayment anywhere.** Every paid claim reconciles exactly, JE credit == claim total == gross:
  ```
  claim 10 total=1070.0   JE 291  Cr 1070.0   Dr 1070.0
  claim 11 total=535.0    JE 293  Cr  535.0   Dr  535.0
  claim 12 total=200.01   JE 295  Cr  200.01  Dr  200.01
  claim 14 total=777.0    JE 296  Cr  777.0   Dr  777.0
  claim 15 total=999.0    JE 297  Cr  999.0   Dr  999.0
  ```
  Nothing stranded, no path reimbursed less than gross.
- **Double-pay race.** 6 concurrent `POST /expense-claims/14/pay`: exactly one 200
  (`07-2026-EX-0004`, JE 296), five clean `422 expense_claim.not_approved "(current: Paid)"`.
  One JE, one doc number, no duplicate posting.
- **Reject → resubmit.** claim 15: submit → reject("B3 test rejection") → status `Rejected`,
  `rejectReason` set, version 2 → edit 333→999 (`rejectReason` cleared) → resubmit → approve →
  pay TRANSFER(bankAccountId 4) → `07-2026-EX-0005`, JE 297 `Dr 5200 999.00 / Cr 1120 999.00`.
  Clean; nothing stranded, nothing duplicated.
- **Paid-claim immutability.** All six mutations on claim 10 → 422:
  `not_editable` (PUT), `not_submitted` (approve), `not_approved` (pay), `cannot_cancel`,
  `not_draft` (submit), `not_submitted` (reject). There is no `DELETE` route on
  `/expense-claims` at all.
- **Pay before approval.** Draft → `422 not_approved "(current: Draft)"`;
  Submitted → `422 not_approved "(current: Submitted)"`. Approve before submit → `422 not_submitted`.
- **Zero / negative amounts.** `amount:0` and `amount:-500` → `400` FluentValidation
  `'Amount' must be greater than '0'`.
- **Closed period.** Closed 2026-07 → `POST /expense-claims/17/pay` →
  `422 period.closed "Period 2026-07 is CLOSED. Reopen the period or correct doc_date."`
  → reopened immediately (window ≈1 s; `GET /periods/2026/7/status` → `{"open":true}` confirmed).
  The guard co5 showed is present on co7.
- **Attachment input validation.** `text/plain` → `422 attachment.bad_mime`; empty file →
  `400 "file is required."`; `parent_id=999999` → `422 attachment.parent_not_found
  "EXPENSE_CLAIM 999999 not found in this tenant."`
- **SoD self-approval is by design.** nvadmin02 created, submitted and approved claim 17
  (`approvedBy:24` == creator) → 200. Matches the explicit ruling at
  `specs/expense-claims.md:547` ("permission-only; creator MAY self-approve"). Not a defect.

---

## Prod state left on co7 (co7 only; no other company touched)

| Artefact | State | Note |
|---|---|---|
| Claims 10, 12, 14, 15 | Paid | benign test claims (`EX-0001/0003/0004/0005`) |
| **Claim 11 / `07-2026-EX-0002` / JE 293** | **Paid, immutable** | **carries the Dr 1170 = 535.00 line — CRIT-1 evidence. No void/reversal exists; it will remain on co7's TB.** |
| Claim 17 | stuck `Approved` | MED-6 evidence — no legal exit but `pay` |
| Claims 13, 16, 18–22 | Cancelled / Rejected | probe drafts, cleaned up |
| **Expense category 78 `B3VATTRAP` → account 1170** | **permanent** | HIGH-2 evidence; no delete/deactivate endpoint exists |
| Attachments 14 | live | claim 10 receipt |
| Attachments 15, 16 | soft-deleted | oversize/immutability probes, cleaned up |
| Period 2026-07 | **open** | closed for ≈1 s during the period test, reopened and verified |

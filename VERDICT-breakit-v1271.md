# VERDICT — break-it swarm on v1.27.1 (2026-07-31)

Ham /goal: *"ตอนนี้ Live เป็น 1.27.1 ส่งฝูง Sonnet ไปเทสซะ ทั้งบริษัท Vat/Non Vat ไปทั้งฝูงทำยังไงก็ได้ให้พัง"*
— then: *"ลองระบบซื้อ ขาย Approval ออก PDF ระบบเบิกค่าใช้จ่าย ระบบเงินเดือน ทุกอย่างเท่าที่เป็นไปได้"*

14 agents driven against **live prod** (teas.kazaki-rio.com, v1.27.1), company **co5** (VAT dummy).
Per-agent evidence: `swarm-findings/breakit-v1271/`. State + full finding log: `PROGRESS-breakit-swarm-v1271.md`.
Every finding below marked **[verified]** was re-checked by Fable in the source, not taken on the agent's word.

---

## The one-paragraph answer

> **Note on scope:** the CRITICAL list grew from four to six once the non-VAT company was driven — which is
> the argument for having run it. **C5 is not a non-VAT defect at all**: found on co7, but it sits in code
> shared by every company including the real tenants. **C6 is the opposite** — a whole half of the
> accounting model that only exists on the VAT path. Read those two first.

**The arithmetic is sound; the controls around it are not.** Across every chain driven — sales, purchase,
foreign-vendor reverse charge, expense claims, payroll, manual JV — the money math tied to hand-calc to the
satang, the trial balance held Dr=Cr in every posted scenario, and **there was no cross-tenant leak and no
data corruption from any attack**. What broke was the *guard layer*: four defects let wrong data or wrong
documents through gates that were supposed to stop them, and three of those four end up on a **document
filed with the Revenue Department**. None of this is visible from the UI happy path, which is why 1,073
green tests and five prior swarm rounds missed all of it.

---

## CRITICAL — fix before the next filing deadline

### C1 · Sub-satang amounts reach the immutable ledger — via the agent (MCP) path [verified]
`POST /journals` → `/journals/{id}/post`, and **MCP `create_manual_journal_draft`**, accept amounts with
more than 2 decimals. `/journals/manual` rejects them. They post, and journal entries are immutable.

**co5's live trial-balance total is now `822801.785`** — a number that cannot exist in baht.

The damning part is a side-by-side in a single file: `CreateManualJournalValidator` (JournalDtos.cs:73-77)
carries the 2-decimal rule *and a comment naming this exact failure mode* — "numeric(19,4) so a 3rd/4th
decimal would be STORED and would make ΣDr==ΣCr pass on invisible satang. Reject at the edge."
`CreateJournalValidator` (JournalDtos.cs:94-102) — the draft path, the one MCP uses — has no such rule.
The bug class was known, documented, fixed on one path, and left open on the path v1.27.0 then handed to
autonomous agents. `PostAsync`/`MarkPosted` check only the header total, never per-line precision.

**This is systemic, not one missing rule.** By the end of the round, **three independent paths** were proven
to write >2-decimal money into the ledger:
- the journal draft path and MCP (above) — co5 TB `822801.785`;
- **expense claims** — `BuildLinesAsync` rounds to **4** decimals (ExpenseClaimService.cs:100), posting
  `Dr 100.005` (co7 JE 295);
- **payroll** — a deduction of `100.129` accepted with no validation at all, landing in the GL, the bank
  and the TB (co7: 2170 = 145,165.951, 2180 = 100.129, bank 1120 = −49,554.951) **while the payslip PDF
  prints 100.13 / 14,149.87 — so the payslip no longer foots to its own journal entry.**

There is no money-precision invariant in this system. There is one 2-decimal rule, in one validator, and
every other path predates or bypasses it.

*Fix:* not "port the rule" — put the guard at the **posting seam** every path funnels through, so no future
path can reintroduce it, and have the validators fail fast on top. Then decide what to do about the
already-skewed ledgers on co5 and co7.

### C2 · ภ.พ.36 double-counts reverse-charge VAT — over-remits to the RD [verified]
`WhtFilingService.GeneratePnd36Async` (WhtFilingService.cs:257-266) does `viRows.Concat(pvRows)` with **no
dedup**. A foreign-service vendor invoice *and* the self-withhold payment voucher that settles it both carry
`RequiresPnd36ReverseCharge` with the same subtotal, so one ฿20,000 service is declared as ฿40,000 service /
฿2,800 VAT instead of ฿20,000 / ฿1,400.

On `mode=finalize` this posts an **immutable JV remitting the inflated VAT to the Revenue Department**
(PostReverseChargeJvAsync, line 282-283). co5's pre-existing July data was already double-counted before the
swarm arrived. The agent used preview mode only, so no bad JV was posted on prod.

*Fix:* the flag belongs on the VI **or** the PV, not both; the query picks one side.

### C3 · Payroll posts into a closed period [verified]
`PayrollRunService.cs` — `PostAsync` (line 202) and `PayAsync` (line 229) never call `EnsureOpenAsync` and
do not even inject `IPeriodCloseService`. Every other posting path guards (JournalService.cs:265,
ExpenseClaimService.cs:252, FixedAsset 238/300, BankRec 236). Proven live: a payroll run posted into
**explicitly-closed June 2026**, minting immutable JE 270 (docDate 2026-06-29) plus settlement JE 271, with
no `period.closed` refusal. There is no future-date guard either — Oct/Nov/Dec 2026 posted freely.

This defeats the point of closing a month: a closed period can still be moved after the books are reported.

*Fix:* inject the service and call `EnsureOpenAsync` on both paths.

**Scope of this gap is now settled.** Agent C1 swept every GL-writing endpoint — TI, RC, CN, DN, VI, PV, JV,
expense claim, fixed-asset dispose/write-off/depreciation, bank-rec — and **all of them correctly refuse a
closed period. Payroll is the only one.** Every immutability attack also failed cleanly (405/422).

**Reproduced on a second company** (co7: immutable JEs 298/300 into explicitly-closed June 2026, HTTP 204
where 422 was due) — so this is the product, not one company's state. It is also **unbounded into the
future**: period `209912` with payDate 2099-12-31 was created, approved and posted, leaving a permanent
**JE `12-2099-JV-0001` dated 2099-12-31**.

⚠️ **Implementation warning for whoever fixes this — adding the guard alone will brick payroll.** co7's only
open period already holds a run, and the duplicate-period guard is permanent, so *today the missing guard is
the only reason payroll runs at all on that company*. The fix has to ship together with a way to open the
period you actually need (or reopen/redo a run), or the first customer to hit it cannot run payroll at all.

### C4 · ภ.ง.ด.1 and ภ.ง.ด.1ก print the totals on the wrong row
The summary totals are stamped onto **row 5 — ม.40(2) ผู้รับเงินได้มิได้เป็นผู้อยู่ในประเทศไทย** while the
**"6. รวม" row prints blank**. The values are correct; the placement declares the entire payroll as
non-resident income on a return filed with the RD.

`Pnd1FormFiller.cs:98-105` and `Pnd1aFormFiller.cs:66-67` write to `Text2.18/19/20`. The code's own comment
says those fields are "Row 6 รวม" — but if the form's fields run three per row (row 1 = Text2.1/2.2/2.3),
row 6 would be Text2.16/17/18, so the mapping is off by two. The rendered-PDF measurement is the primary
evidence; the field map itself carries a "Ham visual-validation pending" note, i.e. this area was known to be
unvalidated. **Pin the exact target row against the template before fixing.**

**Confirmed on a second company.** D2 reproduced this byte-for-byte on co7 across three separate payroll
runs and the annual ภ.ง.ด.1ก — totals land on row 5 (ม.40(2) non-resident) *as well as* row 1, and both
"6. รวม" and "8. รวมยอด" print blank. This is the product, not one company's data.

---

### C5 · An expense claim can be booked to ANY account — including the bank — and marked Paid without reimbursing anyone [verified, and broader than the agent reported]
`EnsureExpenseAccountAsync` (ExpenseClaimService.cs:53-66) validates only that the account **exists, is
active, and is not a header**. It never checks the account **type**. So bank (1120), accounts payable
(2110), revenue (4100), equity (3300) and input VAT (1170) are all accepted as "expense accounts".
The bank case produces a claim marked **Paid** whose journal entry never reimburses the employee — and
the books look settled.

Two things make this worse than a crafted-payload attack:

- **It needs no attacker.** `POST /expense-categories` accepts `defaultExpenseAccountId: 1170` with zero
  validation, so an ordinary user picking from the category dropdown posts to it. There is no update,
  delete or deactivate route for expense categories, so a poisoned category is **permanent** (co7 now
  carries category id 78 as evidence).
- **The category path skips the guard entirely** — an extra hole the agent did not report. At line 94-97
  `EnsureExpenseAccountAsync` runs *only* on the override branch; the `category.DefaultExpenseAccountId`
  branch has nothing but a null check. A category whose default account is later deactivated, or turned
  into a header, still posts.

**This is not a non-VAT bug.** It was found on co7 but `ExpenseClaimService` is shared by every company —
co5, and the real tenants co2/co3, are on the same code path.

Related, same file: the v1.22.10 non-VAT 1170 guard only forces `IsRecoverableVat = false`, which kills the
*derived* VAT line but does nothing about an explicitly-passed 1170 account — so `Dr 1170 = 535.00` is now
posted and immutable in co7's JE 293, showing a fictitious recoverable-input-VAT asset on a company that
can never reclaim it. And line 100 rounds to **4 decimals**, so this path leaks sub-satang amounts into the
ledger too (JE 295), the same class as C1.

*Fix:* validate account TYPE (expense/cost only) and run the validation on **both** branches — plus at
category-create time, where the poison enters.

### C6 · A non-VAT company has no accounts receivable at all — its books are half cash-basis [verified]
`BillingNoteService.IssueAsync` (BillingNoteService.cs:294-318) allocates a document number, flips the
status to Issued, records activity and saves. **It never posts a journal entry — the service does not even
inject `IGlPostingService`.** The VAT path does: `TaxInvoiceService.PostAsync:545` calls
`_gl.PostTaxInvoiceAsync`.

A non-VAT company cannot issue a tax invoice (correctly blocked, `ti.non_vat_blocked`), so its only sales
document is the invoice/billing note — which books nothing. **Revenue and AR are therefore never accrued;
they appear only when the receipt posts.** Proven live: `07-2026-IV-0002` is Issued and unpaid for
฿5,000.00, while `/reports/ar-aging` returns `rows: []` with a control balance of 0.00 and the customer
statement is empty.

The purchase side *does* accrue — AP reconciles at 1,070.00 — so a non-VAT company runs **accrual on
purchases and cash on sales**. That asymmetry is what makes this look like a gap rather than a deliberate
cash-basis design. For a Thai company filing ภ.ง.ด.50 on an accrual basis, the financial statements are
materially wrong: revenue understated, AR permanently zero.

It is also **silent** — AR aging showing no rows reads as "nothing outstanding", not "this is not
implemented", so nobody would notice from the UI.

*Fix:* decide the intended basis first (this is a product decision, not a bug fix), then post revenue+AR at
invoice-issue for non-VAT companies, or state the cash basis explicitly in the reports.

## HIGH

| # | Finding | Evidence |
|---|---------|----------|
| H1 | **Duplicate running numbers on tax documents** [verified] — the unique index is `(CompanyId, BranchId, DocNo)` (TaxInvoiceConfiguration.cs:99, TaxAdjustmentNoteConfiguration.cs:62, ReceiptConfiguration.cs:66) but the allocator sequences per (company,type) ignoring branch, so a different/NULL branch reuses a visible number and the DB accepts it. Four duplicate TI numbers found; a duplicate ใบลดหนี้ `07-2026-CN-0001` **live-reproduced**, and D1 later found two POSTED credit notes *printing* that same number against different original invoices. The RD requires unique tax-document numbers. | A1, D1 |
| H2 | **Double-post race returns a raw HTTP 500** [verified] on `/journals/{id}/post`, `/tax-invoices/{id}/post`, `/receipts/{id}/post`. Only `PaymentVoucherService.PostAsync` wraps `DbUpdateConcurrencyException` → clean 409; the other three let it escape unmapped. Data integrity held every time (posts once, contiguous numbers, no orphaned number) — this is error-surfacing, not corruption. Same unmapped-exception class: attachment upload >5MB → 500 (the advertised 25MB limit is unreachable, no 413), and paying with a nonexistent `bankAccountId` → 500. | A5, B1, B2 |
| H3 | **Conversion routes check the wrong scope — systemic** [verified] — every "create-from / convert" route authorizes on the **source** document's manage scope and never on the **target** document's create scope: `billing-notes/{id}/create-tax-invoice` and `delivery-orders/{id}/create-ti` both mint a **ใบกำกับภาษี** off billing-note / delivery-order permissions; `sales-orders/{id}/create-invoice`, `delivery-orders/{id}/create-invoice`, `quotations/{id}/convert-to-so` follow the same pattern. Net effect: a user holding only delivery-order manage can mint a tax invoice, though direct `POST /tax-invoices` returns 403 for them. Drafts only — no ledger movement, no number consumed — which is why this is HIGH, not CRITICAL. Two agents each found one instance; the pattern only appeared when both were read together. | A1, C2 |
| H4 | **Attachment download skips its permission guard** [verified] — `GET /attachments/{id}/download` (AttachmentEndpoints.cs:77-80) omits the `ParentGuard` that upload (line 51) and list (line 69) enforce. A user with only the broadly-granted `sys.attachment.read` gets **403 on the list but 200 and the full PDF on download**, walking sequential ids. Company scope is *not* bypassed (confirmed: cross-company ids all 404), so this is intra-tenant. | B2, C2 |
| H5 | **A voided payment voucher prints as "ต้นฉบับ"** [verified] with the approver's name in the signature box and no ยกเลิก mark; a draft PV prints "ต้นฉบับ" too. `PaymentVoucherService.Read.cs:238` hard-codes the watermark instead of calling `PaperDocConfig.Watermark`, whose line 52 already maps a cancelled status to "ยกเลิก". `PurchaseOrderService.cs:325` repeats the mistake. Every other doctype does it correctly. | D1 |
| H17 | **The same sales order can be billed twice** — `so.invoice_exists` does not traverse SO → DO → Invoice. SO 20 was invoiced through DO 17 (IV-0001, ฿5,000, settled); `POST /sales-orders/20/create-invoice` then returned **200** and minted IV-0002 for the same ฿5,000. Only the *third* call 422s. DO 17 even stores `billingNoteId: 33`, so the link exists — the guard just doesn't follow it. A customer can be billed twice for one order. | A4 |
| H15 | **Every non-VAT payment voucher prints a Grand Total its own lines do not add up to** [verified] — a line of 1,000.00 under a total of `฿ 1,070.00` with no row explaining the 70. `PaperFootPlan.Build` (PaperFootPlan.cs:31-41) adds the Subtotal / BeforeVat / VAT rows **only when `ShowVat` is true**, but still computes `grand = Subtotal + VAT`. The earlier "cont.120" change hid the VAT *display* on non-VAT companies without reconciling the arithmetic. The ledger is right (VAT folds into cost, vendor paid 1,070 in full) — it is the **document handed to the vendor** that does not foot. 3/3 VAT-vendor PVs on co7 (22/55/56). | D2 |
| H16 | **A non-VAT company can generate, print AND FINALIZE a ภ.พ.30 VAT return.** D2 found the PDF renders fully identity-filled (name, 13-digit tax ID, address, พ.ศ. 2569; `tax-filings/pnd30/pdf?period=202607` → 200); A4 then took it further — `mode=finalize` returns **200 "Finalized"** and `filingId 1` is now **on record for co7**, with a 290 KB filled PDF bearing the real tax ID. There is no `non_vat_blocked` guard on this route (every tax-invoice path correctly 422s); the UI gate is front-end only. So it is not merely a stray render — a company with no VAT registration can commit a VAT filing it must never file. | D2, A4 |
| H13 | **Official RD filings generate from an unapproved, unposted DRAFT payroll run** — ภ.ง.ด.1, the สปส.1-10 upload file and PDF, and payslips all rendered from a run with `journalId: null`: a signable return declaring ฿1,103.02 of PIT **with zero ledger backing**. The agent then deleted the run behind it, leaving the artifact with nothing to tie back to. A filing artifact should only ever come from posted data. | B5 |
| H10 | **A fiscal year can become impossible to close — a three-way deadlock** [live-proved, zero writes] — closing a period requires a depreciation run; the depreciation run requires the period OPEN; reopening requires it CLOSED. All three refuse each other. **co5's FY2026 can now never be year-closed**; on a real tenant with fixed assets this is recoverable only by editing the database. Read together with the standing O14 limitation (a closed month cannot be reopened inside a closed year — the reason co6 is frozen until 2027), period management has two traps that need a human to escape. Treat as CRITICAL for any real tenant approaching year-end with fixed assets. | C1 |
| H11 | **Reopening a period is unauditable — and that enables silent back-dating** — the activity row is written against `AccountingPeriod` but **no API route reads it**, and reopening NULLs `ClosedAt`/`ClosedBy`/`CloseNotes`. C1 closed June, reopened it, back-dated two journal entries into it, and re-closed — **all in 27 seconds, with no readable surface anywhere showing June was ever reopened.** For an accounting system this defeats the point of the audit trail. | C1 |
| H12 | **Trial Balance / Balance Sheet default their as-of to UTC while AR/AP aging use Bangkok** — at 05:30 ICT the same defaults produced AP control **46,803.50 on ap-aging (`balanced:true`) versus 36,103.50 on the Trial Balance**, a ฿10,700 gap, with the whole TB omitting ฿162,124.765 of the current Bangkok day. Every day between 00:00 and 07:00 the two reports disagree. (Filed LOW earlier in the round on a phantom-gap sighting; C1's live measurement upgrades it — two reports that must agree, don't.) | A2, C1 |
| H7 | **AR and AP aging ignore `asOf` entirely** [verified] — `ArAgingAsync` (SubledgerReportService.cs, ~line 173) filters only `Status == Posted && PaymentStatus != "PAID"` with **no `DocDate <= asOf`**, and computes `TotalAmount - AmountPaid` from the *current* paid figure. `asOf` moves the aging buckets and nothing else. Proven: `ar-aging/export?asOf=` for 1900-01-01, 2020-01-01 and 2026-06-30 all return **byte-identical CSV** (md5 `1d0f75e9…`, ฿19,979.31) although control account 1130 provably held ฿0.00 with zero rows through 2026-06-30. Hits the backend CSV and the FE AP CSV. **This is the report an auditor pulls for a prior year-end.** The correct pattern sits a few lines above in the same file — the reconciliation query does filter `m.DocDate <= asOf`. | D3 |
| H8 | **Every สปส.1-10 SSO upload file ships employer account `0000000000`** — all 5 payroll runs, HTTP 200, no warning; `ssoEmployerAccountNo` is null on co5 and optional in the validator with no export-side guard. The sibling ภ.พ.30 exporter *does* refuse on a missing mandatory field (`pp30_batch.missing_address`, verified live) — SSO does not. The user discovers this at the government portal. | D3 |
| H9-note | **The `?????` mechanism is now proven, and one standing assumption is REFUTED** — B5 showed a CJK/Bengali character in a name becomes a literal `?` in the สปส.1-10 file and is **silently dropped** from the ภ.ง.ด.1 ใบแนบ. But co7's legacy `???` employee names are **client-side, not a server bug** — Thai round-trips byte-perfect through the API. The standing note blaming an old PowerShell API write should be corrected. | B5 |
| H9 | **SSO files ship `?????????????` as insured-person names** (runs 13 & 15: 37 and 19 `?` bytes) — the root data is the known one-byte-per-char corruption class, now on co5 and already inside POSTED runs. Two export-layer defects compound it: `Encoding.GetEncoding(874).GetBytes` uses the default replacement fallback, so **any** non-cp874 character (including the Bengali MA at U+09AE this project greps for) silently becomes `?`; and nothing validates a payee name before it goes into a government filing. | D3 |
| H6 | **Payslip YTD contradicts the 50ทวิ and ภ.ง.ด.1ก** for the same employee and year — 1,040,000.00 / 88,900.00 versus 560,000.00 / 52,450.00, where 560,000 is ground truth. YTD is frozen at run creation (`PayrollRunService.cs:134`) and never recomputed, so it survives deleted and back-dated runs; June shows a larger YTD than July. | D1 |

---

## MEDIUM (11) and LOW (~10) — the short list

- **สปส.1-10 ส่วนที่ 2 prints entirely blank** on every run — no employee national ID anywhere across the
  4 pages — while ส่วนที่ 1 certifies 3–4 insured persons. **ภ.ง.ด.2 has no PDF route at all**, despite a
  Posted `Pnd2` certificate existing on co7. `pnd50`/`pnd51` PDFs return **HTTP 500** for `year ≤ 0` or
  `≥ 9999` (siblings return a clean 422), and with no validation in between, year 999 or 3000 renders happily.
- **ภ.พ.36 has no printable form at all** (`/tax-filings/pnd36/pdf` → 404) though every sibling filing has one — the mandatory reverse-charge return cannot be filed on paper.
- **`/reports/number-gaps` reports `hasGaps:false`** for the very period that contains H1's duplicates — it detects missing numbers, never reused ones, so a compliance control reports clean over a real breach.
- **sales-summary excludes CN/DN** and therefore disagrees with ภ.พ.30 and the trial balance by exactly the credit-note total. ภ.พ.30 and TB agree with each other; sales-summary is the outlier.
- **MCP `.post` guard is a denylist** [verified] — `EnforceMcpNoPostGuard` (ApiKeyService.cs:153-163) tests `s.Trim().EndsWith(".post")`, and .NET `Trim()` does not strip U+200B, so a zero-width space mints an mcp-kind key carrying a `.post`-suffixed scope. No live exploit (permission matching is exact-ordinal and no MCP tool consumes `.post`), but it breaches the invariant the whole draft/post model rests on — and the correct allowlist (`McpScopes.Normalize`) **already exists and is already used on the OAuth path**, just not at mint. One-line fix.
- **`/mcp` authentication is kind-agnostic** — an `integration` key carrying `gl.journal.post` authenticates there; the draft/post split holds only because no MCP tool consumes `.post`. Behavioural, not structural.
- **Line numbers ≥10 wrap vertically** on every document template ("10" renders as "1" over "0") — hits any invoice with 10 or more lines.
- Draft-path JV throws a raw 500 (Postgres 22001) on an over-length reference or description, where the manual path caps both; no 200-line cap on the draft path; financial statements print CE years on a doc labelled for ภ.ง.ด.50; no PDF exists for JV or expense claim; trial balance defaults its as-of to `UtcNow` while AP-aging uses Bangkok-today (they disagree by a day between 00:00–07:00); VI can over-bill a PO to ~200% with only an advisory chip; expense claims have no amount cap (a ฿1.07-trillion claim approved with no escalation); attachment MIME is spoofable; stale comments claim an SoD DB constraint that no longer exists.

---

## The pattern behind five of these bugs

Worth naming, because it points at a cheap systemic check rather than five separate fixes. In five
independent findings, **the correct implementation already existed a few lines away and simply was not
called**:

| Finding | The correct thing that already existed | Where the bug is |
|---|---|---|
| C1 sub-satang | `CreateManualJournalValidator`'s 2-decimal rule *and its warning comment* | `CreateJournalValidator`, **same file**, 20 lines down |
| H5 voided PV prints "ต้นฉบับ" | `PaperDocConfig.Watermark` already maps a cancelled status → "ยกเลิก" | `PaymentVoucherService.Read.cs:238` hard-codes the string instead of calling it |
| H7 aging ignores `asOf` | the reconciliation query filters `m.DocDate <= asOf` | `ArAgingAsync`, **same file**, a few lines below |
| MCP `.post` denylist | `McpScopes.Normalize` — an allowlist, already used on the OAuth path | not called at API-key mint |
| H8 SSO ships `0000000000` | the ภ.พ.30 exporter refuses on a missing mandatory field (verified live) | the SSO exporter has no equivalent guard |

Every one is a *second* code path that never adopted the fix the *first* path already carries. A grep for
"who else does this?" at review time would have caught all five. That is a more valuable outcome than any
individual fix on this list.

## What held up

Worth stating plainly, because it is most of the system:

- **No cross-tenant leak.** Every non-co5 id returned 403/404 across a broad sweep of document, report,
  attachment, api-key and admin routes. Isolation is two-layer (EF query filter + database RLS). An AUDITOR
  reached zero writes by any route. Company switching is super-admin gated.
- **The ledger never went wrong.** Dr=Cr held on every posted document in every attack. Purchase chain
  money-correct end to end (input VAT→1170, WHT→2152, vendor net = gross − WHT, AP cleared to zero).
  Payroll tied to the satang (PIT 7,008.33, SSO cap ฿875, net 115,491.67) with ภ.ง.ด.1 PIT equal to the 2153
  movement. Expense-claim JE tied exactly.
- **Concurrency is solid where it counts.** JV numbering survived a 20-wide concurrent burst contiguous and
  unique; mixed-type bursts produced no 23505 and no deadlock; payroll's own double-post race is handled
  cleanly (it is the three *other* services that leak the 500).
- **The v1.27.0 agent model works.** No MCP tool can post; fake tool names return a clean protocol error;
  cross-company drafts are refused; `docDate` is server-pinned, which kills the closed-period and future-date
  attack classes outright; every gate-bypass payload was rejected with zero 500s.
- **Non-VAT templates are genuinely clean.** D2 traced every `VAT` string in co7's documents to
  user-entered data: no VAT wording, no VAT column, no VAT total row, no "ใบกำกับภาษี" title and no ม.86
  reference anywhere in a co7 template. The non-VAT rendering work held up — the PV total bug (H15) is an
  arithmetic omission, not a VAT leak.
- **The v1.22.11 foreign-vendor fix still holds** — the VI-linked self-withhold PV posts, JE balanced,
  WHT 3,529.41 matching the reference figure. **O8 payroll proration is fixed** (a mid-month hire and leaver
  both prorate correctly) — the standing note in STATUS.md calling it an open gap is stale and should be corrected.
- Approval state machine: 9/9 missing-scope attempts refused 403; out-of-order transitions all clean 422;
  posted documents immutable; the pending-approvals widget accurate and tenant-scoped.

---

## Coverage gaps — what this round could NOT test

- **The entire non-VAT side (co7) was never driven.** Three agents (non-VAT purchase/sales purity, expense
  1170 guard, payroll edge cases) are written and ready but blocked: **nobody knows the passwords for
  nvadmin02 / nvchief02.** The army script that last logged in deleted itself per its own hard rules. This is
  the single largest remaining gap and needs either Ham's credentials or a super-admin reset.
- **Signature and stamp images are untestable on co5** — `stampUrl` is null and no signature attachments
  exist there, so the v1.26.1 signature pipeline could only be checked for *absence* correctness. Needs a
  company that has them uploaded.
- **Nothing is outstanding. All 17 agents reported** — the co7 blocker was a search failure on my side, not
  missing data: the credentials were in `swarm-findings/v1241/legF-jv-prod.md` the whole time (conventions
  now recorded in `troubles-wiki.md` so this cannot recur).
- Signature and stamp IMAGES remain untested on both companies — neither co5 nor co7 has any uploaded, so
  the v1.26.1 pipeline could only be checked for *absence* correctness (drafts must not show one, and they
  don't). Needs a company with a stamp and signature on file.
- One assumption was **refuted**, not confirmed: co7's `???` employee names are a client-side artifact, not
  a server bug — Thai round-trips byte-perfect through the API. The standing note blaming an old
  PowerShell-driven API write should be corrected in STATUS.md.

---

## Recommended fix arc

Grouped so one change closes several findings:

1. **One exception-mapping pass** kills the whole 500 family (H2 + the >5MB upload + the bad bankAccountId +
   the over-length JV field + **C1's seven period-endpoint 500s** from unvalidated year/month
   `ArgumentOutOfRangeException` — one of which, a read-only `GET /periods/{y}/year-status`, is reachable by
   the lowest-privilege user): mirror `PaymentVoucherService`'s `DbUpdateConcurrencyException` → 409 wrapper
   into the TI/RC/JV post paths, map Postgres 22001/23505 to clean 400/409 globally, and range-validate
   year/month at the period endpoints.
2. **One validator + one post-time guard** closes C1 (sub-satang) — and the post-time guard is what makes it
   durable against the next new path.
3. **One helper on every conversion route** closes H3 systemically; the grep that found it becomes the
   regression check.
4. **Two missing guard calls**: `EnsureOpenAsync` in payroll post/pay (C3), `ParentGuard` in attachment
   download (H4).
5. **Compliance/print batch**, each independent: C2 dedup · C4 field map (pin against the template first) ·
   H1 numbering (branch-aware allocator, or company-wide uniqueness, or a branch segment in the number) ·
   H5 watermark · H6 recomputed YTD · number-gaps detecting reuse · sales-summary including CN/DN.
6. **Cheap hardening**: call `McpScopes.Normalize` at key mint; pin `/mcp` to `kind == mcp`.

Two of these are **product decisions, not fixes** — do them first because they change what the other fixes
should do: whether a non-VAT company is meant to run accrual or cash basis (C6), and how the payroll
period-guard ships without bricking payroll (C3's warning).

**Nothing has been fixed. No code was changed. Awaiting Ham's go on the fix round.**

---

## State of the test companies after the round

Both playgrounds now carry deliberate damage — this matters if either is reused as a baseline:

- **co5**: trial balance skewed to `822801.785` by sub-satang test JVs (immutable); duplicate CN/TI numbers;
  FY2026 can no longer be year-closed (the depreciation deadlock); assorted test documents; draft quotation
  id 37 (32 lines) left as pagination evidence.
- **co7**: an immutable `Dr 1170` line on a non-VAT company (JE 293); a permanently poisoned expense
  category (id 78) with no route to delete it; payroll runs posted into closed June 2026 and into
  **period 209912** (JE dated 2099-12-31); a **finalized ภ.พ.30** on a company with no VAT registration
  (`filingId 1`); sub-satang amounts in the GL, bank and TB.

Neither company is a clean baseline any more. A wipe-and-reseed is the cheapest way back if one is needed
for a future round.

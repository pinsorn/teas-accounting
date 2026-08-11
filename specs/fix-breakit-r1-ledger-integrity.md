# fix-breakit-r1-ledger-integrity

Release **R1** of `PLAN-fix-breakit-v1271.md`. Findings **C6, C1, C5, C3** from `VERDICT-breakit-v1271.md`.
Raw evidence: `swarm-findings/breakit-v1271/{C4-jv-validation,A4-nonvat-chains-co7,B3-expense-co7,B5-payroll-co7}.md`.

> **Living document.** The implementer ticks `[ ]` → `[~]` (partial + note) → `[x]` (+ evidence) in §5 and
> appends to the Attempt log. A retry uses THIS file; never rewrite it.

---

## 0. Headline

R1 closes the four defects that write wrong data into an **immutable** ledger. Four things the
investigation changed versus the plan:

1. **C6 is live on real books.** Ham confirmed Repttown (co2/co3) is non-VAT. A real tenant currently has
   **no accounts receivable at all** and recognises revenue only when cash arrives, while its purchases
   accrue. The **backfill is in scope and is the hardest single piece of this release** — it is a
   money-correcting write against real prod data on immutable documents.
2. **C1 is worse than "three paths": it is four.** A4 proved the **Payment Voucher** path also posts
   `100.005` to the GL (`A4-nonvat-chains-co7.md` §K1, co7 JE 306). The per-validator approach was already
   losing; the seam guard is the only durable answer. The single seam every posted JE passes through is
   **`JournalEntry.MarkPosted`** — not `GlPostingService.BuildAndPostAsync`, which
   `PostClosingEntryAsync` and `JournalService.PostAsync` both bypass.
3. **C5's "expense accounts only" rule would brick every company if written naively.** The seeded `CAPEX`
   expense category points at account **1610 (`AccountType.Asset`)**
   (`MasterDataServices.cs:489` + `:446`), and it is seeded by `CompanyService.CreateAsync` on **every**
   company including the real tenants. The rule must key off the category's `IsCapex` flag.
4. **C3's guard does NOT brick payroll, given the O14 reopen** — see §3.5 for the worked analysis. But it
   does need a *second* rule (a future bound on `PayDate`) and a two-message error contract, because
   `PeriodCloseService.IsOpenAsync` answers "closed" for both a closed month and a never-opened future
   month, and only one of those has a reopen as its way out.

**Recommended release split (Fable's call — see §10):** ship WP-1/3/4/5 as the R1 code release; the
backfill **code** (WP-2) ships in the same release but **inert** (preview-only by default), and the
**apply run against Repttown is a separate, Fable-driven prod operation** after R1 is deployed and
verified. Reason: WP-2's correctness detector depends on WP-1 being live, and an irreversible prod-data
write must not ride inside a code-release gate.

---

## 1. Facts established in code

All file:line VERIFIED by reading the file during design (2026-07-31), not inferred.

### 1.1 The posting seam (C1)

| Fact | Evidence |
|---|---|
| `JournalEntry.MarkPosted` checks status, `IsBalanced` (header totals only), and non-empty DocNo. **No per-line precision check.** | `Accounting.Domain/Entities/Ledger/JournalEntry.cs:58-71` |
| `IsBalanced` is `TotalDebit == TotalCredit && TotalDebit > 0m` — header only. | `JournalEntry.cs:52` |
| **Every** posted JE calls `MarkPosted`. Three call sites: `GlPostingService.BuildAndPostAsync` (all document posters + both `PostManualEntryAsync` overloads), `GlPostingService.PostClosingEntryAsync` (year-end — deliberately bypasses `BuildAndPostAsync`), `JournalService.PostAsync` (draft-JV post, the MCP path). | `GlPostingService.cs:564`, `GlPostingService.cs:489`, `JournalService.cs:110` |
| `Lines` is populated before `MarkPosted` at all three sites (assigned at `GlPostingService.cs:478` / `:555`; `.Include(j => j.Lines)` at `JournalService.cs:90`). | as cited |
| `CreateManualJournalValidator` HAS the 2-dp rule + the comment naming this exact failure mode. | `Accounting.Application/Ledger/JournalDtos.cs:74-77` |
| `CreateJournalValidator` (draft path — **the one MCP uses**) has no precision rule, no `Reference` max-length, no line-count cap. | `JournalDtos.cs:85-108` |
| Four services round line amounts to **4** decimals: expense claim, PV, VI (+ a legit 4-dp `*AmountThb` FX field that must NOT change). | `ExpenseClaimService.cs:100`, `PaymentVoucherService.cs:234`, `VendorInvoiceService.cs:240` |
| Payroll deduction amount validator: `GreaterThan(0m)` + a net-pay cap. **No decimal-scale rule.** | `Accounting.Application/Payroll/PayrollDtos.cs:113` |
| `DomainException` → HTTP: default **422**; `.not_found`→404, `.locked_mismatch`→409, `auth.*`→401. | `Accounting.Api/Middleware/DomainExceptionMiddleware.cs:30-39` |

### 1.2 Non-VAT AR (C6)

| Fact | Evidence |
|---|---|
| `BillingNoteService.IssueAsync` allocates the number, flips status, records activity, saves. **No JE, no `IGlPostingService` injected, and no period gate.** | `BillingNoteService.cs:294-318`; constructor `:21-24` |
| The VAT sibling posts: `TaxInvoiceService.PostAsync` → `_gl.PostTaxInvoiceAsync` → `Dr 1130 gross / Cr 4000 net / Cr 2151 vat`. | `GlPostingService.cs:42-67` |
| `PostReceiptAsync` credits **Sales (4000)** for every non-TI application (DO **and** BillingNote share one `else` branch) and for a standalone receipt. Comments at `:94-96` and `:124-132` assert the cash-basis model explicitly — they become wrong and must be rewritten. | `GlPostingService.cs:114-142` |
| A receipt may apply to a `TaxInvoiceId`, a `DeliveryOrderId`, or a `BillingNoteId`. VAT companies are blocked from BN-settling (`rc.vat_co_no_bn_settle`); non-VAT are blocked from TI (`rc.non_vat_no_ti`). | `ReceiptService.cs:142-202` |
| The DO→Invoice link is **`BillingNote.DeliveryOrderId`**, NOT a column on `DeliveryOrder` — the `billingNoteId` field A4 saw on `GET /delivery-orders/{id}` is a computed lookup. Nothing stops a receipt applying to an **already-invoiced DO**; after C6 that double-counts revenue and strands AR. | `Domain/Entities/Sales/BillingNote.cs:36`; the lookup at `SalesOrderDeliveryServices.cs:467-470`; `ReceiptService.cs:165-175` (no such check) |
| `sales.billing_notes` has **no immutability trigger** (`SqlScripts/322_billing_notes_rls.sql` is RLS only; no `fn_enforce_*` exists for it) — so writing `journal_entry_id` onto an Issued BN is unblocked. VERIFIED by grep over `Migrations/SqlScripts`. | `SqlScripts/322_billing_notes_rls.sql` |
| `BillingNote` has **no** `AmountPaid` column; "already paid" is a SUM over `sales.receipt_applications` of posted receipts. | `ReceiptService.cs:515-525` comment |
| AR sub-ledger movements enumerate **TaxInvoices + TI-linked receipt applications + CN/DN only**. BN and BN-linked applications are absent by design (comment at `:78-83`). | `SubledgerReportService.cs:68-109` |
| `ArAgingAsync` queries `TaxInvoices` (`PaymentStatus != "PAID"`) + `TaxAdjustmentNotes`. No BN. | `SubledgerReportService.cs:169-241` |
| `ArReconciliationAsync` and `CustomerStatementAsync` both derive from `ArMovementsAsync` → both are fixed for free by fixing that one method. | `SubledgerReportService.cs:143-152`, `:245-278` |
| `BillingNoteService.CancelAsync` cancels an **Issued** BN with no GL consideration. After C6 that would strand an AR debit. | `BillingNoteService.cs:320-331` |
| Control-account balance is read straight from posted `JournalLines` on 1130. Sub-ledger total is document-derived. `balanced` is `difference == 0m` — **a hard equality**, so any gap between the two shows up as `balanced:false` on the report. | `SubledgerReportService.cs:50-63`, `:143-152` |

### 1.3 Expense accounts + categories (C5)

| Fact | Evidence |
|---|---|
| `EnsureExpenseAccountAsync` validates exists (tenant-scoped) / active / non-header. **No `AccountType` check.** | `ExpenseClaimService.cs:53-66` |
| It runs **only on the override branch**; `category.DefaultExpenseAccountId` is taken on trust, with a comment claiming it "was already validated when the category was set up". It never was. | `ExpenseClaimService.cs:93-97` |
| `CreateExpenseCategoryValidator` validates `CategoryCode` and `NameTh` only. `DefaultExpenseAccountId` is accepted with **zero** validation. | `Accounting.Application/Master/ReferenceDtos.cs:36-43` |
| `/expense-categories` exposes only `POST` and `GET` — no update / delete / deactivate. A poisoned category is permanent. | `Accounting.Api/Endpoints/MasterEndpoints.cs:155-172` |
| `ExpenseCategory` **already has `IsActive`** (default true) and it is already surfaced on `ExpenseCategoryDto`. No schema change is needed to deactivate one. | `Domain/Entities/Sys/ExpenseCategory.cs`; `ReferenceDtos.cs:33-35`; `MasterDataServices.cs:655-661` |
| **The seeded `CAPEX` category defaults to account code `1610`, which is `AccountType.Asset`.** Seeded on every company by `CompanyService.CreateAsync`. | `MasterDataServices.cs:489` (spec row) + `:446` (CoA row) |
| COGS account `5000` is `AccountType.Expense` — no special case needed for `IsCogs`. | `MasterDataServices.cs:437` |
| The correct precedent already exists in-repo: `FixedAssetService.EnsureAccountAsync(accountId, companyId, expectedType, ct)` — exists / active / non-header / **type**. | `FixedAssetService.cs:74-90` |
| **PV and VI share the identical hole** (category default and override both unvalidated for type, plus the same 4-dp rounding). | `PaymentVoucherService.cs:~162`, `VendorInvoiceService.cs:226-240` |

### 1.4 Payroll period guard (C3)

| Fact | Evidence |
|---|---|
| `PayrollRunService` does not inject `IPeriodCloseService`; neither `PostAsync` nor `PayAsync` calls `EnsureOpenAsync`. | `PayrollRunService.cs:23-31`, `:202-227`, `:229-271` |
| Both JEs are dated **`run.PayDate`** (accrual via `PostPayrollRunAsync` → `BuildAndPostAsync(..., run.PayDate, ...)`; settlement via `PostManualEntryAsync(..., run.PayDate, ...)`). | `GlPostingService.cs:441`, `PayrollRunService.cs:262-265` |
| Every other poster guards: `JournalService.cs:265`, `ExpenseClaimService.cs:252`, `ReceiptService.cs:398`, `PaymentVoucherService.cs:~181`, FixedAsset 238/300, BankRec 236. | as cited |
| **`IsOpenAsync` already bounds the future**: a month with no `AccountingPeriod` row is OPEN only when it is the current Asia/Bangkok month; every other missing month (past or future) is CLOSED. So `EnsureOpenAsync` alone also kills the `209912` case — but with a `period.closed` message that names a reopen which cannot work for a future month. | `PeriodCloseService.cs:25-49` |
| The O14 monthly reopen exists and is reachable: `POST /periods/{year}/{month}/reopen`, permission `Permissions.Gl.PeriodClose`. It refuses inside a closed fiscal year (`period.year_closed`). | `Accounting.Api/Endpoints/PeriodEndpoints.cs:20-28`; `PeriodCloseService.cs:123-163` |
| The manual-JV path's date contract is `docDate <= TodayInBangkok()` → `je.future_date`. Payroll deliberately does NOT copy it — Ham decided the bound is the run's own period end, so pre-payday posting keeps working (§8 #3). | `JournalService.cs:158-159` |

### 1.5 Footguns folded in (do NOT rediscover these)

From `troubles-wiki.md`:
- **`## period.closed 422 … company looks permanently bricked` (line 47)** — the entry's "no fix exists"
  advice is **SUPERSEDED**: `POST /periods/{y}/{m}/reopen` shipped as O14. The C3 error message must point
  at that route.
- **`## Regenerating an already-applied EF migration … teas_test stuck` (line 160)** — WP-1 adds one EF
  migration. If it needs a change while uncommitted, **hand-edit the migration file in place** (same
  timestamp). Never `ef remove` + `add` after `dotnet test` has run.
- **`## Startup SqlScript writing/reading G1/G3 RLS'd tables … 42501 or silently no-ops` (line 185)** — R1
  adds **no SqlScript**. WP-1's schema change is an EF migration (`ALTER TABLE … ADD COLUMN`, runs as the
  table owner) so the per-company `app.company_id` DO-block pattern (template: script 621, **not** 482)
  does not apply. If any implementer finds themselves writing a `SqlScripts/NNN_*.sql` — **stop and
  re-spec.**
- **`## Super-admin cross-company write 500s under RLS` (line 769)** — directly relevant to WP-2. The
  backfill is therefore specified as **same-company only**: the caller's active company must equal the
  target company, so `app.company_id` is already pinned by `TenantMiddleware` and no `set_config` dance is
  needed. Cross-company invocation is refused, not supported.
- **`## dotnet build fails silently with 0 warnings and 0 errors` (line 795)** — build with
  `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false`.
- **`## dotnet build fails MSB3027/MSB3021 … locked by testhost` (line 374)** — no build while a test host
  is alive.
- **`## เทสเทียบ DateTime.UtcNow กับ validator ที่ pin Bangkok` (line 776)** — in tests, dates compared
  against server rules use `new SystemClock().TodayInBangkok()`. **Never `DateTime.UtcNow`/`Today`.**
  WP-5 adds a date validator to payroll, so any payroll test using `UtcNow` for a date must be swept in
  the same edit or it goes red between 00:00–07:00 ICT.
- **`## Test asserts an exact past/future DocDate` (line 646)** — document DocDates are server-pinned to
  Bangkok-today. Do not try to backdate a document in a test; vary the *query* range instead. (WP-2's
  tests need historical dates — build those JEs through `IGlPostingService.PostManualEntryAsync`, which
  takes an explicit `docDate`, not through a document service.)
- **`## Posted-document immutability trigger doesn't fire on a header-only field edit` (line 391)** — the
  general lesson (never trust a trigger's doc-comment, read its `IS DISTINCT FROM` list) still stands, but
  for this release it has been **checked and cleared**: `sales.billing_notes` has no `fn_enforce_*` trigger
  at all (`SqlScripts/322_billing_notes_rls.sql` is RLS only), so WP-1's `bn.JournalEntryId` write on an
  Issued BN lands. No trigger edit needed.
- **Memory `TEAS_TEST_PG per-shell`** — the env var dies between PowerShell calls; a skipped-test run
  fakes green. Compare the skip count against baseline.
- **Memory `TEAS_REPO_ROOT for RBAC tests`** — `RbacAuthMap`/`RbacMatrix` tests throw "Could not locate
  the TEAS repo root" unless set. WP-2 and WP-4 add endpoints → these tests WILL run.
- **Memory `RLS masked by superuser tests`** — `teas_test` connects as superuser, so RLS is bypassed.
  Nothing in R1 depends on RLS behaviour, but do not cite a green test run as proof of an RLS property.
- **Memory `co2 demo P&L load-bearing + polluted`** — co2/co3 are **Repttown, real data**. No test writes
  there, ever, except the WP-2 apply run under §3.2.5's protocol.

---

## 2. Consumer sweep

R1 widens one seam and narrows two. The seam widened is **"a BillingNote is now a GL-posting document
that creates AR"** — every place that assumed a BN moves no money is a consumer.

### 2.1 Seam: BillingNote becomes an AR-accruing document (WP-1/WP-2)

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `BillingNoteService.IssueAsync:294-318` | flips Draft→Issued, no GL, no period gate | **EXTEND** — post the JE, stamp `JournalEntryId`, add `EnsureOpenAsync(bn.DocDate)` |
| `BillingNoteService.CancelAsync:320-331` | cancels an Issued BN with no GL awareness | **EXTEND** — refuse when `JournalEntryId is not null` (`billing_note.cannot_cancel_posted`); an accrued BN is ledger-backed and immutable like every other posted doc |
| `BillingNoteService.DeleteDraftAsync:282-292` | Draft only | **SKIP** — a Draft is never accrued |
| `BillingNoteService.MarkSettledAsync:333-344` | manual Issued→Settled, no GL | **SKIP, documented** — AR outstanding is receipt-derived, not status-derived, so a manual settle cannot corrupt money; it only makes `rc.invoice_already_settled` refuse a later receipt. Pre-existing behaviour, unchanged. |
| `GlPostingService.PostReceiptAsync:124-132` (the shared DO/BN `else` branch) | credits **Sales** for a BN application | **EXTEND** — split the branch: BN application on an **accrued** BN credits **AR**; DO application and un-accrued BN keep crediting Sales |
| `GlPostingService.PostReceiptAsync:94-96, :124-125` (comments) | assert the cash-basis model | **EXTEND** — rewrite; a comment that states the old invariant is a trap for the next reader |
| `GlPostingService.PostReceiptAsync:135-142` (standalone, no applications) | credits Sales | **SKIP** — no invoice exists; genuinely cash-basis, correct |
| `ReceiptService.RebuildLinesAndTotalsAsync:165-175` (DO branch) | lets a receipt apply to an already-invoiced DO | **EXTEND** — refuse with `rc.do_already_invoiced` when a non-cancelled `BillingNote.DeliveryOrderId == doId` exists (the link is on the **BN**, not the DO — §3.2.3). Without this, the same sale is revenue-recognised twice (once at BN issue, once at DO-applied receipt) and its AR never clears. **Behaviour change — flagged in §8.** |
| `ReceiptService.RebuildLinesAndTotalsAsync:176-202` (BN branch) | issued / not-settled / same-customer guards | **SKIP** — guards stay correct |
| `ReceiptService.PostAsync:515-540` (direct-BN settlement / over-collection guard) | sums posted receipt applications per BN | **SKIP** — already correct; WP-1 must not duplicate this logic |
| `SubledgerReportService.ArMovementsAsync:68-109` | AR movements = TI + TI-linked receipts + CN/DN | **EXTEND** — add accrued-BN debit rows and BN-linked receipt credit rows. Fixes `CustomerStatementAsync` and `ArReconciliationAsync` for free. |
| `SubledgerReportService.ArAgingAsync:169-241` | TI + notes only | **EXTEND** — add accrued-BN rows with outstanding = `TotalAmount − Σ(posted receipt applications)` |
| `SubledgerReportService.ArReconciliationAsync:143-152` | control 1130 vs sub-ledger, `balanced := difference == 0` | **EXTEND (derived)** — no code change; but **this is the report that goes RED if any consumer above is missed.** It is the primary regression detector. |
| `SubledgerReportService.CustomerStatementAsync:245-278` | derives from `ArMovementsAsync` | **EXTEND (derived)** — no code change, but needs its own test |
| `SubledgerReportService.cs:78-83` (comment) | states "DO/BillingNote applications … never touch 1130" | **EXTEND** — rewrite |
| `ApAgingService.cs` | AP side | **SKIP** — untouched |
| Trial balance / Balance sheet / P&L (`FinancialReportService`) | GL-derived | **SKIP** — follow automatically; revenue simply moves from receipt date to issue date |
| `YearCloseService.cs:118` | rolls Revenue/Expense to retained earnings | **SKIP** — GL-derived, follows automatically |
| `NumberGapReportService` | JV number continuity | **SKIP** — BN issue now mints a JV number; that is a normal allocation, no gap |
| `TaxSummaryService` / `VatReportService` / ภ.พ.30 | VAT-company surfaces | **SKIP** — a non-VAT company files no ภ.พ.30 (and H16 blocks it in R2) |
| sales-summary report | excludes CN/DN today (an R4 item) | **DEFER** — R4 already owns it; C6 does not change its inputs |
| `ArAgingAsync`'s missing `DocDate <= asOf` filter (H7) | broken for TI today | **DEFER to R4, deliberately** — the new BN rows must follow the **same** convention as the existing TI rows so the report stays internally coherent. Fixing `asOf` for BN but not TI would make one report use two bases. R4/H7 fixes both in one pass. Attempt-log this so R4 knows. |
| Frontend AR-aging / customer-statement pages | render rows from the API | **SKIP** — no hand-enumerated doc-type list; new rows render as-is (implementer: confirm with one grep for a `docType` switch before ticking this) |
| MCP read tools over AR reports (`TeasMcpTools.cs`) | proxy the same services | **SKIP** — no independent enumeration |

### 2.2 Seam narrowed: which account types an expense line may debit (WP-4)

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `ExpenseClaimService.BuildLinesAsync:93-97` — override branch | validates exists/active/non-header | **EXTEND** — add the type rule |
| `ExpenseClaimService.BuildLinesAsync:95-97` — category-default branch | null-check only | **EXTEND** — run the same validation |
| `ExpenseClaimService.PayAsync:238-249` (re-guard) | re-zeroes `IsRecoverableVat`, never re-checks the account | **EXTEND** — re-validate the resolved account at Pay (defence for a claim drafted before this ships, or whose account was deactivated meanwhile) |
| Seeded `CAPEX` category → account `1610` (`AccountType.Asset`) | would be rejected by a naive "Expense only" rule, **on every company incl. real tenants** | **EXTEND (the rule itself)** — Asset is permitted **iff the resolved category has `IsCapex == true`** |
| `CreateExpenseCategoryValidator` (`ReferenceDtos.cs:36-43`) | no account validation | **EXTEND** — service-level validation in `ExpenseCategoryService.CreateAsync` (needs a DB read; FluentValidation is the wrong layer) |
| `ExpenseCategoryService` — no update/deactivate | a poisoned category is permanent | **EXTEND** — add `UpdateAsync` (`PUT /expense-categories/{id}`) covering the defaults + `IsActive` |
| `ExpenseClaimService.BuildLinesAsync:84-87` — category lookup | accepts an **inactive** category | **EXTEND** — refuse an inactive category (otherwise deactivation is cosmetic) |
| `PaymentVoucherService` line accounts (`:~162`) | same hole: no type check on either branch | **DEFER — product decision, §8.** A PV legitimately debits non-Expense accounts (asset purchase, prepaid, loan principal repayment). Pinning the allowed set is Ham's call, not the implementer's. C1's seam guard still protects the ledger from the *precision* half, and WP-3 fixes PV's 4-dp rounding. Attempt-log + troubles-wiki entry. |
| `VendorInvoiceService.BuildLinesAsync:226-240` | same hole | **DEFER — same reasoning, same log entry** |
| `FixedAssetService.EnsureAccountAsync:74-90` | already does the right thing | **SKIP** — the precedent to mirror, not to change |
| FE expense-claim account picker | shows the CoA list | **DEFER to R4 UX** — server refusal is the control; a picker that offers an invalid account is a UX nit, not a money defect. Log it. |

### 2.3 Seam narrowed: 2-decimal money at the posting seam (WP-3)

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `JournalEntry.MarkPosted:58-71` | header-only balance check | **EXTEND** — the guard lives here |
| `GlPostingService.BuildAndPostAsync:526-567` | all document posters | **SKIP (covered)** — calls `MarkPosted` |
| `GlPostingService.PostClosingEntryAsync:448-492` | year-end, bypasses `BuildAndPostAsync` | **SKIP (covered)** — calls `MarkPosted`. ⚠️ but see the co5/co7 note in §8: a company whose ledger *already* holds sub-satang balances can no longer be year-closed. |
| `JournalService.PostAsync:82-115` | draft/MCP post path | **SKIP (covered)** — calls `MarkPosted` |
| `CreateJournalValidator` (`JournalDtos.cs:85-108`) | draft + MCP, no precision rule | **EXTEND** — add the 2-dp rule (fail-fast, good message) |
| `UpdatePayrollDeductionsValidator` (`PayrollDtos.cs:105-160`) | no scale rule | **EXTEND** |
| `ExpenseClaimService.cs:100` `Math.Round(…, 4)` | stores 4 dp | **EXTEND** — `4` → `2` |
| `PaymentVoucherService.cs:234` `Math.Round(…, 4)` | stores 4 dp — **proven to reach the GL** (A4 §K1) | **EXTEND** — `4` → `2` |
| `VendorInvoiceService.cs:240` `Math.Round(…, 4)` | stores 4 dp | **EXTEND** — `4` → `2` |
| `*.TotalAmountThb = Math.Round(… * ExchangeRate, 4)` — PV:374, PO:95, VI:275, RC:92/379, TAN:112, TI:306/476 | a **reporting** THB-equivalent field, never a JE line | **SKIP — DO NOT TOUCH.** 4 dp is correct for an FX-converted memo field. Changing these is an out-of-scope regression. |
| `QuotationChainServices.cs:23`, `TaxInvoiceService.cs:575` — `Math.Round(qty * price, 4)` | line gross on sales docs | **DEFER, logged** — sales lines flow to a TI whose `SubtotalAmount`/`TaxAmount` drive the JE; no evidence of a sub-satang JE from this path in the swarm. The seam guard now catches it as `je.precision` if it ever happens. Attempt-log + wiki. |
| `CreateManualJournalValidator` | already correct | **SKIP** — it is the model |
| Expense-claim / PV / VI / TI / RC line `Amount` validators (`GreaterThan(0)` only) | no scale rule | **EXTEND for expense claim + PV + VI** (the three with proven 4-dp storage); the rest are covered by the seam. Use the shared extension so adding one later is one line. |

---

## 3. Design

### 3.1 The rounding contract (C1) — state this once, cite it everywhere

> **THB is a 2-decimal currency.**
> 1. Money **entering** the system from a caller is **REJECTED** at the API edge if it carries more than
>    2 decimal places (FluentValidation, HTTP 400, field-level message).
> 2. Money **computed** internally (VAT, proration, PIT, allocations) is **ROUNDED to 2 dp,
>    `MidpointRounding.AwayFromZero`, at the point of computation**.
> 3. The **posting seam** (`JournalEntry.MarkPosted`) **REJECTS** any line whose `DebitAmount` or
>    `CreditAmount` is not exactly 2 dp. It is a backstop, **never a rounding point.**

**Why the seam rejects and does not round:** rounding at the seam can break `ΣDr == ΣCr`. C4's live repro
(`C4-jv-validation.md` F1) posted `33.3333 / 33.3333 / 33.3334` against `Cr 100.00`; rounding each line to
2 dp gives `33.33 × 3 = 99.99 ≠ 100.00`. Rounding would then require an invented balancing plug — a system
silently inventing a satang. Rejection is the only safe answer.

**A request balanced at 4 dp but not at 2 dp is rejected in full.** No partial acceptance, no rounding, no
plug line. The caller must restate the split in satang (`33.33 / 33.33 / 33.34`). This is stated in the
error message.

**Exact code — `Accounting.Domain/Entities/Ledger/JournalEntry.cs`, inside `MarkPosted`, after the
`IsBalanced` check and before the DocNo check:**

```csharp
// R1/C1 — THB is a 2-decimal currency and gl.journal_lines is numeric(19,4), so a 3rd/4th
// decimal is STORED silently and makes ΣDr==ΣCr pass on invisible satang (co5 TB 822801.785,
// co7 544060.031). This is the LAST gate EVERY posting path shares:
//   GlPostingService.BuildAndPostAsync · GlPostingService.PostClosingEntryAsync ·
//   JournalService.PostAsync.
// REJECT, never round: rounding here can break ΣDr==ΣCr (33.3333×3 vs 100.00) and would make
// the system invent a satang. Validators fail fast on top of this for a better message.
foreach (var l in Lines)
{
    if (decimal.Round(l.DebitAmount, 2) != l.DebitAmount
     || decimal.Round(l.CreditAmount, 2) != l.CreditAmount)
        throw new DomainException("je.precision",
            $"Line {l.LineNo}: amounts must have at most 2 decimal places " +
            $"(got Dr {l.DebitAmount} / Cr {l.CreditAmount}). " +
            "Restate the split in satang — the entry is not rounded automatically.");
}
if (decimal.Round(TotalDebit, 2) != TotalDebit || decimal.Round(TotalCredit, 2) != TotalCredit)
    throw new DomainException("je.precision",
        "Journal totals must have at most 2 decimal places.");
```

`je.precision` → **422** via `DomainExceptionMiddleware.StatusFor`'s default (verified,
`DomainExceptionMiddleware.cs:38`).

**Shared validator extension** — new file
`Accounting.Application/Abstractions/MoneyValidationExtensions.cs`, mirroring the existing
`CurrencyValidationExtensions.ThbOnly` shape (`CurrencyValidationExtensions.cs:21`):

```csharp
public static IRuleBuilderOptions<T, decimal> Satang<T>(this IRuleBuilder<T, decimal> rule) =>
    rule.Must(v => decimal.Round(v, 2) == v)
        .WithMessage("Amounts must have at most 2 decimal places (THB has 2).");
```

Applied to: `CreateJournalValidator` lines (Debit + Credit), `UpdatePayrollDeductionsValidator.Amount`,
`CreateExpenseClaimValidator`/update line `Amount`, PV line `Amount`, VI line `Amount`.

**Rejected alternatives** (do not relitigate):
- *Round at the seam* — breaks `ΣDr == ΣCr`; see above.
- *Guard in `GlPostingService.BuildAndPostAsync`* — misses `PostClosingEntryAsync` and
  `JournalService.PostAsync`, i.e. misses the exact path the swarm exploited.
- *A DB CHECK constraint on `journal_lines`* — would fire as a raw 23514 → 500, and cannot carry a message.
  The domain guard is the product surface; a constraint could be added later as belt-and-braces.
- *Per-validator patching only* — that is what failed: four paths and counting.

### 3.2 C6 — non-VAT revenue and AR

#### 3.2.1 Schema (WP-1) — one EF migration

`sales.billing_notes` gains **`journal_entry_id bigint NULL`** (`BillingNote.JournalEntryId`, `long?`).
No FK required (mirrors the repo's existing `ExpenseClaim.JournalEntryId` / `PayrollRun.JournalId`
convention). No index needed.

This column is the **single source of truth for "this invoice has accrued"** — used by the receipt path,
the AR reports, the cancel guard, and the backfill's idempotency check. It replaces any string matching on
`Reference`.

- EF migration only. **No SqlScript.** No RLS pattern needed (`ALTER TABLE … ADD COLUMN` runs as the table
  owner at startup).
- **Prod DB backup is mandatory before deploy** (memory: `TEAS prod deploy via plink` — new
  migrations/SqlScripts run at API startup).
- Deploy probe = **row counts, not exit codes**: after deploy, `SELECT count(*) FROM sales.billing_notes
  WHERE journal_entry_id IS NOT NULL;` → expect `0` pre-backfill, and the column must exist.

#### 3.2.2 Forward fix — accrual at issue (WP-1)

New method on `IGlPostingService` / `GlPostingService`, mirroring `PostTaxInvoiceAsync:42-67` exactly:

```csharp
public async Task<long> PostBillingNoteAsync(long billingNoteId, CancellationToken ct)
{
    var bn = await _db.BillingNotes.FirstOrDefaultAsync(b => b.BillingNoteId == billingNoteId, ct)
        ?? throw new DomainException("gl.bn_missing", $"Invoice {billingNoteId} not found for GL posting.");

    // A non-VAT company's Invoice is its ONLY sales document (ม.86/4 blocks the TI), so it is the
    // revenue-recognition point. A VAT company's BN groups already-accrued Tax Invoices and must
    // NEVER post here — that would double-count AR and revenue.
    if (bn.VatAmount != 0m)
        throw new DomainException("gl.bn_vat_unexpected",
            $"Invoice {bn.DocNo} carries VAT {bn.VatAmount}; a non-VAT invoice must not.");

    var ar    = await ResolveAccountIdAsync(bn.CompanyId, _accounts.ArAccount, ct);
    var sales = await ResolveAccountIdAsync(bn.CompanyId, _accounts.SalesAccount, ct);

    var lines = new List<JournalLine>
    {
        new() { LineNo = 1, AccountId = ar,    DebitAmount = bn.TotalAmount, CreditAmount = 0m,
                Description = $"AR {bn.DocNo}" },
        new() { LineNo = 2, AccountId = sales, DebitAmount = 0m, CreditAmount = bn.TotalAmount,
                Description = $"Sales {bn.DocNo}" },
    };

    return await BuildAndPostAsync(
        bn.CompanyId, bn.BranchId, bn.DocDate, $"IV {bn.DocNo}", bn.DocNo, lines, ct,
        businessUnitId: bn.BusinessUnitId);
}
```

`BillingNoteService` gains `IGlPostingService` + `IPeriodCloseService` + `ICompanyTaxConfigService`
(the last is **already injected** as `taxCfg`, `BillingNoteService.cs:24` — reuse it). `IssueAsync`
becomes, inside the existing transaction, after the status flip and number allocation:

```
var tax = await taxCfg.GetAsync(ct);
if (!tax.VatMode)
{
    await period.EnsureOpenAsync(bn.DocDate, ct);          // NEW: issuing now moves money
    bn.JournalEntryId = await gl.PostBillingNoteAsync(bn.BillingNoteId, ct);
}
```

- **Order matters:** allocate the doc number and set `Status = Issued` first (the existing
  `AllocateAndSaveAsync` block), then post — `PostBillingNoteAsync` re-reads the BN on the same
  `DbContext` and needs `DocNo` populated (identical to `ExpenseClaimService.PayAsync:280-284`'s
  identity-map note).
- `EnsureOpenAsync` goes **before** the number allocation is committed if the implementer can do so
  without restructuring; otherwise immediately before the GL call inside the same transaction — either
  way the whole thing rolls back on `period.closed`, and no invoice number is consumed. State which was
  chosen in the attempt log.
- VAT companies: no JE, unchanged behaviour, zero risk.

#### 3.2.3 Forward fix — the receipt must settle AR, not re-recognise revenue (WP-1)

`GlPostingService.PostReceiptAsync`, replacing the shared `else` at `:124-132`. Pre-load the applied BNs
once (mirroring the existing `tiBu` dictionary at `:87-93`):

```
var bnIds = rc.Applications.Where(a => a.BillingNoteId.HasValue).Select(a => a.BillingNoteId!.Value).ToList();
var bnInfo = bnIds.Count > 0
    ? await _db.BillingNotes.AsNoTracking()
        .Where(b => bnIds.Contains(b.BillingNoteId))
        .ToDictionaryAsync(b => b.BillingNoteId, b => new { b.JournalEntryId, b.BusinessUnitId }, ct)
    : new(...);
```

then per application:

| application | credit line | why |
|---|---|---|
| `TaxInvoiceId` | **AR** (unchanged) | VAT path already accrued |
| `BillingNoteId` where that BN has `JournalEntryId != null` | **AR**, `Description = $"AR settle {rc.DocNo}"`, `BusinessUnitId = bn.BusinessUnitId` | the invoice accrued; the receipt clears it |
| `BillingNoteId` where that BN has `JournalEntryId == null` | **Sales** (legacy), description unchanged | a pre-fix invoice that never accrued — the receipt is still its revenue-recognition point. **This is what makes the transition window safe in either order**, and it self-heals: once WP-2 backfills the BN, later receipts credit AR. |
| `DeliveryOrderId` | **Sales** (unchanged) | a DO-applied receipt is a genuine cash sale with no invoice |
| no applications (standalone) | **Sales** (unchanged) | no invoice exists |

`ReceiptService.RebuildLinesAndTotalsAsync`, DO branch (`:165-175`). ⚠️ **`DeliveryOrder` has no
`BillingNoteId` column** — the link lives on `BillingNote.DeliveryOrderId` (`BillingNote.cs:36`); the
`billingNoteId` field on the DO detail DTO is a computed lookup (`SalesOrderDeliveryServices.cs:467-470`).
So the guard needs one batched read, added beside the existing `dos`/`bns` batch loads at `:130-140`:

```csharp
var invoicedDo = doIds.Count > 0
    ? (await _db.BillingNotes.AsNoTracking()
        .Where(b => b.DeliveryOrderId != null
                 && doIds.Contains(b.DeliveryOrderId!.Value)
                 && b.Status != BillingNoteStatus.Cancelled)
        .Select(b => new { DoId = b.DeliveryOrderId!.Value, b.DocNo })
        .ToListAsync(ct)).ToDictionary(x => x.DoId, x => x.DocNo)
    : new Dictionary<long, string?>();
```

then inside the DO branch:

```csharp
// R1/C6 — a DO that was already invoiced accrued its revenue and AR at the Invoice. Applying a
// receipt to the DO instead of to that Invoice would recognise the same sale's revenue twice and
// leave its AR outstanding forever. Live evidence: A4 DO 17 → Invoice 07-2026-IV-0001.
if (invoicedDo.TryGetValue(doId, out var ivNo))
    throw new DomainException("rc.do_already_invoiced",
        $"ใบส่งของ {dord.DocNo} ออกใบแจ้งหนี้ {ivNo} แล้ว — กรุณารับชำระกับใบแจ้งหนี้ฉบับนั้นแทน " +
        $"(Delivery Order {dord.DeliveryOrderId} was already invoiced as {ivNo}; " +
        "apply the receipt to that invoice.)");
```

#### 3.2.4 Forward fix — AR reports (WP-1)

`SubledgerReportService.ArMovementsAsync` gains two row sources (only accrued BNs — `JournalEntryId != null`
— so pre-fix invoices stay out of AR exactly as they are out of the GL):

```
// Debit rows — one per accrued Invoice
db.BillingNotes.Where(b => b.CompanyId == tenant.CompanyId
                        && b.JournalEntryId != null
                        && b.Status != BillingNoteStatus.Cancelled
                        && (customerId == null || b.CustomerId == customerId))
  → PartyMovement(CustomerId, DocDate, "Invoice", Rank 0, BillingNoteId, DocNo, null, TotalAmount, 0m)

// Credit rows — one per Receipt, summing only its applications against ACCRUED BNs
db.Receipts.Where(r => r.CompanyId == … && r.Status == Posted && (customerId == null || …))
  .Select(r => new { …, AppliedBn = r.Applications
        .Where(a => a.BillingNoteId != null && accruedBnIds.Contains(a.BillingNoteId.Value))
        .Sum(a => a.AppliedAmount) })
  → merged into the SAME receipt row as the existing TI-applied credit (one row per receipt, Credit =
    tiApplied + bnApplied) so a receipt never appears twice in a statement.
```

`ArAgingAsync` gains an accrued-BN source, mirroring the existing TI shape:

```
outstanding(bn) = bn.TotalAmount − Σ(AppliedAmount of applications on POSTED receipts for that bn)
```
included when `!= 0`, bucketed by `bn.DocDate` with the **identical** ageing arithmetic as the TI rows
(including the current `asOf` gap — see §2.1's H7 deferral). Grouped by `CustomerId` with the TI/note rows,
so a customer with both never splits into two lines.

Both comment blocks (`GlPostingService.cs:94-96`/`:124-125` and `SubledgerReportService.cs:78-83`) get
rewritten to state the new model.

#### 3.2.5 The Repttown backfill (WP-2) — REDESIGNED 2026-07-31 after Thai practice research

> **This section replaces an earlier design that posted correcting entries at their true event dates
> inside closed periods.** Research (`specs/research-thai-prior-period-correction.md`) established that
> Thai practice is the opposite: a prior-period error is corrected in the **current open period against
> opening retained earnings** (กำไรสะสม), with comparatives restated for presentation only. Reopening a
> period whose statements are already filed with the DBD is done only for very large errors or on DBD
> demand, and is the signing CPA's call. The redesign is also much simpler.

**The rule — one entry per OUTSTANDING invoice, nothing else.**

| Case | Entry | Why |
|---|---|---|
| Sale already **settled** (any year) | **none** | Revenue was recognised at the receipt, cash was collected, AR is 0 today. The correct present state *is* the actual present state. Only the timing **within closed years** was wrong, and that is fixed by the amended tax return, not by the ledger. |
| Invoice **unpaid**, issued in a **closed** fiscal year | `Dr 1130 AR / Cr กำไรสะสม (retained earnings)`, dated **in the current open period** | The revenue belongs to a closed year, so it must not hit this year's P&L. |
| Invoice **unpaid**, issued in the **current open** fiscal year | `Dr 1130 AR / Cr Revenue`, dated at the invoice's issue date | The period is open; normal recognition applies. |

**Consequences of the redesign — all simplifications:**
- **No entry ever lands in a closed period.** The closed-fiscal-year hard stop, the reopen dance, and any
  interaction with the year-close deadlock (H10) all disappear from this work package.
- Statements already filed with the DBD are untouched.
- Partially-paid invoices use the **outstanding** amount, not the invoiced amount.
- Cancelled/void invoices: no entry.

**Detector, unchanged and still sound.** Only invoices with `JournalEntryId IS NULL` are candidates —
after WP-1 ships, null unambiguously means "issued before the fix". It doubles as the idempotency and
resume key. No JE-line inspection and no cutoff-date heuristic is needed.

**Delivery mechanism.** A super-admin-gated endpoint, mirroring the repo's existing
`tax-filings/pnd30?mode=preview|finalize` shape:

```
POST /admin/nonvat-ar-backfill?mode=preview      → 200, full plan, ZERO writes
POST /admin/nonvat-ar-backfill?mode=apply        → 200, plan + minted JV numbers
```

Correcting entries are posted through `IGlPostingService.PostManualEntryAsync`, which already accepts an
arbitrary `docDate` + `reference` and allocates a real JV number.

**The preview response is a deliverable in its own right — it is what goes to the accountant.** It must
report, per fiscal year:
- `outstandingTotal` — Σ unpaid amounts of invoices issued in that year
- `creditSide` — `Revenue` (current open year) or `RetainedEarnings` (closed year)
- `invoiceCount`, and the invoice list with doc numbers, issue dates and outstanding amounts

**A non-zero `RetainedEarnings` figure for a year whose ภ.ง.ด.50 was already filed is an amended-filing
question, not an engineering one** (§8). Hand those numbers to the company's CPA.

**Book-to-tax follow-through (record it in the release notes, it is easy to forget).** Once the
retained-earnings correction is booked, the **current** year's ภ.ง.ด.50 must back that revenue out — it
was already taxed via the amended prior-year return, so leaving it in would tax it twice.

**Resume protocol (irreversible prod writes).** Each invoice's entry commits in **one transaction per
invoice**, ending with `bn.JournalEntryId = …`. If the run dies mid-way, re-running `mode=apply` skips
every invoice that already has a `JournalEntryId` and resumes at the first that does not. The response
reports `resumedFrom` / `alreadyDone` counts. **Never a single giant transaction** — a crash at invoice
300 of 400 must not roll back 299 good corrections *and* must not double-post them.

**Which account is "กำไรสะสม"?** Resolve it from the company's live chart of accounts, never hardcode a
code — the same lesson as the input-VAT account in leg V3b. If the company has no retained-earnings
account the run must **stop with a clear error**, not invent one.

### 3.3 C5 — expense account type + fixable categories (WP-4)

**The rule** (one method, cited by every caller):

```csharp
/// R1/C5 — an expense-claim line may debit:
///   • any AccountType.Expense account (5xxx incl. COGS 5000), OR
///   • an AccountType.Asset account ONLY when the resolved category IsCapex
///     (the seeded CAPEX category defaults to 1610 Office Equipment — MasterDataServices.cs:489/:446 —
///      so a naive "Expense only" rule would brick every company, including the real tenants).
/// Never Liability, Equity or Revenue. Mirrors FixedAssetService.EnsureAccountAsync:74-90.
private async Task<long> EnsureExpenseAccountAsync(
    long accountId, int companyId, bool categoryIsCapex, CancellationToken ct)
```

existing checks (exists tenant-scoped / active / non-header) stay, plus:

```csharp
var typeOk = account.AccountType == AccountType.Expense
          || (categoryIsCapex && account.AccountType == AccountType.Asset);
if (!typeOk)
    throw new DomainException("expense_claim.expense_account_invalid",
        $"Account {account.AccountCode} is {account.AccountType} — an expense line must use an " +
        "expense account" + (categoryIsCapex ? " or the category's fixed-asset account." : "."));
```

Call sites (`ExpenseClaimService`):
1. `BuildLinesAsync` **override** branch — pass `category.IsCapex`.
2. `BuildLinesAsync` **category-default** branch — same call, same rule (delete the "already validated"
   comment; it was never true).
3. `BuildLinesAsync` category lookup — also refuse `!category.IsActive`
   (`expense_claim.expense_category_inactive`), else deactivation is cosmetic.
4. `PayAsync` re-guard block (`:238-249`) — re-validate each line's resolved account against its category
   before posting, alongside the existing `IsRecoverableVat` re-guard.

**Category master — validate at create, and make a poisoned one fixable:**

- `ExpenseCategoryService.CreateAsync` (`MasterDataServices.cs:627`): when `DefaultExpenseAccountId` is
  supplied, validate it with the **same rule**, using `req.IsCapex` as the capex flag. Error
  `expense_category.default_account_invalid`. (Service level, not FluentValidation — it needs a DB read.)
- **New** `ExpenseCategoryService.UpdateAsync` + `PUT /expense-categories/{id}`, permission
  `Permissions.Sys.ExpenseCatManage` (the same one `POST` already uses — no new permission, no RBAC
  matrix churn). Request `UpdateExpenseCategoryRequest(NameTh, NameEn, Description,
  DefaultExpenseAccountId, DefaultTaxCodeId, DefaultIsRecoverableVat, DefaultWhtTypeId, IsCapex, IsCogs,
  ParentCategoryId, IsActive)`. `CategoryCode` is **immutable** (it is a document-number sub-prefix —
  `ExpenseCategory` doc-comment). Same account validation as create. Tenant-scoped lookup;
  `expense_category.not_found` → 404.
- No delete route: a category referenced by historical claims must not vanish. `IsActive = false` is the
  fix for a poisoned one. co7's category 78 becomes deactivatable (and is cleared entirely by the
  post-R4 reseed).

### 3.4 C1 — the three input paths (WP-3)

Beyond the seam (§3.1):
- `CreateJournalValidator`: add `.Satang()` on line Debit + Credit. **Also add** `Reference`
  `MaximumLength(255)`, line `Description` `MaximumLength(500)` and the `Count <= 200` cap — the manual
  validator has all three and its own comment says the missing ones came back as a raw Postgres 22001 →
  500 (`JournalDtos.cs:67-70`, C4 F2/F3). One-line each, same file, closes three findings for free.
- `UpdatePayrollDeductionsValidator.Amount`: add `.Satang()`.
- `ExpenseClaimService.cs:100`: `Math.Round(input.Amount, 4, …)` → `2`. Plus `.Satang()` on the DTO's line
  `Amount`.
- `PaymentVoucherService.cs:234`: same `4` → `2`. Plus `.Satang()` on the PV line `Amount`.
- `VendorInvoiceService.cs:240`: same `4` → `2`. Plus `.Satang()` on the VI line `Amount`.
- **Do not touch any `TotalAmountThb = Math.Round(… * ExchangeRate, 4)`** — see §2.3.

### 3.5 C3 — payroll period + future guard (WP-5)

**Why the guard does NOT brick payroll — the worked analysis (read this before doubting the guard):**

The dispatch and `B5-payroll-co7.md`'s remediation note warn that adding the guard bricks payroll on a
company whose only open period already holds a run. Traced through the actual code:

- `payroll.duplicate_period` (`PayrollRunService.cs:46-48`) blocks a **second run for the same
  `PeriodYearMonth`**, not a run in a different month.
- `IsOpenAsync` (`PeriodCloseService.cs:25-42`) returns OPEN for the current Bangkok month even with no
  `AccountingPeriod` row.
- Therefore: the **current month is always postable**, and next month becomes postable when the calendar
  rolls. The only blocked case is a **back month** — which is exactly what a period close is supposed to
  block, and which now has a real way out: **O14 monthly reopen**, `POST /periods/{y}/{m}/reopen`,
  permission `gl.period.close` (`PeriodEndpoints.cs:20-28`, verified to exist — the troubles-wiki entry
  saying it does not is superseded).
- Residual: reopen refuses inside a **closed fiscal year** (`period.year_closed`). That is a pre-existing
  limitation (H10/H11, owned by R3), not something C3 creates.

**Implementation.** `PayrollRunService` injects `IPeriodCloseService period`. A shared private guard, called
first in **both** `PostAsync` and `PayAsync` (both JEs are dated `run.PayDate`):

```csharp
/// R1/C3 — payroll was the ONLY posting path with no period gate (VERDICT C3; every other poster
/// guards: JournalService:265, ExpenseClaimService:252, ReceiptService:398, FixedAsset 238/300,
/// BankRec 236). Both the accrual JE (GlPostingService.PostPayrollRunAsync → BuildAndPostAsync,
/// docDate = run.PayDate) and the settlement JE (PostManualEntryAsync, docDate = run.PayDate)
/// land on PayDate, so PayDate is what must be guarded.
private async Task EnsurePostablePayDateAsync(PayrollRun run, CancellationToken ct)
{
    var today = clock.TodayInBangkok();

    // Future bound — same contract as the manual JV path (JournalService.cs:158). Without it the
    // system accepted period 209912 / payDate 2099-12-31 and minted a permanent JE (co7 JE 301).
    // IsOpenAsync would also refuse it, but with a "reopen the period" message that is nonsense
    // for a month that was never open.
    // Ham decision 2026-07-31: bound to the run's OWN period, NOT to today — posting on the 28th
    // for a pay date of the 30th is normal practice, so `PayDate > today` would block real work.
    // This still kills the unbounded case (period 209912 / payDate 2099-12-31 → co7 JE 301),
    // because the period itself must be open and the pay date is capped inside it.
    var periodEnd = new DateOnly(run.PeriodYear, run.PeriodMonth, 1).AddMonths(1).AddDays(-1);
    if (run.PayDate > periodEnd)
        throw new DomainException("payroll.pay_date_outside_period",
            $"วันจ่ายเงิน {run.PayDate:yyyy-MM-dd} อยู่นอกงวด {run.PeriodYear}-{run.PeriodMonth:D2} " +
            $"(ต้องไม่เกิน {periodEnd:yyyy-MM-dd}) " +
            $"[pay date must fall within the run's own period].");

    if (!await period.IsOpenAsync(run.PayDate.Year, run.PayDate.Month, ct))
        throw new DomainException("payroll.period_closed",
            $"งวดบัญชี {run.PayDate.Year}-{run.PayDate.Month:D2} ปิดแล้ว จึงลงบัญชีเงินเดือนไม่ได้ — " +
            $"เปิดงวดใหม่ก่อน (POST /periods/{run.PayDate.Year}/{run.PayDate.Month}/reopen " +
            $"ต้องมีสิทธิ์ gl.period.close) แล้วลงบัญชีอีกครั้ง จากนั้นปิดงวดตามเดิม. " +
            $"[Period {run.PayDate.Year}-{run.PayDate.Month:D2} is closed. Reopen it via " +
            $"POST /periods/{run.PayDate.Year}/{run.PayDate.Month}/reopen (needs gl.period.close), " +
            $"post, then close it again.]");
}
```

**Two distinct codes on purpose.** `IsOpenAsync` answers "closed" for both a genuinely closed month and a
never-opened future month, and only the first has a reopen as its way out. Returning a bare `period.closed`
for `2099-12` would send the user to a route that answers `period.not_closed` — a dead end. That is
precisely the failure the plan's decision #2 forbids.

**Stated behaviour change (Ham-decided, §8 #3):** a run's `PayDate` must fall **within the run's own
period**. Pre-payday posting still works — post on the 28th for a pay date of the 30th — which is why the
bound is the period end and not `today`. `PayDate` beyond the period end is refused; a period that is
itself closed or never-opened is refused by the `IsOpenAsync` check with its own message.

`payroll.period_closed` / `payroll.pay_date_outside_period` → 422 (middleware default).

**Implementer note:** confirm the run's period fields are named `PeriodYear`/`PeriodMonth` before using
them (the sketch above assumes it); if the entity stores the period differently, derive `periodEnd` from
whatever it does store — the rule is "last day of the run's own period", not the exact field names.

---

## 4. Invariants

Each is a money statement, not a field value. `T#` = the test in §6 that proves it.

**C6 — revenue and AR (WP-1)**
- **I1** — For any single non-VAT sale, **total revenue recognised is exactly the invoice amount, once**.
  It moves from the receipt date to the issue date; it is never recognised twice and never dropped. → **T1, T2, T7**
- **I2** — **AR clears exactly.** After a non-VAT invoice is fully receipted, account 1130's net movement
  for that sale is **0.00** and the AR sub-ledger row disappears. → **T2**
- **I3** — **Dr = Cr on every new posting** (invoice accrual and receipt settlement alike). → **T1, T2**
- **I4** — **Cash is unchanged.** The invoice accrual touches no cash/bank account; the receipt debits
  cash by exactly the same amount as before this change. The customer pays the same, on the same day. → **T2**
- **I5** — **The AR control account ties to the AR sub-ledger.**
  `ar-aging.reconciliation.difference == 0` and `balanced == true` for a non-VAT company with any mix of
  issued, part-paid and settled invoices. This is the release's primary regression detector. → **T3**
- **I6** — **A VAT company's books do not move.** No BillingNote on a VAT-registered company posts any JE;
  its TB, AR aging and customer statements are byte-identical before and after. → **T4**
- **I7** — **No sale is billed into revenue twice through the DO path.** A receipt cannot be applied to a
  delivery order that has already been invoiced. → **T5**
- **I8** — **The transition window is safe in either order.** A receipt posted after deploy against an
  invoice issued before deploy recognises revenue exactly once (at the receipt), and AR never goes
  negative. → **T6**

**C6 backfill (WP-2)** — redesigned; see §3.2.5
- **I9** — **A settled sale is not touched at all.** The backfill posts NOTHING for an invoice that is
  fully receipted, so its total revenue is unchanged by construction. For an **unpaid** invoice, exactly
  one entry is posted and it increases AR by the outstanding amount. Formally:
  `Σ Dr 1130 == Σ outstanding of unpaid non-VAT invoices`, and the credit side splits into
  **Revenue** (invoice issued in the current open fiscal year) + **กำไรสะสม** (issued in a closed year).
  → **T8, T9**
- **I10** — **The backfill touches no cash account.** 1110/1120 balances are bit-identical before and
  after. → **T9**
- **I11** — **Dr = Cr on every correcting entry**, and **no existing journal entry is modified or
  deleted** (they are immutable; corrections are new postings only). → **T8**
- **I12** — **Idempotent and resumable.** Running `apply` twice produces zero additional entries; a run
  interrupted mid-way resumes without duplicating or skipping. → **T10**
- **I13** — **`mode=preview` writes nothing.** Row counts of `gl.journal_entries`, `gl.journal_lines` and
  `sales.billing_notes WHERE journal_entry_id IS NOT NULL` are unchanged by a preview. → **T11**
- **I13b** — **No correcting entry is dated inside a closed period.** Every entry the backfill posts falls
  in the current open period, except the current-open-year case which is dated at its own issue date
  (also open by definition). → **T8**

**C1 — precision (WP-3)**
- **I14** — **No amount with more than 2 decimal places can enter the ledger by any path.** Every posted
  journal line has `Round(x,2) == x`. → **T12, T13**
- **I15** — **A request balanced at 4 dp but not at 2 dp is rejected in full** — never rounded, never
  partially accepted, never balanced with an invented plug line. → **T13**
- **I16** — **The printed document foots to its journal entry.** A payslip's printed net equals the 2170
  credit to the satang. → **T14**

**C5 — expense accounts (WP-4)**
- **I17** — **An expense claim can only debit an account that represents a cost the employee incurred** —
  an expense account, or the fixed-asset account of a capex category. A claim can never debit bank, AP,
  revenue, equity or input VAT. → **T15**
- **I18** — **A claim marked Paid always moves money out to the employee.** The credit side is a cash/bank
  account and the debit side is never a cash/bank account, so "Paid" can never mean "we moved money
  between two of our own accounts". → **T15**
- **I19** — **The seeded CAPEX category still works on every company.** → **T16**
- **I20** — **A mis-mapped category is fixable** — it can be corrected or deactivated, and a deactivated
  one cannot be used on a new claim. → **T17**

**C3 — payroll period (WP-5)**
- **I21** — **Payroll cannot move a closed month.** Neither `post` nor `pay` writes a journal entry into a
  period that is not open. → **T18**
- **I22** — **A payroll run's pay date stays inside its own period.** It cannot post with a pay date beyond the period end (which is what made the 2099 JE possible), but posting *before* payday within the period still works. → **T19**
- **I23** — **Payroll still works.** A run for the current Bangkok month posts and pays normally, and a
  back-month run posts after the period is reopened via O14. The refusal message names that route. → **T20**

---

## 5. Requirements checklist

### WP-1 — C6 forward fix: non-VAT revenue + AR *(no dependencies; do this first)*

- [x] EF migration adding `sales.billing_notes.journal_entry_id bigint NULL` + `BillingNote.JournalEntryId (long?)` + EF configuration. **No SqlScript.** Done when `dotnet ef migrations list` shows it and a fresh `teas_test` applies clean. Evidence: `20260811115620_AddBillingNoteJournalEntryId` — `AddColumn<long>("journal_entry_id", schema:"sales", table:"billing_notes", nullable:true)`, no other DDL. `dotnet ef migrations list` against `teas_test` shows it applied (no `(Pending)` tag). EF config: no explicit `b.Property(...)` chain needed — snake_case convention + no-FK/no-index (mirrors `PayrollRun.JournalId`, which also has zero explicit config) — documented inline instead of a no-op call.
- [x] `IGlPostingService` + `GlPostingService`: new `PostBillingNoteAsync` exactly per §3.2.2 (incl. the `gl.bn_vat_unexpected` assertion). Done when a non-VAT BN issue produces `Dr 1130 / Cr 4000` for `TotalAmount` with `Reference == bn.DocNo`. Evidence: T1.
- [x] `BillingNoteService`: inject `IGlPostingService` + `IPeriodCloseService`; `IssueAsync` gates on `!taxCfg.GetAsync().VatMode`, calls `EnsureOpenAsync(bn.DocDate)`, posts, stamps `bn.JournalEntryId` — all inside the existing transaction. Done when a closed-period issue returns `period.closed` **and consumes no invoice number**. Evidence: `Closed_period_issue_returns_period_closed_and_consumes_no_invoice_number`. **Placement chosen:** `EnsureOpenAsync` called BEFORE the number allocation (matches the spec's stated preference, achievable with no restructuring — fetch `taxCfg` once at the guard, gate before `AllocateAndSaveAsync`); `PostBillingNoteAsync` call stays AFTER allocation (needs `DocNo` populated, identity-map re-read).
- [x] `BillingNoteService.CancelAsync`: refuse when `JournalEntryId is not null` (`billing_note.cannot_cancel_posted`, 422). Evidence: `Cancel_on_accrued_invoice_is_refused`.
- [x] `GlPostingService.PostReceiptAsync`: split the DO/BN `else` per §3.2.3's table; pre-load applied BNs in one query; rewrite the `:94-96` and `:124-125` comments. Evidence: T2, T6.
- [x] `ReceiptService`: DO branch refuses an already-invoiced DO (`rc.do_already_invoiced`). Evidence: T5 (message names the invoice DocNo, matches Ham's decision §8 #4 exactly).
- [x] `SubledgerReportService.ArMovementsAsync`: accrued-BN debit rows + BN-applied receipt credits merged into the existing per-receipt row. Rewrite the `:78-83` comment. Evidence: T3 (customer statement).
- [x] `SubledgerReportService.ArAgingAsync`: accrued-BN rows, outstanding = total − Σ posted applications, same bucketing and grouping as TI rows. Evidence: T3 (aging + reconciliation.difference == 0).
- [x] Grep the frontend for a hand-enumerated AR `docType` list; if one exists, extend it and note it. If none, record the grep in the attempt log. **Found one**: `frontend/lib/utils.ts`'s `DOC_TYPE_I18N_KEY` map (feeds `docTypeLabelKey()`, used by customer-statement/vendor-ledger row rendering — comment there explicitly says "Must cover every docType emitted by SubledgerReportService.cs"). Extended with `Invoice: 'billingNote'` (reuses the existing `billingNote` i18n message key, already "Invoice"/"ใบแจ้งหนี้" — no new message-file entries needed).
- [x] Tests T1–T7 green. Evidence: all 9 tests in `NonVatArAccrualTests.cs` (T1–T7 + 2 extra WP-1-checklist tests) pass, 0 skipped — see report below.

**Blast cap:** max **11** source files + **3** test files. 1 EF migration, 0 SqlScripts. Public API: additive only (`journalEntryId` on the BN detail DTO is optional). **Hitting the cap = stop and re-spec.**
**Actual:** 10 source files (9 backend `.cs` + 1 frontend `lib/utils.ts`) + 3 test files (1 new `NonVatArAccrualTests.cs`, 2 edited pre-existing tests that encoded the OLD buggy cash-basis behavior — `InvoiceFlowTests.cs`, `McpDocumentChainTests.cs`) + 1 EF migration + 0 SqlScripts. Within cap.

### WP-2 — C6 backfill for Repttown *(depends on WP-1; same-area, keep the SAME warm worker)*

- [ ] `INonVatArBackfillService` + implementation: enumerate **only invoices with `JournalEntryId IS NULL` that still have an outstanding balance**; settled invoices are skipped entirely (§3.2.5). Preview builds the plan with zero writes.
- [ ] Credit-side routing per §3.2.5: issue date in the **current open fiscal year** → Revenue, dated at issue; issue date in a **closed** fiscal year → **กำไรสะสม**, dated in the current open period.
- [ ] Resolve the retained-earnings account from the company's **live chart of accounts** — never hardcode a code. No such account → stop with a clear error.
- [ ] VAT-company refusal (`backfill.vat_company`).
- [ ] `apply` posts via `IGlPostingService.PostManualEntryAsync`, **one transaction per invoice**, stamping `bn.JournalEntryId` last in that transaction.
- [ ] `POST /admin/nonvat-ar-backfill` with a **required** `mode` (`preview`|`apply`), super-admin-gated, **no `companyId` parameter** (target = `tenant.CompanyId`).
- [ ] Preview response per §3.2.5: per fiscal year `outstandingTotal` / `creditSide` / `invoiceCount` + the invoice list. **This output is handed to the company's accountant** — make it readable, not just machine-parseable.
- [ ] Tests T8–T11 green.
- [ ] `RbacAuthMapTests` / `RbacCartesianTests` green with `TEAS_REPO_ROOT` set (a new endpoint always disturbs these).
- [ ] **The apply run on Repttown is NOT part of this dispatch.** Ship the code; Fable runs the operation per §7's Tier-4 checklist.

**Blast cap:** max **8** source files + **2** test files. 0 migrations, 0 SqlScripts. New public endpoint: **1**.
**Dropped by the redesign:** the fiscal-year hard stop and its `backfill.fiscal_year_closed` blocker are
no longer needed — no entry can land in a closed period by construction.

### WP-3 — C1 precision *(independent of WP-1/2; shares `ExpenseClaimService.cs` with WP-4 and `PayrollDtos.cs` with WP-5 → run WP-3 → WP-4 → WP-5 in that order)*

- [ ] `JournalEntry.MarkPosted`: the per-line + header 2-dp guard, code exactly as §3.1.
- [ ] New `MoneyValidationExtensions.Satang()`.
- [ ] `CreateJournalValidator`: `.Satang()` on line Debit + Credit, **plus** `Reference` MaxLength(255), line `Description` MaxLength(500), `Lines.Count <= 200`.
- [ ] `UpdatePayrollDeductionsValidator.Amount`: `.Satang()`.
- [ ] `ExpenseClaimService.cs:100`, `PaymentVoucherService.cs:234`, `VendorInvoiceService.cs:240`: `Math.Round(…, 4, …)` → `2`.
- [ ] `.Satang()` on the expense-claim / PV / VI line `Amount` validators (create **and** update DTOs).
- [ ] **Verify by grep that no `TotalAmountThb`/FX `Math.Round(…, 4)` was changed** — paste the grep in the attempt log.
- [ ] Tests T12–T14 green.

**Blast cap:** max **10** source files + **2** test files. No migrations. Public API: error codes only.

### WP-4 — C5 expense account type + fixable categories *(after WP-3 — shares `ExpenseClaimService.cs`)*

- [ ] `EnsureExpenseAccountAsync` takes `categoryIsCapex` and enforces the §3.3 type rule.
- [ ] Both `BuildLinesAsync` branches call it; the "already validated" comment is deleted.
- [ ] `BuildLinesAsync` refuses an inactive category (`expense_claim.expense_category_inactive`).
- [ ] `PayAsync` re-guard re-validates each line's resolved account.
- [ ] `ExpenseCategoryService.CreateAsync` validates `DefaultExpenseAccountId` with the same rule.
- [ ] New `UpdateAsync` + `PUT /expense-categories/{id}` (`Sys.ExpenseCatManage`), `CategoryCode` immutable, `IsActive` settable.
- [ ] Tests T15–T17 green; RBAC map tests green.

**Blast cap:** max **7** source files + **2** test files. New public endpoint: **1** (`PUT`). No migrations.

### WP-5 — C3 payroll period + future guard *(after WP-3 — shares `PayrollDtos.cs`)*

- [ ] `PayrollRunService` injects `IPeriodCloseService`; `EnsurePostablePayDateAsync` per §3.5.
- [ ] Called first in **both** `PostAsync` and `PayAsync`.
- [ ] Sweep payroll tests for `DateTime.UtcNow`/`Today` used as a document date → `new SystemClock().TodayInBangkok()` (troubles-wiki line 776 — this WP adds the validator that makes them explode at 00:00–07:00 ICT).
- [ ] Tests T18–T20 green.

**Blast cap:** max **4** source files + **2** test files. No migrations, no API-shape change.

---

## 6. Test list

Behavioural tests exercise the **real transition** (issue → receipt → post), never a seeded end state.

| # | Test | Proves |
|---|---|---|
| **T1** | Non-VAT company: create BN → `IssueAsync` → assert exactly one posted JE `Dr 1130 = Cr 4000 = TotalAmount`, `Reference == DocNo`, `bn.JournalEntryId` set, TB balanced | I1, I3 |
| **T2** | Continue T1: post a receipt applying the full amount → cash debited by the full amount, **1130 net movement for the sale is 0.00**, 4000 total credit across both JEs is exactly the invoice amount (**not** twice) | I1, I2, I3, I4 |
| **T3** | Non-VAT company with three invoices — unpaid / part-paid / fully settled → `ar-aging` `reconciliation.difference == 0m`, `balanced == true`; the aging total equals the sum of outstandings; the customer statement's closing balance matches | I5 |
| **T4** | VAT company: issue a BN → **no** JE created, `JournalEntryId` null, AR aging identical to before | I6 |
| **T5** | SO → DO → invoice → issue; then a receipt applying to that **DO** → `rc.do_already_invoiced` (422). A receipt against the invoice succeeds | I7 |
| **T6** | Simulate the transition window: a BN with `JournalEntryId == null` (pre-fix shape) receipted after the fix → the receipt credits **Sales**, 1130 is untouched, revenue recognised exactly once | I8 |
| **T7** | End-to-end totals: for one sale driven issue→receipt, `Σ credits to 4000 == invoice total` exactly | I1 |
| **T8** | Backfill `apply` on a fixture with one unpaid + one settled historical BN (built via `PostManualEntryAsync` at historical dates) → correcting JEs at the true event dates; each `Dr == Cr`; the pre-existing JEs are unchanged (compare `Version`/row hashes) | I9, I11 |
| **T9** | Same fixture: 4000's total credit across all time is unchanged **for the settled sale**, and increases by exactly the unpaid amount; 1110/1120 balances bit-identical | I9, I10 |
| **T10** | Run `apply` twice → the second run posts 0 entries and reports `alreadyDone == n`. Then a partial-run simulation (abort after invoice 1) → resume completes without duplicating | I12 |
| **T11** | `mode=preview` → row counts of `gl.journal_entries`, `gl.journal_lines`, and `billing_notes WHERE journal_entry_id IS NOT NULL` unchanged; the plan is non-empty | I13 |
| **T12** | **RED-first**: `POST /journals` draft with `100.005` lines → **400** (today: 201). Then `POST /journals/{id}/post` on a pre-seeded 4-dp draft → **422 `je.precision`** (today: 200 Posted). Both must fail before the fix | I14 |
| **T13** | Draft with `33.3333 / 33.3333 / 33.3334` vs `Cr 100.00` → rejected; **assert no JE row was created and no JV number was consumed** — not rounded, not plugged | I14, I15 |
| **T14** | Payroll deduction `100.129` → **400**; expense-claim line `100.005` → **400**; PV line `100.005` → **400**. Then a valid `100.13` deduction posts and the payslip's printed net equals the 2170 credit exactly | I14, I16 |
| **T15** | Expense claim line naming account 1120 (bank) → rejected; same for 2110 / 4100 / 3300 / 1170. Both via **explicit `expenseAccountId`** and via a **category whose default is that account** — the category branch is the one that had no guard at all | I17, I18 |
| **T16** | A claim on the seeded **CAPEX** category (default 1610, `AccountType.Asset`) still creates and pays successfully | I19 |
| **T17** | `POST /expense-categories` with `defaultExpenseAccountId` = 1170 → rejected. `PUT /expense-categories/{id}` repoints a bad default and deactivates it; a new claim on the deactivated category → rejected | I20 |
| **T18** | Close a period; payroll `post` for a run dated in it → **422 `payroll.period_closed`**, message contains the `reopen` route; **no JE created**. Then `pay` on an already-posted run into a since-closed period → same refusal | I21 |
| **T19** | (a) Run whose `PayDate` falls **after its own period end** → `post` → **422 `payroll.pay_date_outside_period`**, no JE. (b) A `209912` run → refused (its period is not open). (c) **REGRESSION — the case Ham's decision protects:** a run for the current period with `PayDate` = a still-future day *inside* that period (e.g. post on the 28th, pay on the 30th) **posts normally**. | I22 |
| **T20** | Current-month run posts and pays normally. Then: close the previous month, create a run for it, `post` → refused; **reopen via `POST /periods/{y}/{m}/reopen`** → `post` succeeds → re-close. Proves the escape hatch the error message names actually works | I23 |

Not automatable, must be reported honestly rather than skipped: the **Repttown preview numbers** (§7 Tier-4)
— they can only be produced against prod data.

---

## 7. Verification gates

**Worker (Tier 1)** — after each WP:

1. `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` → `0 Error(s)`.
   (Serialized per troubles-wiki line 795. No test host running — line 374.)
2. Targeted tests for the WP, e.g.
   `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~<Area>"` → all green.
   Set `TEAS_TEST_PG` **in the same shell call** (memory: it dies between calls) and
   `TEAS_REPO_ROOT` for WP-2/WP-4 (RBAC tests).
   **Report the skip count** and compare it to baseline — a skipped suite fakes green.
3. WP-3 only: paste the grep proving no FX `TotalAmountThb` rounding was changed.
4. WP-1 only: paste `dotnet ef migrations list | tail -3`.
5. FE typecheck **only if a frontend file was touched** (the sweep says none should be); otherwise state N/A.

**Fable (Tier 1 exception + Tier 3)** — the worker reports code-complete; **Fable runs the full suite**
in one backgrounded call and reads the log. Workers never babysit the 13-minute run. **Only one worker may
run the integration suite at a time** — the shared test DB races.

**Tier 2** — money/compliance spanning multiple WPs after two prior REJECT-prone releases:
use the **escalated** multi-agent review (`.claude/workflows/tier2-review.js`) with lenses
`money-invariant / spec-compliance / regression / security`. WP-2 additionally gets a dedicated
prod-data-safety lens.

**Acceptance-tester pass** (blind, spec-only) between implement and Tier-2 — this release is exactly what
that gate exists for.

**Deploy** — prod DB backup **before** the release (WP-1 carries a migration; startup applies it).
Deploy probes, **row counts not exit codes**:
```
SELECT count(*) FROM sales.billing_notes;                                       -- unchanged
SELECT count(*) FROM sales.billing_notes WHERE journal_entry_id IS NOT NULL;    -- 0 pre-backfill
SELECT count(*) FROM gl.journal_entries;                                        -- unchanged by deploy
```
Plus at least one probe **through the public domain** (`https://teas.kazaki-rio.com`), not localhost.

**Tier 4 — live acceptance (mandatory).** In order:
1. On a **freshly reseeded** non-VAT company: issue an invoice → confirm `Dr 1130 / Cr 4000` on screen →
   `ar-aging` shows the row and `balanced: true` → receipt it → AR returns to 0.00, revenue **unchanged**,
   `balanced: true`.
2. Precision: `POST /journals` draft with `100.005` → 400. Payroll deduction `100.129` → 400. Expense
   claim to account 1120 → refused.
3. Payroll: post into a closed month → refusal names the reopen route → reopen → post → re-close.
4. **Repttown, `mode=preview` only.** Capture `totals`, `byFiscalYear`, `blockers`. **Report to Ham; do not
   apply.** The apply run is a separate authorised operation with its own backup, and the resume protocol
   in §3.2.5 applies if it dies mid-run.

---

## 8. Out of scope — and the decisions that belong to Ham

**Out of scope (scope creep here is a reviewable defect):**
- C2, C4 and every other R2/R3/R4 finding.
- **H7** (`asOf` ignored by AR/AP aging) — R4. The new BN rows deliberately follow the current, broken
  convention so the report stays internally consistent (§2.1).
- Cleaning co5's and co7's existing sub-satang ledgers. **They cannot be cleaned by a reversing JV** — that
  JV would itself need sub-satang amounts, which the new seam guard rejects. The wipe+reseed (plan decision
  4) is the answer. ⚠️ **Consequence to state loudly:** until reseeded, **co5 and co7 can no longer be
  year-closed** — `PostClosingEntryAsync` sums their 3-dp balances and now hits `je.precision`. This is
  correct behaviour on corrupt data, and it is one more reason the reseed is not optional.
- PV/VI expense-account **type** validation (deferred with reasoning, §2.2) and their FE pickers.
- Sales-line `Math.Round(qty * price, 4)` (§2.3).
- `payroll.duplicate_period` — one run per period per company, permanently. A pre-existing limitation, not
  created by C3. → troubles-wiki entry.
- `MarkSettledAsync` letting an admin mark an accrued invoice settled without payment (§2.1).
- H10's year-close deadlock, H11's unauditable reopen — R3.
- Any `SqlScripts/*.sql`. If one seems necessary: **stop and re-spec.**

**Product decisions — ALL ANSWERED by Ham, 2026-07-31. Implement these; do not re-open them.**

1. **[WP-2, tax] Prior-year revenue movement — RESOLVED by the §3.2.5 redesign + research.**
   Corrections now land in the **current open period against กำไรสะสม**, so no closed year's ledger is
   restated and no filed statement changes. The **tax** side is handled separately and by humans: the
   preview's per-year figures go to the company's CPA, who files **ภ.ง.ด.50 เพิ่มเติม** for the affected
   years. Voluntary correction before an RD summons waives เบี้ยปรับ (ท.ป. 81/2542); เงินเพิ่ม 1.5%/month
   (ม.27) is statutory and cannot be waived. Full findings + citations:
   `specs/research-thai-prior-period-correction.md`. **Engineering ships the preview numbers; it does not
   file anything.**

2. **[WP-2] Closed fiscal years — RESOLVED, question no longer arises.** By construction no correcting
   entry is dated inside a closed period (§3.2.5), so the year-end closing JE stays true and H10's
   deadlock is never touched. The `backfill.fiscal_year_closed` blocker is dropped.

3. **[WP-5] Payroll future-date bound — DECIDED: bound to the run's own period, NOT to today.**
   `PayDate <= last day of the payroll run's period` (Bangkok). Rationale (Ham): posting on the 28th for a
   pay date of the 30th is normal business practice, so `PayDate <= today` would block real work. This
   still kills the unbounded case the swarm found (period `209912` → JE dated 2099-12-31), because the
   *period itself* must be open, and the guard now caps the pay date inside it.

4. **[WP-1] Receipt against an already-invoiced delivery order — DECIDED: refuse, and say where to go.**
   Refuse with `rc.do_already_invoiced`, and the message must name the existing invoice
   (e.g. "ใบส่งของนี้ออกใบแจ้งหนี้ IV-xxxx แล้ว — รับชำระที่ใบแจ้งหนี้นั้น"). Allowing it would
   double-count revenue, so refusing was the only safe option; the decision was only about the wording.

5. **[R3, not this release] PV/VI account-type validation — the allowed set is now DEFINED.**
   Still deferred to R3, but no longer an open question. **Allowed:** Expense, Asset (e.g. buying
   equipment), Liability (e.g. repaying a director loan). **Forbidden:** Revenue (4xxx), input VAT (1170)
   on a non-VAT company, and a cash/bank account on the **debit** side (that is a transfer, a different
   document). Record this in `troubles-wiki.md` at diff review so it is not rediscovered.

## 9. Blast-radius cap (release total)

**Max 40 source files + 11 test files across all five work packages.** Per-WP caps in §5 are the binding
ones — hitting **any** per-WP cap means stop and re-spec, never a silent overrun. Anyone commissioning
post-review remediation must **edit these numbers in the same edit** that adds the findings.

- EF migrations: **1** (WP-1). SqlScripts: **0**.
- New public endpoints: **2** (`POST /admin/nonvat-ar-backfill`, `PUT /expense-categories/{id}`).
- Breaking API changes: **none**. New error codes only: `je.precision`, `gl.bn_missing`,
  `gl.bn_vat_unexpected`, `billing_note.cannot_cancel_posted`, `rc.do_already_invoiced`,
  `expense_claim.expense_category_inactive`, `expense_category.default_account_invalid`,
  `expense_category.not_found`, `payroll.period_closed`, `payroll.pay_date_outside_period`,
  `backfill.vat_company`, `backfill.fiscal_year_closed`.

**Stop-and-re-spec triggers:**
- A `SqlScripts/*.sql` file appears necessary.
- A second EF migration appears necessary.
- The AR reconciliation cannot be made to tie (`difference != 0`) — that means a consumer in §2.1 was
  missed; report it, do not paper over it with a tolerance.
- The `MarkPosted` guard turns an existing green test red for a *legitimate* computed amount — that is a
  real 4-dp producer the sweep missed. Report it; do not widen the tolerance and do not round at the seam.
- WP-2's preview finds blockers, or a `byFiscalYear` delta on a filed year. Stop, report to Fable.

---

## 10. Sequencing and parallel safety

Shared files force the order:

```
WP-1  (sales/AR)      →  WP-2  (backfill, same area, keep the warm worker)
WP-3  (precision)     →  WP-4  (shares ExpenseClaimService.cs)
                      →  WP-5  (shares PayrollDtos.cs)
```

**Recommended: sequential, one warm worker,** order `WP-1 → WP-2 → WP-3 → WP-4 → WP-5`. The area shift at
WP-3 is the only place a fresh spawn is justified.

**Parallel option** (Track A = WP-1+WP-2, Track B = WP-3+WP-4+WP-5): the file sets **are** disjoint —
Track A touches `BillingNoteService`, `GlPostingService`, `ReceiptService`, `SubledgerReportService`,
the new backfill service/endpoint; Track B touches `JournalEntry`, the validators, `ExpenseClaimService`,
`PaymentVoucherService`, `VendorInvoiceService`, `PayrollRunService`, `MasterDataServices`. **Requirements
if run in parallel:** a git worktree each, a **per-worker test DB**, and an explicit hold on the second
worker's test run until the first finishes — the integration DB is shared and a concurrent run has crashed
the test host before (2026-07-08). The Tier-3 gate runner counts as a test-running worker.

**Release split recommendation for Fable:** ship WP-1/3/4/5 + WP-2's *code* as R1. Run WP-2's `apply`
against Repttown as a **separate authorised prod operation** after R1 is deployed and its Tier-4 leg is
green. Rationale: WP-2's correctness detector (`JournalEntryId`) only becomes meaningful once WP-1 is live,
and an irreversible prod-data write should not be gated by a code-release checklist.

---

## Attempt log

- 2026-07-31 opus-designer: spec written. Design decisions and the open questions for Fable are in
  §3 and §8. Notable corrections to the brief, all verified in code: C1 is **four** paths (PV included,
  `A4-nonvat-chains-co7.md` §K1); the true seam is `JournalEntry.MarkPosted`, not
  `GlPostingService.BuildAndPostAsync` (two posting paths bypass the latter); a naive C5 "expense-only"
  rule would brick the seeded CAPEX category on **every** company; C3's guard does **not** brick payroll
  once O14's reopen is accounted for, but it does need a second error code for the future-period case.
  C6's backfill is designed as a preview/apply endpoint rather than a migration, with the fiscal-year hard
  stop and the resume protocol.
- 2026-07-31 opus-designer (post-write verification pass): two facts corrected in place after re-reading
  the entities — (a) the DO→Invoice link is `BillingNote.DeliveryOrderId`, **not** a `BillingNoteId`
  column on `DeliveryOrder` (the DTO field is a computed lookup), so `rc.do_already_invoiced` needs a
  batched BillingNotes read, now spelled out in §3.2.3; (b) `sales.billing_notes` has **no** immutability
  trigger, so the `journal_entry_id` write is unblocked — checked, not assumed.
- **Deferred-with-reasoning items that R3/R4 must inherit** (do not lose these): PV/VI expense-account
  **type** validation (§2.2, product decision), the sales-line `Math.Round(qty*price, 4)` producers
  (§2.3), H7's `asOf` gap now spanning BN rows too (§2.1), `payroll.duplicate_period`'s one-run-per-period
  permanence, and the FE expense-account picker. Each needs a `troubles-wiki.md` entry at diff review.
- **2026-08-11 sonnet-implementer: WP-1 implemented, all checklist items `[x]`.** 10 source files (9
  backend + `frontend/lib/utils.ts`) + 3 test files (1 new, 2 edited) + 1 EF migration, 0 SqlScripts — within
  the 11/3 cap. RED confirmed first: with the implementation git-stashed, `NonVatArAccrualTests.cs` fails to
  even COMPILE against the pre-fix code (`JournalEntryId`/`PostBillingNoteAsync`/`cannot_cancel_posted` don't
  exist) — the strongest possible proof the fix doesn't exist yet. After un-stashing: `dotnet build` 0/0,
  all 9 new tests green (T1–T7 + closed-period-consumes-no-number + cancel-refused).
  **Two pre-existing tests found asserting the OLD buggy cash-basis behaviour** (they predate C6's fix and
  literally pinned the bug): `InvoiceFlowTests.NonVat_receipt_applied_to_invoice_recognizes_revenue_to_sales`
  and `McpDocumentChainTests.NonVat_sales_chain_settles_billing_note_pins_D3b_je_and_blocks_tax_invoice` —
  both asserted a BN-applied receipt credits Sales for an issued Invoice. Both rewritten to assert the
  correct behaviour (receipt settles AR; the Issue itself now posts the accrual JE) rather than deleted or
  loosened — this consumed 2 of the 3 test-file budget, noted here so a reviewer doesn't mistake it for
  scope creep. **EnsureOpenAsync placement:** before the number allocation in `IssueAsync` (spec's stated
  preference, achieved with zero restructuring — `taxCfg` fetched once, gate before
  `AllocateAndSaveAsync`, `PostBillingNoteAsync` call stays after since it needs `DocNo`).
  **EF config deviation:** no explicit `b.Property(x => x.JournalEntryId)` chain added — it would be a
  no-op (snake_case convention + no FK/index needed) — documented inline instead; `PayrollRun.JournalId`
  is the exact in-repo precedent for "no explicit config at all" (spec's own citation), though
  `ExpenseClaim.JournalEntryId` is a *different* precedent that DOES add an FK — noted for the record since
  the spec's parenthetical undersold that the precedent is mixed.
  Full test evidence (all green): `NonVatArAccrualTests.cs` 9/9 · combined
  `Sales`+`Reports`+`Mcp` namespaces 278/278 (0 skipped) · `RbacAuthMapTests`+`RbacCartesianTests` 4/4 ·
  one flake in `Sprint87ForeignVendorTests` (random-id collision, troubles-wiki-documented pattern,
  standalone rerun passed — confirmed unrelated). FE: `tsc --noEmit` clean; `next build` blocked by a
  pre-existing sandbox network restriction on Google Fonts (documented in troubles-wiki, unrelated to this
  diff — the only FE change is a 3-line docType-map addition in `lib/utils.ts`).
  **WP-2 is NOT implemented** — per dispatch, only WP-1's checklist was in scope.
- **2026-08-11 (post-review) sonnet-implementer: added the cross-period AR reconciliation test**
  requested at diff review (invoice issued month N, receipt settling month N+1, `ar-aging` reconciled
  `asOf` = end-of-N must tie out). Added to `NonVatArAccrualTests.cs` (stayed inside the 3-test-file
  budget). Discovered mid-write: a POSTED `JournalEntry.doc_date` is a DB-trigger-guarded critical field
  (`020_journal_immutability.sql fn_enforce_je_immutability`, UPDATE-only — verified by reading the
  trigger, not assumed), so an already-posted JE's date can never be shifted after the fact, and
  `BillingNoteService`/`ReceiptService` both pin `DocDate` to server-today regardless of request input
  (existing footgun). Worked around by INSERTing the invoice directly with `DocDate` = last day of the
  previous month (mirrors the T6 pattern; `sales.billing_notes` has no immutability trigger at all) and
  calling the real `GlPostingService.PostBillingNoteAsync` against that already-dated row — an INSERT-time
  post, so the JE trigger (UPDATE-only) never engages; the receipt then posts through the ordinary,
  unmodified `ReceiptService` flow, dated today (naturally the next calendar month). **Per the coordinator's
  explicit instruction, no test was RUN** (their full-suite run held `teas_test`) — verified compile-only,
  built to an isolated output directory (`-o` to a scratch path) to avoid the coordinator's locked
  `bin/Accounting.Api.Tests.dll` (troubles-wiki "locked by testhost") without touching their process:
  0 Warning(s), 0 Error(s). Correctness of the assertions (`Reconciliation.Difference == 0`,
  `ControlAccountBalance == SubLedgerTotal == total`) reasoned from static re-reading of
  `ControlAccountBalanceAsync`'s `j.DocDate <= asOf` filter and `ArReconciliationAsync`'s
  `m.DocDate <= asOf` filter over `ArMovementsAsync`'s per-movement dates — both already correctly
  asOf-scoped (unlike `ArAgingAsync`'s own row-level table, which has the deliberately-deferred H7 gap) —
  not run-verified. **The coordinator should treat this test's first run as the real gate**, not this report.

---

## Fable spec review — 2026-07-31 — **APPROVED to implement**

Reviewed personally per the never-delegated gate, with the money-invariant section read in full (§4) —
that section is never skipped, whatever the context pressure.

**Money formula independently re-derived (I9), all three cases:**
- Settled sale ฿1,000 — accrual at issue `+1,000` revenue, timing reversal at receipt `−1,000`
  → **revenue delta 0** (unchanged, only its date moved), AR nets to **0**, cash untouched. ✓
- Unpaid invoice ฿1,000 — accrual only → revenue `+1,000`, AR `+1,000`, outstanding `1,000`. ✓
- Part-paid (1,000 invoiced, 400 received) — `+1,000 − 400` → revenue `+600`, AR `+600`, outstanding `600`. ✓

`netRevenueIncrease == netArIncrease == Σ outstanding` holds in every case. **No sign flip.** The
`JournalEntryId IS NULL` detector is sound: after WP-1 ships, null unambiguously means "issued pre-fix,
so its receipts credited Sales", and it doubles as the idempotency key. I8 covers the transition case I
was most worried about (invoice issued pre-deploy, receipted post-deploy).

**Spot-verified in code, not taken on trust:**
- `CAPEX` seeded category → account `1610` → `AccountType.Asset`
  (MasterDataServices.cs:489 + :446), seeded by `CompanyService.CreateAsync` on **every** company
  including Repttown. The designer's warning is correct: a naive "expense accounts only" rule for C5
  would break capex expense claims everywhere. **This catch alone justified the Opus design tier.**

**Three design calls I agree with and am recording as binding:**
1. The C1 guard belongs at `JournalEntry.MarkPosted`, not `GlPostingService` — two paths bypass the GL
   service, including `JournalService.PostAsync:110`, which is the exact path the swarm exploited.
2. **Reject, never round**, at the seam. C4's live `33.3333×3` vs `Cr 100.00` proves rounding there would
   break `ΣDr==ΣCr` and force an invented balancing satang.
3. The backfill is a preview/apply endpoint, not an EF migration — correcting entries land at their true
   event dates, sidestepping the reopen dance and H10's year-close deadlock.

**Corrections to my own plan, adopted:** C1 is **four** paths, not three — the Payment Voucher path also
posts sub-satang (A4 §K1). C3's guard does **not** brick payroll (the designer traced it: `IsOpenAsync`
returns open for the current Bangkok month even with no period row, so only back-months are affected and
O14 is the way out) — my PLAN warning was over-cautious, but the two-error-code requirement it surfaced is real.

**Consequence to flag loudly at release time:** once C1 ships, co5 and co7 **can no longer be year-closed**
— `PostClosingEntryAsync` sums their existing 3-decimal balances and hits `je.precision`. Correct behaviour
on corrupt data, and unfixable by a reversing JV (that JV would itself need sub-satang). **The wipe+reseed
is therefore mandatory, not housekeeping.**

**Not started — implementation is deliberately deferred.** 7-day quota is at 83% against Ham's 85% hard
stop (no Codex fallback, 2026-07-31). Starting a ~40-file release now would strand it mid-flight. Resume
order is in `PLAN-fix-breakit-v1271.md`.

---

## WP-2 redesign — folded into §3.2.5, §4 and §5 on 2026-07-31

The standalone redesign note that used to live here has been merged into the spec body, so §3.2.5 is now
the single source of truth for the backfill. Rationale and citations: `specs/research-thai-prior-period-correction.md`.


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
| `QuotationChainServices.cs:23/25`, `TaxInvoiceService.cs:575/577` — `Math.Round(qty * price, 4)` / discount | line gross on sales docs | **DONE — deferral reversed 2026-08-12 (Fable).** The seam guard turns "no evidence of a sub-satang JE" into "the invoice cannot post at all": a fractional quantity against an odd unit price (1.5 kg @ ฿33.33 — ordinary business) produces a 3dp gross that flows unrounded into `net`/`LineAmount`/`TotalAmount` (no rounding step before the JE) and now hits `je.precision`. `4` → `2` at all four sites. Evidence + attempt-log entry below. |
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

> ## ⚠️ AMENDED 2026-08-12 — the original rule below was DEFECTIVE and shipped the bug it was meant to close.
> Opus Tier-2 caught it; Fable confirmed in code. **This amendment overrides anything below that conflicts.**
>
> **What was wrong.** The rule read `Expense || (categoryIsCapex && Asset)`. But `AccountType` has only five
> members — Asset, Liability, Equity, Revenue, Expense — with **no cash / fixed-asset distinction**. Bank
> `1120`, AR `1130` and Input VAT `1170` are all `AccountType.Asset` (MasterDataServices.cs:421-423), and the
> `CAPEX` category is seeded on **every** company. So an ordinary employee with only `expense.claim.create`
> could file a claim against the seeded CAPEX category with `expenseAccountId = 1120`, have it pass both the
> line guard and the pay re-guard, and post **Dr Bank / Cr Bank** — balanced, green, status **Paid**, and the
> employee never reimbursed. That is verbatim the P0 this work package exists to close.
>
> **Why it got through.** §3.3 contradicted its own invariants. **I17** says "an expense account, or *the
> fixed-asset account of a capex category*" — singular, the category's own. **I18** says "the debit side is
> never a cash/bank account". The snippet said neither. The implementer followed §3.3 exactly and was right
> to. This is the 2026-07-25 lesson repeating: *when a money spec's stated rule and its invariant disagree,
> the invariant is the specification* — and a spec review that checks the money formula but never tests the
> rule against the invariants will miss it, which is exactly what happened here.
>
> **THE RULE (binding):**
> ```
> allowed = accountType == Expense
>        || (categoryIsCapex && accountId == category.DefaultExpenseAccountId)
> ```
> Asset is permitted **only when it is the capex category's own default account** — an allowlist of exactly
> one account per category, which structurally cannot admit bank, cash, AR or input VAT. Never Liability,
> Equity or Revenue.
>
> **Why an allowlist and not "Asset except bank/cash/VAT".** A denylist has to enumerate every
> non-fixed-asset account correctly, forever, on every tenant's chart. This repo already shipped a denylist
> that was bypassed by a zero-width space (the MCP `.post` guard). One-account allowlist has no such surface.
>
> **Accepted trade-off:** a capex claim can no longer override to a *different* fixed-asset account (e.g.
> 1620 Vehicles under a CAPEX category). The remedy is a second capex category pointing at 1620, which is
> better modelling anyway — a category maps to an account. **If Ham wants multi-account capex categories,
> that is a product decision and a later change; it does not go back to permitting bare `Asset`.**
>
> **Also required by this amendment (fixes Opus HIGH-2, a new dead-end class):**
> - Re-validate lines in **`SubmitAsync` and `ApproveAsync`**, not only at pay. Without this, a legacy draft
>   holding a bad account sails through submit and approve after deploy and only hits the wall at pay,
>   creating NEW stuck claims post-deploy.
> - Allow **cancel from `Approved`** so a claim whose account became invalid has an exit. Today Approved has
>   no route out at all: pay throws, cancel is Draft/Rejected-only, reject is Submitted-only, edit is
>   Draft/Rejected-only, and no unapprove exists. Deactivating any COA account currently strands every
>   Approved claim referencing it.
> - **Pre-deploy audit** (fold into WP-6): count `expense_claim_lines` whose resolved account would now be
>   refused, per company, before this ships.

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

> ## ⚠️ AMENDED 2026-08-12 (2nd time) — the period-END ceiling is WRONG. Remove it.
> **This overrides §8 #3 and anything below that conflicts.** Fable's error, not the implementer's.
>
> **What the ceiling cost.** `PayDate <= last day of the run's own period` makes a December-period run
> paid on 5 January **impossible to produce through the service**. Paying in arrears across a period
> boundary is ordinary Thai payroll (ม.52/ม.59), and this system was explicitly built for it — the
> pre-existing test `Pnd1_filings_follow_payment_date_not_period` exists precisely because **ภ.ง.ด.1
> follows the PAYMENT date, not the period**. The tell: implementing the ceiling forced that test to be
> rewritten to seed posted state directly, **bypassing the service**. When a guard forces a pre-existing
> test to bypass the very path it tests, the guard has broken a real capability.
>
> **What the ceiling bought: nothing.** It does not catch the swarm's `209912` / 2099-12-31 case — there
> `periodEnd == PayDate`, so the ceiling passes and **`IsOpenAsync` is what refuses it** (a never-opened
> future month is CLOSED). The ceiling is pure cost.
>
> **How this got in.** Ham approved "bound to the run's own period end, not `today`" on my recommendation,
> to preserve **pre-payday posting** (post on the 28th, pay on the 30th). That goal is right and stays.
> Neither of us considered paying in the *following* month, and I asserted the ceiling "still kills the
> 2099 case" — true only because `IsOpenAsync` does it. The recommendation was wrong on that axis.
>
> **THE RULE (binding):**
> ```
> EnsureOpenAsync(run.PayDate)        // the real guard — closed months AND never-opened future months
> PayDate >= first day of the run's period   // sanity floor: cannot pay a period before it begins
> // NO ceiling tied to the period end.
> ```
> - **I21** (cannot move a closed month) — satisfied by `EnsureOpenAsync`.
> - **I22** — restate as: *a run cannot post into a period that is not open*, which is what actually kills
>   `209912`. Drop the "pay date stays inside its own period" wording.
> - **I23** (payroll still works) — now genuinely true: pre-payday posting works, **and arrears pay works**.
>
> **Consequences of removing the ceiling:** `payroll.pay_date_outside_period` is no longer needed for the
> ceiling (keep it only for the floor violation, or drop it and use one code). The "no escape hatch"
> problem the implementer flagged disappears with it — there is no longer a shape that can never post.
> `Pnd1_filings_follow_payment_date_not_period` must go back to driving the **real service**, not seeded
> state; if it still needs a workaround after this change, something else is wrong — say so.
> Two error codes are still required for the open-period check itself (a closed month names the O14
> reopen route; a never-opened future month must not, since reopen answers `period.not_closed` there).

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

- [x] `INonVatArBackfillService` + implementation: enumerate **only invoices with `JournalEntryId IS NULL` that still have an outstanding balance**; settled invoices are skipped entirely (§3.2.5). Preview builds the plan with zero writes. Evidence: T11.
- [x] Credit-side routing per §3.2.5: issue date in the **current open fiscal year** → Revenue, dated at issue (or today if the issue month is independently closed); issue date in a **prior (closed)** fiscal year → **กำไรสะสม**, dated in the current open period. Evidence: T8. **Updated per Ham's ruling (2026-08-11):** "closed" = a prior fiscal year, a pure `fy < currentFy` comparison — the `FiscalYearClose`-row approach this row originally described was replaced (it under-reported closure for a company, like Repttown, that never ran the in-app year-close). Plus a second guard: a current-FY invoice whose own issue MONTH is independently closed (ordinary monthly close) still credits Revenue but dates at today, never into that closed month (I13b, now enforced at the month level too).
- [x] Resolve the retained-earnings account from the company's **live chart of accounts** — never hardcode a code. No such account → stop with a clear error. Evidence: `Missing_retained_earnings_account_stops_with_clear_error_and_posts_nothing`. Added `GlAccountsOptions.RetainedEarningsAccount` (default `"3300"`, matching the code `YearCloseService` already hardcodes) — resolved the same way as every other GL role in that options object.
- [x] VAT-company refusal (`backfill.vat_company`). Evidence: `Vat_company_is_refused_on_both_preview_and_apply`.
- [x] `apply` posts via `IGlPostingService.PostManualEntryAsync`, **one transaction per invoice**, stamping `bn.JournalEntryId` last in that transaction. Evidence: T8/T9/T10.
- [x] `POST /admin/nonvat-ar-backfill` with a **required** `mode` (`preview`|`apply`), super-admin-gated, **no `companyId` parameter** (target = `tenant.CompanyId`). Gate mirrors `InstanceSetupEndpoints` exactly: `.RequireAuthorization()` (authn only) + in-handler `IsSuperAdmin` claim check — a permission policy would need an ungrantable-by-design permission for a one-time prod-data operation.
- [x] Preview response per §3.2.5: per fiscal year `outstandingTotal` / `creditSide` / `invoiceCount` + the invoice list. **This output is handed to the company's accountant** — make it readable, not just machine-parseable. Evidence: `NonVatArBackfillResult.ByFiscalYear`.
- [x] Tests T8–T11 green. Evidence: `NonVatArBackfillTests.cs` 6/6 (T8, T9, T10, T11, VAT refusal, missing-account error), 0 skipped.
- [x] `RbacAuthMapTests` / `RbacCartesianTests` green with `TEAS_REPO_ROOT` set (a new endpoint always disturbs these). Allowlist entries added (`ExpectedAuthnOnly` + `HandlerGatedAuthnOnly`, claims-only gate). Evidence: 4/4 passed, 4m22s.
- [~] **The apply run on Repttown is NOT part of this dispatch.** Ship the code; Fable runs the operation per §7's Tier-4 checklist. (Not run — code-only, as instructed.) — blocked on server migration; tracked in MIGRATION-CUTOVER-CHECKLIST.md (triage 2026-08-19)

**Blast cap:** max **8** source files + **2** test files. 0 migrations, 0 SqlScripts. New public endpoint: **1**.
**Dropped by the redesign:** the fiscal-year hard stop and its `backfill.fiscal_year_closed` blocker are
no longer needed — no entry can land in a closed period by construction.

### WP-3 — C1 precision *(independent of WP-1/2; shares `ExpenseClaimService.cs` with WP-4 and `PayrollDtos.cs` with WP-5 → run WP-3 → WP-4 → WP-5 in that order)*

- [x] `JournalEntry.MarkPosted`: the per-line + header 2-dp guard, code exactly as §3.1. Evidence: T12b, T13.
- [x] New `MoneyValidationExtensions.Satang()`. Evidence: builds, used by all validators below.
- [x] `CreateJournalValidator`: `.Satang()` on line Debit + Credit, **plus** `Reference` MaxLength(255), line `Description` MaxLength(500), `Lines.Count <= 200`. Evidence: T12a.
- [x] `UpdatePayrollDeductionsValidator.Amount`: `.Satang()`. Evidence: T14.
- [x] `ExpenseClaimService.cs:100`, `PaymentVoucherService.cs:234`, `VendorInvoiceService.cs:240`: `Math.Round(…, 4, …)` → `2`. Evidence: grep below shows none of the three sites remain at 4dp.
- [x] `.Satang()` on the expense-claim / PV / VI line `Amount` validators (create **and** update DTOs). `ExpenseClaimLineInputValidator` is shared by Create+Update (explicit both); `CreateVendorInvoiceValidator` is shared by `CreateDraftAsync`+`UpdateDraftAsync` (same DTO, no separate Update validator exists); PV has no Update path at all (Create only) — confirmed by reading `IPaymentVoucherService`/`IVendorInvoiceService`. Evidence: T14.
- [x] **Verify by grep that no `TotalAmountThb`/FX `Math.Round(…, 4)` was changed** — paste the grep in the attempt log.
- [x] Tests T12–T14 green. Evidence: RED→GREEN below.
- [x] **Scope addition, Fable-authorised 2026-08-12** (deferral in §2.3 reversed): `TaxInvoiceService.cs:575/577` and `QuotationChainServices.cs:23/25` `Math.Round(…,4)` → `2` — the seam guard turned "no evidence of a sub-satang JE" into "a fractional-quantity sale can no longer be invoiced at all." Evidence: `TaxInvoiceLinePrecisionTests.cs`, RED→GREEN below.

**Blast cap:** max **10** source files + **2** test files → **raised to 12 + 3 by Fable, 2026-08-12** for the sales-line rounding addition. No migrations. Public API: error codes only.
**Actual:** 12 source files (11 edited + 1 new `MoneyValidationExtensions.cs`) + 3 test files (2 edited: `ManualJournalTests.cs`, `PayrollRunServiceTests.cs`; 1 new: `TaxInvoiceLinePrecisionTests.cs`). Exactly at the raised cap. 0 migrations.

### WP-4 — C5 expense account type + fixable categories *(after WP-3 — shares `ExpenseClaimService.cs`)*

- [x] `EnsureExpenseAccountAsync` takes `categoryIsCapex` and enforces the §3.3 type rule. Evidence: `ExpenseAccountRule.IsAllowedType` (new shared helper) called from the guard; T15 RED→GREEN.
- [x] Both `BuildLinesAsync` branches call it; the "already validated" comment is deleted. Evidence: diff shows both the override and category-default branches now route through `EnsureExpenseAccountAsync(..., category.IsCapex, ct)`; stale comment replaced.
- [x] `BuildLinesAsync` refuses an inactive category (`expense_claim.expense_category_inactive`). Evidence: `Claim_on_an_inactive_category_is_rejected` RED→GREEN.
- [x] `PayAsync` re-guard re-validates each line's resolved account. Evidence: `Pay_re_guard_rejects_a_line_whose_account_was_deactivated_after_drafting` RED→GREEN.
- [x] `ExpenseCategoryService.CreateAsync` validates `DefaultExpenseAccountId` with the same rule (`EnsureDefaultAccountAsync`, same exists/active/header/type bundle, error `expense_category.default_account_invalid`). Evidence: `Create_with_a_wrong_type_default_account_is_rejected` RED→GREEN.
- [x] New `UpdateAsync` + `PUT /expense-categories/{id}` (`Sys.ExpenseCatManage`), `CategoryCode` immutable (absent from `UpdateExpenseCategoryRequest`), `IsActive` settable. Evidence: `Update_can_repoint_a_poisoned_default_account_to_a_valid_one`, `Update_repointing_to_ANOTHER_wrong_type_account_is_still_rejected`, `Update_can_deactivate_a_category_and_a_new_claim_against_it_is_rejected` all green; RBAC-generated doc shows `PUT /expense-categories/{id:int} | Perm | sys.expense_category.manage`.
- [x] Tests T15–T17 green; RBAC map tests green. Evidence below.

**Blast cap:** max **7** source files + **2** test files. New public endpoint: **1** (`PUT`). No migrations.
**Actual:** 5 source files (4 edited: `ExpenseClaimService.cs`, `ReferenceDtos.cs`, `MasterDataServices.cs`,
`MasterEndpoints.cs`; 1 new: `ExpenseAccountRule.cs` — shared type-rule helper so `ExpenseClaimService` and
`ExpenseCategoryService` can never drift apart) + 2 test files (1 edited: `ExpenseClaimServiceTests.cs`;
1 new: `ExpenseCategoryServiceTests.cs`). Within cap. 0 migrations. 1 new endpoint (`PUT`).

### WP-5 — C3 payroll period + future guard *(after WP-3 — shares `PayrollDtos.cs`)*

- [x] `PayrollRunService` injects `IPeriodCloseService`; `EnsurePostablePayDateAsync` per §3.5 (adapted to parse `PeriodYearMonth` string per the spec's own implementer note — the entity has no separate `PeriodYear`/`PeriodMonth` fields). Evidence: builds clean; T18/T19 RED→GREEN below.
- [x] Called first in **both** `PostAsync` (after the `Approved`-status check, before the transaction/number allocation — no invoice number consumed on refusal) and `PayAsync` (after the `Posted`/`PaidAt` checks, before bank resolution/transaction).
- [x] Swept payroll tests for `DateTime.UtcNow`/`Today` used as a document date. **Found: zero.** `PayrollRunServiceTests.cs` already computes every `PayDate`/period from `FreshYearAsync`/`RandYear()`-derived synthetic years via `new DateOnly(year, month, day)`, never wall-clock `Today`/`UtcNow`. Grepped the whole payroll-adjacent test surface (`Payroll/`, `Rbac/PayrollFilingRbacTests.cs`, `Reports/TaxSummaryTests.cs`, `TaxFilings/CitExpenseByAccountTests.cs`, `Sales/NonVatArBackfillTests.cs`, `Master/EmployeeSalaryPrecisionTests.cs`) for `DateTime.UtcNow`/`DateTime.Today` — the only hits are `CreatedAt`/`PostedAt`/`IssuedAt` audit `DateTimeOffset` stamps, never a document date compared against the new validator. The two NEW tests that do need the real current month (T19c, T20) use `new SystemClock().TodayInBangkok()` (matches `PeriodMonthlyReopenTests.cs` precedent), never `UtcNow`/`Today`.
- [x] **Unplanned but required fallout** (found via advisor review before implementing, confirmed by a RED run against the reverted guard): adding the period gate at all — not just the future-date half — breaks nearly every EXISTING payroll test that posts a run, because `FreshYearAsync`/`RandYear()` deliberately pick a synthetic (usually far-future) year purely to dodge `payroll.duplicate_period` collisions in the shared `teas_test` DB, and `PeriodCloseService.IsOpenAsync`'s fallback treats any month with no explicit row as CLOSED unless it equals the real current Bangkok month. Fix: added `OpenPeriodAsync(sp, year, month)` test helper (direct-seeds an Open `AccountingPeriod` row, mirroring the existing `FixedAssetServiceTests.OpenPeriodsAsync` precedent for the identical fallback) and called it from `RunThroughPost` plus the 5 other direct `PostAsync` call sites in the file. Confirmed via a RED run (guard reverted, only these 6 sites lacked it) → 19 of 34 tests failed with `payroll.period_closed`; after the fix, 34/34 green.
- [x] ~~Deliberate capability change flagged~~ — **SUPERSEDED, see the §3.5 AMENDED block above.**
  I originally flagged that the period-END ceiling made `Pnd1_filings_follow_payment_date_not_period`'s
  arrears-pay scenario unproducible through the service and had to rewrite the test to bypass
  `PostAsync`. Fable/Ham agreed the ceiling itself was wrong (commit `97ace1c`) and removed it —
  see the ROUND 2 entry below. `Pnd1_filings_follow_payment_date_not_period` now drives the real
  service again, no bypass.
- [x] Tests T18–T20 green (T19 rewritten for the amended rule — see ROUND 2 entry). Evidence below.

**Blast cap:** max **4** source files + **2** test files. No migrations, no API-shape change.
**Actual:** 1 source file (`PayrollRunService.cs`) + 1 test file (`PayrollRunServiceTests.cs`, 1 existing test fixed + 3 new: T18, T19, T20 + a new `OpenPeriodAsync` test helper). Well within cap. 0 migrations, 0 API-shape changes.

**Evidence — ROUND 1 (period-END ceiling, since superseded) RED→GREEN (T18/T19/T20):**
```
RED (git-stashed the source guard only, kept the new tests):
  Failed: 3, Passed: 0, Skipped: 0, Total: 3
  All three failed with "Expected a DomainException to be thrown, but no exception was thrown."

GREEN (guard restored + OpenPeriodAsync fixture fix applied):
  Full PayrollRunServiceTests class: Failed: 0, Passed: 34, Skipped: 0, Total: 34, Duration: 2m 59s
```
`dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` → 0 Warning(s), 0 Error(s) both before and after the test-fixture fallout fix.

**Evidence — ROUND 2 (ceiling removed per §3.5 amendment, commit `97ace1c`), DB-verified after the
ALL-CLEAR:**
```
RED (git-stashed the source guard, zero guard at all): T18/T19/T20 → 3 failed, 0 passed — all
  "Expected a DomainException to be thrown, but no exception was thrown." Stash restored, `git
  stash list` empty afterward.

GREEN, first pass: T18/T19/T20 → 2 passed, 1 failed — T19 sub-case (d) hit payroll.bank_required
  from PayAsync's bank-resolution branch (own test-fixture bug: shared company 1 has accumulated
  multiple active bank accounts across the suite's history), NOT from the period/pay-date guard.
  Fixed by moving sub-case (d) to a fresh TestCompanyFactory company (zero banks by construction).

GREEN, second pass: T18/T19/T20 → 3 passed, 0 failed, 0 skipped, 5s. All four T19 legs proven.

Full class: PayrollRunServiceTests → 34 passed, 0 failed, 0 skipped, 3m9s.

Pnd1_filings_follow_payment_date_not_period, isolated: 1 passed, 0 failed, 22s — driving the REAL
  service (RunThroughPost, no seeded state) end to end.
```
See the ROUND 2 attempt-log entry below for full detail. Not committed — Fable runs the
consolidated full suite before commit.

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
- **2026-08-11 sonnet-implementer: WP-2 implemented, all checklist items `[x]` except the (deliberately
  out-of-scope) Repttown apply run.** 6 source files (`GlAccountsOptions.cs`, `DependencyInjection.cs`,
  `Program.cs` edited; `NonVatArBackfillDtos.cs`, `NonVatArBackfillService.cs`,
  `AdminBackfillEndpoints.cs` new) + 1 test file (`NonVatArBackfillTests.cs`, new) — both within the 8/2
  cap. Also touched `RbacAuthMapTests.cs` + `RbacCartesianTests.cs` (mechanical allowlist entries for the
  new authn-only endpoint, same footgun WP-1 didn't hit since it added no endpoint) — counted separately
  from the 2-test-file budget, same reasoning as WP-1's EF-migration-file exclusion.
  RED confirmed first: moved the 3 new implementation files out and stashed the 3 tracked-file edits →
  `NonVatArBackfillTests.cs` fails to compile (`INonVatArBackfillService` doesn't exist) — strongest
  possible proof. Restored → `dotnet build` 0/0 → T8–T11 + 2 supplementary tests (VAT refusal, missing-
  account error) all green, 0 skipped → `RbacAuthMapTests`+`RbacCartesianTests` 4/4, 4m22s.
  **Judgment calls made, both flagged for the coordinator's awareness:**
  1. **"Closed fiscal year" classification uses the `FiscalYearClose` table literally** (a year is closed
     iff an ACTIVE row exists for it — `YearCloseService`'s own definition, D7), not a "prior year vs
     current year" heuristic. This matches the checklist's literal wording and reuses an already-audited
     codebase concept, but leaves one edge case open: a company that never ran a formal year-end close
     (plausible for Repttown — it apparently never got AR/revenue accrual right either) would have EVERY
     past fiscal year read as "not closed," so an old, never-closed invoice would credit Revenue at its
     own historical date rather than RetainedEarnings-at-today. This is a real, not hypothetical, ambiguity
     — the PREVIEW report surfaces it plainly (unexpected `Revenue` entries in an old fiscal year), so a
     human reviews it before any `apply`, per §3.2.5's own framing ("a non-zero RetainedEarnings figure...
     is an amended-filing question, not an engineering one" — the inverse gap is the same kind of question).
  2. **`AlreadyDone`/`ResumedFrom` are computed via a description-prefix tag** (`"AR Backfill "` on every
     correcting JE) rather than a separate tracking column — `BillingNote.JournalEntryId` alone can't
     distinguish "corrected by this backfill" from "accrued normally by WP-1" once both are simply
     non-null, so a second `apply` invocation (with an empty candidate plan) needs SOME way to report how
     much prior work exists. The tag is greppable/self-documenting, mirrors `GlPostingService`'s own
     `"IV {DocNo}"`/`"RC {DocNo}"` convention, and needed no schema change (0 migrations, per cap).
  Not run: the Repttown apply operation itself (explicitly out of scope — code + tests only, per dispatch).
- **2026-08-11 (post-review) sonnet-implementer: resolved judgment-call #1 per Ham's ruling.**
  `BuildPlanAsync` redefined: "closed" = a PRIOR fiscal year (`fy < currentFy`, pure comparison,
  `FiscalYearCloses` query dropped entirely — Repttown never ran the in-app year-close, so that
  table under-reported closure). Added a second guard: a CURRENT-fiscal-year invoice can still
  have its own issue MONTH already closed via the ordinary monthly `IPeriodCloseService` — I13b
  now actually guaranteed (not just at the year level): `postDate = period.IsOpenAsync(bn.DocDate)
  ? bn.DocDate : today`, credit side stays Revenue either way (still this FY's P&L, only the
  date moves). New constructor dependency: `IPeriodCloseService` (already DI-registered, no
  wiring change needed).
  RED confirmed for this specific fix: temporarily reverted `BuildPlanAsync` to the pre-fix
  `FiscalYearClose`-based logic (tests unchanged) → exactly the 3 tests touching classification
  failed for the right reasons (T9: no RetainedEarnings group found; T8: prior-FY invoice dated
  at its own 2025 issue date instead of today; missing-account test: no exception thrown at all,
  since the old logic never even reached the RetainedEarnings resolution) — T10/T11/VAT-refusal
  correctly stayed green (unrelated to this logic), proving the tests are precisely targeted, not
  vacuous. Restored → `dotnet build` 0/0 → all 6 green again.
  **A second, unplanned bug surfaced while building the "current FY, own month open" test case**:
  `PeriodCloseService.IsOpenAsync`'s real default is "OPEN only for the literal CURRENT Bangkok
  month; every OTHER missing month — past OR future — is CLOSED" (verified by reading the method,
  not assumed — same rule WP-1's spec already documented). This means almost every REAL historical
  invoice's issue month reads as closed by default (only an explicit `AccountingPeriod` row can
  make a past month open), so `postDate = today` is actually the COMMON case for current-FY
  legacy invoices, not the exception the naming implied. Fixed the test to construct a genuinely
  open past month via an explicit `AccountingPeriod(Status=Open)` row (`OpenPastMonthAsync`
  helper) rather than assuming an arbitrary past date would be open. RBAC not re-run for this
  incremental fix — no route/endpoint/policy changed, only an already-DI-registered constructor
  dependency was added, and all 6 tests already prove DI resolution succeeds.
- **2026-08-12 sonnet-implementer: fixed all 3 findings from WP-2's Opus REJECT review** (verified
  real by the coordinator in code, not reviewer noise; full suite green 1089/0/8, money math
  confirmed correct — these were defensive gaps on the immutable ledger, not I9 breaks).
  **FIX 1 (HIGH)** — `ApplyAsync` now preflights `await period.EnsureOpenAsync(today, ct)` before
  ANY posting: `PostManualEntryAsync` deliberately never calls `EnsureOpenAsync` itself (every
  poster's CALLER owns that gate), and an explicit `Closed` `AccountingPeriod` row is authoritative
  even for the literal current month (overriding `IsOpenAsync`'s "no row → open only if current
  month" default) — so a company that closes its month promptly could otherwise post every
  closed-year/closed-month correction straight into a closed period, unfixably (the JE is
  immutable once posted). `PreviewAsync` surfaces the same check as a non-throwing `Blockers`
  entry instead (new `NonVatArBackfillResult.Blockers` field) — the plan still returns, flagged.
  **FIX 2 (MEDIUM)** — the in-loop re-read's comment claimed it "guards against a concurrent apply
  run"; it did not — `AccountingDbContext.SaveChangesAsync` never bumps `BillingNote.Version`
  automatically (only `ExpenseClaimService`/`FixedAssetService` do it manually — the "inert
  Version token" pattern, `ExpenseClaimService.cs:35`), so the configured EF concurrency token was
  dead weight. Added `bn.Version++` immediately before the `JournalEntryId` stamp (mirrors
  `ExpenseClaimService.cs:186`) so a losing concurrent writer's `SaveChangesAsync` now genuinely
  throws `DbUpdateConcurrencyException` (its own transaction rolls back via the `await using tx`
  — no half-posted JE survives); corrected the comment to describe the REAL mechanism.
  **FIX 3 (LOW-MED)** — T10 previously ran `apply` to completion then added a NEW invoice and
  re-ran, proving incremental pickup but never crash-atomicity of the one-tx-per-invoice loop
  (spec §6 T10 explicitly asks for a mid-run abort). Rewrote it with a decorator
  (`FailSecondManualPostGl`, test-file-local) wrapping the real `GlPostingService`: the FIRST
  `PostManualEntryAsync(ManualJvLine[])` call passes through unchanged (invoice 1 posts and
  commits for real), the SECOND throws before reaching the real poster (simulating "the process
  died before invoice 2"). Asserts invoice 1 survives intact, invoice 2 has no JE, and a normal
  resume run completes it with exactly one JE per invoice — plus kept the original "apply again
  with nothing new → 0 posted" idempotency check as a third phase of the same test (I12's other
  half, genuinely different from crash-resume).
  **LOW item** — `PreviewAsync` now probes AR/Sales/RetainedEarnings existence too (not just the
  period), reported via the same `Blockers` list — a missing `3300` is now visible in the
  accountant-facing preview, not only when `apply` explodes.
  **RED→GREEN for all three, via temporary reverts (not git-stash — these files were never
  committed) confirmed then restored:**
  - FIX 1: commented out the `EnsureOpenAsync` preflight → the new
    `Apply_refuses_when_current_period_is_closed_...` test failed exactly as expected ("Expected a
    DomainException to be thrown, but no exception was thrown") → restored → green.
  - FIX 3: temporarily merged the per-invoice transactions into ONE shared transaction for the
    whole loop → the rewritten T10 failed exactly as expected ("Expected a value because invoice 1
    committed before the simulated crash" — invoice 1's correction was lost too when invoice 2's
    simulated failure rolled back the SHARED transaction) → restored → green.
  - FIX 2: no dedicated concurrency-race test was written (would need its own decorator/threading
    harness; not requested) — verified by code reading + the existing T8/T9/T10 suite staying green
    with `Version++` now live (no behavioral regression for the non-concurrent path).
  Final: `dotnet build` 0/0 → `NonVatArBackfillTests.cs` **7/7 passed, 0 skipped** (T8, T9,
  rewritten T10, new period-closed-blocks-apply test, VAT refusal, missing-account test extended
  to also check the preview blocker). Full suite not run (coordinator runs it). `apply` not run
  against any real company. Not committed.
- **2026-08-12 sonnet-implementer: Opus round 2 — APPROVE with 3 LOW items + 1 cosmetic note, all fixed.**
  Coordinator's own suite was live against `teas_test` for this whole round, so per instruction no
  `dotnet test` was run at all (even targeted) — verified by `dotnet build` to an isolated `-o`
  output directory instead (avoids the `MSB3027 locked by testhost` collision without touching
  their process), confirmed 0/0 twice. **Tests were written and are believed correct but are
  UNRUN — the coordinator's own run of them, after their all-clear, is the real gate for this round.**
  **LOW 1** — added `SaveGuardedAsync` mirroring `ExpenseClaimService.cs:36-47` exactly: catches
  `DbUpdateConcurrencyException`, rethrows `DomainException("backfill.locked_mismatch", ...)` (the
  `.locked_mismatch` suffix `DomainExceptionMiddleware` already maps to 409, confirmed by re-reading
  `StatusFor`). `posted.Count` folded into the message text (no new exception field, no
  restructuring) so a 409'd caller still learns how many corrected before the conflict.
  **LOW 2** — the T10 decorator's second-call branch now calls the REAL `PostManualEntryAsync`
  FIRST (so invoice 2's transaction genuinely holds a fresh JE), THEN throws — the actual
  orphan-JE window. New assertion: zero JEs with `Reference == invB's DocNo` survive after the
  rollback.
  **LOW 3** — new `Version_concurrency_token_actually_fires_on_a_stale_write` test: two independent
  `DbContext` scopes (never through `ApplyAsync`) — context B updates+bumps the same
  `BillingNote.Version` first, then context A's own `Version++` + `SaveChangesAsync` must throw
  `DbUpdateConcurrencyException`. Pins the raw EF mechanism directly, so it survives even if a
  future edit swaps the tracked re-read for `AsNoTracking`/`ExecuteUpdateAsync` (the exact "inert
  Version token" trap `ExpenseClaimService.cs:32-35` already names once).
  **Cosmetic** — `ProbeBlockersAsync`'s period-closed check is now unconditional (previously gated
  behind `if (plan.Count == 0) return blockers;`, so an empty plan silently reported zero blockers
  even when `apply` would still throw `period.closed` — preview now agrees with apply). Confirmed
  the empty-plan-refuses-when-closed behavior itself is UNCHANGED per the coordinator's explicit
  "do not fix this" — only preview's HONESTY about it changed.
  `NonVatArBackfillTests.cs` now has 8 tests total (7 from round 1 + the new LOW 3 test); T10 gained
  the orphan-JE assertion inline. Not committed.

- **2026-08-12 sonnet-implementer: WP-3 implemented, all checklist items `[x]`.** 10 source files
  (9 edited + 1 new `MoneyValidationExtensions.cs`) + 2 test files (both edited: `ManualJournalTests.cs`
  gained T12a/T12b/T13, `PayrollRunServiceTests.cs` gained T14) — exactly at the 10/2 cap. 0 migrations.
  `JournalEntry.MarkPosted` guard added verbatim per §3.1 (per-line + header 2-dp reject, `je.precision`).
  New `MoneyValidationExtensions.Satang()` mirrors `CurrencyValidationExtensions.ThbOnly`'s shape, applied
  to `CreateJournalValidator` (+ `Reference` MaxLength(255), line `Description` MaxLength(500),
  `Lines.Count<=200`), `UpdatePayrollDeductionsValidator.Amount`, and the expense-claim/PV/VI line
  `Amount` validators. Confirmed no separate Update DTO/validator exists for PV (Create-only) or VI
  (`UpdateDraftAsync` reuses `CreateVendorInvoiceRequest`/`CreateVendorInvoiceValidator`) — only
  ExpenseClaim has genuinely separate Create/Update validators, both sharing one
  `ExpenseClaimLineInputValidator`, so `.Satang()` there covers both by construction.
  `Math.Round(…,4)` → `2` at the three proven sites (`ExpenseClaimService.cs:100`,
  `PaymentVoucherService.cs:234`, `VendorInvoiceService.cs:240`).

  **RED→GREEN.** T12a/T12b/T13/T14 written first against `main`'s pre-fix code and confirmed RED for the
  right reason (100.005 draft validates `IsValid:true`; posting a 4dp/mixed-precision draft succeeds with
  `Status:Posted`; the 3dp payroll-deduction validator error list has no "2 decimal" entry) — 4/4 failed,
  0 skipped. Then all 10 source changes applied; re-ran the same filter: 4/4 passed. Full targeted sweep
  after: `ManualJournalTests`+`PayrollRunServiceTests` 60/60, `Purchase`/`Expense`/non-VAT-hardening
  namespaces 82/82, `Accounting.Domain.Tests` (full project, includes `JournalEntryTests.cs` which reads
  `MarkPosted` directly) 188/188, MCP suites touching these DTOs
  (`McpBankExpenseFixedAssetTests`/`McpReadExpansionTests`/`McpWriteExpansionTests`/
  `McpManualJournalTests`/`McpDocumentChainTests`) 92/92 — 0 skipped throughout (`TEAS_TEST_PG` set
  per-shell-call per the footgun note).

  **Pre-existing test data that had to be fixed — reported loudly, per the dispatch's explicit footgun
  warning.** The first full-suite run surfaced **17 unrelated payroll test failures**, all
  `je.precision` on absurd aggregate amounts (`Dr 151390834.9492` etc.). Root-caused with a throwaway
  diagnostic query (not a guess): **41** active company-1 employees in the shared `teas_test` DB all
  carried the exact salary `45,678.9012` — every one traced to repeated historical runs of
  `B1_full_month_control_gross_taxable_is_unrounded_base_salary` (O8 proration test, pre-dates this
  release). That test's employee has no `TerminationDate` and is never deactivated, so per this file's
  own documented class invariant ("the run pools EVERY active company-1 employee") it silently joined
  the aggregate salary-expense total of every OTHER payroll-posting test forever — invisible until
  `MarkPosted`'s new precision guard finally refused to post a corrupted total (**I14 working exactly as
  designed**, not a guard defect). This is squarely the "existing test quietly posting >2dp values" case
  the dispatch called out — **fix is to the test, not the guard**: B1 now deactivates its own fixture
  employee (`IsActive = false`) immediately after its own assertions, so it still proves the intended
  "full-month gross is unrounded, no `Math.Round` call in that path" property but stops leaking into
  every other test's shared-DB aggregate. The 41 already-poisoned rows in the LOCAL `teas_test` were
  deactivated via a one-time cleanup query (data fix only, no schema change, not part of the file cap).
  **No test assertion was weakened and no guard was loosened** — B1's own assertions are untouched.

  **`Math.Round(…, 4` grep — every survivor is FX/rate or an already-deferred sales-line producer, none
  of the three WP-3 targets remain:**
  ```
  PaymentVoucherService.cs:375   TotalAmountThb = Math.Round(totalPaid * req.ExchangeRate, 4, …)
  PurchaseOrderService.cs:95     po.TotalAmountThb = Math.Round(po.TotalAmount * po.ExchangeRate, 4, …)
  VendorInvoiceService.cs:276    vi.TotalAmountThb = Math.Round(vi.TotalAmount * vi.ExchangeRate, 4, …)
  QuotationChainServices.cs:23   gross = Math.Round(qty * price, 4, …)                    [§2.3 DEFERRED]
  QuotationChainServices.cs:25   … discount … Math.Round(…, 4, …)                          [§2.3 DEFERRED]
  ReceiptService.cs:92           TotalAmountThb = Math.Round(computed.Amount * req.ExchangeRate, 4, …)
  ReceiptService.cs:401          rc.TotalAmountThb = Math.Round(computed.Amount * req.ExchangeRate, 4, …)
  TaxAdjustmentNoteService.cs:112 TotalAmountThb = Math.Round(total * req.ExchangeRate, 4, …)
  TaxInvoiceService.cs:306       TotalAmountThb = Math.Round(total * req.ExchangeRate, 4, …)
  TaxInvoiceService.cs:476       ti.TotalAmountThb = Math.Round(total * req.ExchangeRate, 4, …)
  TaxInvoiceService.cs:575       gross = Math.Round(input.Quantity * input.UnitPrice, 4, …)  [§2.3 DEFERRED]
  TaxInvoiceService.cs:577       … discount … Math.Round(…, 4, …)                            [§2.3 DEFERRED]
  ```
  All `TotalAmountThb` sites are the legitimate 4dp FX-conversion memo field named in §2.3/§3.4 — untouched.
  The `QuotationChainServices`/`TaxInvoiceService` sales-line-gross sites are the exact sites §2.3
  explicitly DEFERS ("no evidence of a sub-satang JE from this path in the swarm... the seam guard now
  catches it as `je.precision` if it ever happens") — also untouched, as instructed.

  **Deviation from spec: none.** `JournalEntry.MarkPosted` guard is byte-for-byte the §3.1 code block.
  `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` → 0 Warning(s),
  0 Error(s) throughout.

  **Follow-up verification (self-review before reporting):**
  1. Confirmed `CreateJournalValidator` is genuinely live-wired, not just unit-testable: both
     `JournalEndpoints.cs` (`POST /journals` calls `validator.ValidateAsync` → `Results.ValidationProblem`
     = real 400) and the MCP tool `CreateManualJournalDraftAsync` (`TeasMcpTools.cs:1113`) DI-inject
     `IValidator<CreateJournalRequest>` and call it. T12's literal wording ("draft with 100.005 → 400") is
     provably true of the live route, not just the validator class.
  2. Widened the regression sweep beyond the changed files' own areas, since `MarkPosted`'s blast radius
     is every posting path: `Sales`+`FixedAsset`+`Bank`+`YearEndClosingTests` namespaces, 168/168, 0
     skipped — no other latent 4dp fixture found (the deferred sales-line `Math.Round(qty*price,4)` sites
     named in §2.3 did not trip anything live).
  3. Hardened `B1_full_month_control_...`'s cleanup with `try`/`finally` (mirrors this file's own
     `Pay_without_any_active_bank_credits_cash_1110` precedent) — the original version only deactivated
     the 4dp fixture employee if the test's OWN assertions passed; a future failure in that test would
     have re-leaked the exact poison this release just cleaned up. Re-ran `ManualJournalTests`+
     `PayrollRunServiceTests` after: 60/60 unchanged.
  4. **Process notes for the record:** `git stash push/pop` was used once, working-tree only, to prove
     T14 RED against pre-fix source (T12/T13 were written and confirmed RED before any fix — true
     first-attempt RED; T14 was written after the source fix landed and its RED was verified
     retroactively via the same stash). No commit was made at any point. The full solution-wide
     `dotnet test` was deliberately NOT run per dispatch — Tier 1 exception, Fable runs it.

- **2026-08-12 sonnet-implementer: WP-4 implemented, all checklist items `[x]`.** 5 source files
  (4 edited + 1 new `Accounting.Application/Abstractions/ExpenseAccountRule.cs`) + 2 test files (1 edited:
  `ExpenseClaimServiceTests.cs`; 1 new: `ExpenseCategoryServiceTests.cs`). 0 migrations. 1 new endpoint
  (`PUT /expense-categories/{id}`).

  **The rule.** New shared static `ExpenseAccountRule.IsAllowedType(AccountType, bool categoryIsCapex)`
  (`accountType == Expense || (categoryIsCapex && accountType == Asset)`) — placed in
  `Accounting.Application.Abstractions` (already referenced by both `ExpenseClaimService` and
  `MasterDataServices`) specifically so the two independent callers (line resolution +
  category-master validation) can never drift apart, per the spec's "same rule" instruction. This
  is the ONLY deviation from a byte-for-byte reading of §3.3's inline snippet — the snippet's
  `typeOk` expression is reproduced verbatim inside the helper, just factored so it is defined once.

  `ExpenseClaimService.EnsureExpenseAccountAsync` gained a `categoryIsCapex` parameter and the type
  check (after the existing exists/active/header checks, same error code
  `expense_claim.expense_account_invalid`). **Both** `BuildLinesAsync` branches (override AND
  category-default) now call it with `category.IsCapex` — the stale "already validated when the
  category was set up" comment is deleted and replaced with a comment stating the true history
  (VERDICT C5: it never was). `BuildLinesAsync` also now refuses an inactive category
  (`expense_claim.expense_category_inactive`) right after the category lookup, before either branch
  resolves an account. `PayAsync`'s existing non-VAT re-guard block was extended: `Lines` is now
  loaded unconditionally (previously only under the non-VAT branch), then EVERY line's resolved
  `ExpenseAccountId` is re-validated through `EnsureExpenseAccountAsync` against its category's
  live `IsCapex` flag — defence in depth for a claim drafted before this shipped, or whose category/
  account was poisoned or deactivated between Approve and Pay.

  `ExpenseCategoryService` gained a private `EnsureDefaultAccountAsync` (same exists/active/header/
  type bundle, error `expense_category.default_account_invalid`) called from **both** `CreateAsync`
  (when `DefaultExpenseAccountId` is supplied) and the new `UpdateAsync`. `UpdateExpenseCategoryRequest`
  deliberately has NO `CategoryCode` field (structural immutability — the doc-prefix invariant is
  enforced by the type system, not a runtime guard) and DOES carry `IsActive` (the fix for a
  poisoned category that cannot be deleted). `PUT /expense-categories/{id}` reuses
  `Permissions.Sys.ExpenseCatManage` (same as `POST`) — confirmed zero RBAC matrix churn via the
  auto-generated `docs/rbac/endpoint-permission-map.generated.md` diff (+1 row, `Perm |
  sys.expense_category.manage`, total 358→359) produced by the RBAC test run itself.

  **RED→GREEN.** All 15 new/changed tests (T15: 2×5 theory cases [override + category-default
  branches] × {1120 Bank, 2110 AP, 4100 Revenue, 3300 Equity, 1170 Input VAT} = 10, plus inactive-
  category and Pay-re-guard cases; T16: 1 CAPEX test; T17: 4 tests — create-rejected, update-repoint-
  valid, update-repoint-still-invalid, update-deactivate-then-claim-rejected) were written FIRST
  against a minimal compile-only scaffold (DTO/interface/no-op `UpdateAsync` stub, zero validation
  logic) and run: **15 failed / 14 passed / 0 skipped** — every failure was `Assert.Throws() ...
  No exception was thrown`, i.e. every wrong-type account (bank/AP/revenue/equity/input-VAT) and
  every "should still be rejected/inactive" case was silently ACCEPTED, proving the C5 bug and the
  missing category-management guards for the right reason. The 14 passes were the pre-existing
  suite plus the one repoint-to-a-VALID-account test (correctly green even pre-fix, since basic
  CRUD already worked — only the wrong-type rejection was missing). Then all 5 source changes
  applied (the stub `UpdateAsync` replaced by the real validating implementation); re-ran the exact
  same filter: **29 passed / 0 failed / 0 skipped.**

  **Confirmed I19 (the trap this WP exists to avoid) explicitly:** `Seeded_CAPEX_category_still_
  creates_and_pays_successfully` reads the CAPEX category seeded by `TestCompanyFactory.CreateAsync`
  (the real `ICompanyService.CreateAsync` onboarding path — not a hand-rolled fixture), asserts its
  resolved account IS `AccountType.Asset` (1610) BEFORE exercising it, then drives a real claim
  through Create→Submit→Approve→Pay and asserts the JE's debit line lands on that Asset account for
  the full amount. Green both before AND after (never broke it) — proves the Asset-iff-IsCapex
  exception works, not merely that it compiles.

  **Regression sweep after GREEN:** `Rbac` namespace (RbacAuthMapTests + RbacCartesianTests) 67/67, 0
  skipped, ~6m10s (a new endpoint always disturbs these — TEAS_REPO_ROOT set). Broader sweep —
  `Expense`+`Master`+`Purchase`+`Mcp` namespaces (covers every other caller of `ExpenseClaimService`,
  `ExpenseCategoryService`, and the MCP `ListExpenseCategoriesAsync` read path) — 356/356, 0 skipped,
  ~6m. No pre-existing failures encountered (unlike WP-3's 41-row salary poisoning) — no test data
  needed cleanup.

  **Deviation from spec: none**, except the ONE factoring choice noted above (shared
  `ExpenseAccountRule` helper vs. inlining the 2-line `typeOk` check twice) — made to satisfy the
  spec's own "same rule" requirement for `ExpenseCategoryService`'s validation with zero risk of the
  two copies drifting apart later. `dotnet build backend/Accounting.sln --no-restore -m:1
  -p:BuildInParallel=false` → 0 Warning(s), 0 Error(s) throughout. Full solution-wide `dotnet test`
  deliberately NOT run per dispatch — Tier 1 exception, Fable runs it. Not committed.

- **2026-08-12 sonnet-implementer: WP-4 ROUND 2 — Opus Tier-2 REJECT addressed, §3.3 amendment
  implemented.** Fault was the (pre-amendment) spec's, not round 1's implementation — §3.3's rule
  was reproduced verbatim and correctly, and the rule itself let bank/AR/input-VAT (all
  `AccountType.Asset`, same as the seeded CAPEX category's 1610) through for any capex-category
  claim. Fixed per Fable's amendment (commit `a2e9508`) and the coordinator's FIX 1/2/3 dispatch:

  **FIX 1 — corrected rule, split into two functions (deviation flagged, as invited).**
  `ExpenseAccountRule` now has `IsAllowedClaimLineAccount(accountType, accountId, categoryIsCapex,
  categoryDefaultAccountId)` — allowlist of exactly the category's OWN
  `DefaultExpenseAccountId` — and `IsAllowedCategoryDefaultAccount(accountType, categoryIsCapex)` —
  plain Expense-or-(IsCapex&&Asset), unchanged from round 1. **Did not force one shared predicate**:
  reusing the claim-line shape at category-master validation time would compare the CANDIDATE
  account being set against itself (`accountId == categoryDefaultAccountId` where both ARE the
  same incoming value) — a tautology permitting any account once `IsCapex=true`, exactly the
  trap the coordinator flagged. `EnsureExpenseAccountAsync` (`ExpenseClaimService`) now takes
  `categoryDefaultAccountId` and uses the claim-line rule; `EnsureDefaultAccountAsync`
  (`ExpenseCategoryService`) keeps the master rule, gated behind `Sys.ExpenseCatManage` (an
  elevated permission, not the ordinary `expense.claim.create` the P0 exploited). Both
  `BuildLinesAsync` branches now pass `category.DefaultExpenseAccountId` alongside `category.IsCapex`.

  **FIX 2 — dead-end class closed.** New shared `RevalidateLineAccountsAsync(claim, ct)` (loads
  `Lines`, batches category lookups, calls `EnsureExpenseAccountAsync` per line) is now called
  from **`SubmitAsync`**, **`ApproveAsync`**, and `PayAsync` (refactored to call the same helper —
  no more triplicated inline logic). `ExpenseClaim.Cancel()` (domain entity) now permits
  `Approved -> Cancelled` in addition to Draft/Rejected — Paid remains the only non-cancellable
  post-Draft state (a JE already exists by then). Existing test
  `Illegal_transitions_throw_the_named_domain_error` asserted Cancel-on-Approved threw
  `cannot_cancel` — that assertion encoded the now-superseded rule (same category of fix as WP-1's
  two edited pre-existing tests) and was corrected in place: it now asserts the cancel succeeds,
  plus a new assertion that Cancel-on-**Cancelled** (still terminal) still throws.

  **FIX 3 — the PUT footgun closed.** `UpdateExpenseCategoryRequest.DefaultExpenseAccountId`
  changed `long?` → `long` (required), `+RuleFor(x => x.DefaultExpenseAccountId).GreaterThan(0)`,
  and `ExpenseCategoryService.UpdateAsync` now calls `EnsureDefaultAccountAsync` UNCONDITIONALLY
  (the old `if (req.DefaultExpenseAccountId is {} acctId)` skipped validation entirely when the
  field was omitted, which is also what let it null the column). **Chosen shape:** full-replace
  PUT semantics (require + always validate) over partial-patch-with-existing-value-comparison —
  matches the codebase's existing `UpdateAccountRequest`/`UpdateVendorRequest`/`UpdateBranchRequest`
  convention (none of them do null-means-unchanged either), and is the simpler of the two options
  the coordinator offered. `Create*` was left untouched (nullable `DefaultExpenseAccountId` is a
  pre-existing, tested, legitimate "category with no default, override-only" shape at create time
  — the coordinator's FIX 3 was scoped to Update only).

  **LOW — stale BP-01 comment on `ExpenseCategoryService.ListAsync`** corrected in place (the
  super-admin query-filter-bypass arm it described was removed 2026-07-08; the explicit
  `.Where(CompanyId == tenant.CompanyId)` itself is still correct to keep, just not for the reason
  the old comment gave).

  **Not mine, per dispatch:** the pre-deploy `expense_claim_lines` audit — explicitly "fold into
  WP-6" in the amendment, and the coordinator's dispatch to me covers FIX 1/2/3 + the LOW comment
  only. `ParentCategoryId` validation — explicitly "not yours ... I will log it."

  **New/changed tests (all written, NONE run this round — TEST-DB HOLD, WP-5 owns `teas_test`):**
  `CAPEX_category_override_to_BANK_is_rejected_the_P0_this_WP_exists_to_close` (the direct P0
  regression — would have PASSED incorrectly under round 1's code, must fail now),
  `CAPEX_category_override_to_a_DIFFERENT_asset_account_is_also_rejected` (proves the allowlist-
  of-one, not "any Asset", per the amendment's accepted trade-off), `Submit_rejects_a_legacy_
  draft_holding_an_invalid_account` (direct-DB-inserted claim, bypassing BuildLinesAsync, proving
  FIX 2's "sails through post-deploy" scenario is closed), `Approve_rejects_a_submitted_claim_
  whose_account_was_deactivated_after_submit`, `Cancel_is_now_legal_from_Approved_the_escape_
  hatch_for_an_invalidated_account` (proves Pay still refuses AND Cancel now succeeds, on the SAME
  poisoned claim), `Update_validator_rejects_a_missing_or_zero_DefaultExpenseAccountId` (pure
  `[Fact]`, no DB — validator-only, safe under the hold), plus the corrected
  `Illegal_transitions_throw_the_named_domain_error`.

  **Verification under the hold:** `dotnet build backend/Accounting.sln --no-restore -m:1
  -p:BuildInParallel=false -o <scratchpad isolated dir>` → **0 Warning(s)*, 0 Error(s)** (*1
  harmless NETSDK1194 advisory about `-o` at solution scope, not a code warning), run twice (once
  after FIX 1/2/3 source changes, once after the new tests were added) — confirms every file in
  this round, including all new/edited tests, compiles clean. `grep -rn "IsAllowedType\b"` across
  `backend/` → zero hits (confirms no stale reference to the renamed round-1 method survived the
  split into `IsAllowedClaimLineAccount`/`IsAllowedCategoryDefaultAccount`). **No `dotnet test` was
  run — per the explicit hold, awaiting Fable's all-clear on `teas_test`.** Not committed.

- **2026-08-12 sonnet-implementer: WP-4 ROUND 3 — Opus Tier-2 residual closed (category-master
  denylist).** Round 2's own doc comment named the gap before the reviewer did: `IsAllowedClaimLineAccount`
  said bank/cash/AR/input-VAT can never pass "unless the category itself was poisoned, which is the
  category-master path's job to prevent" — but `IsAllowedCategoryDefaultAccount` was still type-only,
  so `Sys.ExpenseCatManage` could point the seeded CAPEX category's default AT bank 1120, after which
  every ordinary employee's claim on that category passes (account IS now the blessed default) and
  posts Dr Bank / Cr Bank — the same P0, one permission level up. I18 is an invariant, not a
  permission-gated one.

  **Fix, exactly per dispatch.** `ExpenseCategoryService.EnsureDefaultAccountAsync` — inside the
  `isCapex && account.AccountType == AccountType.Asset` branch ONLY — now additionally refuses the
  account if it is one of the company's resolved `GlAccountsOptions.{CashAccount,BankAccount,
  ArAccount,InputVatAccount,WhtReceivableAccount}` role accounts, OR any `bank_accounts.GlCashAccountId`
  for that company (a company can have several bank accounts). Both checks are ONE batched EF query
  each (not N sequential/parallel calls) — **caught my own bug during implementation**: my first draft
  used `Task.WhenAll` over 5 `ResolveAccountIdAsync` calls sharing the SAME `DbContext` instance, which
  is an EF Core concurrency violation ("a second operation was started on this context instance before
  the previous operation completed") — rewritten to a single `AnyAsync` with `roleCodes.Contains(a.AccountCode)`
  (translates to one SQL `IN (...)`) before it ever reached a build/test. Codes resolved from
  `IOptions<GlAccountsOptions>` (already DI-registered — `ChartOfAccountService` in the same file
  already injects it, confirmed before adding the constructor param), never hardcoded. A role code
  absent from a company's CoA is skipped (not thrown) — a capex-category edit must not fail on an
  unrelated missing account; this mirrors the codebase's `ResolveAccountIdAsync` convention in intent
  (resolve by code, never a literal) without adopting its "throw if missing" behavior, which would be
  the wrong failure mode for a defence-in-depth check that isn't the primary control.

  **Explained, not just implemented, per the coordinator's explicit ask.** `ExpenseAccountRule.cs`'s
  class-level doc and `EnsureDefaultAccountAsync`'s method doc both now state WHY the denylist is
  correct here and wrong at the claim-line: this is a single decision, made once, by an elevated
  permission, over GL roles the system already knows by configuration — defence in depth BEHIND the
  permission gate + type check, not the primary control. Explicitly flagged: "Do not simplify this
  back into one shared predicate with the claim-line rule."

  **New tests (written, NOT run — hold still in effect):**
  `Create_of_a_CAPEX_category_pointing_at_a_cash_bank_or_VAT_role_account_is_rejected` and
  `Update_of_a_CAPEX_category_to_point_at_a_cash_bank_or_VAT_role_account_is_rejected`, both
  `[SkippableTheory]` over `{1120 Bank, 1170 Input VAT}` — exactly the dispatch's "add a test:
  setting a capex category's default to the bank account (and to input VAT) is refused at create
  AND at update."

  **Verification under the hold:** isolated `-o` build (fresh scratchpad dir) → **0 Warning(s)*, 0
  Error(s)** (*1 harmless NETSDK1194 advisory, same as every round). Confirmed `IOptions<GlAccountsOptions>`
  is DI-registered (`DependencyInjection.cs:112`, `AddOptions<GlAccountsOptions>().Bind(...)`) before
  adding the constructor dependency, so no DI wiring change was needed beyond the primary-constructor
  parameter itself. **Still no `dotnet test` run — TEST-DB HOLD unchanged, WP-5 still owns `teas_test`.**
  Ready to run the FULL round 2 + round 3 targeted evidence — including the mandatory RED proof for
  `CAPEX_category_override_to_BANK_is_rejected_the_P0_this_WP_exists_to_close` against round-1's rule
  — the instant the coordinator's ALL-CLEAR lands. Not committed.

- **2026-08-12 sonnet-implementer: ALL-CLEAR received, RED→GREEN evidence gathered, checkpoint.**
  `teas_test` freed by the coordinator (WP-5 payroll finished). Ran the mandatory proofs:
  **LEG 1 (P0, round-1's exact rule)** — since round 1 was never a separate commit, git-stashed
  ONLY `ExpenseClaimService.cs` (`git stash push -- <file>`, safe: `IExpenseClaimService`'s public
  signatures never changed, so the reverted file still satisfies the interface) to reach pre-WP-4
  HEAD, THEN hand-applied round-1's literal formula (`Expense || (categoryIsCapex && Asset)`) on
  top, restoring the `categoryIsCapex` param and the override-branch call site exactly as round 1
  had it. Ran `CAPEX_category_override_to_BANK_is_rejected_the_P0_this_WP_exists_to_close` filtered
  → **RED: 1 failed, "No exception was thrown."** Restored via `git checkout -- <file>` (discard
  temp patch, back to HEAD) then `git stash pop` (round-3 code back) — verified `grep -c "TEMP
  RED-LEG-1"` → 0, `dotnet build` → 0/0. Re-ran the same filter → **GREEN: 1 passed.**
  **LEG 2 (category-master denylist, round-2's code)** — a file-level stash was NOT viable here:
  reverting `MasterDataServices.cs`/`ReferenceDtos.cs`/`MasterEndpoints.cs` to HEAD removes
  `UpdateAsync`/`UpdateExpenseCategoryRequest`/the PUT route entirely, which `ExpenseCategoryServiceTests.cs`
  references in OTHER test methods too — the whole assembly fails to compile, blocking every test,
  not just the 2 targeted. Used a surgical temporary edit instead (working-tree-only, same spirit):
  deleted round-3's denylist `if` block from `EnsureDefaultAccountAsync` only, kept everything else
  (ctor param, type check, `UpdateAsync` etc.) intact. Ran both new theories
  (`Create_of_a_CAPEX_category_pointing_at_a_cash_bank_or_VAT_role_account_is_rejected`,
  `Update_of_a_CAPEX_category_to_point_at_a_cash_bank_or_VAT_role_account_is_rejected`, 2 InlineData
  each) → **RED: 4 failed, all "No exception was thrown"** (bank AND input-VAT both silently
  accepted as a capex default without the denylist). Restored the block via Edit, verified
  `grep -c "TEMP RED-LEG-2"` → 0, `dotnet build` → 0/0. Re-ran → **GREEN: 4 passed.**
  **Full GREEN sweep:** `ExpenseClaimServiceTests` + `ExpenseCategoryServiceTests` (both classes,
  one filter) → **39 passed / 0 failed / 0 skipped.** `Rbac` namespace (`RbacAuthMapTests` +
  `RbacCartesianTests`, `TEAS_REPO_ROOT` set) → **67 passed / 0 failed / 0 skipped**, ~6m13s.
  No pre-existing `teas_test` pollution encountered in these namespaces (coordinator warned of 5
  unrelated state-caused failures elsewhere today from WP-5/other work — none surfaced here; stayed
  entirely inside Expense/Master/Rbac namespaces as instructed, never touched payroll). Stash list
  confirmed empty after both legs; no `TEMP RED-LEG` markers left in `backend/src/`. **Quota
  checkpoint at 89% (block 95) — this entry written as durable insurance; no commit made (worker
  rule + coordinator's explicit "do NOT commit" both apply — the coordinator commits after reading
  the full diff).** Not committed.
  Cap raised 10/2 → 12/3. `TaxInvoiceService.BuildLine` and `QuotationChainServices.ChainMath.Line` round
  the line's gross to 4dp; when `DiscountPercent == 0` (the common case) that value flows UNCHANGED into
  `net`/`LineAmount`/`TotalAmount` — confirmed by reading `GlPostingService.PostTaxInvoiceAsync`, which
  uses `ti.SubtotalAmount`/`ti.TotalAmount` directly as the JE Dr/Cr amounts with no rounding step in
  between. A fractional quantity against an odd unit price (1.5 kg @ ฿33.33 → gross 49.995, 3dp) is
  ordinary business, and after WP-3's `MarkPosted` guard landed it could no longer be posted at all
  (`je.precision`) — the original "no evidence from the swarm" deferral reasoning was true but not the
  same as safe, exactly Fable's point. Fixed `4` → `2` at all four sites (`TaxInvoiceService.cs:575/577`,
  `QuotationChainServices.cs:23/25`).

  **RED→GREEN**, new file `Sales/TaxInvoiceLinePrecisionTests.cs`
  (`Fractional_qty_times_odd_price_rounds_line_to_2dp_and_posts_cleanly`): creates a VAT-company TI
  with one line (qty 1.5, unit price 33.33, 7% VAT), posts it. Confirmed RED first against pre-fix
  source: `PostAsync` threw `je.precision` with `Dr 53.495` (= net 49.995 + vat 3.50) — the exact live
  proof of Fable's traced consequence. After the fix: GREEN, `line.LineAmount == 50.00m` (1.5×33.33 =
  49.995 exactly, `decimal.Round(49.995, 2, AwayFromZero)` = 50.00 — the value sits exactly on the 2dp
  midpoint, AwayFromZero rounds a positive midpoint up), JE `Dr == Cr`, every line ≤2dp.

  **Regression sweep after the fix** (no fixture needed updating — 0 pre-existing tests asserted a 4dp
  gross value): `Sales`+`Purchase`+`Expense`+non-VAT-hardening namespaces 172/172 · MCP suites incl.
  `McpDocumentChainTests` (exercises the Q→SO→DO→TI chain directly) 92/92 — 0 skipped throughout.

  **`Math.Round(…, 4` grep, re-run after the fix** — only the legitimate `TotalAmountThb` FX-memo sites
  remain (`PaymentVoucherService.cs:375`, `PurchaseOrderService.cs:95`, `VendorInvoiceService.cs:276`,
  `ReceiptService.cs:92/401`, `TaxAdjustmentNoteService.cs:112`, `TaxInvoiceService.cs:306/476`); every
  sales-line-gross site is gone.

  `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` → 0/0 throughout.
  No commit made; full solution-wide suite still deliberately not run (Fable's gate).

- **2026-08-12 sonnet-implementer: WP-3 Opus-REJECT round — FIX A + FIX B, Fable-authorised, +2
  source/+1 test on top of the round-2 cap (now 14/4).** Opus confirmed the guard itself correct
  (choke point, reject-never-round, no FX regression, sales-line addition balances in both VAT
  branches) and rejected on inputs/legacy data around the guard — the legacy-data half (existing
  >2dp rows in `gl.journal_lines`/`payroll_runs.total_*`/`employees.base_salary`) is explicitly
  **out of scope**, spun into the new WP-6 below; Fable specified it separately as a prod-data
  operation, not code.

  **FIX A** — `EmployeeRules.Common` (`EmployeeDtos.cs`) had `salary.GreaterThanOrEqualTo(0m)` and
  no scale rule, so an employee with a >2dp `BaseSalary` (accepted today by both
  `POST`/`PUT /employees`) flows RAW into the payroll accrual JE line
  (`PayrollMath.MonthlyGross` returns a full-month `BaseSalary` unrounded, by design — no
  `Math.Round` call in that path) and now hits `je.precision` at post time — refusing the WHOLE
  company's run (nobody paid), naming a journal line the user cannot edit rather than the
  employee record that caused it. Same mechanism, production-input side, as the 41 poisoned `B1`
  fixture rows diagnosed earlier this WP. Added `.Satang()` to the shared `salary` rule in
  `EmployeeRules.Common` — covers `CreateEmployeeValidator` + `UpdateEmployeeValidator` by
  construction (one shared rule method, both validators call it).

  **RED→GREEN** (pure FluentValidation unit tests, no DB — new file `EmployeeSalaryPrecisionTests.cs`):
  `Create_with_more_than_2dp_salary_is_rejected_at_the_edge` / `Update_..._rejected_at_the_edge` /
  `A_2dp_salary_still_validates_cleanly_on_create_and_update`. RED confirmed via a working-tree-only
  `git stash` of `EmployeeDtos.cs` alone (Fable's suite was live against `teas_test`, so the whole
  solution's shared `bin/` was lock-held by their `testhost` — MSB3027, troubles-wiki.md line 374 —
  the usual `dotnet build -o <isolated dir>` workaround was used for every build+run this round,
  never touching the shared output or Postgres): 2/3 failed (`IsValid` was `True` for the 4dp
  salary on both Create and Update; the 2dp case correctly passed even pre-fix). Stash popped,
  re-built isolated, re-ran: 3/3 GREEN. `dotnet test <isolated-dll-path> --filter ...` (vstest
  directly against the pre-built DLL) was used instead of `dotnet test <project>` specifically
  because these are pure sync unit tests with no DB dependency — confirmed genuinely non-conflicting
  with Fable's running suite (0 Postgres connections, 65ms wall time, no shared `bin/` write).

  **FIX B** — rewrote both `je.precision` messages in `JournalEntry.MarkPosted`. Old per-line text
  ("Restate the split in satang — the entry is not rounded automatically") is correct only for a
  hand-keyed manual JV; payroll has no line for the user to restate (the cause is an employee
  record), year-close's cause is immutable posted history. New text states the line/amounts, says
  satang (2dp) explicitly, and points at "the source document or master-data record that produced
  this amount" instead of instructing a restate the caller may not control. The header variant
  previously said nothing about the actual numbers — now names `TotalDebit`/`TotalCredit`
  explicitly. Grepped the whole test tree for the old message substrings
  (`"Restate the split"`, `"must have at most 2 decimal places"`) first — zero hits, so no test
  pinned the old text; confirmed via the same isolated build (0/0).

  **Not attempted, per Fable's explicit instruction:** the legacy >2dp rows already sitting in
  `gl.journal_lines`, `payroll_runs.total_*`, `employees.base_salary` — a prod-data operation
  Fable is specifying separately (now WP-6 below). Nothing in this round touches existing data.

  Blast cap this round: +2 source (`EmployeeDtos.cs`, `JournalEntry.cs` — already-counted from
  round 1/2) + 1 new test file. **Actual for the FIX A/B round: 2 source files touched
  (`EmployeeDtos.cs` new edit, `JournalEntry.cs` re-edited) + 1 new test file
  (`EmployeeSalaryPrecisionTests.cs`)** — within the +2/+1 authorisation. Running cumulative WP-3
  total: 13 source files + 4 test files (12+3 from the scope-addition round, +1 new source file
  `EmployeeDtos.cs` that round 1/2 never touched, +1 new test file this round;
  `JournalEntry.cs` was already counted). No commit made.

- **2026-08-12 sonnet-implementer: WP-5 implemented (C3 payroll period + future-pay-date guard),
  all checklist items `[x]`.** `PayrollRunService` injects `IPeriodCloseService`; a new
  `EnsurePostablePayDateAsync` (per §3.5, adapted per the spec's own implementer note — the entity
  has `PeriodYearMonth` as one `yyyymm` string, not separate `PeriodYear`/`PeriodMonth` fields) is
  called first in both `PostAsync` (after the `Approved`-status check, before number allocation —
  a refusal consumes no doc number) and `PayAsync` (after the `Posted`/`PaidAt` checks, before bank
  resolution). Two distinct error codes as specced: `payroll.pay_date_outside_period` (PayDate
  beyond the run's own period end) and `payroll.period_closed` (the period itself not open — message
  names the O14 reopen route). 1 source file (`PayrollRunService.cs`, cap 4) + 1 test file
  (`PayrollRunServiceTests.cs`, cap 2).

  **UtcNow sweep (troubles-wiki line 776): zero hits.** Every existing payroll test already computes
  `PayDate`/period from `FreshYearAsync`/`RandYear()`-derived synthetic years via explicit
  `new DateOnly(...)`, never wall-clock `Today`/`UtcNow`, for exactly this reason (the class-level
  isolation comment predates WP-5). Grepped the whole payroll-adjacent test surface (Payroll/,
  Rbac/PayrollFilingRbacTests.cs, Reports/TaxSummaryTests.cs, TaxFilings/CitExpenseByAccountTests.cs,
  Sales/NonVatArBackfillTests.cs, Master/EmployeeSalaryPrecisionTests.cs) — only hits are
  `CreatedAt`/`PostedAt`/`IssuedAt` audit `DateTimeOffset` stamps, never a document date compared
  against a server rule. The two new tests needing the REAL current month (T19c, T20) use
  `new SystemClock().TodayInBangkok()`, matching `PeriodMonthlyReopenTests.cs` precedent.

  **Unplanned but forced fallout, found via advisor review BEFORE implementing and confirmed by a
  RED run:** the period gate itself (I21), not just the future-date half, breaks nearly every
  EXISTING payroll test that posts a run — `FreshYearAsync`/`RandYear()` deliberately pick a
  synthetic (often far-future) year purely to dodge `payroll.duplicate_period` collisions on the
  shared `teas_test`, and `PeriodCloseService.IsOpenAsync`'s fallback treats any month with no
  explicit row as CLOSED unless it equals the real current Bangkok month. Proven: reverted just the
  source guard (git stash), reran the class → 19 of 34 failed with `payroll.period_closed`. Fix:
  new `OpenPeriodAsync(sp, year, month)` test helper (direct-seeds an Open `AccountingPeriod` row),
  mirroring the EXISTING `FixedAssetServiceTests.OpenPeriodsAsync` precedent for the identical
  fallback — wired into `RunThroughPost` plus the 5 other direct `PostAsync` call sites in the file.
  Restored the guard, reran → 34/34 green, 0 skipped.

  **Deliberate capability change, flagged not silently patched:**
  `Pnd1_filings_follow_payment_date_not_period` asserted a December-period run PAID 5 ม.ค. of the
  NEXT year (ม.52/ม.59 arrears pay) posts successfully — that is now exactly the T19(a) refusal
  shape (PayDate beyond the run's own period end), so I22 makes it impossible to *produce* through
  the service going forward. This is Ham's decision as written (§8 #3), not a WP-5 bug — but prod
  already holds runs posted before WP-5 with exactly this shape, and the filing service reads
  `Payslips`/`PayrollRuns` directly (not through the posting guard), so that coverage still matters.
  Fixed the test to seed the posted run/payslip directly via `AccountingDbContext`, bypassing
  `PayrollRunService` entirely (confirmed no immutability trigger exists on
  `payroll_runs`/`payslips` — grepped `SqlScripts` for `fn_enforce`). **Flagging for Fable/Ham:**
  arrears pay across a period boundary is no longer producible via the API/service at all — only
  pre-WP-5 historical data can have this shape.

  **Second consequence flagged, not fixed (needs a Fable/Ham call, not a worker call):**
  `payroll.pay_date_outside_period` has **no escape hatch** — unlike `payroll.period_closed`, which
  names the O14 reopen route, a run whose PayDate already exceeds its own period end structurally
  cannot post or pay, ever, after this ships. For the swarm's `209912`/`2099-12-31` junk that is the
  intended outcome. But it also means: **any run posted before WP-5 with an out-of-period PayDate
  is now permanently unpayable** (`PayAsync` will refuse it forever — there is no reopen for a
  structural date-outside-period violation). The co7 `209912` run is the known instance; whether
  any REAL tenant has a legitimate posted-but-unpaid run in this shape is unverified and worth a
  prod probe before deploy.

  **Stale spec comment flagged, not propagated:** §3.5's code sketch claims the periodEnd check
  "would also refuse it" for the 209912 case and that `IsOpenAsync` is redundant belt-and-braces —
  actually the reverse holds for that exact case: `periodEnd` for period `209912` is `2099-12-31`,
  which EQUALS the reported PayDate, so `PayDate > periodEnd` is FALSE there and it is `IsOpenAsync`
  alone that refuses it. My implementation's own doc-comment states this correctly; the spec's §3.5
  prose does not — worth a fix at the next spec touch.

  **Evidence:** `dotnet build backend/Accounting.sln --no-restore -m:1 -p:BuildInParallel=false` →
  0 Warning(s), 0 Error(s). RED (guard reverted via `git stash`, new tests kept): T18/T19/T20 all
  3 failed, "Expected a DomainException to be thrown, but no exception was thrown." GREEN (guard
  restored + `OpenPeriodAsync` fixture fix): full `PayrollRunServiceTests` class — 34 passed, 0
  failed, 0 skipped, 2m59s. Full solution suite deliberately NOT run (Fable's gate, per dispatch).
  No commit made.

- **2026-08-12 sonnet-implementer: WP-5 ROUND 2 — §3.5 amendment implemented (period-END ceiling
  removed, commit `97ace1c`).** Fable reversed the ceiling based on exactly the two things ROUND 1's
  report flagged: it made arrears pay (a December-period run paid 5 ม.ค.) unproducible through the
  service, and it caught nothing the `IsOpenAsync` check didn't already catch for the `209912` case.
  Fault stated as Fable's own recommendation, not the implementer's — the ceiling was reproduced
  exactly as ROUND 1 spec'd it and the report's own deviation section is what triggered the reversal.

  **Code change** (`PayrollRunService.EnsurePostablePayDateAsync`): the `PayDate > periodEnd` ceiling
  is gone. Replaced with a floor (`PayDate < periodStart` → `payroll.pay_date_outside_period`,
  message rewritten for "before the period starts" instead of "outside the period") plus the
  unchanged `IsOpenAsync(PayDate.Year, PayDate.Month)` check (`payroll.period_closed`, still names
  the O14 reopen route). Chose to KEEP `payroll.pay_date_outside_period` for the floor rather than
  collapsing to one code — a payment dated before its own period begins is a distinct, meaningful
  violation (almost certainly a data-entry error) from a legitimately-timed payment landing in a
  closed or never-opened month, and the two now have zero message/meaning overlap.

  **Test changes:**
  - `Pnd1_filings_follow_payment_date_not_period` reverted to drive the REAL service again
    (`RunThroughPost(sp, Period(y, 12), payDate: new DateOnly(y + 1, 1, 5))`), no seeded bypass — it
    needed no further workaround after the ceiling's removal, confirming Fable's prediction. Removed
    the now-unused `Accounting.Domain.Entities.Payroll` using (no more direct `PayrollRun`/`Payslip`
    entity construction in the file).
  - T19 rewritten and renamed
    (`T19_pay_date_before_period_start_is_refused_arrears_and_pre_payday_pay_both_work`): (a) NEW —
    PayDate one day before the run's own period starts → `payroll.pay_date_outside_period`, no JE
    (the floor). (b) KEPT, reworded — the generalised `209912` case still refuses via
    `payroll.period_closed` (PayDate == periodEnd is no longer even a distinguishing feature since
    there is no ceiling; it refuses purely because the period was never opened). (c) KEPT unchanged
    — the pre-payday regression (post today, pay at period end, current real month). (d) NEW —
    arrears pay WORKS: a December-period run with PayDate 5 ม.ค. of the following year both POSTS
    and PAYS successfully once that January is opened (`OpenPeriodAsync(spD, arrearsPayDate.Year,
    arrearsPayDate.Month)` — opens PayDate's own month, which is what `EnsurePostablePayDateAsync`
    actually checks; the run's nominal December period is never itself checked for openness).
  - T18 and T20 unchanged — neither exercised the ceiling (all their PayDates already sit inside
    their own period), confirmed by re-reading both before leaving them alone, per the coordinator's
    explicit "keep as they are."
  - `OpenPeriodAsync` helper and its wiring into `RunThroughPost` + the 5 direct call sites are
    unchanged — that mechanical work was correct under either rule and needed no rework.

  **Deviations from ROUND 1 now resolved:** the "no escape hatch" consequence (ROUND 1's second
  flag) disappears entirely — there is no longer a PayDate shape that is structurally unpayable
  forever; every refusal now has either the O14 reopen route or a same-day date correction as its
  way out. The "arrears pay impossible" capability change (ROUND 1's first flag) is reversed.

  **Verification under the TEST-DB HOLD** (WP-4 worker owns `teas_test` — no `dotnet test` run):
  isolated `-o` build of `Accounting.Infrastructure.csproj` alone → 0 Warning(s), 0 Error(s).
  Isolated `-o` build of the full `Accounting.Api.Tests.csproj` (pulls in every project including
  WP-4's concurrently-edited `Accounting.Infrastructure`) → first attempt hit a transient CS7036 in
  `ExpenseClaimService.cs` (WP-4's own in-flight edit, not mine — confirmed via `git diff --stat`,
  zero WP-5 files involved); retried ~5s later → 0 Warning(s), 0 Error(s), confirming it was a
  live-edit race, not a real defect. Not committed.

- **2026-08-12 sonnet-implementer: WP-5 ROUND 2 — ALL-CLEAR received, DB evidence complete.**
  `teas_test` freed (WP-4 committed). Ran the full RED→GREEN cycle for the amended (floor +
  `IsOpenAsync` only, no ceiling) rule.

  **RED** (`git stash push -- PayrollRunService.cs` — reverts to pre-WP-5 HEAD, zero guard at all;
  test file untouched): T18/T19/T20 filtered run → **3 failed, 0 passed**, all three
  `"Expected a DomainException to be thrown, but no exception was thrown"` at their first refusal
  assertion (T19 fails at sub-case (a), the new floor check). `git stash pop` restored the guard;
  `git stash list` confirmed **empty** afterward.

  **GREEN**, first pass: T18/T19/T20 filtered run → 2 passed, 1 failed. T19 failed at its NEW
  sub-case (d) (arrears pay) with `payroll.bank_required`, thrown from `PayAsync`'s bank-resolution
  branch, NOT from `EnsurePostablePayDateAsync` — the period/pay-date guard itself raised no
  exception, confirming it worked. Root cause: sub-case (d) used the shared `Provider()` (company
  1), which has accumulated multiple active `BankAccount` rows across the whole suite's history
  (every `Pay_posts_wages_payable_to_selected_bank_and_blocks_double_pay`-shaped test leaves one
  behind, permanently, in the shared `teas_test`); `PayAsync(BankAccountId: null)` then hits
  `activeBanks.Count > 1` → refuses. This is a fixture-design bug in my OWN new test (not a
  "pre-existing failure unrelated to my change" needing the throwaway-worktree protocol — the
  existing precedent for this exact hazard is already in the file,
  `Pay_without_any_active_bank_credits_cash_1110`, which temporarily deactivates every bank first).
  Fixed by switching sub-case (d) to a **fresh** `TestCompanyFactory` company (zero bank accounts
  by construction, mirroring sub-case (c)'s existing pattern) instead of `Provider()`.

  **GREEN**, second pass: T18/T19/T20 filtered run → **3 passed, 0 failed, 0 skipped**, 5s. All
  four T19 legs proven: (a) floor violation refused (`payroll.pay_date_outside_period`, no JE);
  (b) the generalised 209912 case still refused via `IsOpenAsync` alone (`payroll.period_closed`);
  (c) pre-payday regression posts normally; (d) **arrears pay works** — a December-period run with
  PayDate 5 ม.ค. of the following year POSTS and PAYS successfully once January is open.

  **Full class**: `PayrollRunServiceTests` → **34 passed, 0 failed, 0 skipped**, 3m9s.

  **`Pnd1_filings_follow_payment_date_not_period` explicitly confirmed** — isolated single-test
  run → **1 passed, 0 failed**, 22s, driving the real service end to end
  (`RunThroughPost(sp, Period(y, 12), payDate: new DateOnly(y + 1, 1, 5))`; grepped the test body to
  confirm zero `db.PayrollRuns.Add(...)` seeding remains). This is the proof the period-END ceiling
  — not anything else — was what broke arrears pay: same test, same assertions, now driving
  `PostAsync`/the real posting path instead of a seeded bypass, and it passes.

  Not committed. Awaiting Fable's consolidated full-suite run + diff review before commit.

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


---

## WP-6 (NEW, added 2026-08-12) — legacy sub-satang data: audit before deploy, remediate before the guard ships

**Why this exists.** Opus's WP-3 review surfaced a class the spec had not priced: the precision guard is
correct, but it converts *silently wrong* data into a **hard dead-end** for any company that already holds
>2dp values. Three lifecycle operations refuse, with no in-app way out, and the error tells the user to do
something impossible:

| Operation | Where it breaks | Why the advice "restate in satang" is impossible |
|---|---|---|
| **Year-end close / reopen** | `YearCloseService.cs:118-122` sums `DebitAmount`/`CreditAmount` over **already-posted** lines; `:208` rebuilds the reversal from the stored closing entry | the offending lines are immutable posted history |
| **Pay a posted payroll run** | `PayrollRunService.cs:265` posts the **stored** `run.TotalNet` | a posted run is not editable |
| **WP-2 backfill `apply`** | `NonVatArBackfillService.cs:284-287` posts `item.Outstanding`, derived from pre-fix billing-note totals | the source documents are posted |

All four proven pollution paths (ExpenseClaim / PV / VI line amounts, TaxInvoice gross) write **expense or
revenue** lines — exactly what year-close aggregates. So "company with legacy 4dp lines" ≈ "company that can
no longer close its fiscal year". co5 (`822801.785`) and co7 (`544060.031`) are known-polluted; **Repttown
uses all four paths and must be assumed polluted until the audit says otherwise.**

### WP-6.1 — Audit (READ-ONLY, runs on prod BEFORE the R1 deploy)
Report, per company, every row where `Round(x,2) != x`:
- `gl.journal_lines.debit_amount` / `credit_amount` (and the parent entry's date + doc no)
- `payroll_runs.total_*` for runs in `Posted` (not yet paid) — these are the ones that will strand
- `employees.base_salary`
- the document line tables the four paths write (expense-claim lines, PV lines, VI lines, tax-invoice lines)

Output must be per-company counts + the actual rows, so the blast radius is a number before anything ships.
**This is a Fable-run operation, not a worker dispatch** — it reads a live tenant's ledger.

### WP-6.2 — Remediation (design AFTER the audit returns real numbers)
Do not design this in the abstract. The shape depends on what the audit finds:
- **Zero rows on Repttown** → ship R1 as-is; co5/co7 are fixed by the already-planned wipe+reseed.
- **Posted payroll runs stranded** → they need a way to be paid. Options: a one-off correcting entry, or a
  narrowly-scoped allowance on the pay path for pre-existing runs. **Not decided here.**
- **Polluted revenue/expense ledger lines** → year-close is blocked. A correcting JV cannot fix it (that JV
  would itself need sub-satang). Likely needs an explicit, audited data correction posting the rounding
  difference — a money decision that goes to Ham and the CPA, not an engineering call.

### Release gate
**R1 must not deploy until WP-6.1 has run against prod and its result is read.** Shipping the guard onto a
polluted live tenant would strand its year-end close with no remedy — strictly worse than the bug being fixed.

---

## WP-6.1 AUDIT RESULT — run on prod 2026-08-12. **DEPLOY GATE SATISFIED.**

Read-only queries against the live `teas` database (OVH VPS, via the `repttown_deploy` key). Nothing written.

### Sub-satang pollution — the R1 deploy blocker

| source | co2 (Repttown) | co3 | co5 | co7 |
|---|---|---|---|---|
| `gl.journal_lines` >2dp | **0** | **0** | 5 | 8 |
| `payroll_runs` totals >2dp | **0** | **0** | 0 | 1 |
| `master.employees.base_salary` >2dp | **0** | **0** | 0 | 0 |
| `expense_claim_lines.amount` >2dp | **0** | **0** | 1 | 2 |

**Both real tenants are clean.** Every polluted row is on co5/co7, which are already scheduled for
wipe+reseed. **R1 can deploy without stranding anyone's year-end close, payroll payment or backfill.**
The dead-end class WP-6 exists to prevent does not exist in live data.

### C6 backfill scope on the real tenants — and the tax answer

Both co2 and co3 are `vat_registered = false` (so C6 applies to both, not just Repttown) and both have
`fiscal_year_start_month = 1`.

| company | outstanding invoices | amount | issued |
|---|---|---|---|
| co2 | 1 (`07-2026-IV-LAB-0001`) | ฿8,400.00 | 2026-07-12 |
| co3 | 1 (`08-2026-IV-0001`) | ฿15,400.00 | 2026-08-08 |

Every *settled* invoice on both tenants was receipted in the **same calendar year it was issued**
(earliest billing note anywhere is 2026-06), so no prior year ever had revenue deferred out of it.

**→ NO PRIOR FISCAL YEAR WAS UNDERSTATED. No amended ภ.ง.ด.50 is required, and the 1.5%/month
เงินเพิ่ม exposure is ZERO.** This corrects the standing assumption in `PLAN-fix-breakit-v1271.md` and
`specs/research-thai-prior-period-correction.md`, which were written before the data was measured and
prudently assumed exposure. The research itself remains valid and worth keeping — it is simply not
triggered.

**Consequence for WP-2:** on real data the backfill posts **two entries, both crediting Revenue in the
current open fiscal year**. The retained-earnings branch is never exercised on a live tenant. It stays in
the code because co5/co7 and any future tenant can still need it, but the Repttown "apply" run is a
two-invoice, ฿23,800 operation — not the archaeology we had budgeted for.

### Method notes (for whoever re-runs this)
- Prod DB is **`teas`**, not the `accounting_dev` name in the deployed `appsettings` (that value is stale).
- Real schemas differ from the guesses in `tools/audit-subsatang.sql`: employees live in **`master`**, not
  `payroll`; `payroll_runs` has `total_gross_taxable`/`total_gross_non_taxable`, not `total_gross`.
- **Status values are UPPERCASE** (`ISSUED`/`SETTLED`/`POSTED`). A title-case comparison silently returns
  zero rows — it briefly made this audit report "no outstanding invoices anywhere", which was wrong.
  Use `upper(status)`. `tools/audit-subsatang.sql` should be corrected before its next use.

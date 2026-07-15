# Fix round — Codex cross-family review of v1.14.0..v1.17.0 (2026-07-10)

Codex session 019f4b68-5d7a-7702-ba0c-e8c2aa027b5e reviewed all four shipped
cycles. 11 findings; Fable triage: 9 accepted (below), 2 deferred to Ham
(#4 activation-after-run — previously accepted as financially-neutral design;
#7 manual-confirm date window — intentional human escape hatch).

Sequencing: implementation must NOT start until the MCP-expansion-v2 worker
finishes (shared tree + test DB). §1 needs the Opus design addendum first.

## Fixes (checklist)

- [x] **1. Bank rec report scoping (BLOCKING — design addendum required first).**
  Implemented exactly per §1 addendum below: injected `IOptions<GlAccountsOptions>`
  into `BankReconciliationReportService`, resolved `cashGlId`/`bankGlId` once per
  call via a standalone `ResolveAccountIdAsync` copy (mirrors FixedAssetService's
  own copy), added the attribution predicate to the deposits/outstanding queries
  (split into `(Cash && cashAttributesHere) || (!Cash && bankAttributesHere)` —
  avoids a ternary-in-SQL translation risk, same truth table as the addendum's
  ternary), added `DocReconciliationLimited` to the DTO. Amounts UNCHANGED, tie-out
  formula structure UNCHANGED — only the SET narrows, per constraint. Evidence:
  `BankReconciliationReportServiceTests.TScope1And2_*` reproduces the addendum's
  worked example verbatim (GL=170, statement=50, deposits=100 RC2-only,
  outstanding=30, Difference=0); `TScope3_*` proves a non-primary account gets
  empty sets + `DocReconciliationLimited=true`; `TScope4_*` coupling-guard proves
  the report reads codes from `GlAccountsOptions` (injected non-default "9110"/
  "9120", not hardcoded) — all pass.
- [x] **2. ExpenseClaim line account override validation (BLOCKING).**
  Added `EnsureExpenseAccountAsync` (mirrors `BankReconciliationService
  .CreateJournalAsync`'s contra-account check: exists tenant-scoped, `IsActive`,
  `!IsHeader`), called only for a client OVERRIDE (`input.ExpenseAccountId`) in
  `BuildLinesAsync` — the category default stays trusted, per spec. Throws
  `expense_claim.expense_account_invalid`. Tests:
  `Line_account_override_pointing_at_an_inactive_account_is_rejected`,
  `Line_account_override_pointing_at_a_header_account_is_rejected` — pass.
- [x] **3. Statement import bank mismatch guard (MAJOR).**
  `StatementImportService.ImportAsync` now loads the bank account entity (not
  just an `AnyAsync` bool) and compares `DigitsOnly(parsed.AccountNoRaw)` vs
  `DigitsOnly(bankAccount.AccountNo)` immediately after `BankStatementIntegrity
  .Validate` and BEFORE any transaction/attachment/db write; mismatch throws
  `bank.statement_account_mismatch`. Test:
  `ImportAsync_rejects_a_statement_parsed_from_the_wrong_bank_account` (asserts
  code + zero rows persisted) — passes.
- [x] **4. Match-target uniqueness (MAJOR).**
  New EF migration `MatchTargetUniqueness` (only new migration this round) adds
  2 partial unique indexes on `bank.statement_lines.matched_receipt_id` /
  `.matched_payment_voucher_id` (both `WHERE ... IS NOT NULL`) — the spec's
  finding text said `statement_transactions` but the real table (per
  `StatementLineConfiguration`) is `statement_lines`; used the real table.
  `ConfirmMatchAsync`'s `ExecuteUpdateAsync` now wrapped in try/catch for BOTH
  a raw `Npgsql.PostgresException{SqlState:"23505"}` and a wrapped
  `DbUpdateException` (ExecuteUpdateAsync bypasses the SaveChanges pipeline, so
  the raw-exception shape wasn't 100% certain without an EF-internals deep-dive —
  catching both is the defensive, low-risk choice) → `bank.doc_already_matched`
  (reused the EXISTING code, already used by the pre-check). Test:
  `Concurrent_confirm_match_of_two_lines_to_the_same_receipt_never_double_matches`
  (real `Task.Run` race, mirrors the codebase's own established
  `Double_pay_race` pattern — exactly one winner, DB has exactly 1 matched row,
  regardless of which of the two guards catches the loser — matches the
  spec's own "409/23505" phrasing) — passes.
- [x] **5. FA account override validation (MAJOR).**
  `EnsureAccountAsync` now takes an `expectedType: AccountType` param and checks
  exists tenant-scoped + `IsActive` + `!IsHeader` + `AccountType == expectedType`
  (cost→Asset, accum→Asset, dep expense→Expense, wired in
  `ResolveAssetAccountsAsync`); throws `fixed_asset.account_invalid`. Tests:
  `DepExpenseAccountId_override_of_the_wrong_type_Asset_not_Expense_is_rejected`,
  `AssetCostAccountId_override_pointing_at_a_header_account_is_rejected` — pass.
- [x] **5b. FA VendorInvoiceId validation (Fable finding at MCP diff review).**
  Added `EnsureVendorInvoiceAsync` (company-scoped `AnyAsync` when non-null),
  called in both `CreateDraftAsync` and `UpdateDraftAsync` before
  `ResolveAssetAccountsAsync`; throws `fixed_asset.vendor_invoice_invalid`. This
  makes the MCP `create_fixed_asset_draft` Description ("the service applies its
  own check") literally true. Tests:
  `VendorInvoiceId_pointing_at_a_nonexistent_row_is_rejected`,
  `VendorInvoiceId_from_another_company_is_rejected_tenant_scoped` — pass.
- [x] **6. Draft-edit concurrency (MAJOR, both services).**
  Both `FixedAssetService.UpdateDraftAsync` and `ExpenseClaimService
  .UpdateDraftAsync` now do `Version++` immediately before save and call
  `SaveGuardedAsync` (which was already private-defined and used by every OTHER
  transition in both services) instead of a bare `db.SaveChangesAsync`. Tests
  (deterministic two-preloaded-context technique, mirrors each service's own
  existing `Concurrent_Approve_*`/`Concurrent_*` proofs — NOT a flaky
  `Task.WhenAll`):
  `ExpenseClaimServiceTests.Concurrent_UpdateDraft_second_stale_save_throws_DbUpdateConcurrencyException`,
  `FixedAssetServiceTests.Concurrent_UpdateDraft_second_stale_save_throws_DbUpdateConcurrencyException`
  — pass.
- [x] **7. CSV formula-injection hardening (MAJOR).**
  Added one shared `internal static string ReportEndpoints.CsvCell(string?)` —
  prefixes a field starting with `=`,`+`,`-`,`@`, tab, or CR with `'` before
  RFC-4180 quoting. Applied to BOTH backend CSV exports that actually exist in
  this file (`ar-aging/export` and `general-ledger/export`'s csv branch — the
  file's own pre-existing comment confirms these are the two real backend CSV
  writers; `ap-aging`'s CSV is FE-only client-side Blob download per that same
  comment, so per the finding's own "if it shares the writer" conditional it is
  OUT of scope — FE stayed untouched, matching the verification gates). Tests:
  `ReportEndpointsCsvCellTests` (8 cases: all 6 trigger chars, doubled-quote
  interaction, null, normal text unaffected) — pass.
- [x] **8. RFC4180 reader strictness (MINOR).**
  `Rfc4180Reader.ReadAll` now: (a) throws `DomainException("bank.csv_malformed")`
  on EOF while `inQuotes`; (b) rejects a `"` opening when the current field
  already has content OR right after a just-closed quote (`quoteJustClosed`
  flag) — "mid-field" case; (c) rejects any unquoted char immediately following
  a field's closing quote before the next delimiter/newline — "trailing junk"
  case. All existing KBiz fixture tests (metadata/lines/direction/amount/CE-date/
  integrity) still pass unchanged — no regression. New tests:
  `Parse_rejects_unterminated_quoted_field_eof_while_in_quotes`,
  `Parse_rejects_quote_opening_mid_field`,
  `Parse_rejects_trailing_junk_after_closing_quote` — pass.
- [x] **9. Tests per fix** — 24 new tests added this round (see items 1–8 above
  + T-scope-1..4), all passing; full suite evidence below.

## Verification gates
- [x] Full suite green, skip == 8 baseline. Baseline (before this round):
  Domain.Tests 147/147, Api.Tests 786 total/778 pass/8 skip/0 fail — grand
  total 933/8 skip (exactly matches the spec's expected pre-MCP-v2-inclusive
  baseline). AFTER this round: Domain.Tests 147/147, Api.Tests 810 total/802
  pass/8 skip/0 fail — grand total 957/8 skip (+24 new tests, 0 regressions,
  skip count unchanged). `dotnet build Accounting.sln` — 0 errors/0 warnings.
- [x] No change to any pinned money formula without a worked example in this
  file — the §1 addendum's re-derived worked example (GL=170, statement=50,
  deposits=100, outstanding=30, Difference=0) is reproduced verbatim by
  `TScope1And2_*` and passes; the tie-out formula STRUCTURE
  (`statement - (GL - deposits + outstanding + unmatched)`) is byte-for-byte
  unchanged — only the deposits/outstanding SET narrowed via the new
  attribution predicate, per the addendum's explicit constraint.

## Attempt log
- 2026-07-10 Fable: triage of Codex findings; #4/#7 deferred to Ham with
  rationale; #1 sent to Opus for design addendum.
- 2026-07-10 implementer: baseline established — `dotnet test Accounting.sln`
  = Domain.Tests 147/147 pass, Api.Tests 786 total/778 pass/8 skip (0 fail on
  re-run; one flaky pre-existing failure on WhtFormPdfFillTests.Pnd54_renders_
  one_sheet_per_ma70_payment on the FIRST run only, self-resolved on re-run —
  unrelated to this task, a relative-date test generating an absurd year
  8217, not touched). Grand total 933/8 skip, matches spec's expected
  baseline exactly. Starting implementation of all 10 items now.
- 2026-07-10 implementer: all 10 items implemented in one pass (no retries
  needed). `dotnet ef migrations add MatchTargetUniqueness` generated cleanly
  (never-applied migration, no `ef remove`+`add` collision per troubles-wiki —
  N/A here). `dotnet build Accounting.sln` 0/0 after every batch of edits.
  Full suite: 957/8 skip/0 fail (+24 tests, 0 regressions). Deviation from the
  literal finding text: #4's table name in the spec (`statement_transactions`)
  doesn't exist — used the real table `bank.statement_lines`. #7's "ap-aging"
  export doesn't exist on the backend (confirmed FE-only via the file's own
  pre-existing comment) — applied the shared helper to the two REAL backend
  CSV exports instead (ar-aging + general-ledger), consistent with the
  finding's own "if it shares the writer" conditional and the "FE untouched"
  gate. Blast radius honored: only the files named in the findings + the one
  migration + tests touched (see `git status` — 10 src files, 1 new migration
  pair, 7 test files). SelfWithholdMode/TotalPaid and #4 activation-after-run/
  #7 date-window items untouched, as instructed.

---

## §1 design addendum (Opus, 2026-07-10)

### What the docs actually carry (evidence)
- `Receipt.cs:28` `PaymentMethod PaymentMethod` + `:31` `long? BankAccountId`;
  `PaymentVoucher.cs:39` `PaymentMethod` + `:42` `long? BankAccountId`.
  Enum `PaymentMethod.cs` = `{Cash, Transfer, Cheque, CreditCard, Other}`.
- **`BankAccountId` on both docs is written at entry but IGNORED by posting**
  (bank-rec spec Scope-reality #1, verified). The cash side is resolved SOLELY
  by method: `GlPostingService.cs:75` (RC) / `:156` (PV) —
  `debitCode = PaymentMethod == Cash ? _accounts.CashAccount : _accounts.BankAccount`,
  i.e. **`1110` (Cash) for cash-method, `1120` (Bank) for every other method**
  (`GlAccountsOptions.cs:12-13`). No line ever carries `BankAccountId`.
- `JournalEntry.cs:23` has ONLY `string? Reference` (= source DocNo). There is
  **no structured source-doc FK** — a doc→JE link would be a fragile DocNo
  string-join (cross-doc-type collision risk). Code→id resolver =
  `db.ChartOfAccounts.First(a => a.CompanyId==cid && a.AccountCode==code)`
  (`GlPostingService.cs:528-538`).

### Attribution model chosen — replicate the posting rule (no schema change)
A doc is a reconciling item for the reconciled bank account **iff the GL cash
account it POSTED to == `bankAccount.GlCashAccountId`**. That posted account is
`PaymentMethod == Cash ? cashGlId(1110) : bankGlId(1120)` — the exact rule at
`GlPostingService.cs:75/156`. Resolve `cashGlId`/`bankGlId` ONCE per report via
the CoA resolver above (inject `IOptions<GlAccountsOptions>` into
`BankReconciliationReportService` for the `"1110"`/`"1120"` codes — do NOT
hardcode). This is the cheapest model that is provably GL-consistent.

**Rejected — JE-line attribution (option c):** needs the doc→JE link, which is
only the `Reference` string (no FK) → fragile + extra reads; no cheaper than
replicating the 2-line posting rule. It IS the correct phase-2 target once
per-bank posting lands. **Rejected — split by doc `BankAccountId` (option b):
actively HARMFUL in v1.** Posting commingles every non-cash doc into the single
`1120` account, so `GL_1120` already contains Bank B's movements; filtering the
reconciling SET by `BankAccountId` while the GL side stays commingled BREAKS the
tie-out (difference goes nonzero on a fully-reconciled account). The GL cannot be
split by bank in v1, so neither can the items. This is exactly bank-rec D6's
documented single-`1120` limitation, not a new bug.

### Exact new query semantics (deposits-in-transit (b) AND outstanding (c))
Keep the existing shape (posted, `DocDate <= to`, `!matchedIds.Contains`); ADD the
attribution predicate. `bankAccount.GlCashAccountId`, `cashGlId`, `bankGlId` are all
scalars known before the query, so it stays SQL-translatable:
```csharp
// each doc hit cashGlId (cash-method) or bankGlId (all other methods); keep only
// the docs that hit THIS bank's GL cash account. Mirrors GlPostingService.cs:75/156.
.Where(r => (r.PaymentMethod == PaymentMethod.Cash ? cashGlId : bankGlId)
             == bankAccount.GlCashAccountId)
```
Same clause on the PV query (`p.PaymentMethod`). For the primary `1120`-mapped
account this reduces to "exclude cash-method"; for a bank whose `GlCashAccountId`
is neither `1110` nor `1120` the set is EMPTY (nothing posted there in v1).
Amounts UNCHANGED: `r.CashReceived` == the `1120` debit, `p.TotalPaid` == the
`1120` credit (already correct — only the SET narrows).

**Layer (d) — honesty flag, no silent bad tie-out.** Add
`bool DocReconciliationLimited` to the `BankReconciliationReport` DTO, set `true`
when `bankAccount.GlCashAccountId != bankGlId` (a non-primary account: its doc set
is empty AND `GL` of its sub-account excludes the transfers that went to `1120`,
so `Difference` is expected-nonzero). FE shows a note ("v1 reconciles the shared
`1120` account; per-bank doc reconciliation is phase-2"). Never present a silent
broken tie-out as "unreconciled."

### Re-derived worked example (formula STRUCTURE unchanged — only the SET narrows)
One bank acct, `GlCashAccountId = 1120`. Posted: RC1 Transfer 100 (matched/cleared);
RC2 Transfer 100 (in transit); **RC3 Cash 40** (posts `1110`, unmatched);
PV1 Transfer 30 (outstanding); bank-fee statement line MoneyOut 50 (unmatched).
- `GL_1120` = Dr(RC1 100 + RC2 100) − Cr(PV1 30) = **170** (RC3 hit `1110`, excluded).
- statement closing = 0 + 100(RC1) − 50(fee) = **50**.
- NEW sets: deposits = RC2 100 (RC3 dropped ✓); outstanding = PV1 30; unmatched = −50.
- `expected = GL − deposits + outstanding + unmatched = 170 − 100 + 30 − 50 = 50`.
- `difference = 50 − 50 = `**`0`** ✓ (pinned formula `bank-reconciliation.md` L365-377
  intact). OLD code included RC3 → deposits 140 → expected 10 → difference **40** (a
  false diff exactly = RC3) — this is the bug, and the new SET zeroes it.

### Tests (fold into checklist #9)
- **T-scope-1** multi-doc fresh-DB: RC3 cash-method posted+unmatched ⇒ NOT in
  deposits, `Difference == 0` (the finding's failing scenario; use relative dates
  per memory `relative-date-seed-temporal-tests`).
- **T-scope-2** transfer RC ⇒ IS counted (guards over-filtering).
- **T-scope-3** bank acct mapped to a non-`1120` sub-account ⇒ deposits/outstanding
  empty AND `DocReconciliationLimited == true`.
- **T-scope-4** coupling guard: an xUnit assert that the report's cash/bank codes
  come from `GlAccountsOptions` (same source as posting) — fails loudly if posting's
  rule and the report's replica ever diverge.

### Open questions / out-of-scope (flag to Ham, do NOT fix here)
- **SelfWithholdMode PV** (`PaymentVoucher.cs:61`): actual cash out = subtotal+vat,
  but `TotalPaid = subtotal+vat−wht`. The outstanding AMOUNT (and D4 matching) use
  `TotalPaid`; if the `1120` credit line ≠ `TotalPaid` under gross-up, that is a
  PRE-EXISTING amount bug orthogonal to this SET fix — leave untouched, raise
  separately.
- Blast radius honored: `BankReconciliationReportService.cs` + its DTO + tests.
  NO migration, NO change to Receipt/PV posting. Hitting either = STOP and re-spec.

## MANDATORY pre-deploy gate (Tier-2 finding 1, 2026-07-10)
Before deploying the MatchTargetUniqueness migration to prod, run BOTH dedup
probes over SSH; ANY row returned = resolve manually BEFORE deploy (the index
build would otherwise crash API startup entirely):
  SELECT matched_receipt_id, count(*) FROM bank.statement_lines
    WHERE matched_receipt_id IS NOT NULL GROUP BY 1 HAVING count(*)>1;
  SELECT matched_payment_voucher_id, count(*) FROM bank.statement_lines
    WHERE matched_payment_voucher_id IS NOT NULL GROUP BY 1 HAVING count(*)>1;
Watch items (non-blocking): empty AccountNoRaw fails-closed on future adapters;
ExpenseClaim mark-paid path bare save (pre-existing, future concurrency pass).

## SelfWithholdMode PV investigation (2026-07-10)

**VERDICT: NOT A BUG.** The GL cash/bank credit line == actual cash out in both
WHT payer modes. The suspicion's premise (`TotalPaid = subtotal+vat−wht` always)
is false — that formula is only the DEDUCT branch.

Root: `TotalPaid` is computed CONDITIONALLY on the payer mode at
`PaymentVoucherService.cs:219-221`:
`totalPaid = selfWithhold ? subtotal+vatTotal : subtotal+vatTotal−whtTotal`.
Both `selfWithhold` (:215) and `totalPaid` (:219) derive from the same
`payerMode` (:151), so they cannot desync at draft time. The GL cash line credits
`pv.TotalPaid` (`GlPostingService.cs:220`); bank-rec matches on `p.TotalPaid`
(`BankReconciliationService.cs:83` suggest, `:141` confirm-guard). All three read
the SAME field → self-consistent, no drift.

Worked example — subtotal 1,000, VAT 7% (70), WHT 3%, recoverable VAT:

*DEDUCT (normal withhold):* WHT=30 (`WhtPayerModes.Compute` :59). `TotalPaid`=1,040.
JE: Dr expense 1,000 (`GlPostingService.cs:182`) + Dr input-VAT 70 (:190) =1,070;
Cr WHT-payable 30 (:214) + Cr bank 1,040 (:220) =1,070. Vendor receives 1,040;
cash out 1,040 = cash line. ✔

*GROSS_UP_FOREVER (self-withhold):* income=1000/0.97=1,030.93, WHT=30.93
(`WhtPayerModes.cs:47-48`). `selfWithhold`→`TotalPaid`=1,070. JE: Dr expense 1,000
(:182) + Dr input-VAT 70 (:190) + Dr gross-up 30.93 (`GlPostingService.cs:205`)
=1,100.93; Cr WHT-payable 30.93 (:214) + Cr bank 1,070 (:220) =1,100.93. Vendor
receives 1,070 (paid full); cash out 1,070 = cash line. ✔

Dr==Cr does NOT mask a wrong split: the gross-up debit (:205) and WHT-payable
credit (:214) are both `pv.WhtAmount` and cancel exactly, so balance reduces to
Dr(subtotal+vat)==Cr(TotalPaid=subtotal+vat) — no WHT-rounding path can leave it
balanced-but-wrong. Any wrong `TotalPaid` here would throw at `BuildAndPostAsync`,
not drift silently.

Doc smell only (no code change): `PaymentVoucher.cs:52` comment states
`= subtotal+vat−wht` as if universal — true only for DEDUCT. `GlPostingService.cs`
:198 correctly notes `TotalPaid = subtotal+vat` under self-withhold. No fix
required; optionally tighten the :52 comment to name both branches. Not chased:
a draft-UPDATE recompute path (out of scope) — if one exists it must reuse the
same :219-221 conditional.

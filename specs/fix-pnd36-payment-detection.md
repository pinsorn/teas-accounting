# R3/F1 — ภ.พ.36 must not silently omit a payment made outside a Payment Voucher

<!-- Living document. The worker updates the checklist as it works; a retry uses THIS file and
     grows the attempt log — never rewrite the spec for a retry. -->

## 0. Headline

ภ.พ.36 sources its rows from posted PaymentVouchers only (`WhtFilingService.cs:264-270`, shipped
`1e46a35`). That is the correct tax rule — ม.83/6's liability arises on **payment** — but nothing in
the system forces a payment to go through a PaymentVoucher. A manual JV of `Dr 2110 AP / Cr 1120 Bank`
clears a foreign vendor's payable with no PV in existence, and the reverse-charge VAT is then declared
**in no period, ever**.

**The single most important discovery: the obvious fix is impossible, and the second-most-obvious fix
is a lie.**

1. *Impossible:* you cannot attribute a `Dr 2110` journal line to a vendor invoice. `JournalLine`
   carries no vendor tag at all — the codebase states this itself in
   `Accounting.Application/Reports/SubledgerDtos.cs:5-7`. So "find the invoice this JV paid" has no
   answer. Any design that assumes per-invoice attribution is unbuildable without a schema change.
2. *A lie:* refusing manual JVs against AP would create a brand-new dead end for write-offs, opening
   balances and reclassifications — the exact failure mode this release already had to remediate
   (`cb2e362`, the payroll run that could not be posted, deleted or replaced). **Rejected outright.**

What IS computable, exactly and per period, is the **aggregate**: how much was debited to the AP
control account in the filing month, versus how much posted PaymentVouchers account for. Any positive
remainder means something cleared AP outside a payment voucher during the very month being filed. That
number is authoritative, netting-proof, and needs no attribution.

So the design is: **detect, surface, and require an explicit acknowledgement — never refuse.** The
filer sees the discrepancy on the return preview, in baht, with the journal entries to check, and
cannot finalize a return carrying an unexplained AP clearing without affirmatively signing off. The
sign-off is stored in the immutable filing record.

**Honest answer to "can a company still file a return that omits a real liability?" — YES, in three
ways, and the spec must not pretend otherwise:** (a) by ticking the acknowledgement anyway;
(b) via a payment that never touches AP at all (`Dr 5xxx expense / Cr 1120 bank`, no invoice — §2 row
C6); (c) via an employee expense claim reimbursing someone who paid the overseas provider personally
(§2 row C7). (b) and (c) are structurally invisible to any AP-based detector and are **out of scope
here** (§8), with a CPA question attached (§10 E2). What this change guarantees is stated precisely in
§4 I1 — and it is *not* "the return is complete".

**L1 (the filing period follows the PV's posting day, not the payment day) is REAL, is confirmed worse
than reported, and is deliberately NOT fixed here.** Its fix already exists as an approved design
(Feature B, `specs/doc-lifecycle-cancel-reissue-backdate.md` §2, Ham-binding §6 Q2) — but **that design
as written does not actually fix it**, and this spec's §11 says why and what one-line amendment it
needs. Read §11 before dispatching anything.

---

## 1. Facts established in code

Every claim below is `file:line`, read in source on 2026-08-13. Assumptions are flagged `ASSUMED`.

### 1.1 The filing query and its tax point (VERIFIED)

- `WhtFilingService.GeneratePnd36Async` — `backend/src/Accounting.Infrastructure/TaxFilings/WhtFilingService.cs:255-306`.
  Rows come from `db.PaymentVouchers` where `RequiresPnd36ReverseCharge && Status == Posted &&
  DocDate` in the month (`:264-270`). VAT = `SubtotalAmount × 0.07`, rounded 2dp (`:260`, `:275`).
- Finalize guard against re-finalize at `:288-291`; the reverse-charge JV is posted only when
  `totalVat > 0` (`:293-294`); `TaxFilingStore.FinalizeAsync` persists the whole `filing0` DTO to the
  immutable `tax.tax_filings` history (`:299-300`).
- The service already has `IOptions<GlAccountsOptions> glAccounts` injected (`:31`) and already
  resolves accounts by code straight off `db.ChartOfAccounts` (`:322-325`). **No new dependency is
  needed for this change.**
- `Pnd36Row` / `Pnd36Filing` — `backend/src/Accounting.Application/TaxFilings/TaxFilingDtos.cs:114-121`.
- Endpoint — `backend/src/Accounting.Api/Endpoints/TaxFilingEndpoints.cs:153-161`
  (`POST /tax-filings/pnd36?period=&mode=`, `RequireAuthorization(preview)`, finalize additionally
  gated by `GuardFinalizeAsync` → `tax.filing.finalize`).

### 1.2 The AP control account and who can move it (VERIFIED)

- `GlAccountsOptions.ApAccount = "2110"` — `backend/src/Accounting.Infrastructure/Ledger/GlAccountsOptions.cs:11`.
  It is a postable, non-header, active leaf by construction.
- Only two places resolve it from options: `GlPostingService.cs:222` (PV → **Dr**) and
  `GlPostingService.cs:389` (VI → **Cr**). Everything else that reaches 2110 does so via a raw,
  client-supplied `AccountId`.
- `coa.system_account` (`MasterDataServices.cs:203`) blocks *deactivating* / header-flipping an account
  named in `GlAccountsOptions.AllCodes()`. **It does not stop a journal line from debiting 2110.**
- **There is no control-account blocklist anywhere in the repo.** Verified by exhaustive grep for
  `control_account`, `blocklist`, `restricted`, `system_account`, `is_control`, `ControlAccount`. The
  only account-type restriction in the posting layer is `ExpenseAccountRule.IsAllowedClaimLineAccount`
  (`ExpenseClaimService.cs:72`), a positive allowlist scoped to expense-claim lines.
- `JournalService.ValidatePostableAccountsAsync` — `JournalService.cs:244-260`. The complete set of
  line-level guards is: exists + belongs to this tenant (`je.account_not_found`), `IsActive`
  (`je.account_inactive`), `!IsHeader` (`je.account_is_header`). Nothing else.

### 1.3 Every GL poster shares one engine — and one PrefixCode (VERIFIED, this killed a design option)

`GlPostingService.BuildAndPostAsync` (`:581-621`) is the single engine behind PV, VI, manual JV,
bank-rec, payroll and the ภ.พ.36 auto-JV. It stamps **`PrefixCode = JvPrefix` on every entry**
(`:604`). `PaymentVoucher` has **no** `JournalEntryId` column (verified: only `ExpenseClaim`,
`DepreciationRun`, `FixedAsset`, `BillingNote` carry one).

> **Consequence the implementer must internalise:** there is no reliable structural discriminator
> saying "this journal entry came from a PaymentVoucher". The PV's entry is identifiable only by
> convention — `Description = $"PV {pv.DocNo}"`, `Reference = pv.DocNo` (`GlPostingService.cs:299`) —
> which a manual JV could coincidentally or deliberately reproduce. This is why §3.3's **amount** is
> authoritative and its **list** is explicitly best-effort. Do not invert that.

Crucially, the PV's journal entry is dated `pv.DocDate` (`GlPostingService.cs:299` passes
`pv.DocDate` as the entry's `docDate`), which is what makes §3.3's two sides comparable at all.

### 1.4 The settlement column is a subledger fact, not a payment fact (VERIFIED)

`VendorInvoice.SettledAmount` / `SettlementStatus` are written in exactly one place in the whole
`backend/src` tree — `PaymentVoucherService.cs:647-649`. Every other hit is a read-only projection
(`VendorInvoiceService.Read.cs:28,58,109`, `ApAgingService.cs:43`). So an invoice paid by JV stays
`UNPAID` forever while the GL says the payable is gone, and nothing surfaces the divergence.

**This is the root cause of the whole defect** — recorded in `troubles-wiki.md` ("ภ.พ.36 blind spot"):
the original pre-check asked *"what writes `SettledAmount`?"* and correctly answered *"PaymentVoucher
only"*. But that is a question about the **AP subledger**, and ม.83/6 attaches to **payment**, a
strictly broader event. §2 is this spec's answer to the question that should have been asked.

### 1.5 The reverse-charge flag is server-derived and snapshot at draft (VERIFIED)

- `PaymentVoucher.RequiresPnd36ReverseCharge` — written once, `PaymentVoucherService.cs:359`, from
  `requiresPnd36 = autoSelfWithhold` (`:339`) where `autoSelfWithhold = vendor.IsForeign &&
  !vendor.HasThaiVatDReg` (`:223`). No request field, no checkbox, no override. Set at **draft**, never
  recomputed at post.
- `VendorInvoice.RequiresPnd36ReverseCharge` — written once, `VendorInvoiceService.cs:139`, identical
  derivation. Documented as informational-only (`VendorInvoice.cs:65`, `WhtFilingService.cs:250-253`).
- **Footgun — do NOT "helpfully" fix this.** A vendor whose `IsForeign` / `HasThaiVatDReg` flag is
  corrected *after* a draft exists leaves that draft's snapshot stale. Recomputing at post is a
  behaviour change with real filing consequences and has no spec. §3.4's detector reads the **document
  snapshot**, consistent with the filing query, never the vendor's current flag.

### 1.6 The existing tie-out report — reuse its reasoning, not its number (VERIFIED)

`SubledgerReportService.ApReconciliationAsync` (`:185-194`) already computes
`Difference = ControlAccountBalance − SubLedgerTotal` for account 2110 and is already surfaced on AP
aging (`ApAgingService.cs:76`), the vendor ledger, and MCP `get_vendor_ledger`. Its DTO comment
(`SubledgerDtos.cs:5-11`) states plainly that a non-zero Difference is *"a REAL finding (manual JEs
straight to the control account …)"* — i.e. the codebase already documents the exact channel this spec
defends against.

**But it is the wrong instrument here, for three reasons** (this is why §3.3 builds a new, narrower
query instead of calling it):
1. It is **as-of cumulative**, not period-scoped. A ฿10,000 JV posted in March keeps June's return
   flagged forever.
2. It **nets**. A manual credit to AP (a hand-booked accrual) cancels out a manual debit (a payment),
   masking exactly what must not be masked.
3. It yields **no line items**, so a filer who sees it cannot act on it.

### 1.7 L1 — the filing period follows the PV's POSTING day (VERIFIED; worse than reported)

`PaymentVoucherService.cs:496-498` **re-pins** `pv.DocDate = pv.PostingDate = _clock.TodayInBangkok()`
at POST, not merely at draft (`:181`). So the ภ.พ.36 period bucket follows the day the voucher was
*posted*. Pay the provider 30 June, post the voucher 3 July → declared in July's return (due 7 August)
instead of June's (due 7 July): **one month late.** Full analysis and recommendation in §11.

### 1.8 Environment and process footguns (fold in, do not rediscover)

- **`troubles-wiki.md` "ภ.พ.36 blind spot…"** — the defect this spec fixes. Read it.
- **`troubles-wiki.md` "A payroll run with a pay date before its own period start…"** — the
  three-reasonable-rules-compose-into-a-trap precedent. Every refusal in this spec must have a
  named exit; §3.5 states the one refusal added and its exit.
- **`troubles-wiki.md` "ภ.พ.36 reverse-charge JV lands on today … `CreateDraftAsync` silently
  discards its own `docDate`"** — still true (`JournalService.cs:50-51`). **Out of scope here**
  (§8); it moves the auto-JV's date, not the return's period.
- **`troubles-wiki.md` "Thai ม glyph pitfall"** — the Bengali letter at **U+09AE** looks nearly
  identical to Thai ม and creeps into legal citations. This spec and all new code comments / i18n
  strings write ม.83/6 with **Thai ม (U+0E21)**. A grep gate is in §7.
  *(Note: this spec deliberately contains **no** literal U+09AE character anywhere, so that the §7 G4
  gate cannot trip on the spec file itself — that is why G4 below matches by codepoint, not by a
  pasted glyph. Keep it that way when editing this file.)*
- **`TEAS_TEST_PG` dies between PowerShell calls** — set it in the SAME call that runs the tests, and
  compare the skip count to baseline. A skipped `[SkippableFact]` suite is a fake green run.
- **`teas_test` fixture applies each SQL seed ONCE** — new seeds cannot assume earlier ones replay.
  This change adds no seed.
- **Test DB is shared.** Do not run this work package's tests concurrently with any other dispatch
  that runs tests, including the Tier-3 gate runner.
- **Relative-date seeds vs temporal tests** — seed 400 closes the previous month against
  `CURRENT_DATE`. New tests must use today/future dates and vary the *query* period, never hardcode a
  past month. The tests in §6 follow the existing `RandPeriod()` / `PeriodDate()` helpers in
  `Sprint9WhtComplianceTests.cs`.

### 1.9 Verified live exposure — ZERO (do not re-probe; do not touch prod)

Probed on prod 2026-08-12/13 by the orchestrator: foreign reverse-charge vendor invoices exist **only
on co5** (4; one POSTED+UNPAID). **Neither real tenant has any.** **ภ.พ.36 has never been finalized for
any company.** There is no incident to remediate and no historical data to migrate.

This fact is load-bearing twice over, so it is stated as a reason and not as a reassurance:
- It is **why** the DTO may be widened freely — see §3.6.
- It is **why** this spec chooses the better design over the fastest one.

---

## 2. Consumer sweep — every way the EVENT "payment to an overseas service provider" can happen

The seam being reasoned about is not a column; it is the real-world event ม.83/6 keys on. Rows are
`file:line`-verified. **Disposition is explicit for every row — an unlisted channel is a shipped bug.**

| # | Channel (file:line) | Reaches 2110? | Cash out? | Vendor identity? | Declared today? | Disposition |
|---|---|---|---|---|---|---|
| C1 | **PV Post, VI-linked** — `PaymentVoucherService.cs:467` → `GlPostingService.cs:222-228` | **Dr** | Cr 1110/1120 | yes | **YES** | **Unchanged.** The correct path. |
| C2 | **PV Post, standalone (no VI)** — `GlPostingService.cs:247` else-branch | no AP line | Cr 1110/1120 | yes | **YES** | **Unchanged.** Contributes 0 to §3.3's expected-debits side, correctly — it never debited AP. |
| C3 | **Manual JV, create+post** — `JournalService.cs:151-212` → `GlPostingService.cs:569-580` | **any**, incl. Dr 2110 | any | **none** | **NO** ✗ | **THE HOLE.** Detected at filing time (§3.3). Advisory at post time (§3.5, WP-3). Never refused. |
| C4 | **JV draft → post** — `JournalService.cs:40-80` + `:82-115` | **any** | any | **none** | **NO** ✗ | **THE HOLE.** Same two mitigations. This is also the path an MCP-drafted entry reaches (C8) — the advisory must fire here too, not only on C3. |
| C5 | **Bank-rec inline JE** — `BankReconciliationService.cs:208-280` → `GlPostingService.cs:554-564`. `ContraAccountId` accepts any tenant-owned active non-header account (`:224-233`); 2110 passes. | **any** (contra) | Cr bank | **none** | **NO** ✗ | **Detected at filing time (§3.3) — it posts through the same `BuildAndPostAsync`, so the detector sees it identically. DELIBERATELY SKIPPED at source (§3.5):** the user is picking a contra account from a list against a real bank line, not hand-writing a JV; the at-source advisory would land in a different mental model for no extra safety. Reasoning recorded so the skip is reviewable. |
| C6 | **Manual JV / bank-rec that never touches AP** — `Dr 5xxx expense / Cr 1120 bank`, no VI, no PV | **no** | yes | none | **NO** ✗ | **NOT DETECTABLE by any AP-based design. OUT OF SCOPE (§8).** troubles-wiki entry required (WP-5). Structurally identical to C7. This is why §0 says the return is not guaranteed complete. |
| C7 | **Expense claim** — `ExpenseClaimService.cs:263-337` → `GlPostingService.cs:311-380` | **no** — structurally blocked: `IsAllowedClaimLineAccount` (`:72`) permits only `Expense`, or an `Asset` that is the category's own default; 2110 is `Liability` (`MasterDataServices.cs:425`). Re-validated at Submit `:239`, Approve `:248`, Pay `:293`. | yes | **employee, never a vendor** | **NO** ✗ | **OUT OF SCOPE (§8).** An employee paying an overseas SaaS on a personal card is a real ม.83/6 event with no vendor record and no AP movement. Whether the company is the ผู้จ่ายเงิน in that shape is **a CPA question, not a code decision → §10 E2.** troubles-wiki entry required (WP-5). |
| C8 | **MCP tools** — `TeasMcpTools.cs:1106` `create_manual_journal_draft` (scope `gl.journal.create`) | draft only | — | none | n/a | **Not an independent channel.** `ApiKeyService.EnforceMcpNoPostGuard` (`:153-163`) rejects any `mcp` key holding a `.post` scope, and `gl.journal.post` is excluded from `McpScopes.All` (`McpScopes.cs:28-31`). An agent-drafted `Dr 2110` must be posted by a human through **C4** — which is exactly why WP-3 covers `PostAsync` and not only `CreateAndPostManualAsync`. |
| C9 | **Payroll settlement** — `PayrollRunService.cs:253-270` | no (2170/1120/1110) | yes | no | n/a | **N/A.** Cannot reach AP or a vendor. |
| C10 | **Fixed-asset disposal / depreciation** — `FixedAssetService.cs:255-284`, `:391` | no | cash **in** | no | n/a | **N/A.** |
| C11 | **Year-end close / reopen** — `YearCloseService.cs:155`, `:211` | no — sweep filtered to `Revenue`/`Expense` (`:118`); 2110 is `Liability` | no | no | n/a | **N/A.** |
| C12 | **Non-VAT AR backfill** — `NonVatArBackfillService.cs:287` | no (1130/4000/3300) | no | no | n/a | **N/A.** |
| C13 | **ภ.พ.36 finalize auto-JV** — `WhtFilingService.cs:314-345` | no (1170 or 5350 / 2151) | no | no | n/a | **N/A.** Not a vendor payment. Must not be picked up by §3.3 — it never debits 2110, so it isn't. |
| C14 | **Statement import** — `StatementImportService.cs:24-151` | no GL at all | no | no | n/a | **N/A.** Inserts only `StatementImport` + `StatementLine`. JEs arise later, via C5. |
| C15 | **Vendor credit note / debit note / purchase return / AP write-off / petty cash / vendor prepayment / AR↔AP netting** | — | — | — | — | **DO NOT EXIST.** Verified absent: `TaxAdjustmentNoteService` + `GlPostingService.PostTaxAdjustmentNoteAsync` (`:428-464`) are AR-side only; the only "write-off" in the repo is fixed-asset write-off; `PaymentVoucherApplication` is the sole AP-side application table. Any such feature added later must revisit this table. |
| C16 | **Raw SQL / DBA** | any | any | none | **NO** ✗ | **OUT OF SCOPE — no app-level defence is possible.** Verified: no seed script INSERTs a journal line against 2110 (the four `2110` hits in `SqlScripts/` are three `chart_of_accounts` rows and one false positive, `'62110'` in `430_seed_expense_categories_full.sql:44`). Noted for completeness; §3.3's detector *would* in fact catch a raw-SQL AP debit, since it reads the GL rather than the app's write paths. |

**Sweep conclusion.** Of 16 channels, three (C3, C4, C5) create the defect and are all caught by one
GL-grounded detector, because all three post through `BuildAndPostAsync`. Two (C6, C7) are a genuinely
different shape that no AP-based detector can see, and are declared out of scope with a CPA escalation
rather than papered over. The rest cannot reach the event.

---

## 3. Design

### 3.0 The rule this design obeys

> **Detect and surface. Require sign-off. Never refuse.**

Refusal was considered and rejected in two forms:
- *Blocklist AP on manual JV lines* — **rejected.** Write-offs, opening balances, reclassifications and
  corrections legitimately post there. It manufactures a dead end with no exit, which is the exact
  class of defect `cb2e362` had to remediate this release.
- *Require a VendorInvoice link on any AP-touching JV line* — **rejected.** `JournalLine` has no vendor
  or document field (`SubledgerDtos.cs:5-7`); adding one is a schema change plus a new seam across
  every JV consumer, for a company with zero live exposure. It is the right *long-term* direction
  (§8 records it as such), not this change.

### 3.1 What the filer sees

On the ภ.พ.36 page, below the declared rows, up to two blocks appear:

**(a) Informational — always shown when non-empty, no friction.**
> ใบแจ้งหนี้ค่าบริการจากต่างประเทศที่ยังไม่ได้ชำระ N รายการ · มูลค่า ฿X · VAT ที่จะต้องนำส่งเมื่อชำระ ฿Y
> *(N outstanding foreign-service invoices; ภ.พ.36 will declare each one in the month it is paid.)*

This is the normal, legitimate state of an unpaid invoice. It carries **no** acknowledgement
requirement — an unpaid invoice owes nothing yet, and flagging it would be alert fatigue by design.

**(b) Warning — shown only when the AP control account moved in this month by more than posted
PaymentVouchers explain.**
> ⚠ พบรายการหักบัญชีเจ้าหนี้ ฿Z ในเดือนนี้ที่ไม่มีใบสำคัญจ่ายรองรับ
> หากรายการใดเป็นการชำระเงินให้ผู้ให้บริการต่างประเทศ รายการนั้นจะ **ไม่ปรากฏ** ในแบบ ภ.พ.36 ฉบับนี้
> **วิธีแก้:** กลับรายการ (reverse) สมุดรายวันนั้น แล้วบันทึกการชำระเงินใหม่ด้วย "ใบสำคัญจ่าย" เพื่อให้ระบบนำไปแสดงใน ภ.พ.36 โดยอัตโนมัติ

…followed by a table of the candidate journal entries (doc no · date · description · debit · credit),
and a checkbox required before Finalize is enabled:
> ☐ ข้าพเจ้าได้ตรวจสอบรายการข้างต้นแล้ว และยืนยันว่าไม่มีรายการใดเป็นการชำระเงินให้ผู้ให้บริการต่างประเทศ

**The remediation sentence is mandatory copy, not decoration.** An advisory that names a problem
without naming the fix is what gets muscle-memory-clicked.

### 3.2 The asymmetry that decides every tie-break

Under-declaring a tax return is a penalty exposure (เบี้ยปรับ + เงินเพิ่ม 1.5%/month). Over-flagging
costs the filer one checkbox. **Therefore every ambiguous case in this design resolves toward flagging.**
Two concrete consequences, both deliberate:

1. §3.3 sums **debits only**. A credit to AP (a hand-booked accrual) must never net away a debit that
   might be a payment. Netting is how a real omission hides.
2. The warning fires on an unexplained AP debit **whatever its cause** — a genuine write-off trips it
   too. That is accepted: a false positive costs a checkbox, a false negative costs a penalty.

### 3.3 The detector — exact query, and why each side is comparable

Two sums over the filing month, both grounded in the GL rather than in any app write-path:

**Actual** = every debit to the AP control account posted in the month.
**Expected** = what posted PaymentVouchers account for. `GlPostingService.cs:222-228` debits AP exactly
`pv.SubtotalAmount + pv.VatAmount` for a VI-linked PV, and `PaymentVoucherService.cs:641-646` records
that *same figure* as `PaymentVoucherApplication.AppliedAmount`. A standalone PV posts **no** AP line
(`GlPostingService.cs:247` else-branch) and correctly contributes 0.

The two sides bucket by the same date: the PV's journal entry is created with `pv.DocDate` as its
`docDate` (`GlPostingService.cs:299`). This is the fact that makes the subtraction meaningful — state
it in the code comment.

Add this as a private method on `WhtFilingService`. Property/navigation names marked `⚠verify` must be
confirmed against the entities before use — do not assume them.

```csharp
// F1 (specs/fix-pnd36-payment-detection.md §3.3) — ม.83/6 keys on PAYMENT, and nothing forces a
// payment through a PaymentVoucher (§2 C3/C4/C5). JournalLine carries no vendor tag
// (SubledgerDtos.cs:5-7), so a Dr-2110 line can never be attributed to an invoice. What CAN be
// computed exactly is the aggregate: AP debits posted this month, minus what posted PVs explain.
// DEBITS ONLY — deliberately. A credit to AP (a hand-booked accrual) must NEVER net away a debit
// that may be a payment; netting is how a real omission hides (§3.2 asymmetry).
private async Task<Pnd36Unreconciled> DetectUnreconciledAsync(
    DateOnly from, DateOnly to, CancellationToken ct)
{
    const decimal vatRate  = 0.07m;
    const decimal tol      = 0.01m;   // repo money tolerance, cf. pv.vi_over_settle

    var apCode = glAccounts.Value.ApAccount;              // "2110", never a literal
    var apAccountId = await db.ChartOfAccounts.AsNoTracking()
        .Where(a => a.AccountCode == apCode)              // tenant-scoped by the global query filter
        .Select(a => (long?)a.AccountId)
        .FirstOrDefaultAsync(ct);

    // A company with no AP account configured cannot have an AP anomaly. Fail OPEN (empty, no
    // friction) rather than throwing — this is an advisory, and it must never break a filing.
    if (apAccountId is null)
        return new Pnd36Unreconciled(0m, [], [], false);

    // Exclude reversal pairs: an entry that IS a reversal, and an entry that HAS been reversed.
    // Without this a mistake that was correctly reversed flags its month forever.
    var reversedIds = await db.JournalEntries.AsNoTracking()
        .Where(j => j.ReversalOfId != null)
        .Select(j => j.ReversalOfId!.Value)
        .ToListAsync(ct);

    var apLines = await db.JournalLines.AsNoTracking()          // ⚠verify: JournalLine.JournalId FK
        .Join(db.JournalEntries.AsNoTracking(),
              l => l.JournalId, j => j.JournalId, (l, j) => new { l, j })
        .Where(x => x.l.AccountId == apAccountId
                 && x.j.Status == DocumentStatus.Posted        // ⚠verify: JournalEntry.Status enum
                 && x.j.DocDate >= from && x.j.DocDate <= to
                 && x.j.ReversalOfId == null
                 && !reversedIds.Contains(x.j.JournalId))
        .Select(x => new {
            x.j.JournalId, x.j.DocNo, x.j.DocDate, x.j.Description, x.j.Reference,
            x.l.DebitAmount, x.l.CreditAmount })
        .ToListAsync(ct);

    // Expected: exactly what posted PVs cleared against AP this month. Both sides bucket on the
    // SAME date — GlPostingService.cs:299 posts the PV's entry with pv.DocDate.
    var expectedApDebits = await db.PaymentVoucherApplications.AsNoTracking()
        .Join(db.PaymentVouchers.AsNoTracking(),
              a => a.PaymentVoucherId, pv => pv.PaymentVoucherId, (a, pv) => new { a, pv })
        .Where(x => x.pv.Status == DocumentStatus.Posted
                 && x.pv.DocDate >= from && x.pv.DocDate <= to)
        .SumAsync(x => (decimal?)x.a.AppliedAmount, ct) ?? 0m;

    var unexplained = decimal.Round(apLines.Sum(x => x.DebitAmount) - expectedApDebits, 2);

    // BEST-EFFORT attribution for display only. There is no structural PV→JournalEntry link
    // (§1.3): every poster stamps the same PrefixCode and PaymentVoucher has no JournalEntryId.
    // Convention is all we have — GlPostingService.cs:299 sets Reference = pv.DocNo. The AMOUNT
    // above is authoritative; if this list is empty while `unexplained > tol`, the WARNING STILL
    // FIRES. Never gate the warning on this list.
    var pvDocNos = await db.PaymentVouchers.AsNoTracking()
        .Where(p => p.Status == DocumentStatus.Posted
                 && p.DocDate >= from && p.DocDate <= to && p.DocNo != null)
        .Select(p => p.DocNo!)
        .ToListAsync(ct);

    // Both debits AND credits are listed, so a reversal-shaped pair reads as a pair to the filer.
    var entries = apLines
        .Where(x => x.Reference == null || !pvDocNos.Contains(x.Reference))
        .OrderBy(x => x.DocDate).ThenBy(x => x.DocNo)
        .Select(x => new Pnd36UnreconciledEntry(
            x.DocNo ?? "", x.DocDate, x.Description ?? "", x.DebitAmount, x.CreditAmount))
        .ToList();

    // Informational tier: posted reverse-charge invoices not yet fully settled as of period end.
    // Reads the INVOICE'S OWN snapshot flag, matching the filing query — never the vendor's
    // current IsForeign value (§1.5 footgun).
    var outstanding = await db.VendorInvoices.AsNoTracking()
        .Where(v => v.RequiresPnd36ReverseCharge
                 && v.Status == DocumentStatus.Posted
                 && v.DocDate <= to
                 && v.SettledAmount < v.TotalAmount - tol)
        .Join(db.Vendors.AsNoTracking(), v => v.VendorId, ven => ven.VendorId,
              (v, ven) => new { v, ven.CountryCode })
        .Select(x => new Pnd36OutstandingInvoice(
            x.v.VendorName, x.CountryCode, x.v.DocNo ?? "", x.v.DocDate,
            x.v.TotalAmount - x.v.SettledAmount,
            decimal.Round(x.v.SubtotalAmount * vatRate, 2)))
        .OrderBy(r => r.DocDate)
        .ToListAsync(ct);

    return new Pnd36Unreconciled(
        unexplained > tol ? unexplained : 0m,
        unexplained > tol ? entries : [],
        outstanding,
        RequiresAcknowledgement: unexplained > tol);
}
```

**Accepted residual risks, stated rather than hidden:**
- An AP debit *and* a legitimate PV-sized AP debit in the same month can coincidentally sum to the
  expected figure only if the unexplained debit is exactly zero. There is no masking path, because
  credits are excluded.
- A genuine write-off trips the warning. Accepted per §3.2.
- A JV that debits AP in month M for a payment actually made in month M−1 is flagged in M, not M−1.
  Accepted: flagging late beats not flagging.

### 3.4 DTO shapes (exact)

In `backend/src/Accounting.Application/TaxFilings/TaxFilingDtos.cs`:

```csharp
/// One journal entry that moved the AP control account inside the filing month with no
/// PaymentVoucher behind it. BEST-EFFORT attribution (§3.3) — Pnd36Unreconciled.UnexplainedApDebit
/// is the authoritative figure; this list is for the filer's eyes.
public sealed record Pnd36UnreconciledEntry(
    string JournalDocNo, DateOnly DocDate, string Description,
    decimal DebitAmount, decimal CreditAmount);

/// A posted reverse-charge foreign-service invoice not yet fully settled. Informational: an
/// unpaid invoice owes nothing under ม.83/6 — it is declared in the month it is PAID.
public sealed record Pnd36OutstandingInvoice(
    string VendorName, string? VendorCountry, string DocNo, DateOnly DocDate,
    decimal OutstandingAmount, decimal VatIfPaid);

/// F1 — ภ.พ.36 completeness advisory. NEVER blocks a preview; gates finalize only via
/// RequiresAcknowledgement (§3.5), which always has a one-click exit.
public sealed record Pnd36Unreconciled(
    decimal UnexplainedApDebit,
    IReadOnlyList<Pnd36UnreconciledEntry> Entries,
    IReadOnlyList<Pnd36OutstandingInvoice> OutstandingForeignInvoices,
    bool RequiresAcknowledgement);
```

`Pnd36Row` gains `DateOnly PaymentDate` (the settling PV's `DocDate`) — **appended last**, so the
filer can see which day each declared payment is bucketed on. This is L1 visibility, not an L1 fix
(§11).

`Pnd36Filing` gains, **appended last**:
```csharp
Pnd36Unreconciled Unreconciled,
long? AcknowledgedByUserId,
DateTimeOffset? AcknowledgedAt
```

### 3.5 The acknowledgement gate — and its exit

`GeneratePnd36Async` gains a parameter. **The position is not stylistic — get it wrong and you silently
break every existing caller:**

```csharp
Task<Pnd36Filing> GeneratePnd36Async(
    int period, TaxFilingMode mode, CancellationToken ct, bool acknowledgeUnreconciled = false);
```

> **⚠ FOOTGUN — do NOT "tidy" this into the idiomatic parameter order.** Every existing call site is
> `GeneratePnd36Async(period, TaxFilingMode.Preview, default)` (e.g.
> `Sprint9WhtComplianceTests.cs:353,397,458,510`). Inserting a `bool` **before** the
> `CancellationToken` makes `default` bind to the **bool** — it still compiles, silently changes
> meaning at four-plus call sites, and no test would fail. The optional-after-`ct` form is
> deliberate. Say so in a code comment so a future reviewer does not "fix" it.

Behaviour:
- **Preview** — always returns, never throws, regardless of the advisory. Preview must stay a
  read-only diagnostic.
- **Finalize** — if `Unreconciled.RequiresAcknowledgement && !acknowledgeUnreconciled`, throw
  **before** the existing already-finalized guard runs its JV post:
  ```csharp
  throw new DomainException("pnd36.unreconciled_not_acknowledged",
      "ภ.พ.36: an unexplained debit to the Accounts Payable control account was posted in this " +
      "period. Review the listed entries and confirm none of them is a payment to an overseas " +
      "service provider, then finalize again with the confirmation ticked.");
  ```
- On a successful acknowledged finalize, stamp `AcknowledgedByUserId = tenant.UserId`,
  `AcknowledgedAt = clock.UtcNow` onto the `Pnd36Filing` **before** `TaxFilingStore.FinalizeAsync`,
  so the sign-off — and the exact candidate list at that moment — lands in the immutable history.
  This is what makes the friction purposeful rather than decorative.

> **Exit trace (mandatory — a guard is only safe if the state behind it has an exit).**
> State when it fires: the company has an unexplained AP debit in the filing month.
> Exit: tick one checkbox on the same screen and press Finalize again. **Same user, same
> `tax.filing.finalize` permission, no new permission, no admin, no DBA, no reversal required, no
> period reopen required.** The company can always file. If the entry really was a foreign payment,
> the better exit is also offered in the copy (reverse the JV, re-record via a Payment Voucher) — but
> it is an option, never a precondition.

Endpoint — `TaxFilingEndpoints.cs:153-161` gains `[FromQuery] bool? acknowledge` and forwards
`acknowledge == true`. No new permission: the acknowledgement rides on the existing
`GuardFinalizeAsync` → `tax.filing.finalize` check.

### 3.6 Why widening the DTO is safe here (a reason, not a reassurance)

`TaxFilingStore.FinalizeAsync` serialises the whole `filing0` DTO into the immutable
`tax.tax_filings` history. Widening a serialised record normally risks breaking reads of existing
rows. **It does not here, because ภ.พ.36 has never been finalized for any company** (§1.9, prod probe
2026-08-12) — there is no historical PND36 payload to deserialise. The implementer must still:
- verify `TaxFilingStore.FinalizeAsync` and any read path tolerate the new fields, and
- verify `IRdEfilingClient` (`rd`, passed at `WhtFilingService.cs:300`) does not choke on them —
  if it serialises the DTO onward to RD, the advisory fields **must not** reach the RD payload.

If either check fails, that is a **stop-and-re-spec** trigger (§9), not a judgement call.

### 3.7 At-source advisory on the manual-JV path (WP-3)

Non-blocking, additive, and **code-based rather than prose-based** so the FE owns the wording:

`backend/src/Accounting.Application/Ledger/JournalDtos.cs:23` —
```csharp
public sealed record JournalPostedResult(
    long JournalId, string DocNo, DateTimeOffset PostedAt, string? AdvisoryCode = null);
```
Appended with a default, so every existing construction site compiles unchanged.

In `JournalService`, after a successful post, in **both** `CreateAndPostManualAsync` (`:151-212`) and
`PostAsync` (`:82-115`) — `PostAsync` is not optional, it is the path an MCP-drafted entry takes (§2
C8):

1. If no posted line debits the AP control account (`glAccounts.Value.ApAccount`, resolved by code) →
   `AdvisoryCode = null`. **Skip the second query entirely** — no cost on the overwhelmingly common
   path. `JournalService` does not currently inject `IOptions<GlAccountsOptions>`; add it.
2. Otherwise, if the company has ≥ 1 posted, not-fully-settled `RequiresPnd36ReverseCharge`
   VendorInvoice → `AdvisoryCode = "pnd36.ap_cleared_outside_pv"`. Otherwise `null`.

The second condition is what keeps this from becoming noise: a company with no outstanding
foreign-service invoices never sees it, so an accountant doing routine write-offs is not trained to
dismiss it.

FE (`frontend/app/(dashboard)/journals/new/page.tsx`) renders the i18n'd advisory as a **warning
toast alongside the existing success toast** — the post succeeded; this is information, not a failure.

---

## 4. Invariants

Stated in money terms, not field values.

- **I1 — the guarantee, stated exactly.** For every baht debited to the AP control account in filing
  month M, either (a) it is explained by a posted PaymentVoucher in M and any reverse-charge portion
  appears in exactly one ภ.พ.36 period's rows, or (b) it is surfaced to the filer as an unexplained
  amount **and** the finalize that omits it carries a recorded acknowledgement naming the flagged
  entries. **There is no third outcome in which an AP-clearing payment is omitted silently.**
  → **T1, T2, T5.**
  *This is NOT "the return is complete." Payments that never touch AP (§2 C6, C7) fall outside I1
  entirely — by construction, acknowledged in §0 and §8.*
- **I2 — no double-count regression.** For one foreign service of ฿X, ภ.พ.36 declares it exactly once,
  in exactly one period, across every chain shape in §5's table. The `1e46a35` fix stands untouched:
  rows still come from posted PaymentVouchers only. → **T7 (existing T2/T3/T4 in
  `Sprint9WhtComplianceTests.cs` must stay green, unmodified except comments).**
- **I3 — the declared amount does not change.** `TotalService` and `TotalVat` for any period are
  bit-identical before and after this change. The advisory adds rows to a *diagnostic* block, never to
  `Rows`. The reverse-charge JV's amount, accounts and Dr=Cr balance are untouched. → **T6.**
- **I4 — no new dead end.** Every state in which the new refusal fires has a one-click exit available
  to the same user, in the same screen, under the same permission. → **T5.**
- **I5 — sign discipline.** An `unexplained` figure is **positive** when AP was debited beyond what
  PaymentVouchers explain. A negative figure means AP was credited manually and is **not** a ภ.พ.36
  risk — it must never trigger the warning. → **T3** (this test exists specifically to catch a sign
  flip; see §6).
- **I6 — preview is inert.** No mode, no data shape, and no advisory state can make Preview throw or
  mutate anything. → **T4.**

---

## 5. Chain shapes and their expected ภ.พ.36 outcome

Vendor is foreign, `IsForeign && !HasThaiVatDReg`, service ฿10,000, VAT ฿700.

| Shape | Declared? | Period | Advisory | Ack required? |
|---|---|---|---|---|
| **VI (June) + settling PV (June)** | YES, once | June (the PV's `DocDate`) | none | no |
| **VI (June) + settling PV (July)** | YES, once | **July** — payment month, ม.83/6 (see §11 on whether "July" is the right *day*) | none | no |
| **Standalone PV, no VI (June)** | YES, once | June | none | no |
| **VI posted, never paid** | NO — correctly, nothing is owed yet | — | **informational** row (a): "1 outstanding foreign-service invoice, VAT ฿700 when paid" | no |
| **VI (June) + manual JV `Dr 2110 / Cr 1120` (June)** | **NO** ✗ | — | **warning** (b): unexplained AP debit ฿10,700, JV listed; informational row also present | **YES** |
| **VI (June) + manual JV in July** | **NO** ✗ | — | **warning** on **July's** return (the month the JV posted) | YES, in July |
| **VI + JV, JV later reversed, then a proper PV posted** | YES, once, via the PV | PV's month | **none** — the reversal pair is excluded (§3.3) | no |
| **Bank-rec inline JE with contra = 2110** | **NO** ✗ | — | **warning** (b) — same detector, same month | **YES** |
| **Foreign service paid by JV `Dr 5xxx / Cr 1120`, no VI, no AP** | **NO** ✗ | — | **NONE — undetectable (§2 C6)** | no |
| **Foreign service paid personally by an employee, reimbursed via expense claim** | **NO** ✗ | — | **NONE — undetectable (§2 C7)** | no |
| **Genuine AP write-off by JV, no foreign invoices outstanding** | n/a | — | **warning** (b) fires (accepted false positive, §3.2); no at-source advisory (WP-3 gate 2 is false) | YES |

The last three rows are the honest edges. They are in the spec so a reviewer can hold the design to
what it actually claims.

---

## 6. Test list

All in `backend/tests/Accounting.Api.Tests/Hardening/Sprint9WhtComplianceTests.cs`, following the
existing `[SkippableFact]` + `RandPeriod()` / `PeriodDate()` conventions. Every behavioural test posts
through the **real** transition — never seed the target state.

| ID | Test | PURPOSE (what breaks if it is deleted) |
|---|---|---|
| **T1** | `Pnd36_AP_cleared_by_manual_JV_is_flagged_not_silently_dropped` — post a reverse-charge VI, then clear it with a real `CreateAndPostManualAsync` `Dr 2110 / Cr 1120`. Preview: `Rows` empty, `Unreconciled.UnexplainedApDebit == VI total`, `Entries` contains the JV's DocNo, `RequiresAcknowledgement == true`. | **The defect itself.** Proves I1: an omitted payment is surfaced, not silent. Must use the real service call, not a seeded `JournalEntry`. |
| **T2** | `Pnd36_finalize_refuses_unacknowledged_then_succeeds_when_acknowledged` — finalize without the flag → `DomainException("pnd36.unreconciled_not_acknowledged")`; finalize with `acknowledgeUnreconciled: true` → succeeds, `AcknowledgedByUserId`/`AcknowledgedAt` set, and the ✱ filing is retrievable from `tax_filings`. | Proves I1(b) **and** I4: the guard fires *and* the exit works. A test that only asserts the refusal would bless a dead end. |
| **T3** | `Pnd36_manual_AP_credit_does_not_trigger_the_warning` — post a JV that **credits** 2110 (a hand-booked accrual). Assert `UnexplainedApDebit == 0` and `RequiresAcknowledgement == false`. | **The sign-flip catcher (I5).** ⚠ **If this test fails — i.e. a pure AP *credit* produces a positive `unexplained` — the spec's sign reasoning in §3.3 is wrong. STOP and re-spec (§9); do not "fix" the test or flip a sign to make it pass.** |
| **T4** | `Pnd36_preview_never_throws_when_unreconciled` — same setup as T1, call Preview twice. No exception, nothing mutated, identical results. | Proves I6. Guards against someone moving the guard out of the Finalize branch. |
| **T5** | `Pnd36_clean_company_has_no_advisory_and_finalizes_without_a_flag` — VI + settling PV, nothing else. `Unreconciled.UnexplainedApDebit == 0`, `Entries` empty, `RequiresAcknowledgement == false`, finalize succeeds with **no** flag. | **The no-new-friction regression test.** Proves the normal path gained zero ceremony — the single most likely way this change hurts real users. |
| **T6** | `Pnd36_totals_unchanged_by_the_advisory` — for the T5 fixture assert `TotalService`/`TotalVat` exactly, and that the reverse-charge JV still balances Dr=Cr at the same amount and accounts. | Proves I3 — the money is untouched. |
| **T7** | *(no new test)* existing `Pnd36_VI_plus_settling_PV_same_period_declares_once` (`:306`), `Pnd36_standalone_PV_no_VI_declares_once` (`:364`), `Pnd36_VI_and_later_PV_declare_only_in_the_payment_period` (`:408`), `Pnd36_non_VAT_company_still_finalizes_with_irrecoverable_debit` (`:474`) must stay green, **unmodified except for comment retargeting**. | Proves I2 — `1e46a35` is not regressed. |
| **T8** | `ManualJv_clearing_AP_returns_the_pnd36_advisory_code` — with an outstanding reverse-charge VI, `CreateAndPostManualAsync` `Dr 2110` returns `AdvisoryCode == "pnd36.ap_cleared_outside_pv"`; with **no** outstanding foreign invoice, returns `null`; a JV not touching AP returns `null`. | Proves WP-3's targeting — i.e. that it is not noise. The negative cases matter more than the positive one. |
| **T9** | `Journal_draft_then_post_also_returns_the_advisory` — `CreateDraftAsync` then `PostAsync`. | §2 C8: the MCP-drafted path posts through `PostAsync`. Without this test WP-3 covers only half the hole. |

**Not automatable, reported honestly rather than skipped:** the FE rendering of blocks (a)/(b), the
checkbox wiring, and the Thai copy. Verified by the §7 manual gate (screenshot), not by a test.

---

## 7. Verification gates

Worker runs G1–G4 and reports evidence. **The orchestrator runs G5** (long suite) — the worker must
NOT babysit it.

| # | Command | Expected |
|---|---|---|
| **G1** | `dotnet build backend/Accounting.sln -c Debug` | 0 errors, 0 new warnings |
| **G2** | `cd frontend; npx tsc --noEmit` | clean |
| **G3** | Set `TEAS_TEST_PG` **in the same shell call**, then `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~Sprint9WhtCompliance"` | all pass; **skip count equal to baseline** — a skipped `[SkippableFact]` suite is a fake green run |
| **G4a** *(positive control — run FIRST)* | `git diff \| Select-String ([char]0x0E21)` | **MUST match at least once.** The Thai ม is guaranteed present in this change (the new i18n strings). **If this finds nothing, the pipeline is lying and G4b is worthless** — PowerShell 5.1 decodes native-command output via `[Console]::OutputEncoding`, usually a legacy codepage, which mangles the UTF-8 bytes so no Thai/Bengali codepoint ever compares equal. Fall back to reading the changed files directly (`Get-Content -Encoding utf8 <file> \| Select-String …`) or to bash `grep`, and re-run both checks there. |
| **G4b** *(negative check — only meaningful if G4a matched)* | `git diff \| Select-String ([char]0x09AE)` — matched **by codepoint**, never by a pasted glyph, so the command cannot self-match | **no matches.** U+09AE is the Bengali look-alike of Thai ม (U+0E21) — troubles-wiki "Thai ม glyph pitfall". If it hits, find the citation and retype the ม. **A pass here counts only when G4a passed** — otherwise it is an empty grep, not a clean result. |
| **G5** | *(orchestrator)* full `dotnet test` suite, one backgrounded call | no new failures vs. the v2.0.0 baseline |
| **G6** | *(manual, orchestrator)* ภ.พ.36 page screenshot on a company with an unexplained AP debit | blocks (a) and (b) render, Thai copy correct incl. the remediation sentence, Finalize disabled until the checkbox is ticked |

---

## 8. Out of scope — explicit, so scope creep is a reviewable defect

1. **§2 C6 — a foreign-service payment that never touches AP** (`Dr 5xxx / Cr 1120`, no invoice).
   Undetectable by any AP-based design. → troubles-wiki entry (WP-5), R4 candidate.
2. **§2 C7 — expense-claim reimbursement of an employee who paid the overseas provider.** No vendor, no
   AP, structurally blocked from 2110. → troubles-wiki entry (WP-5) + **§10 E2 (CPA)**.
3. **A vendor/document tag on `JournalLine`** — the real long-term fix (it would make the JV path
   first-class declarable instead of merely detectable). Schema change + a new seam across every JV
   consumer, for zero live exposure. Correct direction, wrong release.
4. **L1 — the PV `DocDate` re-pin at post.** See §11. Belongs to Feature B.
5. **`CreateDraftAsync` discarding its own `docDate`** (troubles-wiki) — moves the reverse-charge
   auto-JV's date, not the return's period. Untouched.
6. **`RequiresPnd36ReverseCharge` recomputation at post** (§1.5) — a filing-behaviour change with no
   spec of its own. Do not drive-by fix.
7. **Blocking or restricting manual JVs against AP** — rejected by design (§3.0). A reviewer seeing a
   blocklist in the diff should REJECT it.
8. **Any ภ.พ.36 PDF work** — no RD template exists (R2 §3.7 / §10 E6). Unchanged.
9. **Prod data.** Read-only facts in §1.9 are already gathered. Do not probe, do not touch.

---

## 9. Blast-radius cap

**Max 15 files.** Public API changes: **additive only** (new DTO records; parameters and record
components appended with defaults). No schema change, no migration, no new permission, no new seed.

Expected file set:
`TaxFilingDtos.cs` · `WhtFilingService.cs` · `TaxFilingEndpoints.cs` · `JournalDtos.cs` ·
`JournalService.cs` · `Sprint9WhtComplianceTests.cs` · `frontend/lib/types.ts` ·
`frontend/lib/queries.ts` · `frontend/app/(dashboard)/tax-filings/pnd36/page.tsx` ·
`frontend/app/(dashboard)/journals/new/page.tsx` · `frontend/messages/th.json` ·
`frontend/messages/en.json` · `troubles-wiki.md`  → 13, leaving 2 of slack.

**Stop-and-re-spec triggers — stop and report, never work around:**
- **T3 fails** (an AP *credit* produces a positive `unexplained`) — the sign reasoning is wrong.
- A migration or any schema change appears necessary.
- `TaxFilingStore` or `IRdEfilingClient` cannot tolerate the widened DTO (§3.6).
- Any existing T7 test needs a **behavioural** change to stay green (comment edits are fine).
- The parameter-order footgun in §3.5 turns out not to compile as specced.
- The file count reaches 15.

*(Commissioning any post-review remediation = update this NUMBER in this header, in the same edit that
adds the findings.)*

---

## 10. Escalations — decisions that are NOT engineering

Neither blocks dispatch. Both ship with the recommendation implemented; Ham can reverse either in one
word afterwards.

- **E1 · The acknowledgement gate is a product/UX call.** It adds friction to a filing flow.
  **Recommendation: ship it.** Rationale: the asymmetry in §3.2 — an omission is a penalty, a checkbox
  is five seconds — plus the audit value of a recorded sign-off in an immutable filing. If Ham prefers
  advisory-only, deleting the guard is a ~5-line change (drop the throw, drop the parameter's use,
  keep the DTO and the FE block).
- **E2 · CPA — does ม.83/6 attach when an employee pays the overseas provider personally and is
  reimbursed?** (§2 C7.) Who is the ผู้จ่ายเงิน: the employee or the company? The answer decides whether
  the expense-claim gap is a real filing hole needing its own design, or a non-issue. **Not a code
  decision. Do not guess in code.**
- **E3 · CPA confirmation of the PV-only rule itself is STILL OUTSTANDING** — carried forward from
  `specs/fix-breakit-r2-compliance.md` §10 E1, which was resolved only to the extent that no company
  has ever finalized ภ.พ.36 (so nothing was over-remitted). The underlying question — that ภ.พ.36's
  period follows payment, not the invoice — has not been confirmed by a CPA. **This spec inherits that
  open question and does not close it.** It is recorded here so it is not lost a second time.

---

## 11. L1 — recommendation and justification

**L1 is real, and it is worse than reported.** `PaymentVoucherService.cs:496-498` re-pins
`pv.DocDate = pv.PostingDate = TodayInBangkok()` at **POST**, not merely at draft. The ภ.พ.36 period
therefore follows the day the voucher was *posted*. Pay 30 June, post 3 July → June's ฿700 lands in
July's return, due 7 August instead of 7 July. **One month late; เงินเพิ่ม 1.5%/month accrues
statutorily.** v2.0.0 made this strictly worse: before `1e46a35` the period followed the
VendorInvoice's `DocDate`, which at least *was* user-settable at the time.

**Recommendation: L1 does NOT belong in this change. It belongs to Feature B — but Feature B as
currently written will not fix it, and that gap must be closed now.**

**Why not here.** The correct fix is a single date, not a second one. Under ม.83/6 the tax point is the
real payment day; under accrual accounting the GL entry belongs on that same day (the bank statement
says 30 June). There is no case where the ledger and the return should disagree. So the fix is "let the
user date the payment voucher on the day the money left" — which is precisely **Feature B**
(`specs/doc-lifecycle-cancel-reissue-backdate.md` §2), already answered binding by Ham
(§6 Q2: *"backdating allowed only INSIDE an open period; future dates forbidden"*), and gated on
**H1** (the numbering allocator) because the document number derives from `DocDate` and numbering is
monthly. Bolting a PV-only date field onto this change would fork that decision and duplicate the
allocator risk H1 exists to remove.

**Rejected alternative — a separate "actual payment date" column on PaymentVoucher, read only by
ภ.พ.36.** It would fix the return without waiting for H1. Rejected: it creates a permanent, silent
divergence between the GL date and the tax date with nothing reconciling them — the same shape as the
defect this spec exists to close.

**⚠ THE GAP THAT MUST BE CLOSED — Feature B does not currently fix L1.** Its §0 facts table lists
`PaymentVoucherService.cs:143,181` (draft-create) and §2.1 removes the re-pin-on-edit at `VI:292` /
`PO:136` only. **`PaymentVoucherService.cs:496-498` — the POST-time re-pin — appears nowhere in that
spec.** If Feature B ships exactly as written, the PV's `DocDate` is still clobbered at post and
ภ.พ.36's period stays wrong. Citing Feature B as L1's fix without this amendment would be an imagined
safety net — the failure mode this project's designer rules exist to prevent.

**Therefore, three concrete actions:**
1. **Amend `specs/doc-lifecycle-cancel-reissue-backdate.md` §0/§2.1** to add
   `PaymentVoucherService.cs:496-498` to Feature B's removal list, and to note that the post-time
   re-pin carries its own deliberate rationale (`§4.3 / ม.78 …` in the code comment) which Feature B
   must consciously reverse for PV as it already does for VI and PO. **This is an edit to another
   spec — surfaced to the orchestrator here, not made silently by this spec's implementer.**
2. **In scope for THIS change** (cheap, same DTO, closes the silence while Feature B waits):
   - `Pnd36Row.PaymentDate` (§3.4) so the filer can see which day each declared payment is bucketed
     on, and spot one that really happened last month.
   - **Retarget test `Pnd36_VI_and_later_PV_declare_only_in_the_payment_period` (`:408`) by COMMENT
     ONLY.** The test seeds `DocDate` directly and asserts the *correct* invariant (the period follows
     the payment); the defect is the production re-pin, which is Feature B's job. **The implementer
     must not change this test's assertions** — add a comment naming L1, its owner (Feature B), and
     the wiki entry.
   - A `troubles-wiki.md` entry for L1 (WP-5), so it survives if Feature B slips.
3. **Deferred detector idea, recorded not built:** `StatementLine.TxnDate` matched to a PV via
   `MatchedPaymentVoucherId` (`BankReconciliationService.ConfirmMatchAsync:105-182`) is the bank's own
   record of the real payment day. A future check could flag any PV whose matched statement line falls
   in a different month from its `DocDate` — a provable L1 detection. Not built here: it makes tax
   filing depend on bank-rec adoption, and Feature B removes the need.

---

## 12. Requirements checklist

**Dependencies:** WP-1 → WP-2 (WP-2 consumes WP-1's DTO). WP-3 is file-disjoint from WP-1/WP-2 on the
backend but **shares the test project and the shared test DB** — run it in the same warm worker,
sequentially, never in parallel. WP-4 after WP-1/WP-3. WP-5 anytime.

### WP-1 — backend detection + acknowledgement *(no dependencies)*
- [ ] `TaxFilingDtos.cs` — add `Pnd36UnreconciledEntry`, `Pnd36OutstandingInvoice`, `Pnd36Unreconciled`
      exactly as §3.4. Append `PaymentDate` to `Pnd36Row`; append `Unreconciled`,
      `AcknowledgedByUserId`, `AcknowledgedAt` to `Pnd36Filing`. **Done:** builds; no call site reordered.
- [ ] `TaxFilingDtos.cs` — `IWhtFilingService.GeneratePnd36Async` gains
      `bool acknowledgeUnreconciled = false` **after** `CancellationToken ct`, with the §3.5 footgun
      comment. **Declare the `= false` default on BOTH the interface and `WhtFilingService`'s
      implementation** — C# binds an optional-parameter default at the *compile-time* type, so an
      interface-only default leaves any caller holding the concrete class without one (and vice
      versa). **Done:** every existing call site compiles unchanged and still binds `default` to `ct`.
- [ ] `WhtFilingService.cs` — add `DetectUnreconciledAsync` per §3.3, verifying the three `⚠verify`
      names first. **Done:** T1, T3, T5 pass.
- [ ] `WhtFilingService.cs` — populate `Pnd36Row.PaymentDate` from the PV's `DocDate` in the existing
      projection (`:268-277`). **Done:** T6 asserts it.
- [ ] `WhtFilingService.cs` — Finalize guard + acknowledgement stamping per §3.5, placed so Preview
      can never throw. **Done:** T2, T4 pass.
- [ ] Verify §3.6: `TaxFilingStore.FinalizeAsync` and `IRdEfilingClient` tolerate the widened DTO, and
      that no advisory field reaches the RD payload. **Done:** evidence pasted; failure ⇒ stop-and-re-spec.
- [ ] `TaxFilingEndpoints.cs:153-161` — `[FromQuery] bool? acknowledge`, forwarded. **Done:** builds.

### WP-2 — frontend ภ.พ.36 page *(after WP-1)*
- [ ] `frontend/lib/types.ts:648-656` — mirror the new DTO shapes exactly.
- [ ] `frontend/lib/queries.ts:1564-1571` — `usePnd36` accepts `acknowledge?: boolean`, appended to `qs`.
- [ ] `frontend/app/(dashboard)/tax-filings/pnd36/page.tsx` — render blocks (a) and (b) per §3.1;
      checkbox state; Finalize disabled while `requiresAcknowledgement && !checked`; surface the
      `pnd36.unreconciled_not_acknowledged` error as a readable message.
- [ ] `frontend/messages/th.json` + `en.json` — new keys incl. the **remediation sentence**. Thai ม
      (U+0E21) only. **Done:** G2 + G4 clean, G6 screenshot.

### WP-3 — at-source manual-JV advisory *(sequential with WP-1; shares the test project)*
- [ ] `JournalDtos.cs:23` — append `string? AdvisoryCode = null` to `JournalPostedResult`.
- [ ] `JournalService.cs` — inject `IOptions<GlAccountsOptions>`; add the two-gate advisory per §3.7 to
      **both** `CreateAndPostManualAsync` and `PostAsync`. **Done:** T8, T9 pass, incl. both negatives.
- [ ] `frontend/app/(dashboard)/journals/new/page.tsx` + both message files — warning toast alongside
      the success toast. **Never** a blocking modal.
- [ ] Confirm **no** change to `BankReconciliationService.cs` (§2 C5 deliberate skip). **Done:** absent
      from `git diff --name-only`.

### WP-4 — tests *(after WP-1 and WP-3)*
- [ ] T1–T6, T8, T9 added to `Sprint9WhtComplianceTests.cs`; every one drives the real transition.
- [ ] T7: the four existing tests green, **assertions unmodified**; `:408` gets the §11 comment only.

### WP-5 — knowledge capture *(anytime)*
- [ ] `troubles-wiki.md`: update the "ภ.พ.36 blind spot" entry's **Fix:** section from "not yet
      decided" to what shipped, naming what it does and does not guarantee (§4 I1).
- [ ] `troubles-wiki.md`: new entry for **L1** (PV `DocDate` re-pinned at post → wrong filing month;
      owner = Feature B; must also remove `PaymentVoucherService.cs:496-498`).
- [ ] `troubles-wiki.md`: new entry for the **C6/C7 undetectable shapes** (payments that never touch
      AP), naming E2 as the open CPA question.

---

## Attempt log

- 2026-08-13 opus-designer: spec written. Channel sweep (§2, 16 channels) delegated to a read-only
  Explore agent and verified against source. Key discoveries beyond the brief: (i) `PrefixCode` is
  identical on every poster and `PaymentVoucher` has no `JournalEntryId`, so no structural PV→JE
  discriminator exists — this killed the "list non-PV entries" design as the *primary* signal and
  forced the amount-based detector; (ii) the existing `ApReconciliationAsync` is the wrong instrument
  (as-of, netting, no line items) despite documenting this exact channel; (iii) the parameter-order
  footgun in §3.5 — inserting a `bool` before `ct` would silently rebind `default` at four existing
  call sites and compile clean; (iv) **Feature B does not currently fix L1** (§11) — the post-time
  re-pin at `PaymentVoucherService.cs:496-498` is absent from that spec.

# Testing Swarm Round 2 — Leg 2: Bank Reconciliation — Findings

Worker: findings-only test worker. company_id=1 (Demo Company, VAT-registered 7%).
Environment: FE http://localhost:3000, API http://localhost:5080.

## Pre-flight orientation (source/spec evidence, not yet UI-verified)

- Spec: `specs/bank-reconciliation.md`. Module is FULLY implemented (B1-B5 all [x]).
- D1/B5.1: **v1 has NO stored/locked "reconciliation" entity and NO close/complete action.**
  The reconciliation report (`GET /bank-accounts/{id}/reconciliation?from=&to=`) is a COMPUTED
  query every time; there is no "close period" or "complete reconciliation" button anywhere in
  the matching screen (`bank-accounts/[id]/imports/[importId]/page.tsx`) or elsewhere in FE.
  This directly answers checklist item 1 ("close/complete the reconciliation") and item 3
  ("can a reconciliation close with unmatched lines") — there is no close gate to test; a
  reconciliation report can show `difference == 0` with unmatched lines present as long as
  they're netted into `unmatchedLinesNet` correctly. Will confirm empirically, report as gap
  not bug (by design, spec D1 explicit).
- B2.5 `StatementImportService.ImportAsync`: duplicate/overlapping import is "warn, never
  block" BY DESIGN — creates a full second `StatementImport` + duplicate `StatementLine` rows,
  returns `overlapWarning=true`. To verify empirically: does this corrupt the reconciliation
  report (duplicate unmatched lines with no valid match candidate, since the real
  Receipt/PV is already matched to the first import's lines)?
- Candidate gap: `StatementImportService.ImportAsync`'s try/catch (mapping unexpected
  exceptions to typed `bank.statement_parse_failed` 422) wraps ONLY `adapter.Parse` +
  `BankStatementIntegrity.Validate`. The subsequent `db.SaveChangesAsync` calls (persisting
  `StatementImport`/`StatementLine`) are OUTSIDE that try/catch. `StatementLine.Description`
  is `varchar(500)` with no app-level truncation/validation in the adapter — an oversized
  Description (or TxnType/Channel/RawRef, all capped varchars) could throw a raw
  PostgresException (22001 string data right truncation) at SaveChangesAsync, NOT mapped to a
  typed error. To verify empirically with a >500-char description cell.

## Findings

### L2-1 🟡 Bank-rec modals (Suggest / Journal / statement-import-upload) lack `role="dialog"`
- **Repro:** open the matching screen (`/bank-accounts/{id}/imports/{importId}`), click "ค้นหารายการที่ตรงกัน" (suggest) on any Unmatched line, or "+ นำเข้า Statement" on the bank-account page.
- **Evidence:** `frontend/app/(dashboard)/bank-accounts/[id]/imports/[importId]/page.tsx` `SuggestModal`/`JournalModal` and `frontend/components/bank/StatementImportSection.tsx`'s upload modal all render `<div className="modal modal-open"><div className="modal-box">...` with NO `role="dialog"` (and no `aria-modal`/`aria-labelledby`). Confirmed via Playwright accessibility snapshot: the modal content ("ค้นหารายการที่ตรงกัน" heading + candidate list) appears directly under `main` in the a11y tree, not nested in a `dialog` role, so `page.getByRole('dialog')` finds nothing even though the modal is visibly open.
- **Contrast:** every other create-flow confirm modal in the app (TI/Receipt/PV post-confirm — `PostConfirmDialog`, and `useConfirm()`'s `AlertDialog`) DOES expose `role="dialog"`/`role="alertdialog"`, so this is an inconsistency specific to the 3 bank-rec DaisyUI modals, not house style.
- **Impact:** screen readers won't announce these 3 modals as dialogs (no focus-trap semantics conveyed), and it silently breaks the common `page.getByRole('dialog')` idiom this codebase's own e2e suite uses everywhere else.
- **Expected vs actual:** expected `role="dialog"` (or `alertdialog`) + `aria-modal="true"` matching the rest of the app; actual: a plain unlabelled `<div>`.

## Passes (checklist item 1 — walk, partial)
- Bank account creation/reuse via UI (`/bank-accounts/new`): PASS. co1 bank_account_id=1 (KBIZ, 999-9-627070-1, gl_cash_account_id -> 1120).
- Statement CSV import via real upload UI (`StatementImportSection`): PASS. 4-line synthetic KBiz-format CSV uploaded, parsed exactly (D10 integrity ties out), StatementImport + 4 StatementLine rows persisted atomically. Evidence: importId=3, lineCount=4, opening/closing balance round-tripped exactly through the API (`GET /imports` matches what was uploaded).
- Matching screen: suggest -> confirm for both a MoneyIn line (matched to a real posted Receipt) and a MoneyOut line (matched to a real posted PaymentVoucher), both via the real UI buttons + modal. Both transition Unmatched -> Matched (204, re-verified via GET lines). Suggestions correctly surface ALL exact-amount/date candidates (co1 has several leftover Posted receipts at 1,070.00 — all appeared, D4's "no hard filter, exact-amount ±7day" ranking confirmed loose-but-correct: picking the WRONG same-amount candidate would still succeed since D4 doesn't disambiguate beyond amount+date). Suggest on a line with no valid candidate (the fee line) correctly returns an empty list + "ไม่พบใบเสร็จรับเงิน/ใบสำคัญจ่ายที่ตรงกัน" message, not an error.

### L2-2 🔴 Reconciliation report picks an ARBITRARY import's closing balance when two imports share the same PeriodEnd — tie-out `difference` is silently wrong
- **Repro:** for one bank account, import 2+ KBiz statements whose `PeriodEnd` is the identical calendar date (realistic: re-importing an overlapping/updated export — explicitly SUPPORTED by B2.5's "warn, never block" idempotency design, spec `bank-reconciliation.md` B2.5). Then `GET /bank-accounts/{id}/reconciliation?from=&to=`.
- **Evidence (Postgres, company 1, bank_account_id=1):**
  ```
  statement_import_id | period_end | closing_balance | imported_at
  1                    | 2026-08-19 | 255.0000        | 09:09:57
  2                    | 2026-08-19 | 255.0000        | 09:11:59
  3                    | 2026-08-19 | 255.0000        | 09:12:39
  4                    | 2026-08-19 | 525.0000        | 09:15:05
  5                    | 2026-08-19 | 800.0000         | 09:16:50  <- most recent
  ```
  The report API returned `"statementClosingBalance": 255` — NOT 800 (the actual latest upload). `difference` (computed from that wrong balance) came back `-1020` even though the true reconciling state (GL/deposits/outstanding/unmatched — verified separately, all exact) was fully explained (a hand-derived difference of 0 against import #5's real 800 closing balance).
- **Root cause:** `backend/src/Accounting.Infrastructure/Bank/BankReconciliationReportService.cs`, `GetAsync`:
  ```csharp
  var statementClosingBalance = await db.StatementImports.AsNoTracking()
      .Where(i => i.BankAccountId == bankAccountId && i.CompanyId == tenant.CompanyId && i.PeriodEnd <= to)
      .OrderByDescending(i => i.PeriodEnd)
      .Select(i => (decimal?)i.ClosingBalance)
      .FirstOrDefaultAsync(ct) ?? 0m;
  ```
  `OrderByDescending(i => i.PeriodEnd)` alone has no defined tiebreak on a PeriodEnd tie — Postgres/EF returns physical/plan order, not "most recently imported." No `.ThenByDescending(i => i.ImportedAt)` (or `StatementImportId`).
- **Impact:** the reconciliation report — the artifact whose entire purpose is telling an accountant whether the books match the bank — can silently compare GL against a STALE import's closing balance and show a bogus nonzero `difference` (or, worse, a false "reconciled" `difference == 0` if the stale balance happens to net out) with no indication anything is wrong. Directly collides with B2.5's own documented idempotency behavior (warn-not-block on overlap) — the exact scenario that creates a PeriodEnd tie is one B2.5 explicitly allows to happen.
- **Expected vs actual:** expected the report to always resolve to the most-recently-IMPORTED statement for a given account/period (add `.ThenByDescending(i => i.ImportedAt)` or `.ThenByDescending(i => i.StatementImportId)` as a deterministic tiebreak); actual: arbitrary, observed to pick the STALEST of 5 tied imports in this repro.
- **Note:** this was found via natural repeated same-day test-iteration (5 upload runs against one bank account, all necessarily sharing today's date as PeriodEnd), not a contrived attack — meaning any REAL user who re-imports the same day's statement more than once (very plausible: exporting the statement again mid-day, or an accountant retrying after fixing a mistake) hits this.

## Passes (continued)
- Inline JE (interest line): PASS. Journal posted at the statement line's real date (today), Dr=Cr=5.00 exactly, status Posted, visible at its own `/journals/{id}` detail page + independently confirmed via `GET /journals/{id}` API (totalDebit==totalCredit==5.00, docDate==today).
- Report tie-out (glBalance / depositsInTransitTotal / outstandingPaymentsTotal / unmatchedLinesNet): PASS, exact to the satang, cross-checked against a dynamically-computed "before" baseline (robust to however much leftover test data already existed on co1). A reconciliation CAN legitimately reach a correct/explained state (all 4 components tie) even with one line deliberately left Unmatched (the fee line) — it's carried in `unmatchedLines` and netted correctly, confirming there's no separate "close" gate blocking this (matches the D1 gap noted in pre-flight).
- Report CSV export: PASS. Real "ส่งออก CSV" button -> browser download; verified byte-for-byte: leading UTF-8 BOM (U+FEFF) present, rows joined with explicit `\r\n` (no bare `\n` outside CRLF pairs) — the spec's own "folded footgun" deviation from ap-aging's plain `\n` is correctly implemented.

## Passes (continued)
- Edit door: PASS on all 3 sub-checks.
  - Unmatch a Matched (not Posted) line via the real UI confirm flow -> DB flips to Unmatched, link cleared; re-confirming the SAME match via the API round-trips back to Matched cleanly (idempotent, D8 honoured).
  - Unmatch on a Posted (JE-backed) line correctly 422s `bank.line_posted` with the JE number in the message ("Line posted as JE #50; post a manual adjusting JE to correct.") — D8's immutability rule enforced, not just documented.
  - Bank-account re-save (rename only): PUT succeeds (204), only `bankName` changed; `accountNo`/`bankCode`/`glCashAccountId` unchanged, and re-saving does NOT touch the imports list (row count identical before/after) — no side effects on unrelated state.

### L2-3 🟠 Duplicate/overlapping statement import is warned-not-blocked, but leaves permanently-orphaned StatementLine rows with no recovery path except manual per-line Ignore
- **Repro:** upload the same KBiz CSV twice for one bank account (same file, byte-identical) through the real UI.
- **Evidence:** upload #2 returns 201 with `overlapWarning:true` (correct per B2.5's documented "warn, never block" design — NOT itself the bug) and genuinely creates a SECOND `StatementImport` + 4 more `StatementLine` rows (imports list count went 9->10, confirmed via API). The duplicate's deposit/withdrawal lines can NEVER be matched to the real Receipt/PaymentVoucher they're a copy of — that document is already matched to the ORIGINAL import's line (`ConfirmMatchAsync`'s "not already matched to another line" rule, correctly enforced: `GET .../suggestions` for the duplicate's deposit line does NOT offer `docId===ctx.receiptId`, confirmed empirically). On a clean account (no other same-amount leftover docs) these duplicate lines would show ZERO suggestions and become permanently Unmatched with no legitimate resolution other than the user recognizing them as an accidental duplicate and manually clicking "ข้ามรายการ" (Ignore) on all 4, one at a time, with no bulk action and no "this looks like a duplicate of import #N" hint anywhere in the UI.
- **Consequence:** `unmatchedLinesNet` in the reconciliation report permanently absorbs the duplicate's unresolved lines (observed: 375 -> 630, +255, matching the duplicate's own unmatched-line net once its dep/wd DID find decoy matches among OTHER leftover test receipts — in a clean environment this would be the duplicate's full signed total, e.g. -(receipt+interest)+(pv+fee)). Combined with L2-2 (both new imports share today's PeriodEnd), the report is now doubly unreliable after a duplicate upload: wrong `statementClosingBalance` (L2-2) AND a `difference` polluted by orphaned duplicate lines that can never be matched away (only Ignored).
- **Expected vs actual:** expected either (a) true idempotency (hash/detect byte-identical re-upload and refuse or silently no-op), or (b) at minimum a same-screen affordance to bulk-Ignore or delete an accidentally-duplicated import. Actual: silent full duplication, permanent unmatched-line pollution, zero UI signal that "this import looks like a duplicate of #N" (the toast only says the DATE RANGE overlaps, not "here are the N lines that are byte-identical to a prior import").
- **Note:** this is a design gap the spec itself flagged as accepted risk (B2.5 explicitly chose warn-not-block), not an implementation bug — but the REPORT-side consequence (unresolvable pollution + no bulk remediation) was not called out in the spec and is worth a product decision.

### L2-4 🔴 Oversized statement-line field crashes with a raw `internal_error` 500 (inner Postgres exception leaked in dev), not a typed validation error
- **Repro:** upload a KBiz CSV that is otherwise perfectly valid (D10 integrity ties out exactly) but has one line's รายละเอียด (Description) cell longer than the DB column (600 chars vs `character varying(500)`). `POST /bank-accounts/{id}/imports`.
- **Evidence (actual response body):**
  ```json
  {"type":"urn:teas:error:internal_error","title":"internal_error","status":500,
   "detail":"An error occurred while saving the entity changes. See the inner exception for details. | 22001: value too long for type character varying(500)"}
  ```
  (`detail` is dev-only per `DomainExceptionMiddleware`'s `_env.IsDevelopment()` branch — production would show a generic opaque message, but the status code and lack of a typed `bank.*` error code are the same either way.)
- **Root cause:** `backend/src/Accounting.Infrastructure/Bank/StatementImportService.cs` `ImportAsync` — the `try { adapter.Parse(...); BankStatementIntegrity.Validate(parsed); } catch (DomainException) { throw; } catch (Exception ex) { ...throw new DomainException("bank.statement_parse_failed", ...); }` block wraps ONLY the parse + D10 integrity check. The subsequent `db.SaveChangesAsync(ct)` calls (persisting `StatementImport`, then the `StatementLine`s) are OUTSIDE that try/catch entirely. Neither the `KBizCsvAdapter` nor `BankStatementIntegrity.Validate` enforce any max-length on `Description`/`TxnType`/`Channel`/`RawRef` (all capped `varchar` columns per the `20260708230046_BankReconciliation` migration: description 500, txn_type/channel/raw_ref 100), so a line whose bank-supplied text exceeds those caps sails through parsing+integrity untouched and only fails at the DB layer, escaping as a raw `DbUpdateException` -> generic `internal_error` 500 via `DomainExceptionMiddleware`'s catch-all.
- **Atomicity check (good news):** the whole import is one `BeginTransactionAsync`/`CommitAsync` block; the `await using` transaction auto-rolls-back on the unhandled exception (never reaches `CommitAsync`) — confirmed via Postgres: zero orphaned `StatementImport`/`StatementLine` rows survive this failure. This part of D11/atomicity holds; only the ERROR SHAPE is wrong.
- **Expected vs actual:** expected a typed 422 (`bank.line_field_too_long` or similar, ideally naming the LINE NUMBER only, per D10's own "line numbers + amounts only, never raw text" no-PII convention) caught before/at the SaveChangesAsync boundary; actual: an unhandled `DbUpdateException` mapped only by the generic catch-all, violating this leg's own "typed error, not raw 500" bar and leaking a raw Postgres error class name (`22001`) in dev.
- **Fix shape (not applied — findings-only worker):** either (a) validate each parsed line's field lengths against the known DB caps immediately after `BankStatementIntegrity.Validate` (inside the existing try/catch, so it's caught cleanly), or (b) widen the try/catch to also wrap the SaveChangesAsync calls and translate `DbUpdateException` there.

## Passes (continued)
- Malformed CSV — 3 of 4 handled correctly with typed errors (see L2-4 for the 4th, confirmed bug):
  - Wrong columns (required header missing): 422 `bank.csv_header_not_found`, names the missing column, PASS.
  - Garbage binary content: 422 `bank.csv_malformed` (Rfc4180Reader's strictness catches it before header lookup even runs), PASS.
  - Empty file (0 bytes): 400 `{"detail":"file is required."}`, checked BEFORE the adapter runs, PASS.
  - Oversized field (600-char Description vs varchar(500)): FAILS — see L2-4 (raw 500, `internal_error`), but atomicity holds (zero orphaned rows, verified in Postgres).
- Permission probe (`rbac_sales_staff`, SALES_STAFF role — none of D5's 5 bank.* codes granted): ALL 9 routes (list/create bank accounts, upload statement, suggestions, match, journal, ignore, unmatch, reconciliation report) correctly return 403. PASS, no gaps found.
- GL balance to the satang, independently verified in Postgres (not just the report API): direct SQL `SUM(debit_amount)-SUM(credit_amount)` on `gl.journal_lines` for account_id=2 (1120) = **-566.2500**, EXACT match to the reconciliation report API's `glBalance: -566.25`. Last inline JE (journal_id=66) confirmed Dr=Cr=5.00 exactly, correct accounts (1120 dr, 4300 cr) via direct `gl.journal_lines` query.

## Checklist verdicts
1. **Walk (setup -> import -> match -> adjustments -> close):** PASS through import/match/adjustments via the real UI. "Close/complete" does NOT EXIST as a feature — v1 is a COMPUTED report with no stored/locked reconciliation entity (spec D1, explicit, not a gap). Confirmed via source (no close endpoint, no close button in `ImportMatchingPage`) and via the live report correctly reaching a fully-explained state with one line still Unmatched.
2. **Reconciled balance vs GL to the satang:** PASS. Verified both via the report API (dynamically-derived baseline, exact) and independently via direct Postgres `SUM(debit)-SUM(credit)` on the GL — both agree to the satang.
3. **Unmatched/partial lines — what can the UI do, can a reconciliation "close" with them:** Unmatched lines can be Suggested-and-matched, given an inline JE, or Ignored (reversible). No "close" gate exists at all (see #1) — the report just carries them in `unmatchedLines` and nets them into `unmatchedLinesNet`, which is CORRECT behavior for the formula (confirmed exact tie-out with one line left Unmatched). No partial-match concept exists in v1 (D4: one-to-one exact only, by design).
4. **Duplicate import:** Neither refused nor silently deduped — warned (`overlapWarning:true`) and FULLY duplicated (new StatementImport + 4 new StatementLine rows, confirmed via API + Postgres row counts). See L2-3 (permanent unmatched-line pollution, no bulk remediation) and L2-2 (report picks an arbitrary import's closing balance on a PeriodEnd tie — directly provoked by this scenario).
5. **Adjustment (inline JE) entries:** PASS — correct accounts (bank GL debited/credited per direction, user-picked contra), correct period (statement line's real date, open period), Dr=Cr exact, visible in the journal list module at its own detail page, independently confirmed in Postgres.
6. **Malformed CSV:** 3/4 typed correctly; 1/4 (oversized field) is a raw 500 — L2-4.
7. **Permission probe:** PASS, no gaps — all 9 routes 403 for a role holding none of the 5 `bank.*` permission codes.
8. **Edit door:** PASS — unmatch/rematch of a Matched line is idempotent; unmatch of a Posted line is correctly blocked (D8); re-saving the bank account touches only the edited field, no side effects on imports/lines.

## Findings tally
- 🔴 High: 2 (L2-2 report picks arbitrary import's closing balance on a PeriodEnd tie; L2-4 oversized field crashes with a raw 500)
- 🟠 Medium: 1 (L2-3 duplicate import leaves permanently-orphaned unmatched lines, no bulk remediation)
- 🟡 Low/contract: 1 (L2-1 bank-rec modals lack `role="dialog"`)
- ⚪ Note: 0

## Throwaway specs
- `frontend/e2e/r2-leg2-bank-recon.spec.ts` — 10 tests, `test.describe.serial`, all currently green
  (2 of the 10 deliberately assert the OBSERVED/buggy behavior to lock in repro evidence for
  L2-2 and L2-4 rather than the desired behavior). NOT committed. Run via
  `npx playwright test e2e/r2-leg2-bank-recon.spec.ts` from `frontend/`. Re-running it end-to-end
  creates a fresh Receipt+PV+import each time (by design, for isolation) — safe to re-run, adds
  more of the same shape of leftover data each time.

## Test data left behind (company 1 / Demo Company only; companies 2-4 untouched)
- `bank.bank_accounts` id=1: "KBIZ" / "กสิกรไทย (R2L2 test, renamed)" / 999-9-627070-1, gl_cash_account_id -> 1120.
  (Reused across all runs — pre-existed from an earlier leg per the task briefing, this worker
  only renamed it once via the edit-door test.)
- 14 `bank.statement_imports` rows (ids 1-12, 14, 15 — 13 was consumed by a rolled-back insert,
  harmless Postgres identity-sequence behavior, not a data bug) x 4 `bank.statement_lines` each
  = 56 statement lines, mostly Unmatched/Posted, some Matched.
- 16 `sales.receipts` (RC-0002..RC-0018ish), all Posted, Transfer, 1,070.00 each, dated 2026-08-19.
- 15 `purchase.payment_vouchers`: 12 Posted (800.00 each), 2 Draft, 1 Approved — the Draft/Approved
  ones are from earlier failed dev-iteration runs of this spec (before the Approve/Post
  confirm-dialog fix landed in the test) and are harmless (no GL impact).
- 15 vendors `E2EV-R2L2-*` ("ผู้ขาย e2e r2l2 จำกัด").
- Several GL journal entries (one per successful inline-JE test run) posting Dr 1120 / Cr 4300,
  5.00 each, dated 2026-08-19 — real, immutable, Posted (cannot be undone; intentional per-run
  test evidence, not accidental).
- None of this touches companies 2/3/4.

## Aside (out of scope for this leg, flagging for awareness only)
- `frontend/e2e/_helpers.ts`'s `createVendor()` no longer works standalone: the vendor create
  form now client-blocks Save when "Vendor จดทะเบียน VAT" (checked by default) has no tax id
  ("ผู้ขายจด VAT ต้องระบุเลขผู้เสียภาษี 13 หลัก") — this helper was NOT updated for that
  validation. Worked around locally in this spec (uncheck the box) rather than editing the
  shared helper. Likely also breaks `record-vendor-invoice.spec.ts` and any other spec calling
  `createVendor()` unmodified — not verified against the rest of the suite (out of this leg's
  scope), just flagging.
- Payment Voucher Approve AND Post now each show an extra confirm dialog
  ("ยืนยันการอนุมัติใบสำคัญจ่าย" / "ยืนยันการบันทึกใบสำคัญจ่าย") that
  `payment-voucher-with-wht.spec.ts` and `pv-approval-permission.spec.ts` don't handle (both just
  click Approve/Post and wait for the status text) — likely broken for the same reason if run
  today. Worked around locally in this spec; not fixed in the shared helpers/specs (out of scope).

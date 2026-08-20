# O2b — issue-time reconcile guard for billing notes with linked TIs (Ham ruling, 2026-08-20)

## Ruling
Manual lines stay authoritative (Ham does NOT want option (1)'s auto-generation to become the
*only* path, nor option (3)'s rename). Instead: ISSUE is BLOCKED when a billing note has linked
tax invoices AND its lines don't reconcile with them — typed error naming the difference.

This is additive to the existing 2026-07-26 O2b work (`specs/billing-note-generate-lines-o2b.md`,
option 1 — linking TIs with NO manual lines auto-generates lines). That path is UNCHANGED and
must keep passing; this ruling only adds a guard for the case where a caller links TIs **and**
also supplies/edits manual lines that end up not summing to the linked TIs' total.

## Context (read before touching code)
- `specs/fix-army-findings-2026-07-22.md` §O2b — the original bug: a BN listed ฿6,955 of linked
  TIs while its own total read ฿107.00 (totals never read the join table). Options put to Ham:
  (1) auto-generate from links, (2) keep manual lines authoritative but BLOCK issue when they
  don't reconcile, (3) rename the field to a pure reference tag. Ham chose a hybrid: (1) already
  shipped 2026-07-26 for the no-manual-lines case; this ruling adds (2) on top for the
  manual-lines-with-links case.
- `BillingNoteService.IssueAsync` (backend/src/Accounting.Infrastructure/Sales/BillingNoteService.cs) —
  the only place Draft→Issued happens; the guard belongs here (Issue only, drafts stay free).
- `BillingNoteService.ApplyTaxInvoiceLinesAsync` — the existing generate-from-links path. Copies
  `TI.SubtotalAmount/VatAmount/TotalAmount` verbatim onto generated lines, so `bn.TotalAmount`
  already equals `Σ TI.TotalAmount` by construction whenever it ran. Do not disturb.
  `BuildTaxInvoiceLinksAsync` sets `BillingNoteTaxInvoice.AppliedAmount = TI.TotalAmount` at link
  time (`backend/src/Accounting.Domain/Entities/Sales/BillingNote.cs:99`) — that's the per-link
  "linked TI total" already sitting on the join table, no extra query needed.
- Tests: `backend/tests/Accounting.Api.Tests/Sales/BillingNoteGenerateLinesO2bTests.cs` (private
  helpers `Request`, `ManualLine`, `SeedTaxInvoiceAsync`, `LoadAsync` reused for the new tests —
  same file, same `[Collection(nameof(PostgresCollection))]`).

## Design
### Guard condition — fire ONLY when both a link and a manual/edited line exist
`bn.TaxInvoiceLinks.Count > 0 && bn.Lines.Count > 0` (both already `Include`d by `LoadAsync`).
No need to distinguish "manual" vs "generated" lines by a flag: the pure-generated path always
satisfies `bn.TotalAmount == Σ AppliedAmount` by construction (D3 invariant of the 07-26 spec), so
the guard is a structural no-op for it. It only ever actually refuses when a human edited/replaced
the generated lines, or supplied manual lines alongside links, and the totals drifted.
- No links (`TaxInvoiceLinks.Count == 0`) → guard never fires. Pure-manual BN, untouched.
- Links + zero lines (can't normally happen — the create/update seam either generates ≥1 line per
  surviving link or the caller's own lines win) → guard never fires (nothing to compare against
  the "reconciled by construction" invariant would need Lines.Count > 0 anyway).
- Links + lines, exact match (satang-exact) → Issue proceeds.
- Links + lines, mismatch → `DomainException("billing_note.lines_not_reconciled", ...)`.

### Placement in IssueAsync
Right after the existing `Status != Draft` check, BEFORE `period.EnsureOpenAsync` / number
allocation / GL posting — a pure validation with zero side effects must run before anything that
allocates a number or touches the ledger. **The guard only refuses; it never mutates lines,
totals, or the join table** (money invariant, no `db.SaveChangesAsync` before the throw).

### Comparison
`tiTotal = bn.TaxInvoiceLinks.Sum(l => l.AppliedAmount)` vs `bn.TotalAmount`. Both are `decimal`
(exact, no floating point) already rounded to 2dp by their respective write paths, so a plain `!=`
is satang-exact — no epsilon needed.

### Error
Code `billing_note.lines_not_reconciled` (nearest existing convention: `billing_note.*` prefix,
see the other four codes already thrown in this file). No entry in `DomainExceptionMiddleware`'s
`StatusFor` special-cases needed — falls through to the default 422, consistent with
`billing_note.bad_status` etc. Message carries both totals AND the signed difference (2dp) so the
detail line is self-contained — `problemToast` shows the resolved Thai string as the primary toast
and this EN detail as the secondary `description` line (frontend/lib/api.ts:60-62), so the numbers
are NOT lost behind the Thai key.

### Frontend
One entry in `frontend/lib/i18n/problems.ts`'s `TH` dict, `billing_note.*` section (no such
section exists yet — add one), generic wording (no embedded numbers — Thai can't interpolate a
static dict entry; the numbers survive via `problemToast`'s secondary description line, see
above — this is NOT the `pnd36.unreconciled_figure_changed` / `sso_batch.unencodable_name`
"deliberate no-entry" pattern, because those codes' ONLY message-bearing channel is the single
resolved string via `errorToToast`'s `resolveProblemKey(code) ?? detail`, which really would
destroy the number; `problemToast` (what the BN issue action actually uses,
`useBillingNoteAction` → generic `apiPost` action mutation) keeps both).

## Implication to record for Ham (do not silently resolve, just note it)
This guard blocks **partial billing of a linked TI**: once a TI is linked, its full `AppliedAmount`
must be reflected somewhere in the BN's manual lines (or the BN must regenerate/stay
fully-generated) for Issue to succeed. Billing only PART of a linked invoice now requires either
(a) not linking that TI at all (put its info in a manual line instead, unlinked), or (b) linking
no TIs on that BN. There is no supported way today to link a TI and legitimately bill less than
its full total on the same BN. Flagging this as a known consequence of the ruling, not a bug.

## Checklist
- [x] RED: mismatched manual lines + linked TI → `IssueAsync` throws `billing_note.lines_not_reconciled`
      with both totals + difference in the message. Confirmed RED (guard absent) before
      implementing: 1 failed ("no exception was thrown"), 8 passed.
- [x] GREEN: same guard, exact-match manual lines → `IssueAsync` succeeds.
- [x] GREEN regression: pure-generated path (no manual lines) → `IssueAsync` still succeeds
      (reconciles by construction).
- [x] Frontend: one Thai entry added to `problems.ts` for `billing_note.lines_not_reconciled`
      (new `billing_note.*` section). `tsc --noEmit` clean.
- [x] Gates: targeted new tests green (9/9); `BillingNoteGenerateLinesO2bTests` full class green
      (regression — the 07-26 generate-from-links path is undisturbed); `TaxCodePairIntegrityTests`
      green (10/10, regression); `InvoiceFlowTests`/`BillingNoteSettlementDeletionTests`/
      `NonVatBillingTests` green (15/15, extra IssueAsync-path sanity net); full `Sales` namespace
      green (137/137). `dotnet build` clean (0 warnings, 0 errors).

## Attempt log
- 2026-08-20: RED confirmed via isolated same-depth `-o` build
  (`backend/tests/Accounting.Api.Tests/bin/Debug/net10.0-isolated`, per troubles-wiki.md's
  "sibling leaf folder one level under the test project's own bin/Debug/" fix — the live
  `Accounting.Api.exe` dev server on :5080 was left running per dispatch instruction, so the
  shared `bin/` was avoided entirely). Guard added to `IssueAsync` right after the Draft-status
  check, before `period.EnsureOpenAsync`/number allocation/GL posting. Rebuilt, reran — GREEN.
  Broadened regression sweep (TaxCodePairIntegrityTests, InvoiceFlowTests,
  BillingNoteSettlementDeletionTests, NonVatBillingTests, full `.Sales.` namespace) — all green,
  no unrelated breakage from the new guard.

## Gates
Targeted `dotnet test --filter` on the new tests + `BillingNoteGenerateLinesO2bTests` +
`TaxCodePairIntegrityTests`. `dotnet build` for the touched projects. No `tsc`/`next build`
required — `problems.ts` is a pure data dict, no component changed. Cap: `BillingNoteService.cs`
+ test file + `problems.ts` + this spec = 4 files, well under the 6-file cap.

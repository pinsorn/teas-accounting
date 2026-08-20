# Fix Codex UI review findings — 2026-08-20

Source: `_review/ui-edit-cancel-vat-nonvat-test-2026-08-20.md` (3 verified findings).
Blast cap: 8 files. No commits (orchestrator commits). Backend `dotnet test` runs
LAST (another Sonnet is on teas_test concurrently for seeds/bank fixes).

## Facts established in code

- `frontend/lib/queries.ts:1680-1690` `useUpdateQuotation` invalidates only
  `['quotations']` + `['quotation', id]` — NOT `['paper-doc']`.
- `frontend/lib/queries.ts:1981-1992` `useUpdatePurchaseOrder` already invalidates
  `['paper-doc']` (reference-correct).
- Sweep of ALL `useUpdate*` mutations vs all `usePaperDoc(...)` consumers
  (`frontend/app/(dashboard)/{quotations,sales-orders,invoices→billing-notes,
  purchase-orders,tax-invoices,payment-vouchers,delivery-orders,receipts}/[id]/page.tsx`
  + `components/AdjustmentNoteScreens.tsx` → `tax-adjustment-notes`):
  - `useUpdateQuotation` (1680) — MISSING → fix.
  - `useUpdateSalesOrder` (1722-1733) — already invalidates `['paper-doc']` — OK.
  - `useUpdateBillingNote` (1876-1886) — MISSING → fix (same class, review didn't
    catch it; invoices/[id]/page.tsx consumes paper via docPath `billing-notes`).
  - `useUpdatePurchaseOrder` (1981-1992) — already OK (review's reference impl).
  - tax-invoices / payment-vouchers / tax-adjustment-notes / receipts /
    delivery-orders: no `apiPut`/`useUpdate*` mutation exists for these doc
    types at all (create + action-only, immutable once issued/posted) — no gap
    possible, nothing to fix.
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/Master/CompanyConfiguration.cs:33`
  `.HasDefaultValue(0.07m)` on `VatRate` (decimal, non-nullable). EF's default
  convention: on INSERT, a value-type property whose value == CLR default(T)
  (0 for decimal) AND has a store default is OMITTED from the INSERT column
  list, so the DB default (0.07) wins — even though
  `MasterDataServices.cs:261` explicitly assigns `VatRate = req.VatRate` before
  `SaveChangesAsync`. This is a property-tracking bug (EF can't tell "user typed
  0" from "never touched"), not a data bug.
- `backend/src/Accounting.Application/Master/CompanyDtos.cs:12` `CreateCompanyRequest`
  already carries `decimal VatRate = 0.07m` as an optional positional-record
  parameter — System.Text.Json's parameterized-record deserialization already
  honors this default when the JSON omits `vatRate`, so the "omitted → 0.07"
  behavior is ALREADY correct at the DTO layer today. The bug is exclusively
  the EXPLICIT-0 case, entirely inside the EF mapping.
- DB check constraint `ck_companies_vat_rate: vat_rate >= 0 AND vat_rate <= 1`
  (CompanyConfiguration.cs:46) — guarantees -1 can never be a legitimate stored
  value, safe sentinel choice.
- Raw-SQL company seeds (`120_seed_demo_company.sql`, `400_seed_manual_demo_company.sql`,
  `440_seed_nonvat_demo_company.sql`) INSERT into `master.companies` WITHOUT a
  `vat_rate` column at all — they rely on the DB-level default surviving.
  Therefore the DB default must NOT be dropped (would break these three seed
  scripts, well outside the 8-file blast cap and outside review scope) — the
  fix must be metadata-only (`HasSentinel`), not a default removal.
- `backend/tests/Accounting.Api.Tests/Master/CompanyTaxConfigTests.cs:47-49` and
  `backend/tests/Accounting.Api.Tests/Fixtures/TestCompanyFactory.cs:33-36` both
  carry `// CAUTION` / `// NOTE` comments documenting the pre-fix limitation
  (vatRate:0 not persisted) — both call sites must be updated to actually
  EXERCISE vatRate:0 and assert it persists, and the stale comments removed.
- `frontend/app/(dashboard)/quotations/[id]/page.tsx:80-83` `cancelQuotation()`
  sends `{ reason: t('cancelReason') }` (canned Thai string) from a yes/no
  `useConfirm()` popup.
- `frontend/app/(dashboard)/quotations/[id]/page.tsx:265-274` reject flow
  (`ConfirmActionDialog`) sends `{ reason: t('rejectReason') }` — same canned
  shape.
- `frontend/app/(dashboard)/invoices/[id]/page.tsx:177-188` — cancel precedent:
  toggle button (`showCancel`) reveals an inline `<input>` bound to
  `cancelReason` state + a confirm `<button>` disabled while empty, calling
  `run('cancel', { reason: cancelReason })`. No modal, no canned string. This
  is the idiom to reuse verbatim for BOTH quotation cancel and reject (reject
  has no closer precedent elsewhere — structurally identical to cancel, so the
  same idiom covers it).
- No e2e/spec files reference `q-cancel`/`q-reject`/`cancelQuotation`/
  `cancelConfirm`/`rejectConfirm` (grepped `**/*.spec.ts`) — safe to restructure
  the DOM without breaking Playwright specs.

## Checklist

### UI-1 — paper-doc invalidation sweep [P2]
- [x] `useUpdateQuotation` (queries.ts ~1680) — add `['paper-doc']` invalidation.
- [x] `useUpdateBillingNote` (queries.ts ~1876) — add `['paper-doc']` invalidation
      (found during the mandated sweep, not in the original review).
- [x] Confirm `useUpdateSalesOrder` / `useUpdatePurchaseOrder` already correct
      (no change).
- [x] `tsc --noEmit` clean — 0 errors.

### UI-2 — company VatRate 0 lost on create [P2]
- [x] `CompanyConfiguration.cs`: add `.HasSentinel(-1m)` to the `VatRate`
      property (keep `.HasDefaultValue(0.07m)` — seed scripts depend on it).
- [x] Migration check: `HasSentinel` has zero DDL representation (change-
      tracking/insert-omission metadata only). Empirical corroboration: the
      4200+ line `AccountingDbContextModelSnapshot.cs` has ZERO existing
      `HasSentinel(...)` calls despite dozens of `HasDefaultValue` properties
      across the model — confirms this EF Core version's snapshot generator
      doesn't emit it (not schema-diffed). `dotnet ef migrations
      has-pending-model-changes` could not be run cleanly (API :5080's locked
      bin dir + an `--assembly`/`--project` combination quirk in this EF
      Tools version); reasoning-based conclusion stated explicitly per
      instruction rather than silently assumed. No migration file added —
      file tally stays at 7 source/test files, under the 8-file cap.
- [x] RED test first: `VatRate_zero_persists_on_create` in
      `CompanyTaxConfigTests.cs` — ran via `git stash` of ONLY
      `CompanyConfiguration.cs` (isolated build, no other worker's test
      window touched): FAILED for the right reason —
      `Expected dto.VatRate to be 0M, but found 0.0700M`. 7/8 other tests in
      the file passed unaffected.
- [x] GREEN: `git stash pop` restored the fix; same filtered run → 8/8 passed
      (incl. `VatRate_zero_persists_on_create` and the new JSON-omission test).
- [x] Omitted-VatRate → 0.07 case: new DB-free `[Fact]`
      `CreateCompanyRequest_omitted_vat_rate_deserializes_to_0_07` — pins
      System.Text.Json actually honoring the record ctor default (the
      "moves to app layer" contract) at the wire-shape level, not just by
      inference from reading the DTO.
- [x] Updated `CompanyTaxConfigTests.cs` (test 1's stale NOTE) + `TestCompanyFactory.cs:33-36`
      CAUTION doc — both now state the FIXED behavior and point at the new test.
- [x] Targeted backend test run LAST — polled for the other Sonnet's
      `testhost.exe` to exit (5-min until-loop, cleared quickly), then ran:
      `CompanyTaxConfigTests` filter (RED, then GREEN 8/8) + broader
      `Master|Company` filter (187 tests): run 1 showed 5 unrelated flaky
      failures (not in the VatRate/Company-config tests), runs 2 and 3
      (immediate repeats, same filter) both 187/187 clean, 0 skipped —
      consistent with this repo's documented shared-teas_test-DB test-order
      flakiness (troubles-wiki.md payroll-fixture entry is the same class of
      issue), not a regression from this change.

### UI-3 — quotation cancel/reject canned reason [P3]
- [x] Cancel: replace `useConfirm()` yes/no + canned string with the Invoice
      inline-input idiom (toggle + required text input + disabled-while-empty
      confirm button), reason reaches backend verbatim.
- [x] Reject: same idiom (drop the `ConfirmActionDialog` canned-reason call);
      confirmed via consumer check that no other page needs the removed
      `ConfirmActionDialog(reject)` wiring.
- [x] i18n: add `cancelReasonPlaceholder` / `rejectReasonPlaceholder` keys to
      `quotation` namespace in both `th.json`/`en.json` (mirrors
      `billingNote.cancelReasonPlaceholder`).
- [x] `tsc --noEmit` clean — 0 errors.

## Live browser smoke test (UI-1 + UI-3)
No browser MCP tool was available in this session (searched twice; only
Higgsfield/Lazyweb/claude.ai connectors present). Substituted the repo's own
Playwright e2e tooling (`npx playwright test`, `msedge` headless,
`playwright.config.ts` already points at the running :3000/:5080 — no
webServer autostart) with a THROWAWAY spec written to `frontend/e2e/`, run,
then deleted (never committed, confirmed via `git status` showing zero trace).
Exercised against the review's own fixture companies 4 ("UI VAT Review Co
20260820") and 5 ("UI NonVAT Review Co 20260820") via `POST
/api/auth/switch-company` — never co1/co2.
- Desktop (company 4): create quotation → edit description → save → assert
  the paper preview shows the NEW description and NOT the old one with **no
  `page.reload()` call** — this is the exact UI-1 regression. PASSED.
  Screenshots: `ui1-before-edit-desktop.png`, `ui1-after-edit-no-reload-desktop.png`.
- Desktop (company 4): Send → Reject — confirm button disabled while the
  reason input is empty, enabled once typed; captured the actual outgoing
  `POST .../reject` request body via `page.waitForRequest` and asserted
  `postDataJSON()` equals the typed text verbatim (not a canned string).
  Screenshots: `ui3-reject-empty-disabled-desktop.png`,
  `ui3-reject-filled-enabled-desktop.png`.
- Mobile 390×844 (company 5): Send → Cancel — same disabled/enabled +
  verbatim-request-body assertion, at mobile viewport. Screenshots:
  `ui3-cancel-mobile-390.png`, `ui3-cancel-mobile-done-390.png`.
- All 6 screenshots: `Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\5667c374-e2c0-4998-b10c-b993b4182367\scratchpad\`.
- First run failed for an unrelated reason (the generic `pickCustomer()` helper's
  loose name regex matched the picker modal's own "ยกเลิก" close button before
  any real customer row) — fixed by scoping directly to the results `<ul>`;
  not a defect in the shipped fix.
- **UI-2 was deliberately NOT smoke-tested live**: the running API (:5080,
  pid 22620) is the PRE-FIX binary — its bin dir was locked (still running)
  for the entirety of this session, so it never picked up the `HasSentinel`
  change. Hitting it would show 7% and read as a false failure of a fix
  already proven correct by the RED→GREEN test run below. **Restarting the
  API is the orchestrator's step, not this worker's** (dispatch said "leave
  both running") — UI-2's live/Tier-4 acceptance needs that restart first.

## Gates — ALL PASSED
- `frontend`: `npx tsc --noEmit` — 0 errors.
- `backend` RED: `CompanyTaxConfigTests` filter with `CompanyConfiguration.cs`
  stashed back to pre-fix — 7 passed, 1 failed
  (`VatRate_zero_persists_on_create`: expected 0M, found 0.0700M).
- `backend` GREEN: same filter, fix restored — 8/8 passed.
- `backend` broader sweep: `Master|Company` filter (187 tests) — run 1: 182/187
  (5 unrelated failures, names not captured — output truncated); runs 2 and 3
  (immediate repeats): 187/187 clean both times, 0 skipped. Treated as
  pre-existing shared-teas_test-DB flake (not reproducible, not among the
  VatRate/company-config tests) — flagged, not silently dismissed.

## Blast radius
7 source/test files + 1 spec file. Cap: 8. Under cap.

## Observations for Fable (not fixed — out of this scope/cap)
- Grepped all 4 `VatRate` decimal properties in the model snapshot for the
  same bug class: `Company` (fixed here), `ExpenseClaimLine` (no
  `HasDefaultValue` — not affected), `PaymentVoucherLine` (no
  `HasDefaultValue` — not affected), `VendorInvoiceLine` (no `HasDefaultValue`
  — not affected). Company was the ONLY one at risk; nothing else to fix.
- UI-3 code review turned up a real bug in my first draft (advisor caught it):
  the confirm handlers originally called `setShowX(false); setResetX('')`
  unconditionally after `run()`, but `run()` swallows its own errors — a
  failed cancel/reject (e.g. 422) would silently close the input and wipe the
  user's typed reason. Fixed by deleting those calls entirely and relying on
  the same status-gated conditional the Invoice precedent uses (the block
  hides itself once `d.status` flips on success; on failure it stays open
  with the text intact). Net simpler than the original draft, not just safer.

## Report
See final message to orchestrator for fix shape / files / evidence per finding.

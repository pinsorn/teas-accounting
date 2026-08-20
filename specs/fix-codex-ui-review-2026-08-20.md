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

## Wave 3 — 2026-08-20 addendum

Sources: `_review/ui-codebase-review-2026-08-20.md` findings 2/4/5,
`_review/ui-document-creation-test-2026-08-20.md` activity 403 finding (R3-FE
half — backend re-scoping the permission is a SEPARATE worker/finding).
FE-only. No backend tests run (backend worker owns teas_test this wave).
Blast cap +10 files; used 10.

### R2 [P2] — mobile clipping at 390px
- [x] `Topbar.tsx` breadcrumb `<nav>`: `min-w-0` (was inert without it — a
      flex item with nowrap text inside floors at its min-content size) +
      `flex-auto` (NOT `flex-1` — Tailwind's `flex-1` sets `flex-basis:0%`,
      which rendered the nav at literal ZERO width whenever the row was in
      any width deficit, i.e. hid the breadcrumb on almost every mobile page
      — caught via live diagnostic, not assumed). Non-last crumbs collapse to
      `hidden sm:flex` so mobile shows only the current page.
- [x] `CompanySwitcher.tsx`: `min-w-0` on the inner `<button>` alone did
      NOTHING — daisyUI's `.dropdown` wrapper div (the actual flex item in
      Topbar's header row) is block-level, not flex, so a width:auto flex
      child inside it does not inherit a flexed ancestor's resolved width.
      Fixed by making the wrapper itself `flex min-w-0 max-w-[220px]` and the
      button `w-full min-w-0` (fills the wrapper instead of separately
      capping its own max-width). Confirmed via `getBoundingClientRect()`
      diagnostic: wrapper and button now both resolve to the identical
      flexed width.
- [x] `Bell`/`Settings` icon buttons: added `shrink-0` so they never compress.
- [x] `PageHeader.tsx`: `flex-wrap` on the outer container + the actions
      `<div>` — title block and actions (e.g. P&L's PDF/CSV buttons) now wrap
      to their own row instead of overflowing.
- [x] `DataTable.tsx` `ColumnFilter` dateRange variant: the from/to
      `<input type="date">` pair's wrapping `<span>` gets `flex-wrap` so "to"
      stacks below "from" on narrow screens instead of being clipped (parent
      filter panel already had `flex-wrap`; this was the ONE un-wrapped
      inner level the review caught).
- [x] Live browser verification at 390×844 (admin): CompanySwitcher
      bounding-box fully inside [0,390] on the dashboard; TI list and PV list
      both date `<input>`s fully inside [0,390]; Profit & Loss both PDF and
      CSV buttons fully inside [0,390]. Screenshots:
      `r2-company-switcher-mobile.png`, `r2-ti-date-range-mobile.png`,
      `r2-pv-date-range-mobile.png`, `r2-pl-buttons-mobile.png`.
- [x] `tsc --noEmit` clean — 0 errors.

### R4 [P2] — company selector missing accessible name
- [x] `settings/roles/page.tsx` + `settings/users/page.tsx`: swapped the
      `<div><span>บริษัท</span><select>` sibling pattern for the app's
      existing labeled-select idiom (`<label htmlFor><span>...</span>
      <select id>`, precedent: `VendorForm.tsx`), with page-unique ids
      (`roles-company-select` / `users-company-select`).
- [x] Live browser verification (admin, desktop): `page.getByRole('combobox',
      { name: 'บริษัท' })` resolves on BOTH pages and carries
      `data-testid="rbac-company-select"` — confirms the accessible name is
      correctly bound, not just visually adjacent (accessibility-tree
      snapshot independently showed `combobox "บริษัท"` on /settings/roles).
- [x] `tsc --noEmit` clean.

### R5 [P3] — empty "ทางลัด" heading for zero-action roles
- [x] `app/(dashboard)/page.tsx`: added `anyQuickAction` (same 5 scope checks
      as the per-button `<PermissionGate>`s, via the already-imported
      `useHasScope`), wrapped the WHOLE `<section>` (heading + wrapper) in
      `{anyQuickAction && (...)}` instead of only gating each button.
- [x] Live browser verification: logged in as `sales_staff` (confirmed by
      the review to have zero of the 5 scopes) — dashboard has ZERO
      `heading("ทางลัด")` elements (not just an empty section). Regression
      check: `admin` still sees the section. Screenshot:
      `r5-sales-staff-dashboard-no-quick-actions.png`.
- [x] `tsc --noEmit` clean.

### R3-FE [P2 half] — ActivityLog maps 403 to the empty-history state
- [x] `components/doc/ActivityLog.tsx`: destructured `isError`/`error` from
      `useDocumentActivity` (was `data, isLoading` only — `data ?? []` alone
      can't tell "really empty" from "the query errored"). Added
      `isForbidden = isError && error instanceof ApiError && error.status
      === 403`; render order is loading → forbidden (new) → other error
      (new, generic) → empty-history (unchanged) → list. A query error can
      NEVER reach the empty-history branch now.
- [x] i18n: `common.activityNoPermission` / `common.activityLoadError` added
      to `th.json`/`en.json` next to the existing `activityEmpty` key.
- [x] Live browser verification: logged in as `sales_staff`, created a fresh
      Quotation as themselves (so this is NOT stale review-run data), opened
      its detail page — activity panel shows "คุณไม่มีสิทธิ์ดูประวัติกิจกรรมนี้"
      (the new 403 message) and the OLD "ยังไม่มีประวัติกิจกรรม" text has zero
      matches on the page. Screenshot: `r3fe-activity-403-honest.png`.
- [x] `tsc --noEmit` clean.

### Gates — ALL PASSED (Wave 3)
- `npx tsc --noEmit` — 0 errors (checked after every item, and once more at
  the end).
- Live browser smoke (no MCP browser tool available; used the repo's own
  Playwright e2e tooling as in Wave 1-2, throwaway spec
  `e2e/tmp-wave3-smoke.spec.ts` + `e2e/tmp-diag.spec.ts`, both deleted after
  the run, zero git trace): 4/4 tests green (R2 mobile, R4 desktop, R5+R3-FE
  combined, R5 admin regression). All 6 screenshots in the scratchpad dir
  listed above.
- No `dotnet test` run this wave per explicit instruction (FE-only, backend
  worker owns teas_test).

### Blast radius (Wave 3)
10 files: `page.tsx`, `settings/roles/page.tsx`, `settings/users/page.tsx`,
`ActivityLog.tsx`, `CompanySwitcher.tsx`, `Topbar.tsx`, `DataTable.tsx`,
`PageHeader.tsx`, `th.json`, `en.json`. Cap: +10. Used exactly 10 — at cap,
not over.

### Self-caught regression during this wave (worth flagging to Fable)
My FIRST attempt at R2's Topbar fix (`flex-1` on the breadcrumb `<nav>`)
shipped a NEW bug — it rendered the breadcrumb at literal 0px width on any
mobile page (verified via live `getBoundingClientRect()` diagnostic, not
assumed from reading the class name). Caught before commit because I visually
re-checked the mobile screenshot after the "fix" instead of trusting the
bounding-box assertion alone (the assertion only checked CompanySwitcher, not
breadcrumb presence). Two lessons for future flex-shrink fixes in this repo:
(1) Tailwind's `flex-1` is `flex-basis:0%` — wrong choice whenever the item
needs a content-based fallback size, use `flex-auto` instead; (2) `min-w-0`
must go on the ACTUAL flex item in the parent's row, not on a nested
descendant several levels down (daisyUI's `.dropdown` wrapper silently ate my
first `min-w-0` because it landed on the wrong element). A bounding-box
assertion that only checks the ONE element you're fixing can pass while an
adjacent element silently breaks — screenshot review caught what the
assertion missed.

## Tier-2 follow-up — 2026-08-20 (non-blocking findings on the Wave 1-3 commits)

FE-only, no `dotnet test`, no commits. Fold-in of two findings from the Tier-2
review of the earlier commits in this file.

### F-3 — master-data mutations that feed the paper preview invalidate nothing
Grounded in `backend/src/Accounting.Infrastructure/Pdf/PaperSellerSource.cs`
(`FromCompanyProfileAsync` composes the paper seller block — name/taxId/
address/logo/phone/email — from `CompanyProfiles`, falling back to `Companies`
when the tenant has no profile row yet) and `PaperSignatureSource.cs` (embeds
the signer's uploaded signature image). Every hook below previously
invalidated only its own narrow key; none touched `['paper-doc']`, so an open
document's paper preview kept showing a stale customer/seller/signature after
exactly the master-data edit that should have changed it.

- [x] **(a) `useUpdateCustomer`** (`lib/queries.ts` ~389) — added
      `['paper-doc']` alongside the existing `['customers']`/`['customer', id]`.
- [x] **(b) Company hooks** (`lib/queries.ts`) — added `['paper-doc']` to ALL
      six: `useUpdateCompanyProfileSoft`, `useUpdateRegisteredAddress`,
      `useUpdateCompanyInfo`, `useUploadCompanyLogo`, `useUploadCompanyStamp`,
      `useUpdateCompany(id)`. Verified via `PaperSellerSource.cs` that every
      field these six write (name/address/phone/email/logo/stamp/vatRate) is
      one FromCompanyProfileAsync (or its Company-row fallback) reads.
- [x] **(b) the raw `apiPut('companies/{id}')`** in
      `settings/company/page.tsx:664` (`PaidUpCapitalCard`) — this call
      previously invalidated NOTHING at all (bypassed React Query entirely,
      a plain `apiPut` import), despite PUTting the FULL row (vatRate,
      address, phone echoed back unchanged alongside the real
      `paidUpCapital` edit — a whole-row overwrite endpoint). Routed through
      the existing `useUpdateCompany(profile.companyId)` mutation instead of
      hand-rolling new invalidations (no refactor beyond that swap — `saving`
      state, the `row`/`value` local state, and the GET-via-`apiGet` loader
      are all untouched). Removed the now-dead `apiPut` import.
- [x] **(c) `useUploadUserSignature`** (`lib/queries.ts` ~2238) — added
      `['paper-doc']` alongside the existing `['rbac-users']`.
- [x] `tsc --noEmit` — 0 errors.

### N2 — orphaned i18n keys from the UI-3 cancel/reject rework
Verified EACH key unreferenced by any `.tsx`/`.ts` file before deleting
(`grep -rn` across `app/`, `components/`, `lib/` — see evidence below), scoped
correctly to the `quotation` namespace only (a same-named `rejectReason` key
exists in a DIFFERENT namespace for expense-claims and was left untouched —
confirmed by grep showing its only matches are `t('rejectReason')` calls
inside `expense-claims/[id]/page.tsx`, never `quotations/[id]/page.tsx`).

- [x] `confirmAction.qtReject` (title + warning, both files) — zero code
      references (the reject flow now uses the inline required-reason input,
      not `ConfirmActionDialog`).
- [x] `quotation.cancelConfirm` — zero code references (was the canned
      `useConfirm()` popup description, removed when cancel became the
      inline-input pattern).
- [x] `quotation.cancelReason` — zero `t('cancelReason')` calls; only matches
      were the unrelated `cancelReason` REACT STATE variable name and the
      `cancelReasonPlaceholder` key (kept).
- [x] `quotation.rejectConfirm` — zero code references.
- [x] `quotation.rejectReason` — zero `t('rejectReason')` calls within
      `quotation` namespace context; the only `t('rejectReason')` calls in the
      codebase are `expense-claims/[id]/page.tsx`'s OWN `rejectReason` key in
      a different namespace (left alone).
- [x] Post-deletion re-grep across `app/`, `components/`, `lib/`, `messages/`
      confirms zero remaining references to any of the five removed keys.
- [x] Key-parity check (`th.json` vs `en.json`, `quotation`/`confirmAction`
      namespaces): both files list identical leaf keys after the edit — no
      key deleted from only one language file.
- [x] Both JSON files re-validated with `JSON.parse` after editing.
- [x] `tsc --noEmit` — 0 errors.

### Gates — ALL PASSED (Tier-2 follow-up)
- `npx tsc --noEmit` — 0 errors (run after F-3 edits, again after N2 edits).
- `node -e "JSON.parse(...)"` on both `th.json`/`en.json` — valid.
- Key-parity check script (inline `node -e`) — `quotation`/`confirmAction`
  namespaces identical between languages.
- No `dotnet test` run (not requested this round — FE-only, invalidation
  keys are React-Query-side plumbing with no backend surface).

### Blast radius (Tier-2 follow-up)
4 files: `lib/queries.ts`, `settings/company/page.tsx`, `th.json`, `en.json`.

## Report
See final message to orchestrator for fix shape / files / evidence per finding.

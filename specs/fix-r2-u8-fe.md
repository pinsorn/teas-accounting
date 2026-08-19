# fix-r2-u8-fe — U8 FE half (PLAN-fix-findings-r2.md §U8)

FE-only. Blast cap: 6 files. Backend untouched, no `dotnet test` (test DB owned by another worker).

## Scope
1. **L3-12** — Draft fixed-asset edit page. `PUT /fixed-assets/{id}` (`UpdateDraftAsync`) already
   exists and refuses non-Draft (`fixed_asset.not_editable`, `backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs:151-157`).
   No edit UI exists (`app/(dashboard)/fixed-assets/` only has `new/` and `[id]/`).
2. **L2-1** — 3 bank-rec DaisyUI modals lack `role="dialog"`/`aria-modal` (findings-r2/findings-leg2.md L2-1):
   `SuggestModal` + `JournalModal` in `[id]/imports/[importId]/page.tsx`, upload modal in
   `components/bank/StatementImportSection.tsx`. App idiom everywhere else:
   `className="modal modal-open" role="dialog" aria-modal="true"`.

## Plan (6 files, matches cap exactly)
Pattern source: `expense-claims/[id]/edit/page.tsx` + shared `ExpenseClaimForm` (parameterized by
`edit?: ExpenseClaimDetail`). fixed-assets `new/page.tsx` is NOT currently split into a form
component — extracting it mirrors the expense-claims idiom exactly and is the only way to reuse
the create form for edit without duplicating ~180 lines of JSX.

- [x] `frontend/components/forms/FixedAssetForm.tsx` — NEW. Extract `new/page.tsx` body into a
      component taking `{ edit?: FixedAssetDetail }`, mirroring ExpenseClaimForm's create/update
      branch (`useCreateFixedAsset` / `useUpdateFixedAsset`, both already in `lib/queries.ts`).
      Title: `isEdit ? tc('edit') : t('create')` (no new i18n keys — stays in the file cap).
- [x] `frontend/app/(dashboard)/fixed-assets/new/page.tsx` — thin wrapper: `<FixedAssetForm />`.
- [x] `frontend/app/(dashboard)/fixed-assets/[id]/edit/page.tsx` — NEW. Mirrors
      expense-claims' edit page: `useFixedAsset(id)`, redirect to detail if `status !== 'Draft'`
      (mirrors the API's own refusal), else `<FixedAssetForm edit={d} />`.
- [x] `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx` — added an Edit button (Link +
      Pencil icon, exact idiom copied from expense-claims/[id]/page.tsx's own edit button) next
      to Activate/Cancel (Draft-only), gated the same `fixedasset.manage` PermissionGate already
      used for Activate/Cancel on that row.
- [x] `frontend/app/(dashboard)/bank-accounts/[id]/imports/[importId]/page.tsx` — added
      `role="dialog" aria-modal="true"` to `SuggestModal` (line 197) and `JournalModal`
      (line 252) modal divs.
- [x] `frontend/components/bank/StatementImportSection.tsx` — same attrs on the upload modal
      (line 84).

## Gate
`npx tsc --noEmit` from `frontend/`. No Playwright (API down per dispatch); tsc is the only gate.

Ran: `npx tsc --noEmit` from `frontend/` → no output, exit code 0. Clean.

## Attempt log
- Single pass, no retries needed. File count: exactly 6 (matches cap) —
  `git status --porcelain -- frontend/` showed 4 modified + 2 new (FixedAssetForm.tsx,
  `[id]/edit/` dir) and nothing else.
- Verified via backend read (no edit) that `PUT /fixed-assets/{id}` → `UpdateDraftAsync`
  throws `fixed_asset.not_editable` when status != Draft
  (`FixedAssetService.cs:151-157`) — FE guard in the new edit page mirrors this exactly,
  same pattern as expense-claims' edit page mirroring its own API's Draft/Rejected guard.
- `role="dialog"`/`aria-modal="true"` idiom confirmed via grep across the whole frontend
  (`className="modal modal-open" role="dialog" aria-modal="true"` used identically in
  ~15 other files: payroll, settings/*, SessionExpiredModal, PostConfirmDialog, etc.) —
  applied the exact same two attributes, nothing more.

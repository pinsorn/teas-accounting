# Fix CN/DN residual nits — list shows internal TI id + draft not deletable

Source: REPORT-vat-dummy-test.md residual nits (2026-07-19 eyeball pass). Ham: "แก้เลย".

## N-1 — CN/DN list column "ใบกำกับภาษีเดิม" shows "#1" (internal id)
- [x] /credit-notes and /debit-notes list pages: the ใบกำกับภาษีเดิม column must show
      the original TI's doc number (e.g. 07-2026-TI-0001), not "#<id>".
      A CN/DN can only reference a POSTED TI, so docNo always exists — no fallback
      needed beyond a defensive null-safe render. If the list endpoint's DTO lacks the
      TI docNo, add it to the BE list projection (single JOIN, no N+1; do NOT fetch
      per-row from FE).
      Evidence: `AdjustmentNoteListItem.OriginalTiDocNo` added; `ListAsync` does a
      single `.Join(_db.TaxInvoices, ...)` (no per-row fetch). FE column now
      `accessorFn: r => r.originalTiDocNo ?? '#'+id` (defensive fallback kept, same
      pattern already used on the detail page). Live-verified in browser (see Gates).

## N-2 — CN/DN draft cannot be deleted
- [x] Add draft-only delete for adjustment notes (CN + DN, shared service):
      follow the repo's existing draft-delete pattern for other document types
      (find one — e.g. payroll run delete or quotation/SO draft delete — and mirror
      its endpoint shape, RBAC permission convention, and Draft-only guard).
      Guard: status Draft ONLY (Posted → 4xx domain error, immutability per ม.86/4).
      Evidence: `TaxAdjustmentNoteService.DeleteDraftAsync` mirrors
      QuotationChainServices/BillingNoteService.DeleteDraftAsync exactly (load →
      status guard → DomainException `note.cannot_delete_after_post` → Remove →
      SaveChanges). `DELETE /tax-adjustment-notes/{id:long}` gated by the SAME
      OR-set as create (`sales.credit_note.create` / `sales.debit_note.create` /
      super-admin) — CN/DN has no combined "manage" perm like Quotation/SO, so no
      new permission code needed.
- [x] FE: delete button on the CN/DN draft detail page (and/or list row per existing
      convention elsewhere), destructive confirm dialog (same app-level confirm used
      by bank unmatch), Thai toast, redirect to list after delete.
      Evidence: `AdjustmentNoteDetailView` — `note-delete-action` button (Draft-only,
      scope-gated) → `useConfirm()` destructive dialog → `useDeleteAdjustmentNote()` →
      `toast.success(tc('deleted'))` → `router.push(c.base)`. Live-verified (see Gates).
- [~] Cleanup: after deploy, delete the test draft CN #2 (฿535) on co5 via the new
      button — explicitly NOT the worker's step per spec (Fable/browser, post-deploy
      on PROD). Not touched.

## Gates
- [x] tsc --noEmit + next build pass — both clean, 0 errors (frontend/, full route
      manifest built incl. /credit-notes, /debit-notes).
- [x] dotnet build + affected BE test class(es) green; ADD one test: delete Draft ok,
      delete Posted rejected (mirror existing delete-guard test style) — new
      `TaxAdjustmentNoteDeleteTests` (2 tests): `Draft_note_can_be_deleted` (asserts
      row gone from DB) and `Posted_note_cannot_be_deleted` (drives the REAL
      CreateDraftAsync→PostAsync transition first, then asserts DeleteDraftAsync
      throws `note.cannot_delete_after_post` — never seeds Posted directly). Full
      suite: 899 pass / 8 skip / 0 fail (baseline 897/8 + these 2 new tests).
- [x] grep "ম" over changed files = 0 — confirmed on all 10 changed/new files.
- [x] RBAC: no new permission code (gated by existing CreditNoteCreate/DebitNoteCreate).
      **Found + fixed a real regression along the way**: `RbacCartesianTests` failed
      because the new DELETE endpoint wasn't in `RbacEndpointInventory.AssertionOverrides`
      (its curated OR-set map for `RequireAssertion`-gated routes) — without an entry
      the test infers ZERO required perms for the route and expects DENY for every
      non-super role, but the real assertion legitimately ALLOWS CreditNoteCreate/
      DebitNoteCreate holders → 4 role mismatches (got 404 not 403). Fixed by adding
      `["DELETE /tax-adjustment-notes/{id:long}"] = [creditnote.create, debitnote.create]`
      to the override map (test-infra wiring only, not an RBAC seed — **no new
      SqlScript**). RbacCartesianTests + RbacAuthMapTests green after the fix.
- [x] Attempt log below

## Attempt log
- Found existing patterns: draft-delete convention = QuotationChainServices /
  BillingNoteService / PayrollRunService `DeleteDraftAsync` (load → status guard →
  domain exception → hard delete, no activity-log entry since the row is gone).
  CN/DN's permission model is split Create/Post/Read (no combined Manage like
  Quotation/SO), so delete is gated by Create (mirrors "whoever can draft it can
  un-draft it").
- N-1: single `.Join` added to `TaxAdjustmentNoteService.Read.cs` ListAsync; new
  `OriginalTiDocNo` field on `AdjustmentNoteListItem` (only one construction site).
- N-2: `DeleteDraftAsync` added to `ITaxAdjustmentNoteService` + implementation +
  `DELETE /tax-adjustment-notes/{id:long}` endpoint; FE button/dialog/toast/redirect
  in `AdjustmentNoteScreens.tsx`; `useDeleteAdjustmentNote()` hook in `queries.ts`.
- First full-suite run surfaced 2 failures: (1) `RbacCartesianTests` — real
  regression from the new endpoint missing its `AssertionOverrides` entry, fixed as
  described above; (2) `WhtFormPdfFillTests.Pnd54_renders_one_sheet_per_ma70_payment`
  (8 vs 4 sheets) — confirmed PRE-EXISTING/flaky and unrelated (no file in this diff
  touches WHT/PND54 code; it passed clean on every subsequent re-run, including the
  final full-suite gate). Not touched, not in scope.
- Live browser smoke test (desktop, 1568px — see report body for full narrative):
  logged in as super-admin against `teas_test` (had to `switch-company` to
  company_id=1 since the JWT's default company landed on an unrelated bloated
  test company); created a real Draft CN referencing a live TI via the actual
  create form; confirmed the list AND detail pages render the real TI doc number
  (07-2026-TI-3645) instead of `#<id>`, on both /credit-notes and /debit-notes;
  clicked the new delete button → destructive confirm dialog → confirmed → Thai
  toast → redirected to list → row gone from the refetched list. Self-cleaning
  (the smoke-test draft no longer exists — deleted via the feature itself).
  Mobile-viewport (390×844) resize did not take effect in this session (confirmed
  via `window.innerWidth` still 1920 after `resize_window` calls) — tooling
  limitation, not re-attempted further; the diff adds no new custom layout/CSS
  (reuses the existing `DataTable` column pattern and the exact `btn btn-danger
  btn-sm gap-1` class already shipped on the Quotation delete button).

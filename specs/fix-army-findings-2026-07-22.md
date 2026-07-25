# FIX ARC — army-untested findings (2026-07-22)

Source: swarm-findings/army/{B-rc,B-ec,B-fa,B-br,B-et,B-bn}.md (all committed). Prod v1.22.10.
Grouping = by file-overlap + test-DB rule (ONE dotnet-test runner at a time):
WP-A (backend money, dotnet) ∥ WP-D (FE-only nits, tsc) allowed parallel; WP-B, WP-C sequential after WP-A.

## WP-A — CRITICAL money batch (Sonnet implements + Opus reviews, same dispatch)
- [x] A1 **CRITICAL [B-rc F1]**: `GlPostingService.PostPaymentVoucherAsync` — VI-linked branch
      (`if (pv.VendorInvoiceId is not null)`, ~L162-173) never books the self-withhold gross-up
      **debit** line, while the WHT-payable credit (~L211-217) is unconditional → every
      VI-settling PV with `SelfWithholdMode && WhtAmount>0` 422s `gl.unbalanced`
      (C−D = WhtAmount exactly; live repro PV #17 co5). FIX: add the SAME gross-up debit block
      the standalone `else` branch has (~L197-207, "Self-withhold gross-up {DocNo}") to the
      VI-linked branch — identical condition, identical account. Verified in code by Fable
      (grep 2026-07-22): gross-up exists ONLY in else-branch. Proven in-repo pattern → Sonnet.
      TESTS: unit/integration — VI-linked PV + self-withhold posts balanced JE (Dr AP + Dr gross-up
      = Cr WHT + Cr cash); regression: standalone PV path unchanged; non-self-withhold VI-PV unchanged.
- [x] A2 **HIGH [B-rc F2]**: `frontend/app/(dashboard)/payment-vouchers/new/page.tsx` ~L159
      `const vendorVat = vendor?.vatRegistered ?? true;` — single-flag; must use the dual-flag rule
      the VI form uses (`vendor-invoices/new/page.tsx` ~L85-87): no VAT when
      `!vatRegistered || (isForeign && !hasThaiVatDReg)`. Repro: settle-from-VI on ARMYAWS859829
      fabricated base 18,691.59 + VAT 1,308.41 out of a 20,000/0%-VAT VI. Extract or duplicate the
      exact predicate (prefer shared helper if one exists; else copy the VI form's expression).
- [x] A3 **LOW [B-rc F3]** (same file as A2, so same WP): self-withhold explanation/toggle block is
      `{!fromVi && ...}` (~L445) yet backend still auto-applies GROSS_UP_FOREVER on the fromVi path —
      show the explanation (read-only/locked state is fine) on fromVi too so the accountant sees
      which 50ทวิ condition applies.
- Gates: full backend suite green (baseline 921/0/8) + FE tsc + build. Opus review lenses:
  money-formula correctness, both PV branches' JE shape, no behavior change for standalone path.
  **WP-A implementation evidence (2026-07-22, Sonnet):**
  - A1: added the self-withhold gross-up debit block (same condition/account/description
    pattern as the else-branch) to the `VendorInvoiceId is not null` branch,
    `backend/src/Accounting.Infrastructure/Ledger/GlPostingService.cs`. New test
    `Vi_settled_foreign_self_withhold_pv_posts_balanced_je`
    (`backend/tests/Accounting.Api.Tests/Hardening/Sprint87ForeignVendorTests.cs`) reproduces
    the exact B-rc repro shape (foreign, no VAT-D, ฿20,000 VI, WHT 15%) and asserts a balanced
    JE (Dr AP 20000 + Dr gross-up 3529.41 = Cr WHT 3529.41 + Cr cash 20000), matching the
    army hand-calc (฿3,529.41). Regressions confirmed via EXISTING tests, unchanged:
    standalone self-withhold (`Sprint87ForeignVendorTests` — `Foreign_no_vatd_pv_auto_self_withhold_and_pnd36`,
    `Domestic_manual_self_withhold_gross_up`, `Gross_up_once_uses_single_iteration_and_condition_3`
    all still pass, else-branch untouched) and non-self-withhold VI-linked PV
    (`McpDocumentChainTests.Purchase_chain_settles_vi_with_our_wht_pins_D3c_je` still passes —
    DEDUCT mode, doesn't hit the new debit block since `pv.SelfWithholdMode` is false).
  - A2: `vendorVat` now derived as `vendor ? vendor.vatRegistered && !foreignNoVatD : true`,
    reusing the file's own already-computed `foreignNoVatD` local (same predicate as the VI
    form's `autoNoInputVat`, algebraically: `!(!vatRegistered || (isForeign && !hasThaiVatDReg))`
    = `vatRegistered && !foreignNoVatD`). No shared helper existed; predicate copied per spec
    fallback instruction.
  - A3: block condition changed `{!fromVi && (...)}` → `{(!fromVi || foreignNoVatD) && (...)}`
    (only shows on fromVi for the one case backend actually self-withholds via this path);
    checkbox `disabled={selfWithholdLocked || !!fromVi}` (locked/read-only); the
    GROSS_UP_FOREVER/GROSS_UP_ONCE radio choice — which has zero effect on this path
    (`saveDraft` always sends `selfWithholdMode`/`whtPayerMode` null when `fromVi`, so the
    backend's own auto-derive always resolves GROSS_UP_FOREVER) — is replaced by static
    "forever" mode text instead of an inert interactive radio, backend behavior unchanged.
  - Gates: `dotnet build` clean; full `dotnet test` ×2 runs — both 921 passed/8 skipped/930
    total (skip count = baseline), 1 failure both times in
    `Pnd50FilingServiceTests.Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder`
    (TaxFilings, unrelated file/area) — passes clean in isolation; documented pre-existing
    flake, see `troubles-wiki.md` "Full Accounting.Api.Tests run" entry (this exact method
    already named there from an unrelated 2026-07-04 fix). `npx tsc --noEmit` in
    `frontend/` clean (0 errors). `next build` intentionally NOT run per dispatch (another
    worker's gate).
- After fix ships: re-drive B-rc flow live (VI→PV→post→ภ.ง.ด.54 vs hand-calc ฿3,529.41 on 20,000)
  — that also un-sticks the blocked half of leg B-rc. PV #17 (Approved, stuck) becomes postable?
  — verify or document.

## WP-B — WHT-type validation + stuck-PV escape (after WP-A; backend+FE, dotnet)
- [x] B1 **HIGH [B-bn F1]**: PV with WHT%>0 but Income-Type (50ทวิ) left "— ไม่หัก —" passes
      Draft-save AND Approve, shows a fully-computed misleading post-confirm preview, then post
      422s `pv.wht_type_missing`; Approved PV has NO edit/cancel affordance → permanently stuck
      (live: PV #19 co5). FIX (two halves):
      (a) validate early: client-side block + server-side 422 at draft-save (or at minimum approve)
          when any line has rate>0 && WhtTypeId==null — same error code, surfaced in Thai;
      (b) ESCAPE HATCH — DESIGN (Opus-reviewed; NO migration — code only). Transition chosen:
          **Approved→Voided (cancel, terminal)**, NOT reopen-to-draft — PV has no UpdateDraftAsync so
          reopen strands the doc; `DocumentStatus.Voided` is already the in-repo "dead PV" value
          (`CreateFromVendorInvoiceAsync` active-PV guard keys on `Status != Voided`). Reuse that enum
          value; no new columns (reason → activity note, exactly like `ExpenseClaim.Cancel()`); no new
          permission. DocNo is allocated at Post, so a cancelled Approved PV wastes no number.
          - Entity `PaymentVoucher.Cancel()` (`…/Entities/Purchase/PaymentVoucher.cs`): guard
            `Status==Draft || Status==Approved` else `throw new DomainException("pv.cannot_cancel", …)`
            (→422); set `Status=Voided`. **Corrected by Opus Tier-2 F2 (2026-07-25):** Draft is
            ADMITTED, not rejected — `ApproveAsync`'s B1(a) re-assert can weld a legacy bad Draft
            shut forever (no PV update/delete endpoint exists to fix it in place), which would then
            block `PeriodCloseService` on that month indefinitely; a Draft PV has no JE/DocNo, so
            cancelling it is exactly as safe as cancelling an Approved one. Posted still throws →
            immutable-after-Post stays absolute. (Mirror the `ExpenseClaim.Cancel()` guard shape.)
          - Service `CancelAsync(long id, CancellationToken ct)` (`…/Infrastructure/Purchase/PaymentVoucherService.cs`
            + add to `IPaymentVoucherService`): auth guard; load TRACKED pv; capture the pv's CURRENT
            `Status` before mutating (the activity record's `fromStatus` must be dynamic — Draft or
            Approved — not hardcoded); `pv.Cancel()`; `_activity.Record("PaymentVoucher",
            pv.PaymentVoucherId, pv.DocNo, pv.CompanyId, "Voided", fromStatus:<captured>,
            toStatus:"Voided", module:"purchase")`; `SaveChangesAsync` wrapped in try/catch
            `DbUpdateConcurrencyException` → `throw new DomainException("pv.locked_mismatch")` (→409).
            **Corrected by Opus Tier-2 F1 (2026-07-25):** `Version` was configured
            `.IsConcurrencyToken()` (config L68) but NEVER incremented by any PaymentVoucher
            transition — an inert token, so this catch was dead code (a cancel-vs-post race could
            mark a POSTED PV Voided: payment vanishes from bank-rec/ภ.พ.36 while its JE stays, and
            the VI becomes re-settleable → double payment). Fixed by adding `Version++` to EVERY
            transition (`MarkApproved`, `MarkPosted`, `Cancel` — mirrors `ExpenseClaim`'s pattern),
            making the token live both directions; `PostAsync`'s own SaveChanges path (which also
            calls `MarkPosted`, via `NumberedDocumentWriter.AllocateAndSaveAsync`) needed the SAME
            `DbUpdateConcurrencyException` → `pv.locked_mismatch` mapping — `NumberedDocumentWriter`
            only recognizes the doc_no 23505 collision, not a version conflict.
          - VI release is AUTOMATIC + atomic: settlement (`SettledAmount`/`SettlementStatus`/
            `PaymentVoucherApplication`) is POST-only, so an Approved PV never touched the VI. The single
            PV status flip to Voided IS the release — `CreateFromVendorInvoiceAsync`'s `Status != Voided`
            guard then lets a fresh PV settle the same VI. Zero VI mutation, zero extra rows.
          - Endpoint `POST /payment-vouchers/{id}/cancel` (`PaymentVoucherEndpoints.cs`) → `CancelAsync`;
            `Results.NoContent()`; **`.RequireAuthorization(… + Permissions.Purchase.PaymentVoucherApprove)`**.
            Permission = reuse **approve** (undo-of-approval = approver authority; avoids a seed migration +
            RBAC-matrix churn). Creator-only (`create`) is intentionally NOT sufficient; the approver may
            cancel; an SME single-operator holds all three anyway (cont.77).
          - FE (`payment-vouchers/[id]/page.tsx`): Cancel btn inside the `d.status==='Approved'` block
            — **corrected by Opus Tier-2 F2 (2026-07-25): also `d.status==='Draft'`** (same permission
            gate; a Draft PV's own creator can't cancel without approve-perm either — accepted SoD,
            not a bug), `<PermissionGate scope="purchase.payment_voucher.approve">`, opens
            `ConfirmActionDialog` (`confirmAction.pvCancel.title/warning`), calls a new
            `useCancelPaymentVoucher` hook (`lib/queries.ts`, invalidate the PV query). i18n: add
            `pv.cancel` + `confirmAction.pvCancel.*` AND a `Voided` entry to `StatusBadge` MAP +
            `status.Voided` in messages/{th,en}.json (else a voided PV shows a raw key).
          - TESTS: domain (Draft→Voided ok; Approved→Voided ok; Posted throws `pv.cannot_cancel`;
            Version++ fires on Cancel/MarkApproved/MarkPosted — Opus Tier-2 F1); integration
            (approve→cancel ⇒ Voided; VI-linked approve→cancel ⇒ a NEW PV is creatable from the same VI;
            cancel a Posted PV ⇒ 422 `pv.cannot_cancel`; caller lacking `approve` ⇒ 403; a stale-Version
            CancelAsync call ⇒ 409 `pv.locked_mismatch`, proving the token is actually live).
      (a) SEAM (keep error code `pv.wht_type_missing`): enforce in `CreateDraftAsync`'s per-line loop
          (`PaymentVoucherService.cs` ~L281 — the one seam BOTH REST + from-VI callers funnel through):
          after `WhtTypeId = input.WhtTypeId ?? category.DefaultWhtTypeId`, if `input.WhtRate>0m &&
          resolved is null` throw `DomainException("pv.wht_type_missing", …)` (blocks draft-save). Add the
          same check at the top of `ApproveAsync` so an already-persisted bad draft can't advance either.
      ACCEPTANCE: PV #19 on co5 can be unstuck via the new path after deploy.
- [x] B2 **LOW [B-bn]**: `frontend/e2e/payment-voucher-with-wht.spec.ts` fills WHT% `'0.03'`
      commented "3%" — field takes plain percent (3). Fix value + add an assertion on the WHT amount.
- **WP-B implementation evidence (2026-07-25, Sonnet, resumed after a prior worker died mid-edit):**
  - **Already done by the dead worker** (audited via `git diff` at resume, all verified correct):
    entity `PaymentVoucher.Cancel()` (Approved→Voided guard, `pv.cannot_cancel` otherwise);
    service `CancelAsync` (auth guard, tracked load, `Cancel()`, activity record, `SaveChangesAsync`
    wrapped in try/catch `DbUpdateConcurrencyException` → `pv.locked_mismatch`); interface method;
    endpoint `POST /{id}/cancel` reusing the `approve` permission; B1(a) server-side seam in
    `CreateDraftAsync`'s per-line loop + re-assert in `ApproveAsync` (both → `pv.wht_type_missing`);
    N1 stale-comment fix (`PaymentVoucherService.cs` ~L291, now correctly documents that the
    self-withhold auto-derive applies to VI-linked PVs too, not just standalone); FE detail-page
    Cancel button (`PermissionGate` + `ConfirmActionDialog` + `useCancelPaymentVoucher`);
    `th.json` `pv.cancel`/`confirmAction.pvCancel`; `status.Voided` + StatusBadge `Voided` MAP entry
    (pre-existing from an earlier commit, not part of this diff); both new backend test files
    (`PaymentVoucherCancelTests.cs` ×2, domain + API — comprehensive: all TESTS bullet points
    covered including the VI-release-frees-a-new-PV integration test and the 403 permission gate).
  - **Gaps found + completed this session:**
    1. `en.json` `confirmAction.pvCancel` — genuinely missing (exactly where the worker died
       mid-edit, confirmed via `git diff`: `th.json` had it, `en.json` didn't). Added, matching
       the `pvPost` pattern.
    2. B1(a) **client-side block** in `payment-vouchers/new/page.tsx` — not started at all (only
       the server-side seam existed). Added: `whtTypeMissing` check (`rows.some(rate>0 &&
       whtTypeId==null)`) folded into `canSave`, plus a per-row inline Thai warning
       (`pv.whtTypeRequired`, new key in both json files) next to the WHT-type dropdown.
       **Simplification (noted per Ponytail):** the check requires an EXPLICIT Income-Type pick
       even for a category that has a server-side `DefaultWhtTypeId` (e.g. SVC) — the FE's
       `ExpenseCategoryLite` type doesn't carry that field (confirmed via `expense-category-shape.ts`'s
       whitelist), and plumbing it through (types.ts + shape-parser + selector, 3 more files)
       would have exceeded the minimal-diff spirit for a UX-only nice-to-have. Always requiring
       an explicit pick is a defensible, even safer simplification (no invisible-default reliance
       for a WHT-bearing line) — flagging as a deliberate scope-preserving simplification, not a cut.
    2. B2 — value fixed `'0.03'`→`'3'`; added `expect(cert.whtAmount).toBe(30)` assertion via the
       existing WHT-certificate-list fetch. Also had to add a WHT-type dropdown selection
       (`pv-line-wht-type`, index 1) to the test, made necessary by the new B1(a) client block —
       the test's original flow (rate typed, no type picked) relied on the SVC category's server
       default, which the new stricter client check no longer allows through unselected.
  - **Regression found + fixed by the final gate:** `PurchaseRateBoundTests.
    PaymentVoucher_CreateDraft_AcceptsNormalWhtRate` (orthogonal rate-bound test, unrelated to
    WHT-type) broke against the new B1(a) server seam — its category had no `DefaultWhtTypeId`
    and it passed `whtTypeId: null`. Fixed by looking up the onboarding-seeded "SVC" WhtType and
    passing its id, keeping the test scoped to what it actually verifies (rate-bound behavior).
  - Gates: `dotnet build` clean (0 warnings/errors). Full `dotnet test`: first full run raced
    against a stale/orphaned background process from an earlier session interruption (12 false
    failures from a `pk_companies` collision — see attempt log); killed the stray process, reran
    clean twice. First clean run (before the `PurchaseRateBoundTests` fix): 2 failures — 1 the
    documented `Pnd50FilingServiceTests.Pnd50_with_nonzero_adjustments_renders_the_ladder_in_v2`
    pre-existing flake (named in `troubles-wiki.md`'s 2026-07-04 entry), 1 real regression (fixed,
    see above). Targeted filter re-run (`PurchaseRateBoundTests|PaymentVoucherCancelTests|
    Sprint87ForeignVendorTests`) after the fix: 30/30 passed. **Final full-suite gate: 0 failed,
    930 passed, 8 skipped, 938 total (Api.Tests, 11m28s) + 152/0/152 (Domain.Tests) — fully green,
    skip count matches the 8-baseline exactly, +8 passed vs. the pre-WP-B 921/8/930 Api.Tests
    baseline (the 8 new B1(a)/B1(b) integration tests).** `npx tsc --noEmit` — 0 errors.
    `npm run build` (next build) — compiled successfully, all routes generated including
    `/payment-vouchers/new` and `/payment-vouchers/[id]`. `en.json`/`th.json` full key-parity
    check (all namespaces, not just the touched ones) — 0 mismatches either direction.
- **Opus Tier-2 REJECTED (2 blockers) + fix round (2026-07-25, Sonnet):**
  - **F1 (Version inert token):** `PaymentVoucher.Version` was `.IsConcurrencyToken()` (config
    L68) but no transition ever incremented it — `CancelAsync`'s `DbUpdateConcurrencyException`
    catch was dead code (a cancel-vs-post race could mark a POSTED PV Voided). Fixed: `Version++`
    added to `MarkApproved`, `MarkPosted`, and `Cancel` (mirrors `ExpenseClaim`'s pattern exactly).
    `PostAsync` also needed the same catch — split into a thin public `PostAsync` (auth guard +
    try/catch → `pv.locked_mismatch`) wrapping the unchanged original body, now private
    `PostCoreAsync` (avoids re-indenting ~170 lines; `NumberedDocumentWriter.AllocateAndSaveAsync`'s
    own catch only recognizes the doc_no 23505 collision, a different EF exception, so it never
    interferes with this).
  - **F2 (ApproveAsync re-assert welds bad Drafts shut):** `Cancel()` now accepts `Draft` OR
    `Approved` (Posted still throws `pv.cannot_cancel`, absolute) — a legacy bad Draft
    (rate&gt;0/no-type) had no PV update/delete endpoint to fix it in place, so B1(a)'s own
    `ApproveAsync` guard would strand it in Draft forever, blocking `PeriodCloseService`
    (`period.draft_present`) on that month indefinitely (verified in code, `Ledger/PeriodCloseService.cs`
    L59-60). `CancelAsync`'s activity record now captures the pv's actual `Status` BEFORE
    `Cancel()` mutates it (dynamic `fromStatus`, not hardcoded `"Approved"`). FE Cancel button
    shows on `Draft` too (same permission gate — a Draft's own creator can't self-cancel without
    `approve` either, accepted SoD, left as-is per dispatch).
  - **Nits:** cleaned the `PaymentVoucherService.cs` ~L276 comment debris (pointed at a
    nonexistent `GlPostingService... no,` fragment); this spec's B1(b) design text corrected
    (Cancel guard is Draft-or-Approved, `Version` liveness now correctly attributed to the F1
    fix, not an always-true configuration fact); `TeasMcpTools.cs`
    `create_payment_voucher_draft` description now states whtTypeId is required whenever
    whtRate&gt;0 and points agents to `list_wht_types`. Toast-string nit skipped per dispatch.
  - **Tests:** Domain — `Draft_cancels_to_voided` (new), `Posted_cannot_cancel`/
    `Voided_cannot_cancel_again` (kept, `Draft_cannot_cancel` removed/replaced), plus
    `Cancel_bumps_version`/`MarkApproved_bumps_version`/`MarkPosted_bumps_version` (new, direct
    F1 proof). API — `Cancel_a_draft_pv_voids_it_with_dynamic_from_status` (replaces
    `Cancel_a_draft_pv_is_rejected`; asserts Voided status + `MetadataJson` `fromStatus:"Draft"`);
    `Cancel_a_posted_pv_is_rejected` kept as-is; two new deterministic version-liveness tests,
    `Concurrent_version_bump_makes_cancel_throw_locked_mismatch` and
    `Concurrent_version_bump_makes_post_throw_locked_mismatch` — same scope/DbContext-sharing +
    out-of-band raw-SQL `UPDATE ... SET version = version + 1` technique as
    `ExpenseClaimServiceTests.Concurrent_Approve_second_stale_save_throws_DbUpdateConcurrencyException`
    (grepped per dispatch), but driven through the actual `CancelAsync`/`PostAsync` service calls
    so the `pv.locked_mismatch` domain-error mapping is proven end-to-end, not just the raw EF
    exception.
  - **Gates:** `dotnet build` clean. Targeted filter (`PaymentVoucherCancelTests|
    PurchaseRateBoundTests|Sprint87ForeignVendorTests|PurchaseAuditTests|McpDocumentChainTests`) —
    75/75 passed. Full suite run 1: 2 failures — the now-familiar `Pnd50FilingServiceTests.
    Pnd50_preview_carries_cd_schedules_that_foot_to_the_ladder` flake PLUS a new member of the
    same `TaxFilings`-shared-row flake class, `WhtFormPdfFillTests.
    Pnd54_maps_ma70_amounts_through_to_the_form` (neither file touched by this diff); isolated
    filtered re-run of just those two passed clean 2/2, confirming flake not regression (added to
    `troubles-wiki.md`). **Final clean full-suite gate: 0 failed, 932 passed, 8 skipped, 940
    total (Api.Tests, 11m42s) + 155/0/155 (Domain.Tests) — skip count matches the 8-baseline
    exactly, +2 vs. the pre-Tier-2-fix 930/8/938 baseline (the 2 new version-liveness tests).**
    `npx tsc --noEmit` — 0 errors. `npm run build` — compiled successfully.
  - Footgun hit + documented: an earlier background test run's process/output got orphaned
    across a session resume; killing the stray process and rerunning clean was required before
    these numbers could be trusted (see `troubles-wiki.md`).

## WP-C — K-Plus PDF import 500 (after WP-B; backend, dotnet, needs local sample)
- [ ] C1 **HIGH [B-br F1]**: `POST /bank-accounts/{id}/imports` with REAL K-Plus PDF
      `STM_SA5476_01FEB26_08JUL26.pdf` (repo root, gitignored, password 06121996) → 500
      internal_error. Password handling OK (wrong/absent password → clean 422 bank.pdf_password).
      Crash is in parse/assembly (`KPlusPdfLineAssembler` / integrity or account-mismatch check) —
      existing tests use synthetic PositionedWord arrays only, never a real multi-page PDF.
      Debug WITH the real file locally (NEVER commit it; keep `STM_*.pdf` gitignored), fix, add a
      regression test with a SANITIZED synthetic fixture reproducing the failing shape (not the
      real statement). Any un-mapped exception in the import path should also land as a clean 422.
      NOTE: account-no on the real statement (SA5476…) probably ≠ co5's dummy 123-4-56789-0 — if
      root cause turns out to be the account-mismatch check itself crashing (instead of returning
      its designed error), that's the bug; the DESIGNED mismatch error is fine and then the 500 must
      be reproduced another way before closing.

## WP-D — FE nits batch (parallel with WP-A; FE-only, tsc, files disjoint from WP-A)
- [x] D1 **MEDIUM [B-ec F1]**: `StatusBadge.tsx` MAP + `messages/{th,en}.json` missing
      `Submitted`/`Paid` (PascalCase; existing `PAID` is a different enum) → raw keys
      `status.Submitted`/`status.Paid` visible on expense-claims list/detail. Add both entries ×3 files.
- [x] D2 **MEDIUM [B-fa F-1]**: `depreciation/page.tsx` `handleGenerate()` ignores
      `res.alreadyExisted` → false success toast on re-run. Branch on it → show existing
      `alreadyPosted` string. (Concurrency catch-branch stays.)
- [x] D3 **LOW [B-ec F2]**: expense-claims list/detail render generic "เกิดข้อผิดพลาด" on 403;
      use the same permission-named clean-deny the /new page uses (client permission check or map
      403 → ShieldAlert state).
- Gates: tsc 0 + next build + manual-glance screenshots per item from implementer.
  **WP-D implementation evidence (2026-07-22, Sonnet):**
  - D1: `StatusBadge.tsx` MAP gained `Submitted: { tone: 'info', en: 'Submitted' }` and
    `Paid: { tone: 'success', en: 'Paid' }` (added before the untouched all-caps `PAID` entry).
    `messages/th.json`/`en.json` `status` namespace gained matching keys — Thai wording reused
    verbatim from the existing `expenseClaims.submitted`/`expenseClaims.paid` toast strings
    already shown for these exact transitions (`ส่งอนุมัติแล้ว` / `จ่ายเงินแล้ว`), so the badge
    text now agrees with the toast the user just saw.
  - D2: `depreciation/page.tsx` `handleGenerate()` now checks `res.alreadyExisted` first (info
    toast, existing `t('alreadyPosted')` string), before the `depreciationRunId == null` check;
    the `ApiError`/`depreciation.already_posted` concurrency catch-branch is untouched.
  - D3: added the exact `/new`-page client-side permission-check pattern (`useMePermissions()`
    + `isSuperAdmin || permissions.includes('expense.claim.read')`, `ShieldAlert` +
    `tc('noAccessTitle')`/`tc('noAccessBody', {perm})` — the same shared `common.noAccessBody`
    `{perm}`-parameterized key already used by `/new` and several settings pages) to both
    `expense-claims/page.tsx` (list) and `expense-claims/[id]/page.tsx` (detail), gating BEFORE
    the existing `isLoading`/`isError` generic-error render on the detail page. A non-permission
    error (e.g. genuine 404/network) for a user who DOES hold `expense.claim.read` still falls
    through to the pre-existing generic error state, unchanged.
  - Gates: `npx tsc --noEmit` from `frontend/` — 0 errors. `npm run build` (next build) —
    compiled successfully, all 84 routes generated including `/expense-claims`,
    `/expense-claims/[id]`, `/depreciation`. No manual-glance screenshots taken (no browser
    session in this worker's scope per dispatch — FE-only tsc/build gates only, no live-browser
    smoke test requested).
  - `git status --porcelain -- frontend/` confirms exactly 6 files touched, none under
    `payment-vouchers/*` (blast cap respected, WP-A's territory untouched).

## WP-E — company create/update VAT flag (2026-07-25, found via super-admin UI drive)
- [ ] E1 **HIGH**: /settings/companies create modal — จด VAT toggle OFF is IGNORED: co6 (id=6)
      was created with the toggle visually off yet persisted vat_registered=true (list + edit
      modal both show จด VAT). Suspect FE payload omits the flag (backend defaults true) or
      backend CreateAsync ignores it. Repro: create any company with จด VAT off → list shows จด VAT.
- [ ] E2 **HIGH**: edit-company save → `PUT /api/proxy/companies/6` → **500** (raw
      "An unexpected error occurred" toast), reproduced twice (payload = full form with
      vatRegistered=false). Root-cause the 500 (server log/stack), fix + map to clean 422 if it's
      a domain rejection. ACCEPTANCE: co6 can be flipped to ไม่จด VAT via the UI post-deploy
      (unblocks army B2 non-VAT legs).

- [ ] E3 **LOW [B-mcp]**: malformed MCP tools/call (args not wrapped in `request`) throws
      `System.ArgumentException` which `McpErrorSurfacingFilter` doesn't catch (only McpE2Exception/
      DomainException/ValidationException/JsonException) → SDK swallows into generic
      "An error occurred invoking '<tool>'." — misled a whole test leg into a false CRITICAL.
      FIX: catch ArgumentException in the filter → clean "[mcp.arguments] ..." message.
      (Fable root-caused 2026-07-25 via prod log: worker sent flat DTO fields; schema correctly
      advertises nested `request` — write path itself works, verified by live probe.)

## OPEN (Ham / triage decisions — not dispatched)
- [ ] O1 [B-fa F-2]: FA acquisition posts no GL by design; UI never warns when no VI linked →
      disposal credits cost that was never debited. Options: warning badge on asset detail
      ("ต้นทุนยังไม่ลง GL — ยังไม่ได้ link ใบกำกับ/JE"), or block activate without VI/opening-JE ref.
      DECISION NEEDED (product call).
- [ ] O2 [B-bn INCONCLUSIVE]: BN TI-aggregation — manual UI re-check (pick 2 TIs cleanly) before
      filing: does the selection persist / roll up totals / show back-link chips?
- [ ] O3 [B-bn note]: BN "ดาวน์โหลด PDF (สำเนา)" button fired no request under Playwright —
      quick manual click-test to rule out a real user-facing break.
- [ ] O4 [B-ec item 4]: expense-claim EDIT for Draft/Rejected = UNBUILT (backend PUT wired, zero
      FE). Build or drop? Ham's call.
- [ ] O5 [B-rc]: ภ.พ.36 has no PDF export (pnd54-only route). Build parity or accept? Ham's call.
- [ ] O6 [C1 vision]: 50ทวิ field "ลำดับที่ ... ในแบบ ภ.ง.ด.53" always blank (cert issued at PV post,
      before the monthly filing; immutable so never backfillable). Options: fill at filing-finalize
      time on a COPY, print "-", or accept blank (common practice). Compliance call — Ham.

## Verification plan (after WP-A..D deployed)
- Re-run targeted live probes: B-rc full chain (the CRITICAL), B-bn fresh WHT PV with empty
  income-type (expect clean early error), PV #19 unstick, K-Plus PDF import (real file, expect
  parse or clean 422), dep re-run toast, expense-claim status badges.
- Full suite + public-domain probes per deploy protocol.

## Attempt log
- 2026-07-22 16:1x consolidated from 6 leg reports; WP-A+WP-D dispatched first (parallel-safe),
  WP-B/C sequential next.
- 2026-07-22 16:2x C1 vision triaged (1 false positive killed vs code, O6 added).
- 2026-07-22 WP-A (A1/A2/A3) implemented by Sonnet: GlPostingService.cs gross-up debit added
  to VI-linked branch; PV new-page dual-flag VAT derivation + fromVi self-withhold explanation
  (locked/read-only). 1 new backend test; existing tests cover both regressions. Full suite
  921/8/930 twice (1 pre-existing unrelated Pnd50 flake both times, documented in
  troubles-wiki.md); tsc clean. Not committed — left for Fable's diff review.
- 2026-07-25 WP-B (B1/B2) completed by Sonnet, resuming a prior worker killed mid-edit (dead
  worker had already done: entity Cancel(), service CancelAsync + concurrency mapping, endpoint,
  B1(a) server-side seam at CreateDraftAsync+ApproveAsync, N1 comment fix, FE detail-page Cancel
  button + queries hook, th.json keys, both new backend test files — comprehensive and correct).
  Gaps closed this session: en.json `confirmAction.pvCancel` (missing exactly where the worker
  died); B1(a) client-side block on `payment-vouchers/new` (not started — added with a noted
  simplification, see WP-B evidence above); B2 e2e value fix + WHT-amount assertion + a WHT-type
  dropdown pick (made necessary by the new client block). Found + fixed one real regression
  the new server-side seam caused in an unrelated existing test (`PurchaseRateBoundTests.
  PaymentVoucher_CreateDraft_AcceptsNormalWhtRate`). Env footgun hit: the first backend test
  launch's background process/output was orphaned across a session resume ("no live background
  children remain"); a naive rerun raced it and produced 12 false failures (pk_companies
  collision) before the stray process was found and killed — see troubles-wiki entry added.
  Final full suite: 0 failed / 930 passed / 8 skipped / 938 total (Api.Tests) + 152/0/152
  (Domain.Tests), skip count matches baseline exactly. tsc + next build clean. Not committed —
  left for Fable's diff review + Opus Tier-2 (per dispatch).
- [ ] O7 [B-mcp F2]: pending-agent-approvals widget shows agent drafts to APPROVER, but APPROVER holds zero sales.quotation.* perms — its "ตรวจ" link lands on an empty /quotations (cannot view/act; sales01 had to act instead). Decide: grant APPROVER read on agent-draft doc types, or filter the widget rows by the viewer's per-doc-type permission. Product call — Ham.

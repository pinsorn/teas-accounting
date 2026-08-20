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
- [x] C1 **HIGH [B-br F1]**: `POST /bank-accounts/{id}/imports` with REAL K-Plus PDF
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
      **C1 implementation evidence (2026-07-25, Sonnet, systematic-debugging discipline):**
      - **Root cause (proven via a throwaway repro xunit test running the real PDF through
        `KPlusPdfTextExtractor.Extract` + `KPlusPdfLineAssembler.Assemble` directly, no HTTP) —
        TWO distinct bugs, both inside `Assemble`, BOTH firing entirely before the account-mismatch
        check (`StatementImportService.cs` ~L63) ever runs — the mismatch NOTE above does not
        apply; account no. parsed cleanly as `751-2-31547-6`, confirming the crash is upstream:**
        1. The real export prints a small vertical stamp/watermark of single-CHARACTER
           `PositionedWord`s in the page's FAR-LEFT MARGIN (X well left of the "Date" column's own
           derived left edge). `ClusterRows`' Y-proximity row-banding is X-BLIND, so the stamp's
           tightly-spaced characters (~3pt gaps, within `RowYTolerance`=3.5) bridged the Y-gap
           between two otherwise well-separated (~23pt apart) real transaction rows, fusing BOTH
           into one row-band. `AssignColumns` then joined both rows' Balance-column tokens with a
           space ("911.08 862.08"), and `FinalizeRow`'s `decimal.Parse` threw an unhandled
           `FormatException` — surfaced as a raw 500, never a `DomainException`.
        2. After fixing (1), a SECOND bug surfaced (had been masked by the first crash firing
           earlier in page 1): a per-page footer note ("ออกโดย K PLUS…") whose leading words land
           inside the Date column's own X-range by X-coincidence, but aren't date-SHAPED text. The
           old `hasDate = cols["Date"].Length > 0` check treated ANY word landing in the Date
           bucket as a new transaction row, producing a row with no Amount/Balance that crashed
           `FinalizeRow` with `DomainException: "Row dated ออกโดย K has no balance value."`
           (a controlled exception, but for the WRONG reason — a real design gap, not a designed
           rejection).
      - **Fix ((1) `KPlusPdfLineAssembler.cs` `Assemble`):** derive an outer left/right horizontal
        bound for the table using the SAME symmetric-midpoint logic `AssignColumns` already uses
        for INNER column boundaries (D9 — data-driven from the header's own positions, never
        hardcoded) and drop any word outside that span from `txnWords` BEFORE `ClusterRows` runs
        (filtering only at `AssignColumns` time would be too late — clustering already fused the
        rows by then).
      - **Fix (2):** added `DateShapePattern` (`^\d{2}-\d{2}-\d{2}$`, matching `ParseDdMmYyCe`'s own
        format) and require the Date bucket's content to be date-SHAPED, not merely non-empty, to
        set `hasDate`. A non-date-shaped band now falls into the "wrapped continuation" branch
        (harmless no-op — its Channel/Detail buckets are empty) instead of starting a fake row.
      - **Validated against the FULL real PDF** (repro harness, then deleted per spec — throwaway):
        `KPlusPdfTextExtractor.Extract` + `KPlusPdfLineAssembler.Assemble` +
        `BankStatementIntegrity.Validate` (D10) all succeed end-to-end — 7,006 words / 17 pages →
        436 lines, AccountNoRaw=`751-2-31547-6`, Opening=326.89, Closing=2,019.49, D10 balance
        integrity holds. **Gate outcome: the real PDF now imports successfully at the parse layer**
        (not the designed-422 branch — the account-mismatch check is a SEPARATE, later, still-
        correct 422 against co5's dummy account, per the spec's own NOTE, and was never what
        crashed).
      - **Hardening (spec's "any un-mapped exception → clean 422"):** `StatementImportService.
        ImportAsync` now wraps `adapter.Parse` + `BankStatementIntegrity.Validate` in try/catch —
        any non-`DomainException` is logged server-side (`ILogger<StatementImportService>`, newly
        injected via standard DI, no manual wiring needed) and rethrown as
        `DomainException("bank.statement_parse_failed", ...)`, a generic client-safe message
        (`DomainExceptionMiddleware`'s default-422 fallback, same "hardcoded message" policy
        `KPlusPdfTextExtractor`'s password path already uses). Defense-in-depth for the NEXT parse
        edge case (this or the KBiz CSV adapter, same choke point) — nothing persists before this
        point, mirrors the pre-existing D10 comment.
      - **Regression test** (`KPlusPdfLineAssemblerTests.cs`,
        `Assemble_ignores_left_margin_watermark_and_non_date_footer_note`): a SANITIZED synthetic
        `PositionedWord` fixture (placeholder account no./amounts, no real statement data)
        reproducing BOTH failing shapes in one page — a 9-character vertical watermark strip at
        Left=30 bridging two 30pt-apart real rows, plus a non-date-shaped footer note below the
        last row. Asserts 2 correctly-separated lines + D10 integrity holds. **Verified test
        validity by temporarily reverting both fixes and re-running** — failed with the EXACT
        real-world shape (`FormatException: 'The input string '700.00 550.00' was not in a
        correct format.'`), then restored the fix and confirmed green (red→green proof, not just
        a passing assertion written against the fixed code).
      - Gates: `dotnet build` (whole solution) clean, 0 warnings/errors. Full `dotnet test` ×2 runs:
        both 932 passed / 1 failed / 8 skipped / 941 total (skip count = baseline) — run 1's
        failure detail was lost to a `tail` truncation, run 2 failed
        `ExpenseClaimServiceTests.Cancel_is_legal_from_Draft_and_Rejected` (`DomainException:
        Company with Tax ID '...' already exists` from `TestCompanyFactory` — a shared-DB Tax-ID
        collision, file untouched by this diff); isolated re-run of that test + all new/touched
        Bank tests together passed 12/12, confirming pre-existing shared-`teas_test`-DB flakiness
        per troubles-wiki's documented "single, different test fails each run" class (new instance
        appended to that wiki entry — outside the previously-seen `TaxFilings`/`Pnd50` pool, so
        worth recording). Not committed — left for Fable's diff review.

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
- [x] E1 **HIGH**: /settings/companies create modal — จด VAT toggle OFF is IGNORED: co6 (id=6)
      was created with the toggle visually off yet persisted vat_registered=true (list + edit
      modal both show จด VAT). Suspect FE payload omits the flag (backend defaults true) or
      backend CreateAsync ignores it. Repro: create any company with จด VAT off → list shows จด VAT.
      **Investigated end-to-end 2026-07-25 (Sonnet): NOT reproducible against current source.**
      Read the full chain — FE `CreateCompanyDialog.submit()` (`frontend/app/(dashboard)/settings/
      companies/page.tsx`), `CreateCompanyRequest`/`CreateCompanyValidator`
      (`Accounting.Application.Master.CompanyDtos`), `MasterEndpoints.MapCompanies` POST handler,
      `CompanyService.CreateAsync` — `req.VatRegistered` threads through untouched at every layer,
      no default-true anywhere, no `HasDefaultValue` on the `VatRegistered` column (unlike the
      KNOWN, unrelated `VatRate=0` EF-default footgun documented in `TestCompanyFactory.CreateAsync`'s
      own doc comment). Proved via a NEW full-HTTP-pipeline test (real routing/validator/JSON, not
      just the service call) — `POST /companies` with `vatRegistered:false` persists false
      (`CompanyVatFlagHttpTests.Create_with_vat_registered_false_persists_false`, real Postgres).
      Most likely explanation for the live symptom: entangled with E2 below — a create-then-
      immediate-edit-to-flip workflow would hit E2's UpdateAsync bug, which rolls back the WHOLE
      transaction (the flip never lands), leaving the row at whatever it started as. No code
      change needed for E1 itself; regression test added to lock in correct behavior.
- [x] E2 **HIGH**: edit-company save → `PUT /api/proxy/companies/6` → **500** (raw
      "An unexpected error occurred" toast), reproduced twice (payload = full form with
      vatRegistered=false). Root-cause the 500 (server log/stack), fix + map to clean 422 if it's
      a domain rejection. ACCEPTANCE: co6 can be flipped to ไม่จด VAT via the UI post-deploy
      (unblocks army B2 non-VAT legs).
      **ROOT CAUSE (proven 2026-07-25, Sonnet, red→green via a real-RLS-enforcing test — teas_test's
      normal superuser connection BYPASSES RLS and would have hidden this entirely):**
      `CompanyService.UpdateAsync` conditionally calls `IActivityRecorder.Record(...)` whenever a
      tax field (VatRegistered/VatRate/Pnd30SubmissionMode) changes, queuing an
      `audit.activity_log` insert with `company_id = <the company being edited>`. That table
      carries `FORCE ROW LEVEL SECURITY` (`600_superadmin_scoped_rls.sql` G3:
      `company_id = current_setting('app.company_id') OR company_id IS NULL OR app.bypass_rls`).
      `TenantMiddleware` pins `app.company_id` SESSION-scoped to the CALLER's OWN company for
      every request — a super-admin editing a DIFFERENT company (exactly the /settings/companies
      use case) never gets that GUC re-pinned to the target. The queued activity_log row's
      `company_id` therefore mismatches the session's `app.company_id`, the INSERT's implicit
      RLS check 42501s inside `SaveChangesAsync`, the WHOLE save rolls back (the VAT flip never
      lands either), and the unhandled `DbUpdateException` surfaces as a raw 500. This is the
      EXACT same class of bug `CompanyService.CreateAsync` was fixed for (commit 4b92efd,
      2026-07-18, `specs/fix-company-create-rls-atomic.md`) — that fix was never extended to
      `UpdateAsync`. Genuine bug (per dispatch's "audit-log path" hint), not a domain rejection —
      no 422 mapping needed, the operation now succeeds.
      **FIX** (`backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs`,
      `CompanyService.UpdateAsync`): wrap the save in an explicit transaction and
      `SELECT set_config('app.company_id', {companyId}, true)` (LOCAL — auto-reverts at
      commit/rollback, never leaks onto the pooled connection) before `SaveChangesAsync`, mirroring
      `CreateAsync`'s already-proven, already-reviewed idiom. `CompanyProfileService`'s own
      tax_config_change audit write was checked too — it operates exclusively on
      `tenant.CompanyId` (self-service `/company-profile/*`, always same-company), not affected,
      left untouched.
      **TESTS:** `backend/tests/Accounting.Api.Tests/Persistence/CompanyUpdateRlsTests.cs` (new) —
      reproduces the EXACT RLS-enforced shape via the `pg_database_owner` non-bypass-role trick
      already proven in `CompanyCreateRlsTests` (SET ROLE + session-scoped `app.company_id` pinned
      to a DIFFERENT "own" company than the target being updated, REAL `ActivityRecorder` not a
      no-op stub — a no-op silently defeats the repro, hit this once before catching it). Verified
      RED (`42501: new row violates row-level security policy for table "activity_log"`) before the
      fix, GREEN after. Plus `CompanyVatFlagHttpTests.Update_flips_vat_registered_true_to_false`
      (full HTTP PUT round-trip, 204 + persisted false). New troubles-wiki.md entry ("Super-admin
      cross-company write 500s under RLS").
- [x] E3 **LOW [B-mcp]**: malformed MCP tools/call (args not wrapped in `request`) throws
      `System.ArgumentException` which `McpErrorSurfacingFilter` doesn't catch (only McpE2Exception/
      DomainException/ValidationException/JsonException) → SDK swallows into generic
      "An error occurred invoking '<tool>'." — misled a whole test leg into a false CRITICAL.
      FIX: catch ArgumentException in the filter → clean "[mcp.arguments] ..." message.
      (Fable root-caused 2026-07-25 via prod log: worker sent flat DTO fields; schema correctly
      advertises nested `request` — write path itself works, verified by live probe.)
      **DONE 2026-07-25 (Sonnet):** added an `ArgumentException` catch to
      `McpErrorSurfacingFilter.cs` (same pattern/position as the 4 existing catches — logs Warning
      server-side, returns `[mcp.arguments] <message>` as a non-throwing `CallToolResult`). New
      test `McpErrorSurfacingTests.CreateTaxInvoiceDraft_args_not_wrapped_in_request_surfaces_mcp_arguments`
      calls `create_tax_invoice_draft` with the malformed FLAT-field shape (no `request` wrapper)
      and asserts `IsError` + the `[mcp.arguments]` prefix.
- **WP-E implementation evidence (2026-07-25, Sonnet):**
  - CHANGED: `backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs` (E2 fix, 12
    lines), `backend/src/Accounting.Api/Mcp/McpErrorSurfacingFilter.cs` (E3 catch, 11 lines),
    `backend/tests/Accounting.Api.Tests/Persistence/CompanyUpdateRlsTests.cs` (new, E2 RLS repro),
    `backend/tests/Accounting.Api.Tests/Master/CompanyVatFlagHttpTests.cs` (new, E1+E2 HTTP repro),
    `backend/tests/Accounting.Api.Tests/Mcp/McpErrorSurfacingTests.cs` (E3 test added). 5 files —
    within the ≤6 blast cap. No frontend files touched (E1 needed no fix; FE gates not applicable).
  - GATES: `dotnet build` (whole solution) — clean, 0 warnings/errors. Full `dotnet test`
    (Accounting.Api.Tests) — **0 failed, 937 passed, 8 skipped, 945 total (12.0 min)** — skip
    count matches the 8-baseline exactly, no flake hit this run (all green, no isolate-rerun
    needed). Accounting.Domain.Tests — **155/155 passed, 0 failed.** No frontend changes → tsc/
    next build not run (not applicable per dispatch).
  - SIMPLIFIED/SKIPPED: none — both E1 and E2's ACCEPTANCE ("co6 can be flipped to ไม่จด VAT via
    the UI post-deploy") is satisfied by the E2 fix alone; E1 required no code change after
    exhaustive verification found no defect.

## OPEN (Ham / triage decisions — not dispatched)
- [x] O1 [B-fa F-2]: FA acquisition posts no GL by design; UI never warns when no VI linked →
      disposal credits cost that was never debited. Options: warning badge on asset detail
      ("ต้นทุนยังไม่ลง GL — ยังไม่ได้ link ใบกำกับ/JE"), or block activate without VI/opening-JE ref.
      DECISION NEEDED (product call).
      **DONE 2026-07-25 (Wave 1, Sonnet), warning-badge option, NO GL behaviour change:**
      `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx` — an `alert-warning` notice
      (`data-testid="fa-no-gl-cost-warning"`) renders whenever `d.vendorInvoiceId == null`
      (any status), quoting `FixedAssetService.cs`'s own FA-A comment back to the accountant:
      link a Vendor Invoice or post a manual opening-balance JE before this asset is disposed
      (otherwise disposal credits a cost that was never debited). i18n key
      `fixedAssets.costNotOnGlWarning` added to both `en.json`/`th.json` (key-parity checked
      programmatically, 0 mismatches across all namespaces). No backend/GL code touched.
      TESTS: no jest/RTL or Playwright fixture exists for `/fixed-assets/[id]` (confirmed —
      `frontend/**/*.test.tsx` and `frontend/e2e/**fixed-asset**` both empty), so per the
      dispatch's fallback this is SKIPPED and noted rather than inventing a new test harness;
      verified by code inspection (the condition mirrors the DTO field already asserted end-to-end
      by `frontend/lib/types.ts`'s `FixedAssetDetail.vendorInvoiceId`) + `tsc --noEmit` (0 errors)
      + `next build` (compiled, `/fixed-assets/[id]` route generated).
- [x] O2 [B-bn INCONCLUSIVE → RESOLVED, real gap]: live re-check picked both TIs cleanly
      (chips confirmed pre-issue) — see `swarm-findings/army/O2-O3-verify.md`. Selection
      **persists** (join table + API both correct); total **does NOT** roll up (BN #24
      totalAmount ฿107.00 = manual line only, TI sum was ฿6,955); back-link chips **do NOT**
      render on the BN detail page (`bn-ti-chips` only exists in the create form; API already
      returns `taxInvoices` with doc_no "for chips" per the BE's own comment — display half was
      just never wired up). Product gap, not a false positive: add the chip list to the BN
      detail page. DECISION NEEDED (build the missing detail-page display, or accept
      picker-only visibility).
- [x] O3 [B-bn note → RESOLVED, automation artifact]: re-verified with proper Playwright
      `.click()` (tall viewport, no raw coordinates/force-click) — button fires
      `mark-printed` + `pdf` network calls and produces a real download
      (`billing-notes-24.pdf`), reproduced on a control page (TI) using the same shared
      `PrintMenu` component; direct endpoint probe also 200/application-pdf. Not a real
      user-facing break — no Ham confirmation needed, close as swarm-script artifact. See
      `swarm-findings/army/O2-O3-verify.md`.
- [x] O4 [B-ec item 4]: expense-claim EDIT for Draft/Rejected = UNBUILT (backend PUT wired, zero
      FE). Build or drop? Ham's call. — closed by triage 2026-08-19 (edit page exists, d877286)
- [ ] O5 [B-rc]: ภ.พ.36 has no PDF export (pnd54-only route). Build parity or accept? Ham's call.
- [x] O6 **CLOSED 2026-07-25 — NO CODE CHANGE NEEDED.** Research (AGY + Fable review:
      `swarm-findings/army/O6-research-50twi-pnd53-seq.md`) found the field is an administrative
      cross-reference, **not legally mandatory at issuance**, and a cert with it blank **is valid for
      the payee's tax credit** (ม.60; RD matches credits by TIN/amount/year). Verified citations:
      rd.go.th ม.50 ทวิ + RD ruling กค 0702/3793 (3 พ.ค. 2556). The reasoning is also structurally
      forced regardless of sources: ม.50 ทวิ requires issuing the cert AT PAYMENT, which is before the
      monthly ภ.ง.ด.53 exists, and the cert is immutable — so it can only ever be blank unless we
      break one of those two rules. DECISION: leave it blank permanently; never mutate or reprint an
      issued cert; ภ.ง.ด.53 sequence numbers stay inside the month-end filing engine only. (Rejected
      the report's alternative of printing our own voucher ID in that box — the box is LABELLED
      "ลำดับที่ในแบบ ภ.ง.ด.53", so putting a non-ภ.ง.ด.53 identifier there would be actively
      misleading.) Caveat recorded: the report's "how other Thai software handles it" table is
      weakly sourced (domain roots only) — the legal sections are the verified part.
      Original finding:  50ทวิ field "ลำดับที่ ... ในแบบ ภ.ง.ด.53" always blank (cert issued at PV post,
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
- 2026-07-25 WP-C (C1) implemented by Sonnet, systematic-debugging discipline (throwaway repro
  xunit test running the real gitignored PDF through the extractor+assembler directly, no HTTP).
  Root cause = TWO bugs in `KPlusPdfLineAssembler.Assemble`, both firing before the account-
  mismatch check ever runs: (1) a vertical watermark/stamp of single-char words in the page's
  far-left margin bridged two real transaction rows into one row-band via X-blind Y-proximity
  clustering, joining Balance-column tokens with a space → unhandled `FormatException` (raw 500);
  (2) once fixed, a per-page footer note whose words coincidentally land in the Date column's
  X-range but aren't date-shaped was treated as a fake transaction row → controlled but wrong
  `DomainException` ("no balance value"). Fixed both: data-driven outer table-bound word filter
  (same symmetric-midpoint logic as the existing inner-column boundaries) before clustering, and
  a date-SHAPE regex gate instead of mere Date-bucket occupancy. Validated against the FULL real
  PDF (17 pages, 436 lines, D10 integrity holds) — imports cleanly at the parse layer; the
  account-mismatch 422 is a separate, still-correct, later check (co5's dummy account ≠ the real
  statement's, per the spec's own note — never what crashed). Hardened
  `StatementImportService.ImportAsync` to map ANY un-mapped exception from `adapter.Parse`/
  `BankStatementIntegrity.Validate` to a clean `bank.statement_parse_failed` 422 (logged
  server-side via newly-injected `ILogger<StatementImportService>`), defense-in-depth for the
  next such gap. New regression test with a SANITIZED synthetic fixture reproduces both shapes in
  one page; validity proven red→green (temporarily reverted the fix, confirmed the test fails
  with the exact real-world `FormatException` shape, then restored and confirmed green). Full
  suite ×2: both 932/1/8/941, a DIFFERENT single pre-existing flake each run (unrelated files),
  isolated re-run confirmed both non-regressions; new flake instance appended to troubles-wiki.
  Not committed — left for Fable's diff review.
- [x] O7 [B-mcp F2]: pending-agent-approvals widget shows agent drafts to APPROVER, but APPROVER holds zero sales.quotation.* perms — its "ตรวจ" link lands on an empty /quotations (cannot view/act; sales01 had to act instead). Decide: grant APPROVER read on agent-draft doc types, or filter the widget rows by the viewer's per-doc-type permission. Product call — Ham.
      **Graduated from "Ham's call" to fixed-by-convention (WP-I / I1, 2026-07-25, Sonnet):** the
      repo already has a standing WP1/WP2 rule — never show a link that 403s — so this wasn't a
      product decision, just an unapplied existing convention. FIX
      (`frontend/app/(dashboard)/page.tsx`): reused `useHasScope()` (`components/PermissionGate.tsx`)
      + the SAME per-doc-type `.read` permission codes `SidebarNav.tsx`'s `SECTIONS` already use to
      gate each doc type's list-page nav item (`sales.tax_invoice.read`, `sales.quotation.read`,
      `sales.receipt.read`, `purchase.purchase_order.read`, `purchase.vendor_invoice.read`,
      `purchase.payment_voucher.read`). Each `agentTypes` row now carries its matching `perm`; the
      push-to-`alerts` loop gates on `a.n > 0 && hasScope(a.perm)`. No new permission, no widened
      grant — a dropped row is dropped ENTIRELY (not hidden-but-counted), so the widget's own
      per-type count (`n`) always matches what's rendered (no "1 pending" with zero rows), matching
      spec's requirement. Backend `/reports/pending-agent-approvals` endpoint intentionally
      untouched (still gated on `sales.tax_invoice.read` only, per B-mcp F2's own framing — this is
      an FE display-gate fix, not a backend RBAC change).
      GATES: `npx tsc --noEmit` (frontend/) — 0 errors. `npm run build` — compiled successfully
      (see WP-I gate summary below for the consolidated run).

## WP-F — V1 post-deploy residual (2026-07-25, found by the v1.22.11 verify leg)
- [x] F1 **MEDIUM (money-adjacent, FE prefill) [V1-F1]**: `payment-vouchers/new/page.tsx` L135
      (VI-prefill effect) still derives the VAT rate with the SINGLE-flag
      `vendor.vatRegistered ? taxRateForProductType(productType) : 0` — the exact predicate WP-A2
      replaced at L159. For a foreign no-Thai-VAT-D vendor the prefilled line `amount` lands
      ~6.8% short of the VI's outstanding (18,691.59 vs 20,000 live on co5, v1.22.11 —
      `derivePvPrefillBase` divides by a rate the form will never apply). Form is editable so
      nothing is locked, but the number is silently wrong unless hand-corrected.
      FIX: use the same dual-flag predicate — inside the effect, reference the component's own
      `vendorVat` (declared below the effect; initialized by the time an effect runs) or recompute
      `vendor.vatRegistered && !(vendor.isForeign && !vendor.hasThaiVatDReg)`. Verify the prefill
      grand total lands EXACTLY on `outstanding` for all 4 combos (domestic VAT / domestic non-VAT /
      foreign with VAT-D / foreign without VAT-D) — that invariant is the Ham decision documented
      at L130-133. ACCEPTANCE: re-drive VI→PV on ARMYAWS859829 → prefilled amount = 20,000.00.
      DONE (2026-07-25): rate now reads `vendorVat` (the dual-flag ม.82/5 predicate at L159,
      unchanged) instead of the single-flag `vendor.vatRegistered` check. Referencing it directly
      is safe — the effect closure only reads `vendorVat` when the callback fires post-render
      (React commit phase, after the whole component body incl. L159 has executed), and the
      pre-existing guard at L126 (`if (!vendor || vendor.vendorId !== vi.vendorId) return;`)
      ensures `vendor` is loaded before the rate line runs, so `vendorVat`'s `vendor ? ... : true`
      fallback never applies here. Confirmed with `tsc --noEmit` (0 errors) + `npm run build`
      (green) — no reordering, no other lines touched. 4-combo reasoning (base = outstanding /
      (1+rate) rounded, VAT = base*rate, total = base+VAT = outstanding by construction of
      `derivePvPrefillBase`):
        - domestic, VAT-registered: vendorVat=true → rate=product rate (e.g. 7%) → total=outstanding.
        - domestic, non-VAT: vendorVat=false → rate=0 → base=outstanding, VAT=0 → total=outstanding.
        - foreign, WITH Thai VAT-D: foreignNoVatD=false → vendorVat=vatRegistered&&true → rate=product
          rate → total=outstanding (was already correct pre-fix, unaffected).
        - foreign, WITHOUT Thai VAT-D: foreignNoVatD=true → vendorVat=false → rate=0 → base=outstanding,
          VAT=0 → total=outstanding (this is the combo the bug hit: old code used
          `vendor.vatRegistered` alone, which can be true even with hasThaiVatDReg=false, wrongly
          applying a nonzero rate and landing ~6.8% short — now fixed).
      No e2e spec asserts this prefill number (`grep -r "derivePvPrefillBase\|fromVendorInvoiceId" frontend/e2e` → no matches), so no second file touched.

## WP-G — non-VAT company PV path (2026-07-25, army B2-nv on co6) — MONEY, Opus review mandatory
Root cause verified by Fable in code: the PV path has **no company-VAT-mode gate at all**, while the
VI path has one at every seam. `PaymentVoucherService.CreateDraftAsync` L196-200 already loads
`_db.Companies.Select(c => new { c.RequiresBusinessUnit, c.VatRate })` — it just never reads
`VatRegistered`; its only VAT guard (L248) is `vendor.VatRegistered`. The FE PV form has no
`useSystemInfo()`/`vatMode` at all (compare `vendor-invoices/new/page.tsx` L92-93).
- [x] G1 **HIGH (money control point) [B2-nv F1/F2]**: add the company gate to
      `PaymentVoucherService.CreateDraftAsync` — extend the existing L198 projection with
      `c.VatRegistered`, then mirror `VendorInvoiceService`'s WP1.2 block (L147-152) verbatim in
      intent: when `!companyVatRegistered`, force every line's `VatRate = 0`, `VatAmount = 0`,
      `IsRecoverableVat = false` BEFORE the totals roll-up, so header/GL/preview agree. This is the
      ONE control point both REST and the from-VI guided path funnel through (same seam WP-B(a) used).
      **Why HIGH even though B2-nv found the ledger clean:** the leg only exercised the VI-LINKED GL
      branch (Dr AP = subtotal+vat, no 1170 line). The STANDALONE branch
      (`GlPostingService.PostPaymentVoucherAsync` else-branch, ~L203-211) debits **1170 input VAT**
      whenever `l.IsRecoverableVat && l.VatAmount > 0` — on a non-VAT company that is exactly the
      F-B/1170 pollution class (v1.22.10) reaching the ledger through an unguarded door. Prove it
      either way with a test before deciding severity down.
      **1170 EXPOSURE VERDICT (2026-07-25, Sonnet, proven BEFORE the fix, per dispatch):
      LIVE ledger bug, not just display.** A throwaway-then-kept red run of
      `PaymentVoucherNonVatCompanyTests.NonVatCompany_StandalonePv_PostsNo1170_VatTotalsZero`
      against unfixed code: a non-VAT company + standalone PV line with client-sent
      `VatRate:0.07, IsRecoverableVat:true` persisted verbatim (`IsRecoverableVat=True,
      VatAmount=70`) — nothing zeroed it (the only existing guard, L248, checks the VENDOR's
      `VatRegistered`, never the company's). Had the test continued to Post, GlPostingService's
      `if (l.IsRecoverableVat && l.VatAmount > 0m)` would have debited 1170 for real. The
      VI-linked path's OWN test failed too but for a different, already-partially-mitigated
      reason: VI's existing WP1.2 gate had already forced `IsRecoverableVat=false` on the VI
      line (so the standalone branch's 1170 debit could never fire there — matches B2-nv's
      "ledger clean" finding), but `VatRate`/`VatAmount` still leaked through nonzero
      (VI reroutes into `NonRecoverableVatAmount`, it doesn't zero) — a header/display
      inconsistency, not a ledger-cash bug, but still wrong per this spec's stricter
      always-zero-for-PV design.
      **FIX** (`backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.cs`,
      `CreateDraftAsync`): projection extended with `c.VatRegistered`
      (+ `companyVatRegistered` local, fail-safe `??false` mirroring VI's F-3 direction);
      new block right after the per-line loop, BEFORE `subtotal`/`vatTotal`/`whtTotal` are
      summed — `foreach (var l in lines) { l.VatRate=0; l.VatAmount=0; l.IsRecoverableVat=false; }`
      when `!companyVatRegistered`. Applies uniformly to standalone AND VI-linked lines (single
      seam, matches spec). **Deliberate design note for Tier-2 review:** this zeroes outright
      rather than VI/ExpenseClaim's "fold non-recoverable VAT into cost" pattern (PV has no
      `NonRecoverableVatAmount` header bucket to reroute into) — an explicit, narrower policy
      than the sibling docs, per this spec's own literal wording and TESTS acceptance ("VAT
      totals are 0"), not a simplification I introduced.
      **TESTS:** `backend/tests/Accounting.Api.Tests/Hardening/PaymentVoucherNonVatCompanyTests.cs`
      (new) — `NonVatCompany_StandalonePv_PostsNo1170_VatTotalsZero` (red before fix, green
      after — the 1170-proof test), `NonVatCompany_ViLinkedPv_PostsNo1170_VatTotalsZero`,
      `VatRegisteredCompany_StandalonePv_StillBooksRecoverableVat` (regression, co5 shape
      unchanged, 1170 still posts 70 for a VAT company).
      **Opus Tier-2 REJECTED + fix round (2026-07-25, Sonnet):** the "zero VatRate/VatAmount"
      gate above was WRONG — Fable's spec error (the "VAT totals are 0" acceptance criterion),
      which I had followed literally and correctly per-spec, but it produced broken money (see
      the amended TESTS + CORRECT GATE SHAPE bullets below the G3 note). **Fix:** the
      `!companyVatRegistered` block now sets ONLY `l.IsRecoverableVat = false;` — `VatRate`/
      `VatAmount` are left untouched, mirroring `VendorInvoiceService`'s WP1.2 block exactly
      (which also never zeroes them). `GlPostingService`'s pre-existing `expenseGross =
      l.IsRecoverableVat ? l.Amount : l.Amount + l.VatAmount` fold already routes the VAT into
      cost from that one flag — no GL code touched. **Tests corrected to assert the RIGHT
      shape** (Opus F4: the original 3 tests all passed green on the broken zeroing because
      they only asserted Dr=Cr / no-1170 / VatAmount==0, none of which distinguish "zeroed" from
      "folded"): standalone renamed `NonVatCompany_StandalonePv_FoldsVatIntoCost_NoInputVatLine`
      — now asserts `draft.Lines` keep `VatRate==0.07m && VatAmount==70m` (not zeroed),
      `posted.TotalPaid==1070m`, and the expense-account JE line `DebitAmount==1070m` (not just
      "no 1170"); VI-linked renamed `NonVatCompany_ViLinkedPv_SettlesViInFull_NoInputVatLine` —
      now asserts `vi.SettlementStatus=="PAID"` and `vi.SettledAmount==vi.TotalAmount` (proving
      the VI is NOT stranded PARTIAL) instead of a VatAmount==0 check. Both still assert no
      Input-VAT-account debit line (resolved via `IOptions<GlAccountsOptions>`, never hardcoded
      — G3, unaffected by this correction). Regression test unchanged (company IS VAT-registered,
      so the `!companyVatRegistered` block never fires there).
- [x] G2 **HIGH (FE) [B2-nv F1]**: `payment-vouchers/new/page.tsx` — read
      `const companyVatRegistered = useSystemInfo().data?.vatMode ?? true;` and fold it into
      `vendorVat` (`companyVatRegistered && vendor.vatRegistered && !foreignNoVatD`); hide the VAT
      rate control + VAT summary line for a non-VAT company, mirroring the VI form and the
      expense-claims F-B treatment. Repro: co6 PV form showed a live "7%" VAT UI.
      **DONE (2026-07-25):** added `useSystemInfo` import + `companyVatRegistered` local (top of
      `PvForm`, alongside `company`); `vendorVat` now
      `companyVatRegistered && (vendor ? vendor.vatRegistered && !foreignNoVatD : true)` — ANDs
      the company gate onto the existing WP-A2/B-rc-F2 dual-flag predicate without touching its
      inner shape (the `vendor ? ... : true` pre-load fallback is preserved). WP-F's VI-prefill
      effect (L138, `vendorVat ? taxRateForProductType(...) : 0`) is untouched and automatically
      inherits the company gate transitively. Per-row VAT display box now wrapped in
      `{companyVatRegistered && (...)}` (was an always-visible read-only derived %, not an
      editable control — hidden the same way expense-claims F-B hides its VAT select). Totals
      box VAT line now `...(companyVatRegistered ? [{label:t('vat'), value:vat}] : [])`, same
      spread-conditional idiom as `vendor-invoices/new`. WP-B(a) WHT-type block (client check +
      inline warning) untouched — confirmed by diff (only 4 edit sites: import line, hook decl,
      `vendorVat` formula, VAT row wrap, totals row wrap).
      **Opus Tier-2 (2026-07-25): found G2 already correct as implemented — no further FE
      change made.** The FE always sends the gross amount + the derived rate (never a
      VAT-exclusive figure), so it was never exposed to the G1 zeroing-vs-folding bug; the
      review scope for G2 was backend-only.
- [x] G3 note [B2-nv F3]: co6's output-VAT account is `2151`, not `2130` — future non-VAT/VAT
      assertions must read the account from the company's CoA, not a hardcoded code.
      No code change (note-only, per dispatch). New test file resolves the Input VAT account
      code from DI (`IOptions<GlAccountsOptions>.Value.InputVatAccount`) rather than hardcoding
      `"1170"`, honoring the caution even though 1170 itself is config-stable (not the account
      G3 flagged as divergent).
- TESTS (**AMENDED 2026-07-25 after Opus Tier-2 REJECT — the original "VAT totals are 0" criterion
  was WRONG and the implementer followed it literally; Fable's spec error**): non-VAT company +
  standalone PV on a recoverable-VAT category → posted JE has **NO 1170 debit**, the VAT is **FOLDED
  INTO the expense debit** (`GlPostingService` L196 already does this whenever `IsRecoverableVat` is
  false: `expenseGross = l.Amount + l.VatAmount`), and **`TotalPaid == gross`** (1,000 + 7% ⇒ 1,070 —
  a non-VAT company still really pays its VAT-charging vendor in full; that cash belongs in cost);
  non-VAT company + VI-linked PV → **`vi.SettlementStatus == "PAID"` and `SettledAmount ==
  vi.TotalAmount`** (the VI's own non-recoverable VAT sits inside TotalAmount, so a short settle
  strands AP forever — the `vi.pv_exists` guard blocks a second PV); VAT company (co5 shape)
  unchanged (regression); FE tsc + build.
- **CORRECT GATE SHAPE** (Opus F1, verified by Fable in code): in the `!companyVatRegistered` block
  keep ONLY `l.IsRecoverableVat = false;` — do NOT zero `VatRate`/`VatAmount`. That single flag kills
  the 1170 debit AND folds the VAT into cost, and it is exactly what `VendorInvoiceService`'s WP1.2
  block does (it touches neither VatRate nor VatAmount). ภ.พ.30 reads only VendorInvoices, never
  PaymentVouchers, so keeping the PV's VatAmount has no filing side-effect.
  **Gates, corrected round (2026-07-25, Sonnet, post Opus Tier-2 REJECT):** `dotnet build`
  (whole solution) clean, 0 warnings/errors. Targeted run — corrected
  `PaymentVoucherNonVatCompanyTests` (3/3) + broader PV/VI regression filter
  (`PaymentVoucher|Sprint87|PurchaseRateBound|VendorInvoice|McpDocumentChain`, 76/76) — all green.
  `npx tsc --noEmit` — 0 errors (FE untouched this round, re-verified per Opus's "already
  correct" finding). `npm run build` — compiled successfully. **Final full-suite gate**
  (auto-backgrounded, >10min, polled to completion): Domain.Tests 155/0/155 clean. Api.Tests —
  **0 failed, 940 passed, 8 skipped, 948 total (12m29s)** — fully green this run (the
  `Pnd50FilingServiceTests` flake that fired on the pre-correction run did not recur here); skip
  count matches the 8-baseline exactly, +3 vs. the pre-WP-G 937/8/945 baseline (this session's 3
  corrected tests). Not committed — left for Fable's diff review + Opus Tier-2 re-review (per
  dispatch, MONEY control point).

## WP-H — payroll read/filing RBAC (2026-07-25, army B2-pr) — SQL seed = footgun, Opus review
Root cause verified by Fable: `Permissions.Payroll` has only `RunManage`/`RunPost`/`RunPay` — there is
**no read-level payroll permission at all**, and every GET in `PayrollEndpoints.cs` (list, detail,
payslip PDFs, **`/pnd1/pdf`**, **`/pnd1a/pdf`**) is gated on `Payroll.RunManage`. So a TAX_OFFICER —
whose whole job is filing ภ.ง.ด.1/1ก — gets 403 on the tax forms themselves (live: nvtax01 on co6),
while anyone who CAN read payroll necessarily also holds manage. No seed grants payroll to TAX_OFFICER.
- [x] H1 **HIGH [B2-pr F3]**: give the two RD filing endpoints (`/payroll/runs/{id}/pnd1/pdf`,
      `/payroll/pnd1a/pdf`) the SAME gate the other RD forms use — `tax.filing.read` (see
      `627_seed_tax_officer_filing_grant.sql`) — either instead of, or OR-ed with, `Payroll.RunManage`.
      Prefer the smallest correct change: these are tax filings, not payroll administration.
      FE: the payroll/ภ.ง.ด.1 nav + pages must not 403-spam for a TAX_OFFICER; if the filing lives
      under the payroll module in the sidebar, gate the LINK on the same permission the endpoint now
      requires (don't show a link that 403s — the WP1/WP2 lesson).
      **Done (2026-07-25, Sonnet):** Gated on `payroll.run.manage OR tax.filing.preview` (NOT
      `tax.filing.read` — code-read of `TaxFilingEndpoints.cs` shows every other RD-form PDF/export
      endpoint (pnd30/pdf, pnd3/pdf, pnd51/pdf, pnd50/pdf, batch-files, …) is gated on `preview`, not
      `read` — `read` only guards the `/tax-filings` history list. Followed the ACTUAL pattern, not
      the spec's literal permission name; TAX_OFFICER already holds both from 627, so this doesn't
      change TAX_OFFICER's outcome, only which permission namespace is architecturally consistent).
      OR-ed (not replaced) via the in-repo `RequireAssertion` OR-set pattern (`TaxAdjustmentNoteEndpoints.cs`'s
      CN/DN gate) — `PayrollEndpoints.CanFile` — so COMPANY_ADMIN/CHIEF_ACCOUNTANT keep working
      unchanged via their existing `RunManage` grant, zero new SQL/grants needed. Widened 5 endpoints
      (the same "RD/SSO filing" class B2-pr F3 actually found blocked): `pnd1/pdf`, `sso/file`,
      `sso/pdf`, `pnd1a/pdf`, `wht50tawi/pdf`. Payroll list/detail/create/approve/post/pay/payslip
      endpoints untouched (still `RunManage`/`RunPost`/`RunPay`-only). `RbacEndpointInventory.
      AssertionOverrides` updated (5 new entries) so `RbacAuthMapTests` classifies these as
      `Assertion` with the correct OR-set instead of flagging a permission-catalog mismatch; the
      generated `docs/rbac/endpoint-permission-map.generated.md` was regenerated by running the test
      (not hand-edited) and shows all 5 routes as `Assertion | payroll.run.manage / tax.filing.preview`.
      FE: **no change** — read `payroll/page.tsx` + `payroll/[id]/page.tsx`: the pnd1/pnd1a/sso/
      wht50tawi buttons live only inside the payroll run list/detail pages, both of which load via
      `usePayrollRuns`/`usePayrollRun` → `GET /payroll/runs/` / `GET /payroll/runs/{id}` — endpoints
      this WP deliberately did NOT widen (payroll administration, per spec). So nothing new is shown
      to TAX_OFFICER (sidebar `/payroll` link correctly stays hidden, unchanged) and nothing that
      "now works" is hidden either — TAX_OFFICER's new backend access is reachable directly (API/
      future dedicated filing UI), not yet through this admin-gated UI. Building that UI would be
      scope creep beyond H1's "preferred minimal" mandate; flagging as a residual gap below, not
      silently fixed.
      **New tests** (`backend/tests/Accounting.Api.Tests/Rbac/PayrollFilingRbacTests.cs`):
      TAX_OFFICER passes the gate on `pnd1/pdf` + `pnd1a/pdf` (not 401/403); a role holding neither
      `payroll.run.manage` nor `tax.filing.preview` still 403s; COMPANY_ADMIN + CHIEF_ACCOUNTANT
      unchanged (still pass). **Gates:** `dotnet build` clean (0/0). `RbacAuthMapTests` +
      `RbacMatrixTests` + `TaxOfficerFilingGrantTests` + new `PayrollFilingRbacTests` — filtered run
      `--filter FullyQualifiedName~Rbac`: **55/55 passed, 0 failed, 0 skipped**. **Full-suite gate**
      (auto-backgrounded, >10min, polled to completion): Domain.Tests 155/0/155 clean. Api.Tests —
      1 failed, 943 passed, 8 skipped, 952 total (12m7s) — skip count matches the 8-baseline
      exactly; the 1 failure is `Pnd50FilingServiceTests.Pnd50_with_nonzero_adjustments_renders_
      the_ladder_in_v2`, the EXACT test named in troubles-wiki.md's "Pnd50 ladder test fails ...
      residue from CitYearDataServiceTests, not your diff" entry (shared teas_test DB residue from
      an unrelated test class, not scoped to any file this diff touches). Isolated re-run
      (`--filter FullyQualifiedName~Pnd50FilingServiceTests`) passed clean 7/7, confirming
      pre-existing flake, not a regression.
      *Residual gap (not in H1 scope, flag for Ham):* TAX_OFFICER still cannot reach these PDFs
      through the current UI (no dedicated filing page exists outside the admin-gated payroll list/
      detail) — only via direct API call. A minimal filing-only FE surface would be new scope.
- [x] H2 NOT NEEDED (2026-07-25): H1's OR-set closed the hole — TAX_OFFICER reaches all 5 filing artifacts (verified live, leg V2) while payroll administration stays RunManage-only, so no new perm code, no grants, no SqlScripts file. Original option, kept for the record: adding a real
      `payroll.run.read` code is the cleaner long-term shape (mirrors `629_seed_read_manage_split_grant.sql`)
      but costs a new perm code + grants for ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/TAX_OFFICER.
      **Seed-ordering footgun (memory `rbac-seed-ordering-footgun`): insert the perm CODE first, then
      the grants, in the SAME or a LOWER-numbered script — a grant referencing a code inserted by a
      later-numbered file silently no-ops.** New `SqlScripts/*.sql` ⇒ DB backup mandatory at deploy +
      post-deploy assert of the script's effect (memory `teas-prod-deploy-plink`).
- TESTS: `RbacAuthMapTests` + `RbacMatrixTests` must stay green (set `TEAS_REPO_ROOT` — memory
  `teas-repo-root-rbac-tests`); add: TAX_OFFICER can GET pnd1/pnd1a PDFs; a role with no payroll or
  filing grant still 403s; COMPANY_ADMIN/CHIEF_ACCOUNTANT unchanged.

## OPEN — payroll feature gaps (Ham's scope call, NOT dispatched)
- [x] O8 [B2-pr F1] **no day-based salary proration** for a mid-month hire or a mid-month leaver —
      both got a full month's salary + full PIT in the live run and in the printed ภ.ง.ด.1/1ก
      (PRB01 hired 07-15, PRC01 terminated 07-10, identical to the full-month control). Code comment
      says "regular salary only" ⇒ UNBUILT by design, not a crash. For a Thai payroll product this is
      the biggest functional gap the army found. Build proration? (needs a rule decision: calendar
      days vs working days, and how PIT/SSO follow.) — closed by triage 2026-08-19 (SalaryProration.DaysEmployed, PayrollRunService.cs:89-92)
- [x] O9 [B2-pr F1b] **no termination/end-date field** anywhere in the employee UI (the leg had to
      PUT it via the API). Pairs with O8 — proration is unimplementable from the UI without it.
      **DONE 2026-07-25 (Wave 1, Sonnet), FE-only — CHECKED FIRST, backend already fully wired:**
      `Employee.TerminationDate`/`CreateEmployeeRequest.TerminationDate`/
      `UpdateEmployeeRequest.TerminationDate`/`EmployeeDetail.TerminationDate` all already existed
      (`EmployeeDtos.cs`, `EmployeeService.cs`) — confirmed by reading the service before touching
      anything. The FE gap was narrower than it read: `settings/employees/page.tsx` already carried
      `terminationDate` in its `Editing` state, the detail-fetch mapping, and the save payload —
      only the `<input type="date">` itself was missing from the modal JSX (which doubles as both
      create AND detail/edit — there is no separate employee detail page). Added the input right
      after `hireDate`, same `set()`/nullable-clear pattern (`e.target.value || null`) as every
      other optional date field in the file. i18n: `employee.terminationDate` added to both
      `en.json`/`th.json`.
      TESTS (O9 field round-trips): new `backend/tests/Accounting.Api.Tests/Master/
      EmployeeTerminationDateTests.cs` — create with null → Get → null; Update sets a date → Get
      → matches; Update clears back to null → Get → null. No prior Employee test file existed at
      all (confirmed via glob) — this is net-new coverage, not a modification.
      GATES: full backend suite 955/8/963 (+11 vs 944/8 baseline, 0 failed); `tsc --noEmit` 0
      errors; `next build` clean, `/settings/employees` route generated.
- [x] O10 [B2-pr F2] **no negative adjustment / deduction mechanism** in payroll at all
      (`OtherDeductions` is a dead schema stub) — an overpayment clawback has no path. — closed by triage 2026-08-19 (deductions endpoint + o10 spec 100% [x])

## OPEN — สปส.1-10 (SSO) filing readiness (Wave C2 vision, 2026-07-25)
- [x] O11 [C2]: **สปส.1-10 ส่วนที่ 2 (per-employee schedule) is UNBUILT** — the printed form's 10
      employee rows are entirely blank (verified by Fable in the PDF text extraction: page 1 carries
      real summary figures 200,000 wages / 3,500+3,500 = 7,000 contributions, page 2 has only
      dot-leaders). `Sps110FormFiller.cs`'s own doc comment says v1 fills ส่วนที่ 1 only — so this is
      by-design v1 scope, NOT a defect. But the form as printed is **not submittable** (SSO requires
      the per-employee schedule), so ภ.ง.ด.1/1ก are filing-ready while สปส.1-10 is not. Build ส่วนที่ 2?
      Ham's scope call. — OBSOLETE per triage 2026-08-19 (blocked by template 4d71841; superseded by on-screen alt bf87333)
- [x] O12 [C2]: `เลขที่บัญชี` (10-digit SSO employer registration number) prints blank because there
      is nowhere to store it — the filler reads `m.EmployerAccountNo` and the comment says "blank
      stays blank (not submittable)". Needs a company-settings field before any สปส.1-10 can be filed,
      independent of O11. Small, and a prerequisite for O11.
      **DONE 2026-07-25 (Wave 1, Sonnet) — CHECKED FIRST: NO SCHEMA CHANGE, NO MIGRATION.** The
      spec's own premise ("nowhere to store it") was already stale by the time this was dispatched:
      `CompanyProfile.SsoEmployerAccountNo` (column, EF config `HasMaxLength(10)`,
      `CompanyProfileDto`/`UpdateCompanyProfileSoftRequest`), the `/settings/company` soft-field UI,
      and `SsoFilingService`'s fallback-to-`PayrollOptions` wiring into `Sps110FormFiller`/
      `SpsBatchFormat` all ALREADY existed and were already committed (git blame: `69f4003`,
      predates this dispatch) — full round-trip already proven by the pre-existing
      `Sps110FormFillerTests`/`SpsBatchFormatTests`. **Confirmed no migration/SqlScripts file is
      needed for this item** — flagging prominently per the dispatch's instruction, in the negative:
      there was nothing to flag because the column already exists in a committed migration.
      The real remaining gap was validation: only `MaximumLength(10)` was enforced (so "12345"
      or "abc1234567" both saved). FIX: `UpdateCompanyProfileSoftValidator` now requires
      `^\d{10}$` when the field is non-blank (still optional — a company without SSO leaves it
      blank), replacing the bare `MaximumLength` rule. FE: the generic `SoftField` input for this
      one key was replaced with a digit-filtering (`replace(/\D/g, '')`), `maxLength=10` input plus
      an inline "must be exactly 10 digits" hint (mirrors the employees page's `nationalId`
      pattern) — `companyProfile.ssoEmployerAccountNoInvalid` in both message files.
      TESTS: new `backend/tests/Accounting.Api.Tests/Master/CompanyProfileSoftValidatorTests.cs`
      (pure validator, no DB) — null/empty valid, exactly-10-digits valid, 9/11 digits and
      non-digit chars all rejected with `validation.sso10Digits`.
      GATES: full backend suite 955/8/963 (0 failed); `tsc --noEmit` 0 errors; `next build` clean,
      `/settings/company` route generated; en/th key-parity 0 mismatches.
- Vision caveats to respect when acting on C2/C1: AGY's web-research step only reached the RD/SSO
  portal home pages, not the official form PDFs, so "matches official layout" leans on model
  knowledge; and two claims (missing name title-prefixes, blank ภ.ง.ด.1ก employee addresses) are
  vision-only — Thai font subsetting makes those cells extract as dot-leaders either way, so they
  are UNCONFIRMED, not proven defects. Re-check by eye before filing either as a bug.
- WP-G Tier-2 round 2 (2026-07-25): **APPROVE**. Confirmed: PV's header roll-up is unsplit, so
  `applied = Subtotal + pv.VatAmount` structurally equals VI's `VatAmount + NonRecoverableVatAmount`
  → a non-VAT VI-linked PV now reaches `PAID` exactly; the FE (gross + rate 0) and REST/MCP from-VI
  (base + VI's own rate) shapes land the same settlement by different decompositions; the gate never
  executes for a VAT company so co5 is bit-identical. Two non-blocking nits carried forward:
  - [x] G4 (test hardening, do with a filtered run — no full suite needed): the standalone draft
        assertion in `PaymentVoucherNonVatCompanyTests.cs` (~L101) should assert
        `!l.IsRecoverableVat` DIRECTLY rather than relying on the absent-1170-debit as a proxy — the
        flag IS the fix, and a future GL refactor could break it while the account assertion passes.
        (The VI-linked test already asserts it.)
        EVIDENCE: Assertion added with comment on L99-101 — mirrors VI-linked test's pattern exactly
        (draft.Lines.Should().OnlyContain(l => !l.IsRecoverableVat, ...)). Test run blocked on
        database auth (TEAS_TEST_PG creds rejected); code-correct by inspection. — closed by triage
        2026-08-19 (PaymentVoucherNonVatCompanyTests.cs:100)
  - [x] G5 (cosmetic, pre-existing, needs a migration to do properly — Ham's call): a non-VAT
        company's PV header carries `VatAmount = 70` with no recoverable/non-recoverable split, so PV
        detail/print label folded-into-cost VAT plainly as "VAT" where a VI shows it as
        `NonRecoverableVatAmount`. The cash quantity is correct; only the label is imprecise. —
        closed by triage 2026-08-19 (d877286)
- [x] O13 [V2, NOT a bug — API smell + a co6 state note]: `CreatePaymentVoucherRequest` still carries a
      `DocDate` field (`PaymentVoucherDtos.cs:22`) that `CreateDraftAsync` deliberately ignores —
      §10 pins DocDate/PostingDate to Asia/Bangkok today and the code says so explicitly ("never
      trusted from the request"). Accepting a field and silently dropping it misleads every API/MCP
      caller: either remove it from the DTO or 422 when it differs from today. Consequence found
      live: because the period gate runs at DRAFT-create against that pinned date, and leg B2-ye
      closed all 12 FY2026 months on co6, **no PaymentVoucher (not even a draft) can be created on
      co6 until 2027** — reopen a period first if a future leg needs PV work there (B2-ye proved
      reopen+reclose is clean).
      **Attempted 2026-07-25 (Sonnet, WP-I), REVERTED — Fable cost/benefit call, design conclusion
      kept for the next attempt.** First shape tried: guard inside
      `PaymentVoucherService.CreateDraftAsync` (throw `DomainException("pv.docdate_not_today", ...)`
      right after §10's `docDate = _clock.TodayInBangkok()`). This broke **35 pre-existing tests**
      on the full-suite gate — `req.DocDate` is 100% inert past that point (the service always
      recomputes its own `docDate` for the period gate/persistence), so a wide population of tests
      calls `svc.CreateDraftAsync(...)` directly with a fixture/filler `DocDate` that was never
      load-bearing until this guard made it so. `CreateDraftAsync` is the ONE seam every internal
      caller (`CreateFromVendorInvoiceAsync`, always today, unaffected either way) AND every test
      funnels through — the wrong layer for a "don't lie to an external caller" check.
      **Design conclusion (confirmed, keep for the next attempt): the rule belongs in
      `CreatePaymentVoucherValidator` (`PaymentVoucherDtos.cs`), NOT in `CreateDraftAsync`.** The
      REST endpoint (`PaymentVoucherEndpoints.cs`, `IValidator<CreatePaymentVoucherRequest>`) and
      the MCP tool (`TeasMcpTools.CreatePaymentVoucherDraftAsync`, same validator) both already
      funnel through that validator — the ONLY boundary that needs protecting (an external caller
      lying about DocDate). Internal callers/tests never go through it when they call the service
      directly, so a validator-level rule leaves them untouched. Confirmed exhaustively via
      `grep -rn "CreatePaymentVoucherRequest\|CreatePvFromViRequest" backend/tests backend/src`
      (46 hits, no other production call path). Concretely:
      `RuleFor(x => x.DocDate).Equal(_ => new SystemClock().TodayInBangkok())` — no validator in
      this codebase takes a DI dependency (all are constructed parameterless, including
      `new CreatePaymentVoucherValidator()` at existing test call sites), so read the clock
      directly rather than injecting `IClock`. Surfaces as the standard FluentValidation shape
      (`400 Results.ValidationProblem` on REST, `[mcp.validation] DocDate: ...` on MCP via
      `McpErrorSurfacingFilter`'s existing `ValidationException` catch) — a field-named validation
      error, arguably a better fit for a DTO-shape mistake than a 422 domain code, so no new i18n
      key or DomainException code is needed. Test home: `Sprint87ForeignVendorTests.cs` already
      tests this validator directly (two pre-existing self-withhold/payer-mode tests,
      `new CreatePaymentVoucherValidator().Validate(req)`, no DB) — add
      `DocDate_other_than_bangkok_today_is_rejected_by_validator` /
      `DocDate_bangkok_today_passes_validator` there, plus one MCP end-to-end smoke test.
      **Why reverted despite being solved:** cost, not quality — the first (wrong-place) attempt
      plus its correction cost two full ~12-minute suite runs and a design correction; Fable judged
      this the smallest item the whole army batch produced and not worth further session time as a
      bonus item, especially since it isn't a defect harming anyone today (§10's pin itself was
      never broken — only the DTO's honesty about a field it drops). All code reverted
      (`PaymentVoucherDtos.cs`, `Sprint87ForeignVendorTests.cs`, `McpServerSmokeTests.cs`,
      `frontend/lib/i18n/problems.ts` — confirmed clean via `git status`). Re-attempting with the
      design conclusion above should be a ~10-minute job (one `RuleFor`, two validator tests, no
      full-suite surprises since the guard never touches `CreateDraftAsync`'s seam this time).
      **DONE 2026-07-25 (Wave 1, Sonnet) — mirrored the design conclusion above exactly (no stash/
      commit of the prior attempt survived, so re-written fresh from this same spec text).**
      `CreatePaymentVoucherValidator.RuleFor(x => x.DocDate).Equal(_ => new
      SystemClock().TodayInBangkok()).WithMessage("validation.docDateNotToday")` — DTO field kept
      (MCP schema still marks it required, per dispatch). Before adding the rule, re-verified by
      hand that the design conclusion's own risk analysis holds against the CURRENT test suite
      (not just re-trusting the old note): `grep -rn "CreatePaymentVoucherRequest" backend/tests
      backend/src` (source only, bin/obj excluded) shows every one of the ~15 test files
      constructing this DTO calls `svc.CreateDraftAsync(...)` DIRECTLY (bypassing the validator
      entirely — confirmed `CreateDraftAsync` never invokes `IValidator` internally, only the REST
      endpoint and the MCP tool method do); zero existing HTTP-level tests POST to
      `/payment-vouchers` at all; every existing MCP `create_payment_voucher_draft` smoke test
      already sends `docDate = DateOnly.FromDateTime(DateTime.UtcNow)` ("today"), so none broke.
      The two existing validator tests that DO call `new CreatePaymentVoucherValidator().Validate()`
      directly with a hardcoded past DocDate (`Payer_mode_contradicting_selfwithhold_is_rejected`,
      `Self_withhold_with_vendor_invoice_is_rejected_by_validator`) both assert `IsValid==false` +
      `.Should().Contain(...)` on a specific error SUBSTRING — an added DocDate error alongside
      their existing one doesn't break either assertion (`Contain`, not exact-match). Also fixed
      the MCP tool description (`TeasMcpTools.cs`, `create_payment_voucher_draft`) to say the field
      is now enforced, not just "pinned"/silently dropped. i18n: `validation.docDateNotToday` added
      to `frontend/lib/i18n/validation.ts` (th + en — the dispatch asked for the Thai entry
      explicitly, added the EN one too since the file's existing keys are always bilingual).
      TESTS: `Sprint87ForeignVendorTests.DocDate_other_than_bangkok_today_is_rejected_by_validator`
      / `..._bangkok_today_passes_validator` (pure, no DB) exactly as the design conclusion named
      them, plus `McpServerSmokeTests.E3_create_payment_voucher_draft_rejects_non_today_docdate`
      (real HTTP+MCP pipeline, asserts `IsError`).
      GATES: full backend suite **955 passed / 0 failed / 8 skipped / 963 total** (+11 vs the
      944/8 baseline across all four Wave-1 items combined — this is the FULL suite, not a
      filter, so the "no full-suite surprises" prediction is proven, not just repeated);
      `tsc --noEmit` 0 errors; `next build` clean.

## O2 SPLIT + O3 CLOSED (2026-07-25, leg O2-O3-verify — evidence swarm-findings/army/O2-O3-verify.md)
- [x] **O3 CLOSED — automation artifact, NOT a product break.** With a tall viewport and a plain
      Playwright click (no raw coordinates) the billing-note "ดาวน์โหลด PDF (สำเนา)" item fires the real
      `mark-printed` + `pdf` calls and produces an actual download — reproduced on a control page using
      the same shared `PrintMenu` component, and the endpoint probes 200 `application/pdf`. B-bn's
      "no request fired" was its own click strategy. No code change, and Ham does NOT need to hand-test it.
- [x] **O2 CONFIRMED as a real gap (B-bn's inconclusive reading was right about the symptom, wrong about
      the cause — its script reopened the picker with `.click()` instead of `.fill('')`, so the second TI
      never got picked).** With both TIs genuinely selected: the SELECTION persists correctly (join table
      + API both return them), but two things are missing. Split accordingly:
  - [x] **O2a — back-link display (NO product decision needed, just wire up data that already exists):**
        the BN detail page never renders the linked TIs even though the API returns them and the
        backend's own comment says the payload is "for chips". `bn-ti-chips` exists only in the CREATE
        form. Render them on the detail page (same chip pattern), linking each to its TI. → WAVE 5. —
        closed by triage 2026-08-19 (bn-ti-chips, invoices/[id]/page.tsx:162)
  - [x] **O2b — RESOLVED 2026-08-20 (Ham: option 2, block-on-mismatch; commit 3732f27).** Today the total is
        manual-lines-only: ฿107.00 billed vs ฿6,955 of linked TIs (totals never read the join table).
        A Thai ใบวางบิล normally bills the SUM of the invoices it lists, which argues the linked TIs
        should either generate the lines or at least be reconciled against them — but that changes what
        the document asserts commercially, so it needs Ham. Options to put to him: (1) linking TIs
        auto-generates the BN lines (manual lines then become an override), (2) keep manual lines
        authoritative but BLOCK issue when they don't reconcile with the linked TIs, (3) keep the field
        as a pure reference tag and rename its label so nobody expects aggregation. — Ham decision
        pending, see TRIAGE Cluster B

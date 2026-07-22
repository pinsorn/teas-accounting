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
- [ ] B1 **HIGH [B-bn F1]**: PV with WHT%>0 but Income-Type (50ทวิ) left "— ไม่หัก —" passes
      Draft-save AND Approve, shows a fully-computed misleading post-confirm preview, then post
      422s `pv.wht_type_missing`; Approved PV has NO edit/cancel affordance → permanently stuck
      (live: PV #19 co5). FIX (two halves):
      (a) validate early: client-side block + server-side 422 at draft-save (or at minimum approve)
          when any line has rate>0 && WhtTypeId==null — same error code, surfaced in Thai;
      (b) escape hatch: allow cancel/reopen-to-draft for Approved-but-unposted PVs (state-machine
          change — Opus review mandatory; check SoD: who may cancel).
      ACCEPTANCE: PV #19 on co5 can be unstuck via the new path after deploy.
- [ ] B2 **LOW [B-bn]**: `frontend/e2e/payment-voucher-with-wht.spec.ts` fills WHT% `'0.03'`
      commented "3%" — field takes plain percent (3). Fix value + add an assertion on the WHT amount.

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
- [ ] D1 **MEDIUM [B-ec F1]**: `StatusBadge.tsx` MAP + `messages/{th,en}.json` missing
      `Submitted`/`Paid` (PascalCase; existing `PAID` is a different enum) → raw keys
      `status.Submitted`/`status.Paid` visible on expense-claims list/detail. Add both entries ×3 files.
- [ ] D2 **MEDIUM [B-fa F-1]**: `depreciation/page.tsx` `handleGenerate()` ignores
      `res.alreadyExisted` → false success toast on re-run. Branch on it → show existing
      `alreadyPosted` string. (Concurrency catch-branch stays.)
- [ ] D3 **LOW [B-ec F2]**: expense-claims list/detail render generic "เกิดข้อผิดพลาด" on 403;
      use the same permission-named clean-deny the /new page uses (client permission check or map
      403 → ShieldAlert state).
- Gates: tsc 0 + next build + manual-glance screenshots per item from implementer.

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

# Spec: fix purchase-side UX/spec findings F1–F29 (prod UX test 2026-07-14)

Status: DRAFT for Ham review. Source: PROGRESS-purchase-uxtest.md (full findings log +
repro evidence). Test artifacts live in BU TEST @ Repttown (PO-TEST-0001/0002,
VI-TEST-0001, PV-OFFI-0001, PV-PROF-0001, WT-0001) — usable as live repro fixtures.

Verified-good (no work): chain PO→VI→PV→50ทวิ posts correctly; AP settlement flips VI
to PAID; vendor ledger reconciles to GL 2110; 50ทวิ picks ภ.ง.ด.3/53 by vendor type;
WHT rates auto-fill by income type; non-VAT vendor messaging on PV; ม.86/4 attach-file
completeness tracking; live A4 preview.

## Ham decisions (answered 2026-07-14 afternoon, in-chat)
- [x] D1 (→WP1.2): **force non-recoverable** — non-VAT co may enter VAT but it always
      books as cost (isRecoverableVat=false server-enforced), never claimable.
- [x] D2 (→WP3.3): **add draft edit** for PO (reuse create form + existing PUT).
- [x] D3 (→WP3.4): **CONFIRMED — implement Closed status** per Opus proposal (Approved→Closed:
      no further VI/PV linking, drops from open-PO lists, activity-logged, reopen if no posted
      downstream). WP3.4 now UNBLOCKED.
- [x] D4 (→WP4.9): **align text to behavior** — "ผู้อนุมัติควรเป็นคนละคนกับผู้สร้าง —
      super-admin ข้ามได้". No enforcement this release (option A). WP4.9 UNBLOCKED.
- [x] D5 (→WP1.1): **percent UI** — display/accept 7 (%), store fraction 0.07; MCP/API
      contract unchanged (fraction). Validation 0–100 on input, sane-set hint {0, 7}.
- [x] D6 (→WP2.1): **sliding re-issue (Option A)** — POST /auth/refresh re-issues on active
      session, absolute cap 8–12h + idle timeout + re-validate user. Not refresh-token (B, defer).
- [x] D7 (→WP1.5a): **auto-seed 19 expense categories** (with GL mapping) in CompanyService.
      CreateAsync for new companies; 623 backfill covers existing. Extract shared
      DefaultExpenseCategories helper.

## WP1 — MONEY/COMPLIANCE (footgun zone → Opus DESIGN → Sonnet implement, Tier-2 Opus review)
- [x] 1.1 F15+addendum: VAT-rate + WHT-rate fields are fractions behind %-labeled inputs,
      no bounds. Fix per D5: percent-presentation layer on VI "อัตรา VAT" and PV
      "หัก ณ ที่จ่าย %" + validation (VAT ∈ {0, 7} typical, hard-cap 0..1 fraction /
      0..100 percent; out-of-range = inline error, not silent accept).
      Accept: typing 7 yields ฿210 VAT on ฿3,000 base; typing 700 rejected.
      DONE 2026-07-14: `components/ui/PercentRateInput.tsx` (conversion in
      `lib/percent-rate.ts`, exact fractionToPercent/percentToFraction per design) wired into
      VI line vatRate + PV line whtRate. Grepped `type="number"`+Rate under
      `app/(dashboard)` — the only 2 other hits (`settings/companies` vatRatePct,
      `settings/wht-types` rate) already have their OWN correct percent↔fraction conversion
      (different bug class, out of scope). Backend bound `InclusiveBetween(0m,1m)` already
      present on BOTH `VendorInvoiceDtos.cs:158` (VI) AND `PaymentVoucherDtos.cs:106-107` (PV
      line WhtRate+VatRate) — design's "PV gap" was already closed by a prior commit; no
      backend change needed. Unit test `lib/percent-rate.test.ts` (6 cases, round-trip +
      clamp-700 + no float dust) — vitest run 40/40 green.
- [x] 1.2 F27 (per D1): non-VAT company VI VAT handling. Server-side rule + FE mirror.
      Accept: on vatMode=false co, posting VI with recoverable VAT is impossible.
      DONE 2026-07-14: `VendorInvoiceService.cs` `CreateDraftAsync` (reads `Company.VatRegistered`
      alongside the existing `RequiresBusinessUnit` query) forces `HasInputVat=false` + every
      line `IsRecoverableVat=false` when non-VAT — overrides vendor-derived flag AND an explicit
      `req.HasInputVat=true`. Re-asserted in `PostAsync` as defence. `CreateFromPurchaseOrderAsync`
      needs no separate touch — it delegates to `CreateDraftAsync`. FE mirror: VI form shows
      `companyNonVatInfo` advisory (via `useSystemInfo().vatMode`, NOT `useCompanyProfile()` —
      the design's pseudocode named the wrong hook; `CompanyProfile` has no vatRegistered field).
      GL posting code untouched. VI-TEST-0001 left as-is per design (documented, no data-fix).
      Integration test `VendorInvoiceNonVatCompanyTests.cs` (2 cases: forces non-recoverable +
      VAT-registered-company regression) — passing.
      **Opus Tier-2 review (2026-07-14, APPROVE-WITH-FIXES — core invariant confirmed good on
      every GL path) — 3 fixes applied, same branch, no commit:**
      - **F-1 (FE preview mismatch, MEDIUM):** the design's "FE mirror" said FORCE
        `recoverable=false` per row on a non-VAT company; only the advisory line shipped, so the
        live totals box still bucketed the 70 under the *recoverable* row while the server
        booked it as cost. Fixed in `vendor-invoices/new/page.tsx` at all 3 places `row.recoverable`
        is set (PO-link effect, `ExpenseCategorySelector` onChange) — both now AND with
        `companyVatRegistered` — PLUS the `vatRec`/`vatNon` reducers themselves now go through a
        `rowIsRecoverable()` helper that re-ANDs at read time (covers the pre-category-pick edge
        case a fresh row starts in). Verified: 1,000 @ 7% on a non-VAT co → total ฿1,070
        unchanged, but the ฿70 now lands in `vatNon` (non-recoverable), not `vatRec`.
      - **F-2 (UpdateDraftAsync gap, LOW):** `BuildLinesAsync` re-snapshots
        `IsRecoverableVat=true` from the category on every edit; `UpdateDraftAsync` had no guard,
        so an edited non-VAT-co draft's header went inconsistent until `PostAsync` self-healed
        it. Added the identical guard block after `BuildLinesAsync`, before `RollUp`, in
        `UpdateDraftAsync` (own one-column `Company.VatRegistered` query, same shape as
        `CreateDraftAsync`/`PostAsync`'s). New test `NonVatCompany_UpdateDraft_KeepsHeaderNonRecoverable`
        EXERCISES a real `CreateDraftAsync` → `UpdateDraftAsync` transition (amount 1000→2000,
        not a no-op) — confirmed non-vacuous by temporarily reverting the guard and re-running:
        failed with `IsRecoverableVat=True` as expected, then passed again after restoring.
      - **F-3 (fail-safe direction, LOW):** `CreateDraftAsync`'s missing-company-row fallback was
        `?? true` (unsafe direction — allows recoverable) while `PostAsync`'s equivalent query
        defaults to `false` (safe). Changed `CreateDraftAsync` to `?? false` so both guards fail
        safe identically. Unreachable in practice (an authenticated tenant always has a company
        row) but now aligned.
      Re-verified after all 3 fixes: `VendorInvoiceNonVatCompanyTests.cs` 3/3 passing; full
      Hardening+Persistence+Purchase+Master+Bootstrap regression sweep 262/262 passed (4
      pre-existing unrelated skips); FE `tsc --noEmit` + `next build` + `vitest run lib` (40/40)
      all green; Bengali-glyph grep clean. F-4 (vendor taxId grandfathering) and F-5 (internal-
      path DTO bound) intentionally NOT touched — Fable/Ham calls per the coordinator's message.
- [x] 1.3 F14: VI line pulled from PO defaults vatRate=0 even when company+vendor are
      both VAT-registered (on VAT co, per co2 verification pull is correct — restrict fix
      to deriving from vendor/company when PO line has no tax, not blanket 0.07).
      Accept: on co2, PO with no VAT data → linked VI line defaults to vendor-derived rate.
      DONE 2026-07-14: pure `derivePoLineVatRate` extracted to `lib/po-line-vat.ts` (4 unit
      tests), wired into the VI form's PO-link effect using `useSystemInfo()` for company VAT
      status. `PoLineDto` (lib/types.ts) does NOT expose `taxRate` — confirmed by reading the
      type — so the "prefer PO line's own rate" branch is future-proofing only, currently always
      falls through to the amount-derived/company-vendor-gated branches. KNOWN LIMITATION (not
      fixed, out of blast radius): on the "arrive via PO CTA" flow (fromPurchaseOrderId), the
      effect sets `vendorId` and derives `vatRate` in the same tick, so `vendor` (a separate
      react-query hook keyed on `vendorId`) is still stale/undefined on that first run — the
      derivation falls back to 0 in that one sub-case. The "manually link a PO from the dropdown"
      flow (vendor already selected first) is unaffected and is F14's original repro path.
- [x] 1.4 F13: vendor "จดทะเบียน VAT" requires 13-digit เลขผู้เสียภาษี (create+edit,
      server-side validation; existing rows grandfathered with warning on VI create).
      DONE 2026-07-14: `VendorDtos.cs` `CreateVendorValidator` + `UpdateVendorValidator` both
      gained `RuleFor(x => x.TaxId).NotEmpty()...When(x => x.VatRegistered && !x.IsForeign)`,
      error code `vendor.vat_registered_requires_taxid`. Wired at BOTH the REST endpoint
      (`MasterEndpoints.cs`) and the MCP tool (`TeasMcpTools.cs`) — verified by reading both
      call sites, so a pure-validator unit test covers both entry points. Took the design's
      "simplest defensible" grandfathering option (same rule on create+update, no
      changed-field gate) — flagged as the one sharp edge, per the design's own guidance.
      FE: `VendorForm.tsx` Zod `superRefine` mirrors the rule (`taxIdRequiredForVat` issue);
      ALSO wired `{err('taxId')}` to actually render — the taxId Zod error was NEVER displayed
      anywhere in the pre-existing form (dead code), so both the new rule and the pre-existing
      'taxId13' checksum rule are now visible for the first time. Asterisk on the taxId label
      when `vatRegistered && !foreign`. VI-create non-blocking warning banner added
      (`vendorTaxIdMissingWarning`) when the selected vendor is vat-registered-domestic with no
      taxId. `VendorVatTaxIdValidatorTests.cs` (7 cases: create×4, update×3) — all passing.
- [x] 1.5 F20: expense categories without default GL account (COGS on Repttown).
      Two parts: (a) seed/backfill mapping for auto-seeded categories (relates to co2/co3
      CreateAsync-bypass gap in memory), (b) FE: disable/badge categories with no account
      in the dropdown instead of 422 at save. Accept: COGS selectable+savable OR visibly
      marked unusable before save.
      DONE 2026-07-14, all 3 parts: (c) `ExpenseCategorySelector.tsx` disables any `<option>`
      with `defaultExpenseAccountId == null` + inline note (shape-parsing `pick()` extracted to
      `lib/expense-category-shape.ts`, 3 unit tests); `lib/types.ts` `ExpenseCategoryLite` gained
      `defaultExpenseAccountId` (BE `ExpenseCategoryDto` already exposed it — no BE change).
      (b) new `Migrations/SqlScripts/623_backfill_expense_category_accounts.sql`, mirrors the
      611 per-company `set_config('app.company_id',…)` GUC-loop pattern exactly (both
      `sys.expense_categories` and `master.chart_of_accounts` are G1 tables, FORCE RLS, no
      bypass arm); "5200" universal fallback (NOT 51010 — confirmed absent from
      `DefaultChartOfAccounts`), COGS prefers a real cost-of-sales account only if one exists.
      RLS repro test `ExpenseCategoryBackfillRlsTests.cs` uses `SET ROLE pg_database_owner`
      (per troubles-wiki, NOT "teas_rls_test" — that role SKIPs without CREATEROLE) + runs the
      ACTUAL script file content — passing (proves the GUC loop fills a NULL row under real RLS,
      not just under the test suite's superuser bypass connection). (a) per D7, added a
      `DefaultExpenseCategories(companyId, coaLookup)` helper (19-code set, mirrors
      430_seed_expense_categories_full.sql's codes/recoverable/capex/cogs flags, remapped onto
      `DefaultChartOfAccounts`' coarser codes since the granular 62xxx chart only the demo
      company gets) wired into `CompanyService.CreateAsync`. Integration test
      `CompanyCreateExpenseCategorySeedTests.cs` — 19 categories, zero NULL defaults, spot-checks
      RENT→5100, CAPEX→1610, COGS→5200 (no dedicated COGS account exists) — passing. Regression
      swept: no existing test asserts an exact expense-category count/emptiness on a
      TestCompanyFactory-created company; full `Hardening`/`Persistence`/`Purchase`/`Master`/
      `Bootstrap` folders re-run green (261 passed, 0 failed, 4 pre-existing unrelated skips).
      **Deploy note (not done — out of this dispatch's scope):** 623 is a prod-startup SqlScript
      (not demo-gated) — DB backup mandatory before deploy; post-deploy verify via the row-count
      probe in the design (`SELECT company_id, count(*) FILTER (WHERE
      default_expense_account_id IS NULL) …`).

## WP2 — AUTH/SESSION UX (F16 family; Opus design for token strategy, Sonnet FE)
- [x] 2.1 Token refresh: silent refresh or sliding session (current ~25-30 min hard
      expiry). Design owns choice (refresh token vs extended TTL + idle logout).
      DONE 2026-07-14 (D6 Option A — sliding re-issue): `POST /auth/refresh`
      (`AuthEndpoints.cs`) re-issues via `JwtTokenIssuer.Issue` on a still-VALID token only (an
      expired token 401s at the JWT bearer handler before the endpoint runs — sliding never
      resurrects a dead session). `auth_time` claim (`JwtTokenIssuer.cs`) stamped once at login,
      carried forward through every re-issue via `TokenClaims.AuthTime`; refusal past
      `Jwt:AbsoluteSessionCapHours` (default 10h, both appsettings) → 403. Live user
      re-validation via new `IUserRepository.FindByIdAsync` (active/not-locked) → 403 if failed
      — never blindly re-signs. Roles/perms RELOADED (not copied) from the caller's current
      company on every refresh (a revoked grant takes effect immediately). BFF
      `app/api/auth/refresh/route.ts` mirrors switch-company's cookie re-set exactly.
      `lib/useSessionKeepAlive.ts` — self-calibrating timer at 60% of the token's real TTL
      (learned from the refresh response, no extra plumbing), pauses on hidden tab, resumes on
      activity after idle (15 min cutoff), silently stops on any refresh failure (WP2.2 takes
      over at the next real API call). Mounted via `components/auth/SessionKeepAlive.tsx` in
      the dashboard layout. Backend tests `AuthRefreshTests.cs` (6):
      Refresh_WithValidToken_IssuesNewExpiry, _WithExpiredToken_401, _PastAbsoluteCap_403,
      _LockedUser_rejected, _InactiveUser_rejected, _WithoutToken_401 — all passing. Found +
      fixed 2 pre-existing RBAC test-harness gaps the new endpoint exposed (`RbacAuthMapTests`
      allowlist, `RbacCartesianTests` real-DB-user skip-set) — see troubles-wiki.md. Live-curled
      `/api/auth/refresh` end-to-end (valid cookie → 200 + fresh expires_at + re-set cookie).
      Deviation: CompanySwitchService does NOT carry AuthTime forward (out of this WP's file
      list) — a company switch implicitly resets the absolute-cap clock; flagged, not fixed.
- [x] 2.2 Global 401 handler: expired session mid-form → modal "session หมดอายุ —
      login ใหม่" + preserve form state (at minimum: don't leave buttons dead; F1 stale
      shell redirect included). Accept: expire token manually → any save shows the modal,
      re-login → same form still filled.
      DONE 2026-07-14: `lib/session-events.ts` (native `EventTarget`, no new dep — no existing
      global store found in `lib/`) + `lib/api.ts`'s `request()` dispatches `session-expired` on
      `res.status === 401`. `SessionExpiredModal.tsx` (in-place re-login via the existing
      `POST /api/auth/login`, never navigates) mounted in the dashboard layout. DEVIATION from
      the design's literal "401 AND title starts with auth." check: simplified to "any 401" —
      verified live that an EXPIRED (present-but-invalid) token's 401 passes through the proxy
      with NO JSON title at all (only the missing-cookie synthetic 401 carries one), so the
      title check would silently miss the main real-world case; every 401 reachable through this
      proxy is auth-gated by construction (login never goes through it), so status-only is both
      simpler and strictly more correct. Live-verified end-to-end (Demo Company, local dev):
      filled the VI create form → cleared the session cookie server-side without navigating →
      clicked Save → modal opened (toast "เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่" fired too, WP2.4) →
      form fields (vendor/tax-invoice-no/description/amount) still fully populated behind the
      modal → re-logged in → modal closed, form untouched → re-clicked Save → single POST → 201.
- [x] 2.3 F21: hanging duplicate POST after failed save (trailing-slash /api/proxy double
      request) — find root cause in proxy route handlers; a failed save must leave the
      form usable (no reload needed).
      DONE 2026-07-14. **Verify-before-fix: 308 CONFIRMED**, reproduced via curl against
      `next dev` BEFORE any code change — `POST /api/proxy/vendor-invoices/` → `308 Permanent
      Redirect` → `Location: /api/proxy/vendor-invoices`; the no-slash retry reaches the handler
      (401, no cookie). All 3 layers: (1) trailing slash removed from every static create path —
      18 occurrences (17 in `lib/queries.ts` incl. one the initial grep missed — `api-keys/` —
      caught by a follow-up sweep — + 1 inline in `payment-vouchers/new/page.tsx`); confirmed
      zero remain via a repo-wide regex sweep. (2) `AbortSignal.timeout(30_000)` wired into
      `api.ts`'s `request()` fetch call. (3) proxy 3xx hardening in
      `app/api/proxy/[...path]/route.ts` — forwards `Location` or returns 502, never a body-less
      hang. Live-verified: single `POST /api/proxy/vendor-invoices` (no slash) → 201, on two
      separate saves (network trace captured both times, zero 308s, zero duplicate requests).
      FE tests: `lib/queries.trailing-slash.test.ts` (source-scan regression guard) +
      `lib/api.timeout.test.ts` (AbortSignal wiring, mocked fetch + shrunk timeout — doesn't
      wait out the real 30s).
- [x] 2.4 F19: error toasts — Thai translations for domain errors, longer/sticky
      duration for errors, keep EN detail collapsible.
      DONE 2026-07-14: new `lib/i18n/problems.ts` (~55 codes, TH-only dict mirroring
      `validation.ts`'s pattern — an `en`-locale user already sees the backend's own English
      detail, so no parallel EN dict). `errorToToast` (`lib/api/errors.ts`) resolves Thai-by-code
      first (unchanged signature — ALL ~30 existing `toast.error(errorToToast(e))` call sites get
      correct Thai text for free, zero touch). `problemToast` (`lib/api.ts`) additionally gets
      `duration: 8000` + the original detail as a sonner `description` (secondary line) —
      benefits its existing callers (PV/PO/VI detail pages) automatically. New `apiErrorToast`
      (delegates to the enhanced `problemToast`) swapped into the 5 purchase-scoped create/quick-
      create forms still using the bare pattern (`vendor-invoices/new` ×2, `payment-vouchers/new`,
      `VendorForm.tsx`, `VendorQuickCreateForm.tsx`, `CreateViFromPvDialog.tsx`) so the full
      sticky+collapsible experience reaches every purchase-side error toast, not just the ones
      already on `problemToast`. Non-purchase pages left on the (now Thai-corrected) plain
      `errorToToast` path — out of this spec's domain. FE unit tests (`lib/api/errors.test.ts`,
      4 cases: known code → Thai, unknown code → detail fallback, non-ApiError fallback) +
      live-verified (the WP2.2 repro's `auth.unauthenticated` 401 rendered as the Thai toast
      "เซสชันหมดอายุ กรุณาเข้าสู่ระบบใหม่", not the raw "No session." detail).

## WP3 — FLOW/DISCOVERABILITY (Sonnet direct, spec-airtight)
- [x] 3.1 F18: add "+ บันทึกใบกำกับภาษีซื้อ" button on /vendor-invoices list; fix stale
      subtitle ("สร้างจากใบสำคัญจ่าย (PV → บันทึก)" no longer true).
      DONE 2026-07-14 (branch fix/purchase-ux-wp3-wp4): create button + PermissionGate in
      `vendor-invoices/page.tsx`; `vi.createFromPvHint` text updated in th/en.json.
- [x] 3.2 F8: approved-PO action bar gets "บันทึกใบกำกับภาษีซื้อ" CTA (→ /vendor-invoices/new
      ?fromPurchaseOrderId=; primary CTA before สร้างใบสำคัญจ่าย to match chain order).
      DONE: CTA added in `purchase-orders/[id]/page.tsx` (before createPv); VI form
      (`vendor-invoices/new/page.tsx`) reads `fromPurchaseOrderId`, preselects vendor +
      links the PO via the existing PO-prefill effect.
- [x] 3.3 F6 (per D2): PO draft edit (reuse create form, PUT exists per API manual).
      DONE: extracted `components/forms/PurchaseOrderForm.tsx` (create/edit, mirrors
      QuotationForm's pattern) + `useUpdatePurchaseOrder` (PUT, same
      CreatePurchaseOrderRequest shape per backend `PurchaseOrderEndpoints.cs`) + new
      route `purchase-orders/[id]/edit/page.tsx` (Draft-only, bounces to detail
      otherwise) + "แก้ไข" button on the PO detail page for Draft POs.
- [x] 3.4 F29 (per D3): PO close — implement semantics. DONE 2026-07-15 (branch
      fix/purchase-ux-wp34-wp49). Discovery: the backend close action (endpoint, service
      `CloseAsync`, domain `MarkClosed`, activity log) and the FE `po-close` button were
      ALREADY wired end-to-end (commit d88ee51) — the dispatch's "wiring gap" premise was
      stale by the time this task ran. Remaining gaps closed: (1) FE close button called
      `run('close')` directly with no confirmation — wrapped in `ConfirmActionDialog`
      (`confirmAction.poClose`, warning text exactly per D3/dispatch copy). (2) VI-link
      guard: the FE picker (`usePurchaseOrders('Approved', …)`) already excludes Closed
      POs, and `CreateFromPurchaseOrderAsync` already required Approved at create time —
      but the RAW create path (`VendorInvoiceService.CreateDraftAsync` with a bare
      `req.PurchaseOrderId`, reachable via REST/MCP without going through the from-PO
      helper) had NO guard; `PostAsync`'s existing check only rejected Draft/Cancelled,
      never Closed. Added a create-time guard in `CreateDraftAsync` (throws
      `po.not_approved` for any non-Approved linked PO) — the single seam both create
      paths funnel through. Deliberately left `PostAsync`'s guard untouched (spec:
      "already-linked/posted VIs/PVs are untouched" — a VI created while the PO was still
      Approved must still be postable after the PO closes). (3) Reopen — assessed as a
      SIMPLE query ("no posted downstream VI" = no Posted VendorInvoice with this
      PurchaseOrderId; PaymentVoucher has no direct PO FK, only via VendorInvoiceId, so
      checking VI coverage is sufficient) — IMPLEMENTED: domain `PurchaseOrder.MarkReopened`
      (Closed→Approved), `PurchaseOrderService.ReopenAsync` (blocks with
      `po.reopen_blocked` if a Posted VI is linked), `POST /purchase-orders/{id}/reopen`
      (same `purchase.purchase_order.cancel` permission scope as close — no new RBAC
      permission code needed, confirmed by re-running `RbacAuthMapTests`, which
      regenerated `docs/rbac/endpoint-permission-map.generated.md` to include the new
      route), FE reopen button (`po-reopen`, shown on Closed status) +
      `confirmAction.poReopen` dialog. (4) Status badge "ปิดแล้ว" and CTA-hiding on Closed
      were ALREADY correct pre-existing behavior (StatusBadge's `status.Closed` map;
      approve/VI/PV/mark-sent CTAs are all gated on `d.status === 'Approved'`, so Closed
      drops out automatically) — no change needed, verified by reading, not re-implemented.
      Files: `PurchaseOrder.cs` (+MarkReopened), `PurchaseOrderDtos.cs` (+ReopenAsync iface),
      `PurchaseOrderService.cs` (+ReopenAsync), `PurchaseOrderEndpoints.cs` (+/reopen route),
      `VendorInvoiceService.cs` (+create-time PO-link guard), `purchase-orders/[id]/page.tsx`
      (confirm dialogs + reopen button), `messages/th.json`+`en.json` (poClose/poReopen/
      reopen label/activityAction Closed+Reopened). Tests: new
      `PurchaseOrderCloseTests.cs` (5 cases: close Approved→Closed, close Draft rejected,
      VI-link-to-Closed rejected, reopen with no posted VI succeeds, reopen with a posted
      VI blocked — the last one EXERCISES the real auto-close-on-post transition, not a
      seeded Closed row) + `PurchaseOrderStateMachineTests.cs` (+1 domain unit test,
      Reopen_only_from_closed). All passing; Hardening folder regression (194
      passed/4 pre-existing skips, same skip count as WP1.2's prior run) clean.
- [x] 3.5 F24: PV from ชำระด้วยใบสำคัญจ่าย prefills vendor + line (desc "ชำระ <VI docNo>",
      amount = VI outstanding) — user adjusts, not re-keys. Accept: one click from posted
      VI → PV form complete except payment method review.
      DONE: `payment-vouchers/new/page.tsx` — new viPrefilled effect sets vendorId,
      businessUnitId, expense category + recoverable flag (from the VI's first line),
      and one row with `pv.settleLineDesc` ("ชำระ {docNo}"). All fields stay editable.
      Bug found + fixed during live verification (local dev, Demo Company): the PV line
      "amount" field is always the PRE-VAT base (the form re-derives VAT from
      productType and adds it on top), but VI outstanding (totalAmount − settledAmount)
      is VAT-INCLUSIVE — seeding amount=outstanding verbatim double-counted VAT
      (฿1,070 outstanding → ฿1,144.90 total). Fixed by scaling the VI's own
      subtotalAmount by the outstanding ratio instead, so the re-derived total lands
      back on the VI's outstanding figure (exact with no prior partial settlement — the
      common case). Verified end-to-end: VI 06-2026-VI-0007 (฿1,070 outstanding) →
      PV #26 saved with Grand Total ฿1,070.00 exactly, notes correctly show the VI's
      docNo (ties to 4.8 too).
- [x] 3.6 F7/F28: confirmation dialog on PO approve and PV approve/post (mirror VI post
      modal: totals + immutable warning). One shared confirm component.
      DONE: new `components/ui/ConfirmActionDialog.tsx`, wired into PO approve
      (`purchase-orders/[id]/page.tsx`, both the inline button and the ?action=approve
      banner CTA), PV approve and PV post (`payment-vouchers/[id]/page.tsx`).
- [x] 3.7 F9: "ส่ง PO ให้ vendor" → relabel "บันทึกว่าส่งแล้ว" (or add real email later —
      out of scope now) + confirm/undo of the stamp.
      DONE: `purchaseOrder.sentToVendor` relabeled in th/en.json; mark-sent button now
      opens ConfirmActionDialog (simple variant, no totals) before stamping. "Undo" not
      implemented — out of scope per dispatch (email/undo explicitly deferred).
- [x] 3.8 F4: vendor picker modal "+ เพิ่มผู้ขายใหม่" quick-create (name/type/VAT only).
      DONE: `EntityPickerModal` gained a generic `renderQuickCreate` slot; new
      `components/forms/VendorQuickCreateForm.tsx` (code/nameTh/type/VAT toggle → POSTs
      existing `useCreateVendor`, auto-selects on success); wired in
      `components/create/PartySelectBox.tsx` for `kind==='vendor'` — shared by PO/VI/PV
      forms (all three already used PartySelectBox). Verified live (local dev): form
      renders, fields work, POST creates a real vendor row (confirmed via API). NOTE:
      hit the pre-existing F21 double-POST bug (WP2.3, out of this dispatch's scope) —
      the vendor was created successfully but a duplicate request's 422 surfaced a
      false "Unexpected error" toast instead of auto-selecting; reopening the picker
      and searching finds the new vendor. Not a regression from this work — same
      trailing-slash proxy race already logged against vendor-invoices in F21; will
      self-resolve once WP2.3 lands.

## WP4 — POLISH/i18n/a11y (Haiku-able mechanical batch where zero-judgment, else Sonnet)
- [x] 4.1 F2: Thai BE date display for all date inputs (or dual hint) — pick one pattern
      app-wide; native input stays, add BE hint text under field.
      DONE: `formatDateBE()` added to `lib/utils.ts` (dd/MM/yyyy-BE, parsed directly from
      the yyyy-MM-dd string, no Date()/timezone drift); hint wired into the shared
      `DateInput` component (covers every locked docDate use) plus every raw native
      date input on purchase pages: PO create/edit (docDate, expectedDeliveryDate), VI
      new (vendorTaxInvoiceDate), PV new (chequeDate). Native inputs unchanged.
- [x] 4.2 F3: PO/VI list "หน่วยธุรกิจ" column → BU code/name, not #id.
      ALREADY DONE in current code (no change needed) — both list pages already render
      via the shared `useBusinessUnitName()` hook (`buName(r.businessUnitId)` →
      "CODE — nameTh", `useBusinessUnits(includeInactive=true)` so a since-deactivated
      BU still resolves). F3 was very likely observed against a stale prod session
      (same window as the F16 session-expiry findings) or a pre-cont.82 deploy — the
      shipped code (commit fe16cd4, well before this branch) already fixes it.
- [x] 4.3 F11: activity log event labels → Thai ("Created → Draft" etc.).
      DONE: `common.activityAction` map added (Created/Draft/Approved/MarkedSent/Sent/
      Posted) in th/en.json; `ActivityLog.tsx` looks up both `action` and `toStatus`
      via `t.has()`, falling back to the raw code for anything not in the map.
- [x] 4.4 F17: restore last-used company after re-login (localStorage).
      DONE: `CompanySwitcher` persists the chosen id to `localStorage` (`LAST_COMPANY_KEY`,
      `lib/utils.ts`) on every successful switch; `login/page.tsx` calls
      `restoreLastCompany()` right after a successful login (before the redirect) —
      reads the stored id, confirms via `/api/proxy/me` that the user is still a
      super-admin with access to it, and POSTs `/api/auth/switch-company` if so. Fails
      silently on any error (never blocks login); no-op for non-super-admins (only
      super-admins have `allowedCompanies` / can switch at all).
- [x] 4.5 F10: refresh เอกสารอ้างอิง/ประวัติกิจกรรม panels after approve/post/mark-sent.
      DONE — root cause: `usePurchaseOrderAction`, `usePostVendorInvoice`,
      `useApprovePaymentVoucher`, `usePostPaymentVoucher` never invalidated the
      `['purchase-chain', …]` / `['activity', …]` query keys the two side-rail panels
      read (both have `staleTime: 30_000`), so they sat stale until a full reload.
      Added `qc.invalidateQueries({queryKey:['purchase-chain']})` +
      `['activity']` to all four mutations in `lib/queries.ts`.
- [x] 4.6 F12: form inputs get proper label association (a11y) — vendor form first.
      DONE: every input/select/textarea/checkbox in `VendorForm.tsx` (+ `TaxIdInput.tsx`,
      rendered only from VendorForm) now has an explicit `id` + matching `htmlFor` on
      its wrapping `<label>`, on top of the pre-existing implicit wrap-association —
      removes any ambiguity for AT regardless of how the label's accessible name is
      computed.
- [x] 4.7 F22: VI post-confirm title "ใบรับวางบิล (ผู้ขาย)" → "ใบกำกับภาษีซื้อ".
      DONE: `postConfirm.title.vendor_invoice` → "ยืนยันการบันทึกใบกำกับภาษีซื้อ" in
      th.json (en.json updated to match).
- [x] 4.8 F23: user-facing refs use docNo not internal #id once issued (PV subtitle,
      50ทวิ "อ้างอิงใบสำคัญจ่าย: PV #3").
      DONE: PV subtitle/description (`payment-vouchers/new/page.tsx`) now uses
      `vi?.docNo ?? #${fromVi}` (falls back to #id only while the VI lookup is still
      loading); 50ทวิ detail (`wht-certificates/[id]/page.tsx`) fetches the PV via
      `usePaymentVoucher` and shows `pv?.docNo ?? PV #${id}`.
- [x] 4.9 F25 (per D4): align SoD text with actual enforcement. DONE 2026-07-15 (branch
      fix/purchase-ux-wp34-wp49). Primary target `pv.sodHint` (th.json/en.json) — the
      literal string named in D4 ("ผู้อนุมัติต้องไม่ใช่ผู้สร้าง (SoD)") — changed to
      "ผู้อนุมัติควรเป็นคนละคนกับผู้สร้าง — ผู้ดูแลระบบ (super-admin) ข้ามได้" (Option A,
      no enforcement change), rendered on the PV detail page's approve button title +
      the Draft-status hint line. ALSO fixed `confirmAction.pvApprove.warning` (same PV
      page, shown in the approve `ConfirmActionDialog`) — it carried the identical
      absolute claim ("...ผู้อนุมัติต้องไม่ใช่ผู้สร้าง") without the SoD suffix; left
      uncorrected it would have contradicted the just-fixed hint on the very same page/
      click path, undermining D4's "align text to actual behavior" intent. Left
      `pv.approveWarn` untouched — grepped and confirmed it is DEAD (defined in both
      message files, referenced nowhere in `frontend/`), so not part of this finding's
      actual UI surface. EN equivalents updated in en.json (sodHint,
      confirmAction.pvApprove.warning). No backend/enforcement change (Option A, per D4).
- [x] 4.10 /wht-certificates list: แบบยื่น "Pnd3"→"ภ.ง.ด.3", ม.40 column "8"→"40(8) ค่าบริการ".
      DONE (list page only, per dispatch — detail page already renders both fields
      acceptably per PROGRESS-purchase-uxtest.md's note): `wht.formTypeMap` (Pnd3/Pnd53)
      + `wht.section40` ("40({code})") added to th/en.json, applied in
      `wht-certificates/page.tsx` columns. No short-name suffix — `WhtCertificateListItem`
      doesn't carry the WHT-type description (only the detail DTO does), so "+ short
      name if available in data" correctly resolves to "not available" here.

## WP3/WP4 verification note (2026-07-14, branch fix/purchase-ux-wp3-wp4)
`tsc --noEmit` + `next build` green (0 errors) after every edit; Bengali-glyph guard
clean on all changed files. Live-clicked through the riskiest/most novel items on
local dev (backend :5080 + `npm run dev` :3000, logged in as `admin`, Demo Company —
the seeded e2e/rbac-ui test company): PO #11 draft → edit (3.3, PUT persisted,
BE date hints visible) → approve (3.6 confirm dialog, correct totals) → doc-numbered
+ Approved (4.5 panels updated live, no reload) → new "บันทึกใบกำกับภาษีซื้อ" CTA before
"สร้างใบสำคัญจ่าย" (3.2) → mark-sent relabeled + simple confirm dialog (3.7) → VI create
form prefilled vendor+PO link (3.2, BE hint on tiDate) → vendor quick-create form (3.8,
POST verified via API) → VI-0007 (Posted) "ชำระด้วยใบสำคัญจ่าย" → PV prefilled vendor/
category/line (3.5) → saved PV #26 with correct total (see 3.5 bug note above) → VI
list create button + subtitle (3.1) → wht-certificates list Thai form-type/ม.40 mapping
(4.10) → vendor form label association confirmed via `el.labels[0]` at the DOM level
(4.6, not just the a11y-tree tool's own rendering). PV approve/post dialogs reuse the
exact same `ConfirmActionDialog` already proven on PO approve, not re-clicked
separately. 4.2 and 4.4 verified by code reading only (4.2: already-correct pre-existing
code; 4.4: needs a two-company super-admin + fresh login cycle, impractical to stage
safely against this shared local demo DB).

## No action
- F5: PO VAT display gated by vatMode && vendor.vatRegistered = by design (wiki entry
  exists; manual already documents the conditional).
- F26: duplicate of F16 (resolved — PV post works on live session).

## Ordering & notes
- WP1 + WP2.1/2.2 = one release (money + auth); WP3/WP4 can trail.
- Every WP1 item: server-side rule is the source of truth, FE mirrors. JE/GL code paths
  untouched (settlement verified good) — changes stop at validation/derivation layer.
- Manual ch.5: the F15 warning admonition added 2026-07-14 gets REMOVED when 1.1 ships
  (walkthrough 05.02 step-03b then re-captured to show the percent field).
- Test fixtures: reuse BU TEST docs for regression; add integration tests per WP1 item
  (fraction/percent boundary, non-VAT co VI, category-without-account).

---

# Design (Opus, 2026-07-14)

Scope of THIS section: WP1.1–1.5 (money/compliance) + WP2.1–2.4 (auth/session UX),
plus D3/D4 PROPOSALs and D5 percent-UI enumeration. WP3/WP4 are already spec-airtight
and out of this design pass. All file:line refs verified against the tree at commit d795c98.

## Context / footguns folded in (grep troubles-wiki.md + memory)
- **RLS forced on tenant tables + startup has no tenant GUC** (troubles-wiki "Startup
  SqlScript writing/reading … RLS'd tables fails 42501 or silently no-ops on prod",
  2026-07-09; memory "RLS masked by superuser tests"). `sys.expense_categories` is in the
  RLS list (`010_rls_policies.sql:13`) with **ENABLE + FORCE ROW LEVEL SECURITY** (`:21-22`)
  and policy `company_isolation USING (company_id = current_setting('app.company_id')::int
  OR is_super_admin)` (`:28-29`). FORCE = even the table owner obeys RLS; prod app role is
  NOBYPASSRLS. At API startup **no `app.company_id` GUC is set**, so a bare
  `INSERT/UPDATE … SELECT FROM master.companies` on this table is rejected 42501 OR silently
  updates 0 rows — and **teas_test/dev connect as SUPERUSER so RLS is bypassed and the bug is
  invisible in tests**. Any WP1.5 backfill MUST follow the `611_seed_retained_earnings_account.sql`
  per-company GUC-loop pattern (see WP1.5b). `master.companies` itself carries NO RLS (tenant
  root) so the company-id list read is unfiltered.
- **PO/PV VAT display is company/vendor-config-dependent** (troubles-wiki, 2026-07-14):
  `vendorVat = vatMode && vendor.vatRegistered`. Repttown is a non-VAT company
  (`Company.VatRegistered=false`) → no VAT row + PO-pull rate 0 is BY DESIGN, not regression.
  This is why WP1.2 (F27) is real: a non-VAT company must never book **recoverable** input VAT.
- **taxRate/vatRate is FRACTIONAL everywhere** (0.07, never 7) — MCP contract, API DTOs, and
  storage. D5 changes ONLY the presentation layer; every payload/DTO/column stays fraction.
- **teas_test is shared + random-year/relative-date flaky** (memory): integration tests must
  pin doc dates to today/future (seed 400 closes prev-month per CURRENT_DATE); set
  `TEAS_TEST_PG` per PowerShell shell (env dies between calls — a skipped test fakes green).
- **Vendor/company force-flags**: foreign vendors are force `VatRegistered=true`
  (`MasterDataServices.cs:70,:91`) — scope the WP1.4 taxId rule to DOMESTIC vat-registered.

## NON-goals (explicit — do not touch)
- **No JE/GL posting-path changes.** `GlPostingService.PostVendorInvoiceAsync`
  (`GlPostingService.cs:308+`) and settlement are verified good (VI→PV→GL 2110 reconciles).
  WP1.2 changes the *inputs* to posting (`HasInputVat`, line `IsRecoverableVat`) at the
  create/validate seam — never the ledger math itself.
- **No MCP contract change.** `taxRate`/`vatRate` stays fractional on every MCP tool + API DTO.
  D5 is FE-display-only.
- **No new schema/columns.** `default_expense_account_id` already exists (nullable). WP1.5 is
  data + validation + FE, no migration DDL.
- **No mutation of posted JEs.** VI-TEST-0001's recoverable-VAT row (see WP1.2 §data) stays;
  posted journals are immutable by design (no void) — any correction is a *reversing* entry
  through the normal path, which is out of scope here.

---

## WP1.1 — percent-UI for rate fields (D5). Presentation-only; storage stays fraction.

**Root state today (both hand-rolled raw-fraction number inputs — the F15 defect):**
- VI "อัตรา VAT": `vendor-invoices/new/page.tsx:304-309` — `<input type="number" step="0.01"
  value={r.vatRate}>` bound to the fraction; typing `7` stores `7` → preview `amount*7`
  (`:97-99`) → 700% VAT; payload sends `vatRate: r.vatRate` (`:128`). `emptyRow` default 0.07
  (`:34`).
- PV "หัก ณ ที่จ่าย %": `payment-vouchers/new/page.tsx:382-385` — same shape on `r.whtRate`;
  the WHT-type dropdown auto-fills `picked.rate` (fraction, e.g. 0.03) at `:371`; payload
  `whtRate: r.whtRate` (`:173`).

**Proven in-repo pattern to reuse (Ponytail):** the SALES side already solved this in
`components/ui/LineItemsTable.tsx:195-209` — VAT rate is a **dropdown {7% / 0%}** whose
`value` is the fraction and whose label is `Math.round(l.taxRate * 100)%`; it reads the
company standard rate `stdRate = sys?.vatRate ?? FALLBACK_VAT` (`:88`) and hides the VAT
column entirely for non-VAT companies via `showVat` (`:90,:113`).

**Design — one shared control + exact conversion:** add `components/ui/PercentRateInput.tsx`
(~20 lines, mirrors `AmountInput.tsx`). Contract:
- Props: `value: number` (FRACTION, source of truth), `onValueChange: (fraction: number) => void`,
  `max?: number` (percent cap, default 100), `quickSet?: number[]` (percent chips), `disabled`,
  `aria-label`.
- Render `<input type="number" inputMode="decimal" min={0} max={max} step="0.01">` whose
  **displayed** value is `fractionToPercent(value)` and whose onChange stores
  `percentToFraction(clamp(inputPercent, 0, max))`.
- **Conversion (pin exactly — float footgun):**
  `fractionToPercent(f) = Math.round(f * 1e6) / 1e4`  (0.07 → 7, not 7.000000001; keeps ≤4 dp)
  `percentToFraction(p) = Math.round(p * 1e6) / 1e8`   (7 → 0.07; 1.5 → 0.015)
  Clamp percent to `[0, max]` BEFORE converting; reject non-finite → 0.
- Suffix label "%" inside/after the field; optional quick-set chips.

**Wire-in:**
- VI: replace the `:304-309` input with `<PercentRateInput value={r.vatRate}
  onValueChange={(f) => setRow(r.key, { vatRate: f })} max={30} quickSet={[0, 7]}
  aria-label={t('vatRate')} />`. Nothing else changes — `r.vatRate` stays the fraction the
  payload already sends. (`max={30}` gives headroom over 7% without allowing 700%.)
- PV: replace the `:382-385` input identically with `value={r.whtRate}` `onValueChange={(f) =>
  setRow(r.key, { whtRate: f })} max={30} aria-label={t('wht') + ' %'}`. The WHT-type
  auto-fill at `:371` still writes the fraction (`picked.rate`) into `whtRate` — unchanged,
  and it now DISPLAYS as e.g. `3` because the control converts. No quick-set (WHT rates vary).

**Full enumeration of rate-showing inputs (D5 — so none is missed):**
| # | Screen | Field | File:line | Editable? | Action |
|---|--------|-------|-----------|-----------|--------|
| 1 | VI create | อัตรา VAT (per line) | vendor-invoices/new/page.tsx:304-309 | yes (raw fraction) | → PercentRateInput |
| 2 | PV create | หัก ณ ที่จ่าย % (per line) | payment-vouchers/new/page.tsx:382-385 | yes (raw fraction) | → PercentRateInput |
| 3 | Sales lines | VAT rate | components/ui/LineItemsTable.tsx:195-209 | dropdown, already %-correct | NO CHANGE (reference) |
| 4 | PV WHT type | rate auto-fill | payment-vouchers/new/page.tsx:365-372 | derived | display via #2 control |
| 5 | Read-only previews | VAT/WHT in PaperDocument, totals boxes | (paper/*, TotalsSummaryBox) | display-only, already formatted | NO CHANGE |
The implementer must grep `type="number"` + `Rate` / `rate` under `frontend/app/(dashboard)`
to confirm no other editable fraction input exists (expected: only #1 and #2).

**Backend defence-in-depth (keep, extend):** `VendorInvoiceDtos.cs:158` already bounds
`VatRate InclusiveBetween(0m, 1m)` on the REST create path — so even pre-fix, a raw `7` POST
is 422'd there. GAP: the PV line validator and the service/domain layer have NO such bound,
and `VendorInvoiceService.CreateFromPurchaseOrderAsync` / PV→VI internal paths bypass the
endpoint validator. Add `InclusiveBetween(0m, 1m)` to the PV line's `WhtRate` and `VatRate`
in the PV DTO validator (mirror `:158`); leave the GL math untouched. The FE percent control
keeps user input in-range so it never trips — the validator is the safety net for
API/MCP/PO-derived callers.

**Worked examples (base ฿3,000):**
- Type `7` → store `0.07` → VAT `Math.round(3000*0.07,2)=฿210.00` → total ฿3,210. ✓ (spec accept)
- Type `700` → clamp to `max=30` (or inline error) → never stores 7.0; no ฿21,000 preview. ✓
- WHT type "ค่าบริการ(บุคคลธรรมดา) 3%" picked → `whtRate=0.03`, field shows `3`; on ฿20,000
  → WHT `฿600`. ✓ (matches the verified 07-2026-WT-0001 fixture)

**Test plan:** (a) FE unit test `PercentRateInput.test.tsx` — round-trip 0.07↔7, 0.015↔1.5,
clamp 700→cap, empty→0, no float dust (assert `percentToFraction(7)===0.07`). (b) Extend PV
validator test: `WhtRate=7.0` → 422; `WhtRate=0.03` → ok. (c) Manual: VI form type 7 → preview
฿210.

---

## WP1.2 — non-VAT company must not book recoverable input VAT (F27, D1). Server = source of truth.

**Confirmed gap:** `VendorInvoiceService.CreateDraftAsync` derives
`HasInputVat = req.HasInputVat ?? !(!vendor.VatRegistered || (vendor.IsForeign &&
!vendor.HasThaiVatDReg))` at **`VendorInvoiceService.cs:108-109`** — keyed ONLY on the
**vendor's** flags. The only company read in the whole VI pipeline is
`RequiresBusinessUnit` (`:78-80`); `Company.VatRegistered` / `Company.VatRate` are never
consulted. Line `IsRecoverableVat` is snapshotted from the category at `:213-216`. GL posting
gates recoverable input VAT on `recoverable = vi.HasInputVat && l.IsRecoverableVat`
(`GlPostingService.cs:325-326`). So on Repttown (`Company.VatRegistered=false`) a VAT-registered
vendor's line posted 70 baht as **recoverable** input VAT — the F27 defect.

**Company VAT flag location:** `Accounting.Domain/Entities/Master/Company.cs:23-33` —
`bool VatRegistered`, `decimal VatRate = 0.07m`, `string Pnd30SubmissionMode`. Read the flag
once in `CreateDraftAsync` (same tenant, one extra column on the existing `:78-80` query — add
`.Select(c => new { c.RequiresBusinessUnit, c.VatRegistered })`).

**Enforcement design (D1 = "may ENTER VAT but it always books as cost"):** in
`CreateDraftAsync`, after computing lines, when `company.VatRegistered == false`:
1. Force header `HasInputVat = false` (override the vendor-derived value AND any client/MCP
   `req.HasInputVat=true`).
2. Force every line `IsRecoverableVat = false` at the `:213-216` snapshot (so the header
   rollup at `:222-229` puts all VAT into `NonRecoverableVatAmount`, not `VatAmount`, keeping
   header and GL consistent — otherwise header would still show "recoverable" while GL books
   cost).
This flips `GlPostingService.cs:325` to `expenseDebit = l.Amount + l.VatAmount` (cost),
matching D1. Do NOT block entry, do NOT change GL math. Re-assert the same guard in
`PostAsync` (`:280`) as defence (a draft created before the fix, or via another path, must not
post recoverable on a non-VAT co). Also apply the identical guard to
`CreateFromPurchaseOrderAsync` (`:134`) since it constructs lines independently.

**FE mirror (advisory, non-authoritative):** in `vendor-invoices/new/page.tsx`, read
`useCompanyProfile()` (already imported as `company`, `:53`) for `vatRegistered`. When false:
force each row's `recoverable=false` and disable the recoverable toggle, and show the existing
`noInputVatInfo` info line (`:219`) reason "บริษัทไม่ได้จด VAT — ภาษีซื้อบันทึกเป็นต้นทุน". The
server still overrides regardless — FE is UX only.

**Data decision — the already-posted VI-TEST-0001 (recoverable VAT 70, non-VAT Repttown,
BU TEST):** **LEAVE AS-IS, documented.** It is an immutable posted JE (no void); a raw UPDATE
of the ledger/register would corrupt the audit trail and the input-VAT register. It is a
70-baht test artifact in BU TEST that predates the fix. RECOMMENDATION (explicit): do not
data-fix; note it in the release notes as a known pre-fix test doc. If Ham wants it corrected,
the ONLY compliant route is a reversing vendor-invoice/credit through the normal posting path
— out of scope for this WP, raise as a separate task.

**Worked example — ฿1,000 line, 7% VAT, non-VAT company after fix:**
- `HasInputVat=false`, line `IsRecoverableVat=false`, `VatAmount=฿70`, header
  `NonRecoverableVatAmount=฿70`, `VatAmount(recoverable)=฿0`, `TotalAmount=฿1,070`.
- GL (unchanged posting code, new inputs): Dr Expense **฿1,070** (Amount+VAT, cost) / Cr AP
  ฿1,070. No Dr Input-VAT(1155). Contrast pre-fix: Dr Expense ฿1,000 / Dr Input-VAT ฿70 /
  Cr AP ฿1,070 (the wrong recoverable claim).

**Test plan:** integration test `VendorInvoice_NonVatCompany_ForcesNonRecoverable` — seed a
company with `VatRegistered=false` + a VAT-registered vendor; create VI with a line
`vatRate=0.07`, `recoverable=true`, even `req.HasInputVat=true`; assert persisted header
`HasInputVat=false`, line `IsRecoverableVat=false`, `NonRecoverableVatAmount=70`; post and
assert GL has NO input-VAT (1155) debit and Expense debit = Amount+VAT. Mirror test on a
`VatRegistered=true` company asserting recoverable still works (regression). Pin doc date to
today (teas_test relative-date footgun).

---

## WP1.3 — PO-linked VI line vatRate derivation (F14). FE-only, scoped.

**Today:** `vendor-invoices/new/page.tsx:80` derives
`vatRate: l.lineAmount > 0 ? Math.round((l.taxAmount / l.lineAmount) * 100) / 100 : 0.07`.
When the PO line carries no VAT (`taxAmount=0`, e.g. a non-VAT-co PO) this yields **0**; on a
VAT-co PO (co2) `taxAmount` is present → 0.07 (verified correct). So the "defaults 0" only
bites when the PO itself had no tax — but on a VAT-registered company+vendor that is the
under-claim risk F14 flags.

**Design (scoped — do NOT blanket 0.07):** derive with a fallback that uses the company
standard rate ONLY when both company and vendor are VAT-registered:
```
const stdRate = company.data?.vatRate ?? 0.07;               // company standard VAT
const vendorVat = !!vendor?.vatRegistered;
const companyVat = !!company.data?.vatRegistered;
vatRate:
  l.taxRate != null ? l.taxRate                              // prefer PO line's own rate if DTO carries it
  : l.lineAmount > 0 && l.taxAmount > 0 ? round2(l.taxAmount / l.lineAmount)
  : (companyVat && vendorVat ? stdRate : 0)
```
Prefer `l.taxRate` directly if the PO-line DTO exposes it (avoids the reverse-derivation
rounding at `:80`); the implementer must check `PurchaseOrderDetail.lines` type in
`lib/types.ts` — if `taxRate` exists, use it and drop the amount-division entirely. Keep the
existing `businessUnitId` pull (`:75`). This lives in the `useEffect` at `:73-83`.

**Worked example:** co2 PO line ฿3,000 with `taxAmount=210` → 0.07 (unchanged). Non-VAT-co PO
line ฿3,000, `taxAmount=0`, company non-VAT → 0 (correct, unchanged). VAT-co PO created with a
0% line but company+vendor VAT-registered → now defaults to `stdRate` 0.07 instead of 0 (the
fix), user can still override to 0 for a genuinely exempt line.

**Test plan:** FE component/unit test on the derivation helper (extract the ternary into a pure
`derivePoLineVatRate(line, companyVat, vendorVat, stdRate)` and unit-test the four branches).
No backend change.

---

## WP1.4 — vendor VAT-registered requires 13-digit tax id (F13). Server rule + FE mirror.

**Confirmed gap:** vendor validators validate taxId FORMAT only when present
(`VendorDtos.cs:44-45` create, `:67-68` update: `Must(t => IsNullOrEmpty(t) ||
ThaiTaxId.TryParse(t, out _))`) and the only VAT cross-field rules are `HasThaiVatDReg ⇒
IsForeign` and `IsForeign ⇒ VatRegistered` (`:50-53`, `:70-73`). **No `VatRegistered ⇒ TaxId`
rule** anywhere (validator, service, or DB CHECK `VendorConfiguration.cs:46-53`). Column is
`char(13)` (`VendorConfiguration.cs:22`). Foreign vendors are force `VatRegistered=true`
(`MasterDataServices.cs:70,:91`).

**Server rule (source of truth) — `Accounting.Application/Master/VendorDtos.cs`:** add to BOTH
`CreateVendorValidator` and `UpdateVendorValidator`:
```csharp
RuleFor(x => x.TaxId)
  .NotEmpty().WithMessage("vendor.vat_registered_requires_taxid")
  .Must(t => ThaiTaxId.TryParse(t, out _)).WithMessage("Invalid Thai Tax ID (13 digits + checksum).")
  .When(x => x.VatRegistered && !x.IsForeign);   // domestic VAT-registered only
```
Scope to `!IsForeign`: a foreign (force-VAT-registered) vendor has no Thai 13-digit id — its
regime is the ม.83/6 / ภ.พ.36 path, not a Thai taxId. Use a stable error CODE
(`vendor.vat_registered_requires_taxid`) so WP2.4 maps it to Thai.

**Grandfathering (D-spec "existing rows grandfathered with warning on VI create"):**
- The UPDATE path must not brick a legacy vendor whose stored taxId is empty/invalid while the
  user edits an UNRELATED field. Mirror the FE's existing "unchanged stored taxId" escape
  (`VendorForm.tsx:54`): in `UpdateVendorValidator`, only enforce the new rule when the vendor
  is (or becomes) `VatRegistered && !IsForeign` AND (`TaxId` changed OR was previously empty).
  Simplest defensible rule: enforce NotEmpty+valid whenever `VatRegistered && !IsForeign` on
  BOTH paths, and accept that saving a legacy vat-registered vendor now requires filling the
  taxId — flag this to Ham as the one behavioural sharp edge (it's correct for compliance).
  If Ham wants softer: gate the NotEmpty on `edit == null || taxIdChanged`.
- **VI-create warning (non-blocking):** in `vendor-invoices/new/page.tsx`, when the selected
  `vendor.vatRegistered && !vendor.isForeign && !vendor.taxId`, show a warning banner
  "ผู้ขายจด VAT แต่ไม่มีเลขผู้เสียภาษี — จำเป็นสำหรับการเครมภาษีซื้อ (ภ.พ.30)" near the vendor
  section (reuse the `:214-224` info-line block). Does not block save (legacy data still
  works); nudges the user to fix the vendor master.

**FE mirror — `VendorForm.tsx`:** extend the Zod `superRefine` (`:51-58`): when
`v.vatRegistered && !v.isForeign` and `!v.taxId?.trim()`, add issue on `['taxId']` message
`'taxIdRequiredForVat'`. Keep the existing unchanged-taxId grandfather for edits. Make the
field visually required (asterisk) when `vatRegistered && !foreign` (watch both). The
`TaxIdInput` + `isValidThaiTaxId` (mod-11) already exist and are wired at `:208-210` — no new
component.

**Worked example:** create vendor, `vatRegistered=true`, `isForeign=false`, taxId empty →
FE Zod error + server 422 `vendor.vat_registered_requires_taxid`. Same vendor with taxId
`0105561000000` (valid mod-11) → saves. Foreign vendor, VAT-registered, no Thai taxId →
allowed (rule skipped). Legacy vendor with bad seed taxId, user edits phone only → allowed if
softer rule chosen; else prompted to fix taxId (flag to Ham).

**Test plan:** validator unit tests (4 cases: domestic-vat-no-taxid → fail; domestic-vat-valid
→ pass; foreign-vat-no-taxid → pass; non-vat-no-taxid → pass). FE: VendorForm renders taxId
error when vatRegistered+empty.

---

## WP1.5 — expense categories with no default GL account (F20). Backend seed + backfill + FE.

**Confirmed root cause:** `sys.expense_categories.default_expense_account_id` is nullable
(`ExpenseCategory.cs:19`, `long?`, no FK/IsRequired). `CompanyService.CreateAsync`
(`MasterDataServices.cs:186`) seeds branch/profile/WHT/CoA/tax/RBAC but **NO expense
categories**; the API create path (`ExpenseCategoryService.CreateAsync`, `:467`) + validator
(`ReferenceDtos.cs:36-43`) require only code+name, so a category (COGS) is savable with a NULL
default. The SQL seeds `150`/`430` populate it but hardcode `company_id=1` and are demo-gated.
Repttown (company 2, onboarded via CreateAsync) got COGS hand-created with NULL default → VI
save throws `vi.expense_account_missing` at **`VendorInvoiceService.cs:189`** ("Line 1: no
expense account (category 'COGS' has no default)."). Sibling throws:
`PaymentVoucherService.cs:229` (`pv.expense_account_missing`), `ExpenseClaimService.cs:85`. The
list DTO `ExpenseCategoryDto` (`ReferenceDtos.cs:33`) ALREADY exposes `DefaultExpenseAccountId`
+ `IsCogs`; the FE selector just ignores them.

**Three parts (all needed for Ham's accept: "selectable+savable OR visibly marked unusable"):**

**1.5c — FE guard (smallest, highest-value; do FIRST).**
`components/ui/ExpenseCategorySelector.tsx`: map `defaultExpenseAccountId` in `pick()`
(`:11-31`) into `ExpenseCategoryLite` (add the field to its type in `lib/types.ts`). In the
`<option>` render (`:79-83`): when `defaultExpenseAccountId == null`, either `disabled` the
option OR append a badge "ยังไม่ผูกบัญชี" and block selection, plus a helper link to
`/settings/expense-categories` (or wherever the category edit lives) to set it. Result: COGS
can't be picked → no 422 mid-form. This alone satisfies the accept criterion's "visibly marked
unusable before save."

**1.5b — idempotent PROD backfill (existing tenants). MUST follow the 611 RLS pattern.**
New file `Migrations/SqlScripts/623_backfill_expense_category_accounts.sql` (NOT on the
DemoScripts allowlist — must run on prod). Runtime security context: executes in
`DbInitializer.ApplyScriptsAsync` (`DbInitializer.cs:120`) at API startup, in one transaction
(`:153-158`), as the **prod app role (NOBYPASSRLS), with NO `app.company_id` GUC set**. A bare
`UPDATE … FROM chart_of_accounts` therefore reads/writes NOTHING through RLS (silent no-op) —
so pin the GUC per company. Skeleton (curly-brace-free per the `611` comment; `INSERT/UPDATE`
inside a per-company `set_config`):
```sql
-- 623: backfill sys.expense_categories.default_expense_account_id where NULL.
-- RLS: sys.expense_categories + master.chart_of_accounts are FORCE ROW LEVEL SECURITY
-- (010_rls_policies.sql). At startup no app.company_id GUC exists → must loop per company
-- (mirror 611). master.companies has no RLS so the id list is unfiltered. Idempotent.
DO $do$
DECLARE c RECORD;
BEGIN
  FOR c IN SELECT company_id FROM master.companies LOOP
    PERFORM set_config('app.company_id', c.company_id::text, true);
    UPDATE sys.expense_categories ec
       SET default_expense_account_id = (
         SELECT coa.account_id FROM master.chart_of_accounts coa
          WHERE coa.company_id = c.company_id AND coa.is_header = FALSE
            AND coa.account_code = COALESCE(
              -- IsCogs: prefer a real COGS account IF this CoA has one, else fall to generic 5200
              CASE WHEN ec.is_cogs THEN (
                SELECT x.account_code FROM master.chart_of_accounts x
                 WHERE x.company_id = c.company_id AND x.is_header = FALSE
                   AND x.account_code IN ('51010','5000','5110')
                 ORDER BY x.account_code LIMIT 1) END,
              '5200')                                    -- 5200 exists in every DefaultChartOfAccounts
          LIMIT 1)
     WHERE ec.company_id = c.company_id
       AND ec.default_expense_account_id IS NULL;
  END LOOP;
  PERFORM set_config('app.company_id', '', true);
END
$do$;
```
**CoA reality (verified `MasterDataServices.cs:341-372`):** the default CoA that
`CompanyService.CreateAsync` seeds contains expense accounts **`5100`(เช่า) `5200`(บริการ)
`5300`(โฆษณา) `5350`(ภาษีซื้อขอคืนไม่ได้) `5400`(เงินเดือน)…** and has **NO `51010` and NO
dedicated COGS account**. `51010` only exists in the demo-only `430` seed (company 1). CoA PK
is `account_id` (`MasterDataServices.cs:178`). So the mapping above uses **`5200`
(Service Expense) as the universal fallback** (present in every CreateAsync company) and only
uses a COGS account when one actually exists (demo co1 / custom CoAs).
- **Limitation to surface, not hide (money):** on a standard-CoA company, an `is_cogs` category
  (e.g. Repttown's COGS) will map to `5200` "ค่าบริการ", NOT a true cost-of-sales account,
  because none exists. This is a safe, overridable *default* (the accountant re-maps the
  category, or the VI line sets `expenseAccountId` explicitly) — it does NOT lock GL
  classification and never touches posting math. Flag to Ham: if a real COGS account is wanted
  by default, add e.g. `("5000","ต้นทุนขาย","Cost of Goods Sold",Expense,Debit)` to
  `DefaultChartOfAccounts` (that is the proper home) and point the mapping's first choice at it.
- A tenant with a CUSTOM CoA lacking `5200` → subquery empty → row stays NULL (safe no-op),
  caught by the FE guard (1.5c) + the deploy probe.
- **Deploy probe (prove it worked by ROW COUNTS, not exit code):** after deploy, run per
  company (as a superadmin session or with the GUC set):
  `SELECT company_id, count(*) FILTER (WHERE default_expense_account_id IS NULL) AS still_null,
   count(*) AS total FROM sys.expense_categories GROUP BY company_id;`
  Expect `still_null=0` for standard-CoA tenants; any residual > 0 = custom-CoA tenant needing
  a manual mapping (the FE guard prevents a 422 in the meantime). Record counts in the deploy
  log. **DB backup mandatory before deploy** (new startup SqlScript on prod — memory
  "TEAS prod deploy via plink").

**1.5a — fix-forward (new companies never hit F20). Backend, PROPOSAL — Ham to confirm scope.**
Add expense-category seeding to `CompanyService.CreateAsync` (`MasterDataServices.cs:186`),
mirroring the `430` 19-code set, resolving `DefaultExpenseAccountId` from the CoA seeded moments
earlier in the same method (`DefaultChartOfAccounts`, `:280`). This is the same remediation
class as the previously-fixed empty-CoA / missing-branch CreateAsync gaps (memory). **Flag to
Ham:** auto-seeding 19 opinionated categories into every new company is a product decision; the
alternative is to leave categories user-driven and rely on 1.5b+1.5c. Recommend seeding (a
fresh company otherwise has an EMPTY category dropdown, a worse UX than F20). If confirmed,
extract a `DefaultExpenseCategories(companyId, coaLookup)` helper next to
`DefaultChartOfAccounts` so the SAME set feeds CreateAsync and could retire the `430` demo
divergence later.

**Test plan:** (a) integration `CreateCompany_SeedsExpenseCategoriesWithAccounts` — new company
via CreateAsync → assert ≥1 category and none with NULL default (only if 1.5a taken). (b)
Backfill test is BLIND on teas_test (superuser bypasses RLS) — so ALSO add a `SET ROLE teas` /
NOBYPASSRLS repro test (memory "RLS masked by superuser tests") asserting the per-company loop
actually updates rows under RLS; a plain superuser test would false-pass. (c) FE:
ExpenseCategorySelector disables a NULL-default option (component test with a mocked category
list).

---

## WP2.1 — human session lifetime / refresh (F16). PROPOSAL — Ham to confirm strategy.

**Current state:** human login is a single httpOnly-cookie JWT, NO refresh token.
`Jwt:AccessTokenMinutes` = **15 (prod, `appsettings.json:60`) / 60 (dev,
`appsettings.Development.json:17-22`)**; issued in `JwtTokenIssuer.cs:37-38` (`exp = now +
AccessTokenMinutes`), returned as `access_token`+`expires_at` by `AuthEndpoints.cs:14-27`; the
BFF login route sets the cookie with `expires = expires_at` (`app/api/auth/login/route.ts:63-70`).
`Jwt:RefreshTokenDays:7` is **vestigial/unused** (no code reads it). A full OAuth refresh
stack exists but ONLY for MCP agents (`Program.cs:170-173`, reference refresh + family
revocation) — not reachable by the human cookie flow. `switch-company` re-issues a fresh token
via the SAME shape (`AuthEndpoints.cs:55-60`) — a ready model for silent re-issue.

**RECOMMENDED design (Option A — sliding re-issue; smallest safe change):** add
`POST /auth/refresh` on the backend that, given a still-VALID (unexpired) access token,
re-issues a fresh one (`JwtTokenIssuer.Issue`, same claims/company/branch) — essentially
`switch-company` to the current company. Front it with a BFF route `app/api/auth/refresh/route.ts`
that forwards the cookie and re-sets the cookie from the new `expires_at`. FE: a small
`useSessionKeepAlive` hook in the dashboard shell that (i) calls refresh on a timer at ~60% of
TTL while the tab is active/visible, and (ii) refreshes on user activity (throttled). Because
re-issue requires a still-valid token, an EXPIRED session cannot be resurrected → still forces
re-login (correct); sliding only extends an ACTIVE session.
- **Security notes (mandatory):** (1) cookie stays `httpOnly + secure(prod) + sameSite=lax`
  (unchanged, good). (2) Add an ABSOLUTE session cap — stamp an `auth_time`/`sid` claim at
  login and refuse refresh past e.g. 8–12h, forcing full re-auth (prevents infinite sliding of
  a stolen cookie). (3) Idle timeout — stop the FE keep-alive after N min of no activity so an
  abandoned tab expires. (4) Refresh must re-check the user is still active/not-locked
  (`LoginService`-style user validation) — do NOT blindly re-sign. (5) Never widen the token's
  scope on refresh.

**Alternative (Option B — proper refresh token):** issue a second httpOnly `refresh_token`
cookie (long-lived, rotating, reference-token + family revocation — reuse the OAuth infra),
`POST /auth/refresh` exchanges it; access token drops to ~5–15 min. More secure + revocable,
more code + a token store. Recommend as the hardening FOLLOW-UP, not this release.

**Flag to Ham:** pick A (this release) vs B (defer). Also decide prod `AccessTokenMinutes`
(15 is aggressive for a data-entry app; with sliding, 15–20 + keep-alive is fine; without any
WP2.1, bump to ~30 as an interim). WP2.2 (below) is required REGARDLESS of A/B — it's the
safety net when refresh can't save the session.

**Test plan:** backend test `Refresh_WithValidToken_IssuesNewExpiry`;
`Refresh_WithExpiredToken_401`; `Refresh_PastAbsoluteCap_403`. FE: hook fires refresh before
expiry (fake timers), stops when hidden/idle.

---

## WP2.2 — global 401 handler + in-place re-login (F16/F1). FE.

**Current:** proxy returns `401 {title:'auth.unauthenticated'}` when the cookie is missing
(`route.ts:15-20`); an expired-but-present token passes through as the backend's 401. Middleware
only redirects on NAVIGATION (`/api` is a public path so client fetches are NOT redirected —
`middleware.ts:28,37-45`). So a mid-form save gets a 401 `ApiError` (`api.ts:53-56`) that each
form turns into a transient toast — no re-login flow, form state lost on manual reload (F1's
stale shell is the navigation variant).

**Design — single global interceptor in `lib/api.ts` `request()`:** on `res.status === 401`
AND body `title` starts `auth.` (an AUTH 401, not a 403/permission), dispatch a global
"session expired" event (tiny module-level event emitter or a zustand store — reuse whatever
global store the app already has; check `lib/` for an existing one before adding). A top-level
`SessionExpiredModal` mounted in the dashboard layout listens and opens an **in-place re-login
modal** (username+password → existing `POST /api/auth/login`, which re-sets the cookie). On
success it closes; the user re-clicks Save (or auto-retry the last failed mutation if trivially
captured). Because we NEVER navigate away, all React form state is intact → satisfies "re-login
→ same form still filled." Fall back to redirect-with-returnTo only if the user dismisses.
- Distinguish 401(auth) from 403(permission) — 403 stays a toast, no modal.
- F1 stale-shell: when a document navigation 401s (token gone), middleware already redirects to
  `/login?returnTo=`; the empty-nav-headers artifact is the SPA rendering a cached shell —
  the modal path covers the in-SPA case; for a hard reload the middleware redirect is correct.

**Test plan:** FE integration — mock a 401 on a mutation → assert modal opens, form inputs
retain values; successful modal login → modal closes; a 403 → toast only, no modal.

---

## WP2.3 — hanging duplicate POST / trailing slash (F21). Root cause FOUND.

**Root cause (verified by F21's own evidence + config):** every create mutation posts to a
**trailing-slash** path — `useCreateVendorInvoice` → `apiPost('vendor-invoices/', req)`
(`queries.ts:466`), the PV form → `apiPost('payment-vouchers/', …)`
(`payment-vouchers/new/page.tsx:148`), and ~16 sibling creates (grep `apiPost.*'[a-z-]+/'`).
`next.config.ts` sets NO `trailingSlash` → default **false** = "redirect trailing-slash URLs to
no-slash." So the browser POSTs `/api/proxy/vendor-invoices/` → Next issues a **308** to
`/api/proxy/vendor-invoices` → the browser re-POSTs to the no-slash URL. That is EXACTLY F21's
"double request: both `/api/proxy/vendor-invoices/` AND without" — the no-slash request can
only exist as the 308 target. The "pending forever → buttons dead" is the redirected POST
failing to settle under error/expiry conditions, leaving React Query `create.isPending` stuck
true (VI buttons are `disabled={…|| create.isPending}`, `:166,:174`) → form bricked until reload.

**Fix (three layers; 1 is primary, 2–3 are robustness the reviewer should keep):**
1. **Remove the trailing slash app-wide** in `lib/queries.ts` + the two inline form
   `apiPost('…/')` calls (VI already goes through the hook; PV form `:148`). Change every
   `apiPost<…>('<resource>/', …)` / `apiPut` create path to no trailing slash. Backend ASP.NET
   routing matches both, and the proxy builds `${BACKEND}/<path>` either way — so this is safe
   and eliminates the 308 + the double request entirely. Mechanical (Haiku-able) but VERIFY
   first (step below). It IS app-wide (not VI-only) because the smell is app-wide; fixing only
   VI would leave the same latent bug on every other create.
2. **AbortController timeout in `api.ts` `request()`** — wrap the `fetch` with an
   `AbortSignal.timeout(30_000)` (or an AbortController) so a genuinely stuck request REJECTS
   after N s → the mutation settles → `isPending` clears → buttons re-enable → error toast.
   This is the general guarantee that NO future hang can brick a form, independent of cause.
3. **Proxy hardening** (`app/api/proxy/[...path]/route.ts`): the handler copies only
   `content-type` + `content-disposition` to `respHeaders` (`:50-54`) — it does NOT forward
   `Location`, and uses `redirect:'manual'` (`:39`). If ANY backend endpoint 3xx-redirects, the
   proxy returns a body-less 3xx the browser cannot follow → a hang. Add: if `upstream.status`
   is 3xx, either forward the `Location` header or return a explicit 502/clear error. Low-risk
   defence; documents the latent trap.

**Verify-before-fix (systematic-debugging — do NOT fix blind):** reproduce a VI/PV create with
the Network panel open; confirm (a) `/api/proxy/<res>/` returns **308** and (b) two POST
entries appear. If 308 is confirmed → layer 1 is the fix. If NOT (Next didn't redirect) → the
double request is elsewhere (React 18 strict-mode dev double-invoke, or a caller firing twice);
layers 2+3 still apply and the trailing-slash removal is still correct hygiene. Capture the
trace in the attempt log.

**Test plan:** after removing slashes, assert a create fires exactly ONE network POST (Playwright
`page.on('request')` count, or a unit test of the path string). Assert a mocked never-resolving
fetch rejects within the timeout and re-enables the button.

---

## WP2.4 — domain error toasts to Thai + sticky (F19). FE.

**Current:** `errorToToast`/`problemToast` (`lib/api/errors.ts`, `lib/api.ts:30-44`) surface
`ApiError.message` (= ProblemDetails `detail`, an English domain string) directly; sonner
auto-dismisses. Backend domain errors carry STABLE CODES in ProblemDetails `title` (e.g.
`vi.expense_account_missing`, `pv.expense_account_missing`,
`vendor.vat_registered_requires_taxid`, `auth.unauthenticated`).

**Design (FE-only):** in `errorToToast`, resolve a Thai message by the error CODE first:
`const code = err.code ?? body.title; const th = tErrors.has(code) ? tErrors(code) : null;`
prefer `th`, else fall back to the current `.message`/`detail`. Add a `problems.*` namespace to
the FE i18n dictionary keyed by code (seed the purchase-side codes; the implementer enumerates
the rest by grepping `throw new DomainException("` in `Accounting.Infrastructure/Purchase/*` and
`/Master/*`). For errors, pass sonner `{ duration: 8000 }` (or a manual-dismiss) and render the
EN `detail` as a collapsible/secondary line so the technical string is available but not
primary. Keep success toasts unchanged.

**Test plan:** FE unit — `errorToToast(new ApiError(422,'vi.expense_account_missing',...))`
returns the Thai string; unknown code falls back to `detail`. Manual: trigger the COGS 422
(pre-1.5c) → Thai sticky toast.

---

## D3 PROPOSAL — PO "ปิด" (close) semantics (F29). PROPOSAL — Ham to confirm before WP3.4.

Today the "ปิด" button no-ops (no endpoint/handler). Proposed semantics:
- **New status `Closed`** on PurchaseOrder, distinct from `Cancelled`. `Cancelled` = voided
  before use; `Closed` = intentionally finished (fully received / no more billing expected),
  retains full history.
- **Allowed transition:** `Approved → Closed` (and optionally `PartiallyReceived → Closed`),
  via `POST /purchase-orders/{id}/close`, permission-gated (reuse the PO approve permission or
  a new `po.close`). Not allowed from Draft (cancel instead).
- **Effects:** (1) a Closed PO is EXCLUDED from the VI form's linkable list — the picker uses
  `usePurchaseOrders('Approved', vendorId)` (`vendor-invoices/new/page.tsx:71`); Closed drops
  out automatically. (2) Excluded from "open PO" dashboards/lists. (3) No new VI/PV may link to
  it (server guard on the VI create PO-link path). Already-linked/posted VIs are untouched.
- **Reopen:** allow `Closed → Approved` by the same permission ONLY if no posted downstream doc
  depends on the closed state; else forbid (keep it simple — Ham may say "no reopen").
- FE: `DocActionBar` "ปิด" → confirm dialog (mirror WP3.6) "ปิดใบสั่งซื้อ — จะเชื่อม VI/PV
  เพิ่มไม่ได้"; on success refresh status + activity panel.
Deliverable of D3 confirmation feeds WP3.4 (implement) — until then WP3.4 stays blocked.

## D4 PROPOSAL — SoD text vs enforcement (F25). PROPOSAL — Ham to confirm (drives WP4.9).

Observed: PV page states "ผู้อนุมัติต้องไม่ใช่ผู้สร้าง (SoD)" but creator `ham_chatsang`
approved own PV (admin not blocked). Two options:
- **(A) Align text to actual behavior (spec default, RECOMMENDED for this release):** change the
  copy to "ผู้อนุมัติควรเป็นคนละคนกับผู้สร้าง — ผู้ดูแลระบบ (super-admin) ข้ามได้". No behavior
  change. Zero risk. This is WP4.9's default.
- **(B) Enforce SoD (compliance hardening, if Ham wants it):** in the PV approve service
  (locate `PaymentVoucherService` approve method — NOT read in this pass; implementer/reviewer
  to confirm the exact seam), reject approve when `approverUserId == createdByUserId` UNLESS the
  caller holds a `pv.approve_own` override permission (grant to super-admin only). Same guard
  optionally on VI post and PO approve for consistency. Requires an RBAC permission add + the
  existing seed-ordering care (memory "RBAC seed-ordering footgun": insert the permission code
  BEFORE the grant script, run RbacAuthMapTests).
Recommend (A) now, (B) as a tracked compliance follow-up. Marked PROPOSAL either way.

## Residual follow-ups — WP2 (from Opus Tier-2 security review, not blocking commit)
- [x] F-C (LOW, UX, fails safe): after the WP2.2 session-expired modal re-login, a super-admin who
  had switched company lands back on their DEFAULT company (POST /auth/login re-scopes there),
  so an in-progress form holding company-X ids submits under company Y → RLS/cross-tenant guard
  rejects (404), never mis-posts. Rough edge only. Fix option later: SessionExpiredModal reads
  the WP4.4 last-company localStorage key and re-switches after re-login.
  DONE 2026-07-15 (branch fix/purchase-ux-fc-f5): extracted the login page's existing
  `restoreLastCompany()` (WP4.4/F17 — reads `LAST_COMPANY_KEY`, confirms via `/api/proxy/me`
  the user is still a super-admin with access, then POSTs the existing
  `/api/auth/switch-company` route) out of `login/page.tsx` into a shared export in
  `lib/auth.ts` (no behavior change — same fetch calls, same fail-silent catch). Wired into
  `SessionExpiredModal.tsx`'s `onSubmit` success path: `await restoreLastCompany()` runs right
  after the in-place re-login succeeds and BEFORE `setOpen(false)` closes the modal — so a
  save immediately after re-login goes out under the restored company. Never a hard navigation
  (unlike `CompanySwitcher`'s `switchTo`, which does `window.location.assign('/')` — that would
  defeat WP2.2's whole point of preserving in-place form state), and `restoreLastCompany`'s own
  try/catch already fails silently on any error (network blip, no-longer-allowed company, etc.)
  so a failed restore never traps the user in the modal — they just land on the default company,
  same as before this fix. `login/page.tsx` now imports the shared function instead of defining
  its own copy (net one function, not a duplicate). Files:
  `frontend/lib/auth.ts` (+restoreLastCompany export), `frontend/app/(auth)/login/page.tsx`
  (import instead of local def), `frontend/components/auth/SessionExpiredModal.tsx` (call +
  doc comment). Verified: `tsc --noEmit` clean, `next build` green (0 errors).
- F-D (INFO, acceptable as shipped): proxy 3xx Location pass-through is backend-originated +
  OpenIddict-validated + fetch-only (no httpOnly JWT replay on the followed hop). No open-redirect
  in practice; noted for completeness.
- F-A (HIGH, security) FIXED this round: absolute-cap bypass via switch-company — auth_time now
  carried forward through CompanySwitchService + the cap 403 enforced on the switch path via a
  shared CheckOrThrow helper reused by /refresh (see WP2 fix dispatch).

## Residual follow-ups — WP1 (from Opus Tier-2 review, not blocking WP1 commit)
- [x] F-5 (LOW, hardening): internal callers `CreateFromPurchaseOrderAsync` +
  `CreateVendorInvoiceFromPvAsync` build `CreateVendorInvoiceRequest` in-code and call
  `CreateDraftAsync` directly, bypassing the FluentValidation `InclusiveBetween(0,1)` rate
  bound (only the REST endpoint runs it). Money-safe today (upstream PO/PV rates are already
  validated at their own creation; the non-VAT guard is service-level so it holds regardless),
  but the 700%-type raw-rate defect could in theory enter via a non-REST path. Consider moving
  the rate bound into `BuildLinesAsync` (service-level) in a later hardening pass; verify the
  MCP `create_vendor_invoice_draft`/`create_payment_voucher_draft` tools run the DTO validator.
  DONE 2026-07-15 (branch fix/purchase-ux-fc-f5): moved the bound into
  `VendorInvoiceService.BuildLinesAsync` (`backend/src/Accounting.Infrastructure/Purchase/
  VendorInvoiceService.cs`) — the single seam `CreateDraftAsync`, `UpdateDraftAsync`, AND
  `CreateFromPurchaseOrderAsync` (which delegates to `CreateDraftAsync`) all funnel through.
  Rejects `input.VatRate < 0m || > 1m` with `DomainException("vi.vat_rate_out_of_range", ...)`
  BEFORE the `net`/`vat` computation, naming the line number + offending value. DTO validators
  kept as-is (defence in depth, not removed) — no GL/settlement math touched.
  **PV WHT rate — same in-code-bypass shape confirmed, NOT already safe:**
  `PaymentVoucherService.CreateFromVendorInvoiceAsync` builds a `CreatePaymentVoucherRequest`
  in-code from `CreatePvFromViRequest.WhtRate` — and `CreatePvFromViValidator` (`PaymentVoucherDtos.cs`)
  does NOT bound `WhtRate` at all (only checks cheque fields), so an out-of-range rate reaches
  `CreateDraftAsync` unchecked via the REST "create PV from VI" endpoint, same defect class as
  the VI side. (PV has no separate `BuildLinesAsync` — `CreateDraftAsync`'s own inline line loop
  IS the one seam both the direct-create and from-VI paths share, since PV has no
  `UpdateDraftAsync`.) Added the identical `input.WhtRate < 0m || > 1m` guard
  (`DomainException("pv.wht_rate_out_of_range", ...)`) in that loop, next to the pre-existing
  VatRate checks (ม.82/5 / ม.81 / standard-rate-only — VatRate was already tightly bound there,
  tighter than [0,1], so no change needed on the PV VAT side). DTO validator's own
  `WhtRate InclusiveBetween(0,1)` (direct-create path) kept as-is.
  Tests: new `backend/tests/Accounting.Api.Tests/Hardening/PurchaseRateBoundTests.cs` (4 cases,
  service-layer calls mirroring the internal-caller shape — DTO validator never runs on these
  calls, so they're the only line of defence): VI VatRate=7.0 → `vi.vat_rate_out_of_range`;
  VI VatRate=0.07 → unaffected (VatAmount=70 exactly, unchanged behavior); PV WhtRate=3.0 →
  `pv.wht_rate_out_of_range`; PV WhtRate=0.03 → unaffected (WhtAmount=30 exactly). All 4 passing
  with `TEAS_TEST_PG` set (0 skipped — confirms real DB, not a silent skip). Regression:
  `dotnet build` 0 errors; Hardening + Mcp folders 226 passed/4 pre-existing unrelated skips
  (same baseline as WP1.2's prior runs); `Accounting.Api.Tests.Purchase` folder 39/39 passed.
- F-4 (RESOLVED, Ham 2026-07-14): full enforcement kept — a domestic VAT-registered vendor
  requires a valid 13-digit taxId on every save (no soft/changed-field gate). Legacy vendors
  with empty/bad taxId must fill it before any edit saves.

## Blast-radius cap (this design's WP1+WP2 implementation)
- WP1.1: +1 shared component + 2 call-site swaps + 1 PV-validator bound. ~5 files.
- WP1.2: `VendorInvoiceService.cs` (create+post+fromPO guards) + FE mirror + tests. ~4 files.
  **No `GlPostingService` edits.**
- WP1.3: 1 FE file (VI form derivation) + 1 unit test.
- WP1.4: 2 validators + `VendorForm.tsx` + VI-form warning + tests. ~4 files.
- WP1.5: 1 new SqlScript (623) + FE selector + `lib/types.ts` + (if 1.5a) CreateAsync + tests.
  ~5 files. **DB backup before deploy; deploy probe by row count.**
- WP2.1: `AuthEndpoints`/`JwtTokenIssuer` (+refresh) + BFF refresh route + FE keep-alive hook.
- WP2.2: `api.ts` + a global store/event + `SessionExpiredModal` + layout mount.
- WP2.3: `queries.ts` + PV form (slash removal, app-wide) + `api.ts` timeout + proxy hardening.
- WP2.4: `errors.ts` + i18n dictionary additions.
Hitting >~6 files on any single WP, or any need to touch the ledger/settlement path =
stop-and-re-spec. Public API additions allowed: `POST /auth/refresh`, `POST /purchase-orders/
{id}/close` (D3, gated on Ham). No breaking API changes.

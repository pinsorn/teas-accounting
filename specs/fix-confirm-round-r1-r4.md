# Spec: fix confirm-round findings R1–R4 (prod v1.21.0 BU TEST re-test, 2026-07-15)

Source: PROGRESS-purchase-uxtest.md §CONFIRM ROUND. All four confirmed on prod
teas.kazaki-rio.com v1.21.0. Expected FE-only. Ham approved fixing all 4.

Worker rules: Ponytail (minimal diff), grep troubles-wiki.md FIRST on any weird error,
NO `git commit`. Local stack for repro: backend :5080 (Dev) + `npm run dev` :3000,
login admin / Demo Company (see docs/superpowers/plans/2026-06-14-manual-build-all-modules-INSTRUCTIONS.md).
Footguns: overnight `next dev` serves stale chunks — restart :3000 before believing
"fix didn't work". Bengali ম glyph guard before finishing.

## R1 — PO list "หน่วยธุรกิจ" column shows raw "#id"
- [x] Evidence: prod /purchase-orders renders "#3"/"#1" while `/api/proxy/business-units
      ?includeInactive=true` returns id 3 = "TEST — Repttown Test" AND /vendor-invoices list
      resolves the same BU to full name. So the shared hook works; the PO LIST page alone
      doesn't use it (or passes the wrong field).
- [x] REAL root cause (code was already byte-identical to VI list — "use the same pattern"
      was a no-op): `columns` is `useMemo(() => [...], [t, tc])` — `buName` (from
      `useBusinessUnitName()`) is a NEW closure every render but isn't in the dep array, so
      whatever `businessUnitData` looked like at the FIRST render (often `[]`, before the
      business-units fetch resolves) is frozen forever — permanently rendering "#id" for the
      life of that mount, regardless of how long the user waits. Reproduced locally: hard
      nav straight to /purchase-orders as the first page after login → row's BU column stuck
      on "#7" even 2s+ after the business-units network call succeeded. This is latent in
      ALL 9 pages using this pattern (delivery-orders, invoices, payment-vouchers,
      quotations, receipts, sales-orders, tax-invoices, vendor-invoices, purchase-orders) —
      it only manifests on whichever page happens to mount before the business-units query
      is warm elsewhere in the session (explains why VI "worked" for the QA tester and PO
      didn't — navigation-order dependent, not a VI-vs-PO code difference). Fixed PO only,
      per spec scope; flagging the other 8 for Fable to decide.
- [x] Fix `app/(dashboard)/purchase-orders/page.tsx`: added `const { data: businessUnits } =
      useBusinessUnits(true);` and added `businessUnits` to the `columns` memo's deps
      (`[t, tc, businessUnits]`) so the memo recomputes once the query resolves.
- [x] Accept: local dev PO list shows "CODE — nameTh" for every row; no other column changes.
      Evidence: edited PO #9 (docDate 2026-06-22) to set BU=REPT via the real edit form,
      then hard-navigated a FRESH tab to /purchase-orders — row shows
      "REPT — หน่วยธุรกิจ REPT" (not "#7"). No other columns touched.

## R2 — PO draft EDIT-save silently resets docDate to today (DATA BUG, highest priority)
- [x] Repro evidence (prod): PO id 1 docDate 2026-07-12; edited ONLY business unit via
      /purchase-orders/1/edit; edit form DISPLAYED 07/12/2026 correctly; after save,
      server docDate = 2026-07-15 (GET verified). So display state ≠ submitted payload.
- [x] systematic-debugging DONE — root cause is BACKEND, not frontend, and it's DELIBERATE
      (not an accidental overwrite):
      `backend/src/Accounting.Infrastructure/Purchase/PurchaseOrderService.cs:104-105`,
      `UpdateDraftAsync`:
      ```
      // §10 — re-pin to today on edit too (never trust req.DocDate); else the rule is half-applied.
      po.DocDate = clock.TodayInBangkok(); po.ExpectedDeliveryDate = req.ExpectedDeliveryDate;
      ```
      This traces to an EARLIER, intentional workstream (claude-mem 9480 "Purchase Order
      DocDate: Pin to Server Today (§10)", then 14760 "All D2 Draft Update Services Re-Pin
      DocDate Server-Side, Ignoring Client Input") — the backend explicitly discards
      `req.DocDate` on every Draft edit and re-pins to server-today, same as at CREATE.
      `ExpectedDeliveryDate` is NOT affected (line 105 uses `req.ExpectedDeliveryDate`
      verbatim) — so this is scoped to DocDate only, not "every header field" as the spec
      suspected. There is no frontend bug here: the edit FORM correctly displays and would
      correctly SUBMIT the original docDate; the server unconditionally overwrites it after
      receiving the request.
      Local repro (Demo Company, PO id 9, docDate 2026-06-22): edited ONLY the business unit
      via the real edit form (docDate field untouched, displayed 06/22/2026 throughout) →
      saved → `GET /api/proxy/purchase-orders/9` → `docDate: "2026-07-15"` (today),
      `businessUnitId: 7`. Confirms the exact prod symptom locally, and confirms it's driven
      entirely server-side.
- [x] STOPPED (backend fix) per spec instruction — root cause is BACKEND (deliberate §10 rule
      extended to UpdateDraftAsync). This is a release-scope decision for Fable/Ham: is
      "re-pin docDate on every draft edit, indefinitely" the intended behavior (matches
      CREATE's rule), or should an edit preserve the existing docDate unless explicitly
      changed? **Backend decision made by the coordinator: do NOT touch the §10 rule.**
- [x] FE honest-UI fix (follow-up dispatch, same day) — since the backend WILL silently
      discard docDate on both create and edit, the FE bug was showing an EDITABLE field the
      server ignores. Fixed `components/forms/PurchaseOrderForm.tsx` (used by both
      create and `[id]/edit`):
      - Replaced the plain editable `<input type="date">` docDate field with the shared
        `<DateInput value={docDate} locked label={t('docDate')} />` component
        (`components/ui/DateInput.tsx`) — same locked-date pattern already used by the VI,
        PV, receipts, and tax-invoice forms (disabled input + hint
        "ล็อกเป็นวันนี้ (Asia/Bangkok) · = dd/MM/2569" under the field, i.e. point 3's
        BE-year hint is preserved for free since `DateInput` already renders it). No new
        i18n key needed — reused the existing `t('docDate')` label and DateInput's own
        (already-generic, non-namespaced) hint text.
      - `docDate` changed from `useState(edit?.docDate ?? today)` to a plain
        `const docDate = today;` — always `bangkokToday()`, in BOTH create and edit mode, so
        the field (and the paper-preview `issueDate`) show what the server WILL actually
        save, never the stale stored value on an existing draft. Removed the now-dead
        `setDocDate` calls (the onChange handler and the edit-rehydrate effect's
        `setDocDate(edit.docDate)`).
      - `expectedDeliveryDate` untouched — stays a normal editable input (backend honors it
        verbatim, confirmed at PurchaseOrderService.cs:105).
      - Payload shape UNCHANGED — `submit()` still sends `docDate` in the POST/PUT body
        (now always `today`, harmless since the server ignores it either way per point 4).
      - Verified live on local dev: `/purchase-orders/new` → docDate field greyed-out,
        shows today (2026-07-15) with the lock hint. `/purchase-orders/8/edit` (PO #8, whose
        STORED docDate is 2026-06-22, unmodified) → docDate field still shows locked
        **07/15/2026** (today), not the stale 06/22/2026 — confirms point 2 (edit mode shows
        today, not the stored value).
- [x] Accept: FE now tells the truth about docDate in both create and edit. Gates: `npx tsc
      --noEmit` 0 errors; `next build` — `✓ Compiled successfully in 10.2s`, 84/84 routes,
      exit 0; Bengali `ম` grep on `PurchaseOrderForm.tsx` — clean, no matches.

### R2 FINAL DECISION (Ham, 2026-07-15 evening): **Option B — preserve docDate on draft edit**
- Backend: `PurchaseOrderService.UpdateDraftAsync` stops re-pinning `po.DocDate` on edit —
  the existing (create-time, server-pinned) DocDate is PRESERVED. `req.DocDate` stays
  IGNORED (never trust client dates still holds — the date is always server-stamped, just
  stamped ONCE at create). CreateDraftAsync unchanged (still pins today). Update the §10
  comment to record the amended rule + this decision.
- Scope: **PO only** (the case Ham saw and decided on). Other D2 draft-update services keep
  their re-pin — flagged as a possible follow-up batch, NOT changed here.
- Tests: find any test asserting re-pin-on-edit for PO and update it to assert PRESERVE
  (deliberate behavior change); add/adjust an integration test that seeds a draft with a
  non-today DocDate directly, calls UpdateDraftAsync, asserts DocDate unchanged.
- FE mirror (`PurchaseOrderForm.tsx`): edit mode shows the doc's STORED docDate (still
  locked/read-only — client still can't set it); create mode keeps locked-today. Hint text
  must not lie in edit mode ("ล็อกเป็นวันนี้" is wrong there — use a "ล็อกตามวันที่สร้าง"-style
  variant).
- [ ] Implement + gates (backend purchase folder tests, tsc, next build, glyph grep)

## R3 — po.reopen_blocked toast English-only
- [x] Add Thai entries to `lib/i18n/problems.ts`:
      - `po.reopen_blocked`: "เปิดใบสั่งซื้อใหม่ไม่ได้ — มีใบกำกับภาษีซื้อที่บันทึก (Post) แล้วเชื่อมกับใบสั่งซื้อนี้"
      - `po.not_approved`: "เชื่อมใบสั่งซื้อไม่ได้ — ใบสั่งซื้อต้องอยู่ในสถานะอนุมัติแล้ว" (replaced the
        prior, more-generic WP2.4 wording — NOTE: `po.not_approved` is also thrown by the
        "close PO" and "mark PO sent" guards, same code/different call-sites; a single
        code→message dict can't branch by call-site without a backend change (out of scope
        for this FE-only fix) — flagged in a code comment for Fable.)
- [x] Grepped backend Purchase* services (Accounting.Infrastructure/Purchase/*,
      Accounting.Domain/Entities/Purchase/*) for every `DomainException("...")` code; also
      added the ones missing beyond the two named above (WP3.4+ additions + a few pre-existing
      gaps): `po.terminal`, `po.not_closed`, `vi.vat_rate_out_of_range`, `vi.no_docno`,
      `vi.invalid_amount`, `pv.wht_rate_out_of_range`, `pv.not_approved`, `pv.no_docno`,
      `pv.invalid_amount`.
- [x] Accept: extended `lib/api/errors.test.ts` with 2 new cases (po.reopen_blocked,
      po.not_approved) following the existing pattern — `npx vitest run lib` → 6/6 pass in
      that file (was 4/4).

## R4 — PV-from-VI prefill under-settles when re-derived VAT ≠ VI's VAT (money-adjacent)
- [x] Evidence: VI 07-2026-VI-TEST-0002 outstanding 214 (base 200 + 14 NON-recoverable VAT,
      vendor NOT VAT-registered) → prefill seeded base 200 → PV grand total 200 ≠ 214.
      Current WP3.5 logic scales VI subtotal by outstanding ratio and assumes the form's
      re-derived VAT restores the total — false when the form derives 0% for a non-VAT vendor.
- [x] Ham decision followed: prefill lands PV grand total EXACTLY = VI outstanding in every
      vendor/VAT combo tested; user can still edit afterward.
- [x] Design implemented: `frontend/lib/pv-prefill.ts` — pure `derivePvPrefillBase(outstanding,
      rate)`: works in integer satang, `rate<=0` → identity (always exact — the non-VAT-vendor
      case this fix targets); nonzero rate → `round(outCents/(1+rate))/100`. Unit tests
      (`lib/pv-prefill.test.ts`, 6 cases): rate 0 → base=outstanding; outstanding 214, rate 0
      → 214 (exact evidence repro); rate 0.07, outstanding 1070 → base 1000; non-round
      outstanding 107.51, rate 0.07 → base 100.48 (satang-exact, asserted via the actual
      re-derivation formula); PLUS two exhaustive property tests over every cents value
      1..20000 — rate 0 always exact (trivial), rate 0.07 always exact-or-≤1-satang-off
      (documented: VAT's satang rounding makes a ~1-in-15 target genuinely UNREACHABLE at ANY
      base when the re-derived rate differs from the VI's own — a real quantization limit, not
      a code gap; tried a nudge-loop rescue first, empirically proved it rescues ZERO
      additional reachable cases, so simplified it back out — Ponytail).
      Wired into `app/(dashboard)/payment-vouchers/new/page.tsx`'s VI-prefill effect: moved
      `useVendor(vendorId ?? 0)` earlier so the effect can read it; the effect now waits for
      `vendor` to resolve AND match `vi.vendorId` (vendorId is set in the SAME effect pass, so
      `vendor` lags a render) before computing `rate = vendor.vatRegistered ?
      taxRateForProductType(productType) : 0` and `derivePvPrefillBase(outstanding, rate)`;
      also now carries the VI's first line `productType` onto the prefilled row (previously
      defaulted to 'GOOD' regardless of the VI's real product — would have silently
      mismatched the rate used to derive vs. the rate the rendered row re-derives, for an
      exempt-product VI).
- [x] Accept — verified live on local dev (backend :5080 Dev + frontend :3000, Demo Company):
      - Created vendor "R4 ไม่จด VAT" (vatRegistered=false) + posted VI 07-2026-VI-0001
        (base 200, vatRate 0.07 → total 214) reproducing the exact prod evidence shape.
        `/payment-vouchers/new?fromVendorInvoiceId=<id>` → prefilled line amount **214**, VAT
        shown "0% · ผู้ขายไม่จด VAT", **Grand Total ฿214.00** (was 200 before the fix).
      - Regression: existing posted VI 06-2026-VI-0007 (VAT-registered vendor, base 1000,
        vat 70, total 1070) → prefilled line amount **1000**, VAT "7%", **Grand Total
        ฿1,070.00** — unchanged from expected.
- [x] NOTE: money-adjacent — flagged for Fable's personal diff review before commit. Payload
      contract UNCHANGED — `saveDraft()`'s POST body still sends `amount` (pre-VAT base) and
      `vatRate` exactly as before; only the prefilled `amount` VALUE and the row's `productType`
      changed, not the request shape.

## Gates (run all, report evidence verbatim)
- [x] `npx tsc --noEmit` (frontend) — 0 errors (no output)
- [x] `npm run build` (frontend, next build) — green (`✓ Compiled successfully in 8.6s`,
      all 84 routes generated, no errors)
- [x] `npx vitest run lib` — 11 files / 55 tests, all green (incl. new `pv-prefill.test.ts`
      6/6 and the 2 new cases in `errors.test.ts`)
- [x] Bengali glyph grep (`ม`, Bengali U+09AE) on every changed file — clean, no matches
- [x] Live repro evidence for R1/R2/R4 on local dev — see each section above (R1: fresh-tab
      hard nav after live-editing a PO's BU; R2: real edit-form save + GET; R4: created a
      non-VAT vendor + posted VI reproducing the exact prod ratio, checked prefill in browser,
      plus a regression check on an existing VAT-vendor VI)

Blast-radius cap: FE only, ≤8 files (source+tests+i18n). Hitting the cap or a backend root
cause = stop-and-report, no fix.
**Files touched (7, under the 8-file cap):**
`app/(dashboard)/purchase-orders/page.tsx`, `app/(dashboard)/payment-vouchers/new/page.tsx`,
`components/forms/PurchaseOrderForm.tsx`, `lib/i18n/problems.ts`, `lib/api/errors.test.ts`,
`lib/pv-prefill.ts` (new), `lib/pv-prefill.test.ts` (new).
**R2: backend root cause confirmed and coordinator decided NOT to change it → FE honest-UI
fix shipped instead (locked docDate field, see R2 section) — no backend fix applied.**

## Attempt log

### 2026-07-15 — worker session (R1, R3, R4 fixed; R2 stopped per spec)
- Local repro stack: backend :5080 (Dev, killed an overnight-stale `next dev` on :3000 per
  the footgun note and restarted both fresh).
- R1: code was already byte-identical to the VI list pattern (no diff to "match" against) —
  had to actually debug why. Found + fixed a `useMemo` stale-closure bug (see above). Verified
  live in a fresh tab.
- R2: systematic-debugging led straight to backend `PurchaseOrderService.UpdateDraftAsync`
  (`§10` comment, deliberate). Confirmed via local edit-form repro + GET. Per spec's own stop
  condition, did NOT touch backend code — reporting to Fable for a scope decision.
- R3: added the 2 spec-given codes + grepped Purchase* services for the rest, added 9 more
  missing codes total. Extended errors.test.ts with the 2 spec-named cases.
- R4: extracted `derivePvPrefillBase` pure helper, found (via an exhaustive test) that a
  first-draft nudge-loop rescue added zero value — simplified back out per Ponytail. Wired
  into the VI-prefill effect with a vendor-data-race guard (vendorId set same-pass as vendor
  fetch depends on it) and carried `productType` onto the row so the derived rate used for
  seeding matches the rate the rendered row will re-derive. Verified both the bug scenario
  and the regression scenario live via browser + direct API test-data creation (new vendor
  id 51 "R4TEST", VI id 29 → posted as 07-2026-VI-0001; local dev DB only).
- All gates green. Left changes uncommitted in the working tree per instructions.

### 2026-07-15 — follow-up dispatch (R2 FE honest-UI fix, backend decision: keep §10 as-is)
- Coordinator decided NOT to touch the backend §10 rule; asked for an FE fix so the PO form
  doesn't show an editable docDate the server silently ignores, mirroring the VI/PV/receipts/
  tax-invoice forms' existing shared `DateInput` locked-date component.
- Fixed `components/forms/PurchaseOrderForm.tsx` (shared by create + edit): swapped the plain
  editable docDate `<input>` for `<DateInput value={docDate} locked label={t('docDate')} />`;
  `docDate` is now `const docDate = today` (was `useState`) so both create AND edit always
  show/send today, never the stale stored value or a user edit. `expectedDeliveryDate` and the
  PUT/POST payload shape are unchanged.
- Verified live: `/purchase-orders/new` shows locked today; `/purchase-orders/8/edit` (PO #8,
  stored docDate 2026-06-22, never re-saved) shows locked **07/15/2026**, not the stale date.
- Gates: `npx tsc --noEmit` 0 errors; `next build` compiled successfully, 84/84 routes;
  Bengali grep on the changed file clean. Left uncommitted per instructions.

# WP-B — PO → Vendor Invoice: VAT rate + product type from the PO, not from a stale closure (GPT-5.6 review HIGH-02, frontend half)

Board: `PLAN-gpt56-review-2026-09-04.md` §2 row B. Blast cap: **7 files**. No commits (orchestrator
commits). Repo: Y:\ClaudePlayground\TEAS-Project. Depends on Round 1a having landed
`PurchaseOrderLineDto.TaxRate` (backend) + `PoLineDto.taxRate` (FE type) — verify both exist
before starting (`grep -n "TaxRate" backend/src/Accounting.Application/Purchase/PurchaseOrderDtos.cs`,
`grep -n "taxRate" frontend/lib/types.ts`). If absent → stop and report.

## 0. Headline
`frontend/app/(dashboard)/vendor-invoices/new/page.tsx:100-134` initialises VI rows from a linked
PO in the same effect that selects the vendor, so on the "arrive via PO CTA" path the VAT
derivation reads a `vendor` that has not loaded yet (the file's own comment at :79-83 admits it),
and the effect's dependency list omits `vendor`/`companyVatRegistered`/`stdRate` (eslint-disabled
at :133) so it never corrects itself. It also overwrites every line's `productType` with `'GOOD'`
(:131) although the PO DTO carries the real one. Real blast: zero-tax PO lines at a VAT-registered
company+vendor get 0% instead of the standard rate on the CTA path; exempt/service lines become
GOOD on every path. The backend trusts the client's `vatRate` (`VendorInvoiceService.cs:236-259`,
range check only), so the wrong value posts to input VAT.
Fix: (1) use the PO line's own `taxRate` (now in the DTO — first branch of `derivePoLineVatRate`
already prefers it, `lib/po-line-vat.ts:14`), (2) `productType: l.productType`, (3) split the
effect so row-init waits for the right vendor and re-runs when its inputs arrive.

## 1. Facts (VERIFIED 2026-09-04)
- `page.tsx:84` `const vendor = useVendor(vendorId ?? 0).data;` → `lib/queries.ts:403-409`
  `queryKey ['vendor', id]`, `enabled: id > 0`.
- `page.tsx:93-94` `companyVatRegistered` (default `true`) and `stdRate` (default `0.07`) come from
  async company config.
- `page.tsx:100-134` single effect: sets BU, (if `fromPoId`) `setVendorId`/`setVendorLabel`, then
  `setRows(prev => poDetail.lines.map(...))` with a categoryId MERGE by position (WP5 fix — keep),
  `recoverable` derived from the pick or `companyVatRegistered`, `vatRate: derivePoLineVatRate(l,
  companyVatRegistered, !!vendor?.vatRegistered, stdRate)`, `productType: 'GOOD'`. Deps
  `[poDetail, poId, fromPoId]` + `eslint-disable-next-line react-hooks/exhaustive-deps`.
- `lib/po-line-vat.ts:6-18` — `if (line.taxRate != null) return line.taxRate;` then reverse-derive
  from `taxAmount/lineAmount`, else `companyVat && vendorVat ? stdRate : 0`. Tests
  `lib/po-line-vat.test.ts` (4 cases).
- `lib/types.ts:1169-1175` `PoLineDto` has `productType: ProductTypeStr` (+ `taxRate` after 1a).
- Sibling that already does it right: `app/(dashboard)/payment-vouchers/new/page.tsx:107`
  `productType: l.productType`.
- No e2e covers the CTA path; `e2e/purchase-chain.spec.ts:104-112` creates the VI by direct API
  POST with `vatRate: 0.07`.
- Local stack: API :5080 + `next dev` :3000. Memory `stale-next-dev-no-hot-reload`: restart :3000
  before trusting a "fix didn't work"; rebuild + restart the API so the DTO change is live
  (memory `local-stack-boot-recipe`: boot recipe in that note — if the stack is DOWN, boot it per
  the recipe; the dev DB is `accounting_dev`, NOT teas_test).

## 2. Design (exact)
### 2.1 `page.tsx` — split the effect
Effect A (keep deps `[poDetail, poId, fromPoId]`): BU + vendor selection only (lines 101-109 as
they are). Remove the `setRows` block from it.
Effect B (new, right after A):
```ts
useEffect(() => {
  if (!poDetail || poDetail.purchaseOrderId !== poId) return;
  // CTA path: wait until the vendor that the PO names has actually loaded; manual "link a PO"
  // path: the user picked the vendor first, derive with what they picked.
  if (fromPoId && vendor?.vendorId !== poDetail.vendorId) return;
  setRows((prev) => poDetail.lines.map((l, i) => ({
    key: i + 1,
    categoryId: prev[i]?.categoryId ?? null,
    recoverable: prev[i]?.categoryId != null ? prev[i]!.recoverable : companyVatRegistered,
    description: l.descriptionTh,
    amount: l.lineAmount,
    vatRate: derivePoLineVatRate(l, companyVatRegistered, !!vendor?.vatRegistered, stdRate),
    productType: l.productType,
  })));
}, [poDetail, poId, fromPoId, vendor, companyVatRegistered, stdRate]);
```
No `eslint-disable`. Keep every existing comment that still applies (move the WP5 merge comment
and the F-1 recoverable comment with the block). Check the `vendor` object's id field name in
`lib/types.ts` (`vendorId`?) before writing the guard. If `useVendor` returns a shape where the id
is absent, guard on `vendorId === poDetail.vendorId && vendor != null` instead.
### 2.2 `lib/po-line-vat.ts`
Update the header comment: `taxRate` is now delivered by the DTO (Round 1a), the reverse-derivation
and the registered-fallback are legacy paths for callers without it. No logic change. Add ONE
test case: `taxRate: 0` with `taxAmount: 0` at a registered company+vendor returns `0` (the PO said
0 — an exempt line must NOT fall through to the std rate).
### 2.3 e2e — `frontend/e2e/purchase-chain.spec.ts` (extend, new `test(...)` block)
Through the real UI: create a PO for a VAT-registered vendor with two lines — one 7% GOOD, one 0%
SERVICE (or whatever exempt productType the PO form offers; if the PO form cannot set 0%, create
the PO via the API the spec already uses, with explicit `taxRate: 0` + `productType: 'SERVICE'`).
Navigate to `/vendor-invoices/new?fromPurchaseOrderId=<id>` (grep the exact query param name in
`page.tsx`). Assert, once the rows render: row 1 VAT rate select shows 7%, productType GOOD; row 2
shows 0%, productType SERVICE. Use the existing helpers in `e2e/helpers/*` for login + company;
follow `rbac-e2e-gating-gotchas` memory (scope locators; wait for the page's ready sentinel).
Do not post the VI (leave `accounting_dev` clean — delete the draft PO/VI at the end if the spec's
siblings do cleanup; otherwise use the same disposable naming they use).

## 3. Invariants
- I1 A VI initialised from a PO carries each line's PO `taxRate` and `productType` verbatim,
  regardless of async load order — T2 (e2e), T1 (unit). Tier-2 F2 correction: the `taxRate` half
  of I1 is guaranteed by `PoLineDto.taxRate` being non-nullable at the DTO layer already (Round
  1a) — `derivePoLineVatRate`'s first branch takes it regardless of which effect/deps version
  reads it, so the e2e's VAT-rate assertions cannot actually falsify the effect split; the split
  is defense-in-depth (correct `recoverable` default, `productType`, no eslint-disable), not what
  makes those specific assertions pass. Only the **`productType`** assertions (row 2 = SERVICE,
  not the pre-fix hardcoded 'GOOD') falsify anything at the base commit. No delay-injection test
  added for the old stale-vendor-closure path — dead code now that taxRate comes from the DTO.
- I2 A user's already-picked expense category on a row survives the (now possibly repeated)
  row-init — code review of the merge (unchanged) + T2 asserts the category select is still
  editable after rows settle.
- I3 Manual "link a PO from the dropdown" path still populates rows (guard is `fromPoId`-only) —
  T3 (manual e2e step in the same test: pick vendor first, choose the PO in the form, rows appear).
- I4 Nothing else changes: payload shape, `canSave`, the server-side computation.

## 4. Checklist
- [x] Pre-check: DTO `TaxRate` + `PoLineDto.taxRate` present. Verified: backend
      `PurchaseOrderDtos.cs:15,36` (`PurchaseOrderLineInput.TaxRate` / `PurchaseOrderLineDto.TaxRate`);
      frontend `lib/types.ts:1172` (`PoLineDto.taxRate: number`).
- [x] 2.1 effect split. `page.tsx` now has Effect A (BU + vendor selection only, deps
      `[poDetail, poId, fromPoId]`, unchanged) and Effect B (row-init, guarded
      `if (fromPoId && vendor?.vendorId !== poDetail.vendorId) return;`, deps
      `[poDetail, poId, fromPoId, vendor, companyVatRegistered, stdRate]`, no eslint-disable).
      `productType: l.productType` (was hardcoded `'GOOD'`).
- [x] 2.2 comment + 1 unit case. Header comment updated in `lib/po-line-vat.ts`; new test
      "an explicit taxRate: 0 from the PO line stays 0 at a registered company+vendor" added to
      `lib/po-line-vat.test.ts` — 5/5 green (`corepack pnpm vitest run lib/po-line-vat`).
- [x] `docs/api/openapi.yaml`: spec's exact anchor (grep `taxAmount` near purchase-orders paths)
      does NOT exist — the whole `/purchase-orders/{id}` GET response was documented as bare
      `description: OK` with zero schema/properties (confirmed: no PO line schema anywhere in the
      file — `lineAmount`/`PoLine`/`PurchaseOrderLine` all zero hits). Per advisor consult: followed
      the in-repo inline-description precedent at line 726 (`'{ noteId, docNo, ... }'` string) —
      added one `description:` line on `/purchase-orders/{id}` GET `'200'` listing the `lines[]`
      shape incl. `taxRate` (sourced from the C# `PurchaseOrderLineDto` record, not types.ts).
      YAML re-validated (275 paths intact). Cap now 7 files (docs/api/openapi.yaml added).
- [x] 2.3 e2e (T2/T3) green against the local stack (API rebuilt Debug --no-restore, :3000
      freshly started — neither was up before this dispatch). New test in
      `e2e/purchase-chain.spec.ts`: "VI from PO: rows carry the PO line's own taxRate +
      productType, on the CTA path and the manual-link path" — 3/3 clean runs green (one earlier
      combined run hit an unrelated 30s login timeout on the LAST login cycle, reproduced as a
      one-off flake, not a logic bug — see Attempt log).
- [x] `pnpm exec tsc --noEmit` 0 errors · `pnpm vitest run lib/po-line-vat` 5/5 green ·
      `pnpm lint` 0 errors repo-wide (17 pre-existing warnings, none in touched files); ran
      `eslint` directly on all 4 touched frontend files — 0 errors/warnings, confirmed the
      `eslint-disable-next-line react-hooks/exhaustive-deps` is gone with no new "unused
      eslint-disable" complaint.

### 4a. Tier-2 remediation (Round 2, commit 77e40c4 REJECTed, 5 findings — all applied)
- [x] F1 (MEDIUM) `ProductTypeSelect.tsx` — `PRODUCT_TYPE_OPTIONS` now includes
      `EXEMPT_GOOD`/`EXEMPT_SERVICE`; comment rewritten. `messages/en.json` already had both
      labels (verified, not edited).
- [x] F3 (LOW) `page.tsx` Effect B — `vendorQ.isError` escape hatch added to the guard + deps.
- [x] F4 (NIT) `e2e/purchase-chain.spec.ts` new test's PO line 1 — `taxCodeId: null` (was the
      seed-order-dependent `1`); pre-existing test #1 untouched (out of scope).
- [x] F5 (NIT) §8 header — "Max 6 files" → "Max 9 files", extra files listed.
- [x] F2 (wording only) — Invariants §3 I1 + the Round-1 attempt-log entry corrected: only
      productType assertions falsify the pre-fix bug, not VAT-rate. No new test added.
- [x] Gates re-run post-remediation: tsc 0, lint 0 errors, vitest 5/5, e2e (`-g` on the WP-B
      test's title) 2/2 green against a freshly-restarted `next dev` :3000.

## 5. Tests
T1 unit (2.2) · T2 e2e CTA path · T3 e2e manual-link path (same test file).

## 6. Gates (worker)
As §4. You may rebuild/restart the local API (:5080) and `next dev` (:3000) — you are the only
worker using the local stack in this round. NO `dotnet test`. NO edits under `backend/`.

## 7. Out of scope
Server-side VAT derivation from tax code (backlog: backend trusts client rate) · the PO form's
tax-code handling · payment-vouchers page · WP-E lint burn-down.

## 8. Blast-radius cap
Max 9 files: `page.tsx`, `lib/po-line-vat.ts`, `lib/po-line-vat.test.ts`, `e2e/purchase-chain.spec.ts`
(+ ≤1 e2e helper if genuinely needed), this spec, `docs/api/openapi.yaml`, `troubles-wiki.md` (Round
1) + `components/ui/ProductTypeSelect.tsx`, `messages/en.json` (Tier-2 remediation, F1). Stop-and-re-
spec if the effect split needs a change to `useVendor`/queries, or the e2e needs a new backend
endpoint.

## Attempt log
- 2026-09-04 Fable: spec written; dispatch in Round 1c after WP-C (DTO) and WP-E land.
- 2026-09-04 sonnet-implementer: pre-check passed. Read `page.tsx`, `lib/po-line-vat.ts`(+test),
  `lib/types.ts` (VendorDetail.vendorId, PurchaseOrderDetail.vendorId, PoLineDto, PurchaseOrderLineInput —
  confirmed NO productType field on the create-request line shape; productType is server-derived
  from productId → Product.ProductType), `payment-vouchers/new/page.tsx` (sibling pattern),
  `PercentRateInput`/`ProductTypeSelect`/`ExpenseCategorySelector` components, `_helpers.ts`
  (`pickVendor`), `e2e/helpers/test-ids.ts`. Backend: `PurchaseOrderService.cs` `Fill()`/
  `ResolveTaxCodesAsync` (confirmed `TaxRate` is taken verbatim from the request, never re-derived
  from `taxCodeId`) + `LineProductType()` (confirmed productType comes from `Product.ProductType`
  via `productId`, defaults GOOD). `ProductDtos.cs` (`CreateProductRequest`/validator — needs
  `IsSaleable || IsPurchasable`).
  Consulted advisor before writing the e2e: confirmed effect-split guard field names, flagged the
  openapi.yaml anchor mismatch (resolved via line-726 precedent, see checklist), the biggest e2e
  risk (taxRate re-derivation from taxCodeId — verified NOT the case), and that row-1 (7%/GOOD)
  is indistinguishable from the pre-fix default so row-2's productType (SERVICE) is the assertion
  that actually falsifies the bug. Tier-2 F2 correction (see Invariants §3, I1): the VAT-RATE half
  of every row's assertion (7%, 0%) is guaranteed regardless by `PoLineDto.taxRate` being
  non-nullable at the DTO layer — it does not falsify the effect-split bug either; only
  productType does. The phrasing above conflated "row 2 as a whole" with "row 2's productType"; it
  is corrected here rather than rewritten in place, per the coordinator's instruction to fold the
  correction into the attempt log.
  Wrote the e2e test (2-line PO: line1 GOOD@7% productId:null, line2 SERVICE@0% via a freshly
  API-created SERVICE product) + CTA-path assertions (row count, description, VAT%, productType
  via `getByTestId('vi-line-product-type').toHaveValue(...)`, I2 category-editable check) +
  manual-link-path assertions (I3).
  First run: BOTH the new test AND the pre-existing (unmodified) test #1 failed identically at
  vendor create (400 `vendor.vat_registered_requires_taxid`) — probed the actual response body via
  a standalone node fetch script (`Z:/temp/.../scratchpad/probe-vendor.mjs`) since Playwright's
  error-context.md doesn't capture it. Confirmed pre-existing validator drift, unrelated to this
  WP (test #1 untouched by this diff) — documented in troubles-wiki.md, fixed only the NEW test's
  vendor payload (`taxId: '0105556123453'`, same value `_helpers.ts` already uses). Re-ran: new
  test green, test #1 still red (out of scope, flagged for separate 1-line fix).
  Isolated re-runs of the new test: run 2 hit an unrelated 30s login timeout on the 5th
  login/logout cycle (dev-server first-hit compile latency under single-worker load, not a
  logic bug); runs 3 and 4 both green (17-18s each). 3/4 total green, the one red run failed
  on infra timing, not an assertion.
  gates: tsc 0 errors, vitest 5/5, lint 0 errors (17 pre-existing warnings elsewhere, 0 in the
  4 touched files). Backend build: Debug --no-restore succeeded 0 warnings/errors. Local stack
  booted fresh (API :5080, next dev :3000 — both were down at dispatch start) and left running.
  DEVIATION (documented, not silent): openapi.yaml's exact spec'd anchor didn't exist; used an
  in-repo precedent instead of inventing a full schema (see checklist item 3, advisor-endorsed).
- 2026-09-05 sonnet-implementer (Round 2, Tier-2 remediation — COMPLETE): coordinator relayed
  Tier-2 REJECT with 5 Fable-verified findings on top of commit 77e40c4 (WP-B already committed).
  Blast cap raised to 9 files (§8 updated). Session hit the 95% quota-guard threshold right after
  reading `git log`/`git show 77e40c4` before any edit; checkpointed a resume note (since removed
  — folded here) and paused (no `ScheduleWakeup` available to a dispatched worker). Quota reset;
  resumed and applied all 5 findings:
  - F1 `components/ui/ProductTypeSelect.tsx` — `PRODUCT_TYPE_OPTIONS` now
    `['GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE']`; comment rewritten to explain the desync
    (PO lines can arrive as EXEMPT_GOOD per `PurchaseOrderService.cs`'s `LineProductType` —
    verified: a productId-null line with `TaxRate` 0 returns `"EXEMPT_GOOD"`, confirmed by reading
    the function directly). `messages/en.json` already had `EXEMPT_GOOD`/`EXEMPT_SERVICE` labels
    (:708-709, verified) — no en.json edit needed.
  - F3 `page.tsx` — `const vendor = useVendor(...).data` split into
    `const vendorQ = useVendor(vendorId ?? 0); const vendor = vendorQ.data;`; Effect B's guard is
    now `if (fromPoId && !vendorQ.isError && vendor?.vendorId !== poDetail.vendorId) return;` with
    `vendorQ.isError` added to the deps array; added a one-line comment on the error fallback.
  - F4 `e2e/purchase-chain.spec.ts` (my new test's line 1 ONLY — pre-existing test #1's
    `taxCodeId: 1` lines untouched, out of scope) — changed to `taxCodeId: null, taxCode: 'VAT7',
    taxRate: 0.07`; verified `ResolveTaxCodesAsync`'s byCode/standardInput backstop resolves the FK
    without depending on a specific seeded id.
  - F5 §8 header bumped to "Max 9 files", listing `ProductTypeSelect.tsx` + `messages/en.json`
    (not actually touched — already had the labels) alongside the Round-1 files.
  - F2 wording-only: corrected Invariants §3 I1 and the Round-1 attempt-log entry above to state
    the VAT-rate assertions cannot falsify the effect split (guaranteed by non-nullable
    `PoLineDto.taxRate` regardless); only the productType assertions do. No new test added.
  Gates: `tsc --noEmit` 0 errors (ran twice, after F1+F3 and again after F4). `eslint` on the 5
  touched files (incl. `ProductTypeSelect.tsx`): 0 errors/warnings; full `pnpm lint`: 0 errors,
  same 17 pre-existing warnings elsewhere. `vitest run lib/po-line-vat`: 5/5 green. `next dev`
  :3000 was still running from Round 1 — killed (PID on :3000) and restarted fresh; API :5080
  left as-is (no rebuild needed, F1/F3/F4 are FE-only). e2e re-run by title
  (`-g "rows carry the PO line"`) twice against the fresh dev server: 2/2 green (23.1s, 17.0s).
  `git status --porcelain` for my files: `page.tsx`, `ProductTypeSelect.tsx`,
  `e2e/purchase-chain.spec.ts`, this spec (all `M`, en.json NOT modified).

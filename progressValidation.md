# Sprint 13j-PURCH — Validation / Gate Evidence

> Paste ACTUAL command output here per phase gate. Evidence before assertions (CLAUDE.md §9). A phase is not ☑ in `progressPurchase.md` until its gate evidence is recorded here.

---

## Gate H — Flag-1/2 follow-ups (subAgent8, 2026-05-27)

**Scope:** Flag-2 (BP-05) bidirectional Purchase chain + Flag-1 (BP-09) read-only VI PaperDocument. Read-only DTO extension + FE only — NO entity, NO migration, NO new endpoint.

**BE files:**
- `Application/Purchase/VendorInvoiceDtos.cs` — new `VendorInvoiceSettlingPv(PaymentVoucherId, DocNo, Status)` record; `VendorInvoiceDetail` gained `SettlingPvs`.
- `Application/Purchase/PurchaseReadDtos.cs` — new `PaymentVoucherWhtCertificate(WhtCertificateId, DocNo, Status)` record; `PaymentVoucherDetail` gained `WhtCertificates`.
- `Infrastructure/Purchase/VendorInvoiceService.Read.cs` — `settlingPvs` query: `PaymentVouchers.Where(VendorInvoiceId == id)` UNION applied-PV-ids from `PaymentVoucherApplications.Where(VendorInvoiceId == id)`. Tenant-safe: `PaymentVoucherApplication` is NOT ITenantOwned (no global filter), so the applied ids are intersected against the tenant-filtered `PaymentVouchers` DbSet (never trusting raw application rows for isolation).
- `Infrastructure/Purchase/PaymentVoucherService.Read.cs` — `whtCerts` query: `WhtCertificates.Where(PaymentVoucherId == id)` (WhtCertificate IS ITenantOwned → global filter scopes by company).

**FE files:** `lib/types.ts` (+`settlingPvs`/`whtCertificates` + ref interfaces), `components/doc/PurchaseDocumentChain.tsx` (downward resolution), `lib/paper-doc-config.ts` (+`vendor-invoice` kind, watermark case, `companyToCustomer`), `app/(dashboard)/vendor-invoices/[id]/page.tsx` (PaperDocument render).

**Chain resolution now (PurchaseDocumentChain):**
- VI page → resolves PV downward via `vi.settlingPvs[0]`, then WHT via `pv.whtCertificates[0]` (hydrates the downward PV to read its certs).
- PV page → resolves WHT downward via `pv.whtCertificates[0]` (and VI/PO upward as before).
- PO page → `linkedVis[0]` → hydrate that VI → its `settlingPvs[0]` PV → its `whtCertificates[0]` WHT (full PO→VI→PV→WHT).
- WHT page → upward PV→VI→PO unchanged. First-child picked deterministically (BE OrderBy id); single linear path, no fan-out; unresolved nodes omitted.

**Gate 1 — BE build (kill :5080 → `dotnet build W:\Accounting.sln`):**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
Time Elapsed 00:00:33.86
```

**Gate 2a — FE `npx tsc --noEmit`:** `EXIT=0` (0 errors).

**Gate 2b — FE `next build` (native path, next dev stopped first, restarted after):** `EXIT=0`. 66 route `page.js` files compiled, incl. `/vendor-invoices/[id]` (2.84 kB / 172 kB First Load). Dev server restarted on :3000 to restore env.

**No i18n keys added** — VI paper reuses existing `vi.*` keys (vendorTiNo, vendorTiDate, claimPeriod, nonRecVat, settled). No new visible labels.

**No conflicts with CLAUDE.md / Requirements** — VI gets no PrintMenu (§4.6, no `/pdf` endpoint), no watermark (recorded inbound doc), view-only (§4.2). Resolves BP-05 + BP-09.
> Env: BE tests from `W:\tests\Accounting.Api.Tests` with `TEAS_TEST_PG` set; FE `tsc`/`next build` from NATIVE frontend path (NOT `U:\frontend`).

---

## Baselines (record before sprint)

- BE existing test count (pre-sprint): _____ (Answer-Sana cites 112+)
- `dotnet build W:\Accounting.sln`: ☐ 0/0 confirmed clean before starting
- FE `tsc --noEmit`: ☐ 0 before starting
- FE `next build`: ☐ 0/0 (___ pages) before starting

---

## Gate A — Purchase audit hooks  ✅ PASS (subAgent1, 2026-05-27)

- [x] `dotnet build W:\Accounting.sln` → **0 Warning(s) / 0 Error(s)** (after killing :5080 listener).
- [x] `PurchaseAuditTests` run 1 → **12/12 pass** (`Passed! Failed: 0, Passed: 12, Total: 12`)
- [x] `PurchaseAuditTests` run 2 (consecutive, same `teas_test`) → **12/12 pass**.
      (Subsequently re-ran 4× more in a loop → 12/12 each; 6/6 green total. One earlier
      run, immediately after the assertion fix, showed 11/12 with a transient
      `DbUpdateException` on `Pv_post_with_wht_records_certificate_generated_issued`; no inner
      exception captured and NOT reproduced in 6 subsequent full-suite runs → logged BP-01.)
- [x] existing suite ≥ baseline (regression of the 4 patched manual-DI test files + Sprint12):
      `Sprint1HardeningTests|Sprint6SettlementTests|Sprint6VatRegisterTests|Sprint55VendorInvoiceTests|Sprint12PurchaseOrderTests`
      → run1 **26/26 pass**, run2 **26/26 pass** (no regression from adding `IActivityRecorder` to ctors).

**Files touched:** `Infrastructure/Purchase/{PurchaseOrderService,VendorInvoiceService,PaymentVoucherService}.cs`
(audit hooks); 4 test-DI patches (`AddScoped<IActivityRecorder, ActivityRecorder>()` +
`using Accounting.Application.Audit; using Accounting.Infrastructure.Audit;`) in
`Sprint{1Hardening,6Settlement,6VatRegister,55VendorInvoice}Tests.cs`; new
`tests/Accounting.Api.Tests/Purchase/PurchaseAuditTests.cs` (12 tests).

**Drift flagged:** PO `MarkSentAsync` does NOT mutate the status enum to a "Sent" member
(no such member — status stays `Approved`, only `SentToVendorAt` is set); the
`toStatus:"Sent"` audit label is therefore semantic only. Recorded as the plan specified.

## Gate B — AP Aging  ✅ PASS (subAgent2, 2026-05-27)

- [x] D2 settlement source confirmed: ☑ `SettledAmount` / ☐ `Σ applications` — note: `PaymentVoucherService.PostAsync` :293-295 updates `vi.SettledAmount += applied` and recomputes `SettlementStatus` (PAID/PARTIAL/UNPAID) on PV post. Used preferred path: `Outstanding = TotalAmount − SettledAmount` filtered by `SettlementStatus != "PAID"` (stored value, no application SUM).
- [x] build → 0/0 (`dotnet build W:\Accounting.sln` → Build succeeded, 0 Warning(s), 0 Error(s))
- [x] `ApAgingTests` 2× → run1 10/10 · run2 10/10 (Failed:0, Passed:10, Skipped:0)
- [x] multi-tenant isolation test present + green (`MultiTenant_company_a_vi_absent_from_company_b_report`)
- [x] OpenAPI `/reports/ap-aging` added + my blocks valid (standalone PyYAML parse OK; `ApAgingRow` schema added under components). NOTE: whole-file PyYAML strict parse fails on a PRE-EXISTING unquoted-colon line (`Idempotency-Replayed: true` in a `description:` scalar, present in HEAD) — unrelated to this change; lenient OpenAPI tooling (Swagger/Redoc) accepts it.

**Files:** `Application/Reports/{ApAgingDtos,IApAgingService}.cs` (new), `Infrastructure/Reports/ApAgingService.cs` (new), `Infrastructure/DependencyInjection.cs` (+1 registration), `Api/Endpoints/PurchaseOrderEndpoints.cs` (+GET /reports/ap-aging, D1), `docs/api/openapi.yaml` (+path +schema), `tests/Accounting.Api.Tests/Reports/ApAgingTests.cs` (new, 10 cases). Endpoint auth = `PurchaseOrderRead` (same as outstanding-po).

## Gate C — PDF consolidation + migration

- [x] **C1 (MAIN AGENT, per CLAUDE.md §7.4)** entity edits (`PurchaseOrder`+`PaymentVoucher`: `OriginalPrintedAt`,`PrintCount`) → `dotnet build W:\Accounting.sln` **0/0**; then `dotnet ef migrations add AddPrintTrackingToPurchaseChain` WITH build from W: → `20260527033720_AddPrintTrackingToPurchaseChain`
- [x] **C1** migration reviewed — 4 additive cols (nullable timestamptz + int default 0), `Down` drops cleanly, no data loss · applied to dev DB ("Applying… Done."). _subAgent3 does C2–C5 only; must NOT add migrations/edit entities._
- [x] **C2–C5 (subAgent3)** build → `dotnet build W:\Accounting.sln` **0/0** (kill :5080 first)
- [x] `PurchasePdfTests` 2× on teas_test → **run1 6/6** · **run2 6/6** (Draft/Approved/Posted × single/multi-line × PV with/without-WHT; original + copy watermark)
- [x] regression: **Sales suite 27/27** green · **Purchase suite 23/23** green (12 audit + 6 PDF + others). _Note: there is no separate service-layer TI/CN/DN PDF test class in the repo — the shared `PaperDocModel`/renderer changes are additive (new optional `Wht`, `Middle` fields) and the Sales document suite passing is the regression proof._
- [x] PO + PV PDF eyeballed via test asserts: every render `> 1024` bytes AND starts with `%PDF-` magic; `/pdf` endpoints return `application/pdf` (Results.File content-type, unchanged)
- **Deviation (flagged):** task C4 said "stamp OriginalPrintedAt/PrintCount inside the `/pdf` handler." Followed the SHIPPED Sales pattern instead (ENV-briefing "Gold Standard" rule): `/pdf?copy=` controls the watermark only; tracking is a separate `POST /{doc}/{id}/mark-printed?copy=` via the extended `PrintTrackingService` + `PrintEndpoints` (added `PrintDocType.PurchaseOrder`/`PaymentVoucher`, audit `module="purchase"`). Smallest diff + keeps Phase D FE wiring identical to Sales.
- **PaperDocModel extensions (additive, no fork):** `PaperSummary.Wht (decimal? = null)` → PV foot prints "หัก ณ ที่จ่าย · WHT" + grand total label "จ่ายสุทธิ · Net Paid"; `PaperSignRoles.Middle (string? = null)` → PV 3-box sign. Both default null → Sales callers unaffected. Renderer (`PaperDocumentPdf.Foot`/`Sign`) wired for both.

## Gate D — FE paper/chain/print  (subAgent4 — 2026-05-27)

- [x] `tsc --noEmit` → **0 errors** (`node node_modules/typescript/bin/tsc --noEmit`, EXIT 0)
- [x] `next build` (NATIVE path, dev stopped first then restarted) → **EXIT 0, ~60 routes**;
      all four Purchase routes compiled: `/purchase-orders` + `/[id]`, `/vendor-invoices` + `/[id]`,
      `/payment-vouchers` + `/[id]`, `/wht-certificates` + `/[id]`.
- [~] PaperDocument + PrintMenu wired (see deviations — NOT a blanket "all four wrap"):
  - **PO** `[id]`: `<PaperDocument>` (seller=our company via `useCompanyProfile`, customer=vendor via
    `useVendor`) + **tracked** `<PrintMenu docType="purchase-orders">` (Phase C `?copy`+`mark-printed`).
  - **PV** `[id]`: tracked `<PrintMenu docType="payment-vouchers">`; kept card layout (PaperSummary has
    no WHT/net-paid row — see D3-dev-2).
  - **VI** `[id]`: **no PrintMenu** — there is NO `vendor-invoices/{id}/pdf` BE endpoint at all
    (verified in `VendorInvoiceEndpoints.cs`). VI keeps StatusBadge/linked-PO only (D3-dev-1).
  - **WHT** `[id]`: **untracked** `<PrintMenu docType="wht-certificates" tracked={false}>` → hits the
    bespoke 50ทวิ `/pdf` (no `?copy`/mark-printed) — replaces the old download-only button (D3-dev-3).
- [STOP] **DocumentChain panel deferred on ALL Purchase pages** — BE `GetChainAsync` switch is Sales-only
  (returns null for Purchase anchors) and `DocumentChainDto` is a fixed 7-slot Sales shape. Rendering
  `<DocumentChain>` would always return null. Existing inline links (PO→linkedVis, VI→linkedPo,
  PV→settlingVi, WHT→fromPv) cover the need; `// TODO D2 follow-up` left in PO page. See D2 finding.
- [x] Posted detail read-only **confirmed** (no rewrite needed): all four pages already gate every action
      button by `status` and render values, never editable inputs (§4.2). PO actions only show for
      Draft/Approved; VI Post only when Draft; PV approve/post by status; WHT has no mutating action.
- D1: doctype-enum extension **not needed** — `PrintMenu`/`ChainRowPrint`/`useMarkPrinted` all take a
  free-string `docType` (route segment), no enum. `DocumentChain`'s internal `Kind` union extension is
  deferred behind D2 (dead code without a BE Purchase chain shape).
- D1 bugfix: `ChainRowPrint.tsx` `?copy=1` → `?copy=true` (BE binds `bool?`, `1` → 400). See bugPurchase.md.
- Browser render check: **BLOCKED — backend not running on :5080**, so login `/api/auth/login` → 500 and
  data-backed pages can't load (FE dev on :3000 is up; this is environmental, not a code issue). Relied on
  `tsc` 0 + `next build` 0/0 per task fallback. Re-verify in browser once BE is up (login admin/Admin@1234).

### Gate D-supplement — PV→PaperDocument + chain panel (subAgent4b — 2026-05-27, FE-only)

Closes the two parity gaps the prior subAgent4 left (PV stayed a card; chain panel deferred on all 4).
**Now that BE Phase C added `PaperSummary.Wht` + `PaperSignRoles.Middle` (verified in `PaperDocModel.cs`
lines 50/59), the "PaperSummary can't model WHT/net-paid" blocker from D3-dev-2 is RESOLVED — PV is a
PaperDocument.** No C# touched.

- [x] `tsc --noEmit` → **0 errors** (`node node_modules/typescript/bin/tsc --noEmit`, EXIT 0). One fix
      en route: TS5076 mixed `??`/`||` in `PurchaseDocumentChain.tsx` → wrapped in parens.
- [x] `next build` (NATIVE path, dev :3000 stopped first) → **✓ Compiled successfully, 0 errors / 0
      warnings, 52 routes generated**. All 4 Purchase `[id]` routes compiled (PV `[id]` 5.26 kB).
- [x] **FE paper primitives extended (additive, Sales callers byte-identical):**
  - `PaperSummary.wht?: number | null` → `PaperFoot` inserts a "หัก ณ ที่จ่าย · WHT" row (−amount)
    between VAT and the grand total (independent of `showVat`), and the grand-total label switches to
    "จ่ายสุทธิ · Net Paid" with value `total − wht`; amount-in-words follows the net figure. Mirrors
    `PaperDocumentPdf.cs` lines 252–255 exactly.
  - `signRoles.middle?: string` on `PaperDocumentProps`/`PaperSign` → optional 3rd signature box; absent
    → unchanged 2-box strip. Mirrors `PaperSignRoles.Middle`.
  - `paper-doc-config.ts`: added `'payment-voucher'` kind (`ใบสำคัญจ่าย` / PAYMENT VOUCHER, 3-box
    ผู้จัดทำ/ผู้อนุมัติ/ผู้รับเงิน, `Posted` → "ต้นฉบับ" watermark); updated the stale "PV is NOT a
    PaperDocument" comment.
- [x] **PV `[id]` migrated card → `<PaperDocument>`**: seller=our company (`useCompanyProfile`),
      customer=vendor (from PV DTO's own `vendorName/TaxId/BranchCode/Address`). Summary `total =
      subtotal + vat` (pre-WHT gross) so net-paid = `total − whtAmount = totalPaid`. `wht` passed only
      when `whtAmount > 0`. Payment method/category moved into `extraMetaBlock`. Tracked `<PrintMenu
      docType="payment-vouchers">` preserved. View-only — no editable inputs (§4.2), action buttons
      still status-gated.
- [x] **NEW `components/doc/PurchaseDocumentChain.tsx`** (FE-only, mirrors `DocumentChain.tsx` look):
      PO→VI→PV→WHT nodes with `StatusBadge` + docNo, current node highlighted, links to detail pages.
      Dropped into all 4 detail pages vertically (PO/VI side column; VI/WHT below the card).
  - **Node resolution (no BE endpoint added):** purely from cross-refs already on each detail DTO,
    which point UPWARD — WHT.paymentVoucherId→PV, PV.vendorInvoiceId→VI, VI.purchaseOrderId→PO; plus
    the one DOWNWARD ref `PO.linkedVis[0]→VI`. Each referenced doc is hydrated via its own detail hook
    (`enabled` only when id>0).
  - **Omitted downstream nodes (deliberate, partial-but-correct per Phase 1):** the `wht-certificates`
    list has NO `paymentVoucherId` filter, so opening a **PV cannot discover its WHT cert**, and opening
    a **VI cannot discover its PV**; opening a **PO** resolves only its first linked VI (not PV/WHT).
    Full bi-directional chain awaits the deferred downward cross-refs (Question-Backend36).
  - i18n: added `purchaseChain` namespace to `messages/th.json` + `en.json` (TH primary).
- [~] Browser render check: **BLOCKED — backend still DOWN on :5080** (health probe timed out). Did not
      start it (§6 footgun reserved for main agent; FE-only scope). Relied on `tsc` 0 + `next build`
      0/0 per task fallback. Re-verify PV PaperDocument WHT/net-paid row + chain panel once BE is up.

## Gate E — FE AP Aging page  ✅ PASS (subAgent5 + main-finish, 2026-05-27)

subAgent5 hit a SESSION LIMIT mid-edit (left `ap-aging/page.tsx` with a broken JSX fragment + the `apAging` i18n namespace + `nav.apAging` label + SidebarNav entry all unwritten). Main agent finished:
- [x] Fixed `ap-aging/page.tsx` — missing `</button>` on the vendor "clear" control (TS17014/17002 cascade). The rest of subAgent5's page (table, 4 buckets + Totals row, CSV export, MascotGreeting empty-state, Bangkok-today default) was sound.
- [x] `useApAgingReport(asOf, vendorId?)` hook present in `lib/queries.ts:994` — calls `reports/ap-aging${qs({asOf, vendorId})}` → emits `?asOf=` (camelCase, matches BE) ✓; `ApAgingRow`/`ApAgingReport` types in `lib/types.ts`.
- [x] Added missing i18n: `apAging` namespace (13 keys) to `messages/th.json` + `en.json`; `nav.apAging` label both; `SidebarNav.tsx` entry `/reports/ap-aging` (key `apAging`, Icon Coins) under the reports section — Purchase menu + Settings route untouched (Ham locked).
- [x] `tsc --noEmit` → **0** · `next build` (native, dev stopped) → **0/0**; `.next/.../reports/ap-aging/page.js` artifact built ✓.
- [ ] live demo-data render — DEFERRED to Gate G (backend warming up at build time).

## Gate F — FE bug pass + PO form  ✅ PASS (subAgent6, 2026-05-27)

- [x] `npx tsc --noEmit` → **EXIT 0** (no errors) from native frontend path.
- [x] `next build` (native path, dev stopped — port 3000 confirmed free) → **EXIT 0**,
  `✓ Compiled successfully`, `✓ Generating static pages (54/54)`. Both new/lifted routes present:
  - `ƒ /purchase-orders/new        4.61 kB   183 kB`
  - `ƒ /settings/expense-categories 2.63 kB   137 kB`
- [x] **F1 PO /new = VI quality**: rewrote from the 1-line stub (hardcoded `taxCodeId:1/VAT7/0.07`)
  to RHF+Zod + `<LineItemsTable enableProduct>` (multi-line, `<ProductPicker>` per line w/ free-text
  fallback, per-line discount %, VAT-rate dropdown sourced from `/system/info` not hardcoded —
  CLAUDE.md §4.6). Mirrors `QuotationForm` exactly, incl. rate→taxCode map
  (`taxCode: vatMode && taxRate>0 ? 'VAT7' : 'VAT0'`). Submit→`/purchase-orders/[id]` redirect kept.
- [x] **F2 expense-categories**: new read-only `settings/expense-categories/page.tsx` mirroring
  `settings/wht-types` (QueryState + table), using existing `useExpenseCategories()` hook
  (`GET /expense-categories`). No CRUD. Renders code/name/recoverableVat/CAPEX. Nav entry added to the
  Settings group (route NOT relocated). **Row count (19) not visually confirmed — backend was DOWN on
  :5080** during the gate; relies on the seeded `sys.expense_categories` returned by the hook.
- [x] **F3 toast Thai-only (#SR9)**: PO /new error toast now surfaces the BE Thai ProblemDetails
  (`err.detail ?? err.title ?? tc('error')`) instead of generic-only. VI/PV `/new` already used
  i18n keys (`tc('error')`/`tc('save')`/`t('pickVendor')`) — no English bleed found.
- [x] **F4 column headers**: purchase list/report pages clean. Only leftover is a `<th>VAT</th>` on
  `vendor-invoices/[id]` (detail page, not a list) — left as-is: "VAT" is the literal EN i18n value
  and locale-invariant; outside the list-page audit scope.
- [ ] **NOT verified (BE down)**: live browser render of `/purchase-orders/new` + `/settings/expense-categories`,
  and the 19-row count. Recommend a quick visual pass in Gate G once :5080 is up.

## Gate G — FINAL consolidated (main agent, 2026-05-27)

- [x] `dotnet build W:\Accounting.sln` → **0/0** (with print-tracking entity + migration)
- [~] ALL BE tests 2× consecutive on `teas_test` → **run1 174/174** ✅ · **run2 173/174** — the 1 failure = pre-existing `Sprint9VatComplianceTests.Pnd30_finalize_is_immutable` (passes 5/5 ×2 in isolation; cross-test ภ.พ.30 fixed-period collision, NOT a Purchase regression — BP-07). Sprint's own 28 new tests (PurchaseAudit 12 / ApAging 10 / PurchasePdf 6) pass 2× clean.
- [x] `tsc` 0 · `next build` 0/0 (native, **54 routes**)
- [ ] E2E `purchase-chain.spec.ts` — **NOT WRITTEN** (Phase G out of session budget). Downgraded to manual revalidate per G0; chain currently covered by BE integration tests + per-phase verification. → remaining item / Sana RE-VALIDATE.
- [~] Sales regression — BE Sales suite 27/27 + full-suite run1 green; no Playwright E2E run (no purchase E2E authored). Manual revalidate recommended.
- [x] OpenAPI updated (`/reports/ap-aging`) · no `inventory.*` artifact · no posted-doc edit/delete · no `audit.activity_log` delete (verified)
- [x] `docs/Report-Backend35.md` written
- [x] `progress.md` cont.71 prepended · `plan.md` Sprint 13j-PURCH ☑
- [x] **NO git commit** — `HEAD` still `174323c`, handed to Ham

### Gate G — E2E `purchase-chain.spec.ts`  ✅ PASS (subAgent7, 2026-05-27, supersedes line 174)

**E2E infra EXISTS** — `frontend/playwright.config.ts` (system Edge channel, baseURL :3000, stack started externally), 40+ existing specs, `e2e/_helpers.ts` (login/logout/createVendor/pickVendor) + `e2e/helpers/test-ids.ts`. Playwright installed (`node_modules/@playwright/test`). Run via `node node_modules\@playwright\test\cli.js test <name>`.

**Authored** `frontend/e2e/purchase-chain.spec.ts` — hybrid pattern (mirrors `purchase-order-flow.spec.ts`): state transitions driven through the BFF proxy via `page.request`, then the three detail pages visited in-browser for the Phase-D widget assertions. SoD-correct (admin/ap_clerk/approver alternation). Unique vendor via `TestIds.vendorCode`.

- [x] **PASS 2× consecutive** on the live stack (:5080 + :3000), shared dev DB → idempotent:
  - run 1: `✓ 1 [system] › purchase-chain.spec.ts:27 … (20.5s)  ·  1 passed (21.9s)`
  - run 2: `✓ 1 [system] › purchase-chain.spec.ts:27 … (20.1s)  ·  1 passed (21.4s)`
- Chain steps verified (all green): PO multi-line create → SoD approve → mark-sent (204) → **VI from PO (purchaseOrderId links back, claimPeriod set) → post (Posted)** → **PV from VI w/ WHT → SoD approve → post (Posted, whtAmount>0, vendorInvoiceId links back)** → **50ทวิ cert issued + its `/pdf` returns 200** → **`/reports/ap-aging?vendorId=` shows the vendor with ZERO outstanding** (PV settles full VI gross 1605 → VI PAID) → detail pages.
- Detail-page widgets: **PO ✅ (chain + PaperDocument + PrintMenu all visible)** · **PV ✅ (all three)** · **VI ⚠ chain ✅ but PaperDocument + PrintMenu ABSENT** → recorded as test annotations (`known-gap`), NOT a hard fail, NOT app-hacked → **BP-04 / BP-09** (no `GET /vendor-invoices/{id}/pdf` endpoint, Phase D gap). Flip the two VI annotations to hard `expect` once the BE adds the VI PDF endpoint + FE wires the widgets.

**Findings surfaced during G (pre-existing, NOT Phase-G regressions):**
- **BP-08** — `ap_clerk` PV-create → 422 `pv.expense_category_missing` for an admin-resolved SVC category id, on the live stack. Confirmed pre-existing: the untouched `payment-voucher-non-super-rbac.spec.ts` fails identically at its PV-create assertion. Routed around in the new spec by creating the PV as `admin` (SoD still satisfied via `approver`).
- **Sales E2E regression check (G2):** `quotation-chain-flow.spec.ts` FAILS at its very first step — `getByTestId('q-status')` never reaches `Sent|ส่งแล้ว` after issuing a quotation (15s timeout). Unrelated to Purchase work (I added ONE new file + doc edits only; no Sales code/helper touched) → pre-existing FE/data drift on this dev stack, Sales bug track. Flag to Ham; not a Phase-G blocker.

# Fix execution tracker — code-review 2026-06-17 (autonomous overnight run)

Status as of 2026-06-18 ~01:34 (Asia/Bangkok). Overseer = main agent; ALL fixes by subagents (Ham: ห้ามทำเอง). NOT git-committed (CLAUDE.md §10 — Ham reviews on wake).

- **Batch A** (backend compliance ① pin doc_date · ② block non-THB · ③ periods default-closed · ⑩ compliance tests): ✅ **DONE + GATED** — build 0/0; Api 415 pass/0 fail/7 skip ×2; Domain 146/0; ① pinned across 8 services; ② `ThbOnly()` guard live; ③ missing-period open only for current Bangkok month. Files: 17 src + new `CurrencyValidationExtensions.cs` + 10 migrated tests + new `BatchAComplianceTests.cs` (11 tests).
- **Batch D** (frontend ⑨ i18n + ⑪ quality): ✅ **DONE + GATED** — tsc 0; messages th/en 1434/1434, 0 drift; Bengali glyph clean. 18 files. Some FE-quality items deferred (RSC refactor, Buddhist-era display — judgment calls, non-compliance).
- **Batch B** (RLS ④ + immutability triggers ⑤): ✅ **DONE + GATED** — build 0/0; 3 new SqlScripts (570 receipt-immutability+RLS, 571 CN/DN-immutability+RLS, 572 sales-chain RLS) via DbInitializer/fixture auto-apply (no EF migration; tables pre-existed); +11 tests; gate teas_test 426/0/7; fresh reseed 421 ×2; RLS genuinely tested (SET ROLE non-bypass + drop-policy self-check). NOTE: 5 fresh-DB failures (Sprint9VatCompliance ×3, Sprint9FinancialReport sales-summary, Sprint10Product sales-summary) are PRE-EXISTING (co2/co3 demo-seed tax_codes gap), proven not-mine, out of scope.
- **Batch C** (engineering ⑥ .Result MasterData · ⑦ PermissionLookup merge · ⑧ ApiKeyResolver catch): ✅ **DONE + GATED** — build 0/0; Domain 146/0; ⑥ 4 methods async (no .Result/ContinueWith remain); ⑦ PermissionLookup single query + AsNoTracking; ⑧ ApiKeyResolver logs+rethrows-cancellation.
- **⚠️ Overseer finding (gating C):** the "5 fresh-DB failures" that B AND C both labelled "pre-existing co2/co3 tax_codes gap" are **MISDIAGNOSED**. Read the tests: they use **company 1** (`C-DEMO-001`, 12 tax_codes — NOT co2/co3). Real cause = **A's ① doc_date pinning**: `PostTi` hardcodes `new DateOnly(2026,5,16)` then queries period `202605`, but ① now forces doc_date=today → TI lands in current month → 202605 report empty/0. A migrated 21 such tests but MISSED these 5 (gated on bloated teas_test where stale May rows masked them). Product is CORRECT; tests need date-migration. Failing: Sprint9VatComplianceTests (~3), Sprint9FinancialReportTests.Sales_summary_by_customer_sums_posted_tis (A modified it but incompletely), Sprint10ProductTests.Sales_summary_group_by_product.
- **Batch E** (test-migration: fix the 5 date-fragile tests missed by A): ✅ **DONE + GATED** — 6 tests in Sprint9VatCompliance/Sprint9FinancialReport/Sprint10Product migrated to clock-derived period; tests-only (0 src changes).

## ✅ ALL DONE — overseer final gate (2026-06-18 ~02:35)
- `dotnet build W:\Accounting.sln` = **0/0**
- Full Api suite on FRESH teas_test (overseer re-ran) = **Failed 0 / Passed 426 / Skipped 7** ✓
- Domain = **146/0** · FE `tsc --noEmit` = **0** · i18n th/en = **1434/1434, 0 drift** · Bengali glyph = clean
- `backend/src` changed = 24 files (= A 18 + B 3 + C 3); E + test-migration = tests only.
- NOT committed (Ham reviews). vat_rate-in-UI ruled KEEP (super-admin only).
- Overseer caught a B/C misdiagnosis: the 5 fresh-DB failures were ① doc_date pinning (company 1), NOT the co2/co3 tax_codes gap → fixed by Batch E, not hand-waved.

## ROUND 2 — remaining findings (Ham ticked ALL, 2026-06-18) — subagent batches F/G/H/I
### PONYTAIL CUT (2026-06-18, after Ham enabled ponytail mid-round-2)
Ham ticked all ~30 remaining, but ponytail (anti-over-engineer) → ship only the worth-it few; skip the bloat.
- **Batch R2-lean** (1 backend subagent): 🔄 DISPATCHED — (1) remove committed secrets (login_resp.json + appsettings DB pw) (2) RLS purchase PV/PO [High] (3) AsNoTracking GL-posting [High] (4) CN/DN GL-balance test [High] (5) security headers + auth rate-limit (native, no dep) (6) 1-liners: del dead ClockDateExt + PayslipConfig HasPrecision.
- **Ham correction:** ponytail = simplest fix per item, NOT drop scope. Do ALL ticked items, simplest way. Re-queued:
  - **Batch H** (frontend): ⚠️ PARTIAL — Task1 (removed legacy api-client) + Task2 (typed ~30 mutations) DONE but STOPPED mid-Task3 and left tsc RED (1 error receipts/new/page.tsx:279, ReceiptApplicationInput union). RSC barely started (73 pages still 'use client'), i18n leftovers not done.
  - **Batch H2** (frontend continuation): ✅ DONE+GATED (overseer ran tsc=0) — fixed ReceiptApplicationInput.taxInvoiceId→optional (tsc green); RSC converted 10 shell-wrapper pages, 45 stay client (genuinely interactive=correct); i18n leftovers mostly already-correct (no Buddhist era; kBaht/problemToast fixed earlier; toLocaleString fine); useEffect form-seed left as-is (useState not RHF, ponytail). th/en 1434/1434. **FRONTEND (Q3) COMPLETE.**
  - **Batch R2-lean**: ✅ DONE+GATED — build 0/0 · Api 428/0 ×2 · Domain 146 · 573_purchase_chain_rls.sql (PV/PO) · CnDnGlBalanceTests (real balance assert) · AsNoTracking GL · security headers+login rate-limit · secrets removed (login_resp.json gitignored, appsettings pw → placeholder) · dead ClockDateExt deleted · PayslipConfig HasPrecision.
  - **Batch P** (backend remainder): ✅ DONE+GATED (overseer re-gated after shutdown) — build 0/0 · Api 428/0/7 · has-pending-model-changes=**No changes** · migration 20260618023338_AddPerfIndexes (indexes) · FallbackPolicy (whitelist anon) · ValidationException handler · TI PDF exempt-label · BillingNote/IdempotencyStore/CancellationToken/cleanup. P had finished before the process died; the "failed" was just the lost gate-run, which I re-ran green.
  - **Batch T** (backend tests+feature, last): ✅ DONE+GATED — CnDnGlBalanceTests · posted-TI re-post reject test · draft DocNo=null assert · ApAging→TestIds.FuturePeriod · e2e skip loud · receipt over-apply guard (receipt.over_applied + TOCTOU test) · DO qty-cap guard (do.over_delivered + test).

## ✅✅ ROUND 2 COMPLETE — overseer final gate (2026-06-18)
build 0/0 · full Api **431/0/7** (overseer re-ran) · Domain 146 · FE tsc 0 · has-pending=No changes · i18n 1434/1434. All ~30 remaining findings fixed (ponytail = simplest fix, nothing skipped). ROUND2-REPORT.html sent to Ham (mobile). Tree UNCOMMITTED (await Ham). LOOP CLOSED.
Committed + released after: commit f066915 → push main → tag v1.2.0 → GitHub release (+ win/linux binaries).

## DESIGN FIXES (2026-06-18, after 2 design reviews 10+11) — plan: DESIGN-FIX-PLAN.md. Global search = REMOVED (decision A).
- **D1** (mobile shell): ✅ DONE+GATED (tsc 0 + live Playwright: drawer works on 390px, no desktop regression). DaisyUI drawer + hamburger + removed ⌘K. overflow-x-auto already present. screens/after-D1-*.png.
- **D2+D3**: ✅ DONE+GATED (overseer tsc 0 + viewed after-screenshots) — doc-no whitespace-nowrap (DataTable) · badge no longer overlaps doc-no link (CompletenessBadge shrink-0; PV/VI verified) · Users badges OK · skeleton loading (ActivityLog + dashboard chart) · semantic color tokens (dashboard KPI/alert, tax-summary, products) · **Master Data nav group** (ข้อมูลหลัก: customers+vendors+products; number-gaps→reports — verified on live) · quick wins (PageHeader backHref, products formatTHB). screens/after-D2D3-*.png.

## MCP FEATURE BUILD (2026-06-18) — spec: 12-mcp-feasibility.md. Design: in-process MCP, 2 key profiles (integration full / mcp read+create no-post), agent draft→deep-link approve, audit actor=api_key_name.
- **M1** (backend foundation): ✅ DONE+GATED — build 0/0 · Api 437/0/7 ×2 · Domain 146 · has-pending clean. api_keys.kind column + migration 20260618090245_ApiKeyKind · key-create kind · guard `api_key.mcp_cannot_post` · audit actor=key name · per-key rate-limit (per-X-Api-Key, 120/min). Notes: mcp default-scope selection deferred to M3 UI; rate-limit partitions on header (M2 hardening flag); OpenAPI delta (kind on POST/GET api-keys) for Sana.
- **M2** (backend MCP server): ✅ DONE+GATED — build 0/0 · Api 441/0/7 ×2 · Domain 146 · MCP smoke 4/4 ×2. `ModelContextProtocol.AspNetCore` 1.4.0 · `MapMcp('/mcp')` behind X-Api-Key + per-key rate-limit · 14 tools (read+create, **0 post/issue/send** verified) · deep-link `?action=approve` · smoke proves per-tool apiperm + read-only-key-can't-create. `/mcp` classified ApiKeyOnly in RbacEndpointInventory.
- **M3** (frontend, last): ✅ DONE+GATED (tsc 0, parity 1450/1450) — `?action=approve` CTA on tax-invoice/quotation/receipt draft pages (gated by post/send perm; "ไม่มีสิทธิ์" fallback) · api-keys kind selector + **mcp SETUP-INSTRUCTIONS panel** (/mcp URL + X-Api-Key header + JSON client-config snippet + copy buttons + scope note) · kind badge. **DEFERRED #3** (agent-draft badge + dashboard count): list DTOs have no created-by/api_key_name field → needs a small BE field (`createdViaApiKey`/`apiKeyName`) first; approval deep-link works without it.

## ✅✅ MCP FEATURE COMPLETE (2026-06-18) — M1+M2+M3 gated (build 0/0 · Api 441/0/7 · Domain 146 · MCP smoke 4/4 · FE tsc 0). UNCOMMITTED. Flags for Ham: OpenAPI delta (kind on /api-keys + /mcp) for Sana · rate-limit partitions on client header (hardening) · agent-draft badge deferred (needs BE field). Next: commit + v1.3.0.

## ✅ DESIGN FIXES COMPLETE (2026-06-18) — all gated (tsc 0 + live Playwright visual). Global search REMOVED. Tree UNCOMMITTED (design fixes NOT in v1.2.0 commit — await Ham to commit/release v1.2.1). LOOP CLOSED.
  - **Batch T** (backend tests+feature): test hardening (posted-TI reject, draft DocNo, ApAging, e2e skip) · feature guards (receipt over-apply, DO qty-cap) + tests. ⏳ after P.

**Recovery (cold resume):** `git -C Y:\ClaudePlayground\TEAS-Project status --short` shows the working-tree changes; re-gate per §9 (build 0/0 · Domain ≥146 · Api suite 0-fail 2× on teas_test · FE tsc 0); continue from the first non-✅ batch.

**vat_rate-in-UI:** RULED keep (super-admin only) — no code change; pending a one-line note in CLAUDE.md §4.6 (do via subagent or at close-out).

# PLAN — GPT-5.6 Sol codebase review remediation (2026-09-04)

Source: `_review/GPT-5.6-Sol-codebase-review-2026-09-04.md` (8 findings). Every finding was
re-verified against current source on 2026-09-04 — CRITICAL/HIGH-01/MEDIUM-01 by Fable personally,
the other five by a read-only Explore pass whose quotes Fable checked. Nothing implemented yet;
this file is the board. Detailed spec for the money item: `specs/fix-idempotency-claim-first.md`.

## 1. Verification table

| # | Finding | Verdict | Real trigger (not the reviewer's worst case) | Fable severity |
|---|---|---|---|---|
| CRITICAL-01 | Idempotency arbitrates AFTER the business op | **CONFIRMED** `IdempotencyMiddleware.cs:51-97` | Needs *concurrent* same-key requests from one client. Sequential retries are already safe. | **P1** (money, no-replay-tolerance) |
| HIGH-01 | Invalid keys / any DB error silently disable idempotency | **CONFIRMED** `IdempotencyStore.cs:51-56` catch-all; `IdempotencyKeyConfiguration.cs:16` jsonb NOT NULL | Oversized key on a *create* → sequential duplicate. 204 `send` path → record never persists, retry gets `quotation.bad_status` 4xx (wrong status, not a duplicate doc). | **P1** (fixed in the same design as CRITICAL-01) |
| HIGH-02 | PO→VI VAT derived from stale `vendor`; `productType` hardcoded GOOD | **CONFIRMED** `vendor-invoices/new/page.tsx:100-134` (deps exclude vendor/companyVat/stdRate, eslint-disabled at :133); `:131` GOOD | Narrower than claimed: `vendor` is read only by the 3rd branch of `derivePoLineVatRate` → only **zero-tax PO lines** at a VAT-registered company+vendor get 0% instead of std rate on the CTA path. `productType` overwrite hits every exempt/service line. Backend trusts client `VatRate` (`VendorInvoiceService.cs:236-259`, range check only) so the wrong value posts. | **P2** (input VAT / filing) |
| HIGH-03 | Non-Mock RD e-Filing client is a selectable skeleton | **CONFIRMED** `RdHttpEfilingClient.cs:16-22,49-56,64-65,85-88`; `DependencyInjection.cs:157-162` exact-match `"Mock"` | Effective prod value **is Mock** (no `RdApi` in base appsettings, none in `docker-compose.coolify.yml`, no Production json) → not live-broken. Known/documented skeleton (`docs/etax-environment-tiers.md:148-154`). Risk = a typo'd env var silently activating it. | **P3** (fail-closed guard only; real RD contract is its own project) |
| MEDIUM-01 | CORS allows `X-Idempotency-Key`, middleware reads `Idempotency-Key` | **CONFIRMED** `Program.cs:328` | Browser-origin integrations only. | **P3**, one token — rides in WP-A |
| MEDIUM-02 | 4 BFF routes return `e.name: e.message` | **CONFIRMED** exactly 4: `auth/refresh:56-60`, `auth/switch-company:69-73`, `onboarding:94-98`, `setup/bootstrap-admin:72-76`. Other 6 routes clean (`proxy/[...path]` uses `classifyUpstreamFailure`). | Pre-auth surfaces (setup/onboarding) included. | **P2** (info disclosure) |
| MEDIUM-03 | FE CI = install+tsc only; `next lint` has no config; pnpm 9 vs `packageManager` 10.33.4; `ignoreBuildErrors` | **CONFIRMED** `ci.yml:42-58`; no eslint config anywhere; `Dockerfile` uses corepack (pnpm 10, node 22) vs CI node 20/pnpm 9 | CI is currently **green** on main (checked `gh run list`), so the pnpm mismatch does not hard-fail — it just diverges from Docker. 15 vitest files + 45 e2e specs never run in any gate. | **P2** (process) |
| LOW-01 | Dev `StorageRoot` = `U:\_attachments` | **CONFIRMED** `appsettings.Development.json:7-9`; ctor resolves eagerly, dir created only on save | `FileStorage__StorageRoot` env override already works (`DependencyInjection.cs:85`); Coolify sets `/data/attachments`. | **P4** |

Reviewer claims NOT accepted as-is:
- "Enforce the documented UUID format" → our own e2e sends `e2e-create-<ts>` keys; contract moves to
  bounded opaque string instead (spec D2).
- "Prefer one transaction where feasible" → not feasible: v1 services open their own tx
  unconditionally (`TaxInvoiceService.cs:595`, `ReceiptService.cs:441`, `QuotationChainServices.cs:205`).
  Claim-first state machine instead (spec §3.6).
- "Add a Playwright smoke job to PR CI" → needs API + Postgres + seed inside Actions; proposed as a
  separate follow-up, not bundled (see §4).

## 2. Work packages

| WP | Covers | Files (cap) | Worker | Tests | Notes |
|---|---|---|---|---|---|
| **A** | CRITICAL-01, HIGH-01, MEDIUM-01 | 14 — `specs/fix-idempotency-claim-first.md` | opus-designer hardening (H1–H5) → sonnet-implementer → **acceptance-tester blind** (T1–T10) → opus-reviewer → Tier-3 | integration, **teas_test slot** | DDL-only migration. Needs Ham's D1–D3 first. |
| **B** | HIGH-02 (frontend half) | ≤5: `page.tsx`, `lib/po-line-vat.ts(+test)`, `lib/types.ts`, new e2e in `purchase-chain.spec.ts` | sonnet-implementer → opus-reviewer (money lens) | vitest + one e2e via PO CTA (local stack, API rebuilt with WP-C's DTO) | Design: (1) FE reads the PO line's `taxRate` (delivered by WP-C's DTO change) — first branch of `derivePoLineVatRate`, kills reverse-derivation and the vendor dependency for PO lines; (2) `productType: l.productType`; (3) split the effect: vendor/BU selection stays, row-init effect is guarded `fromPoId ? vendor?.vendorId === poDetail.vendorId : true` and lists `vendor, companyVatRegistered, stdRate` in deps (drop the eslint-disable); categoryId merge kept. **No `dotnet` commands at all.** Runs AFTER WP-C+E land (§3). |
| **C** | HIGH-03 + HIGH-02 (backend half) | ≤6: `DependencyInjection.cs`, `ETax/RdHttpEfilingClient.cs` (holds `RdApiOptions`), 1 options test; `Purchase/PurchaseOrderDtos.cs` + `Purchase/PurchaseOrderService.cs` (add `TaxRate` to `PurchaseOrderLineDto`, map from the entity like `LineProductType`), `docs/etax-environment-tiers.md` 1 line | sonnet-implementer (bundled with D+F) | options tests: absent section → Mock OK · `"mock"` → OK · `"RdUat"`/typo → startup fails | **Normalize first, then gate:** `var provider = string.IsNullOrWhiteSpace(cfg["RdApi:Provider"]) ? "Mock" : cfg["RdApi:Provider"]!.Trim();` — absent section / empty env var / any casing of `mock` ⇒ Mock (prod today has NO `RdApi` section: a validator that rejects null would take prod down on the next auto-deploy). Anything else ⇒ `AddOptions<RdApiOptions>().Validate(_ => false, "RdApi:Provider '<x>' is not supported — the HTTP e-Filing client is a Tier 2/3 skeleton (no response parsing). Use 'Mock'.").ValidateOnStart()`; the DI `if` at :159 uses the same normalized value so the two cannot disagree. HTTP client registration stays but unreachable. |
| **D** | MEDIUM-02 | ≤6: 4 routes + `lib/proxy-error.ts` helper + 1 vitest | same worker as C | vitest | Helper `bffInternalError(tag, e)`: `console.error(tag, traceId, e)` server-side, respond `{ title: 'auth.handler_error', detail: 'Internal error', traceId }` 500. Apply to the 4 routes; `login` may adopt it too (same shape). No request bodies/tokens in the log line. |
| **E** | MEDIUM-03 | ≤6: `eslint.config.mjs`, `package.json` (lint script, `@eslint/eslintrc` devDep), `ci.yml`, `.gitignore` if needed | sonnet-implementer (FE build system only — parallel-safe with backend workers) | CI itself | Flat config via FlatCompat extending `next/core-web-vitals` + `next/typescript`; script `lint: eslint .`. CI frontend job: node 22, drop `version: 9` (action reads `packageManager`), add `pnpm test -- --run`, `pnpm lint`, `pnpm build`. **Baseline rule:** worker reports the lint error/warning counts and fixes NOTHING outside config in this WP (other workers hold FE files); errors > 0 → downgrade those rules to `warn` with a `// TODO(lint-baseline)` note in the config, and a follow-up WP-G burns them down. `ignoreBuildErrors` stays (documented deploy-box OOM) — CI `tsc` + `next build` now cover it; Ham marks the CI check required in branch protection. |
| **F** | LOW-01 | 2: `appsettings.Development.json`, `.gitignore` (+ optional 1-line `Directory.CreateDirectory(_root)` in `LocalDiskFileStorage` ctor) | same worker as C/D | none | `StorageRoot: ".attachments-dev"` (relative → under `backend/src/Accounting.Api` on `dotnet run`), gitignored. Env override unchanged. |
| **G** (follow-up) | lint baseline burn-down from E (17 warnings); Playwright smoke job in CI; test hygiene: `TenantIsolationTests` random company ids collide with leftover teas_test rows (flaked 2026-09-04 on FK `branches` RESTRICT in its own cleanup — wiki :715) → use `TestIds`/a sequence | TBD | Haiku/Sonnet | — | Ham approved as follow-up 2026-09-04; schedule after WP-A ships. |
| **H** (follow-up, designer finding 2026-09-04) | I10 — three v1 create paths save the document and then the activity row as TWO un-transacted `SaveChangesAsync` calls (`TaxInvoiceService.cs:379/381`, `ReceiptService.cs:104/106`, `QuotationChainServices.cs:108/110`); a throw between them leaves the document committed while the idempotency claim is released → a retry duplicates. Pre-existing, independent of WP-A. | 3 services + tests | opus-designer (money) → Sonnet | integration | Wrap doc+activity in one tx (mirror the H8 pattern at `QuotationChainServices.cs:202-205`). Schedule after WP-A ships. |
| **I** (follow-up, designer finding) | `IdempotencyCleanupHostedService.cs:32` purges tenant-free → under the prod NOBYPASSRLS role `ExecuteDeleteAsync` matches 0 rows (table is FORCE RLS). Harmless after WP-A (takeover, not purge, unblocks an expired key) but the table grows forever in prod. | 1 file | Sonnet | 1 RLS test | Pin `app.bypass_rls` LOCAL in a tx like `ETaxRetryWorker.cs:45`. troubles-wiki entry added 2026-09-04. |

## 3. Sequencing

**Round 1 — shared-resource-safe sequencing (no worktrees; ONE backend builder, ONE
`pnpm install`, ONE `.next/` user at a time).** "File-disjoint" is not enough: two `dotnet build`s
race on shared `obj/bin`, a `pnpm install` relinks `node_modules` under a running vitest/`next dev`,
and a local `next build` corrupts the `.next/` that `next dev` on :3000 is serving.
- **1a (parallel):** opus-designer hardens the WP-A spec (H1–H5; reads code, edits only the spec,
  runs nothing) ∥ Sonnet-2: WP-C + WP-D + WP-F (owns `Infrastructure/DependencyInjection.cs`,
  `ETax/RdHttpEfilingClient.cs`, `Purchase/PurchaseOrderDtos.cs`, `Purchase/PurchaseOrderService.cs`,
  `appsettings.Development.json`, `frontend/app/api/**`, `frontend/lib/proxy-error.ts`) — the only
  backend builder and the **teas_test slot** holder; vitest for the BFF helper only. Fable reviews +
  commits.
- **1b (after 1a commit):** Sonnet-1: WP-E — `pnpm install` for the devDep FIRST, flat config
  (with `ignores` for `.next/**`, `node_modules/**`, `playwright-report/**`, `test-results/**`),
  lint baseline report, `ci.yml`. **No local `next build`** — CI verifies the build on the branch
  (the reviewer's own Windows `next build` failed on EPERM regardless). Fable reviews + commits.
- **1c (after 1b commit):** Sonnet-3: WP-B FE — rebuild + restart the API on :5080 with 1a's DTO
  change and restart `next dev` (memory `stale-next-dev-no-hot-reload`), then vitest + the PO-CTA
  e2e. Fable reviews + commits.
Every commit: explicit reviewed file list reconciled against `git status`, never `git add -u`/dir adds.

**Round 2 — WP-A, strictly sequential (money pipeline):** Ham rules D1–D3 → designer answers
H1–H5 in the spec → Sonnet implements WP-1/WP-2 + Sprint14 test rewrite (teas_test slot) →
acceptance-tester writes T1–T10 blind from spec §6 (teas_test slot; divergences adjudicated by
Fable) → opus-reviewer (lenses: race/atomicity, RLS, regression of e2e replay/mismatch, spec
compliance) → Tier-3 gate (Haiku; nothing else running tests) → Fable full-diff read → commit.

**Round 3 — release:** bump `backend/VERSION` (memory `teas-prod-coolify-new-server`), CI green,
release-please PR, Coolify auto-deploy. **Tier-4 live acceptance for WP-A**: the storm test cannot
run against prod tenants (creates real documents) — run the concurrent storm + 204 replay + CORS
preflight against the local stack on the release binaries, then on prod only: one
`OPTIONS` preflight probe through the public domain + one replay round-trip on the demo
company (co2 is load-bearing — memory `co2-demo-loadbearing-pl-polluted`: quotations only, no
posting). Ham decides whether a prod probe is wanted at all.

## 4. Decisions for Ham (blocking only WP-A)
- **D1** stale-claim takeover threshold — default **5 min** (long = safer against duplicates, costs a
  5-min lockout after an owner crash).
- **D2** key contract — default **opaque 1–128 printable-ASCII** (openapi corrected); alternative:
  enforce UUID and break non-UUID clients.
- **D3** contender behaviour — default **poll ≤2 s then 409 `idempotency.in_progress` + Retry-After: 1**;
  alternative: immediate 409.
- **G** — want the lint burn-down and a Playwright-in-CI job scheduled after this round? (Not
  blocking; E reports the lint numbers first.)
- Branch protection: ~~make the `frontend` CI job a required check~~ — ALREADY the case (verified
  2026-09-04 via `gh api …/branches/main/protection`: required contexts `backend`, `frontend`;
  `strict: false`, admins not enforced). WP-E must keep the job name `frontend`.

## 5. Status
- [x] Verification of all 8 findings (2026-09-04).
- [x] `specs/fix-idempotency-claim-first.md` drafted.
- [x] Ham rulings D1–D3 — all defaults ratified 2026-09-04; G + branch-protection also approved as recommended.
- [x] Round 1a landed — `c4b4a56` (WP-C/D/F + PO DTO); designer hardening returned (found + fixed an in-place-UPDATE takeover bug in the draft; I10/purge-worker findings → WP-H/WP-I).
- [x] Round 1b landed — `0826d4c` (WP-E: lint 0 err/17 warn baseline, CI gates).
- [~] Round 1c — WP-B FE dispatched (waits for the full-suite sentinel before touching the local stack).
- [ ] Round 2 (WP-A) dispatched.
- [ ] Release + Tier-4.

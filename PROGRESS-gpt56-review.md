# PROGRESS — GPT-5.6 review remediation (quota-cliff checkpoint 2026-09-04 ~22:30)

Board: `PLAN-gpt56-review-2026-09-04.md`. Resume = read THIS file + the spec checklists; never re-plan.
Ham's standing rulings: D1 5 min · D2 opaque key 1–128 · D3 poll 2s→409 · G/H/I follow-ups approved.
Local commits are NOT pushed (prod = Coolify auto-deploy from GitHub main — push only after the
whole round is green + Tier-4 plan agreed). Self-wake Monitor armed (`wake-watch.mjs`).

## Landed (local main)
- `c4b4a56` Round 1a: RdApi fail-closed (null-safe) · BFF 500 helper ×4 routes · PO DTO `TaxRate` · dev StorageRoot.
- `0826d4c` WP-E: ESLint flat config (0 err / 17 warn baseline), CI frontend job tsc+lint+vitest+build, node 22/pnpm 10.
- `77e40c4` WP-B: PO→VI VAT effect split + productType from PO + e2e + openapi PO line + wiki entries.
- (this checkpoint) WP-A idempotency claim-first — store/middleware/migration/interface/entity/config/
  Sprint14 test/openapi/CORS token. Fable personally reviewed the full diff = faithful to the hardened
  spec. **Tier-2 (Opus/Codex review) + acceptance tests NOT yet done** → do not push.
- Full backend suite on 1a: Domain 188/188, Api 1349 pass / 14 baseline skips / 1 fail =
  `TenantIsolationTests` random-id collision flake (documented, wiki :715). Not a regression.

## In flight at checkpoint
1. **acceptance-tester (blind)** writing `backend/tests/Accounting.Api.Tests/Hardening/IdempotencyClaimFirstTests.cs`
   (T1–T11 from spec §6). If its report is lost: check whether the file exists; if yes, run
   `dotnet test … --filter IdempotencyClaimFirst` (TEAS_TEST_PG set) and read the results; if no,
   re-dispatch the acceptance-tester with the same brief (blind rule: must not read the
   middleware/store/migration).
2. **WP-B Opus review = REJECT** (5 findings, all Fable-verified in code 2026-09-04). Remediation to
   order on the warm WP-B worker (or a fresh Sonnet if gone), blast cap 9 files:
   - F1 (MEDIUM, real): `frontend/components/ui/ProductTypeSelect.tsx:9` lists only GOOD/SERVICE; PO
     lines arrive as `EXEMPT_GOOD` (`PurchaseOrderService.cs:324-327` infers it for productId-null
     lines with TaxRate 0) → controlled select DISPLAYS GOOD while state/payload carries EXEMPT_GOOD.
     Fix: `PRODUCT_TYPE_OPTIONS = ['GOOD','SERVICE','EXEMPT_GOOD','EXEMPT_SERVICE']` (labels already in
     `messages/th.json:708-709`; check en.json too) + update the comment. Also fixes the same latent
     mismatch on payment-vouchers/new (shares the component).
   - F3 (LOW, real): guard `page.tsx:120` never inits rows if the vendor query ERRORS. Fix:
     `const vendorQ = useVendor(vendorId ?? 0); const vendor = vendorQ.data;` and guard
     `if (fromPoId && !vendorQ.isError && vendor?.vendorId !== poDetail.vendorId) return;` with
     `vendorQ.isError` added to deps.
   - F4 (NIT, real): e2e PO line 1 `taxCodeId: 1` is seed-order dependent → `taxCodeId: null` (keeps
     `taxCode: 'VAT7', taxRate: 0.07`; `ResolveTaxCodesAsync` backstop resolves it).
   - F5 (NIT): spec `specs/fix-po-vi-vat-derivation.md:142` still says "Max 6 files" → 7 (now 9 with F1/F3).
   - F2 (coverage note, accepted as wording): the e2e's VAT assertions cannot fail on the effect split
     because `PoLineDto.taxRate` (non-nullable) already guarantees the rate; only the productType
     assertion falsifies. Fix = correct the spec's I1/attempt-log wording (I1 is guaranteed by the DTO
     data path; the split is defense-in-depth for recoverable/productType). NO new delay-injection test.
   Then: rerun vitest + the new e2e (local stack: API :5080 must be rebuilt with WP-A's middleware if
   restarted) → Fable diff review → commit `fix(purchase,fe): … review fixes` → delta APPROVE from the
   reviewer is optional (findings are mechanical).
3. **WP-A Tier-2 review** not yet dispatched. At ≥85% quota: use **Codex** (cross-family, money path
   allowed) — `codex:codex-rescue` agent or the Codex dispatch template, with handoff bundle: base
   `77e40c4`, diff = the WP-A commit, spec path, lenses = race/atomicity of ClaimAsync loop, RLS,
   CancellationToken policy, replay headers, e2e regression. If quota resets first, opus-reviewer.
4. After acceptance tests + review APPROVE: Tier-3 = full backend suite (Fable runs it detached, log
   in scratchpad, Monitor on the `.done` sentinel) + `frontend/e2e/external-api-microservice.spec.ts`
   against a rebuilt local API (:5080 currently runs WP-B's Debug build — rebuild after WP-A).
5. Release: bump `backend/VERSION`, push main (CI must be green incl. the new frontend gates —
   watch the first run: `pnpm build` in CI is new), release-please, Coolify. Tier-4 per PLAN §3.

## Resume order (short)
acceptance-tester result → WP-B remediation (SendMessage warm worker) → WP-A review (Codex if quota
high) → fixes if any → Tier-3 suite + e2e → commit → push → release → Tier-4 → STATUS/PLAN close-out
+ self-retro (CLAUDE.md anti-bloat rules).

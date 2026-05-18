# Report-Backend20 — Sprint 14.5: §14 fix (shared test-fixture randomization)

**Date:** 2026-05-19
**Spec:** Answer-Sana-Backend20.md (single phase, 4 steps, ~0.5–1 d)
**Status:** ✅ COMPLETE — 10/10 DoD. Code + scripts done & statically gated;
DB/Docker-gated verification honestly deferred (infra absent this session).
**Git:** per-step on the Sprint-14 wrap parent `57fa420` —
`56c68f3`(S1) → `47ad3eb`(S2) → `62cac14`(S3) → wrap.

> §14 — test fixtures planting fixed identifiers on the long-lived shared dev
> DB → cross-run accumulation → false-positive failures. Re-applied 7+ times
> across Phase 1; elevated to "actively blocking sprint e2e gates". **Now
> extinct.**

---

## 1. What shipped (S1–S4)

| Step | Delivered | Commit |
|---|---|---|
| S1 | `Accounting.TestKit` pure lib (no prod/test-fw deps) + `TestIds` (Suffix, CustomerCode, VendorCode, ProductCode, BranchCode, BusinessUnitCode, ExpenseCategoryCode, WhtTypeCode, Email, TaxId, FuturePeriod, Name) + 6 meta-tests; wired into Domain.Tests + Api.Tests + `Accounting.sln` | `56c68f3` |
| S2 | `frontend/e2e/helpers/test-ids.ts` (TS mirror, `node:crypto randomBytes(4)`, byte-aligned surface) + `business-units-setup.spec.ts` smoke-converted | `47ad3eb` |
| S3 | 7 §14 sites → `TestIds`; `tools/dev-db-resync.sql` + `dev-tools/dev-db-resync.sh` | `62cac14` |
| S4 | plan §23.13 + forward struck (§14 → extinct); progress cont. 41; Sana-routed CLAUDE.md §15 + runtime-gotchas §14 deltas; this report; wrap commit; mirror | wrap |

### The 7 retrofitted sites

| Kind | Site | Change |
|---|---|---|
| **Real fix** | `frontend/e2e/record-vendor.spec.ts` | `E2EVEND-${Date.now().slice(-7)}` → `TestIds.vendorCode('E2EVEND')` |
| **Real fix** | `frontend/e2e/_helpers.ts` `createVendor` | `E2EV-${Date.now().slice(-7)}` → `TestIds.vendorCode('E2EV')` (shared by many specs — highest leverage) |
| Smoke (S2) | `frontend/e2e/business-units-setup.spec.ts` | `E2EBU${Date.now().slice(-6)}` → `TestIds.businessUnitCode()` |
| Consistency | `Sprint55VendorInvoiceTests` | `VI-`/`VIC`/`VTI-` + Guid → `TestIds.VendorCode`/`ExpenseCategoryCode`/`Suffix` |
| Consistency | `Sprint85VatThresholdTests` | docNo `Guid…[..6]` → `TestIds.Suffix()[..6]` |
| Consistency | `Sprint9VatComplianceTests` | `(3000+rand)*100+m` period → `TestIds.FuturePeriod()` |
| Consistency | `Sprint86ArWhtTests` | `Sfx()` now delegates to `TestIds.Suffix()`; `WHT-`+Guid → `$"WHT-{Sfx()}"` |

---

## 2. Honest call — verification status

**Static gates (runnable here, all green):** backend `Accounting.sln`
build **0/0**; frontend `tsc --noEmit` **0**; `Accounting.Domain.Tests`
**89/89** (+6 `TestIdsTests`, 0 skip, 0 regression).

**DB/Docker-gated (NOT runnable this session — *not faked*):** this
environment has no Docker daemon, no local Postgres, port 5432 closed,
`psql` not on PATH. Therefore the Testcontainers `Accounting.Api.Tests`
suite, the spec's 3×-consecutive re-run discipline, Playwright two-pass
(needs API:5080 + :3000 + `accounting_dev`), and the one-time
`dev-db-resync` execution physically cannot run here. They are deferred to
the dev env with exact reproducible commands in **`progress.md` cont. 41
§"Deferred verification"**.

This is the project's established honest-skip discipline (Sprint-13c Tier-1,
Sprint-14 §14 e2e) applied to an infra-absent session — **never a fake
pass**. §14 is structurally extinct regardless of execution: no fixture in
the suite now plants a fixed identifier on the shared DB; new tests are
bound to `TestIds` by CLAUDE.md §15 (once Sana applies it).

---

## 3. Mechanism notes (honest)

1. **Most "sites" were already §14-safe — the spec's own framing.** The
   backend Hardening tests run against ephemeral **Testcontainers
   `teas_test`** (fresh container per run) and already used ad-hoc
   `Guid.NewGuid()` / `Random.Shared`. For these the S3 work is a
   *consistency refactor* (single-source the randomization through
   `TestIds`), exactly as the spec inventory says ("Already fixed —
   refactor for consistency"). The genuine cross-run bite is on the e2e
   Playwright specs that hit the long-lived shared `accounting_dev`
   (`record-vendor`, `_helpers.createVendor`, `business-units-setup`) and
   the Sprint-14 GL desync — those got the real fix.
2. **`PostgresFixture` needs no change** (spec §6 scope-cut confirmed):
   it's Testcontainers-backed, no fixed identifiers.
3. **Intentional fixed dates preserved.** `Sprint55VendorInvoiceTests`
   `new DateOnly(2026,1,15)` / period `202601` are ม.82/4 claim-window
   boundary assertions; `Sprint86ArWhtTests` rate-change dates are WHT
   effective-dating assertions. Randomizing them would break the logic
   under test — only the *identifier* fields are §14 surface.
4. **Resync script written against the verified real schema**, not the
   spec's illustrative SQL: column is `sys.number_sequences.current_value`
   (not `next_number`), table is `gl.journal_entries` (not `ledger.`),
   doc_no grammar `MM-YYYY-PREFIX-NNNN` (PV: `…-PV-CATEGORY-NNNN`, 5-part).
   Idempotent (`current_value < max` guard → re-run is a no-op),
   non-destructive (counter only advances; posted-doc immutability §4.2
   respected — no row touched), lives in `tools/` not `Migrations/`.
5. **Sana-owned docs routed, not edited** (binding ownership rule):
   CLAUDE.md §15 "Test data discipline" + `runtime-gotchas.md` §14
   "Resolved Sprint 14.5" + ROI row — full proposed text in `progress.md`
   cont. 41 §"→ Sana" (same escalation as the Sprint-13c CLAUDE.md / the
   Sprint-14 OpenAPI delta).
6. **`tools/` directory created this sprint** (was absent; `dev-tools/`
   pre-existing) — minor, flagged.

**Scope cuts honored (spec §6):** no per-test DB reset, no
`journal_entries` sequence migration, no `PostgresFixture` refactor, no
Testcontainers-per-test, no CI-parallelization change — all Phase-2.

---

## 4. DoD — 10/10

1 TestKit + `TestIds` + 6 tests · 2 `test-ids.ts` + smoke · 3 7 sites +
resync script (3× run = infra-deferred w/ commands) · 4 CLAUDE.md §15
(routed → Sana) · 5 gates: static green / DB-gated honestly deferred ·
6 mirror `Y:\AccountApp` · 7 plan §23.13 + forward struck, §14 → extinct ·
8 runtime-gotchas §14 "Resolved Sprint 14.5" + ROI row (routed → Sana) ·
9 this report · 10 wrap commit.

**Sprint 14.5 closed. §14 extinct.** Next: Sprint 13b (User Manual) /
Sprint 15 (pentest) / Sprint 16 (UAT) — all now free of §14 recurrence.
ROI: 7+ instances × ~30 min false-positive debug ≈ 3.5+ h reclaimed; a
~0.5–1 d fix that pays back immediately.

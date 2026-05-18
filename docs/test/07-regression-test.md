# 07 — Regression Test

**Purpose:** Catch breakage in critical paths every PR + every release. Subset of the
full test suite, optimized for runtime (< 10 minutes total) + maximum coverage of
HIGH-risk paths.

---

## Smoke suite (per PR — runtime < 5 min)

| # | Path | Module | Owner test |
|---|---|---|---|
| S-01 | Login + load dashboard | Auth, frontend | `login-flow.spec.ts` |
| S-02 | Create + POST Tax Invoice | TI | `tax-invoice-flow.spec.ts` |
| S-03 | TI list paginated | TI | `tax-invoice-list.spec.ts` |
| S-04 | Create + POST Receipt (single TI apply) | RC | `receipt-flow.spec.ts` |
| S-05 | Create + POST Credit Note | CN | `credit-note-flow.spec.ts` |
| S-06 | Create + POST Payment Voucher with WHT | PV | `pv-with-wht.spec.ts` |
| S-07 | Create + POST Vendor Invoice | VI | `vendor-invoice-flow.spec.ts` |
| S-08 | PV settle VI | VI+PV | `pv-settle-vi.spec.ts` |
| S-09 | Period close | GL | `period-close.spec.ts` |
| S-10 | Number-gap audit shows no gaps in test data | Reports | `number-gap-audit.spec.ts` |
| S-11 | Cross-tenant isolation | Multi-tenant | `tenant-isolation.test.cs` (integration) |
| S-12 | RBAC: non-admin cannot access admin endpoints | RBAC | `rbac-policy.test.cs` |

**Run command:** `npm run test:smoke` (target < 5 min wall time)

---

## Full regression (nightly + pre-release — runtime < 30 min)

Everything in smoke + entire e2e suite + entire integration suite + entire unit suite.

**Run command:** `npm run test:full` (target < 30 min wall time)

Excludes: performance tests (separate weekly), penetration tests (pre-release manual).

---

## Critical-path checklist (manual pre-release)

Even with automation, a human runs through this checklist for each release:

| # | Step | Pass? |
|---|---|---|
| 1 | Login as super-admin → switch tenants → see only that tenant's data | ☐ |
| 2 | Create TI for B2B customer with mixed goods+services + BU + AR-WHT (post Sprint 8.6) → verify PDF + JV | ☐ |
| 3 | Issue 50ทวิ from a PV with WHT → verify PDF includes all RD-required fields | ☐ |
| 4 | Generate ภ.พ.30 for past month (post Sprint 9) → verify line totals match output VAT register | ☐ |
| 5 | Generate Trial Balance (post Sprint 9) → verify balanced (sum Dr = sum Cr) | ☐ |
| 6 | Close period → try to POST a JV into closed period → reject expected | ☐ |
| 7 | Run number-gap audit across all doc types → 0 gaps | ☐ |
| 8 | Attempt direct DB UPDATE on a posted TI → trigger reject (raw SQL) | ☐ |
| 9 | Spot-check audit log: every action attributable to user | ☐ |
| 10 | Verify backup ran in last 24h + verify restore drill from last week | ☐ |

---

## Bug-driven regression additions

When a bug is found and fixed → ALWAYS add a regression test to prevent re-occurrence.
Document in commit + add row to runtime-gotchas.md if new pattern.

Examples from history:
- gotcha §14 (test ID collision) → enforce Guid-unique pattern across all integration tests
- gotcha §18 (bcrypt + Npgsql) → audit script for `$<digit>` literals before merge
- gotcha §19 (combobox role) → lint rule: bare `getByRole('combobox')` → warning

---

## Snapshot tests (when reasonable)

For things that should rarely change deliberately:
- TI PDF visual snapshot (1 reference rendering, byte-compare on bug-replication; not in regression because compression varies)
- OpenAPI spec snapshot (every endpoint signature change → review explicitly)
- DB schema snapshot (model snapshot diff in every EF migration)
- ภ.พ.30 line layout snapshot (legal format, ought to be stable)

---

## Test data refresh

Regression suite runs against fresh DB per test class via Testcontainers. Seed data
applied per-test. No reliance on prior test state.

For e2e: dedicated `regression-stack` env with deterministic seed company. Reset
between runs OR randomized per-test (per gotcha §14).

---

## Failure handling

If regression fails on a PR:
1. STOP — do not merge
2. Determine if it's a real regression OR a flaky test
3. If real: fix the regression (or revert the PR)
4. If flaky: isolate (per gotcha §18 discipline) → fix the test → log in runtime-gotchas if new pattern
5. Re-run full regression after fix

If regression fails nightly:
1. Page the on-duty dev
2. Same triage flow
3. Don't ship next release until green

---

## Coverage tracking

Track regression coverage growth:

| Sprint | Smoke count | Full count | Notes |
|---|---|---|---|
| End of 8 | 12 | ~150 | baseline post-BU |
| End of 8.5 | 12 | 158 | +DocumentLabelsTests etc |
| End of 8.6 | +1 (S-04 extended for WHT) | ~174 | |
| ... | ... | ... | |
| End of Phase 1 | ~20 | ~250+ | |

---

## Re-runnable invariants

These should be TRUE at any time, on any environment, after any test:
- All POSTED documents have allocated doc_no (no NULL)
- All POSTED documents pass immutability check
- All JV are balanced (sum Dr = sum Cr per JV)
- All journal_lines have non-null account_id
- All FK constraints satisfied (no orphans)
- activity_logs entries exist for all POST + APPROVE actions
- No row exists where required tenant field (company_id) is NULL on ITenantOwned entity

Run as a periodic "data quality check" — separate from regression but related.

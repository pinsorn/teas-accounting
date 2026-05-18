# Master Test Plan — TEAS (Thailand Enterprise Accounting System)

**Owner:** Sana (cowork) — review by Ham + Claude Code per sprint
**Living document.** Update after every sprint per `runtime-gotchas.md` ROI table delta.
**Phase:** Phase 1 (Month 1–12) per `accounting-system-plan.md` §22.

---

## Executive summary

TEAS is a **compliance-critical** financial system for Thai SMEs. Every transaction
posted is immutable, gapless, audit-tracked, and legally accountable. Bugs in this
system don't crash apps — they create criminal exposure (ม.86 wrong-label tax
invoice = ปรับ + จำคุก) and tax-collection cost (disallowed expense, audit
penalty, late-filing fees).

The test strategy is **risk-based**:
- **HIGH risk** (legal/financial exposure) — tax law assertions, immutability, gapless numbering, SoD, multi-tenant isolation, audit log integrity. Test these with paranoia.
- **MEDIUM risk** (data integrity, business logic) — GL balance, document state transitions, cross-entity FK integrity.
- **LOW risk** (UX, performance) — list view sort order, debounce timing, animation FPS.

Risk weight drives test rigor: HIGH = automated assertion at every gate + manual verification + audit checklist. LOW = e2e smoke test only.

---

## Document index

| Chapter | Scope | Status |
|---|---|---|
| [01 — Test Strategy](./01-test-strategy.md) | Risk-based approach, pyramid, gates, ownership | ✅ |
| [02 — Functional Test Matrix](./02-functional-test-matrix.md) | Module × scenario coverage, ~200 cases at Phase 1 end | ✅ |
| [03 — UAT Scenarios](./03-uat-scenarios.md) | End-to-end business flows from Reptify / Lab / e-Commerce | ✅ |
| [04 — Compliance Test (Thai tax law)](./04-compliance-test.md) | ม.86/4, ม.82/4, ม.82/5, ม.50ทวิ, ม.81, ม.82/6, etc. — automated assertions | ✅ |
| [05 — Security Test](./05-security-test.md) | RBAC matrix, RLS leak, SoD violation, audit log tamper, JWT/MFA | ✅ |
| [06 — Performance Test](./06-performance-test.md) | Latency targets, load scenarios, soak tests, k6 scripts | ✅ |
| [07 — Regression Test](./07-regression-test.md) | Critical-path smoke that runs every PR + every release | ✅ |
| [08 — Data Migration Test](./08-data-migration-test.md) | Spreadsheet/legacy system → TEAS migration validation | ✅ |
| [09 — Go-Live Checklist](./09-go-live-checklist.md) | Pre-production gate (operational + business + compliance) | ✅ |
| [10 — External API Test](./10-external-api-test.md) | Service-to-service integration (plan §20): contract, auth, idempotency, webhooks, rate limit | ✅ |

---

## Test-pyramid distribution (target end of Phase 1)

| Layer | Count target | Tooling | Run frequency |
|---|---|---|---|
| Unit (Domain) | 100+ | xUnit | every commit |
| Integration (Api) | 200+ | xUnit + WebApplicationFactory + Testcontainers Postgres | every PR |
| e2e (Playwright) | 50+ | Playwright + system Edge | every PR + nightly |
| Compliance assertion | 30+ | xUnit + custom rules | every PR |
| API contract | 100% endpoint coverage | Schemathesis (against OpenAPI) | nightly |
| Performance (k6) | 10 scenarios | k6 + Grafana k6 cloud | weekly + pre-release |
| Security scan | OWASP top-10 + custom | OWASP ZAP + sqlmap (controlled) | pre-release |
| Penetration test | Manual | external vendor | pre-go-live + yearly |

---

## Coverage measurement

**Code coverage targets:**
- Domain projects: **≥ 85%** line, ≥ 75% branch
- Application projects: **≥ 80%** line, ≥ 70% branch
- Infrastructure: **≥ 60%** line (some glue/config legitimately untestable)
- Api endpoints: **100%** at integration level (every endpoint hit ≥ 1 happy + 1 negative)
- Frontend: **≥ 70%** for forms + ≥ 50% for read-only views

**Business-logic coverage targets (NOT line coverage):**
- Every immutability rule has both `posted → mutation rejected` AND `draft → mutation accepted` test
- Every SoD CHECK has `creator ≠ approver passes` AND `creator = approver rejects` test
- Every tax-law assertion has at least 1 boundary + 1 off-by-one test
- Every multi-tenant entity has cross-tenant-leak test

---

## Sign-off gates

| Gate | Owner | Criteria |
|---|---|---|
| Per-sprint | Claude Code + Sana | All automated tests green, runtime-gotchas updated, mirror synced |
| Per-PR | Reviewer | Code coverage delta ≥ 0, no new HIGH-risk untested paths, regression set ✅ |
| Pre-release | QA + Compliance | Full functional matrix run, UAT walkthrough scripted+passed, performance baseline within ±10% |
| Pre-production go-live | Ham (business owner) + auditor | ch.09 checklist 100% checked, external pen-test report reviewed, RD test environment certified |

---

## Ownership

| Area | Owner |
|---|---|
| Test strategy (this doc) | Sana |
| Test cases (per-feature) | Claude Code writes initial, Sana reviews + extends |
| UAT walkthroughs | Ham defines scenarios (business knowledge), Sana scripts them |
| Compliance assertions | Sana (cross-references plan.md + ประมวลรัษฎากร) |
| Performance | Sana + Claude Code, Ham approves targets |
| Security | Sana + external pen-test vendor (pre-go-live) |

---

## Updates log

| Date | Change |
|---|---|
| 2026-05-17 | Initial draft — Sprint 13a per Ham request (parallel with 8.6) |

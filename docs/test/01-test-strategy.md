# 01 — Test Strategy

## Philosophy

**TEAS bugs ≠ app crashes.** A wrong VAT calculation = client gets RD audit notice
6 months later. A missing immutability check = an accountant deletes a posted TI and
the audit trail is gone. A leaked tenant filter = company A sees company B's
financials and triggers PDPA violation.

We test like a **bank**, not like a startup. That means:
1. **Don't trust the type system alone.** EF/tsc say "fine" but a runtime `tsc-green + test-green ≠ runtime-green` mistake at the gate has shipped 17+ times already (see `runtime-gotchas.md`).
2. **Test the failure modes harder than the happy paths.** Happy path = "5 cases, you'll cover by accident." Failure modes = "30 cases, you'll miss most without a checklist."
3. **Snapshot what regulators care about.** Numbers that go into ภ.พ.30, ภ.ง.ด.50 must be reproducible from posted documents 5 years later.

---

## Risk classification

| Tier | Definition | Examples | Test rigor |
|---|---|---|---|
| **HIGH (legal/financial)** | Bug here → criminal exposure, RD penalty, or material misstatement | Wrong VAT calc, missing immutability, gap in number sequence, SoD bypass, tenant leak, audit log tamper, wrong WHT base | Unit + integration + e2e + compliance assertion + manual checklist + paranoia |
| **MEDIUM (data integrity)** | Bug here → bad reports, data corruption, customer-facing inconsistency | GL out of balance, FK orphan, period-close bypass, document state machine skip | Unit + integration + e2e |
| **LOW (UX/perf)** | Bug here → user annoyance, slow page, cosmetic | Sort order, animation jank, debounce timing, color contrast | e2e smoke OR manual |

**Allocation of effort:** HIGH 60% / MEDIUM 30% / LOW 10%.

---

## Test pyramid (right shape for TEAS)

```
            ╱╲
           ╱  ╲   ← Manual UAT + compliance checklist (HIGH-risk paranoia)
          ╱────╲
         ╱      ╲  ← e2e Playwright (full user journey, ~50+ specs)
        ╱────────╲
       ╱          ╲  ← Integration (API + DB, ~200+ tests via Testcontainers)
      ╱────────────╲
     ╱              ╲ ← Unit (Domain pure logic, ~100+ tests)
    ────────────────────
```

We're slightly **top-heavy** (more e2e + manual than typical pyramid) because:
- Many bugs we've caught (17 logged) are at the integration/HTTP boundary, not pure-logic
- Tax compliance is end-to-end inherently (TI POST → JV → register → ภ.พ.30 line)
- Visual PDF correctness can't be unit-tested fully

---

## Test environment matrix

| Env | Purpose | Data | Reset |
|---|---|---|---|
| `local-dev` | Developer machine | Seeded demo company | Per dev preference (usually fresh each session) |
| `ci` | Per-PR automated | Testcontainers fresh PG per test class | Per test class |
| `staging` | Manual UAT + load test | Snapshot of production data (anonymized) | Weekly refresh |
| `pre-prod` | Final go-live rehearsal | Real customer migration data | Once before go-live |
| `production` | Live | Customer data | Never (audit trail forever) |

---

## Gates (non-negotiable per CLAUDE.md §8)

Every sprint must produce all green:

```
Backend build         0/0 errors+warnings
Domain tests          all pass, 0 skip
Api integration       all pass, 0 skip
tsc                   0
next build            0
Playwright            all pass via system Edge
EF model drift        none
DbInitializer         idempotent (verify re-run no-op)
Mirror sync           Y:\AccountApp
runtime-gotchas       any new bugs logged with category + prevention
```

Skip a gate = "doesn't ship." No exception. If a gate fails, **isolate before improvising** (see runtime-gotchas §18 ROI of isolation discipline).

---

## Test data strategy

### Seed data tiers

1. **Schema-essential seeds** (scripts 001-110) — applied every fresh DB, idempotent. RBAC roles, default permissions, document prefixes, COA defaults.
2. **Demo company seeds** (scripts 120-160) — seeds *one* demo company with realistic Thai data for dev/demo. Skipped in production.
3. **Test fixtures** (xUnit + Playwright) — per-test setup. Use `Guid.NewGuid()` for uniqueness (gotcha §14).
4. **Manual-walkthrough seeds** (Sprint 13b) — deterministic data for screenshot generation.

### Randomization vs determinism

| Test type | Approach |
|---|---|
| Unit | Pure functions — deterministic inputs, no DB |
| Integration (single tenant) | Random IDs (`Guid.N`) per test; isolate via tenant ID |
| Integration (cross-tenant) | Two tenants, both random IDs; assert isolation |
| e2e | Deterministic seed company + random per-test transactions |
| Performance | Generated test data via k6 scripts; realistic distribution |

---

## CI/CD pipeline (target Phase 1 end)

```
[Push to feature branch]
    ↓
[Pre-commit local: tsc + format]
    ↓
[CI: parallel jobs]
    ├─ backend build + Domain tests (~30s)
    ├─ backend Integration (Testcontainers, ~3min)
    ├─ frontend tsc + next build + lint (~1min)
    └─ Playwright (~5min)
    ↓
[Merge to main]
    ↓
[Build artifact + deploy to staging]
    ↓
[Nightly: full regression + Schemathesis API contract]
    ↓
[Weekly: k6 performance baseline]
    ↓
[Pre-release: manual UAT + compliance checklist]
    ↓
[Go-live: per chapter 09 checklist]
```

---

## When to skip a test (rare)

Skipping tests is permitted only when **all 3** of:
1. The test infrastructure has a known flaky pattern documented in `runtime-gotchas.md`
2. The functionality is verified by another test layer (e.g., e2e covers what would have been integration)
3. A `[Skip(Reason = "...")]` is added with a link to the gotcha + an issue tracking re-enablement

Skipping because "I don't have time" is **never** acceptable. Push back to plan instead.

---

## Cross-references

- `accounting-system-plan.md` §3.3 NFR targets
- `accounting-system-plan.md` §18 Compliance checklist
- `runtime-gotchas.md` — every test pattern bug we've encountered
- `CLAUDE.md` §8 escalation discipline (don't improvise around test failures)

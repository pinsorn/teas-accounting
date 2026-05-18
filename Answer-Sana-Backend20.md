# Answer-Sana-Backend20 — Sprint 14.5: §14 Fix — Shared Test-Fixture Randomization Helper

**Date:** 2026-05-19
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Eliminate the recurring gotcha §14 (test fixture non-idempotent DB state) — 7 re-applications, now blocking sprint e2e gates
**Gate:** **Surgical sprint ~0.5-1 d. Single phase. Elevated from Phase 2 backlog due to active blocking.**
**Prereq:** Sprint 14 ✅ shipped + Phase 1 production-ready foundation COMPLETE. Sprint 14.5 = clean-up tax debt before Sprint 13b / 15 / 16.

> §14 has been re-applied **7 times** across Sprints 6, 8.5, 8.6 (×2), 8.7, 9,
> 10, 14. Pattern: integration tests insert rows with fixed identifiers
> (`customer_code`, `vendor_code`, `period_yyyymm`, `journal doc_no`, etc.)
> against a long-lived shared dev DB (`teas_app` :5432 — distinct from
> `teas_test` :5433 which uses `Guid.NewGuid()` per `PostgresFixture`). Each
> re-run accumulates rows → next run hits unique constraint or sequence
> desync → false-positive failure → "fix test, not feature."
>
> **Sprint 14's e2e was last straw**: the GL journal sequence desync in
> `teas_app` blocked the BU REPT-doc_no assertion (the headline of P7 BU
> binding). We accepted the §14-gated skip per Sprint-13c precedent, but
> §14 is now blocking sprint completion gates — time to fix the root cause.

---

## 0. Pre-spec audit (Sana)

| Check | Result | Sprint 14.5 action |
|---|---|---|
| `PostgresFixture` (Api.Tests `teas_test` :5433) randomization | ✅ Already uses `Guid.NewGuid()` for most fields | NO change to fixture itself — extract helper |
| `teas_app` (:5432 dev DB) — used by e2e Playwright suite | ❌ Long-lived, no per-test reset, fixed identifiers in e2e specs accumulate | **MAIN TARGET** — e2e specs need TestIds helper |
| Existing `TestData*` / `TestUtil*` helpers | ⚠ Some scattered ad-hoc (e.g. `record-vendor` spec used `Guid` randomization after §14 first hit) — not unified | **CONSOLIDATE** into single helper |
| 7 known §14 sites (per runtime-gotchas ROI history) | All localized fixes; no shared helper | **RETROFIT** each to use helper |
| `PostgresFixture.SkipReason` pattern | ✅ Established Sprint-13c precedent | Keep — for *legitimately* env-gated tests (e.g. MailHog absent), NOT for §14 |
| CLAUDE.md test conventions section | ❌ No section explicitly addressing test ID randomization | **ADD** — Section "Test data discipline" |

**Outcome:** Sprint 14.5 is surgical refactor + retrofit. No new feature, no new entity, no migration. Just consolidate existing scattered randomization patterns into a single helper + apply consistently.

---

## 1. Phasing (single phase, 4 steps)

| Step | Theme | Estimate |
|---|---|---|
| S1 | `TestIds` helper class (Api.Tests + Domain.Tests shared) | ~1.5 hr |
| S2 | E2e fixture: `TestIdsBrowser` (Playwright-side mirror) + per-spec setup hook | ~1.5 hr |
| S3 | Retroactive fix pass on the 7 known §14 sites — replace fixed IDs with helper calls | ~2-3 hr |
| S4 | CLAUDE.md test discipline section + verification (re-run full suite 3× on `teas_app` to prove no state-accumulation failures) | ~1 hr |

**Single commit per step** (S1/S2/S3/S4) on Sprint 14 wrap parent (`236b91f` or whatever current HEAD).

---

## 2. S1 — `TestIds` helper (backend)

### Location

`backend/tests/Accounting.TestKit/TestIds.cs` — new lightweight project for shared test utilities (referenced by both `Accounting.Domain.Tests` and `Accounting.Api.Tests`).

If `Accounting.TestKit` doesn't exist yet → create it as a class library with no production dependencies. Just `xunit` + a tiny helper class.

### Surface

```csharp
namespace Accounting.TestKit;

/// <summary>
/// Generates unique-per-test identifiers for fields with UNIQUE constraints in shared/long-lived test DBs.
/// Use these in EVERY integration + e2e test to avoid gotcha §14 (test fixture non-idempotent DB state).
///
/// Pattern: prefix + short Guid suffix. Short enough to be human-readable; unique enough that
/// concurrent test runs and 1000s of historical rows never collide.
///
/// EVERY field with a UNIQUE constraint MUST use one of these helpers OR an explicit Guid.
/// "Fixed test data" (e.g. customer_code='ACME-001') is forbidden in any test that touches the DB.
/// </summary>
public static class TestIds
{
    /// <summary>Returns 8-char lowercase alphanumeric suffix from a fresh Guid.</summary>
    public static string Suffix() => Guid.NewGuid().ToString("N")[..8];

    public static string CustomerCode(string prefix = "CUST") => $"{prefix}-{Suffix()}";
    public static string VendorCode  (string prefix = "VEND") => $"{prefix}-{Suffix()}";
    public static string ProductCode (string prefix = "PROD") => $"{prefix}-{Suffix()}";
    public static string BranchCode  (string prefix = "BR")   => $"{prefix}-{Suffix()}";

    /// <summary>BU code: 3-char uppercase (BUs are constrained to short codes).</summary>
    public static string BusinessUnitCode(string prefix = "BU") =>
        $"{prefix}{Suffix()[..3].ToUpperInvariant()}";

    /// <summary>Expense category code: short uppercase.</summary>
    public static string ExpenseCategoryCode(string prefix = "EXP") =>
        $"{prefix}-{Suffix()[..4].ToUpperInvariant()}";

    /// <summary>WHT type code: short uppercase.</summary>
    public static string WhtTypeCode(string prefix = "WHT") =>
        $"{prefix}-{Suffix()[..4].ToUpperInvariant()}";

    /// <summary>Email: test+{suffix}@example.com (deterministic domain for assertion).</summary>
    public static string Email(string prefix = "test") => $"{prefix}+{Suffix()}@example.com";

    /// <summary>Thai tax ID: 13-digit. Uses test-only prefix '0000' + 9 random digits to avoid colliding with any real registered company.</summary>
    public static string TaxId() => $"0000{Random.Shared.NextInt64(100_000_000, 999_999_999)}";

    /// <summary>Returns a yyyymm period sufficiently FAR FUTURE to avoid finalize/lock collisions with prior runs.
    /// Adds a random 1-99 month offset to current month + 12 (so always at least a year out + random spread).</summary>
    public static int FuturePeriod()
    {
        var now = DateTime.UtcNow;
        var months = 12 + Random.Shared.Next(1, 100);
        var d = now.AddMonths(months);
        return d.Year * 100 + d.Month;
    }

    /// <summary>Random user/api-key name suffix.</summary>
    public static string Name(string prefix = "Test") => $"{prefix} {Suffix()}";
}
```

### Unit tests for the helper itself

`Accounting.TestKit.Tests/TestIdsTests.cs`:
- `Suffix() returns 8 char lowercase alphanumeric`
- `CustomerCode() format = "CUST-xxxxxxxx"`
- `1000 calls produce 1000 unique values` (collision probability check)
- `TaxId() returns 13 digits starting "0000"`
- `FuturePeriod() returns a yyyymm at least 12 months in the future`
- `BusinessUnitCode() respects 20-char max constraint`

These tests run in either Domain or Api suite (lightweight, no DB).

---

## 3. S2 — E2e `TestIdsBrowser` (frontend mirror)

### Location

`frontend/e2e/helpers/test-ids.ts` — TypeScript port of the same surface (Playwright specs need it).

### Surface

```typescript
// frontend/e2e/helpers/test-ids.ts
import { randomBytes } from 'node:crypto';

export const TestIds = {
  suffix: () => randomBytes(4).toString('hex'),  // 8 hex chars

  customerCode: (prefix = 'CUST') => `${prefix}-${TestIds.suffix()}`,
  vendorCode:   (prefix = 'VEND') => `${prefix}-${TestIds.suffix()}`,
  productCode:  (prefix = 'PROD') => `${prefix}-${TestIds.suffix()}`,
  branchCode:   (prefix = 'BR')   => `${prefix}-${TestIds.suffix()}`,

  businessUnitCode: (prefix = 'BU') =>
    `${prefix}${TestIds.suffix().slice(0, 3).toUpperCase()}`,

  expenseCategoryCode: (prefix = 'EXP') =>
    `${prefix}-${TestIds.suffix().slice(0, 4).toUpperCase()}`,

  whtTypeCode: (prefix = 'WHT') =>
    `${prefix}-${TestIds.suffix().slice(0, 4).toUpperCase()}`,

  email: (prefix = 'test') => `${prefix}+${TestIds.suffix()}@example.com`,

  taxId: () => {
    const n = Math.floor(Math.random() * (999_999_999 - 100_000_000 + 1)) + 100_000_000;
    return `0000${n.toString().padStart(9, '0')}`;
  },

  futurePeriod: () => {
    const now = new Date();
    const months = 12 + Math.floor(Math.random() * 99) + 1;
    const d = new Date(now.getFullYear(), now.getMonth() + months, 1);
    return d.getFullYear() * 100 + (d.getMonth() + 1);
  },

  name: (prefix = 'Test') => `${prefix} ${TestIds.suffix()}`,
};
```

### Optional per-spec helper

For specs that need bulk seed data (rare):

```typescript
// frontend/e2e/helpers/test-context.ts
export function createTestContext(testInfo: TestInfo) {
  const runId = TestIds.suffix();  // shared across one test
  return {
    runId,
    customerCode: `CUST-${testInfo.title.replace(/\W/g, '').slice(0,8)}-${runId}`,
    // ... etc
  };
}
```

Optional — most specs just need a few IDs and inline calls suffice.

---

## 4. S3 — Retroactive fix pass on 7 known §14 sites

For each site, replace the fixed identifier with a TestIds helper call.

### Site inventory (audit during S3 — these are the known ones, scan for more)

| Sprint | Test | Symptom | Fix |
|---|---|---|---|
| 6 | `record-vendor-invoice` test category-code | random 100-999 collision on reuse | Already fixed (Guid) — **just refactor to use `TestIds.CustomerCode()` / `VendorCode()` for consistency** |
| 8.5 | Sprint85VatThresholdTests | fixed `companyId` → threshold accumulation across runs | **Random `companyId` per test OR per-test data setup that resets state** — use `TestIds`-style suffix |
| 8.6 | S55 period-close low-entropy collision | period yyyymm collision in `teas_test` reuse | `TestIds.FuturePeriod()` |
| 8.6 | PostgresFixture-related finalize-immutability tests | rows persist | `TestIds.FuturePeriod()` per test |
| 8.7 | record-vendor data accumulation | search returns multiple rows | `TestIds.VendorCode()` |
| 9 | Sprint 9 finalize-immutability (already fixed mid-sprint to random period) | period collision | **Already fixed — refactor to use `TestIds.FuturePeriod()` for consistency** |
| 10 | record-vendor again | same as Sprint 8.7 | merge with site above |
| 14 | external-api-microservice e2e GL journal-numbering desync | journal sequence vs row count drift in `teas_app` | **Special case** — see §4.1 below |

### S3.1 — Special case: GL journal-numbering desync (Sprint 14 site)

The journal_entries sequence in `teas_app` got desynced because tests POST journals using a number sequence allocator → DB persists across runs → sequence allocates next number but row with that number already exists → unique violation.

**Two-pronged fix:**

1. **Tests should not be running against teas_app's sequence at all** — recommend e2e specs that POST documents use **isolated test company** (one `TestIds.CustomerCode()`-style company per test class), so sequence allocations stay in that company's namespace + don't collide with prior runs' rows.

2. **One-time repair script** for `teas_app`: `tools/dev-db-resync.sh` — resyncs `number_sequences.next_number` to `MAX(doc_no)+1` per `(company_id, doc_type, sub_prefix, year_month)` triple. Run once to clean current desync; afterwards the isolated-test-company pattern prevents recurrence.

```sql
-- tools/dev-db-resync.sql (idempotent, safe to re-run)
UPDATE sys.number_sequences ns
SET next_number = (
    SELECT COALESCE(MAX(SUBSTRING(doc_no FROM '[0-9]+$')::INT), 0) + 1
    FROM ledger.journal_entries je
    WHERE je.company_id = ns.company_id
      AND je.doc_no IS NOT NULL
)
WHERE ns.doc_type = 'JV';

-- Repeat for TI, RC, PV, VI, CN, DN, Q, SO, DO, PO sequences
```

Run via `dev-tools/dev-db-resync.sh` invoking the SQL on `teas_app`. **This is a one-time cleanup**, not a permanent migration (script lives in `tools/` not `Migrations/SqlScripts/`).

### S3.2 — Verification per site

After each fix, run that test 3× consecutively against `teas_app`:
- Run 1: passes (clean state OR using random IDs)
- Run 2: passes (no accumulation = §14 dead)
- Run 3: passes (consistency check)

Document each in `progress.md` cont. 41 §"S3 retrofit verification".

---

## 5. S4 — CLAUDE.md "Test data discipline" section

Insert new subsection under §5 Coding Conventions or as new top-level §15 (after §14 e-Tax switching).

```markdown
## 15. Test data discipline (Sprint 14.5)

The single most-re-applied gotcha (§14 — test fixture non-idempotent DB state)
caused 7+ false-positive sprint failures. Sprint 14.5 added `TestIds` helper +
this rule:

### Rule

**Every test that inserts a row with a UNIQUE constraint MUST use `TestIds.*`
or an explicit `Guid.NewGuid()` for that field.** Never hardcode `"ACME-001"`,
`"PROD-X"`, `"yyyymm=202601"`, etc.

### Helpers

- Backend: `Accounting.TestKit.TestIds` (`CustomerCode()`, `VendorCode()`,
  `ProductCode()`, `BusinessUnitCode()`, `ExpenseCategoryCode()`,
  `WhtTypeCode()`, `Email()`, `TaxId()`, `FuturePeriod()`, `Name()`,
  `Suffix()`)
- Frontend e2e: `frontend/e2e/helpers/test-ids.ts` — same surface in TypeScript

### Pattern

```csharp
// ❌ WRONG (will collide on re-run against teas_app)
await CreateCustomerAsync("ACME-001", "Acme Corp");

// ✅ RIGHT
var code = TestIds.CustomerCode();        // "CUST-a1b2c3d4"
await CreateCustomerAsync(code, "Acme Corp");
```

```typescript
// e2e
import { TestIds } from './helpers/test-ids';

await page.getByLabel('Customer code').fill(TestIds.customerCode());
```

### When fixed values ARE OK

- **Pure unit tests** (no DB) — fixed values fine
- **Read-only assertions** on seeded reference data (e.g. existing demo company's tax codes) — fine
- **Verifying serialization shape** of a fixed example — fine

The rule applies ONLY to **write-then-read** integration tests against
long-lived shared DBs.

### Why this exists

The gate's "test must pass 2-3 consecutive runs on the SAME `teas_app` DB" is
non-negotiable. If a test fails on run 2 but passes on run 1 → §14. Fix
immediately with `TestIds.*`.
```

---

## 6. Scope cuts — explicitly OUT

- ❌ **Per-test DB reset** (truncate or restore from snapshot) — heavyweight, ~30s per test class, would 10× CI time. `TestIds.*` per-row randomization is much cheaper + sufficient.
- ❌ **Migration for `journal_entries` sequence reset** — tools/ script only, not a permanent schema change
- ❌ **Refactoring `PostgresFixture` itself** — already uses random Guids; no change needed
- ❌ **New test framework** (e.g. Testcontainers per-test) — out of scope; existing fixture pattern works once §14 is gone
- ❌ **CI parallelization changes** — orthogonal concern, Phase 2

If any block → escalate per §8.

---

## 7. Gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| New project `Accounting.TestKit` | builds + tests pass |
| Existing tests | All 83 Domain + 114 Api still pass after retrofit (regression = 0) |
| **Re-run discipline** | Full Api suite runs 3× consecutively on `teas_app` AND `teas_test` — all passes identical (no flakey, no accumulation) |
| Playwright | 31 → 31 still (might convert §14-gated skip back to passing run on `teas_app` after dev-db-resync.sh applied) |
| Mirror sync | `Y:\AccountApp` |
| Git commit per step | 4 commits (S1/S2/S3/S4) on Sprint 14 wrap parent |

---

## 8. DoD (single phase, 4 items + wrap)

1. S1: `Accounting.TestKit` project created + `TestIds` helper + 6 helper tests
2. S2: `frontend/e2e/helpers/test-ids.ts` + minimal usage smoke (1 e2e converted as proof)
3. S3: 7 known §14 sites retrofitted + `tools/dev-db-resync.sh` one-time cleanup script + run 3× verification per site
4. S4: CLAUDE.md §15 "Test data discipline" section added
5. All gates green (incl. 3× consecutive re-run pass)
6. Mirror sync `Y:\AccountApp`
7. plan.md §23.3 — strike Sprint 14.5 + note §14 from "actively blocking" → "extinct"
8. runtime-gotchas §14 entry updated with "Resolved Sprint 14.5 via `TestIds` helper" note + ROI table row
9. `Report-Backend20.md`
10. Single wrap commit "Sprint 14.5 COMPLETE — §14 fix (TestIds helper + 7-site retrofit)"

**Total: 10 DoD items.**

---

## 9. After this sprint

- §14 = extinct → future sprints free of recurrence
- **Sprint 13b** (User Manual generator) — can start clean; no §14 risk for screenshot generation
- **Sprint 15** (Claude Code Pentest) — security audit can rely on tests being deterministic
- **Sprint 16** (Sana + Ham UAT walkthrough) — fresh state per walkthrough easier

ROI: 7+ instances of §14 found across Phase 1. At ~30 min per false-positive debug + fix, that's 3.5+ hours of wasted dev time. Sprint 14.5 (0.5-1 d) pays back immediately.

---

**Build it. Single phase 4 steps. Report back via Report-Backend20.**

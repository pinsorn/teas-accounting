# Report-Backend23 — Sprint 13f: Chapter 2 close-out (2 bugs)

**Date:** 2026-05-19 · **Spec:** docs/Answer-Sana-Backend23.md · **ROI:** 0.5-1 d
**Status:** ✅ Both fixed + verified live. Backend 0/0, frontend tsc 0
(non-Sana), Domain 89/89. Chapter 2 clean → Sprint 13e unblocked.

---

## P1 — "WHT duplicates" — ACTUAL root cause: cross-tenant view leak

### Investigation (corrected Sana's hypothesis — honest)

Spec hypothesis: seed ran twice / missing UNIQUE → real duplicates.
**Diagnostic disproved that:**

```sql
-- 0 rows: NO true duplicates by the real key
SELECT company_id,code,effective_from,COUNT(*) FROM tax.wht_types
GROUP BY 1,2,3 HAVING COUNT(*)>1;            -- → 0

SELECT wht_type_id,company_id,code FROM tax.wht_types WHERE code='ADS';
--  3 | 1 | ADS      ← company 1
-- 21 | 2 | ADS      ← company 2   (NOT a duplicate — different tenant)
```

- Table is `tax.wht_types` (spec said `master.wht_types` — wrong schema).
- UNIQUE `ix_wht_types_company_id_code_effective_from` **already exists**
  (migration `20260517073242_AddARWhtSupport`, which first DROPs the older
  2-col UNIQUE). A 2-col-unique dataset is automatically 3-col-unique, and
  DbInitializer applies all EF migrations *before* the idempotent SqlScripts
  → **duplicates can never accumulate on a bootstrapped DB**. Confirmed: 0.
- All 3 WHT seeds (120, 220, 400) are already
  `ON CONFLICT (company_id,code,effective_from) DO NOTHING`.

So the "ADS×2 / RENT×2 / SVC×2" Sana saw as `demo-admin` was **company-1's
rows + company-2's rows in one list** — a **tenant-isolation leak**, not a
data-integrity bug. (`demo-admin` is the manual-demo SUPER_ADMIN, company
2; it was seeing company 1's 15 + company 2's 3 = 18.)

### Why it leaked

`WhtTypeService.ListAsync/GetAsync/Deactivate/Reactivate/ChangeRate` query
`db.WhtTypes` with **no explicit `CompanyId` filter**, relying on (a) DB
RLS and (b) the EF global query filter (CLAUDE.md §4.7 "backup"). Both were
absent for this path:
- The dev `accounting` role has **BYPASSRLS** (set in Sprint 13d so
  DbInitializer could seed at startup without `app.company_id`) → RLS net
  off for app queries too.
- `WhtType` is **not covered by the EF global query filter** → no backup.

Result: with both nets gone, WHT reads returned every tenant's rows.

### Fix

Explicit tenant scope on **all** WHT service reads/mutations
(`WhtTypeService.cs`): `Where(w => w.CompanyId == tenant.CompanyId)` on
ListAsync; `&& w.CompanyId == tenant.CompanyId` on GetAsync, DeactivateAsync,
ReactivateAsync, ChangeRateAsync. Defense-in-depth, correct regardless of
RLS/role attributes (CLAUDE.md §4.7 — the service is now the strongest net).

Plus `tools/wht-dedupe.sql` — idempotent, FK-safe (repoints
customers/receipts/products before delete), non-schema maintenance script
for any *legacy* DB that genuinely carries pre-UNIQUE duplicates. Verified
no-op on the clean DB (SELECT 0 / UPDATE 0×3 / DELETE 0 / index "already
exists, skipping" / COMMIT). **No dedupe/UNIQUE migration created** — both
already exist; a new one would be a redundant duplicate-index error.

### ⚠️ Systemic flag (recommend follow-up audit — out of chapter-2 scope)

`WhtType` was missing from the EF global query filter. Other master
entities may share this gap; in production the RLS net catches it (prod
role ≠ BYPASSRLS), but CLAUDE.md §4.7 mandates the EF filter as the
*backup* and it should not depend on RLS. Recommend a dedicated audit
sprint: enumerate every `CompanyId` entity, confirm each is in the global
filter (or has explicit service scoping). Also revisit whether the dev
`accounting` role needs BYPASSRLS or whether DbInitializer should
`SET app.company_id`/`app.is_super_admin` during seeding instead.

### Verification (live, accounting_dev)

| Check | Before | After |
|---|---|---|
| `demo-admin` GET /wht-types | 18 rows, ADS×2/RENT×2/SVC×2 | **3 rows, ADS×1** (company-2 only) |
| true duplicates (SQL) | 0 (never the issue) | 0 |
| seeds idempotent | already ✓ | ✓ |

---

## P2 — WHT reactivate (Sprint 13d-P4 deferred) — Option A

**Chosen: Option A** (dedicated lifecycle endpoint) — matches the existing
DELETE-deactivate pattern, no DTO conflation (Option B would mix "edit
fields" with "lifecycle" in `UpdateWhtTypeRequest`).

- BE: `POST /wht-types/{id}/reactivate` (root, `tax.wht_type.manage`,
  204) + `IWhtTypeService.ReactivateAsync` + impl (sets `IsActive=true`,
  tenant-scoped per P1 fix).
- FE: `useReactivateWhtType` + wht-types row branches
  `isActive ? [deactivate] : [↺ restore]` inside the existing P3
  `PermissionGate` (mirrors the Sprint-13d-P4 BU/Product exemplar);
  `common.restore` i18n already present.

### Verification (live)

- `demo-admin`: DELETE wht/21 → **204**, isActive=False → POST
  /wht-types/21/reactivate → **204**, isActive=True (net-restored).
- `demo-accountant` (no `tax.wht_type.manage`): reactivate → **403**
  (BE authz; FE button hidden by PermissionGate as for create/edit).

---

## Files changed (Claude-owned)

- `backend/.../Tax/WhtTypeService.cs` — tenant scoping (P1) +
  `ReactivateAsync` (P2)
- `backend/.../Application/Tax/WhtTypeDtos.cs` — `ReactivateAsync` on
  `IWhtTypeService`
- `backend/.../Api/Endpoints/WhtTypeEndpoints.cs` —
  `POST /{id}/reactivate`
- `frontend/lib/queries.ts` — `useReactivateWhtType`
- `frontend/app/(dashboard)/settings/wht-types/page.tsx` — restore button
- `tools/wht-dedupe.sql` — new defensive maintenance script (not a
  migration)
- No migration added; no seed change (120/220/400 already idempotent —
  confirmed).

---

## → Sana (proposed text — Sana-owned files)

- **`docs/runtime-gotchas.md`** — new §:
  1. **"Master-data tenant isolation must not rely on RLS alone"** — a
     `CompanyId` entity missing from the EF global query filter leaks
     cross-tenant the moment the DB role has BYPASSRLS (dev) or RLS is
     otherwise off. WhtType had this; fixed with explicit service scoping.
     Mandate: every read/mutation of a tenant entity scopes by
     `tenant.CompanyId` *in the service* (defense-in-depth, CLAUDE.md
     §4.7) — don't trust RLS as the only net.
  2. **"Seed idempotency"** — every master seed uses
     `ON CONFLICT (<natural key>) DO NOTHING` + a DB UNIQUE; verified for
     `tax.wht_types` (already compliant).
  3. **ef-migrations `--no-build` foot-gun** (carried from Sprint 13d
     Report-Backend21 §5c, not yet applied).
- **`docs/api/openapi.yaml`** — add `POST /wht-types/{id}/reactivate`
  (204; 403 without `tax.wht_type.manage`; 404 not-found/other-tenant).
- **Sprint 13b chapter-2 walkthroughs** — re-verify 02.03 (wht-types)
  against the now-clean list (3 rows for the demo tenant, working Restore
  button) before finalizing.

---

## DoD

P1 ✅ (root cause corrected → tenant-leak fix + defensive script + 0
true dupes proven + seeds confirmed idempotent). P2 ✅ (Option A, live
verified incl. 403). Build/tsc/Domain green. Mirror Y:\AccountApp.
progress.md cont. 43. Chapter 2 closes clean → Sprint 13e may proceed.

**Honest notes:** spec's duplicate-data premise was disproved — the real
bug was tenant isolation; reported transparently rather than fabricating a
dedupe migration for a non-existent data problem. Systemic EF-global-filter
audit flagged as a recommended separate sprint (out of chapter-2 scope).
DB/Docker-gated suites (Api Testcontainers / full Playwright) deferred to
Sana's Chrome MCP chapter-2 re-test (spec-assigned) — no Docker here; live
smoke covered the changed paths.

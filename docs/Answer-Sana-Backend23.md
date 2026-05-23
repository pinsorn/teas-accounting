# Sprint 13f — Chapter 2 bug fixes (Sprint 13b sequential gate)

**Owner**: Claude Code
**Spec author**: Sana (live-found during chapter 2 re-test, 2026-05-19)
**Workflow**: CLAUDE.md §16 chapter-sequential gate — chapter 2 fix phase
**Sequencing**: BEFORE Sprint 13e (sales/chapter 3). Chapter 2 must close cleanly
before any chapter 3 work resumes.
**ROI**: 0.5-1 day

---

## Background

After Sprint 13d shipped (UX hardening + Company Profile), Sana re-validated
all chapter 2 pages (BU/Products/WHT/API-keys/Company Profile) via Chrome MCP.
6 phases verified working as designed. **2 real bugs surfaced during
re-test** — both in WHT types. Both block chapter 2 from being marked
"complete" per the new sequential workflow (CLAUDE.md §16).

---

## P1 — WHT-types data duplicates (data integrity bug)

### Evidence (live, demo-admin, 2026-05-19)

```bash
$ curl -b cookie /api/proxy/wht-types
# 18 rows returned
```

Codes returned:
```
ADS, ADS, AGRI, COMM, CONTRACT, ENTERTAIN, FOR-ROYAL, FOR-SVC,
INT, PRIZE, PROF, RENT, RENT, ROYAL, SVC, SVC, SVC-IND, TRANS
```

**ADS appears twice** with these whtTypeIds: `21` and `3`. Both rows have
identical: code=ADS, rate=0.02, formType=PND53, incomeTypeCode="4",
nameTh="ค่าโฆษณา", effectiveFrom="2020-01-01", effectiveTo=null,
isActive=true. Only `whtTypeId` differs.

Same pattern for RENT (×2) and SVC (×2).

For comparison: demo-accountant sees only 3 rows (ADS/RENT/SVC) —
the manual-demo seed entries. Different counts → likely cross-tenant
view OR seed ran twice into the same tenant.

### Root cause (Sana's hypothesis — Claude Code to confirm)

Two plausible causes:
1. **Seed ran twice** — system standard WHT seed inserted full set
   (18 codes) once; manual-demo seed inserted ADS/RENT/SVC a second time
   without idempotency check → 3 duplicates in DB
2. **No UNIQUE constraint** on `(code, company_id)` — DB allowed the
   second insert when it should have rejected
3. **Both** — most likely

### Fix

**Step 1 — investigate** (1 query):
```sql
SELECT code, company_id, COUNT(*) AS dup_count, array_agg(wht_type_id)
FROM master.wht_types
GROUP BY code, company_id
HAVING COUNT(*) > 1;
```

**Step 2 — dedupe**: delete duplicate rows. Keep the row with the **lowest
`wht_type_id`** (older) since any document already referencing WHT
likely references the original IDs. Audit affected document refs first
(if any FK exist to wht_types).

**Step 3 — add UNIQUE constraint**:
```sql
ALTER TABLE master.wht_types
ADD CONSTRAINT uq_wht_types_code_company UNIQUE (code, company_id);
```

**Step 4 — make seed idempotent**: change INSERT statements in seed
SQL to `INSERT ... ON CONFLICT (code, company_id) DO NOTHING` (or
DO UPDATE if we want to refresh data on re-seed).

**Step 5 — verify** with the same Step 1 query → 0 rows returned.

### Why this matters for manual

If we document WHT setup in chapter 2 manual showing "ADS appears twice
in the list", users will think they need to clean it up themselves — but
it's a system bug. Manual must be written against a clean baseline.

### Files
- New migration: `{timestamp}_AddWhtTypesUniqueConstraint.cs` + `.sql`
  (dedupe + ADD CONSTRAINT)
- Update seed SQL files that inserted WHT types — add ON CONFLICT clause
- (No FE change unless dedupe FK-cascades caused issues)

### Acceptance
- Re-run the diagnostic query → 0 duplicates
- Admin GET /api/proxy/wht-types → 15 rows (was 18; removed 3 dupes)
- Re-run seed → no new duplicates inserted
- Accountant view unchanged (still 3 rows, same data)
- Sana re-verifies via Chrome MCP screenshot

---

## P2 — WHT restore endpoint (Sprint 13d-P4 deferred)

### Evidence

Sprint 13d Report-Backend21 §3.3 honestly flagged this:

> `UpdateWhtTypeRequest` (and its BE DTO) has **no `isActive`**, so
> "PUT isActive=true" is impossible without a backend DTO/endpoint
> change — outside P4's stated FE-only scope. BU + Product restore
> shipped; WHT restore needs a small BE follow-up.

So BU + Product have working Restore buttons (verified ✅), but WHT
doesn't. If user disables a WHT type by mistake → cannot re-enable via
UI.

### Fix (choose ONE — Claude Code's call)

**Option A (preferred — simpler)**: dedicated reactivate endpoint
```
POST /api/v1/wht-types/{id}/reactivate
→ 204 (sets isActive=true, returns)
```
- Matches the established pattern (separate endpoint for status change)
- No DTO change needed
- FE: PermissionGate-wrapped button calls this endpoint

**Option B**: add `isActive` to `UpdateWhtTypeRequest` DTO
- Smaller code change in BE
- But conflates "edit fields" with "lifecycle" — less clean

### Files
- Whichever option chosen — Controller endpoint + service method +
  validator update
- FE: `frontend/app/(dashboard)/settings/wht-types/page.tsx` — copy the
  Restore button pattern from BU/Product (Sprint 13d-P4 exemplar exists)
- Permission scope: same as edit — `tax.wht_type.manage`

### Acceptance
- Admin can disable WHT → row shows "↺ เปิดใช้งานใหม่" button → click →
  toast + row active
- Accountant: button hidden (PermissionGate works as for create/edit)
- Sana re-verifies via Chrome MCP

---

## Out of scope (defer to later sprints)

- **Sprint 13e (chapter 3)**: blocked until this sprint merges per
  workflow gate
- Re-run of full validator i18n sweep (Sprint 13d-P5 follow-up):
  unrelated to chapter 2 verification, do as separate tracker
- WHT effective-date pattern UI ("เปลี่ยนอัตรา" button) — out of scope
  unless Sana finds it broken during re-validate

---

## Sana owns (apply AFTER merge)
- `docs/runtime-gotchas.md` — add §X "Seed idempotency required for
  master data" — pattern: ON CONFLICT clauses + DB-level UNIQUE
  constraints catch duplicate-seed mistakes
- `docs/api/openapi.yaml` — add `POST /wht-types/{id}/reactivate`
  (if Option A chosen)
- Sprint 13b chapter 2 walkthroughs (after re-verify): finalize
  02.01-02.04 with fully verified behavior (clean WHT list, working
  WHT restore button)

---

## Reporting back

`Report-Backend23.md` — concise (this is a small sprint). Include:
- Diagnostic query results (how many dupes were found, FK cascade check)
- Migration applied + git diff snippet
- Which restore option chosen + rationale
- Live verification: API calls + screenshots optional
- Any new gotcha learned

---

## File ownership reminder
Same as previous sprints. Claude Code edits source/migrations/tests.
Sana owns: CLAUDE.md, plan.md, runtime-gotchas.md, openapi.yaml,
manual/**.

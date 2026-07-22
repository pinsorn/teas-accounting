# Spec — Fix two post-DCR bugs (onboarded company has no branch; consent unreachable via login)

Owner: Fable designed both (Bug A = tenant-data creation; Bug B = route relocation). sonnet-implementer
types the code. opus-reviewer checks. Fable gates + diff-review + commit + release + prod deploy.
Ham is away — full autonomous cycle authorized (incl. prod deploy, creds held).

Context: DCR now works in prod (v1.11.0). Two follow-on bugs block a clean first-time connector run.

## Bug A — `CompanyService.CreateAsync` never creates the head-office branch

`backend/src/Accounting.Infrastructure/Master/MasterDataServices.cs` `CompanyService.CreateAsync`
(~line 167) seeds Company + CompanyProfile (with `BranchCode="00000"`) + WHT + full CoA + TaxCodes +
RBAC — but **never inserts a `master.branches` row**. So every onboarded company has zero branches.
The MCP OAuth authorize flow (`OAuthEndpoints.cs:105-110`) requires an active HQ branch to pin the
token's branch_id → returns `400 company_has_no_active_branch`. (Same class as the already-fixed CoA
gap: onboarded tenants had an empty CoA until CreateAsync was made to seed it.) Confirmed on prod:
companies 2 & 3 had 0 branches; a data backfill (`fix-missing-hq-branches.sql`) already patched prod,
but new companies will keep breaking until CreateAsync creates the branch.

### Fix (Bug A)
In `CompanyService.CreateAsync`, after `db.Companies.Add(e); await db.SaveChangesAsync(ct);` (so
`e.CompanyId` is assigned) and alongside the other default seeding, add the head-office branch:

```csharp
db.Branches.Add(new Branch
{
    CompanyId    = e.CompanyId,
    BranchCode   = "00000",              // HQ convention (matches CompanyProfile.BranchCode + seeds 120/400)
    NameTh       = "สำนักงานใหญ่",
    NameEn       = "Head Office",
    IsHeadOffice = true,
    IsActive     = true,
    AddressTh    = addressTh,            // reuse the composed company address already in scope
});
```
- `Branch` entity: `backend/src/Accounting.Domain/Entities/Master/Branch.cs` (`BranchCode`+`NameTh`
  required; `IsActive` defaults true — set it explicitly anyway).
- Ensure `using Accounting.Domain.Entities.Master;` is present (the file already uses `Company` etc.,
  so likely yes — confirm).
- Place the `Add` so it's persisted by an existing `SaveChangesAsync` (e.g., the batch before line ~271
  or its own). It must run for EVERY company create (unconditional, like CompanyProfile).
- Thai string `"สำนักงานใหญ่"` — the .cs file is UTF-8; do NOT let the Bengali glyph ম (U+09AE) slip in
  for Thai ม (U+0E21). `grep -n "ম"` the file after editing — must be zero.
- **Guard check:** confirm there is NO GLOBAL unique index on `branches.branch_code` (seeds 120 & 400
  both use `'00000'` for different companies, so it must be per-company or none). If a global unique
  exists, STOP and re-spec (would break multi-company tests). Check the EF model config
  (`AccountingDbContextModelSnapshot.cs` / the Branch entity config).

### Test (Bug A)
Find the existing CompanyService tests (grep `CompanyService` under `backend/tests`). Add a test:
creating a company via `CompanyService.CreateAsync` yields exactly one branch that is
`BranchCode=="00000"`, `IsHeadOffice`, `IsActive`. Do NOT break existing CreateAsync tests (they may now
also see a branch — adjust only assertions that explicitly expected none). Run the affected test class
×1 (teas_test) and report pass/skip counts.

## Bug B — the consent page is gated by the onboarding redirect (login → onboarding → dashboard)

`app/(dashboard)/oauth/consent/page.tsx` lives in the `(dashboard)` route group, whose
`layout.tsx:26,47` redirects any `isSuperAdmin && companyId===0` user to `/onboarding` (which then
auto-switches to the first company and `window.location.replace('/')` → dashboard). A super-admin has
`companyId===0` (no home company), so deep-linking to `/oauth/consent` (via the backend authorize →
`/login?returnTo=/oauth/consent` → login pushes it) gets bounced through onboarding to the dashboard —
the consent screen never shows.

### Fix (Bug B) — relocate the consent page OUT of the onboarding-gated group
Move the page file:
`app/(dashboard)/oauth/consent/page.tsx`  →  `app/oauth/consent/page.tsx`
(use `git mv`; then remove the now-empty `app/(dashboard)/oauth/consent/` and `app/(dashboard)/oauth/`
directories IF they contain nothing else — verify there are no other files under
`app/(dashboard)/oauth/`).

Why this is correct + safe:
- The URL is unchanged (`/oauth/consent` — route groups don't affect the path), so the backend
  authorize redirect and the login `returnTo` still resolve. No internal `<Link>` targets it.
- All providers (NextIntl, React Query, Theme, Confirm, Toaster) come from the ROOT `app/layout.tsx`,
  which wraps every route — the page keeps them after the move. The page renders its own centered card
  (no dependency on the dashboard sidebar/topbar).
- It STAYS session-gated: `middleware.ts` PUBLIC_PATHS does NOT include `/oauth/consent`, and the
  matcher covers top-level routes, so a request without the `access_token` cookie is still redirected
  to `/login?returnTo=/oauth/consent…`. (Do NOT add `/oauth/consent` to PUBLIC_PATHS.)
- Being outside `(dashboard)`, it is no longer subject to the `needsOnboarding` redirect → a
  companyId===0 super-admin now reaches consent directly after login. The consent page already works at
  companyId===0 (it fetches `/api/proxy/me` `allowedCompanies` and posts a chosen `company_id`; the
  backend authorize validates membership for a super-admin against any active company).

Do NOT change the consent page's contents (it carries the F1 fix already shipped). Move only.

### Verify (Bug B)
`cd frontend && pnpm build` (or `tsc --noEmit` + `next build` per the repo) → passes; the
`/oauth/consent` route still builds. Confirm the file now resolves at `/oauth/consent` and that
`app/(dashboard)/oauth/` no longer exists (or is empty). No e2e harness change required.

## Gates (Fable owns)
- Backend: solution build 0/0; the CompanyService test class green (report counts).
- Frontend: build/typecheck green; `/oauth/consent` route present in the build output.
- No migration (Bug A seeds a row in an existing table; Bug B is a file move).
- ম-glyph grep = 0 on the edited .cs.

## Blast radius
- Bug A: ONLY `MasterDataServices.cs` (`CompanyService.CreateAsync`) + the CompanyService test file.
- Bug B: ONLY move `oauth/consent/page.tsx`; no content edit, no middleware/layout/onboarding edit.
- NO change to: token/refresh/RLS/tenant pinning, the DCR endpoint, the authorize/accept flow logic,
  middleware auth rules, the onboarding gate logic itself. Touching any of those = stop & re-spec.

## Attempt log
- 2026-07-05 Fable: spec created after diagnosing both bugs live on prod (branch missing → 400;
  consent gated by (dashboard) onboarding redirect). Root causes confirmed by reading the code +
  prod DB. Dispatching sonnet-implementer.
- 2026-07-05 sonnet-implementer:
  - **Bug B: [x] DONE.** `git mv "app/(dashboard)/oauth/consent/page.tsx" "app/oauth/consent/page.tsx"`;
    removed the now-empty `app/(dashboard)/oauth/consent/` and `app/(dashboard)/oauth/` dirs (verified
    nothing else lived under them). No content edit. `pnpm build` (fallback `next build`) exit 0,
    `/oauth/consent` present in route output (2.52 kB / 122 kB First Load JS).
  - **Bug A: [~] CODE DONE + narrow gate GREEN, but BLOCKED-RED on the full suite — needs Fable's
    scope call before proceeding.** Added the unconditional `db.Branches.Add(...)` in
    `CompanyService.CreateAsync` (MasterDataServices.cs) exactly per spec, right before
    `CompanyProfiles.Add` (same SaveChangesAsync batch). Guard confirmed: `BranchConfiguration.cs:25`
    is `HasIndex(x => new { x.CompanyId, x.BranchCode }).IsUnique()` — COMPOSITE, no global unique on
    `branch_code` alone → safe per spec's guard condition. Added
    `OnboardingFoundingAddressTests.CreateAsync_creates_head_office_branch` (exercises
    `ICompanyService.CreateAsync` directly, asserts exactly 1 branch, BranchCode "00000",
    IsHeadOffice, IsActive). `dotnet build`: 0 warnings/0 errors. Ran
    `--filter FullyQualifiedName~OnboardingFoundingAddressTests` with `TEAS_TEST_PG` set: **4/4
    passed, 0 skipped**, incl. `CreateAsync_creates_head_office_branch [175 ms] Passed` (executed,
    not skipped) — this satisfies the spec's literal Verify wording ("run the CompanyService test
    class").
  - **Scope conflict found and CONFIRMED EMPIRICALLY (not touched, per blast-radius cap):**
    `tests/Accounting.Api.Tests/Fixtures/TestCompanyFactory.cs` lines 75-80 manually
    `db2.Branches.Add(new Branch { CompanyId, BranchCode="00000", ... })` right AFTER calling
    `ICompanyService.CreateAsync` — a workaround for the exact bug this spec fixes. Once CreateAsync
    seeds its own HQ branch, this second insert collides with `BranchConfiguration`'s composite
    unique `(CompanyId, BranchCode)`. Reran `CompanyTaxConfigTests` (a consumer of
    `TestCompanyFactory.CreateAsync`) as proof: **4 of 6 FAILED** with
    `23505: duplicate key value violates unique constraint "ix_branches_company_id_branch_code" —
    Key (company_id, branch_code)=(696953, 00000) already exists`, thrown from
    `TestCompanyFactory.CreateAsync` line 95. `TestCompanyFactory.CreateAsync` is called by ~26 test
    files / 109 occurrences repo-wide (grepped) — every one of them would go red on a full-suite run.
    Grepped the FULL collision surface (`Branches.Add|new Branch`) across `backend/tests` AND
    `backend/src`: **exactly one file** touches Branch directly outside the fixed service —
    `TestCompanyFactory.cs` itself. So the blast radius, if authorized, is ONE additional file, not 26
    (the 26 are indirect consumers, not direct branch-inserters).
  - Spec tension to resolve: Blast Radius says "Bug A: ONLY MasterDataServices.cs + the CompanyService
    test file" (would exclude TestCompanyFactory.cs); the Test section says "Do NOT break existing
    CreateAsync tests... adjust only assertions that explicitly expected none" (implies fixture
    adjustment is in-scope). Did NOT pick a side — stopped and left it for Fable, per blast-radius cap
    ("if you need more, STOP and report").
  - Proposed minimal fix (NOT applied): in `TestCompanyFactory.CreateAsync`, delete the manual
    `db2.Branches.Add(branch)` block (lines 75-80, now redundant/colliding) and instead read back the
    branch `CompanyService.CreateAsync` already created (`await db2.Branches.SingleAsync(b =>
    b.CompanyId == companyId && b.BranchCode == "00000")`) to populate `SeededCompany.BranchId` (still
    consumed downstream by ~22 files via `BuildProvider(..., c.BranchId)`). ~5-line diff, one file.
  - ম-glyph grep: ZERO matches on both `MasterDataServices.cs` and the edited test file (confirmed via
    `grep -n "ম"`, exit 1 = no match, both files). Re-confirmed after a cosmetic reorder (moved
    `Branch.Add` to right after `Companies` SaveChanges, ahead of the ม.86/4 CompanyProfile comment
    it was previously sandwiched inside) — rebuilt (0/0) and reran the 4/4 green test class again.
  - **Commit atomicity — do not split:** the `MasterDataServices.cs` change and the
    `TestCompanyFactory.cs` fix (once authorized) MUST land in the SAME commit. The service change
    alone reddens the full suite (proven above) — it is not a standalone green unit of work until the
    fixture is fixed alongside it. Bug B is fully independent and commit-ready now on its own.
  - Closed loop offered: on authorization, will apply the ~5-line `TestCompanyFactory.cs` fix (delete
    the redundant branch insert, read back the CreateAsync-created branch for `SeededCompany.BranchId`)
    and rerun the full `Accounting.Api.Tests` suite (or at minimum every file grepped as a
    `TestCompanyFactory.CreateAsync` consumer) to confirm green before calling Bug A done.
- 2026-07-05 sonnet-implementer — **Bug A: [x] DONE, fixture fix APPROVED and applied.** Coordinator
  approved expanding blast radius to the one file. Applied the proposed fix in
  `TestCompanyFactory.cs` (deleted the manual `db2.Branches.Add(branch)` insert; replaced with
  `await db2.Branches.SingleAsync(b => b.CompanyId == companyId && b.BranchCode == "00000")` reading
  back the branch `CompanyService.CreateAsync` now seeds; `SeededCompany.BranchId` populated the same
  way as before). Diff: 7 insertions / 6 deletions, one file only.
  - `dotnet build`: 0 Warning(s), 0 Error(s).
  - `CompanyTaxConfigTests` (the class proven RED above): **6/6 passed, 0 skipped** (was 4 failed / 2
    passed with `23505 duplicate key` before the fix).
  - `Accounting.Api.Tests.OAuth` namespace: **44/44 passed, 0 skipped**.
  - 4 more `TestCompanyFactory` consumers: `Pnd30CorrectnessTests` 4/4, `SalesChainRlsTests` 5/5,
    `NonVatBillingTests` 7/7, `TaxInvoiceRateDerivationTests` 7/7 — all passed, 0 skipped, 0 failed.
  - Re-ran the original Bug A gate (`OnboardingFoundingAddressTests`): still 4/4 passed, 0 skipped,
    `CreateAsync_creates_head_office_branch [173 ms] Passed`.
  - No `23505 duplicate key` in any run above. ম-glyph grep on `MasterDataServices.cs`: still 0
    matches (exit 1).
  - Both Bug A files (`MasterDataServices.cs` + `TestCompanyFactory.cs`) and Bug A's test file
    (`OnboardingFoundingAddressTests.cs`) are ready to commit TOGETHER as one unit, alongside Bug B's
    independent file move. No `git commit` run — orchestrator owns the commit.
- 2026-07-05 Fable: full-diff review APPROVE (verified middleware still gates `/oauth/consent` after
  the move; branch add persists via the CompanyProfile SaveChanges batch; fixture read-back correct).
  Commit `e15c44e` -> PR #45 -> CI green (backend 8m18s, frontend 29s) -> merged main `33cd835` ->
  release PR #46 -> merged -> tag `v1.11.1` (MinVer `1.11.1+319d3fb`).
- 2026-07-05 Fable: DEPLOYED v1.11.1 to prod (`ubuntu@158.69.197.154`, both tiers). API SC swap:
  `DEPLOY_OK http=200 status=online registration_endpoint=present dcr_register=201`. FE was a FILE
  MOVE — deploy-fe-v1111.sh overlaid the new `app/oauth/consent/page.tsx` AND `rm`-ed the old
  `app/(dashboard)/oauth/consent` (else a duplicate `/oauth/consent` route) -> `OLD_PATH_GONE ok`,
  `BUILD_OK`, `FE_DEPLOY_OK`. PUBLIC verify: `/oauth/consent` no-cookie -> 307
  `/login?returnTo=%2Foauth%2Fconsent…` (route moved, still session-gated, no onboarding bounce);
  DCR unchanged (registration_endpoint + POST 201). Remaining = Ham's fresh (logged-out) connector
  run to confirm login -> consent -> tool call end to end.

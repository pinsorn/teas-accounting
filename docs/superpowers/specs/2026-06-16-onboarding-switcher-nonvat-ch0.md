# Spec — Onboarding wizard + Super-admin company switcher + Non-VAT demo + Chapter 0 rewrite + DB reseed

> Program kicked off 2026-06-16 (Ham). Main agent = overseer (§7); subagents do bounded chunks.
> Source of design facts = reconnaissance 2026-06-16 (this session). Compliance: §4.6 (VAT config =
> company master, super-admin only), §4.7 (multi-tenant RLS), §11 (new endpoints → flagged for openapi).

## Background / auth model (from recon — do NOT re-derive)

- **One company per user.** JWT (`JwtTokenIssuer.cs:44`) carries `company_id`, `branch_id`,
  `is_super_admin`. `HttpTenantContext` reads them; `TenantMiddleware` sets `SET LOCAL app.company_id`
  + `app.is_super_admin` per request. RLS is enforced at the DB session → **switching company REQUIRES
  a new JWT** (no header swap).
- **LoginService** (`LoginService.cs:74-84`) picks the primary (company,branch) = first active role
  assignment. **A super-admin with NO role assignment logs in with `companyId=0, branchId=0`.** ← this
  is the onboarding "no company yet" signal (NOT "DB has zero companies"; seeded admin in dev has a
  company-1 role so it never triggers there — fixtures safe).
- **Super-admin** = `is_super_admin` claim, seeded in `130_seed_admin_and_customer.sql` (user `admin`, id 1).
- **Company CRUD** (`MasterEndpoints.cs:90-113`): `GET/POST/PUT /companies`, all `Master.CompanyManage`
  (super-admin). `CreateCompanyRequest` (`CompanyDtos.cs:7-11`) already has `VatRegistered`,
  `VatRegisterDate`, `VatRate`, `Pnd30SubmissionMode` + validators. **Reuse as-is for onboarding.**
- **FE:** JWT in httpOnly cookie (`app/api/auth/login/route.ts`), forwarded by BFF proxy
  (`app/api/proxy/[...path]/route.ts`). No client-side company context, no `/me`, no topbar dropdown.
  Topbar = `components/layout/Topbar.tsx` (breadcrumbs + search; room top-right).

## New backend endpoints (Phase 1 — security-sensitive, main agent reviews)

1. **`GET /me`** — authenticated; returns `{ userId, username, companyId, branchId, isSuperAdmin,
   companyName?, allowedCompanies: [{id,nameTh,nameEn}] }`. `allowedCompanies` = (super-admin → all
   active companies via the same query as `GET /companies`; normal user → just their own). Read-only,
   from claims + a company lookup. Low risk.
2. **`POST /auth/switch-company/{companyId}`** — **super-admin only** (reject 403 if `!IsSuperAdmin`).
   Validate target company exists + active. Re-issue JWT via `JwtTokenIssuer` with
   `company_id=companyId`, `branch_id=<HQ/first active branch of that company>`, `is_super_admin=true`,
   same userId/username, fresh expiry. Return the token the same shape login does; the FE auth route
   sets the cookie. **SECURITY GATES (main agent must verify):** only super-admin can switch; target
   must exist+active; issued token's company_id == requested; no privilege beyond what super-admin
   already has; audit-log the switch (`audit.activity_log`, action `company_switch`). One-company users
   are NOT given switching (future multi-company-per-user out of scope).
   - openapi.yaml: add both (flag for Sana). i18n: any new strings in th+en.

## FE features (Phase 2 — subagents, main agent validates)

3. **Company switcher (super-admin):** dropdown in `Topbar.tsx` top-right, visible only when
   `isSuperAdmin` (from `/me`). Lists `allowedCompanies`, shows current. On select → `POST
   /api/proxy/auth/switch-company/{id}` → on success set new cookie (mirror login route) → hard reload
   so RSC/React-Query refetch under the new company. th+en labels.
4. **Onboarding wizard:** after login, if `companyId===0 && isSuperAdmin` → gate dashboard, redirect to
   `/onboarding` (a route under `(dashboard)` or its own group). Wizard form = create first company
   (name th/en, taxId, VAT status + rate + pnd30 mode, fiscal year, currency) → `POST
   /api/proxy/companies` → then `POST /auth/switch-company/{newId}` (or re-login) to scope the token →
   land on dashboard. Keep it a clean 1-step form (can grow later). th+en.

## DB wipe + reseed (Phase 3 — MAIN AGENT, destructive, §6 footgun)

- Option ข (Ham). Stop API → drop+recreate `accounting_dev` empty → restart API → DbInitializer
  migrates + applies all SqlScripts (company1 + co2/co3 shells + reference; NO transactional data —
  that's created live). Clears the ม.70 pollution + e2e noise. Verify login admin + demo-admin, ภ.ง.ด.54
  = 0 ม.70 rows. **Sequenced AFTER features built so the manual demo isn't broken longer than needed.**

## Manual rebuild (Phase 4 — subagents, after clean DB)

5. **Rebuild demo transactional dataset** on co2 (+ co3) via UI/API so chapters have data (sales chain,
   purchases, payroll, WHT, the ม.70 case now that routing exists). Re-capture ch4–8.
6. **Chapter 0 rewrite:** keep 0.1–0.3 (prereqs/install/DB-init); ADD onboarding-wizard walkthrough
   (super-admin first-company setup) + a pre-go-live checklist (CoA/WHT types/prefixes/business units/
   default tax accounts). New walkthrough(s) under chapter 0.
7. **Non-VAT coverage (new walkthroughs):** `04.11` non-VAT company end-to-end (quotation→billing
   note→receipt, no tax invoice, co3); `03.03` individual บุคคลธรรมดา customer (no taxId, ม.86/4 #3
   omits buyer taxId); `05.06` individual บุคคลธรรมดา vendor + WHT. Use the `nonvat` persona / co3.

## Per-subagent gates (every dispatch carries §6 briefing + these)

- BE: `dotnet build W:\Accounting.sln` 0/0 (kill :5080 before full build, restart after) · new
  integration tests pass **2× consecutive** on `teas_test` (`TEAS_TEST_PG`, + `TEAS_REPO_ROOT` if RBAC
  touched) · Domain ≥146 · no `company_id` leak · ProblemDetails/async rules · do NOT `git commit`.
- FE: `tsc --noEmit` 0 · i18n th+en parity · do NOT `next build` while dev running · do NOT `git commit`.
- Manual: capture passes · `gen-markdown.mjs` ok · grep Bengali ม = 0 · eyeball every screenshot · never
  stage `frontend/screenshots`/`docs/RD-Forms`/`docs/SSO-Forms`.
- Main agent runs the consolidated gate + all commits.

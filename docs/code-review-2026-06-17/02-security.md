# Security Review — TEAS — 2026-06-17

Read-only security review of the TEAS backend (.NET 10 Clean Architecture) and frontend (Next.js).
Lens: AuthN/AuthZ, multi-tenant isolation, secrets, injection, PII/crypto, transport/headers.

## Summary

**Posture: Solid.** The security-critical primitives are implemented correctly and defensively:
JWT validation is fully configured, the per-request tenant pin uses parameterized SQL sourced from
the authenticated token (never user input), passwords use BCrypt work-factor 12 with account lockout,
the two intentionally-public first-run setup endpoints have sound zero-state gates (one TOCTOU-safe
via advisory lock), API keys cannot obtain super-admin bypass, and no live secret is committed. The
material finding is a **defense-in-depth gap**: ~15 tenant-owned transactional tables (the sales and
purchase document chain) are isolated by the EF Core query filter only, with **no RLS backstop**,
contradicting the CLAUDE.md §4.7 / DbInitializer claim that RLS covers "every business table." In
normal operation these tables are correctly isolated; the gap is that a single forgotten `WHERE`/raw
query against them would not be caught by the database, which is exactly what RLS exists to prevent.

Authorization is **opt-in per endpoint** (no global `FallbackPolicy` is registered), so a missing
`RequireAuthorization` would mean a publicly reachable route. I therefore swept **all 34 endpoint
files**: every business route is protected, either by a route-level `RequireAuthorization` or by a
group-level `RequireAuthorization` on its `MapGroup` (which ASP.NET Core propagates to all child
routes). The anonymous surface is exactly three intentionally-public routes — `/auth/login`, `/health`,
and the zero-state-gated `/system/setup/bootstrap-admin` — of which **only bootstrap is an explicit
`AllowAnonymous`; login and `/health` are anonymous by omission** (no auth metadata at all). No
*unintentionally* open endpoint was found. That reliance on omission for intended-public routes is
itself the substance of Finding #2: nothing in the code distinguishes "intentionally public" from
"forgot to protect." See "Verified CORRECT" for the per-file basis.

**Counts:** Critical 0 · High 1 · Medium 4 · Low 3

| # | Severity | Title |
|---|----------|-------|
| 1 | High | Tenant-owned sales/purchase chain tables lack RLS (EF query filter only) |
| 2 | Medium | Auth is opt-in (no global fallback policy) — one forgotten `RequireAuthorization` = open route |
| 3 | Medium | Dev DB password committed in base `appsettings.json` connection string |
| 4 | Medium | No rate limiting on `/auth/login` (lockout exists but is per-account, not per-IP) |
| 5 | Medium | No HTTP security headers (HSTS / X-Content-Type-Options / X-Frame-Options / CSP) |
| 6 | Low | `login_resp.json` (real JWT) at repo root is not git-ignored |
| 7 | Low | CORS `AllowCredentials()` + `AllowAnyHeader`/`AllowAnyMethod` (origin is constrained) |
| 8 | Low | Prod relies on env, not code, to suppress the Development exception page on root/BFF routes |

---

## Findings

### 1. [High] Sales/purchase chain tables are tenant-isolated by the EF query filter ONLY — no RLS backstop
**Confidence: [Confirmed]**

`backend/src/Accounting.Infrastructure/Migrations/SqlScripts/010_rls_policies.sql` and the per-table
RLS scripts (040, 060, 200, 322, 323, 430, 480, 500) enable RLS on this **complete** set:

```
branches, chart_of_accounts, customers, vendors, expense_categories, number_sequences, api_keys,
tax_codes, journal_entries, business_units, tax_invoices, vendor_invoices, billing_notes,
billing_note_tax_invoices, employees, payroll_runs, payslips, cit_year_summaries, cit_adjustments
```

The EF tenant filter (`AccountingDbContext.cs:126-145`) is applied by convention to **every**
`ITenantOwned` implementer (33 entities), e.g.:

```csharp
foreach (var entityType in modelBuilder.Model.GetEntityTypes())
    if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
        method.MakeGenericMethod(entityType.ClrType).Invoke(this, [modelBuilder]);
// ...
.HasQueryFilter(e => _tenant == null || _tenant.IsSuperAdmin || e.CompanyId == _tenant.CompanyId);
```

The authoritative RLS set was established by enumerating every `CREATE POLICY company_isolation ON`
target across all SqlScripts (both the literal `ALTER TABLE x ENABLE ...` form and the `DO $$ ... tables
text[] := ARRAY[...]` loop form in 010/322, which I read in full). Cross-referencing the 33
`ITenantOwned` entities against that set, these tenant-owned tables have **no `company_isolation`
policy**:

```
quotations, sales_orders, delivery_orders, receipts, tax_adjustment_notes (CN/DN),
purchase_orders, payment_vouchers, products, accounting_periods, attachments,
wht_certificates, wht_types, tax_filings, etax_submissions, idempotency_keys
```

`010_rls_policies.sql` itself states the intent: *"every business table that carries company_id gets
a policy ... so a forgotten WHERE-clause in application code still can't leak another tenant's rows."*
CLAUDE.md §4.7 makes the same promise ("PostgreSQL RLS on every business table"). For the ~15 tables
above, that backstop is absent.

**Attack scenario:** Not exploitable through the normal EF path (the global filter isolates correctly,
and the per-request `app.company_id` is set from the token). The risk is a future regression: any raw
SQL, `IgnoreQueryFilters()`, projection through a non-filtered navigation, or a service-layer query
that forgets the company predicate against one of these tables would silently return cross-tenant rows,
with no database-level policy to stop it. This is precisely the failure mode RLS was added to defeat,
and it is currently relied upon for `journal_entries`/`tax_invoices` but not for the document chain.

**IDOR sub-check (related, reassuring):** the by-id read paths for these unbacked tables still go
through the EF tenant filter. `Attachments/AttachmentService.cs:147-158` uses `AsNoTracking()` (which
does **not** strip the query filter) with `FirstOrDefaultAsync(x => x.AttachmentId == id ...)`, and
`Sales/SalesChainPdfService.cs:58-105` fetches quotation/SO/DO PDFs by id with the filter intact — so
a cross-tenant id is filtered out, not served. This is good, but it is *the EF filter doing the work*,
which is exactly why the missing RLS backstop is the finding: these by-id reads are the regression that
RLS would have to catch.

**Fix:** Add `company_isolation` RLS policies (same `company_id = current_setting('app.company_id')
OR is_super_admin` pattern already used in 010/040/060) for each tenant-owned table that carries a
`company_id` column; for child/line tables without `company_id`, isolate via FK to the parent (as
322 documents for `billing_note_lines`). Add a test that asserts every `ITenantOwned` entity's table
has `relrowsecurity = true` (`pg_class`) so the EF set and the RLS set can never drift again.

---

### 2. [Medium] Authorization is opt-in — no global fallback policy means one missing `RequireAuthorization` opens a route
**Confidence: [Confirmed]**

`Program.cs` registers permission authorization and one named policy (`ApiV1Endpoints.ApiKeyOnlyPolicy`,
lines 88-91) but **no `FallbackPolicy`** — confirmed by a grep that returned only the named policy and
`RequireAuthenticatedUser()` inside it. Consequently a route with no `RequireAuthorization`/`AllowAnonymous`
is anonymous-by-default rather than denied-by-default.

The current code is **not** vulnerable — my sweep of all 34 endpoint files confirms every route is
covered (route-level or group-level `RequireAuthorization`; see "Verified CORRECT"). This is a
latent-risk / hardening finding: the safety of the whole API rests on every future endpoint author
remembering to opt in, with no backstop and no failing test if they forget.

**Attack scenario:** A future PR adds `app.MapGet("/reports/export", ...)` and forgets
`.RequireAuthorization()`. With no fallback policy it is silently world-readable; nothing fails CI.

**Fix:** Register a global `FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`
so routes are authenticated-by-default, and keep `AllowAnonymous` explicit on the bootstrap/health
routes. (Verify the BFF/static/health routes still resolve.) Optionally add a test enumerating endpoint
metadata to assert every endpoint has either an `IAuthorizeData` or an explicit `IAllowAnonymous`.

---

### 3. [Medium] Dev DB password committed in the base (non-Development) `appsettings.json`
**Confidence: [Confirmed]**

`backend/src/Accounting.Api/appsettings.json:15`:

```json
"ConnectionStrings": {
  "Postgres": "Host=localhost;Port=5432;Database=accounting_dev;Username=accounting;Password=accounting_dev_password;Include Error Detail=true",
```

The committed base config carries a real (dev) password and `Include Error Detail=true`. The JWT
`SigningKey` here is a safe placeholder (`REPLACE_WITH_STRONG_KEY_IN_USER_SECRETS_OR_VAULT`), and
`appsettings.Development.json` uses an obvious placeholder key — so no *production* secret is leaked.
The concern is twofold: (a) the base file (which also applies in Production unless overridden) ships a
working credential and a verbose-error DB flag, normalizing the pattern; (b) `Include Error Detail=true`
in the base config can surface parameter values / constraint detail in exceptions if it ever reaches a
non-dev environment.

**Fix:** Remove the password and `Include Error Detail` from the committed base `appsettings.json`
(leave a key with an empty/placeholder value); keep the working dev credential only in
`appsettings.Development.json` or user-secrets. Confirm Production sources the connection string from
an env var / vault.

---

### 4. [Medium] No rate limiting on the authentication endpoint
**Confidence: [Confirmed]**

`AuthEndpoints.cs` maps `POST /auth/login` with no rate limiter; `Program.cs` does not call
`AddRateLimiter`/`UseRateLimiter`. `LoginService` (`LoginService.cs:49-58`) enforces a per-account
lockout (5 failed attempts → 15 min, confirmed in `User.IsLocked`), which blunts single-account
brute force, but there is no per-IP throttle. An attacker can spray many usernames, or trigger lockouts
on known accounts as a denial-of-service. Login also returns distinct codes (`auth.invalid_credentials`
vs `auth.account_locked` vs `auth.account_disabled`), enabling username enumeration.

**Fix:** Add ASP.NET Core rate limiting (a fixed/sliding window keyed on client IP) to `/auth/login`
and the public `/system/setup/*` endpoints. Consider collapsing the disabled/locked responses into a
single generic failure to reduce enumeration.

---

### 5. [Medium] No HTTP security response headers
**Confidence: [Confirmed]**

`Program.cs:124` calls `app.UseHttpsRedirection()` but there is no `UseHsts()`, and no middleware adds
`X-Content-Type-Options`, `X-Frame-Options`/CSP `frame-ancestors`, or a Content-Security-Policy. A
`grep` for these across `Program.cs` and `Middleware/*` found only `UseHttpsRedirection`. (This matches
a prior repo observation: "missing HTTP security headers.")

**Attack scenario:** Without `X-Content-Type-Options: nosniff` and framing protections, the API/BFF is
more exposed to MIME-sniffing and clickjacking; without HSTS, a first-request downgrade is possible.

**Fix:** Add `UseHsts()` (non-dev) and a small headers middleware setting `X-Content-Type-Options:
nosniff`, `X-Frame-Options: DENY` (or CSP `frame-ancestors 'none'`), and a baseline CSP.

---

### 6. [Low] `login_resp.json` containing a real JWT sits at repo root and is not git-ignored
**Confidence: [Confirmed]**

The repo root holds `login_resp.json` with a full `access_token` (a valid HS256 JWT for user sub=5207
with the permission set in the payload). `git check-ignore login_resp.json` returns nothing — it is
**not** ignored (it is currently untracked per git status, so not yet committed). The token is expired
(`exp` 2026-06-16, now 2026-06-17), limiting impact, but the file is one `git add .` away from being
committed, and the pattern (writing login responses to repo root) risks committing a live token later.

**Fix:** Delete the file and add `login_resp.json` (or `*_resp.json`) to `.gitignore`.

---

### 7. [Low] CORS allows credentials with any header/method (origin is constrained, which mitigates)
**Confidence: [Confirmed]**

`Program.cs`:

```csharp
builder.Services.AddCors(o => o.AddPolicy("frontend", p =>
    p.WithOrigins(builder.Configuration["Frontend:Origin"] ?? "http://localhost:3000")
     .AllowAnyHeader().AllowAnyMethod().AllowCredentials()));
```

`AllowCredentials()` combined with `AllowAnyHeader`/`AllowAnyMethod` is broad, but it is correctly
scoped to a **single configured origin** (not `AllowAnyOrigin`, which would be rejected with credentials
anyway). Residual risk is low and depends entirely on `Frontend:Origin` being set correctly per
environment. Worth noting that the frontend uses a BFF cookie pattern (`frontend/app/api/auth/login/
route.ts` comment: token "is never exposed to client JS / localStorage"), so the browser does not hold
the JWT directly.

**Fix:** Constrain headers/methods to those actually used, and ensure `Frontend:Origin` is a required,
environment-specific value in production (no localhost fallback).

---

### 8. [Low] Unhandled non-`DomainException` on root/BFF routes leaks detail only in Development
**Confidence: [Confirmed]**

`DomainExceptionMiddleware.cs` maps `DomainException` and `ValidationException` for both surfaces, and
maps a generic `Exception` for the `/api/v1/*` surface (production-opaque, dev shows inner messages —
correctly gated on `_env.IsDevelopment()`). The root/BFF branch, however, catches only `DomainException`,
with no catch-all for an unexpected `Exception` on a non-`/api/v1` route — it falls through to the
framework default. Note the correct framework behavior: ASP.NET Core minimal hosting auto-enables the
Developer Exception Page **only in the Development environment**; with no `UseExceptionHandler`
registered, the Production default for an unhandled exception is an opaque empty-body 500 (no stack
trace). So this is **not a production leak** — it only exposes stack traces if the app is run with
`ASPNETCORE_ENVIRONMENT=Development`, which CLAUDE.md §6 indeed prescribes for this machine.

**Fix (defense-in-depth):** Add a generic root/BFF catch that returns an opaque 500 (mirroring the v1
branch) so the BFF surface is consistent regardless of environment, and ensure Production never runs
with `ASPNETCORE_ENVIRONMENT=Development`.

---

## Verified CORRECT (defenses confirmed working)

- **JWT validation fully configured** — `Program.cs` AddJwtBearer validates issuer (`teas.local`),
  audience (`teas.api`), signing key (HS256 symmetric), and lifetime, with a 1-minute clock skew.
  Signing key/MFA key/JWT lifetime are loaded from the git-ignored `appsettings.Secrets.json`
  (`reloadOnChange`), which `.gitignore` covers (`appsettings.Secrets.json` + `**/appsettings.Secrets.json`).
- **Per-request tenant pin is safe and token-sourced** — `Middleware/TenantMiddleware.cs:34-38` sets
  `app.company_id`/`app.is_super_admin` via **parameterized** `set_config(..., {0}, false)`, with values
  taken from `ITenantContext` (resolved from JWT claims in `Tenancy/HttpTenantContext.cs`), never from
  request input. Settings are reset in a `finally` to avoid pool poisoning.
- **EF global query filter** applies to all 33 `ITenantOwned` entities by convention
  (`AccountingDbContext.cs:126-145`); super-admin and migration-time (`_tenant == null`) bypass is
  explicit and intentional.
- **RLS policy shape is correct** where present — `company_id = current_setting('app.company_id')
  OR is_super_admin`, with `FORCE ROW LEVEL SECURITY` so even the table owner is subject to it.
- **Password hashing** — `Identity/BcryptPasswordHasher.cs`: BCrypt work factor 12; `Verify` swallows
  `SaltParseException` to a constant-false (no exception oracle).
- **Account lockout** — `LoginService.cs`: 5 failed attempts → 15-minute lock; failed MFA also
  increments the counter; password never logged.
- **Public first-run endpoints are correctly gated:**
  - `BootstrapAdminEndpoints.cs` (`/system/setup/bootstrap-admin`, `AllowAnonymous`) — zero-users gate
    inside one transaction under `pg_advisory_xact_lock` (TOCTOU-safe), uses `IgnoreQueryFilters()` for
    the global count, 12-char min password, BCrypt hash, 409 once any user exists.
  - `InstanceSetupEndpoints.cs` (`/system/setup/instance-keys`) — requires authenticated **super-admin**
    (explicit `IsSuperAdmin` claim check, not a permission policy, correct for the no-perms first-run
    state), validates AES-256 key length, refuses to overwrite an existing MFA key, zeroes key bytes,
    temp-file-then-rename write, never logs the key.
- **RBAC enforcement mechanism** — `Authorization/PermissionRequirement.cs` (`PermissionHandler`): JWT
  users need the exact permission claim; super-admins bypass per-permission checks (by design, §4.1);
  **API keys authorize only against their CSV scopes and explicitly never receive super-admin bypass**.
  Policies are generated per `perm:`/`apiperm:` prefix (`PermissionPolicyProvider.cs`) with scheme
  isolation (root = JWT-default, `/api/v1/*` = ApiKey-only) so a JWT on v1 → 401 and an API key on
  root → 401.
- **Per-endpoint authorization coverage (full sweep)** — all 34 files in `Endpoints/` audited by
  diffing `Map(Get|Post|Put|Delete)` counts against `RequireAuthorization`/`AllowAnonymous` and then
  inspecting every file where the counts differed. In every such case the gap is explained by
  **group-level** `RequireAuthorization` on the `MapGroup` (propagated to children) — e.g.
  `MasterEndpoints` (6 groups, each with a permission policy), `SalesChainEndpoints` (3 groups),
  `RbacAdminEndpoints` (2 groups: `Sys.RoleManage` + `Sys.UserManage`), `EmployeeEndpoints`,
  `BusinessUnitEndpoints`, `WhtCertificateEndpoints`, `ApiKeyEndpoints`. The one route mapped on `app`
  rather than its group (`BusinessUnitEndpoints.cs:58` `/business-units/company-setting`) carries its
  own `.RequireAuthorization()`. The anonymous surface is exactly three intended-public routes:
  `/system/setup/bootstrap-admin` (explicit `AllowAnonymous` + zero-state gate), and `/auth/login` +
  `/health` (anonymous **by omission** — no auth metadata). No unintentionally open endpoint found.
  (Note: this is *current* coverage; Finding #2 is that it isn't enforced by a fallback policy — and
  the by-omission anonymity of login/health is precisely why no automated signal would catch a future
  forgotten `RequireAuthorization`.)
- **No IDOR on by-id reads of the un-RLS'd tables** — `AttachmentService.cs:147-158` reads by
  `AttachmentId == id` via `AsNoTracking()` (which keeps the global tenant filter), and
  `SalesChainPdfService.cs` fetches quotation/SO/DO PDFs by id with the filter intact; cross-tenant ids
  are filtered out. All 23 `IgnoreQueryFilters()` call sites are in legitimate pre-tenant/identity
  (UserRepository, PermissionLookup, ApiKeyResolver), super-admin company management, or background
  e-Tax contexts; `CompanySwitchService` documents that it re-pins the tenant after stripping the
  filter — none expose a request-driven cross-tenant read.
- **Company switcher** — `/auth/switch-company/{id}` requires `Master.CompanyManage` (super-admin-only
  permission) **and** re-checks `IsSuperAdmin` in the handler (defense in depth); re-issues a JWT
  (RLS is pinned per session, so a new token is the only way to re-scope).
- **`/me` does not leak tenants** — non-super users go through `GetAsync(tenant.CompanyId)`, never
  `ListAsync` (commented as a §4.7 leak guard); no tax fields exposed in the allowed-company DTO.
- **No SQL injection in the raw-SQL spots** — `NumberSequenceService.cs` (parameterized `DbCommand`),
  `TenantMiddleware.cs` (parameterized `set_config`), `MasterDataServices.cs:270`
  (`ExecuteSqlInterpolatedAsync($"SELECT sys.seed_company_roles({e.CompanyId})")` — `e.CompanyId` is an
  `int`, parameterized by EF), `BootstrapAdminEndpoints.cs:73` (parameterized advisory lock). The other
  raw-SQL sites are in `DbInitializer` (static DDL / file-loaded migration scripts, not user input).
- **No live secrets committed** — `git ls-files` surfaced no `.env`/`.pem`/`.key`/credential files
  (only a test file `InstanceSecretsReloadTests.cs`); SMTP creds are empty in committed config.
- **No PII in logs** — a scan of `Log*` calls for password/token/secret/taxid/national-id matched
  nothing; bootstrap/setup endpoints log "that it ran" + username, never the secret.
- **Frontend does not store the JWT in localStorage** — only reference to localStorage is a comment in
  `frontend/app/api/auth/login/route.ts` asserting the token stays server-side (BFF httpOnly cookie).

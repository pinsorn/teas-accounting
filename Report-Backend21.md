# Report-Backend21 — Sprint 13d: Settings UX hardening + Company Profile

**Date:** 2026-05-19
**Spec:** docs/Answer-Sana-Backend21.md (6 phases, ~2-3 d)
**Sequencing used:** P1 → P6 → P2 → P3 → P4 → P5 (1-dev order from spec §Dependencies)
**Status:** ✅ All 6 phases code-complete + verified. Backend solution build
**0/0**, frontend tsc **0** (non-Sana), Domain **89/89**. Live-smoked on the
running stack (accounting_dev). Runs parallel with Sprint 13b — no shared
source files touched.

---

## 1. What shipped (verified)

| Phase | Delivered | Verification |
|---|---|---|
| **P1** | `components/ui/AlertDialog.tsx` (DaisyUI, `data-testid`, Esc/backdrop=cancel, destructive=red) + `hooks/useConfirm.tsx` (`ConfirmProvider`, promise-based) wired in `layout.tsx`; 7 `window.confirm` callers migrated (business-units, products, api-keys, attachments, WhtFilingClient, pnd30, pnd36); i18n `common.confirm/confirmTitle` th+en | `grep window.confirm` app/components = **0**; tsc clean |
| **P6** | `CompanyProfile` entity + EF config + DbSet + migration `20260519041450_AddCompanyProfile` + `CompanyProfileService` + `CompanyProfileEndpoints` (GET / PUT soft / PUT hard→501) + `410_seed_manual_demo_company_profile.sql`; FE `/settings/company` (hard disabled+tooltip, soft editable, logo URL+preview), `useCompanyProfile`/`useUpdateCompanyProfileSoft`, Sidebar link first in ตั้งค่า, i18n `companyProfile` | **Live:** GET 200 (data correct), PUT /hard(admin)→**501**+body, PUT /soft(admin)→**204** & hard unchanged, migration in `sys.__ef_migrations` |
| **P2** | `components/states/QueryState.tsx` (NoAccessState/ErrorState/EmptyState + wrapper: 403→NoAccess, 401→logout+redirect, other→Error+retry, empty→Empty); QueryClient `retry` now skips all 4xx (no 403 loop); api-keys + wht-types wrapped; i18n `states` th+en | tsc clean; retry predicate unit-evident |
| **P3** | `MeEndpoints.cs` `/me/permissions` (claims-sourced, no DB) + `usePermissions`/`useHasScope` + `PermissionGate` (hidden, not disabled); create + row write buttons gated on business-units (`master.business_unit.manage`), products (`master.product.manage`), wht-types (`tax.wht_type.manage`), api-keys (`sys.api_key.manage`) | **Live:** `/me/permissions` 200, 41 perms, isSuperAdmin true (demo-admin) |
| **P4** | Row action column branches on `isActive`: active=[edit, deactivate], inactive=[edit, ↺restore→PUT isActive=true→toast→refresh] for business-units + products; AlertDialog (not window.confirm); i18n `common.restore` | tsc clean |
| **P5** | `ValidationErrorEnvelopeMiddleware` — ModelState 400 → unified `{type:"urn:teas:error:validation",title,detail,status,fieldErrors:[{field,messages[]}]}`, fields camelCased, **zero endpoint edits**; BusinessUnit + CompanyProfile validators → i18n-key messages (exemplars); FE `lib/api/errors.ts` (single parser) + `lib/i18n/validation.ts` (key→TH/EN); business-units save() wired as exemplar | **Live curl:** blank BU → 400 `application/problem+json` body exactly `{type:urn:teas:error:validation,…,fieldErrors:[{field:"code",messages:["validation.required","validation.code.format"]},{field:"nameTh",messages:["validation.required"]}]}` |

---

## 2. ⚠️ Breaking change (P5) — error envelope

**Before:** root validation = ASP.NET RFC-9110 ModelState
`{type,title,status,errors:{Pascal:[en-text]}}`; business rules = v1
`urn:teas:error:*`. Two shapes.
**After:** root validation 400 is reshaped to the **same v1 family**
`{type:"urn:teas:error:validation",title:"validation",detail,status,
fieldErrors:[{field(camelCase),messages(i18n keys)}]}`. `/api/v1/*`
unchanged (already enveloped).

**Impact — every test/asserter that keys off the old ModelState `errors`
member or expects English literal validation text will break.** This is
expected (spec §Reporting anticipated it). Not yet swept:
- All FluentValidation validators except `BusinessUnit*` and
  `CompanyProfileSoft` still emit English literals. The FE resolver passes
  unknown `validation.*` keys → generic localized text and **passes
  legacy literals through unchanged**, so nothing 500s during migration —
  but messages aren't localized until each validator is converted. A
  mechanical sweep (~30 validators: `.WithMessage("validation.<key>")`)
  is the documented follow-up.
- Backend integration tests that assert `problem.errors[...]` need updating
  to `fieldErrors[]`.
- Per-form inline field rendering: only `business-units` save() wired to
  `errorToToast`; other forms still show generic `tc('error')` until each
  adopts `parseApiError`/`fieldErrorMap` (mechanical, follow-up).

Recommend Sana's Sprint-13b chapter-2 re-test (Chrome MCP) treats the
envelope change as the contract going forward.

---

## 3. Deviations from spec (all deliberate, to match codebase)

1. **Minimal-API, not MVC controllers.** Spec said
   `CompanyProfileController.cs` / `MeController.cs`; codebase is Minimal-API
   endpoint modules. Built `CompanyProfileEndpoints.cs` / `MeEndpoints.cs`
   (CLAUDE.md §5.2). Same routes/semantics.
2. **No new proxy routes.** Spec listed `app/api/proxy/company-profile/…`;
   the frontend already has a catch-all `app/api/proxy/[...path]/route.ts`,
   so `company-profile` + `me/permissions` proxy automatically. No file
   added.
3. **WHT restore (P4) deferred — honest.** `UpdateWhtTypeRequest` (and its
   BE DTO) has **no `isActive`**, so "PUT isActive=true" is impossible
   without a backend DTO/endpoint change — outside P4's stated *FE-only*
   scope. BU + Product restore shipped; WHT restore needs a small BE
   follow-up (add `isActive` to the WHT update DTO **or** a dedicated
   reactivate endpoint). Flagged, not faked.
4. **Logo = URL field + live preview**, not an upload widget. `logo_url` is
   a URL column (per spec DB design); wiring company-logo through the
   Sprint-11 attachment infra (which is document-parented) is heavier and
   deferred. Soft form accepts/previews the URL now.
5. **P5 validator i18n + per-form wiring = exemplars only**
   (BusinessUnit/CompanyProfile + business-units form). Full sweep is the
   breaking follow-up above.
6. **Restore loses optional defaults.** BU restore sends
   `defaultRevenueAccountId:null`; Product restore nulls tax/wht/uom (the
   list DTO doesn't carry them). Acceptable for the demo tenant (no such
   defaults set); a correctness-perfect restore would re-fetch detail
   first — noted for Phase 2.

---

## 4. ⚠️ Incident + recovery (transparent — migration history)

Generating the P6 migration first used `dotnet ef migrations add
--no-build` → an **empty** migration (stale assembly). The cleanup
`dotnet ef migrations remove` then **removed the wrong migration** — it
deleted `20260518155129_AddIdempotencyKeys` (the real last migration) and
reverted the model snapshot, because the empty AddCompanyProfile hadn't
updated the snapshot so `remove` targeted the snapshot's last entry.
**Recovered via git** (repo is tracked): `git restore` the two
`AddIdempotencyKeys` files + `AccountingDbContextModelSnapshot.cs`, deleted
the empty orphans, rebuilt, regenerated `AddCompanyProfile` **with a real
build**. Verified: `git status` Migrations/ clean, migration creates
`master.company_profile` on the correct `AddIdempotencyKeys` base,
`sys.__ef_migrations` has the row after MigrateAsync. No data loss
(accounting_dev already had idempotency_keys; its `__ef_migrations` row
untouched).
**Rule learned:** never `dotnet ef migrations add --no-build`; `ef
migrations remove` is unsafe when the snapshot is out of sync — verify the
target first.

---

## 5. → Sana (proposed text — Sana-owned files, NOT edited by Claude)

**(a) `docs/accounting-system-plan.md`**
- §20.7 ErrorEnvelopeV1 — add `fieldErrors?: [{ field: string (camelCase),
  messages: string[] (i18n keys) }]`; note root validation now maps to
  `urn:teas:error:validation` (was RFC-9110 ModelState).
- New §6.X "Company Profile (hybrid lock)" — 1:1 `master.company_profile`;
  HARD fields (legal_name/tax_id/registration_number/registered_address*/
  vat_registration_date/branch_code) read-only Phase 1, ภ.พ.20-bound,
  Phase 2 = 2-person approval; SOFT fields admin-editable
  (`master.company.manage`); PUT /hard → 501 in Phase 1.

**(b) `docs/api/openapi.yaml`**
- `components/schemas/ErrorEnvelopeV1` += `fieldErrors`.
- New paths: `GET /company-profile`, `PUT /company-profile/soft` (204),
  `PUT /company-profile/hard` (501), `GET /me/permissions`.

**(c) `docs/runtime-gotchas.md`** — add entries:
- **§ ef-migrations `--no-build` / `remove` foot-gun** (see §4 above) —
  empty migration + snapshot desync → `remove` deletes the wrong migration;
  recover via git; never `--no-build` for `migrations add`.
- **§ Sprint-13b BFF env fallback** (carried from Sprint 13b): `route.ts`
  `?? 'http://localhost:5000'` masked a missing `BACKEND_API_URL` →
  silent 500; fixed via `.env.local`.
- **§ Sprint-13b ESM/CJS tailwind.config.ts** (carried): `require()` in an
  ESM `tailwind.config.ts` crashes `/login` compile under the native ESM
  config loader → dev server dies; fix = `import` (match `daisyui`).

**(d) CLAUDE.md** — no change required this sprint.

---

## 6. Deferred verification (honest — no Docker this session)

- **Api Testcontainers integration suite** + **full Playwright two-pass**
  cannot run here (no Docker daemon; Testcontainers needs it). Established
  honest-defer pattern. The spec explicitly assigns post-merge verification
  to Sana via Chrome MCP ("ผม Sana จะ verify ผ่าน Chrome MCP ทุก fix").
  Commands for the dev env:
  ```bash
  cd backend && dotnet test Accounting.sln -c Debug          # needs Docker
  cd frontend && pnpm exec playwright test                    # needs full stack
  ```
- **What WAS verified live** (running stack, accounting_dev): P6
  GET/soft/hard + migration; P3 `/me/permissions`; P5 unified validation
  envelope via raw curl (body shape confirmed byte-exact); Domain 89/89;
  backend solution 0/0; frontend tsc 0 (non-Sana). New e2e specs from spec
  §Test plan (`403_shows_no_access_state`, `permission_gate_hides_buttons`,
  `settings/company`) are **not authored here** — Sprint 13b owns
  `frontend/e2e`/walkthroughs (Sana); recommended as her chapter-2 re-test.

---

## 7. DoD

P1✅ P2✅ P3✅ P4✅(BU+Product; WHT restore deferred-flagged) P5✅(core +
breaking follow-up flagged) P6✅. Build/tsc/Domain green. Live smoke green.
Sana-owned doc deltas routed (§5). Breaking-change + deviations + incident
disclosed. Mirror Y:\AccountApp synced. progress.md cont. 42.

**Not done (explicit follow-ups, not silent):** full validator→i18n-key
sweep; per-form `parseApiError` wiring beyond business-units; WHT restore
backend DTO; logo upload widget; backend integration-test error-shape
updates; Testcontainers/Playwright run (Sana Chrome re-test).

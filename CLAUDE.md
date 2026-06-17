# CLAUDE.md — Instructions for Claude Code

> Read this FIRST, then `progress.md` (newest entry) + `plan.md`. This file is the lean,
> always-true core. Sprint history lives in `progress.md`; deep references live in `docs/`.

---

## 1. Project Identity

- **Name:** Thailand Enterprise Accounting System (TEAS)
- **Type:** B2B+B2C accounting platform for Thai companies, VAT-compliant by design.
- **Stage:** Active build, broad feature coverage shipped — Identity/per-company RBAC (+ super-admin
  company switcher + onboarding wizard), master data, full sales chain Quotation→SO→DO→Invoice→Tax
  Invoice→Receipt, CN/DN, Purchase/VI→PV, WHT 50ทวิ, payroll + ภ.ง.ด.1/1ก/SSO, GL + reports, the RD
  tax-form PDF fillers (ภ.พ.30, ภ.ง.ด.1/3/53/54/50/51, ภ.พ.01/09/36), corporate income tax (ภ.ง.ด.50/51),
  per-company VAT config, multi-tenant RLS, non-VAT mode, document-chain + print tracking, MinVer +
  release-please versioning, and a squashed single-`InitialCreate` migration baseline. **e-Tax is Phase-1
  scaffolding only** (XAdES signer inert + email/mock RD client — NOT a live RD submission). See
  `progress.md` for the current frontier; `plan.md` for what's left. **Not greenfield.**
- **Owner:** Ham (hamtawat@gmail.com)
- **Compliance bar:** must pass a Thai Revenue Department (สรรพากร) audit at any time.

---

## 2. Tech Stack — DO NOT CHANGE WITHOUT APPROVAL

| Layer | Choice |
|---|---|
| Backend | C# / **.NET 10 LTS**, ASP.NET Core Minimal APIs, EF Core 10 |
| DB | **PostgreSQL 16+** via Npgsql; EF Migrations = source of truth |
| Frontend | **Next.js 15** (App Router) + React, TypeScript 5, Tailwind 3, shadcn/ui + Radix |
| State / forms | React Query (TanStack) v5 · React Hook Form + Zod |
| Auth | OAuth2 + JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`) |
| i18n | `next-intl` — **TH primary, EN secondary** |
| Test | xUnit + FluentAssertions + Testcontainers (BE) · Playwright (e2e) |

**Forbidden (do not propose):** MS SQL Server · LIFO costing (illegal, TAS 2) · plaintext PII ·
skipping the multi-tenant `company_id` filter.

---

## 3. Where Everything Is

```
code/
├── CLAUDE.md            ← this file        progress.md ← session log (newest on top)
├── plan.md  ← forward plan                 docs/ ← specs & references
│   ├── accounting-system-plan.md  ⭐ source of truth (§ references throughout)
│   ├── Design(UI).md · Design(Architect).md · api/openapi.yaml
│   ├── etax-xades-spec.md · etax-environment-tiers.md · runtime-gotchas.md
│   └── superpowers/specs/  ← design specs (brainstorm → spec → plan flow)
├── backend/src/{Accounting.Api, .Application, .Domain, .Infrastructure, .Workers}
│   └── (Clean Architecture: Domain → Application → Infrastructure → Api)
├── backend/tests/{Accounting.Api.Tests, .Domain.Tests, .TestKit}
├── frontend/   (app/(dashboard)/*, components/, lib/, messages/{th,en}.json, e2e/)
└── infra/  db/schema.sql (reference only — migrations are authoritative)
```

Re-read the relevant `docs/accounting-system-plan.md` section before designing a feature
(map in §13 below).

---

## 4. Compliance — ห้ามพลาด (NEVER VIOLATE)

Hard legal rules from Thai law. Violations = criminal/financial penalties.

**4.1 Tax Invoice (ม.86/4)** — every Tax Invoice MUST carry all 8 fields: (1) prominent
"ใบกำกับภาษี" label; (2) seller name+address+TaxID(13)+branch(5, `00000`=HQ); (3) buyer
name+address+TaxID+branch **if buyer is VAT-registered**; (4) sequential doc number, **no gaps**;
(5) item name/type/qty/value per line; (6) **VAT shown SEPARATELY** from goods value; (7) issue
date = tax-point date; (8) other required text.

**4.2 Immutability after Post** — a posted Tax Invoice is immutable; NEVER write code that edits/
deletes a posted document. Corrections = Credit Note + reissue. Enforced at DB (trigger) AND app layer.

**4.3 Document numbering** — `MM-YYYY-PREFIX-NNNN` (PV adds `-CATEGORY-`). Sequential, no gaps,
monthly reset. Number assigned **only on POST/Issue**, never on Draft. Voided numbers stay (status
VOIDED), never reused.

**4.4 e-Tax** — Phase 1 = e-Tax Invoice by Email: XAdES-BES-signed XML + email customer + cc
`csemail@rd.go.th` simultaneously; per-invoice real-time; after submit status=SUBMITTED (errors → CN).

**4.5 ภ.พ.30 (VAT return)** — monthly, due 15th of next month; `auto` (RD API) or `manual`
(per-company, see §4.6); auto mode auto-submits at 23:00 day 15.

**4.6 VAT mode/rate/ภ.พ.30 mode = COMPANY MASTER DATA (per-company-vat-mode spec, 2026-06-11)** —
live on `master.companies` (`vat_registered`, `vat_rate`, `pnd30_submission_mode`), served per
request by `ICompanyTaxConfigService`. Settable only via `POST/PUT /companies` (super-admin
permission `Master.CompanyManage`); every tax-field change writes `audit.activity_log`
(`tax_config_change`). **NEVER** a regular user-facing settings UI. Non-VAT doc labels stay in
appsettings (cosmetic, instance-wide).

**4.7 Multi-tenant isolation** — every business table has `company_id INT NOT NULL`; PostgreSQL RLS
on every business table; `SET LOCAL app.company_id` per request; EF global query filter as backup.

**4.8 Audit trail** — every state change → `audit.activity_log`; critical post-fields immutable (DB
trigger); 5-year retention (พรบ.การบัญชี ม.14); e-Tax XML append-only.

---

## 5. Coding Conventions

**General:** code/comments/commits in English; user-facing strings Thai (default) + English via i18n.
`DateTimeOffset` internally, convert to `Asia/Bangkok` only at display; **CE calendar only** (never
Buddhist internally). Money = `decimal` (4 dp), never `double`/`float`. IDs = `long` (BIGINT), `int` for lookups.

**Backend (.NET):** Clean Architecture; built-in DI (no Autofac); **async everywhere** — never
`.Result`/`.Wait()`/`Task.Run` in request paths, always `async Task`+`await`+`CancellationToken`.
FluentValidation before domain; ProblemDetails (RFC 7807) out, domain exceptions inside. DbContext
Scoped, config Singleton. Structured logging, no PII. EF migrations only (raw SQL just for triggers/
RLS/views in `Migrations/SqlScripts/`). Naming: `ix_<table>_<col>`, `fk_<table>_<ref>`, `ck_<table>_<rule>`.

**Frontend (Next.js):** App Router; Server Components by default, `'use client'` only when needed.
RHF+Zod forms; React Query (client) / native fetch (server). `app/(auth)/*` + `app/(dashboard)/*`.
Tailwind + `components/ui/`; shadcn/ui via CLI. `next-intl` (edit BOTH `messages/th.json` + `en.json`,
TH primary). TH Sarabun New / Inter via `next/font`. Thai locale number/date.

**SQL/EF migration commands** — see §6 (run from `W:`, never `--no-build` after entity edits).

---

## 6. Dev Environment & Gotchas (READ — hard-won, costly if ignored)

**Servers (this machine):**
- **Backend** on `http://localhost:5080`. MUST run with `ASPNETCORE_ENVIRONMENT=Development`
  (otherwise login → 500). Start: `cd W:\; $env:ASPNETCORE_ENVIRONMENT='Development';
  $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api` (background).
- **Frontend** on `http://localhost:3000`. `pnpm` is often NOT on PATH → use
  `node node_modules\next\dist\bin\next dev` (or `… build`) from `frontend\`.
- **Login:** `admin / Admin@1234` (company 1). JWT field = `access_token`.
- **Dev DB:** `Host=localhost;Port=5432;Database=accounting_dev;Username=accounting;Password=accounting_dev_password`.

**`subst` drives (recreate if missing — they vanish on resume):**
- `U:` → `<repo>\code`, `W:` → `<repo>\code\backend`. `subst U: <path>` / `subst W: <path>`.
- **Run `dotnet test` / `dotnet ef` / `dotnet run` from `W:`** — the real path is too long and
  spawning from it throws `Win32Exception (87)`. `dotnet build W:\Accounting.sln` works from anywhere.

**Backend build/exe lock:** the running API on :5080 locks `Accounting.Api.exe` + dependency DLLs.
**Kill it before a full build** (`Get-NetTCPConnection -LocalPort 5080 -State Listen` → `Stop-Process
-Force`), build, then restart. (Building Application/Infrastructure/Domain alone doesn't need the kill.)

**⚠️ EF migration discipline (footgun that corrupted the dev DB before):**
- **NEVER `dotnet ef … --no-build` after editing entities.** Stale `Api/bin` Infrastructure.dll →
  empty/wrong diff, and `ef migrations remove` once ran a `Down` on the live dev DB. Always **build
  the solution first, then run `dotnet ef` WITH build**, from `W:`.
- Commit the generated `Migrations/*` files **together with** the code that needs them.
- Generate: `dotnet ef migrations add <Name> --project src\Accounting.Infrastructure --startup-project src\Accounting.Api` (from `W:`); apply: `dotnet ef database update …`.

**Integration tests:** `PostgresFixture` reads env **`TEAS_TEST_PG`** (any Postgres) before falling
back to Testcontainers (Docker — usually unavailable here). A clean `teas_test` DB exists on the dev
PG; run from `W:\tests\Accounting.Api.Tests` with
`$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'`.
The fixture migrates + seeds it. If `teas_test` is missing, create the empty DB first (no psql/docker
here → a tiny Npgsql console `CREATE DATABASE teas_test`). **Also set `$env:TEAS_REPO_ROOT=<repo root
holding CLAUDE.md+docs/>`** when running the RBAC `RbacAuthMapTests`/`RbacMatrixTests` from `W:` — they
write a generated map under `docs/rbac/` and the subst `W:` drive clamps their CLAUDE.md auto-walk, so
without it they throw "Could not locate the TEAS repo root" and look like RBAC drift when they are not.

**VAT mode toggle** (per-company since 2026-06-11 — `Tax:VatMode` config is GONE): VAT behavior
follows `master.companies.vat_registered`/`vat_rate` of the caller's company. Dev non-VAT testing =
use a non-VAT company (or `UPDATE master.companies SET vat_registered=false WHERE company_id=1` +
revert). No API restart needed. Integration tests: NEVER flip company 1 — create a fresh company via
`Accounting.Api.Tests/Fixtures/TestCompanyFactory.CreateAsync(conn, vatRegistered:false)`.

**Frontend build:** never run `next build` while `next dev` is running (corrupts `.next`) — stop dev,
`rm -rf .next`, build, restart dev. `tsc --noEmit` is the fast gate during dev (hot-reload covers the rest).

---

## 7. Operating Model — overseer + subagents (context-efficient)

Default working mode for large/multi-part work, to keep the main agent's context lean:

1. **Main agent = planner + overseer.** Brainstorm → spec (`docs/superpowers/specs/`) → phased plan →
   dispatch subagents → verify + integrate + commit. Hold the architecture/compliance picture; don't
   burn context on mechanical edits a subagent can do.
2. **Spawn subagents for bounded, well-specified chunks.** Each spawn starts COLD and re-derives
   context (expensive) → prefer **fewer, larger, well-bounded** tasks over many tiny ones.
3. **Sequential vs parallel:**
   - **Parallel** ONLY when file sets are **completely disjoint** (e.g. an isolated BE module vs an
     unrelated FE page). Use `run_in_background`.
   - **Sequential** whenever tasks share files (most multi-phase FE work) — dispatch one, verify, then
     the next. Avoids merge/clobber conflicts.
4. **Main agent keeps (do NOT delegate cold):** debugging, compliance-critical logic, EF
   migrations/schema, and anything touching the §6 footguns. A cold subagent repeats footguns.
5. **Every subagent prompt must carry the §6 environment briefing** (subst/W:, kill :5080, never
   `ef --no-build`, `TEAS_TEST_PG`, FE tsc rules) + the spec path + explicit **verification gates**
   (build 0/0 · Domain ≥ baseline · new tests pass **2× consecutive** on `teas_test` · FE tsc 0) +
   "do NOT `git commit`".
6. **Main agent runs the final consolidated gate + the commit.** Subagents self-gate; main confirms
   the whole picture is green before committing.

---

## 8. Test-Data Discipline (non-negotiable)

The gate requires a test to **pass 2–3 consecutive runs on the SAME shared DB**. Therefore: **every
test that inserts a row with a UNIQUE constraint MUST use `Accounting.TestKit.TestIds.*`** (`CustomerCode()`,
`VendorCode()`, `ProductCode()`, `ExpenseCategoryCode()`, `WhtTypeCode()`, `Email()`, `TaxId()`,
`FuturePeriod()`, `Suffix()`, …) or an explicit `Guid` — never a hardcoded code/period. FE e2e mirror:
`frontend/e2e/helpers/test-ids.ts`. Fixed values are fine ONLY for pure unit tests, read-only assertions
on seeded reference data, or serialization-shape checks. A test that passes run 1 but fails run 2 = a
data-collision bug → fix with `TestIds.*` immediately.

---

## 9. Verification Checklist (before claiming "done" / before a PR)

- [ ] Unit + integration tests written and **passing** (real PG; `TEAS_TEST_PG`).
- [ ] EF migration generated (built first, not `--no-build`), reviewed, applied; committed with code.
- [ ] No `company_id` leak (tenant filter in every query/join). No PII in logs.
- [ ] Compliance-affecting logic has a test citing the legal section (`// ม.86/4 #6`).
- [ ] OpenAPI updated if endpoints changed (flag delta for Sana).
- [ ] No `.Result`/`.Wait()`/`Task.Run` in async paths.
- [ ] FE `tsc --noEmit` 0; `next build` 0/0 before ship (dev stopped first).
- [ ] Thai labels reviewed. `progress.md` prepended + `plan.md` ticked.

Evidence before assertions — quote the actual command output; if a test fails, say so.

---

## 10. Critical "DO NOT" List

❌ edit/delete posted Tax Invoices · ❌ non-monotonic doc numbers · ❌ skip `company_id` filter ·
❌ store passwords/tokens/PII unencrypted · ❌ `localStorage` for sensitive data · ❌ `.Result`/`.Wait()`
on async · ❌ expose VAT rate/mode in UI · ❌ delete from any audit/log table · ❌ Buddhist calendar
internally · ❌ commit `.env`/secrets · ❌ Simplified Tax Invoice (ม.86/6 — Full only) · ❌ inventory
tracking unless asked · ❌ trust user input for `doc_date` (always `today` in `Asia/Bangkok`) ·
❌ `dotnet ef … --no-build` after entity edits (§6) · ❌ `git commit` unless Ham asks.

---

## 11. Autonomy Boundaries

**Can do autonomously:** implement code matching `docs/` specs · EF migrations · tests · refactors ·
fix bugs in shipped features · update OpenAPI to match new endpoints.

**ASK Ham first:** new endpoints not in `openapi.yaml` · tech-stack change · **any change to a §4
compliance rule** · 3rd-party services with cost · schema changes beyond the plan · anything touching
e-Tax submission. If docs are ambiguous or contradict, ASK — do not improvise on compliance.

---

## 12. Progress & Plan Tracking — MANDATORY

Two living files at repo root, the source of truth for "where are we" (not memory/chat):
- **`progress.md`** — append-only, newest on top. End each session by **prepending** a dated entry:
  status table, what shipped, exact verification results (test counts, build status), env notes.
- **`plan.md`** — forward plan; edit in place (tick ☑, add, re-order by impact).
A task isn't done until recorded in `progress.md` with the `plan.md` item ticked. If either file is
missing, recreate from `docs/accounting-system-plan.md` §22.

---

## 13. Reference Map (where to look)

| Topic | Location |
|---|---|
| Source of truth (legal, flow, schema, roadmap) | `docs/accounting-system-plan.md` (§2 legal, §6 sales, §7 PV/AP, §9 GL, §12 tax, §13 e-Tax, §15.10/.11 doc specs, §16 .env config, §17.3 expense categories + numbering, §18 compliance, §19 schema, §22 roadmap) |
| UI per screen | `docs/Design(UI).md` |
| Architecture decisions | `docs/Design(Architect).md` |
| REST contract | `docs/api/openapi.yaml` |
| e-Tax XAdES signing (.NET) | `docs/etax-xades-spec.md` |
| **e-Tax env tiers (Tier 1→2→3, config-only)** | `docs/etax-environment-tiers.md` |
| Runtime gotchas (read before similar code) | `docs/runtime-gotchas.md` |
| Design specs (recent features) | `docs/superpowers/specs/*.md` |
| User-manual production workflow (Sana track) | `docs/manual/` (chapter-by-chapter, sequential) |

`/graphify` (codebase knowledge graph, `graphify-out/`, gitignored) is **optional** — useful only for
cold-start orientation or "where is X used" on unfamiliar areas. For this layered repo, `Grep`/`Glob` +
the layout above are usually faster and never stale; treat any graph as a hint, verify against live files.

---

**Start of session: read this → `progress.md` (top) → `plan.md`. Then work.**

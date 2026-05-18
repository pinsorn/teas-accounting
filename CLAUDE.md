# CLAUDE.md — Instructions for Claude Code

> Read this file **first** before doing anything. It defines the project context, conventions, and what NOT to do.

---

## 0. ⚠️ First-Time Setup (Run BEFORE Anything Else)

### 0.1 Install required Claude Code plugins — **GLOBAL** scope

Both plugins are required. Install them before writing any code in this project:

#### a) `dotnet-skills` — for .NET 10 backend

[Aaronontheweb/dotnet-skills](https://github.com/Aaronontheweb/dotnet-skills) — **30 specialized skills + 5 sub-agents** for professional .NET development (C#, EF Core, testing, performance, Akka.NET, Aspire, etc.) — battle-tested production patterns.

```
/plugin marketplace add Aaronontheweb/dotnet-skills
/plugin install dotnet-skills
```

Manual install fallback:
```bash
git clone https://github.com/Aaronontheweb/dotnet-skills.git /tmp/dotnet-skills
mkdir -p ~/.claude/skills ~/.claude/agents
cp -r /tmp/dotnet-skills/skills/* ~/.claude/skills/
cp -r /tmp/dotnet-skills/agents/* ~/.claude/agents/
```

#### b) `ui-ux-pro-max` — for Next.js frontend UI/UX

[nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill) — AI design intelligence skill, activates automatically when you do UI/UX work. Supports Next.js, React, Tailwind, DaisyUI, shadcn — perfect match for this project's stack.

```
/plugin marketplace add nextlevelbuilder/ui-ux-pro-max-skill
/plugin install ui-ux-pro-max@ui-ux-pro-max-skill
```

CLI install fallback:
```bash
npm install -g uipro-cli
cd code/    # at the project root
uipro init --ai claude
```

#### Update both plugins later

```
/plugin marketplace update
```

### 0.2 Next.js Documentation Rule

<!-- BEGIN:nextjs-agent-rules -->
# Next.js: ALWAYS read docs before coding
Before any Next.js work, read the relevant doc in `node_modules/next/dist/docs/` **if present**.
If that directory is absent (Next 15.x ships without it), fetch current docs via the
**Context7 MCP** server (`mcp__context7__*`) — query for the exact Next.js version pinned in
`package.json` and the topic you're about to touch (App Router, Server Components, route
handlers, etc.). Your training data is outdated — live docs are the source of truth.
<!-- END:nextjs-agent-rules -->

**Practical for Next 15.0.0 in this project:**
1. `cd frontend && pnpm install`
2. Confirm: `ls node_modules/next/dist/docs/` — **expected absent for Next 15**
3. Use Context7 MCP for any App Router / Server Components / route handler / middleware /
   `next/font` / `next-intl` work — query "next.js 15 app router <topic>"
4. Never code App Router from memory — semantics changed substantially between 13/14/15

If Context7 MCP is also unavailable, ask in `Question-Backend{N}.md` before writing code.
Do not improvise.

### 0.3 Verification

Confirm setup before proceeding to Phase 1:

- [ ] `/plugin list` shows `dotnet-skills` (or `~/.claude/skills/dotnet/` exists)
- [ ] `/plugin list` shows `ui-ux-pro-max` (or `~/.claude/skills/ui-ux-pro-max/` exists)
- [ ] `frontend/node_modules/next/dist/docs/` is readable
- [ ] Read `docs/accounting-system-plan.md` end-to-end

---

## 1. Project Identity

**Name:** Thailand Enterprise Accounting System (TEAS)  
**Type:** B2B+B2C accounting platform for Thai companies, designed VAT-compliant from day 1  
**Stage:** Greenfield — repo is scaffolded but no business logic implemented yet  
**Owner:** Ham (hamtawat@gmail.com)  
**Compliance bar:** Must pass Thai Revenue Department (สรรพากร) audit at any time  

---

## 2. Tech Stack — DO NOT CHANGE WITHOUT APPROVAL

| Layer | Choice | Version |
|---|---|---|
| Backend language | C# / .NET | **.NET 10 LTS** |
| Web framework | ASP.NET Core Minimal APIs | 10.x |
| ORM | Entity Framework Core | 10.x |
| Database | **PostgreSQL** (local self-hosted Phase 1) | **16+** |
| EF Provider | Npgsql.EntityFrameworkCore.PostgreSQL | latest stable |
| Frontend | Next.js (App Router) + React | 15.x |
| Frontend lang | TypeScript | 5.x |
| Styling | Tailwind CSS | 3.x |
| UI primitives | shadcn/ui + Radix | latest |
| State | React Query (TanStack Query) | 5.x |
| Forms | React Hook Form + Zod | latest |
| Auth | OAuth2 + JWT (Built-in `Microsoft.AspNetCore.Authentication.JwtBearer`) | — |
| Container | Docker + Docker Compose (local) | latest |
| Test | xUnit + FluentAssertions + Testcontainers (backend); Playwright (e2e) | — |

**Forbidden choices** (do not propose):
- MS SQL Server (we already chose PostgreSQL)
- LIFO inventory costing (illegal under TAS 2)
- Storing decrypted PII in plaintext
- Skipping the multi-tenant `company_id` filter

---

## 3. Where Everything Is

```
code/
├── CLAUDE.md                ← you are here
├── README.md                ← human-readable getting started
├── docs/                    ← READ THESE FIRST
│   ├── accounting-system-plan.md   ⭐ source of truth (1900+ lines)
│   ├── Design(UI).md               ⭐ UI specification per screen
│   ├── Design(Architect).md        ⭐ architecture decisions
│   ├── Cost-Estimate.md
│   └── api/openapi.yaml            ⭐ REST API contract
├── backend/
│   ├── src/
│   │   ├── Accounting.Api/         (ASP.NET Core entry)
│   │   ├── Accounting.Application/ (use cases / app services)
│   │   ├── Accounting.Domain/      (entities + business rules)
│   │   ├── Accounting.Infrastructure/  (EF Core, integrations)
│   │   └── Accounting.Workers/     (background jobs)
│   └── tests/
├── frontend/                ← Next.js 15 app
├── design/                  ← design tokens, component patterns
├── infra/                   ← docker-compose, .env.example
└── db/schema.sql            ← PostgreSQL schema reference (EF Migrations = source of truth)
```

**Always re-read `docs/accounting-system-plan.md` before designing a new feature.**

---

## 4. Compliance — ห้ามพลาด (NEVER VIOLATE)

These are hard legal rules from Thai law. Violations = criminal/financial penalties.

### 4.1 Tax Invoice Issuance (มาตรา 86/4)

Every Tax Invoice MUST have all 8 fields:
1. ป้าย "ใบกำกับภาษี" (or "ใบกำกับภาษี/ใบเสร็จรับเงิน") prominent
2. Seller name + address + Tax ID (13 digits) + branch code (5 digits, `00000`=HQ)
3. Buyer name + address + Tax ID + branch code **if buyer is VAT-registered**
4. Sequential document number (no gaps allowed)
5. Item name, type, quantity, value (per line)
6. **VAT amount shown SEPARATELY** from the goods value
7. Issue date = Tax Point date (same day)
8. Other text as required (e.g., "ใบกำกับภาษี/ใบเสร็จรับเงิน")

### 4.2 Immutability After Post

- Posted Tax Invoice = immutable. NEVER write code that allows editing/deleting posted documents.
- Any correction → must issue a Credit Note + new Tax Invoice (Reissue).
- This is enforced both at the DB layer (trigger) AND the application layer.

### 4.3 Document Numbering

- Format: `MM-YYYY-PREFIX-NNNN` (or `MM-YYYY-PREFIX-CATEGORY-NNNN` for Payment Vouchers)
- Sequential, no gaps — sequence reset monthly
- Number assigned ONLY when document is POSTED (not on Draft save)
- Voided numbers stay in DB (status=VOIDED), never reused

### 4.4 e-Tax Submission

- Phase 1: e-Tax Invoice **by Email** — XML signed via XAdES-BES + email customer + cc `csemail@rd.go.th` SAME TIME
- Real-time submission (per-invoice, not batch)
- After submission, status = SUBMITTED — any error needs Credit Note

### 4.5 ภ.พ.30 (VAT Return)

- Monthly, due by 15th of next month
- Two modes (env-config): `auto` (RD Open API) or `manual` (accountant downloads file)
- In `auto` mode: auto-submit safety net at 23:00 on day 15

### 4.6 Configuration in .env Only

- VAT rate, VAT mode, ภ.พ.30 mode — **NEVER expose as UI settings**
- Changes require deployment process (audit trail in git)
- See `docs/accounting-system-plan.md` Section 16 for full list

### 4.7 Multi-tenant Isolation

- Every business table has `company_id INT NOT NULL`
- PostgreSQL Row-Level Security MUST be enabled on every business table
- Session must `SET LOCAL app.company_id = <id>` per request via middleware
- EF Core global query filter as backup

### 4.8 Audit Trail

- Every state change → `audit.activity_log` entry
- Critical fields after post → immutable (DB trigger enforced)
- Document retention: **5 years minimum** (พรบ.การบัญชี ม.14)
- e-Tax XML stored in append-only storage

---

## 5. Coding Conventions

### 5.1 General

- **Language for code:** English (variable names, comments, commit messages, log messages)
- **Language for user-facing strings:** Thai (default) + English (secondary) via i18n
- **Date handling:** `DateTimeOffset` everywhere internally, convert to `Asia/Bangkok` only at display
- **Money:** `decimal` (4 decimal places), never `double` / `float`
- **IDs:** `long` (BIGINT in DB), `int` for lookups

### 5.2 Backend (.NET)

- **Architecture:** Clean Architecture (Domain → Application → Infrastructure → API)
- **Dependency Injection:** built-in container, no Autofac/StructureMap
- **Async everywhere:** never `.Result` or `.Wait()` — always `async Task<T>` + `await`
- **CancellationToken:** propagate through all async methods
- **Validation:** Zod-style with FluentValidation; reject invalid before reaching domain
- **Errors:** ProblemDetails (RFC 7807) for HTTP responses; custom domain exceptions inside
- **DI lifetime:** Transient default, Scoped for DbContext, Singleton for config/factories
- **Logging:** Microsoft.Extensions.Logging structured logs (JSON) — Serilog optional
- **Config:** Options pattern + IOptionsSnapshot, read from .env via `DotNetEnv` or `IConfiguration`
- **Migrations:** EF Core only — never hand-write SQL migrations except for raw SQL ops (triggers, RLS, views)
- **Tests:** xUnit + FluentAssertions; Testcontainers for PostgreSQL integration tests

### 5.3 Frontend (Next.js)

- **App Router** (not Pages Router)
- **Server Components by default**, Client Components only when needed (`'use client'`)
- **Forms:** React Hook Form + Zod schemas
- **Data fetching:** React Query (TanStack Query) for client; native fetch on server
- **Auth:** Server-side session check via middleware
- **Routing convention:**
  - `app/(auth)/login`, `app/(auth)/setup-mfa`
  - `app/(dashboard)/...` for authenticated pages
- **Styling:** Tailwind utility classes; component layer in `components/ui/`
- **Component library:** shadcn/ui (install via CLI, don't reinvent)
- **i18n:** `next-intl` — TH primary, EN secondary
- **Font:** TH Sarabun New for Thai, Inter for English (load via `next/font`)
- **Number/Date format:** Thai locale by default, formatted via `Intl.NumberFormat('th-TH')`

### 5.4 SQL & EF

- Generate migrations: `dotnet ef migrations add <Name> --project Accounting.Infrastructure --startup-project Accounting.Api`
- Apply: `dotnet ef database update --project Accounting.Infrastructure --startup-project Accounting.Api`
- Triggers, RLS policies, views → write raw SQL in `Migrations/SqlScripts/` and reference via `migrationBuilder.Sql(File.ReadAllText(...))`
- Index naming: `ix_<table>_<col>`
- Foreign key naming: `fk_<table>_<ref>`
- Check constraint naming: `ck_<table>_<rule>`

---

## 6. What Phase 1 Looks Like

Read `docs/accounting-system-plan.md` Section 22 — Implementation Roadmap.

**Phase 1 — Month 1-3: Foundation** (where Claude Code should start):

- [ ] Boot scaffolded backend (`docker compose up postgres` + `dotnet run`)
- [ ] Boot frontend (`pnpm install && pnpm dev`)
- [ ] Implement Identity service: user table, login, MFA TOTP
- [ ] RBAC: roles, permissions, user_roles
- [ ] Master Data CRUD: company, branch, customer, vendor, chart_of_accounts
- [ ] Document Prefix Registry seed
- [ ] Expense Category seed
- [ ] Number sequence service (atomic increment)
- [ ] Basic GL: journal_entries table, posting service
- [ ] EF Core migration with all critical triggers + RLS policies

**Phase 2** (next):
- Sales: Quotation → SO → DO → Tax Invoice → Receipt
- Purchase: Vendor Invoice → Payment Voucher (require expense_category)
- WHT certificate 50 ทวิ
- Customer/Vendor receipts

(See full roadmap in `docs/accounting-system-plan.md` Section 22)

---

## 7. Verification Checklist (Before Every PR)

Claude Code should verify each item before claiming "done":

- [ ] Unit tests written and passing (`dotnet test`)
- [ ] Integration test for the new endpoint (Testcontainers PostgreSQL)
- [ ] EF migration generated, reviewed, applied successfully
- [ ] No `company_id` leak (multi-tenant filter present in all queries)
- [ ] No PII printed in logs
- [ ] Compliance-affecting logic has a corresponding test referencing the legal section (e.g., `// Test for ม.86/4 #6`)
- [ ] OpenAPI spec updated if new endpoint added
- [ ] No `.Result` / `.Wait()` / `Task.Run` in async code paths
- [ ] Thai labels reviewed (we're shipping for Thai users)

---

## 8. When in Doubt

| Question | Where to look |
|---|---|
| What field does ใบกำกับภาษี need? | `docs/accounting-system-plan.md` Section 15.3 |
| What's the workflow for Credit Note? | `docs/accounting-system-plan.md` Section 6.5 |
| What does the UI for Tax Invoice look like? | `docs/Design(UI).md` Section 7.6 |
| Which expense categories exist? | `docs/accounting-system-plan.md` Section 17.3 |
| What's the API for creating a Quotation? | `docs/api/openapi.yaml` `/quotations` POST |
| What env vars control behavior? | `docs/accounting-system-plan.md` Section 16.1 |
| Tax rate / VAT mode? | `infra/.env.example` → values come from `.env`, not UI |
| How to sign e-Tax XML? | `docs/Design(Architect).md` Section 9 |

**If the docs are ambiguous or contradict, ASK Ham. Do not improvise on compliance.**

---

## 9. Skill Boundaries

Claude Code can autonomously:
- ✓ Write code matching specs in `docs/`
- ✓ Generate EF migrations
- ✓ Add tests
- ✓ Refactor for code quality
- ✓ Fix bugs in implemented features
- ✓ Update OpenAPI spec to match new endpoints

Claude Code should ASK before:
- ⚠ Adding new endpoints not in `openapi.yaml`
- ⚠ Changing tech stack
- ⚠ Modifying any compliance rule (Section 4 above)
- ⚠ Adding 3rd-party services with cost implications
- ⚠ Changing schema beyond what's in the plan
- ⚠ Touching anything related to e-Tax submission (high-risk area)

---

## 10. Critical "DO NOT" List

- ❌ DO NOT write code that allows editing posted Tax Invoices
- ❌ DO NOT generate document numbers in non-monotonic ways
- ❌ DO NOT skip the `company_id` filter in queries
- ❌ DO NOT store passwords/tokens/PII without encryption
- ❌ DO NOT use `localStorage` for sensitive data on frontend
- ❌ DO NOT call `.Result` / `.Wait()` on async tasks
- ❌ DO NOT expose VAT rate/mode in UI settings
- ❌ DO NOT delete from any audit/log table
- ❌ DO NOT mix Buddhist and Christian calendar internally (CE only)
- ❌ DO NOT commit `.env` or secrets
- ❌ DO NOT implement Simplified Tax Invoice (ม.86/6) — we only do Full
- ❌ DO NOT add inventory tracking unless explicitly asked
- ❌ DO NOT trust user input for `doc_date` — it's always `today` in `Asia/Bangkok`

---

## 11. Local Development Quickstart

```bash
# 1. Clone repo
cd code/

# 2. Boot infra (PostgreSQL, Redis, MailHog for local email testing)
cd infra/
cp .env.example .env
docker compose up -d

# 3. Backend
cd ../backend/
dotnet restore
dotnet ef database update --project src/Accounting.Infrastructure --startup-project src/Accounting.Api
dotnet run --project src/Accounting.Api

# 4. Frontend (separate terminal)
cd ../frontend/
pnpm install
cp .env.local.example .env.local
pnpm dev

# 5. Open
# Frontend: http://localhost:3000
# Backend API: http://localhost:5000 (Swagger UI at /swagger)
# MailHog (local mail inbox): http://localhost:8025
# PostgreSQL: localhost:5432 (user: accounting / db: accounting_dev)
```

---

## 12. Reference Section Map (for quick lookup)

When implementing a feature, `Ctrl+F` these section labels in the markdown docs:

| Topic | File | Section |
|---|---|---|
| **e-Tax XAdES signing (.NET impl)** | **docs/etax-xades-spec.md** | full file — algorithm, QualifyingProperties XML, .NET code |
| **Runtime gotchas (read before similar code)** | **docs/runtime-gotchas.md** | 13 latent bugs caught by gate, root cause + prevention pattern per category |
| Legal framework | accounting-system-plan.md | §2 |
| User roles & RBAC | accounting-system-plan.md | §4 |
| Sales document flow | accounting-system-plan.md | §6 |
| **Payment Voucher / AP flow** | accounting-system-plan.md | §7 (esp 7.2, 7.3 3-way match, 7.4 Input VAT rules) |
| GL & posting | accounting-system-plan.md | §9 |
| Tax module (VAT + WHT) | accounting-system-plan.md | §12 |
| e-Tax submission | accounting-system-plan.md | §13 |
| Document specs (50 ทวิ, RV, PV) | accounting-system-plan.md | §15.10, §15.11 |
| Tax config (.env) | accounting-system-plan.md | §16 |
| Numbering + **Expense Category prefix** | accounting-system-plan.md | §17.3 (the 19 default categories + GL/tax/WHT defaults) |
| Compliance checklist | accounting-system-plan.md | §18 |
| Database schema | accounting-system-plan.md | §19 |
| External API | accounting-system-plan.md | §20 |
| Roadmap | accounting-system-plan.md | §22 |
| UI screens (incl. Vendor Invoice Entry §8.1, Payment Voucher §8.2) | Design(UI).md | §5-13 |
| Architecture | Design(Architect).md | §1-20 |

### 12.1 Payment Voucher — Quick Reference (HEAD of mind for current work)

When implementing the Payment module, the non-negotiables:

- **Document number:** `MM-YYYY-PV-{CATEGORY}-NNNN` — sub-prefix MANDATORY (e.g. `05-2026-PV-RENT-0001`). See §17.3 of the plan.
- **Expense Category required** at creation (`purchase.payment_vouchers.expense_category_id NOT NULL`). Picking the category should auto-fill:
  - default expense GL account
  - default input-VAT tax code (with `is_recoverable_vat` — ENT and VEHI are non-deductible per ม.82/5)
  - default WHT type (RENT=5%, SVC=3%, ADS=2%, etc.)
- **SoD enforced:** `created_by ≠ approved_by` (DB CHECK constraint `ck_pv_sod` + app-level check)
- **WHT certificate (50 ทวิ)** must be generated on post if any line has WHT — see §15.10
- **GL posting on Post:** Dr.AP / Dr.Expense (or via vendor invoice settlement) / Cr.Cash-Bank / Cr.WHT Payable
- **Vendor info snapshot:** copy vendor name/tax_id/branch_code into the PV at post time — vendors can be edited later
- **OpenAPI:** `POST /payment-vouchers`, `POST /payment-vouchers/{id}/approve` — see `docs/api/openapi.yaml`
- **Endpoints already mapped in Program.cs** are: Auth, Customer, Master, Journal, TaxInvoice — **Payment endpoints still need to be added** (`app.MapPaymentVoucherEndpoints()` not present yet)
- **Seed reference (use for tests):** `docs/accounting-system-plan.md` §17.3 lists all 19 categories with defaults; `db/schema.sql` has the `sys_acc.expense_categories` table (Postgres version)

---

## 14. e-Tax environment switching (Sprint 13c)

Tier 1 → 2 → 3 is **config-only** (no code edit per environment). Full audit + tier matrix + operational runbook:
- `docs/etax-environment-tiers.md` — 3-tier swap matrix + config keys per tier + transition procedure
- `Answer-Sana-Backend18.md` — Sprint 13c spec (8 phases shipped)

### Tier 1 (local dev mock) — startup
1. `./dev-tools/gen-test-cert.sh dev123 backend/secrets/dev-cert.pfx` — self-signed PFX for XAdES signing
2. `docker compose -f docker-compose.dev.yml up -d postgres mailhog mockserver` — local stack (MailHog SMTP capture + MockServer RD API)
3. Set in `appsettings.Development.json`: `ETax:Enabled=true`, `ETax:AutoSendOnTaxInvoicePost=true`, `ETax:Signing:PfxPath=secrets/dev-cert.pfx`, `ETax:Signing:PfxPassword=dev123`
4. `dotnet run --project backend/src/Accounting.Api`
5. MailHog Web UI: `http://localhost:8025` (sent emails) · MockServer: `http://localhost:1080` (RD API mocks)

### Critical rules
- **Config keys are .env / appsettings ONLY** — never UI (CLAUDE.md §4.6 reinforced)
- **RD client selector:** `RdApi:Provider` = `"Mock"` (Tier 1) | `"RdUat"` (Tier 2) | `"RdProduction"` (Tier 3)
- **`etax.submissions` is append-only** — 5-year legal retention per พรบ.การบัญชี ม.10; UPDATE/DELETE rejected by DB trigger
- **`ETax:Email:RedirectAllToEmail` = Tier 2 safety net** — CRITICAL: prevents UAT runs from emailing real customers. Must be set to UAT mailbox when transitioning Tier 1→2. Production (Tier 3) sets to `null` to enable real customer sending.
- **`ETax:Validation:RequireSchemaPass`** — Tier 1 `false` (graceful skip if XSDs not loaded), Tier 2/3 `true` (mandatory)

### Phase 0/2 prereqs (NOT in this sprint)
- Real RD UAT credentials (4-6 wk lead time, requires Service Provider registration with กรมสรรพากร)
- ETDA มกค.14-2563 XSDs (external controlled artifact, download per `etax-schemas/README.md`)
- CA-issued Class 2 PFX certificate (~3-5k บาท/ปี from TDID/INET/CAT)
- HSM impl (`HsmETaxSigner` — Phase 2 when first customer needs HSM)
- Durable retry queue (Hangfire/Quartz — Phase 2 at load)

---

## 13. Progress & Plan Tracking — MANDATORY

Two living files at repo root track state across sessions. **Read both at the start of
every session, update both before ending one.**

- **`progress.md`** — append-only log (newest on top). At the end of a working session,
  prepend a dated entry: status snapshot table, what was completed, build/test commands,
  exact verification results, environment notes. Never rewrite history; only prepend.
- **`plan.md`** — the forward plan (what's left, prioritised). Edit in place when scope
  or priority changes: tick items done (☑), add new ones, re-order by impact.

Rules:
- These are the source of truth for "where are we" — do not rely on memory or chat history.
- Keep entries concrete: file paths, commands, exact results (test counts, build status),
  not vague prose.
- A task is not "done" until its result is recorded in `progress.md` and the matching
  `plan.md` item is ticked.
- If `progress.md` / `plan.md` are missing, recreate them from `docs/accounting-system-plan.md`
  §22 before doing other work.

---

**End of CLAUDE.md — start by reading `docs/accounting-system-plan.md` end to end, then `progress.md` + `plan.md`.**

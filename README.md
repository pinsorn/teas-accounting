# TEAS — Thailand Enterprise Accounting System

A B2B + B2C accounting platform for Thai companies, **VAT-compliant by design** and built around
Thai Revenue Department (สรรพากร) rules. Full document chain from Quotation through Tax Invoice and
Receipt, withholding tax (50 ทวิ), GL + financial reports, **print-ready RD tax-form PDFs**, payroll,
and a multi-tenant RBAC core.

> **Release v1.0.0** — see [Releases](https://github.com/pinsorn/teas-accounting/releases) for the
> Windows x64 and Linux x64 backend builds.
>
> Backend: **.NET 10** (ASP.NET Core Minimal APIs, EF Core 10) · DB: **PostgreSQL 16** ·
> Frontend: **Next.js 15** (App Router, TypeScript, Tailwind, shadcn/ui).

---

## Features

- **Sales chain** — Quotation → Sales Order → Delivery Order → **Tax Invoice** → Receipt, plus
  Credit / Debit Notes, with sequential gap-free document numbering (ม.86/4).
- **Purchases & WHT** — Vendor Invoice → Payment Voucher → Withholding-tax certificate (50 ทวิ).
- **Tax filings** — generates the filled RD PDFs: ภ.พ.30, ภ.ง.ด.1 / 3 / 53 / 54, ภ.ง.ด.50 / 51 (CIT),
  ภ.พ.01 / 09, ภ.พ.36 reverse charge.
- **Payroll** — runs, payslips, PIT + social-security (ปกส.), ภ.ง.ด.1 / 1ก.
- **General ledger & reports** — journals, trial balance, P&L, balance sheet, monthly tax summary,
  AP aging.
- **Compliance** — per-company VAT config, posted-document immutability, PostgreSQL row-level
  security per tenant, and an append-only audit trail.
- **RBAC** — per-company roles, super-admin company switcher, and a first-run onboarding wizard.

---

## Tech stack

| Layer         | Choice                                                                |
|---------------|-----------------------------------------------------------------------|
| Backend       | C# / .NET 10, ASP.NET Core Minimal APIs, EF Core 10 (migrations)       |
| Database      | PostgreSQL 16 via Npgsql, row-level security                          |
| Frontend      | Next.js 15 (App Router) + React, TypeScript 5, Tailwind 3, shadcn/ui   |
| State / forms | React Query (TanStack) v5, React Hook Form + Zod                       |
| Auth          | OAuth2 + JWT bearer                                                    |
| i18n          | next-intl — Thai primary, English secondary                           |
| Tests         | xUnit + FluentAssertions + Testcontainers (backend), Playwright (e2e)  |

---

## Quick start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) and [pnpm](https://pnpm.io) (`corepack enable` works)
- [Docker](https://www.docker.com) (for PostgreSQL) — or a local PostgreSQL 16

### 1. Clone

```bash
git clone https://github.com/pinsorn/teas-accounting.git
cd teas-accounting
```

### 2. Start PostgreSQL

```bash
docker compose up -d
```

This creates an `accounting_dev` database with the credentials the backend expects (see
`backend/src/Accounting.Api/appsettings.json`). Prefer your own PostgreSQL? Create an empty
`accounting_dev` database with user `accounting` / password `accounting_dev_password`, or edit the
`ConnectionStrings:Postgres` value.

### 3. Run the backend (port 5080)

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Accounting.Api
```

On first start the app applies EF migrations and the SQL bootstrap scripts (RLS, triggers, and seed
data, including the admin user and demo companies) automatically — no manual migration step needed.
Wait for `http://localhost:5080/health` to return `200`.

> Windows PowerShell:
> ```powershell
> cd backend
> $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
> dotnet run --project src\Accounting.Api
> ```

### 4. Run the frontend (port 3000)

```bash
cd frontend
pnpm install
echo "BACKEND_API_URL=http://localhost:5080" > .env.local   # point the BFF proxy at the backend
pnpm dev
```

Open <http://localhost:3000>.

### 5. Log in

| User    | Password     | Scope                  |
|---------|--------------|------------------------|
| `admin` | `Admin@1234` | Company 1, super-admin |

Two demo companies are seeded: **company 2** (VAT-registered) and **company 3** (non-VAT). A
super-admin can switch between them from the top bar.

---

## Tests

Backend integration tests need a PostgreSQL database. Point them at one via `TEAS_TEST_PG` (the
fixture migrates + seeds it), or let Testcontainers spin one up if Docker is available.

```bash
cd backend
TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password" \
TEAS_REPO_ROOT="$(git rev-parse --show-toplevel)" \
  dotnet test Accounting.sln
```

Frontend type-check: `cd frontend && pnpm exec tsc --noEmit`.

---

## Project layout

```
backend/
  src/
    Accounting.Domain          # entities, enums, domain rules
    Accounting.Application      # use cases, DTOs, abstractions
    Accounting.Infrastructure   # EF Core, services, RD PDF fillers, SQL bootstrap scripts
    Accounting.Api              # ASP.NET Core minimal-API host
    Accounting.Workers          # background jobs
  tests/                        # xUnit (Domain + Api integration) + a shared TestKit
frontend/
  app/(dashboard)/*             # screens   ·  components/, lib/, messages/{th,en}.json
docs/                           # specs, OpenAPI contract, RD-form references, user manual
infra/db/schema.sql             # reference only — EF migrations are authoritative
```

---

## Versioning & releases

The assembly version is derived from git tags by [MinVer](https://github.com/adamralph/minver)
(`vX.Y.Z`), surfaced on `GET /system/info` and in the dashboard footer.
[release-please](https://github.com/googleapis/release-please) turns conventional commits on `main`
into release PRs (version bump + changelog + tag). CI (`.github/workflows/ci.yml`) builds and tests
the backend and type-checks the frontend.

---

## User manual

A step-by-step user manual (Thai, with screenshots) lives in [`docs/manual/`](docs/manual/) —
~46 captured walkthroughs across installation / onboarding, master data, the sales and purchase
chains, payroll, tax filings, and reports, plus a categorized
[API reference](docs/manual/api/index.md).

- **Read it as a PDF** (self-contained, screenshots embedded):
  [`docs/manual/AccountProject-User-Manual-TH-v0.5.pdf`](docs/manual/AccountProject-User-Manual-TH-v0.5.pdf)
  — also attached to the [v1.0.0 release](https://github.com/pinsorn/teas-accounting/releases/tag/v1.0.0).
- **Single-page HTML:** [`docs/manual/generated/print.html`](docs/manual/generated/print.html)
  (rendered from the walkthroughs; open it with the sibling `docs/manual/captures/` folder present).
- **Browse as a site / markdown:** start at [`docs/manual/index.md`](docs/manual/index.md), or:

  ```bash
  pip install mkdocs mkdocs-material
  mkdocs serve -f docs/manual/mkdocs.yml   # then open http://localhost:8000
  ```

The PDF/HTML are regenerated from the Playwright captures via `frontend/manual/gen-markdown.mjs`
(markdown + `print.html`) and `gen-pdf.mjs` (`print.html` → PDF).

---

## Documentation & compliance

- `docs/accounting-system-plan.md` — the master specification (legal references, flows, schema,
  roadmap). `docs/api/openapi.yaml` — the REST contract. `CLAUDE.md` — engineering conventions.
- This system encodes Thai tax law (VAT under ประมวลรัษฎากร, withholding tax, CIT, payroll PIT /
  ปกส.). Posted tax documents are immutable; corrections are issued as Credit Notes. The seeded demo
  data is for development only and is not tax advice.

## License

Proprietary — see the repository owner.

# Thailand Enterprise Accounting System (TEAS)

ระบบบัญชี enterprise สำหรับธุรกิจไทย — VAT-compliant, e-Tax-ready, multi-company

> **For Claude Code:** Read `CLAUDE.md` first.  
> **For humans:** Read `docs/accounting-system-plan.md` for full spec.

---

## Tech Stack

- Backend: **.NET 10 LTS** + ASP.NET Core Minimal APIs + EF Core 10
- Database: **PostgreSQL 16** (local self-hosted)
- Frontend: **Next.js 15** + TypeScript + Tailwind CSS + shadcn/ui
- Container: Docker Compose for local development

## Project Structure

```
.
├── CLAUDE.md          # Instructions for Claude Code (read first)
├── README.md          # This file
├── docs/              # Full specifications
│   ├── accounting-system-plan.md     ⭐ master plan
│   ├── Design(UI).md
│   ├── Design(Architect).md
│   ├── Cost-Estimate.md
│   └── api/openapi.yaml
├── backend/           # .NET 10 solution
│   ├── src/
│   │   ├── Accounting.Api/
│   │   ├── Accounting.Application/
│   │   ├── Accounting.Domain/
│   │   ├── Accounting.Infrastructure/
│   │   └── Accounting.Workers/
│   └── tests/
├── frontend/          # Next.js 15 app
├── design/            # Design tokens + component patterns
├── infra/             # Docker Compose + .env.example
└── db/                # SQL reference (EF Migrations = source of truth)
```

## Quick Start (Local Development)

### 1. Prerequisites

- .NET 10 SDK
- Node.js 20+ + pnpm
- Docker Desktop
- PostgreSQL 16 client tools (optional, `psql` for debugging)

### 2. Boot infrastructure

```bash
cd infra/
cp .env.example .env
docker compose up -d
```

This starts:
- PostgreSQL on `localhost:5432`
- Redis on `localhost:6379`
- MailHog (local SMTP catcher) on `localhost:1025` + UI on `localhost:8025`

### 3. Backend

```bash
cd backend/
dotnet restore
dotnet ef database update --project src/Accounting.Infrastructure --startup-project src/Accounting.Api
dotnet run --project src/Accounting.Api
```

API runs on `http://localhost:5000` (Swagger UI at `/swagger`).

### 4. Frontend

```bash
cd frontend/
pnpm install
cp .env.local.example .env.local
pnpm dev
```

Opens at `http://localhost:3000`.

## Compliance Notes (สำคัญ — อ่านก่อนเขียน code)

- ทุก Tax Invoice = immutable หลัง post (กฎหมายไทย)
- ห้ามเปิด UI แก้ VAT rate (อยู่ใน .env เท่านั้น)
- e-Tax XML signed + email RD ทุกใบ real-time
- ภ.พ.30 ยื่นรายเดือน, deadline วันที่ 15 ของเดือนถัดไป
- Document number = `MM-YYYY-PREFIX-NNNN` (sequential, no gaps)
- Multi-tenant: ทุก query บังคับ filter ตาม `company_id`

ดูรายละเอียดที่ `docs/accounting-system-plan.md` Section 18 (Compliance Checklist)

## Status

Phase 0: ✓ Documentation complete  
Phase 1 (Foundation): 🟡 Scaffolded, awaiting implementation  
Phase 2-5: ⏳ Pending  

ดู roadmap ที่ `docs/accounting-system-plan.md` Section 22

## License

Proprietary — internal use only

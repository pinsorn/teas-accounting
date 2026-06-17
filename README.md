<p align="center">
  <img src="frontend/public/teas-logo.png" alt="TEAS logo" width="160">
</p>

# TEAS — ระบบบัญชีวิสาหกิจสำหรับธุรกิจไทย

แพลตฟอร์มบัญชี B2B + B2C สำหรับบริษัทไทย **ออกแบบให้ VAT-compliant** และยึดตามกฎกรมสรรพากร
(ประมวลรัษฎากร) — รองรับสายเอกสารครบตั้งแต่ใบเสนอราคาจนถึงใบกำกับภาษีและใบเสร็จ, ภาษีหัก ณ ที่จ่าย
(50 ทวิ), บัญชีแยกประเภท + รายงานการเงิน, **PDF แบบฟอร์มสรรพากรที่กรอกแล้วพร้อมพิมพ์**, เงินเดือน,
และระบบ multi-tenant + RBAC

> **Release v1.0.0** — ดู [Releases](https://github.com/pinsorn/teas-accounting/releases) สำหรับ
> backend build (Windows x64 / Linux x64) + คู่มือ PDF
>
> Backend: **.NET 10** (ASP.NET Core Minimal APIs, EF Core 10) · DB: **PostgreSQL 16** ·
> Frontend: **Next.js 15** (App Router, TypeScript, Tailwind, shadcn/ui)

---

## ความสามารถหลัก (Features)

- **สายเอกสารขาย** — ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → **ใบกำกับภาษี** → ใบเสร็จรับเงิน, พร้อม
  ใบลดหนี้ / ใบเพิ่มหนี้ ใบกำกับภาษีเต็มรูปตาม ม.86/4 (ครบ 8 ช่อง, แสดง VAT แยก), เลขเอกสารเรียงลำดับ
  ไม่ขาดช่วง (`MM-YYYY-PREFIX-NNNN`, ออกเลขตอน post) อัตรา VAT ต่อบรรทัด **คำนวณฝั่ง server** จาก
  ค่าตั้งของบริษัท — ไม่เชื่อค่าจาก client
- **ซื้อ + ภาษีหัก ณ ที่จ่าย** — ใบสั่งซื้อ → บันทึกซื้อ → ใบสำคัญจ่าย → หนังสือรับรองหัก ณ ที่จ่าย
  (50 ทวิ), มี guard ต่อบรรทัด (ผู้ขายไม่จด VAT → 0%, รหัสยกเว้น/0% → 0%, อัตรามาตรฐานจากค่าตั้ง)
- **PDF แบบฟอร์มสรรพากร** (กรอกแล้ว พร้อมพิมพ์) — ภ.พ.30; ภ.ง.ด.1 / 1ก / 3 / 53 / 54;
  ภ.ง.ด.50 / 51 (ภาษีเงินได้นิติบุคคล); ภ.พ.01 / 09; ภ.พ.36 (reverse charge — คำนวณ + ลง JV อัตโนมัติ)
- **เงินเดือน** — รอบจ่าย, สลิป, ภาษีเงินได้บุคคล + ประกันสังคม (ปกส.), ภ.ง.ด.1 / 1ก
- **บัญชีแยกประเภท + รายงาน** — สมุดรายวัน, งบทดลอง, งบกำไรขาดทุน, งบดุล, สรุปภาษีรายเดือน,
  สรุปยอดขาย, อายุหนี้เจ้าหนี้
- **Multi-tenant + RBAC** — หนึ่ง deployment รองรับหลายบริษัทด้วย **PostgreSQL row-level security**;
  ค่าตั้ง VAT ต่อบริษัท; บทบาท + สิทธิ์ละเอียดต่อบริษัท; super-admin สลับบริษัท; onboarding wizard
- **Compliance** — เอกสารที่ post แล้วแก้ไม่ได้ (DB trigger + app layer), audit trail แบบ append-only,
  แก้ไขผ่านใบลดหนี้ — ยึดตามประมวลรัษฎากร + พ.ร.บ. การบัญชี

### สถาปัตยกรรมโดยสรุป

.NET 10 **Clean Architecture** (Domain → Application → Infrastructure → Api) + worker host;
**PostgreSQL 16** พร้อม RLS ต่อ tenant; auth แบบ OAuth2 / JWT; EF Core migrations เป็น source of
truth ของ schema ส่วน frontend Next.js ทำหน้าที่ **BFF proxy** ไป API — รายละเอียดเต็มอยู่ใน
[as-built specification](docs/accounting-system-plan.md)

---

## รายการฟังก์ชันโดยละเอียด (สิ่งที่ระบบทำได้)

### ข้อมูลหลัก (Master data)
- **บริษัท (multi-tenant):** สร้าง / แก้ไขบริษัท + profile (ที่อยู่จดทะเบียน, สาขา, โลโก้); ตั้งค่า
  **การจด VAT, อัตรา, และโหมดยื่น ภ.พ.30 ต่อบริษัท** (เฉพาะ super-admin, ทุกการแก้ field ภาษีถูก audit)
- **ลูกค้า & ผู้ขาย:** ระเบียนเต็ม — เลขประจำตัวผู้เสียภาษี, รหัสสาขา, สถานะ VAT, ธงต่างชาติ, บัญชี
  ธนาคาร; รองรับทั้งบุคคลธรรมดาและนิติบุคคล
- **สินค้า / บริการ:** รหัสภาษีซื้อ-ขาย default, ประเภทสินค้า (good / service / exempt), ธง
  ซื้อได้ / ขายได้, ผูก business unit
- **ข้อมูลอ้างอิง:** ผังบัญชี, หมวดค่าใช้จ่าย, ประเภทหัก ณ ที่จ่าย, prefix เลขเอกสาร, business units, รหัสภาษี

### ขาย (Sales)
- **สายเอกสาร:** ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → **ใบกำกับภาษี** → ใบเสร็จ — แต่ละใบ create /
  edit / list / PDF + เปลี่ยนสถานะ (send, accept, issue, post); แปลงเอกสารใบหนึ่งเป็นใบถัดไปในสาย
- **ใบกำกับภาษีเต็มรูป (ม.86/4):** ครบ 8 ช่อง, แสดง VAT แยก, เลขเรียงไม่ขาดช่วง ออกตอน post
- **ใบลดหนี้ & ใบเพิ่มหนี้** (tax adjustment notes) อ้างใบกำกับที่ post แล้ว
- **เส้นทาง non-VAT:** ใบวางบิล → ใบเสร็จ สำหรับบริษัทไม่จด VAT (ไม่มีใบกำกับภาษี ไม่มี VAT)
- **VAT ฝั่ง server:** อัตรา VAT ต่อบรรทัด derive จากค่าตั้งบริษัท (มาตรฐาน / ยกเว้น / 0%) ไม่เชื่อ client
- **cross-reference** เอกสาร, **print tracking**, และ **ตรวจเลขขาดช่วง**

### ซื้อ + ภาษีหัก ณ ที่จ่าย (Purchases & WHT)
- **ใบสั่งซื้อ → บันทึกซื้อ → ใบสำคัญจ่าย** พร้อม create / approve / post + PDF
- **ภาษีหัก ณ ที่จ่าย** คำนวณต่อบรรทัดพร้อม guard (ผู้ขายไม่จด VAT → 0%, ยกเว้น → 0%, อัตรามาตรฐาน
  จากค่าตั้ง); ออก **หนังสือรับรอง 50 ทวิ** (ภ.ง.ด.3 / 53 / 54 ตามประเภทเงินได้) พร้อม PDF
- **ผู้ขายต่างชาติ / reverse charge:** ม.70 → ภ.ง.ด.54, และ ม.83/6 → ภ.พ.36

### เงินเดือน (Payroll)
- **พนักงาน** (master); **รอบจ่ายเงินเดือน** รายเดือน (create / approve / post); **สลิป** PDF รายคนหรือ zip
- คำนวณ **ภาษีเงินได้บุคคล** + ลดหย่อน + **ประกันสังคม (ปกส.)**; ไฟล์นำส่ง สปส.
- แบบหัก ณ ที่จ่าย: **ภ.ง.ด.1** (รายเดือน), **ภ.ง.ด.1ก** (รายปี), และ **50 ทวิ** รายพนักงาน

### แบบยื่นภาษี (PDF กรอกแล้ว)
- **VAT:** ภ.พ.30 (แบบแสดงรายการ); ภ.พ.36 (reverse charge — คำนวณ + ลง JV อัตโนมัติ)
- **หัก ณ ที่จ่าย:** ภ.ง.ด.1 / 1ก / 3 / 53 / 54
- **ภาษีเงินได้นิติบุคคล:** ภ.ง.ด.51 (ครึ่งปี) + ภ.ง.ด.50 (ปี) พร้อมการคำนวณ CIT
- **จดทะเบียน VAT:** ภ.พ.01 / ภ.พ.09

### บัญชีแยกประเภท + รายงาน (GL & reports)
- **ลงสมุดรายวันอัตโนมัติ** ตอน post; สมุดรายวันแบบ manual; **เปิด / ปิดงวดบัญชี**
- รายงาน: **งบทดลอง, งบกำไรขาดทุน, งบดุล, สรุปภาษีรายเดือน, สรุปยอดขาย, อายุหนี้เจ้าหนี้,
  ทะเบียนภาษีซื้อ / ขาย, ทะเบียน + อายุภาษีหัก ณ ที่จ่ายค้างรับ, ตรวจเลขขาดช่วง**

### การจัดการ & สิทธิ์ (Admin & access)
- **RBAC ต่อบริษัท:** บทบาท, สิทธิ์ละเอียด, ผูก user-role
- **super-admin สลับบริษัท**; **onboarding wizard** ครั้งแรก
- **External API** (`/api/v1`) พร้อม API key, idempotency, และผูก business unit ได้
- **audit trail** ทุกการเปลี่ยนสถานะ; แนบไฟล์ (attachments) บนเอกสาร

### แพลตฟอร์ม
- แยก tenant ด้วย **PostgreSQL row-level security**
- เวอร์ชันแสดงบน `GET /system/info` + footer ของ dashboard; UI **ไทย / อังกฤษ** (ไทยเป็นหลัก)

---

## Tech stack

| ส่วน | เทคโนโลยี |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core Minimal APIs, EF Core 10 (migrations) |
| Database | PostgreSQL 16 ผ่าน Npgsql, row-level security |
| Frontend | Next.js 15 (App Router) + React, TypeScript 5, Tailwind 3, shadcn/ui |
| State / forms | React Query (TanStack) v5, React Hook Form + Zod |
| Auth | OAuth2 + JWT bearer |
| i18n | next-intl — ไทยเป็นหลัก, อังกฤษรอง |
| Test | xUnit + FluentAssertions + Testcontainers (backend), Playwright (e2e) |

---

## เริ่มใช้งาน (Quick start)

### สิ่งที่ต้องมี
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org) และ [pnpm](https://pnpm.io) (`corepack enable` ก็ได้)
- [Docker](https://www.docker.com) (สำหรับ PostgreSQL) หรือ PostgreSQL 16 ที่ติดตั้งเอง

### 1. Clone

```bash
git clone https://github.com/pinsorn/teas-accounting.git
cd teas-accounting
```

### 2. เปิด PostgreSQL

```bash
docker compose up -d
```

สร้าง database `accounting_dev` พร้อม credentials ที่ backend คาดไว้ (ดู
`backend/src/Accounting.Api/appsettings.json`) ถ้าใช้ PostgreSQL ของตัวเอง ให้สร้าง database
`accounting_dev` เปล่า ๆ ด้วย user `accounting` / password `accounting_dev_password` หรือแก้ค่า
`ConnectionStrings:Postgres`

### 3. รัน backend (port 5080)

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Accounting.Api
```

ตอนเริ่มครั้งแรก ระบบจะ apply EF migrations + SQL bootstrap scripts (RLS, triggers, seed data รวมถึง
user admin + บริษัทตัวอย่าง) ให้อัตโนมัติ — ไม่ต้องสั่ง migrate เอง รอจน `http://localhost:5080/health`
ตอบ `200`

> Windows PowerShell:
> ```powershell
> cd backend
> $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
> dotnet run --project src\Accounting.Api
> ```

### 4. รัน frontend (port 3000)

```bash
cd frontend
pnpm install
echo "BACKEND_API_URL=http://localhost:5080" > .env.local   # ชี้ BFF proxy ไป backend
pnpm dev
```

เปิด <http://localhost:3000>

### 5. เข้าสู่ระบบ

| ผู้ใช้ | รหัสผ่าน | ขอบเขต |
|---|---|---|
| `admin` | `Admin@1234` | บริษัท 1, super-admin |

มีบริษัทตัวอย่าง 2 บริษัท: **บริษัท 2** (จด VAT) และ **บริษัท 3** (ไม่จด VAT) — super-admin สลับได้จาก
แถบบน

---

## ทดสอบ (Tests)

Backend integration tests ต้องมี PostgreSQL ชี้ผ่าน `TEAS_TEST_PG` (fixture จะ migrate + seed ให้)
หรือปล่อยให้ Testcontainers สร้างเองถ้ามี Docker

```bash
cd backend
TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password" \
TEAS_REPO_ROOT="$(git rev-parse --show-toplevel)" \
  dotnet test Accounting.sln
```

Frontend type-check: `cd frontend && pnpm exec tsc --noEmit`

---

## โครงสร้างโปรเจกต์

```
backend/
  src/
    Accounting.Domain          # entities, enums, domain rules
    Accounting.Application      # use cases, DTOs, abstractions
    Accounting.Infrastructure   # EF Core, services, RD PDF fillers, SQL bootstrap scripts
    Accounting.Api              # ASP.NET Core minimal-API host
    Accounting.Workers          # background jobs
  tests/                        # xUnit (Domain + Api integration) + TestKit
frontend/
  app/(dashboard)/*             # หน้าจอ  ·  components/, lib/, messages/{th,en}.json
docs/                           # spec, OpenAPI, RD-form references, คู่มือผู้ใช้
infra/db/schema.sql             # อ้างอิงเท่านั้น — EF migrations คือ source of truth
```

---

## เวอร์ชัน & release

เวอร์ชันของ assembly มาจาก git tag โดย [MinVer](https://github.com/adamralph/minver) (`vX.Y.Z`)
แสดงบน `GET /system/info` และ footer ของ dashboard [release-please](https://github.com/googleapis/release-please)
แปลง conventional commits บน `main` เป็น release PR (bump เวอร์ชัน + changelog + tag) ส่วน CI
(`.github/workflows/ci.yml`) build + test backend และ type-check frontend

---

## คู่มือผู้ใช้ (User manual)

คู่มือผู้ใช้แบบ step-by-step (ภาษาไทย มีภาพประกอบ) อยู่ใน [`docs/manual/`](docs/manual/) — ~46
walkthrough ครอบคลุมการติดตั้ง / onboarding, ข้อมูลหลัก, สายขาย-ซื้อ, เงินเดือน, แบบยื่นภาษี และรายงาน
พร้อม [API reference](docs/manual/api/index.md) แยกหมวด

- **อ่านเป็น PDF** (รวมภาพในตัว):
  [`docs/manual/AccountProject-User-Manual-TH-v0.5.pdf`](docs/manual/AccountProject-User-Manual-TH-v0.5.pdf)
  — แนบไว้ใน [release v1.0.0](https://github.com/pinsorn/teas-accounting/releases/tag/v1.0.0) ด้วย
- **HTML หน้าเดียว:** [`docs/manual/generated/print.html`](docs/manual/generated/print.html)
  (เปิดพร้อมโฟลเดอร์ `docs/manual/captures/` ที่อยู่ข้างกัน)
- **เปิดเป็นเว็บ / markdown:** เริ่มที่ [`docs/manual/index.md`](docs/manual/index.md) หรือ:

  ```bash
  pip install mkdocs mkdocs-material
  mkdocs serve -f docs/manual/mkdocs.yml   # เปิด http://localhost:8000
  ```

---

## เอกสาร & compliance

- `docs/accounting-system-plan.md` — as-built specification (สถาปัตยกรรม, compliance, modules, schema)
  `docs/api/openapi.yaml` — REST contract `CLAUDE.md` — engineering conventions
- ระบบยึดกฎหมายภาษีไทย (VAT ตามประมวลรัษฎากร, หัก ณ ที่จ่าย, CIT, เงินเดือน PIT / ปกส.) เอกสารภาษีที่
  post แล้วแก้ไม่ได้ แก้ไขผ่านใบลดหนี้ ข้อมูลตัวอย่างที่ seed ไว้ใช้สำหรับ dev เท่านั้น ไม่ใช่คำแนะนำทางภาษี

## License

[GNU AGPL-3.0](LICENSE) — ใช้ / แก้ไข / นำไปเปิดเป็นบริการได้ แต่ถ้า **แก้ไขแล้วนำไปให้บริการ** (รวมถึง
ผ่านเครือข่าย / SaaS) **ต้องเปิดเผย source ที่แก้ไขด้วย** ยินดีรับ contribution — ดู
[`CONTRIBUTING.md`](CONTRIBUTING.md)

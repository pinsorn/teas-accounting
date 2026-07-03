<p align="center">
  <img src="frontend/public/teas-logo.png" alt="TEAS logo" width="160">
</p>

<h1 align="center">TEAS — Thailand Enterprise Accounting System</h1>

<p align="center">
  ระบบบัญชีที่เกิดมาเพื่อกฎหมายภาษีไทย — ไม่ใช่ระบบบัญชีทั่วไปที่เอามา "ปรับให้เข้ากับสรรพากร" ทีหลัง
</p>

<p align="center">
  <a href="https://github.com/pinsorn/teas-accounting/releases"><img src="https://img.shields.io/github/v/release/pinsorn/teas-accounting?label=release&color=orange" alt="release"></a>
  <a href="https://github.com/pinsorn/teas-accounting/actions/workflows/ci.yml"><img src="https://github.com/pinsorn/teas-accounting/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-AGPL--3.0-blue" alt="AGPL-3.0"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4" alt=".NET 10">
  <img src="https://img.shields.io/badge/Next.js-15-black" alt="Next.js 15">
  <img src="https://img.shields.io/badge/PostgreSQL-16-336791" alt="PostgreSQL 16">
</p>

---

ทุกระบบบัญชีออกใบแจ้งหนี้ได้ แต่ธุรกิจไทยต้องการมากกว่านั้น: **ใบกำกับภาษีเต็มรูปครบ 8 ช่องตาม ม.86/4**
เลขเอกสารเรียงลำดับห้ามขาดช่วง เอกสารที่ post แล้วห้ามแก้ หัก ณ ที่จ่ายพร้อม 50 ทวิ แบบ ภ.พ.30 / ภ.ง.ด.
ที่กรอกเสร็จพร้อมยื่น และ audit trail ที่พร้อมให้สรรพากรตรวจได้ทุกเมื่อ

TEAS สร้างข้อกำหนดพวกนี้ไว้ **ในตัว schema และ domain logic ตั้งแต่ต้น** — ไม่ใช่ checkbox ที่แปะทีหลัง:
เอกสารที่ post แล้วถูกล็อกด้วย **database trigger** (ไม่ใช่แค่ปุ่มที่หายไปจากหน้าจอ), เลขเอกสารออกตอน post
เท่านั้นและไม่ reuse, อัตรา VAT คำนวณ **ฝั่ง server จากค่าตั้งบริษัท** (ไม่เชื่อ client), และทุกการแก้ไข
หลังบันทึกต้องเดินผ่านใบลดหนี้/ใบเพิ่มหนี้ตามครรลองของประมวลรัษฎากร

> **v1.10** — **TEAS Connect เปิดประตูให้ AI**: agent อย่าง Claude ต่อเข้าระบบผ่าน **MCP** ได้ทั้ง
> API key (Claude Code / Desktop) และ **OAuth 2.1 + PKCE** (Claude Mobile / native connectors) —
> อ่านข้อมูล ร่างเอกสาร ดึง PDF ได้ภายใต้ scope ที่จำกัด (ร่างได้ แต่ **post เองไม่ได้** — อำนาจอนุมัติ
> ยังอยู่กับมนุษย์เสมอ)
> **v1.9** — **screen == print**: จอกับกระดาษใช้ข้อมูลก้อนเดียวกันผ่าน canonical paper DTO
> (`GET /{doc}/{id}/paper`) + redesign เอกสารทั้งชุดให้สวย อ่านง่าย จบหน้าเดียว

---

## ระบบทำอะไรได้

**สายขายครบวงจร** — ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → ใบกำกับภาษี → ใบเสร็จรับเงิน พร้อม
ใบลดหนี้/ใบเพิ่มหนี้อ้างใบกำกับเดิม, ใบวางบิล, แปลงเอกสารต่อสายอัตโนมัติ, cross-reference,
print tracking, ตรวจเลขขาดช่วง และเส้นทาง **non-VAT เต็มรูปแบบ** สำหรับกิจการที่ไม่จด VAT

**สายซื้อ + หัก ณ ที่จ่าย** — ใบสั่งซื้อ → บันทึกใบกำกับภาษีซื้อ → ใบสำคัญจ่าย → **หนังสือรับรอง 50 ทวิ**
คำนวณ WHT ต่อบรรทัดพร้อม guard (ผู้ขายไม่จด VAT → บังคับ 0%), รองรับผู้ขายต่างชาติครบทั้ง
ม.70 → ภ.ง.ด.54 และ reverse charge ม.83/6 → ภ.พ.36 (คำนวณ + ลง JV ให้อัตโนมัติ), กระทั่งเคส
"ผู้รับไม่ยอมให้หัก" (gross-up ออกภาษีแทนทั้งแบบครั้งเดียว/ตลอดไป) ก็คิดฐานภาษีให้ถูกต้อง

**เงินเดือน** — รอบจ่ายรายเดือน (draft → approve → post → paid), สลิป PDF รายคน/zip ทั้งรอบ,
ภาษีเงินได้บุคคลธรรมดาแบบขั้นบันได + ลดหย่อน, ประกันสังคมพร้อมไฟล์นำส่ง สปส.1-10 (ทั้ง text
e-Service และ PDF), ภ.ง.ด.1 / 1ก และ 50 ทวิรายพนักงาน

**แบบภาษีพร้อมยื่น** — PDF ฟอร์มจริงของกรมสรรพากร กรอกเสร็จพร้อมพิมพ์:
ภ.พ.30 · ภ.ง.ด.1 / 1ก / 3 / 53 / 54 · **ภ.ง.ด.50 / 51** (คำนวณ CIT ทั้ง SME ladder, ขาดทุนยกมา,
งบฐานะ) · ภ.พ.01 / 09 · ภ.พ.36 — บวก **ไฟล์ "Format กลาง" (.txt) สำหรับ RD Prep**
(ภ.ง.ด.3 / 53 + ภ.พ.30 → `.rdx` → อัปโหลด e-Filing) และ PDF งบการเงินประกอบการยื่น ภ.ง.ด.50

**บัญชีแยกประเภท + รายงาน** — ลง JV อัตโนมัติทุกการ post, สมุดรายวัน manual, เปิด/ปิดงวด,
งบทดลอง, งบกำไรขาดทุน, งบดุล, **สรุปภาษีรายเดือนหน้าเดียว** (รายได้/VAT/WHT พร้อม drill-down),
ทะเบียนภาษีซื้อ-ขาย, อายุหนี้เจ้าหนี้, WHT ค้างรับ

**Multi-tenant + RBAC จริงจัง** — หลายบริษัทใน deployment เดียว แยกข้อมูลด้วย **PostgreSQL
row-level security** (บังคับที่ DB ไม่ใช่แค่ WHERE clause), ค่าตั้ง VAT ต่อบริษัท, บทบาท + สิทธิ์
ละเอียดต่อบริษัท (พิสูจน์ด้วย Cartesian test ทุก role × ทุก endpoint), super-admin สลับบริษัท,
onboarding wizard

**TEAS Connect (MCP + External API)** — AI agent ต่อผ่าน `/mcp` ด้วย API key หรือ OAuth 2.1
(authorize → consent เลือกบริษัท → token พร้อม rotating refresh); REST `/api/v1` พร้อม API key +
idempotency สำหรับ integration ทั่วไป; ทุก credential ถูกจำกัดด้วย scope ฝั่ง server และวิ่งผ่าน
RLS + audit เหมือน user ปกติ

---

## สถาปัตยกรรม

```
Next.js 15 (BFF proxy, ไทย/EN)  ──►  ASP.NET Core Minimal APIs (.NET 10, Clean Architecture)
        │                                    │
        │  cookie session → JWT              │  EF Core 10 migrations = source of truth
        ▼                                    ▼
   ผู้ใช้ / AI agent (MCP)            PostgreSQL 16 + RLS ต่อ tenant + immutability triggers
```

- **Domain → Application → Infrastructure → Api** + worker host — กฎธุรกิจอยู่ใน domain,
  ฟอร์มสรรพากรอยู่ใน PDF filler engine (`/Rect`-driven, ฟอนต์ Sarabun ฝังในตัว)
- เอกสารทุกใบ render จาก **canonical model ก้อนเดียว** ทั้งบนจอ (React) และบนกระดาษ (QuestPDF) —
  หมดยุคหน้าจอโชว์อย่าง ปริ้นท์ออกมาอีกอย่าง
- รายละเอียดเต็ม: [as-built specification](docs/accounting-system-plan.md) ·
  [OpenAPI contract](docs/api/openapi.yaml)

| ส่วน | เทคโนโลยี |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core Minimal APIs, EF Core 10 |
| Database | PostgreSQL 16 (Npgsql), row-level security, DB triggers |
| Frontend | Next.js 15 (App Router), TypeScript 5, Tailwind, shadcn/ui, React Query v5, RHF + Zod |
| Auth | OAuth2 / JWT bearer · API keys · OAuth 2.1 + PKCE AS (OpenIddict) สำหรับ MCP |
| i18n | next-intl — ไทยหลัก อังกฤษรอง |
| Test | xUnit + FluentAssertions + Testcontainers · Playwright e2e |

---

## เริ่มใช้งานใน 5 นาที

ต้องมี: [.NET 10 SDK](https://dotnet.microsoft.com/download) · [Node.js 20+](https://nodejs.org) +
[pnpm](https://pnpm.io) · [Docker](https://www.docker.com) (หรือ PostgreSQL 16 ของคุณเอง)

```bash
# 1) clone + database
git clone https://github.com/pinsorn/teas-accounting.git
cd teas-accounting
docker compose up -d          # PostgreSQL พร้อม database accounting_dev

# 2) backend (:5080) — migrate + seed อัตโนมัติตอนบูตครั้งแรก
cd backend
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Accounting.Api

# 3) frontend (:3000)
cd ../frontend
pnpm install
echo "BACKEND_API_URL=http://localhost:5080" > .env.local
pnpm dev
```

เปิด <http://localhost:3000> — ติดตั้งสะอาดจะพาเข้า **onboarding wizard** (สร้าง super-admin +
บริษัทแรกเอง ไม่มี password ฝังมา) ถ้า seed demo ไว้ (`SeedDemoData=true`) login `admin` /
`Admin@1234` ได้เลย พร้อมบริษัทตัวอย่างทั้งแบบจดและไม่จด VAT

> Windows PowerShell:
> ```powershell
> cd backend
> $env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
> dotnet run --project src\Accounting.Api
> ```

### ต่อ AI agent (TEAS Connect)

ใน dashboard → **ตั้งค่า → API Keys** จะมี snippet พร้อมใช้ต่อ client:

- **Claude Code** — `type: http` + header `X-Api-Key`
- **Claude Desktop** — `mcp-remote` bridge ใน `claude_desktop_config.json`
- **Claude Mobile / native connector** — วาง URL `/mcp` แล้วระบบพาเข้า OAuth: login → เลือกบริษัท →
  อนุญาต เสร็จ

scope ถูกล็อกฝั่ง server: อ่าน + สร้าง draft ได้ แต่ **post เอกสารไม่ได้** — เอกสารภาษีทุกใบยังต้องมี
มนุษย์กดอนุมัติ

---

## ทดสอบ

```bash
cd backend
TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password" \
TEAS_REPO_ROOT="$(git rev-parse --show-toplevel)" \
  dotnet test Accounting.sln
```

Integration tests รันบน PostgreSQL จริง (fixture migrate + seed ให้เอง) — ครอบคลุมถึงระดับ
"ยิงทุก role × ทุก endpoint แล้วเทียบกับ permission matrix" และ "ออกเลขเอกสารพร้อมกัน 25 ทาง
ห้ามซ้ำห้ามขาดช่วง" Frontend: `pnpm exec tsc --noEmit` + Playwright e2e

---

## โครงสร้างโปรเจกต์

```
backend/
  src/
    Accounting.Domain           # entities, กฎธุรกิจ, เครื่องคิดภาษี (PIT/CIT/WHT) แบบ pure
    Accounting.Application      # use cases, DTOs, abstractions
    Accounting.Infrastructure   # EF Core, services, RD PDF fillers, SQL bootstrap (RLS/triggers/seed)
    Accounting.Api              # minimal-API host + MCP server + OAuth AS
    Accounting.Workers          # background jobs
  tests/                        # xUnit (Domain + Api integration) + TestKit
frontend/
  app/(dashboard)/*             # หน้าจอ · components/ · lib/ · messages/{th,en}.json · e2e/
docs/                           # as-built spec · OpenAPI · RD-form references · คู่มือผู้ใช้
infra/db/schema.sql             # อ้างอิงเท่านั้น — EF migrations คือ source of truth
```

---

## คู่มือผู้ใช้

คู่มือภาษาไทยแบบ step-by-step มีภาพประกอบ ~46 บท ครอบคลุมตั้งแต่ติดตั้งจนถึงยื่นแบบ:
[`docs/manual/`](docs/manual/) · [API reference](docs/manual/api/index.md)

- **อ่านเลย (แนะนำ):** [`docs/manual/generated/print.html`](docs/manual/generated/print.html) —
  รวมทุกบทหน้าเดียว เปิดในเบราว์เซอร์ / Print เป็น PDF ได้
- **เปิดเป็นเว็บ:** `pip install mkdocs mkdocs-material && mkdocs serve -f docs/manual/mkdocs.yml`

---

## เวอร์ชัน & release

git tag → [MinVer](https://github.com/adamralph/minver) stamp ลง assembly → โชว์ที่
`GET /system/info` + footer ของ dashboard · conventional commits บน `main` →
[release-please](https://github.com/googleapis/release-please) เปิด release PR (changelog + tag)
อัตโนมัติ · CI build + test backend และ type-check frontend ทุก PR

---

## Compliance

ระบบยึดกฎหมายภาษีไทยเป็นข้อกำหนดตายตัว: ใบกำกับภาษีเต็มรูป ม.86/4, ความ immutable ของเอกสาร
ที่ post แล้ว (พ.ร.บ. การบัญชี — เก็บ 5 ปี), เลขเอกสารต่อเนื่อง, VAT/WHT/CIT/PIT ตามประมวลรัษฎากร
โค้ดจุดที่แตะกฎหมายมี test อ้าง มาตราไว้ในตัว (`// ม.86/4 #6`) — ทั้งนี้ TEAS เป็นซอฟต์แวร์
ไม่ใช่ที่ปรึกษาภาษี ควรให้นักบัญชีของคุณตรวจก่อนยื่นจริงเสมอ

## License

[GNU AGPL-3.0](LICENSE) — โอเพนซอร์สเต็มตัว ใช้ / ต่อยอด / เปิดเป็นบริการได้
ยินดีต้อนรับ contributor ทุกคน 🙌 ดู [`CONTRIBUTING.md`](CONTRIBUTING.md)

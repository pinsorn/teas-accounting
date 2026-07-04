<p align="center">
  <img src="frontend/public/teas-logo.png" alt="TEAS logo" width="160">
</p>

<h1 align="center">TEAS — Thailand Enterprise Accounting System</h1>

<p align="center">
  ระบบบัญชีสำหรับธุรกิจไทย ที่ออกแบบให้สอดคล้องกับประมวลรัษฎากรและข้อกำหนดของกรมสรรพากรตั้งแต่ระดับสถาปัตยกรรม
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

## ภาพรวม

TEAS เป็นแพลตฟอร์มบัญชีแบบ multi-tenant สำหรับบริษัทไทย ครอบคลุมงานบัญชีครบวงจรตั้งแต่สายเอกสารขายและซื้อ
ภาษีหัก ณ ที่จ่าย เงินเดือน บัญชีแยกประเภท ไปจนถึงแบบภาษีของกรมสรรพากรที่กรอกข้อมูลแล้วพร้อมพิมพ์ยื่น

จุดที่ทำให้ TEAS ต่างจากระบบบัญชีทั่วไปคือ ข้อกำหนดทางกฎหมายภาษีไทยถูกออกแบบไว้ในโครงสร้างของระบบตั้งแต่ต้น
ไม่ใช่ความสามารถที่เพิ่มเข้ามาภายหลัง ตัวอย่างที่เป็นรูปธรรม:

- **ใบกำกับภาษีเต็มรูปครบ 8 องค์ประกอบตามมาตรา 86/4** — แสดงภาษีมูลค่าเพิ่มแยกจากมูลค่าสินค้าเสมอ
- **เอกสารที่บันทึกบัญชีแล้วแก้ไขไม่ได้** — บังคับทั้งที่ database trigger และ application layer
  การแก้ไขต้องดำเนินการผ่านใบลดหนี้หรือใบเพิ่มหนี้ตามครรลองของกฎหมาย
- **เลขที่เอกสารออกเรียงลำดับไม่ขาดช่วง** — กำหนดเลขเมื่อบันทึก (post) เท่านั้น เลขที่ยกเลิกคงอยู่ในระบบ
  และไม่นำกลับมาใช้ซ้ำ พร้อมรายงานตรวจสอบเลขขาดช่วง
- **อัตราภาษีมูลค่าเพิ่มคำนวณฝั่งเซิร์ฟเวอร์** จากข้อมูลหลักของบริษัท ระบบไม่เชื่อถือค่าที่ส่งมาจาก client
- **Audit trail แบบ append-only** ในทุกการเปลี่ยนสถานะเอกสาร รองรับการตรวจสอบย้อนหลังตามพระราชบัญญัติการบัญชี

## ความสามารถหลัก

### สายเอกสารขาย
ใบเสนอราคา → ใบสั่งขาย → ใบส่งของ → ใบกำกับภาษี → ใบเสร็จรับเงิน พร้อมใบวางบิล ใบลดหนี้ และใบเพิ่มหนี้
ที่อ้างอิงใบกำกับภาษีเดิม ระบบแปลงเอกสารต่อสายให้อัตโนมัติ เก็บ cross-reference ระหว่างเอกสาร ติดตามประวัติ
การพิมพ์ (ต้นฉบับ/สำเนา) และรองรับกิจการที่ไม่จดทะเบียนภาษีมูลค่าเพิ่มด้วยเส้นทางเอกสารแยกต่างหาก
(ใบวางบิล → ใบเสร็จ โดยไม่มีใบกำกับภาษี)

### สายเอกสารซื้อและภาษีหัก ณ ที่จ่าย
ใบสั่งซื้อ → บันทึกใบกำกับภาษีซื้อ → ใบสำคัญจ่าย → หนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)
ภาษีหัก ณ ที่จ่ายคำนวณรายบรรทัดพร้อมการป้องกันข้อผิดพลาด เช่น ผู้ขายที่ไม่จดทะเบียนภาษีมูลค่าเพิ่ม
จะถูกบังคับอัตราภาษีซื้อเป็นศูนย์ รองรับผู้ขายต่างประเทศครบทั้งการนำส่งตามมาตรา 70 (ภ.ง.ด.54)
และ reverse charge ตามมาตรา 83/6 (ภ.พ.36 พร้อมบันทึกรายการบัญชีอัตโนมัติ) รวมถึงกรณีผู้รับเงิน
ไม่ยินยอมให้หักภาษี (การออกภาษีแทนแบบ gross-up ทั้งกรณีออกให้ครั้งเดียวและออกให้ตลอดไป)

### เงินเดือน
ทะเบียนพนักงาน รอบจ่ายเงินเดือนรายเดือนพร้อมวงจรอนุมัติ (ร่าง → อนุมัติ → บันทึกบัญชี → จ่ายแล้ว)
สลิปเงินเดือนรายบุคคลหรือรวมทั้งรอบ การคำนวณภาษีเงินได้บุคคลธรรมดาแบบขั้นบันไดพร้อมค่าลดหย่อน
เงินสมทบประกันสังคมพร้อมไฟล์นำส่ง สปส.1-10 (ทั้งรูปแบบไฟล์ e-Service และแบบฟอร์ม PDF)
แบบ ภ.ง.ด.1 รายเดือน ภ.ง.ด.1ก รายปี และหนังสือรับรอง 50 ทวิ รายพนักงาน

### แบบภาษีพร้อมยื่น
ระบบกรอกแบบฟอร์มจริงของกรมสรรพากรเป็นไฟล์ PDF พร้อมพิมพ์และลงนาม ครอบคลุม ภ.พ.30, ภ.ง.ด.1, ภ.ง.ด.1ก,
ภ.ง.ด.3, ภ.ง.ด.53, ภ.ง.ด.54, ภ.พ.36, ภ.พ.01 และ ภ.พ.09 สำหรับภาษีเงินได้นิติบุคคล ระบบคำนวณและกรอก
ภ.ง.ด.51 (ครึ่งปี) และ ภ.ง.ด.50 (ประจำปี) ให้ครบถ้วน รวมบันไดการปรับปรุงกำไรทางภาษี อัตราภาษีแบบ SME
ขาดทุนสะสมยกมา และงบแสดงฐานะการเงิน นอกจากนี้ยังส่งออกไฟล์ "Format กลาง" (.txt) สำหรับนำเข้าโปรแกรม
RD Prep ของกรมสรรพากร (ภ.ง.ด.3, ภ.ง.ด.53 และ ภ.พ.30) เพื่อแปลงเป็นไฟล์ .rdx สำหรับยื่นผ่านระบบ e-Filing
พร้อมเอกสารงบการเงินประกอบการยื่น ภ.ง.ด.50

> **หมายเหตุ:** e-Tax Invoice (การนำส่งใบกำกับภาษีอิเล็กทรอนิกส์ถึงกรมสรรพากรโดยตรง) ยังอยู่ในสถานะ
> โครงร่างระยะที่ 1 และยังไม่เปิดใช้งาน — ดูรายละเอียดใน `plan.md`

### บัญชีแยกประเภทและรายงาน
รายการบัญชีบันทึกอัตโนมัติเมื่อเอกสาร post พร้อมสมุดรายวันแบบบันทึกเอง การเปิด–ปิดงวดบัญชี งบทดลอง
งบกำไรขาดทุน งบแสดงฐานะการเงิน สรุปภาษีรายเดือนแบบหน้าเดียว (รายได้ ภาษีมูลค่าเพิ่ม และภาษีหัก ณ ที่จ่าย
พร้อม drill-down) ทะเบียนภาษีซื้อ–ภาษีขาย รายงานอายุหนี้เจ้าหนี้ และทะเบียนภาษีหัก ณ ที่จ่ายค้างรับ

### Multi-tenancy และการควบคุมสิทธิ์
รองรับหลายบริษัทในการติดตั้งเดียว โดยแยกข้อมูลด้วย PostgreSQL Row-Level Security ซึ่งบังคับที่ระดับ
ฐานข้อมูล การตั้งค่าภาษีมูลค่าเพิ่ม (สถานะการจดทะเบียน อัตรา และโหมดการยื่น ภ.พ.30) เป็นข้อมูลหลัก
รายบริษัท ระบบบทบาทและสิทธิ์แบบละเอียดต่อบริษัท ได้รับการพิสูจน์ด้วยชุดทดสอบแบบ Cartesian
(ทุกบทบาท × ทุก endpoint เทียบกับ permission matrix) พร้อม super-admin ที่สลับบริษัทได้
และ onboarding wizard สำหรับการติดตั้งครั้งแรก

### TEAS Connect — การเชื่อมต่อสำหรับ AI Agent และระบบภายนอก
AI agent เช่น Claude สามารถเชื่อมต่อผ่าน MCP (Model Context Protocol) ที่ `/mcp` ได้สองวิธี:
API key สำหรับ Claude Code และ Claude Desktop หรือ OAuth 2.1 + PKCE สำหรับ Claude Mobile และ
native connector อื่น ๆ (ระบบมี authorization server ในตัว พร้อมหน้า consent สำหรับเข้าสู่ระบบและ
เลือกบริษัท) ขอบเขตสิทธิ์ของ agent ถูกจำกัดฝั่งเซิร์ฟเวอร์: agent อ่านข้อมูลและสร้างเอกสารร่างได้
แต่**ไม่สามารถบันทึกบัญชี (post) ได้ในทุกกรณี** — เอกสารที่ agent ร่างจะแนบลิงก์สำหรับให้ผู้ใช้
ที่เป็นมนุษย์เปิดตรวจสอบและกดอนุมัติด้วยสิทธิ์ของตนเอง สำหรับการเชื่อมต่อระบบภายนอกทั่วไป
มี REST API (`/api/v1`) พร้อม API key และ idempotency key

## สถาปัตยกรรม

```
ผู้ใช้ / AI agent (MCP)
        │
        ▼
Next.js 15  (BFF proxy · session cookie · ไทย/อังกฤษ)
        │
        ▼
ASP.NET Core Minimal APIs (.NET 10, Clean Architecture: Domain → Application → Infrastructure → Api)
        │
        ▼
PostgreSQL 16  (Row-Level Security รายบริษัท · immutability triggers · EF Core migrations)
```

หลักการออกแบบที่สำคัญ:

- **Clean Architecture** — กฎทางธุรกิจและเครื่องคำนวณภาษี (ภาษีเงินได้บุคคลธรรมดา นิติบุคคล
  และภาษีหัก ณ ที่จ่าย) เป็น pure code ใน Domain layer ทดสอบได้โดยไม่ต้องมีฐานข้อมูล
- **เอกสารทุกใบ render จากข้อมูลก้อนเดียว** — endpoint `GET /{doc}/{id}/paper` ให้ canonical model
  ที่ทั้งหน้าจอ (React) และไฟล์ PDF (QuestPDF) ใช้ร่วมกัน สิ่งที่เห็นบนจอจึงตรงกับกระดาษเสมอ
- **EF Core migrations เป็น source of truth ของ schema** — รวม SQL scripts สำหรับ RLS, triggers
  และข้อมูลตั้งต้น ระบบ migrate ให้อัตโนมัติเมื่อเริ่มทำงาน
- **แบบฟอร์มสรรพากร** กรอกด้วย engine กลางที่อ่านพิกัดช่องจากตัวไฟล์ PDF ของกรมสรรพากรโดยตรง
  พร้อมฝังฟอนต์ Sarabun เพื่อให้อักษรไทย render ถูกต้องในทุกโปรแกรมอ่าน PDF

รายละเอียดฉบับเต็มอยู่ที่ [as-built specification](docs/accounting-system-plan.md) และ
[OpenAPI contract](docs/api/openapi.yaml)

## เทคโนโลยีที่ใช้

| ส่วน | เทคโนโลยี |
|---|---|
| Backend | C# / .NET 10, ASP.NET Core Minimal APIs, EF Core 10 |
| ฐานข้อมูล | PostgreSQL 16 (Npgsql), Row-Level Security, database triggers |
| Frontend | Next.js 15 (App Router), TypeScript 5, Tailwind CSS, shadcn/ui, React Query v5, React Hook Form + Zod |
| การยืนยันตัวตน | OAuth2 / JWT bearer, API keys, OAuth 2.1 + PKCE authorization server (OpenIddict) |
| ภาษา (i18n) | next-intl — ภาษาไทยเป็นหลัก ภาษาอังกฤษเป็นรอง |
| การทดสอบ | xUnit, FluentAssertions, Testcontainers (backend) · Playwright (end-to-end) |

## การติดตั้งสำหรับนักพัฒนา

สิ่งที่ต้องมีก่อน: [.NET 10 SDK](https://dotnet.microsoft.com/download), [Node.js 20+](https://nodejs.org)
พร้อม [pnpm](https://pnpm.io) และ [Docker](https://www.docker.com) (หรือ PostgreSQL 16 ที่ติดตั้งเอง)

**1. Clone โปรเจกต์และเปิดฐานข้อมูล**

```bash
git clone https://github.com/pinsorn/teas-accounting.git
cd teas-accounting
docker compose up -d        # PostgreSQL พร้อม database accounting_dev
```

หากใช้ PostgreSQL ของตนเอง ให้สร้าง database เปล่าชื่อ `accounting_dev` (user `accounting` /
password `accounting_dev_password`) หรือแก้ค่า `ConnectionStrings:Postgres` ใน
`backend/src/Accounting.Api/appsettings.json`

**2. เริ่ม backend (port 5080)**

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 \
  dotnet run --project src/Accounting.Api
```

การเริ่มครั้งแรกจะ apply EF migrations และ SQL bootstrap scripts (RLS, triggers, ข้อมูลตั้งต้น)
ให้อัตโนมัติ รอจน `http://localhost:5080/health` ตอบสถานะ 200

<details>
<summary>คำสั่งสำหรับ Windows PowerShell</summary>

```powershell
cd backend
$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'
dotnet run --project src\Accounting.Api
```
</details>

**3. เริ่ม frontend (port 3000)**

```bash
cd frontend
pnpm install
echo "BACKEND_API_URL=http://localhost:5080" > .env.local
pnpm dev
```

**4. เข้าสู่ระบบ**

เปิด <http://localhost:3000> — การติดตั้งแบบสะอาด (ค่าเริ่มต้น) จะไม่มีบัญชีผู้ใช้มาให้
หน้าแรกจะนำเข้าสู่ onboarding wizard เพื่อสร้าง super-admin และบริษัทแรกด้วยตนเอง
หากเปิดข้อมูลตัวอย่างไว้ (`SeedDemoData=true`) สามารถเข้าสู่ระบบด้วย `admin` / `Admin@1234`
ซึ่งมาพร้อมบริษัทตัวอย่างทั้งแบบจดทะเบียนและไม่จดทะเบียนภาษีมูลค่าเพิ่ม

### การเชื่อมต่อ AI agent (TEAS Connect)

หลังเข้าสู่ระบบ ไปที่ **ตั้งค่า → API Keys** ซึ่งมีวิธีตั้งค่าและ configuration snippet
สำหรับแต่ละ client:

| Client | วิธีเชื่อมต่อ |
|---|---|
| Claude Code | `type: http` + header `X-Api-Key` |
| Claude Desktop | `mcp-remote` bridge ใน `claude_desktop_config.json` |
| Claude Mobile / native connector | วาง URL `/mcp` แล้วดำเนินการตาม OAuth flow (เข้าสู่ระบบ → เลือกบริษัท → อนุญาต) |

**หมายเหตุ MCP Connector:** เมื่อใช้ OAuth flow (Claude Mobile / native connector) ให้ใช้ OAuth Client ID ที่ลงทะเบียนไว้แล้ว **`teas-mcp`** 
(public client, no secret, PKCE) ในตัวแปร connector; automatic dynamic client registration ไม่เปิดใช้งาน — รองรับ hosted claude.ai/Desktop connector เท่านั้น 
(Claude Code CLI loopback ยังไม่รองรับ)

## การทดสอบ

ชุดทดสอบ integration ของ backend ทำงานบน PostgreSQL จริง โดยกำหนดผ่านตัวแปรแวดล้อม `TEAS_TEST_PG`
(test fixture จะ migrate และ seed ฐานข้อมูลให้เอง) หรือปล่อยให้ Testcontainers สร้างให้หากมี Docker

```bash
cd backend
TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password" \
TEAS_REPO_ROOT="$(git rev-parse --show-toplevel)" \
  dotnet test Accounting.sln
```

ชุดทดสอบครอบคลุมถึงการทดสอบเชิงระบบ เช่น การยิงทุกบทบาทเข้าทุก endpoint เทียบกับ permission matrix
และการออกเลขเอกสารพร้อมกัน 25 ทางเพื่อพิสูจน์ว่าเลขไม่ซ้ำและไม่ขาดช่วง สำหรับ frontend ใช้
`pnpm exec tsc --noEmit` เป็น type gate และ Playwright สำหรับการทดสอบ end-to-end

## โครงสร้างโปรเจกต์

```
backend/
  src/
    Accounting.Domain           # entities, กฎทางธุรกิจ, เครื่องคำนวณภาษี (pure)
    Accounting.Application      # use cases, DTOs, abstractions
    Accounting.Infrastructure   # EF Core, services, RD PDF form fillers, SQL bootstrap scripts
    Accounting.Api              # ASP.NET Core minimal-API host, MCP server, OAuth authorization server
    Accounting.Workers          # งานเบื้องหลัง (background jobs)
  tests/                        # xUnit (Domain + Api integration) และ TestKit
frontend/
  app/(dashboard)/*             # หน้าจอระบบ · components/ · lib/ · messages/{th,en}.json · e2e/
docs/                           # as-built spec, OpenAPI, เอกสารอ้างอิงแบบฟอร์มสรรพากร, คู่มือผู้ใช้
infra/db/schema.sql             # สำหรับอ้างอิงเท่านั้น — EF migrations คือ source of truth
```

## คู่มือผู้ใช้

คู่มือการใช้งานภาษาไทยแบบ step-by-step พร้อมภาพประกอบประมาณ 46 บท ครอบคลุมตั้งแต่การติดตั้ง
การตั้งค่าข้อมูลหลัก สายเอกสารขาย–ซื้อ เงินเดือน ไปจนถึงการยื่นแบบภาษี อยู่ที่ [`docs/manual/`](docs/manual/)
พร้อม [API reference](docs/manual/api/index.md) แยกตามหมวด

- **อ่านแบบหน้าเดียว (แนะนำ):** [`docs/manual/generated/print.html`](docs/manual/generated/print.html)
  รวมทุกบทไว้ในไฟล์เดียว เปิดในเบราว์เซอร์หรือสั่งพิมพ์เป็น PDF ได้
- **เปิดเป็นเว็บไซต์:** `pip install mkdocs mkdocs-material` แล้ว
  `mkdocs serve -f docs/manual/mkdocs.yml` (เปิดที่ http://localhost:8000)

## เวอร์ชันและการ release

เวอร์ชันของ assembly มาจาก git tag ผ่าน [MinVer](https://github.com/adamralph/minver)
และแสดงที่ `GET /system/info` รวมถึง footer ของ dashboard การ release เป็นแบบอัตโนมัติ:
conventional commits บน branch `main` จะถูก [release-please](https://github.com/googleapis/release-please)
รวบรวมเป็น release pull request (พร้อม changelog และ tag) ส่วน CI ทำการ build และทดสอบ backend
พร้อม type-check frontend ในทุก pull request

## Security & compliance hardening (2026-07-04)

การตรวจสอบชุดใหญ่แก้ไขความเสี่ยง 10 HIGH + 12 MEDIUM + 7 LOW:

- **Multi-tenant isolation:** เพิ่ม RLS backstop บน 8 ตาราง + audit.activity_log; background workers (VAT snapshot, e-Tax retry, api-key auth) 
  ตรึง tenant ให้ RLS ทำงานภายใต้ least-privilege prod role
- **Document immutability (ม.86/4 / §4.2):** Tax Invoice header และ lines ไม่สามารถ un-post, re-parent หรือแก้ไขช่องได้; 
  audit log คุ้มกัน TRUNCATE (append-only, 5-year retention)
- **OAuth/MCP auth:** consent + refresh ตรึง MCP scope กับ RBAC ของผู้ใช้; api-key auth แก้ไขภายใต้ RLS; per-IP login rate-limit
- **ภ.พ.30 (VAT return):** input-VAT gate, box-12 double-count fixed, CN/DN category ถูกต้อง
- **PUT-endpoint validation, frontend error-detail + Zod, open-redirect fix**

## ข้อสังเกตด้าน compliance

ระบบนี้ออกแบบตามข้อกำหนดของกฎหมายภาษีไทย ได้แก่ ภาษีมูลค่าเพิ่มตามประมวลรัษฎากร ภาษีหัก ณ ที่จ่าย
ภาษีเงินได้นิติบุคคล ภาษีเงินได้บุคคลธรรมดา และประกันสังคม โค้ดส่วนที่เกี่ยวข้องกับข้อกฎหมายมีชุดทดสอบ
ที่อ้างอิงมาตรากำกับไว้ในตัว อย่างไรก็ตาม TEAS เป็นซอฟต์แวร์ ไม่ใช่คำแนะนำทางภาษีหรือการบัญชี
ผู้ใช้ควรให้ผู้ทำบัญชีหรือผู้สอบบัญชีตรวจทานก่อนนำส่งเอกสารต่อหน่วยงานราชการทุกครั้ง
ข้อมูลตัวอย่างที่ระบบ seed ให้มีไว้สำหรับการพัฒนาเท่านั้น

## License

โปรเจกต์นี้เผยแพร่ภายใต้สัญญาอนุญาต [GNU AGPL-3.0](LICENSE) — สามารถนำไปใช้ ปรับปรุง ต่อยอด
หรือให้บริการต่อได้ตามเงื่อนไขของสัญญาอนุญาต ยินดีต้อนรับผู้ร่วมพัฒนาทุกท่าน
ดูแนวทางได้ที่ [`CONTRIBUTING.md`](CONTRIBUTING.md)

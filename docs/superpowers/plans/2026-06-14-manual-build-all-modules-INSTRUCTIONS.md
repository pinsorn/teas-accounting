# คู่มือ HTML ทุก module — INSTRUCTIONS สำหรับ session ใหม่ (cold start)

> **อ่านไฟล์นี้ก่อนเริ่ม.** เขียนไว้ให้ session ที่เริ่มจากศูนย์ทำคู่มือ HTML (MkDocs)
> ของ TEAS ต่อจนครบทุก module. ทุกขั้น UI ต้องมี **ภาพหน้าจอจริงที่ capture สด**.

---

## 0. กฎเหล็ก (อ่านก่อน ห้ามพลาด)

1. **ห้ามใช้รูปเก่า — capture สดใหม่ทุกครั้ง.** Ham (2026-06-14): "อย่าเนียนเอารูปเก่ามาใช้
   เพราะเราแก้ไปเยอะมาก". UI เปลี่ยนเยอะ (RBAC nav/button gating, **dashboard ออกแบบใหม่ทั้งหน้า**,
   create-form redesign). → **ต้อง re-run capture ของบทที่ 1–2 เดิมด้วย** (captures ที่อยู่ใน
   `docs/manual/captures/01,02/` = STALE). อย่าปล่อยรูปเก่าค้างในคู่มือ.
2. **ทุกขั้นตอน UI = 1 รูป.** เขียน walkthrough ให้เรียก `capture()` ทุก step. ขั้น terminal/SQL
   (install) ไม่มี UI → เป็น code block (บทที่ 0 ทำไว้แล้ว).
3. **ไทยหลัก** (caption + intro ไทย; technical term คงอังกฤษ).
4. ภาษาเอกสาร/commit เขียนปกติ (ไม่ caveman).

---

## 1. ภาพรวม pipeline (มีอยู่แล้ว — ต่อยอด ห้ามสร้างใหม่)

```
frontend/manual/walkthroughs/NN.MM-name.ts   ← เขียนใหม่ต่อบท (Playwright per-step)
   │  เรียก walkthrough(meta, body) จาก lib/walkthrough.ts (register ตอน import)
   ▼  body ใช้ ctx.page (ขับแอป) + ctx.capture(stepId, {caption, highlight?, arrow?})
frontend/manual/run-capture.spec.ts          ← import ทุก walkthrough + รัน (login persona)
   │  pnpm manual:capture  → เขียน docs/manual/captures/<chap>/<id>.json + รูป
   ▼
frontend/manual/gen-markdown.mjs             ← pnpm manual:md
   │  อ่าน captures/*.json → docs/manual/generated/chapter-NN.md (+ per-wt md + print.html + nav.json)
   ▼
python -m mkdocs build -f docs/manual/mkdocs.yml   ← pnpm manual:site
   ▼  docs/_site/*.html   (multi-page HTML; repo **tracks** _site — commit หลัง build)
```

**Commands (pnpm มักไม่อยู่ใน PATH → ใช้ตรง):**
- capture: `cd frontend && node node_modules/@playwright/test/cli.js test -c manual/playwright.config.ts`
  (หรือกรองบท: `... test -c manual/playwright.config.ts -g "02.0"`) — path `.pnpm/@playwright+test@1.60.0/...` เดิม **ใช้ไม่ได้** (module not found); ใช้ hoisted path นี้.
- gen md: `cd frontend && node manual/gen-markdown.mjs`
- build site: `cd docs/manual && python -m mkdocs build -f mkdocs.yml` → ออกที่ `docs/_site/`
- toolchain ยืนยันแล้วมีในเครื่อง: Python 3.10 + mkdocs 1.6.1 + mkdocs-material. (ข้าม `manual:pdf` — Ham อยาก HTML)
- **บทที่ 7 (ภาษี) — ตัวอย่าง PDF ที่ระบบ fill แล้ว:** ก่อน capture บท 7 ต้องรัน
  `cd frontend && python manual/render-pdf-samples.py` (backend :5080 + co2 seed ขึ้นอยู่) ก่อน →
  เรียก endpoint `/tax-filings/pnd51,pnd50/pdf` + `/wht-certificates/{id}/pdf` แล้ว render หน้าแรก
  ด้วย **PyMuPDF (fitz)** ลง `frontend/manual/pdf-samples/*.png`. walkthrough ฝังผ่าน
  `lib/pdf-sample.ts` → `showPdfSample()` (guard: ไฟล์หาย → throw "run render-pdf-samples.py first").
  render ได้: ภ.ง.ด.51 · ภ.ง.ด.50 · 50ทวิ. (ภ.ง.ด.1/1ก ต้อง payroll run posted ก่อน — DRAFT = 404/422.)

**Capture config** `frontend/manual/playwright.config.ts`: testMatch `run-capture.spec.ts`, baseURL :3000,
viewport 1440×900, locale th-TH. **ต้องมี stack รันอยู่ก่อน** (ดู §3).

---

## 2. Contract การเขียน walkthrough (ดูตัวอย่างจริง `walkthroughs/02.05-company-profile.ts`)

```ts
import { walkthrough } from '../lib/walkthrough';
walkthrough({
  id: '03.01',                       // NN.MM — chap = 2 ตัวแรก, เรียงตาม id
  title: 'สร้างลูกค้า',
  chapter: '3. ข้อมูลหลัก',          // = หัวบท (gen-markdown ใช้ตั้งชื่อบท)
  intro: `…ไทย หลายบรรทัด (markdown: ตาราง/หัวข้อ/bold ได้)…`.trim(),
  prerequisites: ['login เป็น ADMIN', '…'],
  // persona?: 'admin' | 'accountant'  (default จาก id ผ่าน lib/personas.ts)
}, async ({ page, capture }) => {
  await page.goto('/customers/new');
  await capture('step-01', { highlight: 'main', caption: 'ขั้นที่ 1: …ไทย…' });
  await page.getByLabel('…').fill('…');
  await capture('step-02', { highlight: 'form', arrow: 'down', caption: 'ขั้นที่ 2: …' });
  // …ทุก step มี capture…
});
```

- **ต้อง register**: เพิ่ม `import './walkthroughs/03.01-…';` ใน `run-capture.spec.ts` (ดู list import ที่นั่น).
- **persona/บริษัทที่ capture**: `lib/personas.ts` — `run-capture` login เป็น demo-admin ของ
  **manual-demo company (company_id = 2)** ก่อนรัน body (ยกเว้น id ใน `SELF_BOOTSTRAP_IDS`).
  → flow ทั้งหมด capture บน **company 2** ไม่ใช่ company 1. ตรวจ/เตรียม master data + เอกสารบน co2.
- `capture(stepId, {highlight, arrow, caption})`: `highlight` = CSS selector ไฮไลต์, `arrow` = ลูกศรชี้,
  `caption` ไทยขึ้นต้น "ขั้นที่ N:" (gen-markdown strip prefix ออกเอง).

---

## 3. Environment briefing (§6 ของ CLAUDE.md — cold session ต้องรู้)

- **subst drives** (หายตอน resume): `subst U: <repo>` , `subst W: <repo>\backend`.
- **Backend :5080** ต้อง `ASPNETCORE_ENVIRONMENT=Development` (ไม่งั้น login 500). start จาก `W:\`:
  `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api`.
  **kill :5080 ก่อน full build** (ล็อก Accounting.Api.exe).
- **Frontend :3000** — capture รันกับ **`next start` (prod build)** หรือ `next dev` ก็ได้ แต่ถ้าแก้ FE
  ต้อง **rebuild** (`next build` แล้ว `next start`) — ห้าม `next build` ตอน `next dev` รัน (พัง .next).
  pnpm มักไม่อยู่ PATH → `node node_modules\next\dist\bin\next build|start`. build จาก **path จริง**
  `Y:\...\frontend` ไม่ใช่ `U:\frontend` (subst ทำ webpack path ปนกัน build fail).
- **manual-demo seed**: company 2 + demo-admin (seed 400/410). ตรวจว่า migrate+seed ครบ (DbInitializer auto ตอน API start).
- **เช็ค :3000 พร้อม**: poll `http://localhost:3000/login` = 200 ก่อน capture.
- DB: `accounting_dev` (Host=localhost;Port=5432;…). login admin/Admin@1234 = super-admin company 1.

---

## 4. สถานะปัจจุบัน (อะไรเสร็จ / อะไรค้าง)

**เสร็จ (commit แล้วบน branch `feat/rbac-per-company-admin-ui`):**
- **บทที่ 0 — ติดตั้ง+ผู้ดูแลระบบ** (`docs/manual/chapters/00-ติดตั้งและผู้ดูแลระบบ.md`) — prose ไทย
  (install/super-admin/สร้างบริษัท). อยู่ใน nav. ไม่มี walkthrough (terminal). **เนื้อหา OK.**
- **บทที่ 1–2 + walkthroughs** `01.01-01.04`, `02.01-02.05` — มีโครง **แต่ captures STALE** (ก่อน UI redesign).
- **RBAC UI Guide** (`docs/manual/rbac-ui-guide.md`, gen โดย `scripts/gen-rbac-manual.mjs`) — อยู่ใน nav.
  ⚠️ รูป sidebar ของ guide นี้ลิงก์ไป `frontend/e2e/screenshots/rbac/*.png` (นอก docs_dir) → **broken ใน HTML site**.
  ถ้าจะให้รูปขึ้นใน site: copy เข้า `docs/manual/img/rbac/` + แก้ path ใน generator (follow-up).
- **index.md** มี roadmap ทุก module แล้ว. **mkdocs.yml** nav มีบท 0/1/2 + RBAC guide.
- **Dashboard ออกแบบใหม่** (`app/(dashboard)/page.tsx`, cont.97d) — KPI/trend/alerts/quick. **ต้องมี walkthrough ใหม่.**

**ค้าง (งานของ session ใหม่):**
1. **Re-capture บท 1–2** (UI เปลี่ยน — รวม dashboard ใหม่). 01.02-dashboard-tour ต้องเขียนใหม่ให้ตรงหน้าใหม่.
2. **เขียนบท 3–9** (ดู §5) แบบ captured walkthrough ครบทุกขั้น.

---

## 5. Roadmap บท 3–9 (เขียน walkthrough ต่อ — เรียงตามนี้)

| บท | เรื่อง | walkthrough ที่ควรมี (ทุก step มีรูป) |
|---|---|---|
| **3** | ข้อมูลหลัก | 03.01 สร้างลูกค้า · 03.02 สร้างผู้ขาย · 03.03 ผังบัญชี · 03.04 หมวดค่าใช้จ่าย · 03.05 พนักงาน |
| **4** | งานขาย | 04.01 ใบเสนอราคา · 04.02 SO→DO · 04.03 ใบแจ้งหนี้ · 04.04 **ออก+โพสต์ใบกำกับภาษี** · 04.05 ใบเสร็จ · 04.06 ใบลดหนี้/เพิ่มหนี้ |
| **5** | งานซื้อ | 05.01 ใบสั่งซื้อ (อนุมัติ SoD) · 05.02 ใบสำคัญจ่าย+WHT · 05.03 ใบกำกับภาษีซื้อ · 05.04 หนังสือรับรองหัก ณ ที่จ่าย 50ทวิ |
| **6** | เงินเดือน | 06.01 พนักงาน · 06.02 รอบเงินเดือน (สร้าง→อนุมัติ→โพสต์→จ่าย) · 06.03 ภ.ง.ด.1/ประกันสังคม |
| **7** | ภาษีและการยื่น | 07.01 ภ.พ.30 · 07.02 ภ.ง.ด.3/53/54 · 07.03 สรุปภาษีรายเดือน (`/reports/tax-summary`) |
| **8** | รายงาน | 08.01 งบทดลอง · 08.02 กำไรขาดทุน · 08.03 ช่องว่างเลขเอกสาร · 08.04 อายุหนี้ |
| **9** | e-Tax | 09.01 ตั้งค่า · 09.02 ออกใบกำกับภาษีอิเล็กทรอนิกส์ + ส่งอีเมล (Tier 1 mock) |

> เพิ่ม nav เข้า `mkdocs.yml` ต่อบท: `- "บทที่ N — …": generated/chapter-NN.md`.

---

## 6. Fixture / data สำหรับบทขาย-ซื้อ-เงินเดือน (สำคัญ)

walkthrough ขาย/ซื้อ/เงินเดือน ต้องมี master data + เอกสาร status ถูกบน **company 2 (demo-admin)**:
- **ตรวจ data co2 ก่อน**: GET `/customers /vendors /products /expense-categories /employees` (Bearer demo-admin).
  ถ้าขาด → walkthrough สร้างเอง (เป็นส่วนหนึ่งของ demo) หรือ seed ผ่าน API ก่อน.
- **บางขั้นต้องมีเอกสารรออยู่** (เช่น โชว์ปุ่ม approve บน PV ต้องมี PV Draft). seed ผ่าน BFF API —
  payload ดูจาก `frontend/e2e/helpers/rbac-detail-fixtures.ts` + `frontend/e2e/purchase-chain.spec.ts`
  (vendor→PO→VI→PV ครบ chain). **SoD: PO/PV ที่ approve ต้อง create กับ approve คนละ user** (ck_po_sod);
  PV ไม่มี SoD constraint (cont.77). employee: `POST /employees/` (nationalId 13 หลักอะไรก็ได้,
  maritalStatus 'SINGLE'); payroll run: `POST /payroll/runs/` ({periodYearMonth,payDate}, gen payslip
  จาก active employee, คืน 201 body ว่าง → หา id จาก list).
- **gaps ที่เจอแล้ว** (อาจต่างใน co2): company 1 มี vendors/categories แต่ employees=0; co3 (non-VAT) ว่างหมด.
  → ตรวจ co2 เอง ก่อนเขียน.
- helper UI มีอยู่: `frontend/e2e/_helpers.ts` (`createAndPostTaxInvoice`, `createVendor`, `pickCustomer`, …).

---

## 7. ขั้นตอนทำงานแนะนำ (ต่อ 1 บท)

1. ตรวจ data co2 ที่บทต้องใช้ (seed ถ้าขาด).
2. เขียน `walkthroughs/NN.MM-*.ts` (ทุก step มี `capture`).
3. `import` ใน `run-capture.spec.ts`.
4. stack รัน (API :5080 Dev + next start :3000; rebuild ถ้าแก้ FE).
5. capture เฉพาะบท: `... cli.js test -c manual/playwright.config.ts -g "NN."`.
6. `node manual/gen-markdown.mjs` → ตรวจ `docs/manual/generated/chapter-NN.md` มีรูปครบทุกขั้น.
7. เพิ่ม nav ใน `mkdocs.yml` → `python -m mkdocs build -f docs/manual/mkdocs.yml`.
8. เปิด `docs/_site/...html` ตรวจตา (เปิด browser/screenshot) — **ทุกขั้นมีรูป, ไทย, ไม่มีรูป broken**.
9. commit (รวม `docs/_site` — repo track) + push เมื่อ Ham สั่ง/ตามที่ตกลง.

---

## 8. Verification (ก่อนปิดแต่ละบท)

- [ ] mkdocs build **0 error** (warning เรื่องรูป rbac-ui-guide เดิม = known, ไม่นับ).
- [ ] ทุก step UI มีรูป **ที่ capture สดรอบนี้** (ไม่มีรูปเก่าค้าง — ตรวจ timestamp/เนื้อหาตรง UI ปัจจุบัน).
- [ ] caption ไทยทุกขั้น · nav อัปเดต · เปิด HTML ดูจริงแล้ว.
- [ ] `docs/_site` rebuild + commit.
- [ ] progress.md prepend + index.md roadmap tick (✅).

---

## 9. อ้างอิงไฟล์

- pipeline: `frontend/manual/{walkthroughs,lib,run-capture.spec.ts,gen-markdown.mjs,playwright.config.ts}`
- ตัวอย่าง walkthrough ที่ดี: `frontend/manual/walkthroughs/02.05-company-profile.ts`
- เนื้อหา: `docs/manual/{chapters,generated,index.md,mkdocs.yml,stylesheets/manual.css}` · output `docs/_site/`
- fixtures/data: `frontend/e2e/{purchase-chain.spec.ts,helpers/rbac-detail-fixtures.ts,_helpers.ts}`
- branch ปัจจุบัน: `feat/rbac-per-company-admin-ui` (RBAC + manual ch0 + dashboard + logo อยู่นี่หมด)
- progress ล่าสุด: `progress.md` (cont.97a–d)

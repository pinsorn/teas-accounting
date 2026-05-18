# Answer-Sana-Backend13 — Sprint 13b: User Manual Generator (Playwright + MkDocs)

**Date:** 2026-05-17
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Auto-generated end-user manual ภาษาไทย with Playwright screenshots + highlights
**Gate:** **Substantial sprint ~8-12 days. Wait until Phase 1 fully shipped (post-Sprint 12 + e-Tax wiring) before kickoff — screenshots will be stale otherwise.**

> Concept: write walkthrough scripts that drive the app via Playwright, inject CSS to
> highlight target elements before each screenshot, save annotated PNGs, generate
> markdown chapters, compile to HTML site (MkDocs Material) + printable PDF
> (wkhtmltopdf). Thai-only. ~30-40 walkthroughs covering all Phase 1 features.

---

## 1. Concept summary

Traditional approach: developer writes manual, takes screenshots manually, manual goes
stale within weeks as UI evolves. Re-writing = labor-intensive, often skipped.

**Our approach:** "Manual as code" — walkthroughs are TypeScript files that:
1. Log in as `demo-accountant` (deterministic seed user)
2. Navigate to a feature
3. Before each action, inject CSS to highlight the target element + add Thai caption
4. Take screenshot
5. Perform the action
6. Repeat until walkthrough complete

Output: markdown chapters with embedded screenshots → compiled to HTML site + PDF.

**Benefit:** when UI changes, re-run walkthroughs → screenshots auto-refresh. No manual
editing of images. Captions still need updating (one line of TypeScript per step).

---

## 2. Tech stack

| Need | Choice | Rationale |
|---|---|---|
| Walkthrough engine | Playwright (existing dep) | reuse skill + infra + deterministic seed |
| Element highlight | CSS injection via `page.evaluate()` | no extra libs; renders in actual screenshot |
| Arrow overlay (optional) | SVG injected into DOM before screenshot | precise position; cleans up after capture |
| Screenshot tool | Playwright `page.screenshot()` | built-in, full or element-only |
| Markdown compiler | **MkDocs Material** | Thai-language friendly, search built-in, clean default theme, easy nav |
| PDF generator | wkhtmltopdf OR Puppeteer print-to-PDF | wkhtmltopdf better for fonts (TH Sarabun); puppeteer fallback if wkhtml fails on TH glyph rendering |
| Manual content language | Thai primary | TH-only per Ham (accountant Thai terms hard to translate even for Thai-EN bilinguals) |

---

## 3. Directory layout

```
docs/manual/
  ├── mkdocs.yml                   MkDocs config
  ├── chapters/                    Markdown content (generated + intro)
  │   ├── 01-เริ่มต้นใช้งาน.md
  │   ├── 02-ตั้งค่าบริษัท.md
  │   ├── 03-การขาย/
  │   │   ├── 01-สร้างใบกำกับภาษี.md
  │   │   ├── 02-ออกใบเสร็จ.md
  │   │   ├── 03-ออกใบลดหนี้.md
  │   │   └── 04-ออกใบเพิ่มหนี้.md
  │   ├── 04-การซื้อ/
  │   │   ├── 01-บันทึกใบกำกับภาษีซื้อ.md
  │   │   ├── 02-ออกใบสำคัญจ่าย.md
  │   │   ├── 03-WHT-และใบ-50ทวิ.md
  │   │   └── 04-Foreign-vendor.md
  │   ├── 05-รายงาน/
  │   │   └── ...
  │   └── 06-ตั้งค่า-master-data/
  │       └── ...
  ├── screenshots/                 (auto-generated, .gitignore'd or committed sparingly)
  │   └── (output of capture runs)
  ├── overrides/                   MkDocs theme overrides (Thai font, custom colors)
  └── _site/                       compiled HTML (gitignored)

frontend/manual/
  ├── playwright.manual.config.ts  separate config from e2e (different testDir, headless, slowMo)
  ├── walkthroughs/
  │   ├── 03.01-create-tax-invoice.ts
  │   ├── 03.02-issue-receipt.ts
  │   ├── 04.01-record-vendor-invoice.ts
  │   └── ... (~30-40 files)
  └── lib/
      ├── capture.ts               highlight + annotate + screenshot helper
      ├── walkthrough.ts           framework (defines walkthrough() entry point)
      ├── seed-data.ts             deterministic test data setup
      └── compile.ts               post-process: markdown assembly + MkDocs build + PDF
```

---

## 4. Walkthrough script API

```typescript
// frontend/manual/walkthroughs/03.01-create-tax-invoice.ts
import { walkthrough } from '../lib/walkthrough';

walkthrough({
  id: '03.01',
  title: 'สร้างใบกำกับภาษี',
  chapter: '3. การขาย',
  intro: `
ใบกำกับภาษีเต็มรูป (ม.86/4) คือเอกสารที่ผู้ขายซึ่งจดทะเบียน VAT
ต้องออกให้ผู้ซื้อภายในเวลาที่กฎหมายกำหนด เพื่อแสดงรายการขาย
จำนวนภาษีมูลค่าเพิ่ม และข้อมูลของผู้ซื้อ-ผู้ขาย.

ในบทนี้คุณจะได้เรียนรู้การสร้างใบกำกับภาษีให้ลูกค้านิติบุคคล
มูลค่า 10,000 บาท + VAT 7% ผ่านระบบ TEAS แบบ step-by-step.
  `.trim(),
  prerequisites: [
    'มี Customer "Acme Co., Ltd." ในระบบ Master Data',
    'มี Product "Lab Service" (Service type) พร้อม default tax code',
    'Login เป็น Accountant role',
  ],
}, async ({ page, capture }) => {

  await loginAs(page, 'demo-accountant');
  await page.goto('/');

  await capture('step-01', {
    highlight: '[data-nav="tax-invoices"]',
    arrow: 'right',
    caption: 'ขั้นที่ 1: คลิกเมนู "ใบกำกับภาษี" ทางแถบซ้าย',
  });
  await page.click('[data-nav="tax-invoices"]');

  await capture('step-02', {
    highlight: '[data-action="new-tax-invoice"]',
    arrow: 'down',
    caption: 'ขั้นที่ 2: คลิกปุ่ม "สร้างใบกำกับภาษีใหม่" ที่มุมบนขวา',
  });
  await page.click('[data-action="new-tax-invoice"]');

  await capture('step-03', {
    highlight: '[data-field="customer"]',
    caption: 'ขั้นที่ 3: เลือกลูกค้า — พิมพ์ชื่อ หรือเลขประจำตัวผู้เสียภาษีเพื่อค้นหา',
  });
  await page.getByLabel('ลูกค้า').fill('Acme');
  await page.getByRole('option', { name: 'Acme Co., Ltd.' }).click();

  await capture('step-04', {
    highlight: '[data-field="business-unit"]',
    caption: 'ขั้นที่ 4: เลือก Business Unit (ถ้าบริษัทเปิดใช้) — ระบบจะ tag ใบกำกับภาษีว่าเป็นของ BU ไหน เพื่อแยกรายงาน',
  });

  // ... continue all steps ...

  await capture('step-final', {
    highlight: '[data-doc-no]',
    caption: 'ขั้นสุดท้าย: ใบกำกับภาษีออกแล้ว — หมายเลขถูก allocated จาก sequence โดยอัตโนมัติ',
  });
});
```

---

## 5. `capture()` helper implementation sketch

```typescript
// frontend/manual/lib/capture.ts
import { Page } from '@playwright/test';

export interface CaptureOptions {
  highlight?: string;      // CSS selector to outline
  arrow?: 'up' | 'down' | 'left' | 'right';
  caption: string;
}

export async function capture(page: Page, name: string, opts: CaptureOptions): Promise<void> {
  // 1. Inject highlight CSS + arrow overlay
  if (opts.highlight) {
    await page.evaluate(({ sel, arrow }) => {
      const el = document.querySelector(sel) as HTMLElement;
      if (!el) throw new Error(`Capture target not found: ${sel}`);

      // Highlight: red outline + glow
      el.classList.add('__manual-highlight');
      const style = document.createElement('style');
      style.id = '__manual-highlight-style';
      style.textContent = `
        .__manual-highlight {
          outline: 3px solid #ef4444 !important;
          outline-offset: 2px !important;
          box-shadow: 0 0 0 8px rgba(239, 68, 68, 0.25) !important;
          z-index: 9999 !important;
          position: relative !important;
        }
      `;
      document.head.appendChild(style);

      // Optional arrow (SVG injection)
      if (arrow) {
        const rect = el.getBoundingClientRect();
        const svg = createArrowSvg(rect, arrow);  // returns positioned SVG element
        svg.id = '__manual-arrow';
        document.body.appendChild(svg);
      }
    }, { sel: opts.highlight, arrow: opts.arrow });
  }

  // 2. Wait for paint
  await page.waitForTimeout(150);

  // 3. Take screenshot
  const path = `docs/manual/screenshots/${name}.png`;
  await page.screenshot({
    path,
    fullPage: false,
    animations: 'disabled',
    clip: undefined,  // could clip to visible viewport
  });

  // 4. Cleanup
  await page.evaluate(() => {
    document.querySelectorAll('.__manual-highlight').forEach(e => e.classList.remove('__manual-highlight'));
    document.getElementById('__manual-highlight-style')?.remove();
    document.getElementById('__manual-arrow')?.remove();
  });

  // 5. Append to current walkthrough's markdown buffer (lib/walkthrough.ts tracks this)
  recordStep(name, opts.caption);
}

function createArrowSvg(targetRect: DOMRect, dir: 'up'|'down'|'left'|'right'): SVGElement {
  // Returns positioned SVG arrow pointing AT the target
  // Implementation ~30 lines
}
```

---

## 6. Compile pipeline

```bash
# Step 1: Spin up dev server with deterministic seed
$ pnpm manual:seed       # idempotent: resets to "manual-demo" seed data
$ pnpm dev &             # backend + frontend
$ wait_for_ready

# Step 2: Run all walkthroughs in parallel (limit concurrency to avoid DB contention)
$ pnpm manual:capture    # spawns Playwright, runs all walkthroughs/, generates screenshots/ + markdown chapters/

# Step 3: Build HTML site
$ pnpm manual:build      # mkdocs build → docs/manual/_site/

# Step 4: Build PDF
$ pnpm manual:pdf        # wkhtmltopdf _site/ → AccountProject-User-Manual-TH.pdf

# Step 5 (optional): Verify
$ pnpm manual:verify     # broken-link check + visual diff vs previous
```

---

## 7. MkDocs config (`docs/manual/mkdocs.yml`)

```yaml
site_name: AccountProject — คู่มือใช้งาน
site_url: https://manual.your-teas.example
theme:
  name: material
  language: th
  palette:
    primary: teal
    accent: amber
  font:
    text: Sarabun        # Google Font "Sarabun" — fallback for browser; PDF uses TH Sarabun New
  features:
    - navigation.tabs
    - navigation.sections
    - search.suggest
    - search.highlight
    - content.code.copy

nav:
  - หน้าแรก: index.md
  - เริ่มต้น: chapters/01-เริ่มต้นใช้งาน.md
  - ตั้งค่าบริษัท: chapters/02-ตั้งค่าบริษัท.md
  - การขาย:
      - สร้างใบกำกับภาษี: chapters/03-การขาย/01-สร้างใบกำกับภาษี.md
      - ออกใบเสร็จ: chapters/03-การขาย/02-ออกใบเสร็จ.md
      - ใบลดหนี้: chapters/03-การขาย/03-ออกใบลดหนี้.md
      - ใบเพิ่มหนี้: chapters/03-การขาย/04-ออกใบเพิ่มหนี้.md
  - การซื้อ:
      - ...

markdown_extensions:
  - admonition
  - pymdownx.details
  - pymdownx.superfences
  - tables
```

---

## 8. Walkthroughs to write (target ~30-40)

| Chapter | Walkthroughs | Sprint dep |
|---|---|---|
| 01 เริ่มต้น | login, dashboard tour, language toggle, profile | ✅ |
| 02 ตั้งค่าบริษัท | company info, branches, CoA, fiscal year | ✅ |
| 03 การขาย | create TI, post TI, view TI, edit draft, void, issue receipt, multi-TI receipt, CN, DN | ✅ + Sprint 8.6 (AR-WHT) |
| 04 การซื้อ | record VI, post VI, ม.82/4 claim period, PV with WHT, PV settles VI, 50ทวิ, foreign vendor | + Sprint 8.7 |
| 05 รายงาน | number-gap audit, sales summary, trial balance, P&L by BU, ภ.พ.30, ภ.ง.ด.3/53/54, ภ.พ.36 | + Sprint 9 |
| 06 Master data | customer, vendor (incl foreign flags), product, WhtType, ExpenseCategory, BusinessUnit | ✅ + Sprint 8.6/8.7 |
| 07 ขั้นสูง | period close, PO workflow, internal approval, file attachment | + Sprint 11/12 |
| 08 e-Tax | sign + send, RD ack workflow | + Phase 1 ปลาย |
| 09 External API | API key mgmt, sample integration, webhook | + Phase 1 ปลาย |
| 10 Troubleshooting | common errors, contact support | ✅ |

Estimate: **~30-40 walkthroughs**. Each ~10-20 capture steps → ~300-800 screenshots total.

---

## 9. Deterministic seed for manual

A dedicated seed `manual-demo` ensures screenshots are stable across runs:

```
manual-demo company:
  - 5 customers (mix INDIVIDUAL/CORPORATE, including "Acme Co., Ltd.")
  - 5 vendors (mix incl 1 foreign with no Thai VAT-D — "Amazon Web Services Inc.")
  - 10 products (mix GOOD/SERVICE, incl Reptify exempt items)
  - 3 BUs: ECOM, LAB, REPT
  - 5 expense categories
  - 3 users: super-admin, accountant (=demo-accountant), ap-clerk, approver
  - 1 closed period (2026-04), 1 open period (2026-05)
  - 5 sample posted TIs (varied — for "view existing" walkthroughs)
```

Script: `frontend/manual/lib/seed-data.ts` → resets DB to this state. Idempotent.

---

## 10. Scope cuts — explicitly OUT

- ❌ **English version** — TH only per Ham
- ❌ **Video walkthroughs** — too heavy, screenshots sufficient
- ❌ **Interactive demo (live preview)** — out of scope; static manual only
- ❌ **AI-generated voice-over** — out of scope
- ❌ **In-app contextual help / tooltips** — Phase 2
- ❌ **Search backend** — MkDocs built-in client-side search sufficient
- ❌ **Per-customer customization** — generic manual covers all customers
- ❌ **Versioned manual archive** — current version only; older versions archived externally
- ❌ **Per-role manuals** — single manual covers all features; user navigates to what they care about

---

## 11. Verification gates

| Gate | Expectation |
|---|---|
| Backend build | 0/0 (no backend changes expected this sprint) |
| Frontend build | 0/0 |
| Walkthrough runs | all complete cleanly, no Playwright errors |
| Screenshot count | matches expected (~300-800) |
| Markdown chapters | generated for all chapters in §8 nav |
| MkDocs build | 0 warnings, 0 broken links |
| PDF render | TH Sarabun font correctly applied, all images embedded, < 100MB total |
| Visual inspection | sample 10 random pages — text + screenshots + highlights look correct |
| All Playwright e2e regression | still pass (no interference) |

---

## 12. Definition of done

1. `frontend/manual/` directory created with framework files
2. `lib/capture.ts` + `lib/walkthrough.ts` + `lib/seed-data.ts` implemented
3. MkDocs config + theme overrides set up
4. 30+ walkthrough scripts written covering all chapters in §8
5. Seed data (`manual-demo` company) deterministic + idempotent
6. `pnpm manual:capture` runs end-to-end without manual intervention
7. `pnpm manual:build` produces HTML site
8. `pnpm manual:pdf` produces single PDF file
9. Spot-check: 10 random pages reviewed, screenshots + captions correct
10. CI workflow added: regenerate manual on every merge to main, deploy to docs site
11. Manual hosted at `https://manual.your-teas.example` (or internal equivalent)
12. PDF downloadable from manual home page + landing page on main app
13. README in `frontend/manual/` explains how to add a new walkthrough (template)
14. Mirror sync to `Y:\AccountApp\frontend\manual`
15. Update `plan.md` §23.3 — strike Sprint 13b row "✅ shipped"
16. `Report-Backend{NN}.md`

---

## 13. After this sprint

Phase 1 should be production-ready. Next:
- External pen-test
- Go-live checklist (ch.09)
- First customer onboarding
- Phase 2 planning

---

**Build it. ~8-12 days. Report back via Report-Backend{NN}.**

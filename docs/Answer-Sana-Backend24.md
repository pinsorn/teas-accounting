# Sprint 13g — Manual rendering framework + chapter 1+2 capture (vertical slice)

**Owner**: Claude Code
**Spec author**: Sana (per CLAUDE.md §16 sub-step 4a + user direction to pilot
chapters 1+2 before chapter 3)
**Sequencing**: BEFORE Sprint 13e (chapter 3 sales forms fix). User intent =
prove the manual pipeline works on real chapters before producing more content.
**ROI**: 1-2 days

---

## Background

Sprint 13b (original spec, Answer-Sana-Backend13.md) listed framework
building + Playwright capture + MkDocs + PDF as Claude Code tasks AFTER
walkthroughs were written. Sana wrote 9 walkthroughs (chapters 1 + 2)
during chapter-by-chapter validation cycle (CLAUDE.md §16). Claude Code
paused framework work to fix bugs in Sprint 13d/13f.

Now the bugs are fixed + walkthroughs are stable + user wants to **pilot
the framework on chapters 1-2 BEFORE writing chapter 3 walkthroughs**.
The reason: if the framework has design issues (caption layout, screenshot
quality, highlight rendering, etc.), better to find them on 9 walkthroughs
than 30+.

Pre-existing assets:
- 9 walkthrough files under `frontend/manual/walkthroughs/`
  (01.01-01.04, 02.01-02.05) — each ~7-10 capture() calls
- Stub import `import { walkthrough } from '../lib/walkthrough'` —
  the lib file does NOT exist yet (this sprint builds it)
- Chapter markdown under `docs/manual/chapters/` (01 + 02 done)
- `accounting_dev` DB with manual-demo seed (company_id=2,
  demo-admin + demo-accountant users)

---

## P1 — Framework files

### Files to create
- `frontend/manual/lib/walkthrough.ts` — defines `walkthrough()` helper
- `frontend/manual/lib/capture.ts` — defines `capture()` with highlight/arrow/caption
- `frontend/manual/lib/personas.ts` — credentials map for admin/accountant
- `frontend/manual/playwright.config.ts` — Playwright config for headless capture
- `frontend/manual/run-capture.ts` — entry point: iterate walkthroughs, login per
  persona, execute walkthrough, save captures

### `walkthrough()` API

```ts
type WalkthroughMeta = {
  id: string;           // e.g. '02.05'
  title: string;        // e.g. 'ตั้งค่าข้อมูลบริษัท'
  chapter: string;      // e.g. '2. ตั้งค่าระบบ'
  intro: string;        // multi-line Thai intro shown above first screenshot
  prerequisites: string[];
  persona?: 'admin' | 'accountant';  // default 'accountant' — controls which
                                      // login runs before the walkthrough body
};

type WalkthroughCtx = {
  page: Page;           // Playwright page (logged-in as persona)
  capture: CaptureFn;
};

declare function walkthrough(
  meta: WalkthroughMeta,
  body: (ctx: WalkthroughCtx) => Promise<void>
): void;
```

### `capture()` API

```ts
type CaptureOpts = {
  highlight?: string;       // CSS selector — element to outline + dim rest of page
  arrow?: 'up'|'down'|'left'|'right';  // optional pointer arrow to highlight
  caption: string;          // Thai caption shown under screenshot in final manual
};

type CaptureFn = (id: string, opts: CaptureOpts) => Promise<void>;
```

### Capture pipeline (per call)

1. Wait for selector to be visible (if `highlight` provided), 1s settle
2. Inject CSS overlay: dim background (rgba 50% black), `outline: 3px solid #f59e0b`
   on highlight element + arrow overlay if specified
3. Take screenshot: full viewport (1440×900 by default — override via config)
4. Save PNG: `frontend/manual/captures/<chapter-folder>/<id>/<step-id>.png`
   - chapter-folder = first 2 chars of walkthrough id, e.g. `02` for `02.05`
   - step-id = the id passed to capture(), e.g. `step-05-soft-fields`
5. Remove overlay
6. Append to in-memory step record: `{walkthroughId, stepId, caption, imagePath}`

After walkthrough completes: write `<walkthroughId>.json` with step records
(used by P3 markdown generator).

---

## P2 — Persona / login support

### `personas.ts`

```ts
export const personas = {
  admin:      { username: 'demo-admin',      password: 'Demo@1234' },
  accountant: { username: 'demo-accountant', password: 'Demo@1234' },
};
```

### Login flow (run BEFORE each walkthrough body)

- Skip for `01.01-login.ts` (it tests the login flow itself — starts at /login,
  self-bootstraps)
- For all others: navigate to `/login`, fill credentials per persona, submit,
  wait for redirect to `/`
- Use httpOnly cookie session — no token management in framework

### Per-walkthrough persona enforcement

- `02.03-wht-types.ts`, `02.04-api-keys.ts`, `02.05-company-profile.ts` →
  `persona: 'admin'`
- Others default to `'accountant'` (sufficient for BU/Product CRUD)
- `01.04-logout.ts` logs out at end — next walkthrough re-logs in (clean isolation)

---

## P3 — Markdown generation

### Output structure
- `docs/manual/generated/<chapter>/<walkthroughId>.md` — auto-generated per walkthrough
  ```markdown
  ## <walkthroughId> — <title>

  > **Pre-condition**: <prerequisites joined>

  <intro>

  ### ขั้นที่ 1: <caption derived from step>
  ![step-01](../captures/<chapter>/<walkthroughId>/step-01-name.png)

  ### ขั้นที่ 2: ...
  ```
- Chapter markdown imports these via MkDocs include macros (or simple
  `<!-- include -->` markers replaced at build).

### Caption styling
- Step number stripped from caption (already in heading)
- Caption shown as italicized paragraph under image
- Image captioned via `<figure>` HTML element with `<figcaption>`

---

## P4 — MkDocs build

### Setup
- `docs/manual/mkdocs.yml` — site config
- Theme: **Material for MkDocs** (battle-tested + Thai font support)
- Thai font: Sarabun (Google Fonts) or Noto Sans Thai — load via theme.font.text
- Custom CSS: `docs/manual/stylesheets/manual.css` for figcaption + step heading

### Pages

```yaml
nav:
  - บทนำ: index.md
  - "บทที่ 1 — เริ่มต้นใช้งาน": chapters/01-เริ่มต้นใช้งาน.md
  - "บทที่ 2 — ตั้งค่าระบบ": chapters/02-ตั้งค่าระบบ.md
  # ... more chapters added incrementally
```

### Build

```bash
cd docs/manual
mkdocs build  # output → docs/manual/_site/
```

---

## P5 — PDF export

Two viable options — Claude Code chooses:

**Option A: Playwright PDF print** (preferred — already have Playwright)
- After MkDocs build, render `docs/manual/_site/index.html` in headless Chrome
- Print to PDF with Thai font support
- One PDF per "edition" — chapter selector via env var

**Option B: wkhtmltopdf** (battle-tested but old)
- More PDF formatting features
- Thai font requires extra config (--enable-local-file-access + font path)
- Heavier install

Output: `docs/manual/AccountProject-User-Manual-TH-v0.2.pdf` (v0.2 = chapters 1+2 only)

---

## P6 — Pilot run on chapters 1+2

### Run

```bash
# Assume backend on :5080 + frontend on :3000 + manual-demo seed applied
cd frontend
pnpm manual:capture  # runs P2 framework against all 9 walkthroughs
pnpm manual:build    # P3 + P4 + P5 → final PDF
```

### Acceptance

- `frontend/manual/captures/01/01.01-login/step-*.png` exist (5 files for
  01.01 = 5 capture() calls)
- Same for 01.02 (10 steps), 01.03 (4), 01.04 (3), 02.01 (8),
  02.02 (7), 02.03 (8), 02.04 (8), 02.05 (8) — total ~71 PNG files
- `docs/manual/_site/index.html` opens in browser, navigation works,
  Thai renders correctly, screenshots load
- `AccountProject-User-Manual-TH-v0.2.pdf` ≤ 50 MB (rough sanity)
- Open PDF → verify visually: each walkthrough step has matching screenshot
  + Thai caption, no clipping, highlight visible, arrows render if specified

### What can go wrong (Sana flags upfront)

- Some walkthrough selectors may not match in headless mode (anti-flake — use
  `page.waitForSelector` with reasonable timeout)
- `02.01-business-units.ts` and `02.05-company-profile.ts` write to DB.
  Either: (a) make framework run in transaction + rollback, or (b) document
  that pilot run modifies demo-tenant data (acceptable for now — user
  confirmed test DB is writable)
- `01.04-logout.ts` ends with no session — next walkthrough re-logs in
- AlertDialog highlight selector `[role="alertdialog"]` may need fallback
  to `.modal` or specific class depending on shadcn AlertDialog impl
- `02.03-wht-types.ts` step 7 expects disable to succeed without
  confirm — keep walkthrough as-is, output will visually show this
  (intentional — it's documented as a UX gap)

---

## Out of scope (defer)

- Auto-regenerate on file change (watch mode) — Phase 2 enhancement
- Diff-detect between PDF versions for review
- CI integration (run on every PR) — handle when stable
- Multi-language manual (Thai + English) — Phase 2; current scope = Thai only
- Searchable PDF (text layer) — MkDocs Material handles this OK by default

---

## Sana owns (apply AFTER merge)
- `docs/runtime-gotchas.md` — add new section IF framework/Playwright/MkDocs
  surfaces any new patterns worth documenting
- `frontend/manual/walkthroughs/*` — refine selectors if pilot run finds
  any that don't match in headless mode (carry-over: chrome mcp uses live
  rendered DOM but headless playwright is identical, so issues unlikely)
- Chapter 1 + 2 markdown — add `<!-- include -->` macros if MkDocs needs
  explicit imports for generated walkthrough MD

---

## Reporting back

`Report-Backend24.md` — concise. Include:
- Sample PNG file path (so Sana can quickly inspect quality)
- Total step count + capture success rate
- Any walkthrough that failed (which step, why)
- Final PDF file size + page count
- Time to run end-to-end pipeline (for capacity planning when 10 chapters done)

Sana will inspect PDF in workspace + give feedback. If chapters 1-2 look
production-grade → Sprint 13e (chapter 3 fix) starts immediately.

---

## File ownership reminder
Standard. Claude Code: source + scripts. Sana: CLAUDE.md/plan.md/
runtime-gotchas/openapi/manual content (the walkthrough .ts + chapter .md
files are Sana-owned source content, but the generated/ files from P3 are
Claude Code build output — leave the build output to Claude Code, but the
authored content stays Sana).

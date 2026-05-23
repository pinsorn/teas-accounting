# Claude Design TEAS Mockup — Integration Brief

**Author:** Sana · **Date:** 2026-05-21 · **Audience:** Claude Code (next session)

**Source artifact:** `design/claude-design/` (23 files, 9 screenshots)
- Ham received from Claude Design tool as `TEAS.zip`, unpacked + mirrored under `code/design/claude-design/`
- Live preview: open `design/claude-design/index.html` in a browser (Babel-standalone + React 18 UMD, no build needed)

**Sprint assignment:** Sprint 13j (Print/PDF revamp) — propose **expanding scope** to include the FE design-system swap, since the paper preview component IS the QuestPDF visual contract. Pending Ham approval; if scope feels too big, split as 13j-FE + 13j-PDF.

---

## 1. What we got

| Layer | File(s) | Status vs current TEAS |
|---|---|---|
| **Design tokens** | `styles.css` (1006 lines, CSS vars + 3 theme variants) | NEW — currently use Tailwind defaults + DaisyUI tokens |
| **Brand assets** | `assets/teas-logo.png`, `uploads/TEAS3.png` (mascot) | NEW — current logo is generic SVG |
| **App shell** | `app.jsx` (router, theme apply, sidebar collapse) | NEW layout — current uses generic Next.js layout |
| **Shared comps** | `components.jsx` (Icon set, Sidebar, Topbar, Badge, PaperDocument, Mascot, Activity, ToastProvider, RouterProvider) | Mixed — Badge/Toast exist; PaperDocument/Mascot/Activity NEW |
| **Pages (generic)** | `pages/dashboard.jsx`, `doc-list.jsx`, `doc-detail.jsx`, `doc-create.jsx`, `all-docs.jsx` | NEW templates — current pages are per-doctype concrete files |
| **Data mock** | `data.jsx` | Reference only — TEAS uses real BE API |
| **Tweaks panel** | `tweaks-panel.jsx` | NEW — runtime theme switcher (orange-bold / ink-bold / bi-tone) |

Total surface: ~5000 lines of vanilla React (JSX via Babel standalone). Not Next.js — must be **re-implemented**, not copy-pasted.

---

## 2. Design system summary (memorize these)

### 2.1 Color palette (warm peach + ink black, derived from logo)

```
--peach-50:  #FBF1E8  (primary-soft)
--peach-100: #F8E3D0  (badge bg, mark bg)
--peach-400: #E8A87C  (LOGO ORANGE)
--peach-500: #DD8E5C  (primary CTA)
--peach-600: #C57543  (primary hover)
--peach-700: #9E5C34  (primary-ink, accent text)

--ink-50:  #FAF8F5    (page bg)
--ink-75:  #F4F1EC    (surface-alt)
--ink-100: #ECE7DF    (hairline borders)
--ink-200: #D7D1C7    (border-strong)
--ink-500: #6B6660    (text-3 / muted)
--ink-600: #4D4943    (text-2)
--ink-900: #1A1816    (LOGO BLACK, text, body)

--success: #4A7C59 / bg #E6EFE7
--warning: #C68A2E / bg #FBEFD7
--danger:  #B5524A / bg #FBE4E1
--info:    #5B7B9A / bg #E5ECF2
--draft:   #8A847A / bg #ECE7DF
```

3 theme variants set via `html[data-theme="..."]`:
- `orange-bold` — peach primary (DEFAULT, used in screenshots)
- `ink-bold` — black primary (editorial style)
- `bi-tone` — black primary + peach accent

### 2.2 Type

- UI font: `Noto Sans Thai` (weights 400/500/600/700/800) — sidebar, topbar, forms, tables
- Document body font: `Sarabun` (incl. italic) — paper-style document preview, falls back to TH Sarabun New
- Mono: ui-monospace / SF Mono / Menlo

### 2.3 Shape + shadow

- Radii: 6px / 10px / 14px / 18px (sm / md / lg / xl)
- Shadows: warm-tinted using rgba(26,24,22,X) — NOT pure black
- Border style: 1px solid var(--border) for cards; 1.5px solid var(--ink-900) for paper-document head separator

### 2.4 Sidebar geometry

- Expanded: 256px · Collapsed: 72px · Topbar: 56px
- Active item: peach-soft bg + peach-ink text + 3px left rail in var(--primary)
- Nav grouped with uppercase labels (10.5px / letter-spacing 1.2px)

---

## 3. Key new components (don't have these in TEAS today)

### 3.1 `PaperDocument` ★ HIGHEST PRIORITY

The paper-styled document preview is the **single most important contribution**. It's the visual target for both:
- the FE detail page (current: plain card layout)
- QuestPDF output (current: minimal Sprint 13e-era template)

Spec (per `components.jsx` + `screenshots/qt-detail-paper-final.png`):

- A4 width (794px @ 96dpi), white bg, 48×56px padding, font-family `Sarabun`
- Top accent bar: 6px tall, 35% black + 65% peach split (`linear-gradient` left to right)
- `paper-head` grid 1fr/auto with company block left (logo mark 56×56 + name + address) and title block right (label-en uppercase peach + label-th 28px black 800 + docno 16px 600)
- Optional `paper-wm` watermark: 140px font, ink-100 color, rotated -22deg, behind content (z-index 0); color variants success/danger/warning
- `paper-meta` grid 1.4fr/1fr with two blocks (e.g. customer info + dates); block label uppercase peach + value 15px
- `paper-items` table with **black header bg / white text** (intentional — high contrast for fiscal compliance look), thin rows, dashed empty-rows for padding
- `paper-foot` grid 1.4fr/1fr — notes block left (dashed border), totals right with total row highlighted (peach-50 bg + 1.5px peach-400 border + 18px bold)
- `paper-sign` grid 1fr/1fr with two signature boxes (border-top ink-900 + role label + sub)
- Amount-words line in Thai bath text (function `bathText(n)` already implemented in `components.jsx`)

This component is the **shared contract** between FE preview and QuestPDF — design once, render twice.

### 3.2 `Mascot` illustration component

- TEAS brand mascot (anime girl with calculator/clipboard/laptop) from `uploads/TEAS3.png`
- Used in: dashboard greeting card (left of "พร้อมทำงานวันที่ดี ๆ แล้วครับ" copy), empty states, possibly login page
- Implemented as circular avatar with object-position center 30%, scale 1.4 — crops to upper body
- Sizing: 80px (dashboard hero), 120px (empty state)

### 3.3 `DocActionBar`

Bar above the document content. Layout:
- Left: status badge (with EN suffix, e.g. "ตอบรับแล้ว · Accepted") + docno block divider + value
- Right: status-conditional buttons:
  - Draft → แก้ไข (secondary) + ส่งให้ลูกค้า (primary)
  - Posted/Accepted/Delivered → primary chain-forward action (e.g. แปลงเป็นใบสั่งขาย) + ยกเลิก (danger)
  - Cancelled → no actions

This replaces the current ad-hoc button placement on detail pages.

### 3.4 `FilterBar` (5-column grid)

Status / BU / Customer / Date-from / Date-to — matches the C3 Sprint 13i requirement.

### 3.5 `TweaksPanel` (optional, dev-only)

Right-side floating panel with theme + sidebar density toggle. Useful for design QA but **not for production** — gate behind `process.env.NEXT_PUBLIC_TEAS_TWEAKS === '1'`.

### 3.6 Stat cards (dashboard)

4-up grid; warm peach-soft icon-wrap top-right; 26px tabular-nums value; +/- delta below.

### 3.7 `Activity` log + `Related` docs panel

For doc-detail right rail (320px column):
- Activity: vertical timeline of state transitions (Draft → Issued → Posted) with dot indicators (active = peach)
- Related: chip links to upstream/downstream docs in the Q→SO→DO→TI→RC→CN chain

---

## 4. Adoption strategy — DO NOT swap the stack

Current FE: **Next.js 15 App Router + Tailwind 3 + DaisyUI + shadcn/ui** (CLAUDE.md §5.3, non-negotiable).
Claude Design: vanilla React + raw CSS vars.

**Recommended path (≤2 sprints, low blast radius):**

### Phase A — Tokens (1-2 days)
1. Copy `styles.css` `:root` block → `frontend/lib/design-tokens.css` (new file)
2. Map each CSS var to Tailwind theme extension in `tailwind.config.ts`:
   ```js
   theme: {
     extend: {
       colors: {
         peach: { 50: '#FBF1E8', 100: '#F8E3D0', /* ... */ 700: '#9E5C34' },
         ink:   { 50: '#FAF8F5', /* ... */ 900: '#1A1816' },
       },
       fontFamily: {
         ui:  ['"Noto Sans Thai"', 'sans-serif'],
         doc: ['Sarabun', '"TH Sarabun New"', 'serif'],
       },
       boxShadow: { 'warm-sm': '0 1px 2px rgba(26,24,22,0.06)', /* etc */ },
       borderRadius: { 'r-sm': '6px', 'r-md': '10px', 'r-lg': '14px', 'r-xl': '18px' },
     }
   }
   ```
3. Update DaisyUI theme to `teas-orange` with primary=#DD8E5C / primary-content=white / base-100=white / base-200=#FAF8F5
4. Update `app/layout.tsx` to set `<html data-theme="teas-orange">` + load Noto Sans Thai + Sarabun via `next/font/google`

### Phase B — Shell components (2-3 days)
1. `components/layout/Sidebar.tsx` — rewrite using Tailwind from `components.jsx` Sidebar spec; mount inside `app/(dashboard)/layout.tsx`
2. `components/layout/Topbar.tsx` — breadcrumbs + rounded-pill search + icon buttons
3. `components/ui/StatusBadge.tsx` — EXTEND existing component to accept `withEn` prop + use peach/ink palette (currently DaisyUI-only)
4. `components/ui/DocActionBar.tsx` — NEW
5. `components/layout/MascotGreeting.tsx` — NEW (dashboard hero card)

### Phase C — PaperDocument (3-4 days, highest visual impact)
1. `components/paper/PaperDocument.tsx` — A4-sized container, props for `docType, docTypeEn, docNo, date, validUntil, customer, items, summary, notes, signRoles, watermark, watermarkClass`
2. `components/paper/PaperHead.tsx`, `PaperMeta.tsx`, `PaperItems.tsx`, `PaperFoot.tsx`, `PaperSign.tsx` — sub-parts
3. Helper `lib/bath-text.ts` (port `bathText()` from `components.jsx`)
4. Wire into all 7 sales doc detail pages (`app/(dashboard)/quotations/[id]/page.tsx` etc.) as the document body
5. Wire into Q/SO/DO/TI/RC/CN/DN/BN create pages as **sticky right-rail preview** (see `qt-detail-scrolled.png`)

### Phase D — QuestPDF mirror (3-5 days, Sprint 13j R1 core)
1. Create `backend/src/Accounting.Infrastructure/Pdf/PaperDocumentTemplate.cs` — QuestPDF Document that mirrors the FE PaperDocument 1:1
2. Shared spec doc: column widths, font sizes, color hex codes, watermark angles — Sana to write `docs/paper-document-spec.md` BEFORE implementation
3. Per-doctype thin wrappers: `QuotationPdfTemplate`, `TaxInvoicePdfTemplate`, etc. — only differ in title, watermark conditions, sign roles
4. Hook up to existing `IPdfService` + the SR8 fix (พิมพ์ button must download PDF, not window.print)

### Phase E — Activity + Related (1-2 days)
1. `components/doc/ActivityLog.tsx` — timeline pulled from `audit.activity_log` (BE needs new endpoint `GET /api/{docType}/{id}/activity`)
2. `components/doc/RelatedDocs.tsx` — fetches cross-ref chain (BE already returns `docRefs` from Pattern X)

---

## 5. Things NOT to do

- ❌ DO NOT rip out DaisyUI/shadcn — the design system is compatible, just retheme
- ❌ DO NOT use Babel standalone in the actual app (the mockup uses it for instant preview, but Next.js compiles JSX at build)
- ❌ DO NOT copy `data.jsx` mock data into production — use real BE API
- ❌ DO NOT enable the TweaksPanel in production builds
- ❌ DO NOT change the document number format, status state machines, or any compliance logic to match the mockup — the mockup is **visual** only
- ❌ DO NOT remove the StatusBadge component from existing code — extend it
- ❌ DO NOT regress Sprint 13i bug fixes when restyling pages

---

## 6. Open decisions (Sana asks Ham before Claude Code starts)

1. **Sprint assignment:** fold into 13j (Print/PDF), or new 13j-FE sprint, or wait until 13L (post-DevOps)?
2. **Asset licensing:** is `TEAS3.png` mascot final, or placeholder? Need explicit "yes use this" from Ham before baking into prod bundle.
3. **Tweaks panel:** ship for internal QA, or strip entirely?
4. **3 theme variants:** ship just orange-bold (default), or all three with persisted user pref?
5. **Stitch designs:** do we still need Stitch list/detail prompts as additional reference, or does Claude Design supersede? (Sana's read: Claude Design is more cohesive — recommend dropping Stitch unless Ham wants comparison.)
6. **Mascot in production UI:** dashboard hero only, or also empty states + login? Ham conservative-mode = "logo only, no mascot in UI" is valid too.

---

## 7. Asset inventory (paste-ready file paths)

```
design/claude-design/
├── assets/teas-logo.png          ← brand logo (use as <img src="/logo.png">)
├── uploads/TEAS3.png             ← mascot (move to /public/teas-mascot.png)
├── styles.css                    ← design tokens source
├── components.jsx                ← Icon set, Sidebar, Paper*, Mascot, Toast, Router
├── app.jsx                       ← shell + router (reference only)
├── data.jsx                      ← mock data (reference only)
├── tweaks-panel.jsx              ← dev-only theme switcher
├── pages/dashboard.jsx           ← stat-grid + mascot hero + recent activity
├── pages/doc-list.jsx            ← tabs + filter-bar + tbl + pagination
├── pages/doc-detail.jsx          ← action-bar + detail-grid (paper + side rail)
├── pages/doc-create.jsx          ← create-grid (form left + sticky paper right)
├── pages/all-docs.jsx            ← unified doc explorer
├── index.html                    ← live preview entry (open in browser to see all)
└── screenshots/                  ← 9 reference PNGs (dashboard, quotation list, qt-detail paper variants)
```

---

## 8. Acceptance criteria (must pass before sprint 13j-FE marked done)

- [ ] All token CSS vars mapped to Tailwind theme — no hardcoded hex outside the token file
- [ ] Sidebar renders with peach-soft active state + 3px peach rail + collapsible
- [ ] All 9 sales doc detail pages use `PaperDocument` as body (Q/SO/DO/TI/RC/CN/DN/BN) — visual parity with `screenshots/qt-detail-paper-final.png`
- [ ] All 9 create pages have sticky right-rail preview using `PaperDocument`
- [ ] StatusBadge renders with peach/ink palette + `withEn` prop showing "ตอบรับแล้ว · Accepted" style
- [ ] Watermark renders correctly for cancelled/posted/draft states (rotated -22deg, behind content)
- [ ] QuestPDF output of any Tax Invoice matches FE PaperDocument layout at 794px width
- [ ] No regressions in Sprint 13i bug fix surface (B1-B7 + C1-C7 still green)
- [ ] Build green: `dotnet build` (0 errors) + `pnpm build` (0 errors) + frontend tsc (0 errors)
- [ ] Sana RE-VALIDATE deep mode passes batch 1-6 + 9 against restyled UI

---

## 9. Cross-reference

- CLAUDE.md §5.3 — frontend stack constraint (Tailwind + DaisyUI + shadcn)
- `docs/Stitch-Prompts-Sales.md` + `docs/Stitch-Prompts-Sales-Lists.md` — alternative visual brief (deprecate if Claude Design adopted)
- `docs/accounting-system-plan.md` §15.3 (ม.86/4 required fields — must remain visible on the paper)
- `docs/runtime-gotchas.md` §28 (idempotent seeds — relevant when seeding theme prefs)
- Sprint 13i scope (Answer-Sana-Backend28) — must not regress

---

## 10. Paste-ready prompt for Claude Code (TLDR)

> Read `docs/ClaudeDesign-Integration-Brief.md` end-to-end first. Source files are at `design/claude-design/`. Live preview: open `design/claude-design/index.html` in a browser.
>
> Sprint 13j-FE: implement Phase A (tokens) + Phase B (shell) + Phase C (PaperDocument) per §4 of the brief. **Do not start Phase D (QuestPDF) yet** — Sana will write `docs/paper-document-spec.md` after Phase C is visually approved.
>
> Confirm Ham's answers to §6 open decisions before starting. Default assumption if Ham silent: ship orange-bold theme only, mascot in dashboard hero + empty states only, TweaksPanel stripped, drop Stitch prompts.
>
> Build/test gate: zero TS errors, zero .NET build errors, Sprint 13i acceptance still green (run the existing E2E suite + 81 BE tests).

---

**End of brief. Sana hands off to Claude Code via Dispatch when Ham approves §6 decisions.**

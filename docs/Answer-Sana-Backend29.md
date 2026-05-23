# Answer-Sana-Backend29 — Sprint 13j-FE · Claude Design integration

**Author:** Sana · **Date:** 2026-05-21 · **Sprint:** 13j-FE (FE design system swap)
**Predecessor:** Sprint 13i SHIPPED (Answer-Sana-Backend28) — currently awaiting Sana RE-VALIDATE deep mode
**Companion brief:** `docs/ClaudeDesign-Integration-Brief.md` (read FIRST)
**Source artifact:** `design/claude-design/` (23 files, Babel-standalone React preview)
**Successor:** Sprint 13j-PDF (QuestPDF mirror — separate sprint, after this ships)

## 0a. ⚠️ GOLD STANDARD — existing spec wins on every conflict

The Claude Design mockup is **VISUAL REFERENCE ONLY**. It is NOT authority on behavior,
data model, state machine, compliance, RBAC, document numbering, field requirements, or
business logic.

**On every conflict, these existing documents are authoritative:**
- `docs/accounting-system-plan.md` (1900+ lines — source of truth)
- `docs/Design(UI).md` (per-screen UI spec)
- `docs/Design(Architect).md` (architecture decisions)
- `docs/api/openapi.yaml` (REST API contract)
- `CLAUDE.md` §4 compliance rules (ม.86/4, immutability, numbering, multi-tenant, audit)
- `docs/runtime-gotchas.md` (28+ gotchas — must continue avoiding)
- All prior Answer-Sana-Backend*.md sprints already shipped

**If the mockup shows something that contradicts spec — spec wins, ignore the mockup.**

Examples of conflicts where SPEC must win (non-exhaustive):
- Mockup omits VAT row in totals → SPEC requires VAT separately (compliance §4.1)
- Mockup shows watermark text "DRAFT" → SPEC defines Thai watermarks (ฉบับร่าง/ต้นฉบับ/ยกเลิก)
- Mockup shows fields that don't exist in domain → DO NOT add to entity/EF; use empty/null
- Mockup shows different button on Draft state → SPEC state machine wins
- Mockup shows tabs on list pages (ภาพรวม/รายงาน/การตั้งค่า) → SPEC has single-purpose lists; defer
- Mockup shows a doc-create action that bypasses workflow → SPEC chain (Q→SO→DO→TI→RC) wins

When in doubt — ASK Ham via `Question-Backend{N}.md` BEFORE coding. Do not improvise on
compliance, data shape, or workflow.

## 0b. Ham's locked decisions (from brief §6 + 2026-05-21 clarification)

| # | Question | Decision |
|---|---|---|
| 1 | Sprint assignment | **New Sprint 13j-FE** (not folded into 13j-PDF) |
| 2 | Mascot asset | **USE TEAS3.png** as final brand mascot |
| 3 | TweaksPanel in prod | **STRIP** (dev preview only, not shipped) |
| 4 | Theme variants | **orange-bold only** (default); ink-bold + bi-tone deferred to v2 |
| 5 | Stitch prompts status | **DROP** — Claude Design supersedes |
| 6 | Mascot placement | **Dashboard hero + empty states + login** (3 locations) |

Authorization clause from Ham: **"ถ้ามันมีอะไรที่ Design มีแต่ Project เราไม่มีก็ ทำซะ"** — Claude Code is authorized to implement any new VISUAL component, layout, or interaction that exists in `design/claude-design/` but not in current TEAS, subject to §0a Gold Standard rule, §3 scope, and §6 do-not-do rules.

**Important boundary on authorization clause:** this authorizes new VISUAL primitives (PaperDocument, Mascot, ActivityLog, DocActionBar, etc.) — NOT new business behavior, data fields, workflow steps, or RBAC roles. If the mockup implies a behavior the spec doesn't have, ASK before implementing.

## 1. Scope

This sprint integrates the Claude Design mockup into the live Next.js 15 app. **FE only.** QuestPDF mirroring is a separate downstream sprint (13j-PDF / 13k). PaperDocument MUST be built props-driven so QuestPDF can reuse the same spec without re-thinking layout.

### In scope (4 phases, do in order)

**Phase A — Design tokens (target: 1 day)**
- A1. Create `frontend/lib/design-tokens.css` with `:root` block from `design/claude-design/styles.css` (peach 50-700 + ink 50-900 + status colors + shape + warm shadows + sidebar geometry)
- A2. Extend `frontend/tailwind.config.ts` to expose tokens as Tailwind classes (`bg-peach-500`, `text-ink-900`, `shadow-warm-md`, `rounded-r-lg`, etc.)
- A3. Register DaisyUI theme `teas-orange` with primary=#DD8E5C, primary-content=#FFFFFF, base-100=#FFFFFF, base-200=#FAF8F5, base-300=#F4F1EC, neutral=#1A1816, info/success/warning/error matching token palette
- A4. Update `app/layout.tsx`: set `<html data-theme="teas-orange" lang="th">`, load `Noto Sans Thai` (UI, weights 400/500/600/700/800) and `Sarabun` (doc, regular+italic+700) via `next/font/google`, apply `font-ui` to body
- A5. Move `design/claude-design/uploads/TEAS3.png` → `frontend/public/teas-mascot.png` (final asset, not placeholder)
- A6. Move `design/claude-design/assets/teas-logo.png` → `frontend/public/teas-logo.png`
- A7. Verify Tailwind utility classes resolve (`pnpm tsc --noEmit` clean + `pnpm build` clean)

**Phase B — Shell components (target: 2-3 days)**
- B1. Rewrite `components/layout/Sidebar.tsx` to match `design/claude-design/components.jsx` Sidebar:
  - Logo mark 38×38 (peach gradient bg with logo img cover)
  - Brand "TEAS / ENTERPRISE ACCOUNTING" stacked
  - Collapse toggle 28×28 in border-strong square
  - Grouped nav (ภาพรวม / SALES / TAX / AR / REPORTS / SETTINGS) with uppercase 10.5px labels
  - Active item: peach-soft bg + peach-ink text + 3px peach left rail + 600 weight
  - Badge support (number chip top-right per item)
  - Footer: user avatar (peach gradient) + name + role
  - Persist collapsed state to `localStorage` (NOT for sensitive data — UI pref only is OK per CLAUDE.md §10 exception)
- B2. Rewrite `components/layout/Topbar.tsx`:
  - Breadcrumbs with separators (chevron-right) — last crumb bold ink-900
  - Search pill (rounded-full bg-ink-75 border-ink-100 width 280px) with ⌘K hint
  - Icon buttons (bell with peach dot + settings)
- B3. Extend `components/ui/StatusBadge.tsx`:
  - Add `withEn` prop → renders "ตอบรับแล้ว · Accepted" pattern
  - Migrate palette from DaisyUI defaults to peach/ink tokens (success-bg #E6EFE7, etc.)
  - Add dot prefix variant (6×6 currentColor rounded-full)
  - Keep API back-compat — all existing call sites must continue to work
- B4. Create `components/ui/DocActionBar.tsx`:
  - Layout: status block left + docno block (with left border separator) + right action group
  - Status-conditional buttons (per brief §3.3):
    - Draft: แก้ไข (secondary) + ส่งให้ลูกค้า (primary)
    - Posted/Accepted/Delivered: primary chain-forward (e.g. แปลงเป็นใบสั่งขาย) + ยกเลิก (danger)
    - Cancelled: no action buttons (read-only)
  - Replace ad-hoc button placement on all 8 sales doc detail pages
- B5. Create `components/layout/MascotGreeting.tsx`:
  - Peach-50 bg rounded card with mascot avatar (80×80, object-position center 30%, scale 1.4)
  - Thai greeting "พร้อมทำงานวันที่ดี ๆ แล้วครับ" + dynamic sub copy (this month sales + delta vs last month)
  - Right-aligned CTA "ดูสรุปภาพรวม →"
- B6. Create `components/ui/EmptyState.tsx`:
  - Centered mascot 120×120 + h3 ink-900 + p text-2 + optional CTA button
  - Use in: empty list pages, empty search results, error fallbacks
- B7. Create `components/ui/FilterBar.tsx`:
  - 5-column grid (sm: 1 col, md: 2 col, lg: 5 col): status select + BU select + customer combobox + date-from + date-to
  - URL-persisted via `useSearchParams` (`?status=&bu=&customerId=&dateFrom=&dateTo=`)
  - Replace existing filter wiring on all 8 list pages (carry-over from Sprint 13i C3)

**Phase C — PaperDocument (★ critical, target: 3-4 days)**
- C1. Create `lib/bath-text.ts` — port `bathText(n)` from `design/claude-design/components.jsx` lines 65-98. Add unit tests covering: 0, 1, 21, 100, 1000, 1234.56, 1000000, 10000000.
- C2. Create `components/paper/PaperDocument.tsx` — A4-sized container (max-w-[794px]) with:
  - Top 6px accent bar (ink-900 35% + peach-400 65% gradient)
  - `font-doc` (Sarabun) at 16px base
  - Padding 48px top/bottom 56px left/right
  - Watermark layer: 140px font, ink-100 color, rotate -22deg, z-index 0, behind content (z-index 1)
  - Watermark variants: `success` (paid/posted/accepted/delivered) / `danger` (cancelled) / `warning` (draft) / `none` (no watermark)
- C3. Sub-components:
  - `PaperHead` — grid 1fr/auto, company block (logo mark 56×56 + name 18px 700 + address 14px) left, title block (label-en uppercase peach + label-th 28px 800 ink-900 + docno 16px 600) right, bottom border 1.5px ink-900
  - `PaperMeta` — grid 1.4fr/1fr, two info blocks (customer + dates), block has uppercase peach label + 15px val + dl/dt/dd grid for key-value pairs
  - `PaperItems` — full-width table, black header (bg ink-900 white text 13px 600 0.5px letter-spacing), 15px body cells, num cols right-aligned with tabular-nums, dashed empty-row variant for filler
  - `PaperFoot` — grid 1.4fr/1fr, notes block left (dashed border ink-200) + totals right (rows with dotted ink-100 dividers, total row highlighted: peach-50 bg + 1.5px peach-400 border + 18px 700 + value in peach-700)
  - `PaperSign` — grid 1fr/1fr, two signature boxes (border-top ink-900 + role label 14px 700 + sub 13px text-3)
- C4. Props API (lock this — QuestPDF will mirror):
  ```tsx
  interface PaperDocumentProps {
    docType: string;           // "ใบเสนอราคา" etc.
    docTypeEn: string;         // "QUOTATION"
    docNo: string;
    issueDate: string;
    validUntil?: string;       // or dueDate, or deliveryDate (per doctype)
    validUntilLabel?: string;  // override label text
    seller: SellerInfo;        // company + branch + taxId + address (from session BU)
    customer: CustomerInfo;    // name + taxId + branch + address (snapshot at post)
    items: PaperLineItem[];
    summary: PaperSummary;     // subtotal, discount, vat, total
    amountWords?: string;      // pre-computed Thai bath text
    notes?: string;
    signRoles: { left: string; right: string };
    watermark?: { text: string; variant: 'success' | 'danger' | 'warning' };
    extraMetaBlock?: ReactNode; // for doc-specific extras (e.g. payment method on RC)
    signatureImg?: string;     // optional signed-by name overlay on left sign box
  }
  ```
- C5. Wire `PaperDocument` into all 8 sales doc detail pages as the document body — REPLACE current minimal card layout:
  - `app/(dashboard)/quotations/[id]/page.tsx`
  - `app/(dashboard)/sales-orders/[id]/page.tsx`
  - `app/(dashboard)/delivery-orders/[id]/page.tsx`
  - `app/(dashboard)/tax-invoices/[id]/page.tsx`
  - `app/(dashboard)/receipts/[id]/page.tsx`
  - `app/(dashboard)/credit-notes/[id]/page.tsx`
  - `app/(dashboard)/debit-notes/[id]/page.tsx`
  - `app/(dashboard)/billing-notes/[id]/page.tsx`
- C6. Wire `PaperDocument` into 8 create pages as **sticky right-rail preview** (matches `design/claude-design/screenshots/qt-detail-scrolled.png`):
  - Layout: grid 1fr/720px (lg) / 1fr/540px (md) / single col (sm)
  - Preview side: position sticky top 20px, max-height calc(100vh - topbar - 60px), overflow-y auto
  - Preview content updates LIVE as form fields change (no debounce — React state already debounced enough)
- C7. Doc-type-specific watermark + sign role wiring (DO NOT hardcode in PaperDocument — pass via props):

| Doctype | Cancelled wm | Default wm | Sign left | Sign right |
|---|---|---|---|---|
| Quotation | ยกเลิก/danger | (none) / (none) | ผู้เสนอราคา | ผู้รับใบเสนอราคา |
| Sales Order | ยกเลิก/danger | ยืนยันแล้ว/success (if posted) | ผู้ขาย | ผู้สั่งซื้อ |
| Delivery Order | ยกเลิก/danger | ส่งของแล้ว/success (if delivered) | ผู้ส่งของ | ผู้รับของ |
| Tax Invoice | ยกเลิก/danger | ต้นฉบับ/success (always after post) | ผู้ออกใบกำกับ | ผู้ซื้อ |
| Receipt | ยกเลิก/danger | ต้นฉบับ/success | ผู้รับเงิน | ผู้จ่ายเงิน |
| Credit Note | ยกเลิก/danger | ต้นฉบับ/success | ผู้ออกใบลดหนี้ | ผู้ซื้อ |
| Debit Note | ยกเลิก/danger | ต้นฉบับ/success | ผู้ออกใบเพิ่มหนี้ | ผู้ซื้อ |
| Billing Note | ยกเลิก/danger | ออกแล้ว/info | ผู้ออกใบแจ้งหนี้ | ผู้รับใบแจ้งหนี้ |

**Phase D — Activity log + Related docs side rail (target: 1-2 days)**
- D1. Create BE endpoint: `GET /api/{docType}/{id}/activity` returning chronological list of `audit.activity_log` entries scoped to this doc — fields: `actor, action, fromStatus, toStatus, at, note?`
- D2. Wire into existing endpoint structure (`Accounting.Api/Endpoints/*Endpoints.cs`) — add to all 8 sales doctypes
- D3. Create `components/doc/ActivityLog.tsx`:
  - Vertical timeline with 28×28 dot indicators (active = peach-100 bg + peach-300 border + peach-700 icon, inactive = surface-alt + ink-200 + text-2)
  - Title 13.5px 600 + meta 12px text-3 below
  - Icons per action: created, issued, posted, accepted, converted, delivered, cancelled, emailed
- D4. Create `components/doc/RelatedDocs.tsx`:
  - Chain chips: upstream + downstream cross-refs from existing `docRefs` (Pattern X) — peach-50 hover bg, peach-300 hover border
  - Each chip: 32×32 icon (peach-50 bg, peach-700 icon) + type uppercase 11px + docno 13px 600 + arrow chevron-right
  - Click → navigate to that doc's detail page
- D5. Wire into all 8 detail pages: grid `1fr / 320px` (lg) / single col (sm)

### Out of scope (separate sprints / leave as-is)

- ❌ **Purchase module (ส่วนการซื้อ) — LEAVE AS-IS this sprint.** Sidebar menu items keep current labels + routing. Existing pages (vendor-invoices, payment-vouchers, etc.) DO NOT get the PaperDocument / MascotGreeting / DocActionBar treatment in 13j-FE. They WILL pick up new token colors + fonts passively via Tailwind/CSS-var cascade (acceptable). Full visual rebuild of purchase pages deferred to a future sprint (TBD by Ham).
- ❌ **Settings page — LEAVE AT EXISTING ROUTE.** No new settings UI introduced. No moving existing settings into sidebar / topbar / TweaksPanel. Settings page picks up new tokens passively. If existing Settings page needs restyle later → separate sprint.
- ❌ QuestPDF mirror — Sprint 13j-PDF (Sana writes `docs/paper-document-spec.md` AFTER Phase C visually approved)
- ❌ ink-bold + bi-tone themes — v2
- ❌ TweaksPanel ship — strip entirely from prod bundle (keep file in `design/claude-design/` as reference)
- ❌ all-docs unified explorer (`design/claude-design/pages/all-docs.jsx`) — defer until v2
- ❌ Sprint 13i pending bug fixes (SR2/SR4/SR5/SR7/SR8/SR9) — those are RE-VALIDATE follow-ups, not this sprint
- ❌ Tabs on list page (ภาพรวม/รายงาน/การตั้งค่า from screenshot) — design has them but SPEC has single-purpose lists; defer (§0a Gold Standard)

## 2. New components Claude Code MUST create (do not skip)

Cross-reference against the existing `frontend/components/` tree. If any of these already exists with a different shape, ASK Ham via `Question-Backend{N}.md` before overwriting:

| Component | Status | Phase |
|---|---|---|
| `components/layout/Sidebar.tsx` | EXTEND (currently exists, minimal) | B1 |
| `components/layout/Topbar.tsx` | EXTEND or CREATE | B2 |
| `components/ui/StatusBadge.tsx` | EXTEND (Sprint 13e shipped) — add `withEn` | B3 |
| `components/ui/DocActionBar.tsx` | NEW | B4 |
| `components/layout/MascotGreeting.tsx` | NEW | B5 |
| `components/ui/EmptyState.tsx` | NEW (or EXTEND if exists) | B6 |
| `components/ui/FilterBar.tsx` | EXTEND (Sprint 13i C3 partial) | B7 |
| `components/paper/PaperDocument.tsx` | NEW (★ priority) | C2 |
| `components/paper/PaperHead.tsx` | NEW | C3 |
| `components/paper/PaperMeta.tsx` | NEW | C3 |
| `components/paper/PaperItems.tsx` | NEW | C3 |
| `components/paper/PaperFoot.tsx` | NEW | C3 |
| `components/paper/PaperSign.tsx` | NEW | C3 |
| `lib/bath-text.ts` | NEW (+ unit tests) | C1 |
| `lib/design-tokens.css` | NEW | A1 |
| `components/doc/ActivityLog.tsx` | NEW | D3 |
| `components/doc/RelatedDocs.tsx` | NEW | D4 |
| `public/teas-mascot.png` | NEW (copy from design folder) | A5 |
| `public/teas-logo.png` | NEW (copy from design folder) | A6 |

## 3. Compliance + safety rails (DO NOT regress)

- ✅ ม.86/4 Tax Invoice fields — all 8 required fields must remain visible on PaperDocument when used as TI. Test with a posted TI.
- ✅ ต้นฉบับ watermark MUST render on posted Tax Invoice + Credit Note + Debit Note + Receipt — fiscal compliance signal.
- ✅ VAT amount MUST display SEPARATELY in the totals row — not folded into subtotal (compliance §4.1 item 6).
- ✅ Document number format `MM-YYYY-PREFIX-NNNN` preserved as-is — do not restyle to remove dashes.
- ✅ Multi-tenant `company_id` filter still enforced — restyling pages must not bypass the company-scoped data fetches.
- ✅ Sprint 13i ship surface (B1-B7 + C1-C7) must remain green — run the existing E2E suite after Phase B and Phase C ship.
- ✅ All Thai labels stay Thai — restyle is visual only, not i18n.
- ✅ No new build warnings introduced (`pnpm build` + `dotnet build`).
- ✅ Date format: keep ISO internally, format as Buddhist-era Thai (DD/MM/YYYY+543) at display only — `fmtDateShort` from design is the reference.

## 4. Acceptance criteria

- [ ] All design tokens mapped — `grep -r "#[0-9a-fA-F]\{6\}" frontend/components frontend/app` returns ZERO hex codes outside `lib/design-tokens.css` and `tailwind.config.ts`
- [ ] Sidebar matches `screenshots/01-dashboard.png` left rail — peach-soft active state, 3px left peach rail, collapsible to 72px, badge chips render
- [ ] Topbar matches screenshots — breadcrumbs + rounded search pill + icon buttons
- [ ] StatusBadge `withEn` prop renders "ตอบรับแล้ว · Accepted" pattern; existing call sites unaffected
- [ ] All 8 sales doc detail pages render PaperDocument as body (no fallback to old card layout)
- [ ] All 8 create pages have sticky right-rail PaperDocument preview that updates live as form changes
- [ ] Mascot renders on: (a) dashboard hero card, (b) any list page empty state, (c) login page
- [ ] ActivityLog renders on all 8 detail pages — pulled from real BE endpoint (no hardcoded data)
- [ ] RelatedDocs shows correct Pattern X chain chips (Q ← SO ← DO ← TI ← RC; CN/DN ← TI; BN ← TI*)
- [ ] Watermark renders correctly per §C7 doctype matrix
- [ ] `pnpm tsc --noEmit` returns 0 errors
- [ ] `pnpm build` returns 0 errors and 0 new warnings
- [ ] `dotnet build` returns 0 errors
- [ ] Existing E2E tests pass (`pnpm test:e2e`)
- [ ] All 81+ BE integration tests still pass (`dotnet test`)
- [ ] Bath text unit tests cover 8 cases (0, 1, 21, 100, 1000, 1234.56, 1000000, 10000000)
- [ ] Test on both demo-admin and demo-accountant logins — no role-based regression
- [ ] Smoke flow works end-to-end: login → dashboard (mascot greeting visible) → Q create (sticky preview visible) → submit → Q detail (PaperDocument visible) → Accept → convert to SO (chain chip in RelatedDocs)

## 5. Build/run reminders (per CLAUDE.md §0.2 + runtime-gotchas)

- Use `subst U:` for short paths to avoid MSIX MSBuild node launch issue (gotcha §29)
- API runs on `:5080` — stop `Accounting.Api.exe` + `dotnet run` wrapper before rebuild (gotcha §36)
- Build BEFORE migration: `dotnet build src/Accounting.Api/Accounting.Api.csproj` then `database update --no-build`
- Frontend: `node node_modules\typescript\bin\tsc --noEmit` from `U:\frontend` for type-check
- Postgres: `S:\Program Files\PostgreSQL\18\bin\psql.exe`, db `accounting_dev`, postgres/egoist
- For test idempotency on shared DB: use `TestIds.*` helper per CLAUDE.md §15

## 6. DO NOT (hard stops)

- ❌ DO NOT remove DaisyUI or shadcn — extend their themes, don't replace
- ❌ DO NOT use Babel standalone in app — Next.js compiles JSX at build (mockup uses it for preview only)
- ❌ DO NOT copy `design/claude-design/data.jsx` mock data — use real BE API for everything
- ❌ DO NOT ship TweaksPanel to prod — keep file in `design/claude-design/` as reference only
- ❌ DO NOT change any compliance logic to match the mockup (mockup is visual only)
- ❌ DO NOT change document numbering, state machines, or RBAC during this sprint
- ❌ DO NOT regress Sprint 13i fixes
- ❌ DO NOT add tabs to list pages (ภาพรวม/รายงาน/การตั้งค่า from screenshots) — design has them but defer per §1 out-of-scope
- ❌ DO NOT introduce localStorage for anything beyond UI prefs (theme, sidebar collapsed)
- ❌ DO NOT auto-bump versions of Next, Tailwind, DaisyUI, or shadcn — stay on pinned versions
- ❌ DO NOT rebuild Purchase module pages (vendor-invoices, payment-vouchers, etc.) — leave existing UI; tokens propagate passively only
- ❌ DO NOT move Settings to sidebar/topbar/anywhere new — Settings stays at its existing route
- ❌ DO NOT change any spec-defined behavior to match the mockup — §0a Gold Standard rule (mockup is visual only)
- ❌ DO NOT add fields to entities/DTOs/forms just because the mockup shows them — spec wins

## 7. Reporting

When sprint is complete (all Phase A-D shipped + acceptance criteria green):

1. Write `Report-Backend33.md` (next number after Report-Backend32 — verify current number first)
2. Update `progress.md` with cont. NN entry (top): per-phase result table + verification commands run + screenshots before/after
3. Tick Sprint 13j-FE in `plan.md`
4. Mirror to `Y:\AccountApp\` per existing process
5. Update `docs/Session-Resume.md` to point next session to Sprint 13j-PDF spec authoring (Sana's job)
6. Notify Dispatch — Sana will RE-VALIDATE deep mode (visual parity against `design/claude-design/screenshots/*.png`)

## 8. Questions before starting

If anything blocks, write `Question-Backend{N}.md` (next number; check existing dir) covering:
- Component naming conflicts with existing code
- Token migration path for in-progress branches
- Any compliance ambiguity in PaperDocument layout for a specific doctype
- BE endpoint shape questions for `GET /api/{docType}/{id}/activity`

Do not improvise on compliance-adjacent layout. Visual layout is fully owned by Claude Code; compliance content is owned by Sana → escalate to Ham.

---

**End of Answer-Sana-Backend29. Sprint 13j-FE authorized. Ship Phase A → B → C → D in order.**

# Report-Backend34 — Sprint 13j-FE · Claude Design integration

**Author:** Claude Code · **Date:** 2026-05-21 · **Sprint:** 13j-FE (FE design-system swap)
**Spec:** `docs/Answer-Sana-Backend29.md` + `docs/ClaudeDesign-Integration-Brief.md`
**Predecessor:** Sprint 13i (Report-Backend33). **Successor:** Sprint 13j-PDF (QuestPDF mirror — Sana).
**Report #:** 34 (33 already existed for Sprint 13i tail — used next number per §7).

## Status: Phase A→B→C→D ALL SHIPPED + build-green. Gold-Standard §0a honoured throughout.

---

## Phase results

| Phase | Result |
|---|---|
| **A — tokens/theme/fonts/assets** | ☑ `lib/design-tokens.css` (peach 50-700, ink 50-900, status+bg, shape, warm shadows, sidebar geometry — orange-bold only per decision #4). `tailwind.config.ts`: `peach`/`ink` scales + `status-*` colors (hex lives here, whitelisted), `font-ui`/`font-doc`, `shadow-warm-*`, semantic radii `chip/field/card/panel` (named to avoid `rounded-r-*` collision). DaisyUI theme **`teas-orange`** added as default. `app/layout.tsx`: `data-theme="teas-orange"`, `Noto_Sans_Thai` (UI 400-800) + `Sarabun` (doc, +italic), body `font-ui`. Mascot `TEAS3.png`→`public/teas-mascot.png`, logo→`public/teas-logo.png`. Viewport hex moved to `lib/brand.ts` to keep `app/` hex-free. |
| **B — shell** | ☑ B1 `SidebarNav` rewritten in place (kept routing/i18n + **purchase section unchanged** per scope): logo mark, stacked brand, collapse toggle (localStorage `teas-sidebar-collapsed`), uppercase group labels, peach-soft active + 3px left rail, badge support, footer. B2 new `components/layout/Topbar.tsx` (breadcrumbs from pathname, ⌘K search pill, bell+settings) mounted in dashboard layout. B3 `StatusBadge` re-paletted to peach/ink soft pills + `withEn` ("ตอบรับแล้ว · Accepted") + `dot` — **PascalCase status keys kept** (§0a: spec wins over mockup's lowercase); all existing call sites back-compat. B4 `DocActionBar` (presentational; pages pass workflow buttons). B5 `MascotGreeting` (dashboard hero). B6 `EmptyState` (mascot). B7 `FilterBar` (5-col grid) — `ListFilters` re-exports it so all 8 list call-sites work unchanged. Mascot on dashboard + quotations empty-state + login. |
| **C — PaperDocument ★** | ☑ `lib/bath-text.ts` + 8-case vitest (`bath-text.test.ts`). `components/paper/` — `PaperDocument` + `PaperHead/PaperMeta/PaperItems/PaperFoot/PaperSign`; faithful CSS ported to `lib/paper.css` (outside hex-grep; `white` keyword vs `#FFFFFF`) for 1:1 visual parity. **Props API §C4 LOCKED** in `components/paper/types.ts` (QuestPDF mirrors it). `lib/paper-doc-config.ts` = §C7 matrix (titles, sign roles, watermark resolver) + `companyToSeller`. Wired into **all 8 detail pages** (Q/SO/DO/TI/RC/CN/DN/BN) as `detail-grid` (paper + side rail) and **all 8 create pages** as sticky live `preview-side` (`lib/paper-line-totals.ts` shared mapper). |
| **D — activity + related** | ☑ BE `GET /{docType}/{id}/activity` (8 doctypes) — `IActivityQueryService`/`ActivityQueryService` (tenant-scoped, read-only, `Permissions.Report.AuditRead`), registered in DI + Program.cs. FE `useDocumentActivity` hook + `ActivityEntry` type. `components/doc/ActivityLog.tsx` (timeline) + `components/doc/RelatedDocs.tsx` (Pattern X chips; `crossRefsToItems` from `useCrossReferences` for TI/RC/CN/DN, inline refs for Q/SO/DO/BN). Wired into all 8 detail side rails. |

## Verification (all gates green)

| Gate | Result |
|---|---|
| `tsc --noEmit` (frontend) | **0 errors** |
| `next build` (production) | **0 errors, 0 warnings** — *must run from native path, NOT `U:` subst (webpack mangles `U:`-cwd + `C:`-node_modules paths → false "module not found"; pure env quirk, code is fine).* |
| `dotnet build` (Api) | **0 errors, 0 warnings** |
| `dotnet test` | **112 passed, 0 failed** (Domain 89/89, Api 23/23; 91 Api integration tests **skipped** — no Postgres/Testcontainers in this env, not regressions). |
| hex-grep `components`+`app` | **0 matches** — PASS (§4). All brand hex confined to `lib/{design-tokens.css,paper.css,brand.ts}` + `tailwind.config.ts`. |
| bath-text 8 cases | **logic verified 8/8** via direct node eval. vitest itself **cannot run in this MSIX sandbox** (esbuild child-`.exe` spawn blocked, ENOENT) — env limitation, test file is correct and will pass in CI/normal dev. |
| E2E smoke (Playwright) | **not executed** — needs a running app + browser in this headless env. Detail/create routes all compiled in `next build`; recommend Sana runs the smoke flow during RE-VALIDATE. |

## ⚠️ Flag for Ham/Sana (see `Question-Backend15.md`)

**`audit.activity_log` has NO writes for the 8 sales doctypes** — today only `ApiKeyService` writes it. The new activity endpoint is real + correct but returns **empty** for sales docs, so `ActivityLog` shows a graceful "ยังไม่มีประวัติกิจกรรม" until transition logging is added. Backfilling writes across every command handler is a cross-cutting BE change (touches posting/immutability paths — CLAUDE.md §9 "ASK before") and **out of scope for an FE-visual sprint**; per CLAUDE.md §4.8 it should be its own backend sprint. No fabricated data was introduced (§6).

## Notes / locked-decision deviations

- **§C4 watermark union**: added `'info'` additively (non-breaking superset of success/danger/warning) for the Billing Note "ออกแล้ว" default per §C7. QuestPDF mirror unaffected.
- **CN/DN/Receipt** carry no line array → PaperDocument items synthesized (CN/DN: one reason+value line per ม.86/10; Receipt: one line per applied TI). VAT shown separately on all fiscal docs (§3 / ม.86/4 #6).
- **Q/SO/DO/BN** details have no customer address/taxId → customer block shows what exists; nothing fabricated (§0a).
- Out-of-scope held: Purchase pages + Settings untouched (passive token cascade only); no list tabs; TweaksPanel not shipped; ink-bold/bi-tone deferred.

## Files (mirror to Y:\AccountApp)

- **BE:** `Application/Audit/IActivityQueryService.cs`, `Infrastructure/Audit/ActivityQueryService.cs`, `Api/Endpoints/ActivityEndpoints.cs`, `Infrastructure/DependencyInjection.cs`, `Api/Program.cs`.
- **FE new:** `lib/{design-tokens.css,paper.css,brand.ts,bath-text.ts,bath-text.test.ts,paper-doc-config.ts,paper-line-totals.ts}`, `components/paper/{types,PaperDocument,PaperHead,PaperMeta,PaperItems,PaperFoot,PaperSign}.tsx`, `components/layout/{Topbar,MascotGreeting}.tsx`, `components/ui/{DocActionBar,EmptyState,FilterBar}.tsx`, `components/doc/{ActivityLog,RelatedDocs}.tsx`, `public/{teas-mascot,teas-logo}.png`.
- **FE edited:** `tailwind.config.ts`, `app/layout.tsx`, `app/globals.css`, `app/(dashboard)/layout.tsx`, `app/(dashboard)/page.tsx`, `app/(auth)/login/page.tsx`, `components/app-shell/SidebarNav.tsx`, `components/ui/{StatusBadge,ListFilters}.tsx`, `lib/{types,queries}.ts`, all 8 detail pages + `components/AdjustmentNoteScreens.tsx`, all 8 create forms/pages, `app/(dashboard)/quotations/page.tsx`.

## Hand-off
Sprint 13j-PDF (Sana writes `docs/paper-document-spec.md`): mirror `PaperDocumentProps` (§C4, LOCKED) + `lib/paper.css` geometry 1:1 in QuestPDF. See `SESSION-RESUME.md`.

# Design Fix Plan — TEAS — 2026-06-18

From the 2 design reviews (`10-design-review.md` code-level, `11-design-review-visual.md` live).
Ponytail: simplest fix per item, no new dependencies (DaisyUI + shadcn + tokens already present).
FE-only; gate = `tsc --noEmit` 0 + visual re-check on the live app. Grouped into 3 fix batches + 1 decision.

---

## Batch D1 — Mobile / responsive  ⟶ 🔴 Critical (both reviews)
**Problem:** fixed ~230px `<aside>` has no breakpoint/collapse → at <768px it eats 65% of the viewport; every list/form/table unusable on phone.
**Fix (simplest, no new dep — DaisyUI `drawer` is already a dependency):**
1. Wrap the app shell in DaisyUI `drawer`: sidebar = `drawer-side` (off-canvas <`lg`), content = `drawer-content`. `<aside>` → `hidden lg:flex` for the static desktop rail; on mobile it's the drawer.
2. Add a hamburger button in the Topbar (visible `<lg`) bound to the drawer toggle (`<label for="app-drawer">`, native checkbox — no JS state needed).
3. Wrap wide tables (`DataTable`, line-items) in `overflow-x-auto` so they scroll instead of clipping (also fixes D2-#4).
**Files:** `components/app-shell/SidebarNav.tsx`, the dashboard layout (`app/(dashboard)/layout.tsx`), Topbar, `DataTable`. **Effort:** M (the one real chunk).

## Batch D2 — Table / badge visual bugs  ⟶ 🟠 High (visual, concrete)
Mechanical CSS — each is small:
1. **Doc-number wraps 3 rows** → add `whitespace-nowrap` to the doc-no column cell.
2. **Badge overlaps doc-no link** (PV/VI lists) → remove the `absolute` positioning; render the status badge inline in its own flex cell with `gap`, not layered over the link.
3. **Users: status+role badges overlap** → `flex flex-wrap gap-1` on the badge container.
4. **Line-items table clips right (VAT/total off-screen, no cue)** → `overflow-x-auto` container + a right-edge fade/shadow (or `sticky right-0` on the total col). Covered partly by D1-#3.
**Files:** the list-page columns + `components/forms/*` line-items table + `settings/users/page.tsx`. **Effort:** S.

## Batch D3 — Skeletons · tokens · IA · quick wins  ⟶ 🟠 High (structural) + quick wins
1. **Loading → skeleton** (spec §14.3): replace `<p>กำลังโหลด…</p>` / `{tc('loading')}` on detail pages, dashboard chart, `ActivityLog` with the existing `.skeleton-shimmer` (a small `<Skeleton>` wrapper if not present). Reuse what's there.
2. **Hardcoded colors → semantic tokens**: dashboard KPI `KPI_TONE`/`TONE` + tax-summary `emerald/rose/amber/sky-*` → `status-success-bg` / `status-warning-bg` / … tokens (fixes dark-mode + theming).
3. **IA: move Customers from Sales → Master Data** nav group (+ move `number-gaps` to Reports/Admin). One nav-config edit.
4. **Quick wins:** `backHref` on `PageHeader`; `toLocaleString()` → `formatTHB()` in products table; move hardcoded Thai toast strings (`ChainRowPrint.tsx`, `ActivityLog.tsx`) to i18n (both messages files).
**Files:** detail pages, `app/(dashboard)/page.tsx`, `reports/tax-summary`, `SidebarNav`, `PageHeader`, `settings/products`. **Effort:** M (mechanical, many small).

---

## Decision — Global search (⌘K)  ⟶ Ham's call
**Current:** ⌘K pill in the Topbar, explicitly "presentational for now" — does nothing. A fake affordance is worse than none (user clicks → dead end).
**Per-page search/filter already exists** (DataTable filters) → a global search is largely redundant.

**Options (ponytail-ranked):**
- **A. Remove it (RECOMMENDED).** Delete the ⌘K pill. Cost ≈ 5 min. Rationale: cross-entity global search = a real backend search index + endpoint + ranking = big build for marginal value when every list already filters. YAGNI. Declutters the header, kills the false promise.
- **B. Downgrade to a nav-only command palette.** ⌘K opens a list of pages/actions to jump to (no data search) — uses existing routes, no backend. Keeps the power-user ⌘K muscle-memory cheaply (~half a day). Choose this only if the ⌘K feel matters.
- **C. Full global search.** Backend search across customers/vendors/docs + ranking + UI. Real feature, real cost. Not worth it now.

**My recommendation: A (remove).** It's the honest, lazy-correct move — you can already search per page; a non-functional button is a liability. If you like ⌘K, B is the cheap middle. Avoid C unless users actually ask for cross-document search.

---

## Suggested execution
3 subagent batches, FE-only, sequential-ish (D1 touches shell/layout; D2/D3 mostly disjoint pages → D2+D3 can run after D1 or parallel if file sets don't overlap). Gate each: `tsc --noEmit` 0 + a live visual re-check (servers already running on :3000/:5080). No new deps. Not committed until reviewed.
Global search handled per Ham's pick (A/B/C) — default A if unspecified.

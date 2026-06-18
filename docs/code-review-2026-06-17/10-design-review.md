# UI/UX Design Review — TEAS — 2026-06-18

---

## Summary

**Overall design posture:** Solid foundation with a well-considered warm peach/ink token system, a coherent shared `DataTable` pattern, and a live-preview document creation flow that is genuinely valuable for accountants. The biggest risks are three cross-cutting themes:

1. **Skeleton/loading regression** — the spec calls for skeleton screens everywhere; the implementation uses plain text strings (`กำลังโหลด...` / `{tc('loading')}`) across most detail and report pages. An accountant opening a slow connection sees a blank or text-only wait.
2. **Off-token color proliferation** — dashboard KPI tiles and report cards hardcode Tailwind's `emerald-*/rose-*/amber-*/sky-*` palette rather than the design-system tokens (`--status-success`, etc.), creating a visual split personality between those surfaces and everything else that correctly uses warm ink tokens.
3. **Mobile non-handling** — the sidebar is a persistent `<aside>` with no mobile drawer/overlay. On screens narrower than the sidebar width it simply pushes or overflows content. The spec promises `<768px` limited functionality; the implementation delivers no handling at all.

| Severity | Count |
|----------|-------|
| High | 6 |
| Medium | 8 |
| Low | 5 |

---

## Findings

### H-01 · States · Loading is plain text, not skeleton screens [High] [Confirmed]

**File/component:** Multiple — `frontend/app/(dashboard)/tax-invoices/[id]/page.tsx` (isLoading guard), `customers/[id]/page.tsx` line 33, `customers/[id]/edit/page.tsx` line 13, `number-gaps/page.tsx` line 43, dashboard `page.tsx` trend chart section, `components/doc/ActivityLog.tsx` line 47.

**Evidence:**
```tsx
if (isLoading) return <p className="text-base-content/50">{tc('loading')}</p>;
// and in ActivityLog.tsx:
<p className="text-[13px] text-ink-500">กำลังโหลด…</p>
```
The dashboard trend chart section also uses `<div className="grid h-52 place-items-center text-sm text-base-content/40">{t('loading')}</div>` — a 208px gray box with text — rather than a bar-shaped skeleton.

**Why it hurts:** An accountant on a slow network or waiting for a large dataset sees layout shift and a jarring blank-to-populated jump. The design spec §14.3 explicitly mandates "skeleton screens matching final layout." The `.skeleton-shimmer` class is already defined in `globals.css` and unused in these paths.

**Fix:** Replace each `isLoading` text guard with a skeleton that mirrors the final layout. For list pages the `DataTable` already passes `isLoading` — verify its internal skeleton rows are rendered (the implementation file returned no output, suggesting a potential gap there too [Suspected]). For detail pages: a card skeleton with shimmer rows matching the `PaperDocument` dimensions.

---

### H-02 · Responsive · No mobile sidebar handling [High] [Confirmed]

**File/component:** `frontend/components/app-shell/SidebarNav.tsx` (the `<aside>` element), `frontend/app/(dashboard)/layout.tsx`.

**Evidence:**
```tsx
<aside
  data-testid="app-sidebar"
  className={`flex shrink-0 flex-col border-r border-ink-100 bg-base-100 transition-[width] duration-200 ${
    collapsed ? 'w-[72px]' : 'w-64'
  }`}
>
```
The `layout.tsx` wraps children with `<SidebarNav />` and `<Topbar />` with no breakpoint-conditional rendering. There is no `md:hidden`, no overlay, no hamburger menu, no `<dialog>` drawer for mobile. The CSS token `--sb-w-collapsed: 72px` is defined in `design-tokens.css` but the sidebar never hides at any breakpoint.

**Why it hurts:** On a 375px phone the sidebar (72–256px) eats 20–70% of the viewport, making every list page effectively unusable. The spec §15 acknowledges mobile is limited-functionality but still requires the UI to render correctly. Thai accountants commonly use iPads; at 768px the 256px sidebar leaves only 512px for `app-shell-main`, and wide DataTable columns will overflow.

**Fix:** Add a `md:hidden` hamburger trigger in the Topbar; render the sidebar as a slide-over `<dialog>` (DaisyUI `drawer`) below `md`. At `md:block` restore the current collapsible behaviour. The CSS variables `--sb-w` / `--sb-w-collapsed` are already defined for this purpose.

---

### H-03 · Consistency · KPI tile and report card colors bypass the token system [High] [Confirmed]

**File/component:** `frontend/app/(dashboard)/page.tsx` (KPI_TONE / ALERT_TONE objects), `frontend/app/(dashboard)/reports/tax-summary/page.tsx` (TONE object).

**Evidence:**
```tsx
const KPI_TONE = {
  emerald: 'border-emerald-200 bg-emerald-50 text-emerald-700',
  rose:    'border-rose-200 bg-rose-50 text-rose-700',
  amber:   'border-amber-200 bg-amber-50 text-amber-700',
  sky:     'border-sky-200 bg-sky-50 text-sky-700',
};
const ALERT_TONE = {
  error:   'bg-rose-50 text-rose-800 hover:bg-rose-100',
  warning: 'bg-amber-50 text-amber-800 hover:bg-amber-100',
  info:    'bg-sky-50 text-sky-800 hover:bg-sky-100',
};
```
`design-tokens.css` defines `--success`, `--success-bg`, `--warning`, `--warning-bg`, `--danger`, `--danger-bg`, `--info`, `--info-bg` and the Tailwind config exposes them as `status-success`, `status-warning`, etc. These are used correctly by `StatusBadge` but ignored by the most-visible dashboard surface.

**Why it hurts:** Switching themes (the config already defines `teas`, `teas-dark`, `teas-orange`) will break dashboard tiles because `emerald-50` is not theme-aware. Dark-mode users see bright pastel boxes. The brand consistency signal is "warm peach + ink" everywhere except the first page users see.

**Fix:** Replace `emerald-*` → `status-success / status-success-bg`, `rose-*` → `status-danger / status-danger-bg`, `amber-*` → `status-warning / status-warning-bg`, `sky-*` → `status-info / status-info-bg` across both files. Also `text-amber-700` in tax-summary footnote (`reports/tax-summary/page.tsx` last section).

---

### H-04 · Navigation · Customers lives inside the "Sales" nav section [High] [Confirmed]

**File/component:** `frontend/components/app-shell/SidebarNav.tsx`, SECTIONS constant.

**Evidence:**
```tsx
{ key: 'sales', items: [
  { href: '/customers', key: 'customers', ... },
  { href: '/quotations', ... },
  ...
```
Vendors live under `purchase`. Customers, however, are master data shared across Sales and the entire document chain — the spec's sidebar diagram (§3.3) places them under "Master Data" alongside Vendors, Products, and Chart of Accounts. In the current nav, a user looking for a customer from the AR Aging report context or from the purchase context must know to look under "Sales."

**Why it hurts:** Thai accounting workflows frequently start at a customer record to check credit terms before creating any document. Burying customers in Sales breaks discoverability for users who approach from reports or AP context.

**Fix:** Move `customers` to a `master` section alongside `vendors`, or add a top-level "Master Data" group as the spec describes. If Vendors can't be moved (they're in purchase), at minimum group Customers into their own "Master Data" section.

---

### H-05 · Navigation / Spec Adherence · Topbar search is presentational — no backend [High] [Confirmed]

**File/component:** `frontend/components/layout/Topbar.tsx`, comment: `"Search pill is presentational for now (no global search backend)."`

**Evidence:** The component renders a Search icon + `⌘K` affordance but clicking it does nothing. The spec §3.2 promises "search invoices, customers, products (Cmd/Ctrl+K shortcut)" as a primary header feature.

**Why it hurts:** Every list page an accountant uses has its own search, but cross-entity search (find customer "ABC" and immediately see their invoices) is the primary efficiency win for a daily user. A clickable non-functional button also erodes trust.

**Fix (quick win):** Either remove the search pill until the backend is ready (to avoid false affordance), OR implement a simple client-side command palette (cmdk/shadcn Command component) that routes to filtered list pages. The pill already has the right chrome.

---

### H-06 · Forms UX · Products modal has no validation feedback; uncontrolled state [High] [Confirmed]

**File/component:** `frontend/app/(dashboard)/settings/products/page.tsx`, edit modal section.

**Evidence:** The create/edit modal uses raw local state (`edit` object) with no RHF/Zod. The only validation surface is the checkbox usage guard:
```tsx
{!edit.isSaleable && !edit.isPurchasable && (
  <span className="mt-1 text-xs text-error">{t('usageRequired')}</span>
)}
```
No required-field indication on `nameTh`, `productCode`, `unitPrice`. There is no `min-length` enforcement and no error displayed if the user submits with an empty name — the API call simply fails and a toast appears.

**Why it hurts:** An accountant creating 50 new products manually has no visual indication of which fields failed — they see only a toast error and must guess what to fix.

**Fix:** Adopt RHF + Zod for this modal (consistent with every other create flow). Show inline `text-error` messages under each required field, matching the pattern in the Onboarding page. The `compliance-required::after` CSS class is available for required markers.

---

### M-01 · Consistency · Loading guard style varies across pages [Medium] [Confirmed]

**File/component:** Multiple detail pages.

**Evidence:** Three different patterns in use:
- `<p className="text-base-content/50">{tc('loading')}</p>` (tax-invoice detail)
- `<div className="p-6 text-ink-400">{tc('loading')}</div>` (customer detail)
- `<span className="text-sm text-base-content/50">{tc('loading')}</span>` (tax-summary report inline)
- DaisyUI `loading-spinner` (onboarding only)

**Fix:** Extract a `<PageSkeleton />` component used by all detail pages; the spinner is already styled for brand colour at `text-peach-600`.

---

### M-02 · Information Architecture · "Number Gaps" lives in Sales nav, not Audit/Reports [Medium] [Confirmed]

**File/component:** `SidebarNav.tsx`, SECTIONS — `number-gaps` is in the `sales` section.

**Evidence:**
```tsx
{ href: '/number-gaps', key: 'numberGaps', Icon: ListChecks, perm: 'report.audit.read' },
```
The permission is `report.audit.read`, the spec places it under "Admin > Number Gap Audit" (§13.3), but it renders in the Sales group.

**Fix:** Move to a `reports` or `admin` section to match the permission domain and the spec.

---

### M-03 · Spec Adherence · VAT mode chip and notification bell missing from Topbar [Medium] [Confirmed]

**File/component:** `frontend/components/layout/Topbar.tsx`.

**Evidence:** The Topbar imports `Bell` and `Settings` from lucide-react and `CompanySwitcher`, but its rendered output only shows breadcrumbs, the search pill, the Settings icon, and the CompanySwitcher. There is no VAT_MODE chip ("VAT 7%" / "NON-VAT") and no notification bell with pending-task count as specified in §3.2. The `Bell` icon is imported but the rendered JSX has no bell element visible in the component output [Suspected - Topbar JSX was partially truncated; needs rendered confirmation].

**Why it hurts:** An accountant working in non-VAT mode has no always-visible compliance indicator. The spec rates "compliance-visible" as its first design principle.

**Fix:** Add a `useSystemInfo()` hook call to render `<span class="badge badge-sm">VAT 7%</span>` or `NON-VAT` chip. Add the bell with a count from `useNumberGaps()` + pending e-Tax alerts (already computed in the dashboard).

---

### M-04 · Data Display · `toLocaleString()` without locale argument in products table [Medium] [Confirmed]

**File/component:** `frontend/app/(dashboard)/settings/products/page.tsx`, `defaultUnitPrice` column cell (confirmed from search result line 106).

**Evidence:**
```tsx
return <span className="tabular-nums">{v == null ? '—' : v.toLocaleString()}</span>;
```
`toLocaleString()` without arguments uses the browser's system locale. On Windows with a system locale of `en-US`, `5000` renders as `5,000`; on some Thai system locales it may render as `5,000.00` or with different separators.

**Fix:** Replace with `formatTHB(v)` (already available in `@/lib/utils`) or `v.toLocaleString('th-TH', { minimumFractionDigits: 2 })`. All other money columns use `formatTHB` correctly.

---

### M-05 · i18n · ChainRowPrint and ActivityLog have hardcoded Thai strings [Medium] [Confirmed]

**File/component:** `frontend/components/doc/ChainRowPrint.tsx` lines 26, 30, 44, 45, 53; `frontend/components/doc/ActivityLog.tsx` lines 47, 49; `frontend/components/doc/ReceiptWhtCertSection.tsx` line 31.

**Evidence:**
```tsx
toast.warning('ต้นฉบับเคยถูกพิมพ์แล้ว — พิมพ์เป็นสำเนาแทน');
toast.error('บันทึกการพิมพ์ไม่สำเร็จ — ออกเป็นสำเนา');
aria-label="พิมพ์เอกสาร"
<p className="text-[13px] text-ink-500">กำลังโหลด…</p>
```
The project is bilingual (TH primary, EN secondary) and uses `next-intl`. These toasts appear in English mode as Thai text, breaking the bilingual spec.

**Fix:** Move strings into `messages/th.json` and `messages/en.json` under a `doc` or `print` namespace; use `useTranslations('doc')` in the component.

---

### M-06 · Forms UX · PageHeader lacks a "back" affordance on detail pages [Medium] [Confirmed]

**File/component:** `frontend/components/ui/PageHeader.tsx`.

**Evidence:**
```tsx
export function PageHeader({ title, subtitle, actions }: { ... }) {
  return (
    <div className="mb-6 flex items-end justify-between gap-4 border-b border-base-300 pb-4">
      <div>
        <h1 className="text-2xl font-bold">{title}</h1>
        {subtitle && <p className="mt-1 text-sm text-base-content/60">{subtitle}</p>}
      </div>
      {actions && <div className="flex gap-2">{actions}</div>}
    </div>
  );
}
```
There is no `back` prop, no breadcrumb link, and no `<Link href="..">← Back</Link>`. The Topbar breadcrumb shows the path but is not clickable (text-only crumbs [Suspected — Topbar `<Link>` usage needs render confirmation]). Detail pages like Tax Invoice detail have only the actions slot for navigation.

**Why it hurts:** An accountant navigating Quotation → create Tax Invoice → Tax Invoice detail has no in-page back affordance. Browser back works but is not discoverable on large monitors where the browser chrome is hidden.

**Fix:** Add an optional `backHref` prop to `PageHeader`. Render it as `<Link href={backHref} className="btn btn-ghost btn-sm btn-square"><ChevronLeft /></Link>` prepended to the title. Use it on all `[id]/page.tsx` detail pages.

---

### M-07 · Visual Hierarchy · Dashboard section labels use `text-sm font-semibold text-base-content/80` everywhere [Medium] [Confirmed]

**File/component:** `frontend/app/(dashboard)/page.tsx` — KPI section aria-label, trend chart h2, alerts section h2, quick actions h2.

**Evidence:** All section headers (`h2`) use identical styling: `text-sm font-semibold text-base-content/80`. The design spec §2.2 allocates `--text-xl: 24px` for section headers and `--text-2xl: 32px` for page titles. The page title (`h1`) uses `text-2xl font-bold` but the immediate sub-sections are `text-sm` — a 14px/32px jump with no intermediate `text-lg` or `text-xl` level.

**Why it hurts:** An accountant scanning the dashboard cannot quickly parse the information hierarchy. KPI tiles, chart, and alerts blend into one density level.

**Fix:** Elevate section headers to `text-base font-semibold` (16px) or `text-lg` (18px per spec). Reserve `text-sm font-semibold` for column-within-section sub-labels.

---

### M-08 · Accessibility · PostConfirmDialog uses `modal modal-open` without focus trap [Medium] [Suspected]

**File/component:** `frontend/components/ui/PostConfirmDialog.tsx`.

**Evidence:**
```tsx
<div className="modal modal-open" role="dialog" aria-modal="true">
  <div className="modal-box">
```
DaisyUI's `modal modal-open` pattern does not automatically trap focus to the modal box. The component has `role="dialog"` and `aria-modal="true"` which is correct semantically, but without a focus trap implementation (DaisyUI does this via `<dialog>` element, not `div`) keyboard users can tab behind the modal.

**Fix:** Switch to DaisyUI's `<dialog>` element with `ref.showModal()` / `ref.close()` which provides native focus trapping, or use the shadcn `Dialog` (Radix) which has this built in. Given the PostConfirmDialog is the critical compliance gate for irreversible Post actions, accessible keyboard confirmation is important.

---

### L-01 · Typography · Spec specifies "TH Sarabun New" for UI; implementation uses Noto Sans Thai as primary [Low] [Confirmed]

**File/component:** `frontend/tailwind.config.ts` fontFamily, `frontend/app/layout.tsx`.

**Evidence:**
```tsx
fontFamily: {
  sans: ['var(--font-noto-thai)', 'var(--font-inter)', ...],
  ui:   ['var(--font-noto-thai)', 'var(--font-inter)', ...],
  doc:  ['var(--font-sarabun)', '"TH Sarabun New"', 'serif'],
```
The spec §2.2 sets `--font-th: 'TH Sarabun New', 'Sarabun'` as the primary Thai font. The implementation uses Noto Sans Thai for UI (body) and Sarabun only for `font-doc` (print documents). This is a deliberate product decision (Noto renders better at screen sizes) but diverges from the spec.

**Impact:** Cosmetic. Noto Sans Thai is a valid choice for screen readability. The mismatch should be resolved by updating the spec, not the code.

---

### L-02 · Data Display · Status badge spec says "Draft = dashed border"; implementation uses solid bg [Low] [Confirmed]

**File/component:** `frontend/components/ui/StatusBadge.tsx`, globals.css `.pill-draft`.

**Evidence:**
```tsx
// StatusBadge:
draft: 'bg-status-draft-bg text-status-draft',
// globals.css:
.pill-draft { @apply pill bg-base-300 text-base-content; }
```
Spec §2.4: Draft = "gray bg, dashed border." Neither implementation renders a dashed border.

**Fix (low effort):** Add `border border-dashed border-status-draft` to the Draft badge. Makes the Draft → Posted visual transition more legible.

---

### L-03 · Data Display · Document chain cross-reference missing "Posted" lock icon [Low] [Suspected]

**File/component:** Document detail pages using `DocActionBar` / `StatusBadge`.

**Evidence:** Spec §1 principle "Immutable transparency": Posted documents should show "🔒 Posted" clearly. `StatusBadge` for "Posted" renders `bg-status-success-bg text-status-success` with a dot, but no lock icon. The spec explicitly lists this as a UI principle.

**Fix:** Add `<Lock className="h-3 w-3" />` to the Posted variant in `StatusBadge`.

---

### L-04 · Accessibility · `number-gaps` page inline loading uses `<p>` not ARIA live region [Low] [Confirmed]

**File/component:** `frontend/app/(dashboard)/number-gaps/page.tsx` line 43.

**Evidence:**
```tsx
{isLoading && <p className="text-base-content/50">{tc('loading')}</p>}
```
This `<p>` is conditionally rendered. Screen readers won't announce the loading state because it's not in an ARIA live region.

**Fix:** `<p aria-live="polite" aria-busy={isLoading}>` or use a visually-hidden `<span role="status">`.

---

### L-05 · Visual Hierarchy · SVG chart axis labels use `text-[8px]` / `text-[9px]` — below legibility threshold [Low] [Confirmed]

**File/component:** `frontend/app/(dashboard)/page.tsx` TrendBars function; `frontend/app/(dashboard)/reports/tax-summary/page.tsx` GroupedBars function.

**Evidence:**
```tsx
<text ... className="fill-base-content/50 text-[8px]">{label}</text>
<text ... className="fill-base-content/40 text-[9px]">{kBaht(max, locale)}</text>
```
8px SVG text is below WCAG minimum recommended body text (14px) and will render as unreadable on standard displays, especially for Thai month labels.

**Fix:** Increase to `text-[10px]` minimum (still compact). Consider showing only every-other label if space is tight, or using `<title>` tooltips (already present on bar rects) as the primary data disclosure.

---

## What's Well-Designed

1. **Live document preview on create forms** — `DocumentCreateLayout` splits form (left) + live `PaperDocument` preview (right), sticky on large screens. This is genuinely excellent UX for an accountant who must see what the Tax Invoice will look like before posting.

2. **StatusBadge design** — bilingual (Thai + English), semantic dot colour, `withEn` mode available, token-driven colours, `line-through` on Voided. The intent (text + colour, never colour alone) matches accessibility best practice.

3. **PostConfirmDialog** — the immutability warning in Thai citing ม.86/4 is excellent compliance-visible design. The summary preview (customer, total, VAT, recipients) gives an accountant a final sanity-check before an irreversible action.

4. **Font stack** — Noto Sans Thai + Inter + JetBrains Mono (monospace for doc numbers) is well-considered for Thai data-dense UI. The `font-variant-numeric: tabular-nums` on `.num` / `input.num` ensures money columns align correctly.

5. **`.pill-*` / `.doc-no` CSS utilities** — globally available, token-driven, consistent. Document number as monospace badge (`font-mono text-xs bg-base-200`) is a clean pattern.

6. **DataTable shared component** — one TanStack-powered table across all list pages. Unified sort, column filter (text/select/dateRange), client global search, pagination. Every list page is visually consistent; the `RowLink` mono variant for doc numbers is a good micro-detail.

7. **Responsive `DocumentCreateLayout`** — the two-column grid uses `minmax(440px,500px)` + `1fr` and "collapses to 1 column on small" per the component comment. Form-first, preview second on narrow screens — correct priority.

8. **Design token system** — `design-tokens.css` defines a coherent warm-peach + ink palette with CSS custom properties. Tailwind config maps them to utility classes. `StatusBadge` uses them correctly. The system is production-ready when all components adopt it.

9. **Keyboard-accessible focus ring** — `*:focus-visible { box-shadow: 0 0 0 2px oklch(var(--p)/0.5); }` is global, theme-aware, and consistent. No `outline: none` without replacement.

10. **Onboarding wizard** — the two-phase admin creation → company setup flow is well-structured with DaisyUI spinner for the async probe phase, structured sections, and inline validation errors via `err()` helper.

---

## Quick Wins (cheap, high-impact)

| # | Fix | Effort | Impact |
|---|-----|--------|--------|
| QW-1 | Replace `KPI_TONE`/`ALERT_TONE`/`TONE` objects in `page.tsx` + `tax-summary/page.tsx` with semantic token classes (`status-success-bg`, etc.) | ~30 min | High — theme-correctness + dark mode |
| QW-2 | Add `aria-label` to Topbar search pill; disable/remove until functional OR add a route-based cmdk placeholder | ~20 min | High — removes false affordance |
| QW-3 | Add `backHref` prop to `PageHeader`; wire on all `[id]/page.tsx` detail pages | ~45 min | High — back navigation for accountants |
| QW-4 | Move `toLocaleString()` → `formatTHB()` in products table (`settings/products/page.tsx` line 106) | 5 min | Medium — locale-correct formatting |
| QW-5 | Move `number-gaps` nav item from `sales` section to `reports` or `admin` | 5 min | Medium — correct IA |
| QW-6 | Add `border border-dashed border-status-draft` to Draft StatusBadge | 10 min | Low-Medium — spec compliance + visual clarity |
| QW-7 | Add `<Lock className="h-3 w-3" />` icon to Posted variant in `StatusBadge` | 10 min | Low-Medium — compliance-visible principle |
| QW-8 | Move hardcoded Thai strings in `ChainRowPrint.tsx` and `ActivityLog.tsx` to i18n messages | 30 min | Medium — EN mode correctness |
| QW-9 | Increase SVG axis label sizes from `text-[8px]`/`text-[9px]` to `text-[10px]`/`text-[11px]` | 5 min | Low — legibility |
| QW-10 | Elevate dashboard `h2` section labels from `text-sm` → `text-base font-semibold` | 10 min | Medium — visual hierarchy |

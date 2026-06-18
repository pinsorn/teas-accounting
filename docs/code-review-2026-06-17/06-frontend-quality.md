# Frontend Code Quality Review — TEAS — 2026-06-17

## Summary

Overall posture: **Moderate risk, well-architected foundation with known technical debt.**
The security model is sound — JWT is httpOnly cookie via BFF proxy, never in localStorage. React Query usage is consistent and invalidation is wired. The biggest issues are: a stale legacy `api-client.ts` that bypasses the BFF (still present and imported by login page), pervasive `unknown`/`apiPost<unknown>` typing across ~30 mutations, 75 of 76 page files marked `'use client'` (zero RSC pages aside from layout), and ~10 hardcoded Thai strings that bypass `next-intl`.

**Counts by severity:**
| Severity | Count |
|---|---|
| High | 3 |
| Medium | 5 |
| Low | 4 |

---

## Findings

---

### H-1 · Legacy `api-client.ts` bypasses BFF — direct backend exposure

**Severity:** High  
**File:** `frontend/lib/api-client.ts:22-28`

```ts
const baseUrl = process.env.NEXT_PUBLIC_API_URL ?? 'http://localhost:5000';

export async function api<T = unknown>(path: string, init: RequestInit = {}): Promise<T> {
  const res = await fetch(`${baseUrl}${path}`, {
    credentials: 'include',
    ...
```

**Confidence:** [Confirmed]  
**Why:** `NEXT_PUBLIC_*` env vars are bundled into the client JS bundle — they are visible in the browser. This module makes direct browser→backend calls, bypassing the BFF proxy (`/api/proxy`) that injects the Bearer token from the httpOnly cookie. Its own comment says *"TODO: add a generic /api/proxy/[...path] handler; until then this wrapper is only for unauthenticated/public endpoints."* The login page imports `ApiError` from this file (`app/(auth)/login/page.tsx:11`), but the `api()` function itself is the concern — any developer who accidentally imports `api()` instead of `apiGet()` from `lib/api.ts` makes authenticated calls without the Bearer token. The `/api/proxy` route handler does already exist (it is the primary path), so this file is vestigial and confusing.

**Fix:** Delete or narrow `lib/api-client.ts` to export only `ApiError` (the error class) and remove the `api()` function and `baseUrl` constant. Update `lib/api.ts` to import `ApiError` from a shared location. Remove `NEXT_PUBLIC_API_URL` from `.env` and docs once done.

---

### H-2 · All 75 route pages are `'use client'` — zero RSC data-fetching

**Severity:** High  
**File:** `frontend/app/(dashboard)/**/*.tsx` (all 75 pages, confirmed by grep)

```
app/(auth)/login/page.tsx:1:'use client'
app/(dashboard)/page.tsx:1:'use client'
app/(dashboard)/invoices/page.tsx:1:'use client'
... [72 more]
```

**Confidence:** [Confirmed]  
**Why:** Next.js 15 App Router's primary benefit is Server Components: data fetching on the server, zero JS to client, no loading flash. TEAS ships zero RSC pages — every page is a full client component that fetches from React Query after hydration. This means: (a) every page incurs a client-side request waterfall before content appears; (b) the dashboard makes 6+ parallel React Query calls in the browser rather than composing server fetches; (c) initial HTML is an empty skeleton (bad for perceived performance on accounting workflows where data is the content). The dashboard layout (`app/(dashboard)/layout.tsx`) is correctly a server component but every child page nullifies that benefit.

This is an architectural pattern, not a per-page oversight. The root cause is that `lib/queries.ts` file-level `'use client'` directive pulls all hooks into the client module graph, which is correct for hooks — but pages that only *read* data could use server `fetch()` + RSC, then pass data as props to thin client components for interactivity.

**Fix:** Progressively convert read-heavy list pages (invoices list, customers list, vendors list) to RSC: use `getTranslations` + native `fetch` server-side; keep form/edit pages as client. This is a medium-effort refactor but directly improves LCP.

---

### H-3 · `useCreateReceipt` and ~30 other mutations typed as `req: unknown`

**Severity:** High  
**File:** `frontend/lib/queries.ts` (multiple lines, e.g. 167, 199, 883, 918, 966, 983, 1049, 1148)

```ts
// line 167
mutationFn: (req: unknown) => apiPost<{ receipt_id: number }>('receipts/', req),

// line 883
mutationFn: (req: unknown) => apiPost<{ product_id: number }>('products/', req),

// line 918
mutationFn: (req: unknown) => apiPost<{ quotation_id: number }>('quotations/', req),
```

**Confidence:** [Confirmed]  
**Why:** At least 28 `mutationFn` parameters are typed `req: unknown` or the return is `apiPost<unknown>`. This defeats TypeScript's ability to catch shape mismatches between form values and the API contract. The request types *do* exist in `lib/types.ts` (e.g. `CreateTaxInvoiceRequest`, `CreateVendorRequest`, `CreateCompanyRequest`) but are not applied to these mutations. A call site can pass an arbitrary object and TypeScript will not catch the error until runtime. Combined with the large number of `as unknown` casts in `apiPost<unknown>` returns, this means mutation calls have no compile-time safety.

**Fix:** Replace `req: unknown` with the correct request type from `lib/types.ts`. The types already exist; this is a mechanical substitution. Same for `apiPost<unknown>` returns — type them with the actual response shape (or `void` where the response is discarded).

---

### M-1 · Hardcoded Thai validation messages bypass `next-intl`

**Severity:** Medium  
**File:** `frontend/app/(auth)/login/page.tsx:14-15,35,42`

```ts
const schema = z.object({
  username: z.string().min(1, 'ระบุชื่อผู้ใช้'),
  password: z.string().min(1, 'ระบุรหัสผ่าน'),
});
// line 35
toast.info('กรอก MFA code 6 หลักจาก Authenticator app');
// line 42
const msg = err instanceof ApiError ? err.message : 'เกิดข้อผิดพลาด';
```

**Confidence:** [Confirmed]  
**Why:** These Zod error strings and toast messages are hardcoded Thai literals, bypassing `next-intl`. The project supports TH+EN (`messages/th.json` + `messages/en.json`); login is the entry point seen by all users. EN users will see Thai validation messages. CLAUDE.md §5 requires all user-facing strings via i18n.

**Fix:** Use `useTranslations('auth')` (already called `useTranslations` elsewhere in the file, which uses `next-intl`). Move strings to `messages/{th,en}.json` under an `auth.*` namespace.

---

### M-2 · Hardcoded Thai month name arrays on the dashboard

**Severity:** Medium  
**File:** `frontend/app/(dashboard)/page.tsx:17-20`

```ts
const THAI_MONTHS = ['ม.ค.', 'ก.พ.', 'มี.ค.', 'เม.ย.', 'พ.ค.', 'มิ.ย.',
  'ก.ค.', 'ส.ค.', 'ก.ย.', 'ต.ค.', 'พ.ย.', 'ธ.ค.'];
const THAI_MONTHS_FULL = ['มกราคม', 'กุมภาพันธ์', 'มีนาคม', ...];
```

**Confidence:** [Confirmed]  
**Why:** These static arrays are Thai-only. When the user switches to EN locale, the chart axis and month labels still show Thai text. The `useTranslations` hook is already in scope at line 30 — `Intl.DateTimeFormat` with `locale` from `next-intl` would render the correct locale automatically.

**Fix:** Replace with `new Intl.DateTimeFormat(locale, { month: 'short' }).format(date)` using the active locale from `useLocale()`.

---

### M-3 · `SidebarNav` locale toggle writes bare `document.cookie` — no `httpOnly` or SameSite enforcement

**Severity:** Medium  
**File:** `frontend/components/app-shell/SidebarNav.tsx:162`

```ts
document.cookie = `locale=${next}; path=/; max-age=31536000; samesite=lax`;
```

**Confidence:** [Confirmed]  
**Why:** The locale cookie is set directly from client JS without `secure` flag. In production (HTTPS), the cookie is sent over plain HTTP if the browser is redirected from HTTP. The cookie is not sensitive but the pattern is inconsistent with the project's secure-cookie stance. Also, the 1-year max-age means stale locale preferences persist across device resets.

**Fix:** Add `; secure` when `window.location.protocol === 'https:'`. Consider a short server-side route for locale preference instead.

---

### M-4 · `useEffect` seeding form state from query data (settings/company, tax-invoices/new)

**Severity:** Medium  
**File:** `frontend/app/(dashboard)/settings/company/page.tsx:66-77`, `app/(dashboard)/tax-invoices/new/page.tsx:88-99`

```ts
// settings/company:66
useEffect(() => {
  if (!p) return;
  setForm({ tradeName: p.tradeName ?? '', logoUrl: p.logoUrl ?? '', ... });
}, [p]);

// tax-invoices/new:88
useEffect(() => {
  if (!fromQuotationId || !quotation.data) return;
  reset({ customerId: q.customerId, lines: q.lines.map(...) });
}, [fromQuotationId, quotation.data, reset]);
```

**Confidence:** [Confirmed]  
**Why:** Using `useEffect` to sync React Query data into local form state is an anti-pattern — it creates a copy that can diverge, and it fires after each re-render where the data changes (e.g. after a background refetch). The correct RHF pattern is to pass `defaultValues` from the query data and use `reset()` inside the form setup, or use the `useForm({ values: data })` option in RHF v7+ which handles re-seeding properly.

**Fix:** Replace `useEffect(() => setForm(data), [data])` with `useForm({ values: queryData })` (RHF's `values` prop auto-resets when the value changes). For the quotation prefill, gating on `enabled: !!fromQuotationId` + using `defaultValues` avoids the effect entirely.

---

### M-5 · `problemToast` default fallback is a hardcoded Thai string

**Severity:** Medium  
**File:** `frontend/lib/api.ts:20`

```ts
export function problemToast(err: unknown, fallback = 'เกิดข้อผิดพลาด'): void {
```

**Confidence:** [Confirmed]  
**Why:** This utility is called from across the entire app as the generic error handler. EN-locale users will see Thai for any unhandled error. This is a library-level function — it cannot call `useTranslations` (not a React component), but it should accept the fallback string from the call site where a translation is available, not hardcode it.

**Fix:** Keep the fallback param but remove the default Thai string — callers must pass a translated fallback. Or export a React-safe hook wrapper that resolves the translation internally.

---

### L-1 · `documents/page.tsx` `forms.map` — key confirmed present but `RD_FILING_CHANNELS.map` needs verification

**Severity:** Low  
**File:** `frontend/app/(dashboard)/documents/page.tsx:38,53,71`

```tsx
{RD_FILING_CHANNELS.map((c) => (
  <a key={c.code} ...>   // key={c.code} present — GOOD
{RD_FORM_CATEGORIES.map((category) => {
  return <section key={category} ...>  // key={category} present — GOOD
{forms.map((f) => (
  <FormRow key={f.code} form={f} />   // key present — GOOD
```

**Confidence:** [Confirmed — false alarm from initial grep]**  
Keys are present. No issue here.

---

### L-2 · `localStorage` for sidebar collapse state — acceptable non-sensitive use

**Severity:** Low  
**File:** `frontend/components/app-shell/SidebarNav.tsx:138,144`

```ts
setCollapsed(localStorage.getItem(COLLAPSE_KEY) === '1');
localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0');
```

**Confidence:** [Confirmed]  
**Why:** This is UI preference (sidebar expand/collapse) — not sensitive data. CLAUDE.md §10 forbids `localStorage` for sensitive data; this is not sensitive. However, the `useEffect` reads `localStorage` on mount only, which is a common SSR-safe pattern. Minor note: no `try/catch` around `localStorage` calls means incognito mode + storage disabled throws uncaught DOMException.

**Fix:** Wrap in `try { ... } catch { /* ignore */ }` for robustness against restricted storage contexts.

---

### L-3 · `e2e/helpers/rbac-detail-fixtures.ts:35` returns `any`

**Severity:** Low  
**File:** `frontend/e2e/helpers/rbac-detail-fixtures.ts:35`

```ts
async function postOk(page: Page, path: string, data: unknown, label: string): Promise<any> {
```

**Confidence:** [Confirmed]  
**Why:** Test helper returning `any` — callers lose type safety on the returned fixture IDs. Low impact since this is test code, but it masks shape bugs in fixture setup.

**Fix:** Type the return as `Promise<Record<string, number>>` or a proper union, since it's used for ID extraction.

---

### L-4 · `fetchAllPages` fetches up to 100,000 records client-side for list pages

**Severity:** Low  
**File:** `frontend/lib/api.ts:74-91`, `frontend/lib/queries.ts:101-106`

```ts
// queries.ts:101
export function useTaxInvoices(_filters?: TaxInvoiceFilters) {
  return useQuery({
    queryKey: ['tax-invoices', 'all'],
    queryFn: () => fetchAllPages<TaxInvoiceListItem>('tax-invoices'),
  });
}

// api.ts:81
for (let guard = 0; guard < 1000; guard++) { // 1000 pages × 100 items = 100k rows
```

**Confidence:** [Confirmed]  
**Why:** The comment ("cont.81 DataTable — fetch-all so the unified client-side TanStack table filters/sorts the whole set") explains the intent. However, with 1000 page cap × 100 items/page, this could fetch 100,000 rows into the browser. For TEAS at SME scale this is likely fine today, but it will degrade as data grows. The `_filters` parameter is explicitly ignored. Server-side pagination is the correct long-term fix.

**Fix:** Low priority now, but track in `plan.md`. When any list page exceeds ~500 rows, switch to server-side filtering + pagination on that endpoint.

---

## Verified GOOD (patterns confirmed correct)

1. **JWT security model** — `access_token` is set as `httpOnly; sameSite=lax` by the BFF route handler (`app/api/auth/login/route.ts:55-61`). The primary API client (`lib/api.ts`) uses `/api/proxy` (same-origin), so JWT never touches client JS. [Confirmed]

2. **`dangerouslySetInnerHTML` — not used** in any app source file (only in `node_modules` type definitions). [Confirmed]

3. **React Query invalidation** — consistent pattern: `onSuccess: () => qc.invalidateQueries({ queryKey: [...] })` used throughout `queries.ts`. Note: `onSuccess` is deprecated in TanStack Query v5 in favour of `mutationOptions` or passing callbacks at call sites, but it still works in the installed version and will not break at runtime.

4. **`useEffect` for localStorage init** (`SidebarNav.tsx:137-139`) — correct SSR-safe pattern: reads `localStorage` only on mount, empty dependency array. [Confirmed]

5. **RHF + Zod wired correctly** — `zodResolver(schema)` used on every form inspected; `formState.errors` propagated to inputs; `isSubmitting` used to disable submit buttons. [Confirmed]

6. **`useQuery.enabled` guards** — `enabled: id > 0` / `enabled: Number.isFinite(id) && id > 0` present on all detail-fetch hooks to prevent fetching on null/0 IDs. [Confirmed]

7. **`payment-vouchers/new` row `.map` key** — `key={r.key}` where `r.key` is a stable UUID assigned on row creation, not the array index. [Confirmed]

8. **Auth token never in client storage** — grep across all app files shows `localStorage` used only for sidebar collapse preference (`COLLAPSE_KEY`), not any auth credential. [Confirmed]

9. **`documents/page.tsx` map keys** — all three `.map()` calls have explicit stable keys (`c.code`, `category`, `f.code`). The initial grep false-positive was a line-level match that didn't include the `key=` on the next line. [Confirmed]

10. **`SidebarNav` RBAC gating** — `useMePermissions()` + `useSystemInfo()` are awaited before rendering nav items; a `gatesReady` sentinel prevents flash of wrong permissions. [Confirmed]

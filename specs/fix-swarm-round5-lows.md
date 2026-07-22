# Fix swarm round 5 residual LOW nits (tax01 + audit01)

Evidence: `swarm-findings/round5/tax01.md` (nit 1), `swarm-findings/round5/audit01.md` (nits 2, 3).
FRONTEND only. Blast radius cap: 6 files total (already mapped below — do not exceed;
if a fix needs a 7th file, STOP and report instead of expanding scope).

Root-cause investigation for all 3 nits was already done by the orchestrator (Fable) by reading
the actual source — this spec tells you exactly what to change and why. Don't re-derive the design,
just verify the line numbers still match (code may have shifted slightly) and apply.

FYI (no action needed): co5's pp30 address gap itself is already fixed by data (reg_house_no filled
on prod). Nit 1 below fixes the TOAST LANGUAGE for any future 422 of this shape, not the 422 itself.

Windows/PowerShell env, gates run from `frontend/`. Known footgun (troubles-wiki.md): **do not run
`pnpm next build` while a `next dev` server is live against the same checkout** — it corrupts the
dev server's `.next/` cache (dev then 500s on every route until killed+restarted). If a dev server
might be running, check first (`netstat -ano | findstr :3000` or just ask) before running the build
gate.

---

## Nit 1 — file-response error toast shows English (tax01, LOW-MED)

**Symptom:** the ภ.พ.30 "ดาวน์โหลดไฟล์โอนย้ายข้อมูล (.txt)" button's 422 error toast
(`pp30_batch.missing_address`) rendered the backend's hardcoded-English `detail` string instead of
Thai, even though the app already has a working Thai-toast infrastructure
(`lib/api.ts` `problemToast()` + `lib/api/errors.ts` `apiErrorToast()`/`errorToToast()` +
`lib/i18n/problems.ts` `resolveProblemKey()`).

**Root cause (confirmed by reading the code, not the swarm's guess):** it is NOT
`throwFileResponseError()`'s field-ordering (that function is fine — it builds an `ApiError` with
`code = body.title`, `message = body.detail`, exactly what `problemToast`/`apiErrorToast` expect).
The actual bug is that `frontend/app/(dashboard)/reports/pnd30/page.tsx`'s two download buttons
bypass that infrastructure entirely — they catch with a raw
`.catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))`, which always shows
the raw (English) `ApiError.message`, never consulting `resolveProblemKey`. Every OTHER page in the
app that wants Thai-by-code resolution already routes through `apiErrorToast`/`errorToToast`
(see `frontend/app/(dashboard)/payroll/[id]/page.tsx`, `.../reports/balance-sheet/page.tsx`,
`.../settings/employees/page.tsx` for the existing pattern) — pnd30/page.tsx just never got that
treatment.

Also: `lib/i18n/problems.ts`'s `TH` dict has no entries for the `pp30_batch.*` codes yet, so even
after routing through the resolver, `resolveProblemKey('pp30_batch.missing_address')` would return
`null` and fall back to the English detail anyway (same failure, one layer up). Both parts are
needed.

### Fix — `frontend/lib/i18n/problems.ts`
Add two entries to the `TH` dict (there are only two `pp30_batch.*` codes — confirmed by grepping
`backend/src/Accounting.Infrastructure/TaxFilings/Pp30BatchExportService.cs`, which throws exactly
`pp30_batch.no_data` and `pp30_batch.missing_address`, nothing else). Insert as a new section, e.g.
right after the `company_info.pnd30_invalid` entry (~line 112, before the `// payroll.*` section
comment) — keep the existing one-code-per-line style:

```ts
  // pp30_batch.* (ภ.พ.30 RD Prep "Format กลาง" batch-file export guard — Pp30BatchExportService)
  'pp30_batch.no_data': 'ไม่มียอดขายในงวดนี้ ไม่สามารถสร้างไฟล์ ภ.พ.30 ได้ (ต้องมียอดขายมากกว่า 0)',
  'pp30_batch.missing_address':
    'ข้อมูลที่อยู่จดทะเบียนของบริษัทไม่ครบถ้วน (ต้องมีเลขที่และรหัสไปรษณีย์) กรุณากรอกข้อมูลโปรไฟล์บริษัทให้ครบก่อนสร้างไฟล์ ภ.พ.30',
```

These are TH-only (matches this file's existing TH-only-by-design pattern — the file's own header
comment explains why there's no parallel EN dict: an `en` locale falls straight through to the
backend's own English `detail`, which is already fine EN). Do NOT add an `en` dict here — that would
break the file's established shape for zero benefit. This is also why the "i18n th/en key parity"
gate is a non-issue for this file specifically (it's intentionally TH-only, mirrors
`lib/i18n/validation.ts`'s DICT shape) — the parity gate concerns `messages/en.json` /
`messages/th.json` (next-intl `t()` keys), which this fix does not touch at all (no new `t('...')`
key added anywhere in this task).

### Fix — `frontend/app/(dashboard)/reports/pnd30/page.tsx`
Two call sites currently do:
```tsx
onClick={() => openPdf(`tax-filings/pnd30/pdf?period=${toPeriod(ym)}`)
  .catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))}
```
and
```tsx
onClick={() => downloadFile(
  `tax-filings/pnd30/batch-file?period=${toPeriod(ym)}`,
  `PP30_${toPeriod(ym)}.txt`)
  .catch((e: unknown) => toast.error(e instanceof Error ? e.message : 'Error'))}
```
Replace both `.catch(...)` handlers with `.catch(apiErrorToast)` (the existing one-stop sink in
`frontend/lib/api/errors.ts` — `apiErrorToast(err) = problemToast(err, errorToToast(err))`: Thai
primary line via `resolveProblemKey`, EN `detail` as the muted secondary/description line, 8s sonner
duration — this IS "matching problemToast()'s existing pattern", it just reuses the helper instead
of hand-rolling it). Add the import: `import { apiErrorToast } from '@/lib/api/errors';`. Keep the
existing `toast` import (still used by `toast.success(...)` in `run()`).

**Do not** touch `frontend/components/tax-filings/WhtFilingClient.tsx` even though it has the
identical `toast.error(e.message)` pattern for its own PDF/batch buttons — same bug family, but out
of THIS task's blast-radius budget and not named in tax01's finding (tax01 tested `/reports/pnd30`
only). Note it in your report as an observed-but-untouched sibling for a future pass.

---

## Nit 2 — bank-reconciliation 403-vs-empty ambiguity (audit01, LOW)

**File:** `frontend/app/(dashboard)/reports/bank-reconciliation/page.tsx`

**Symptom:** `noStatementImported` (line ~45) is computed as
`bankAccountId != null && !imports.isLoading && (imports.data?.length ?? 0) === 0`. When the
`useStatementImports` fetch 403s (a role holding `bank.report.read` but not the write-shaped
`bank.statement.import` scope, e.g. AUDITOR), react-query leaves `imports.data` as `undefined` on
error — same as a genuine empty array — so the page shows the same "ยังไม่มีการนำเข้า Statement"
(`diffNoStatementBadge`) badge whether the fetch was denied or genuinely returned zero imports. Not
currently showing WRONG info by luck (this account's real state happens to also be zero imports),
but the logic can't tell the two cases apart.

**Fix:** distinguish the error case. `useStatementImports` is a plain `useQuery` (see
`frontend/lib/queries.ts` ~line 1227), so it already exposes `isError`. Change:
```tsx
const noStatementImported = bankAccountId != null && !imports.isLoading
  && (imports.data?.length ?? 0) === 0;
```
to:
```tsx
const noStatementImported = bankAccountId != null && !imports.isLoading && !imports.isError
  && (imports.data?.length ?? 0) === 0;
```
Then in the JSX (~line 131-135), the badge ternary currently is:
```tsx
badge={report.difference !== 0 ? (
  noStatementImported
    ? <span className="badge badge-ghost badge-xs">{t('diffNoStatementBadge')}</span>
    : <span className="badge badge-warning badge-xs">{t('diffUnreconciledBadge')}</span>
) : undefined}
```
Add a third branch so a 403'd imports lookup shows NEITHER claim (hides the badge — the minimal
"neutral state" option the finding explicitly allows) instead of falling through to
`diffUnreconciledBadge` (which would be just as wrong — it asserts a real reconciling item when we
actually just don't know):
```tsx
badge={report.difference !== 0 ? (
  imports.isError
    ? undefined
    : noStatementImported
      ? <span className="badge badge-ghost badge-xs">{t('diffNoStatementBadge')}</span>
      : <span className="badge badge-warning badge-xs">{t('diffUnreconciledBadge')}</span>
) : undefined}
```
No new i18n keys needed. `imports` is already in scope in this component.

---

## Nit 3 — stray 403s on expense-categories / employees (audit01, LOW)

**Root cause (confirmed by reading the code — the swarm's "Link-prefetch" guess was wrong):** three
`/new` pages — `frontend/app/(dashboard)/vendor-invoices/new/page.tsx`,
`frontend/app/(dashboard)/payment-vouchers/new/page.tsx`,
`frontend/app/(dashboard)/expense-claims/new/page.tsx` — each gate their create form behind:
```tsx
const perms = useMePermissions();
const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
if (perms.data && !canCreate) {
  return ( /* state-no-access deny box */ );
}
return ( /* the full create form */ );
```
While `perms.data` is still `undefined` (react-query loading window, before `/me/permissions`
resolves), `perms.data && !canCreate` short-circuits to `false` — so the deny check does NOT fire on
the FIRST render, and the full form renders immediately, including its child selector components:
`ExpenseCategorySelector` (`frontend/components/ui/ExpenseCategorySelector.tsx`) fires a raw
`apiGet('expense-categories')` unconditionally in a `useEffect` on mount, and `EmployeeSelector`
(`frontend/components/ui/EmployeeSelector.tsx`) fires `useEmployees()` unconditionally on mount. For
a role like AUDITOR (no `sys.expense_category.read`/employee-read grant), those fetches 403
IMMEDIATELY — then a subsequent render (once `perms.data` arrives and `canCreate` is false) swaps to
the deny box, but the 403 already happened and is already in the console/network log. This exactly
matches audit01's count: 3 `expense-categories` 403s (VI/new + PV/new + expense-claims/new, all use
`ExpenseCategorySelector`) + 1 `employees` 403 (expense-claims/new only, the only one of the three
using `EmployeeSelector`).

This is a real race, not a red herring: `ExpenseCategorySelector`/`EmployeeSelector` ARE correctly
absent from the FINAL rendered DOM for AUDITOR (which is why audit01's WP1 sweep counted 0 form
inputs and didn't flag it there) — the fetch still fires and 403s during the brief mount-before-deny
window.

**Existing in-repo pattern that avoids this race:** `frontend/app/(dashboard)/settings/companies/page.tsx`
(~line 46) already does it correctly — it checks `perms.isLoading` FIRST and renders a loading state
before ever reaching the allow/deny decision, so the gated body never mounts prematurely:
```tsx
if (perms.isLoading) {
  return ( /* loading placeholder */ );
}
if (!perms.data?.isSuperAdmin) {
  return ( /* deny */ );
}
```

**Fix:** in each of the 3 files (`vendor-invoices/new/page.tsx`, `payment-vouchers/new/page.tsx`,
`expense-claims/new/page.tsx`), add a `perms.isPending` short-circuit BEFORE the existing
`if (perms.data && !canCreate)` block, so the form (and its eager-fetching children) never mounts
until permission status is actually known:
```tsx
const perms = useMePermissions();
const canCreate = perms.data?.isSuperAdmin || (perms.data?.permissions.includes(SCOPE) ?? false);
if (perms.isPending) return null;
if (perms.data && !canCreate) {
  return ( /* existing deny box — unchanged */ );
}
```
Use `return null;` (not a loading spinner/text) — these 3 pages already wrap their form in
`<Suspense fallback={null}>` immediately below, so `null` while permissions resolve matches the
page's own existing "blank flash, not a spinner" convention; don't introduce a new loading-state UI
or i18n key for this. Do NOT touch the other 13 `/new` pages that share the same
`if (perms.data && !canCreate)` shape but don't mount a permission-scoped eager-fetching selector
(no observed 403 from them) — that would blow the blast-radius budget for no evidenced bug; note
them in your report as an out-of-scope sibling pattern if you want, but do not edit them.

---

## Verification gates (run from `frontend/`)
1. `pnpm tsc --noEmit` — clean, 0 errors.
2. `pnpm next build` — clean build, all routes compile. Check troubles-wiki.md's `next dev`-corruption
   note above FIRST — do not run this while a dev server is live against the same checkout.
3. i18n th/en key parity: this task adds ZERO new `t('...')` keys (verify by re-reading your own
   diff) — `messages/en.json`/`messages/th.json` are untouched, so parity holds trivially. Confirm
   with a quick diff review, no new tooling needed.
4. ม-glyph scan clean: grep your changed files for the Bengali `ম` character (NOT the Thai `ม` you
   are intentionally adding) — it must not appear. `rg 'ম' frontend/lib/i18n/problems.ts
   frontend/app/\(dashboard\)/reports/pnd30/page.tsx frontend/app/\(dashboard\)/reports/bank-reconciliation/page.tsx`
   (and the other changed files) should return nothing.

## Blast radius (6 files, do not exceed)
- [x] `frontend/lib/i18n/problems.ts` — added `pp30_batch.no_data` / `pp30_batch.missing_address` TH entries (line ~114-117, after `company_info.pnd30_invalid`, before `// payroll.*`). Evidence: ম-glyph scan clean, tsc/build clean.
- [x] `frontend/app/(dashboard)/reports/pnd30/page.tsx` — added `import { apiErrorToast } from '@/lib/api/errors';` (line 9); both download-button `.catch((e) => toast.error(...))` handlers (lines 85, 94) replaced with `.catch(apiErrorToast)`. `toast` import kept (still used by `toast.success` in `run()`). Evidence: tsc/build clean, diff reviewed — matches spec exactly.
- [x] `frontend/app/(dashboard)/reports/bank-reconciliation/page.tsx` — `noStatementImported` (line 45) now also requires `!imports.isError`; badge ternary (line 131-135) gained an `imports.isError ? undefined : ...` outer branch so a 403'd imports fetch hides the badge instead of falling through to `diffUnreconciledBadge`. No new i18n keys (both `t()` calls pre-existing). Evidence: tsc/build clean.
- [x] `frontend/app/(dashboard)/vendor-invoices/new/page.tsx` — added `if (perms.isPending) return null;` (line 439, before the existing `if (perms.data && !canCreate)` deny check) so `ExpenseCategorySelector` never mounts before permission status resolves. Evidence: tsc/build clean.
- [x] `frontend/app/(dashboard)/payment-vouchers/new/page.tsx` — same `if (perms.isPending) return null;` guard added (line 511). Evidence: tsc/build clean.
- [x] `frontend/app/(dashboard)/expense-claims/new/page.tsx` — same `if (perms.isPending) return null;` guard added (line 89, before the deny check; this page has no `<Suspense>` wrapper since it doesn't use `useSearchParams`, but `return null` still matches its blank-flash convention on first render). Evidence: tsc/build clean.

All 4 verification gates pass — see worker report for verbatim output. Task complete, no scope changes.

## Report format
Per nit: what changed + where (file:line) + evidence. Include the 4 gate outputs. Do NOT
`git commit`/push — orchestrator commits after diff review.

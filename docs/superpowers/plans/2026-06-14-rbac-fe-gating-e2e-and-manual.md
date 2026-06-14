# RBAC FE Gating — Playwright e2e (all roles × all buttons, VAT + non-VAT) + combined user manual — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove — automatically (Playwright) and visually (screenshots) — that the frontend permission-gating shows/hides every nav item and every action button correctly for **every role**, on **both a VAT and a non-VAT company**; and produce ONE combined user manual (คู่มือ) documenting who-sees-what, generated from the same run so it can never drift.

**Architecture:** A single source-of-truth **manifest** (`rbac-manifest.ts`) maps every gated UI control → its route + selector + required permission (+ `vatOnly`). The Playwright spec logs in as one seeded user **per role per company**, reads that user's live grants from `GET /me/permissions` + `GET /system/info` (vatMode), and asserts each control's DOM presence equals `expected = isSuperAdmin || perms.has(perm)` (and, for `vatOnly`, `&& vatMode`). The same run captures screenshots and emits a results JSON; a small generator turns the manifest + results into the combined manual. This mirrors the backend `RbacCartesianTests` Cartesian approach on the FE — data-driven, not hardcoded per role.

**Tech Stack:** Playwright (`@playwright/test`, msedge channel, `frontend/playwright.config.ts`), Next.js 15 app (`next start` :3000), ASP.NET API (:5080, Development), Postgres seed SQL (`Migrations/SqlScripts`), BFF proxy `/api/proxy/*` (cookie auth). The manual is plain Markdown under `docs/manual/`.

**Why this is safe to assert FE-only:** the backend already enforces every role×endpoint pair (`RbacCartesianTests`, green ×2). This plan tests the **FE affordance layer** (UX: don't show a button that 403s) + produces the manual; it is not the security boundary.

---

## Pre-flight: environment (every implementing session)

The e2e run needs the full stack running against `accounting_dev`. The Playwright config has **no `webServer`** — you start it yourself.

- `subst` drives: `subst U: <repo>` , `subst W: <repo>\backend` (recreate if missing).
- **API (:5080, Development):** from `W:\` →
  `$env:ASPNETCORE_ENVIRONMENT='Development'; $env:ASPNETCORE_URLS='http://localhost:5080'; dotnet run --project src\Accounting.Api` (background).
- **Frontend (:3000) — `next start`, NOT `next dev`:** Playwright targets the production build. Per CLAUDE.md §6, never `next build` while `next dev` is running. So: stop any `next dev` on :3000, `rm -rf frontend\.next`, `node node_modules\next\dist\bin\next build`, then `node node_modules\next\dist\bin\next start` (background).
- New seed SQL is applied by `DbInitializer` on **API startup** (apply-once, tracked in `sys.applied_sql_scripts`) — so restart the API after adding `550_*.sql`.
- Run e2e from `frontend\`: `node node_modules\@playwright\test\dist\cli.js test e2e/rbac-ui-gating.spec.ts` (or `npx playwright test e2e/rbac-ui-gating.spec.ts`). Channel defaults to `msedge` (override `PW_CHANNEL`).

---

## File Structure

- **Create** `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/550_seed_rbac_e2e_users.sql`
  — one login user per role in the VAT reference company (id 1) and in the non-VAT company (id 3). Deterministic creds (password `Admin@1234`), idempotent.
- **Create** `frontend/e2e/helpers/rbac-manifest.ts`
  — the single source of truth: every nav item + every gated button as `{ feature, kind, route, locate, perm, vatOnly, alwaysVisible, superAdminOnly }`. Plus the role list + company list + username convention.
- **Modify** `frontend/e2e/_helpers.ts`
  — add `loginAs(page, username)` (parameterised password already fixed) is present; add `mePermissions(page)` + `systemVatMode(page)` helpers that read the live grants via the BFF.
- **Modify** the gated pages to add stable `data-testid`s to the create links that lack them (8 list pages): `customers`, `vendors`, `tax-invoices`, `receipts`, `payment-vouchers`, `purchase-orders`, `invoices`, `quotations`. (Detail-page buttons mostly already have test-ids: `ti-post-action`, `pv-create-vi`, `po-approve/po-mark-sent/po-close/po-cancel/po-create-pv`, `cp-soft-save`, `cp-logo-upload`; add the few missing.)
- **Create** `frontend/e2e/rbac-ui-gating.spec.ts`
  — the Cartesian FE test: per company × per role, assert every manifest control + capture screenshots + append to a results array; in `test.afterAll`, write `frontend/e2e/.artifacts/rbac-gating-results.json`.
- **Create** `frontend/e2e/screenshots/rbac/` (output dir, git-tracked) — `<company>-<role>-<page>.png`.
- **Create** `scripts/gen-rbac-manual.mjs` — reads the manifest + results JSON + screenshots dir → writes the combined manual.
- **Create** `docs/manual/rbac-ui-guide.md` — the combined, all-roles user manual (generated; do not hand-edit the matrix block).

---

## Task 1: Seed one login user per role in a VAT and a non-VAT company

**Files:**
- Create: `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/550_seed_rbac_e2e_users.sql`

- [ ] **Step 1: Write the seed**

Convention: username `rbac_<rolecode_lower>` in company 1 (VAT), `rbac_nv_<rolecode_lower>` in company 3 (non-VAT). `user_id` block 5100–5199 (VAT) / 5200–5299 (non-VAT) to avoid collisions. SUPER_ADMIN users get `is_super_admin = TRUE`; all others FALSE. Password `Admin@1234` via pgcrypto (NEVER a literal `$2a$` hash — runtime-gotchas §18). Assign each user to the matching **per-company** role (`role_id WHERE role_code = X AND company_id = <co>`). Idempotent.

```sql
-- 550_seed_rbac_e2e_users.sql — DEV/SMOKE ONLY (e2e RBAC UI gating).
-- One login per role in company 1 (VAT) + company 3 (non-VAT). Password 'Admin@1234'.
-- Mirrors 130/440 (crypt(... gen_salt('bf',12))). Idempotent. NB: no curly braces.

-- helper: insert a user + assign one per-company role -------------------------
DO $do$
DECLARE
    r RECORD;
    uid INT;
    -- (company_id, branch_id, uid_base, username_prefix)
    cfg RECORD;
BEGIN
    FOR cfg IN
        SELECT 1 AS company_id, 1 AS branch_id, 5100 AS uid_base, 'rbac_'    AS px
        UNION ALL
        SELECT 3, 3, 5200, 'rbac_nv_'
    LOOP
        FOR r IN
            SELECT role_code, ROW_NUMBER() OVER (ORDER BY role_code) AS rn
            FROM (SELECT DISTINCT role_code FROM sys.role_templates
                  UNION SELECT 'SUPER_ADMIN') t
        LOOP
            uid := cfg.uid_base + r.rn::INT;
            INSERT INTO sys.users (
                user_id, username, email, password_hash, full_name,
                is_super_admin, is_active, failed_login_count, must_change_password,
                created_at, updated_at, version)
            VALUES (
                uid,
                cfg.px || lower(r.role_code),
                cfg.px || lower(r.role_code) || '@e2e.local',
                crypt('Admin@1234', gen_salt('bf', 12)),
                'E2E ' || r.role_code,
                (r.role_code = 'SUPER_ADMIN'), TRUE, 0, FALSE,
                now(), now(), 0)
            ON CONFLICT (user_id) DO NOTHING;

            -- Assign to the per-company role of this company (SUPER_ADMIN = the global row).
            INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
            SELECT uid, ro.role_id, cfg.company_id, cfg.branch_id, DATE '2026-01-01'
            FROM sys.roles ro
            WHERE ro.role_code = r.role_code
              AND (ro.company_id = cfg.company_id
                   OR (r.role_code = 'SUPER_ADMIN' AND ro.company_id IS NULL))
            ON CONFLICT DO NOTHING;
        END LOOP;
    END LOOP;
END
$do$;
```

- [ ] **Step 2: Apply + verify**

Restart the API (DbInitializer applies 550). Then verify each login works and carries the right grants:

Run (PowerShell, with the stack up):
```powershell
$b='http://localhost:5080'
foreach ($u in @('rbac_company_admin','rbac_ar_clerk','rbac_tax_officer','rbac_nv_ar_clerk')) {
  $r = irm "$b/auth/login" -Method Post -ContentType application/json -Body (@{username=$u;password='Admin@1234'}|ConvertTo-Json)
  "$u -> $($r.access_token.Length) token chars"
}
```
Expected: each returns a token (non-empty). `rbac_company_admin` then `GET /me/permissions` (Bearer) lists `sys.role.manage` etc.; `rbac_tax_officer` lists `tax.pnd30.read` (post-530).

- [ ] **Step 3: Commit**

```bash
git add backend/src/Accounting.Infrastructure/Migrations/SqlScripts/550_seed_rbac_e2e_users.sql
git commit -m "test(rbac): seed one login per role in VAT (co1) + non-VAT (co3) companies"
```

---

## Task 2: Add stable `data-testid`s to the create links that lack them

**Files:**
- Modify: `frontend/app/(dashboard)/{customers,vendors,tax-invoices,receipts,payment-vouchers,purchase-orders,invoices,quotations}/page.tsx`

The 8 list "New" links sit inside `<PermissionGate scope="…">`. Give each a `data-testid="<entity>-create"` so the spec can locate it without relying on Thai label text.

- [ ] **Step 1: Add the test-id (repeat per file with the right entity name)**

Example (`tax-invoices/page.tsx`):
```tsx
<PermissionGate scope="sales.tax_invoice.create">
  <Link href="/tax-invoices/new" data-testid="tax-invoice-create" className="btn btn-primary btn-sm gap-1">
    <Plus className="h-4 w-4" aria-hidden /> {t('create')}
  </Link>
</PermissionGate>
```
test-ids to add: `customer-create`, `vendor-create`, `tax-invoice-create`, `receipt-create`, `payment-voucher-create`, `purchase-order-create`, `billing-note-create`, `quotation-create`.

- [ ] **Step 2: Verify FE still compiles**

Run (from `frontend\`): `node node_modules\typescript\bin\tsc --noEmit`
Expected: exit 0.

- [ ] **Step 3: Commit**

```bash
git add "frontend/app/(dashboard)"
git commit -m "test(rbac): add data-testid to gated create links for e2e selectors"
```

---

## Task 3: Build the RBAC manifest (single source of truth)

**Files:**
- Create: `frontend/e2e/helpers/rbac-manifest.ts`

This file enumerates EVERY gated control. Keep it in sync with `SidebarNav.tsx` (nav `perm`) and the `PermissionGate scope=` I added on pages. Cross-check against `docs/rbac/endpoint-permission-map.generated.md` so no gated action is missed.

- [ ] **Step 1: Write the manifest**

```ts
// Single source of truth for the FE permission-gating e2e + manual generator.
// kind: 'nav' (sidebar link, located by href) | 'button' (action, located by data-testid).
// perm: required permission (super-admin always passes). vatOnly: hidden on non-VAT companies.
// alwaysVisible: rendered for any authenticated user (no gate). superAdminOnly: is_super_admin only.
export type Control = {
  feature: string;            // human label for the manual
  kind: 'nav' | 'button';
  route: string;              // page to visit (the detail-button routes use a seeded id, see spec)
  locate: { href?: string; testId?: string };
  perm?: string;
  vatOnly?: boolean;
  alwaysVisible?: boolean;
  superAdminOnly?: boolean;
  detail?: boolean;           // true = button lives on a detail page (needs a seeded doc id)
};

export const ROLES = [
  'SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AR_CLERK','AP_CLERK',
  'SALES_STAFF','PURCHASING_STAFF','WAREHOUSE_STAFF','APPROVER','AUDITOR','TAX_OFFICER',
] as const;

export const COMPANIES = [
  { key: 'vat',    companyId: 1, vatMode: true,  userPrefix: 'rbac_' },
  { key: 'nonvat', companyId: 3, vatMode: false, userPrefix: 'rbac_nv_' },
] as const;

export const usernameFor = (prefix: string, role: string) => prefix + role.toLowerCase();

// ── NAV (mirror SidebarNav.tsx) ───────────────────────────────────────────────
export const NAV: Control[] = [
  { feature: 'Dashboard',            kind: 'nav', route: '/',                 locate: { href: '/' }, alwaysVisible: true },
  { feature: 'Customers',            kind: 'nav', route: '/',                 locate: { href: '/customers' }, perm: 'master.customer.read' },
  { feature: 'Quotations',           kind: 'nav', route: '/',                 locate: { href: '/quotations' }, perm: 'sales.quotation.manage' },
  { feature: 'Sales orders',         kind: 'nav', route: '/',                 locate: { href: '/sales-orders' }, perm: 'sales.sales_order.manage' },
  { feature: 'Delivery orders',      kind: 'nav', route: '/',                 locate: { href: '/delivery-orders' }, perm: 'sales.delivery_order.manage' },
  { feature: 'Invoices (billing)',   kind: 'nav', route: '/',                 locate: { href: '/invoices' }, perm: 'sales.billing_note.read' },
  { feature: 'Tax invoices',         kind: 'nav', route: '/',                 locate: { href: '/tax-invoices' }, perm: 'sales.tax_invoice.read', vatOnly: true },
  { feature: 'Receipts',             kind: 'nav', route: '/',                 locate: { href: '/receipts' }, perm: 'sales.receipt.read' },
  { feature: 'Credit notes',         kind: 'nav', route: '/',                 locate: { href: '/credit-notes' }, perm: 'sales.credit_note.read', vatOnly: true },
  { feature: 'Debit notes',          kind: 'nav', route: '/',                 locate: { href: '/debit-notes' }, perm: 'sales.debit_note.read', vatOnly: true },
  { feature: 'Number gaps',          kind: 'nav', route: '/',                 locate: { href: '/number-gaps' }, perm: 'report.audit.read' },
  { feature: 'Vendors',              kind: 'nav', route: '/',                 locate: { href: '/vendors' }, perm: 'master.vendor.manage' },
  { feature: 'Purchase orders',      kind: 'nav', route: '/',                 locate: { href: '/purchase-orders' }, perm: 'purchase.purchase_order.read' },
  { feature: 'Payment vouchers',     kind: 'nav', route: '/',                 locate: { href: '/payment-vouchers' }, perm: 'purchase.payment_voucher.read' },
  { feature: 'Vendor invoices',      kind: 'nav', route: '/',                 locate: { href: '/vendor-invoices' }, perm: 'purchase.vendor_invoice.read' },
  { feature: 'WHT certificates',     kind: 'nav', route: '/',                 locate: { href: '/wht-certificates' }, perm: 'purchase.wht.read' },
  { feature: 'Payroll',              kind: 'nav', route: '/',                 locate: { href: '/payroll' }, perm: 'payroll.run.manage' },
  { feature: 'Tax summary',          kind: 'nav', route: '/',                 locate: { href: '/reports/tax-summary' }, perm: 'report.profit_loss.read' },
  { feature: 'Trial balance',        kind: 'nav', route: '/',                 locate: { href: '/reports/trial-balance' }, perm: 'report.trial_balance.read' },
  { feature: 'Profit & loss',        kind: 'nav', route: '/',                 locate: { href: '/reports/profit-loss' }, perm: 'report.profit_loss.read' },
  { feature: 'Sales summary',        kind: 'nav', route: '/',                 locate: { href: '/reports/sales-summary' }, perm: 'report.profit_loss.read' },
  { feature: 'ภ.พ.30',               kind: 'nav', route: '/',                 locate: { href: '/reports/pnd30' }, perm: 'tax.pnd30.read', vatOnly: true },
  { feature: 'Outstanding PO',       kind: 'nav', route: '/',                 locate: { href: '/reports/outstanding-po' }, perm: 'purchase.purchase_order.read' },
  { feature: 'AP aging',             kind: 'nav', route: '/',                 locate: { href: '/reports/ap-aging' }, perm: 'purchase.purchase_order.read' },
  { feature: 'Tax filings',          kind: 'nav', route: '/',                 locate: { href: '/tax-filings' }, perm: 'tax.filing.read' },
  { feature: 'Documents',            kind: 'nav', route: '/',                 locate: { href: '/documents' }, alwaysVisible: true },
  { feature: 'Missing WHT cert',     kind: 'nav', route: '/',                 locate: { href: '/tax-filings/missing-wht-cert' }, perm: 'tax.pnd53.read' },
  { feature: 'WHT receivable',       kind: 'nav', route: '/',                 locate: { href: '/reports/wht-receivable' }, perm: 'tax.pnd53.read' },
  { feature: 'Company profile (own)',kind: 'nav', route: '/',                 locate: { href: '/settings/company' }, alwaysVisible: true },
  { feature: 'Companies (tax cfg)',  kind: 'nav', route: '/',                 locate: { href: '/settings/companies' }, superAdminOnly: true },
  { feature: 'Roles admin',          kind: 'nav', route: '/',                 locate: { href: '/settings/roles' }, perm: 'sys.role.manage' },
  { feature: 'Users admin',          kind: 'nav', route: '/',                 locate: { href: '/settings/users' }, perm: 'sys.user.manage' },
  { feature: 'Products',             kind: 'nav', route: '/',                 locate: { href: '/settings/products' }, perm: 'master.product.manage' },
  { feature: 'Business units',       kind: 'nav', route: '/',                 locate: { href: '/settings/business-units' }, perm: 'master.business_unit.manage' },
  { feature: 'Employees',            kind: 'nav', route: '/',                 locate: { href: '/settings/employees' }, perm: 'master.employee.manage' },
  { feature: 'WHT types',            kind: 'nav', route: '/',                 locate: { href: '/settings/wht-types' }, perm: 'tax.wht_type.manage' },
  { feature: 'Expense categories',   kind: 'nav', route: '/',                 locate: { href: '/settings/expense-categories' }, perm: 'sys.expense_category.manage' },
  { feature: 'API keys',             kind: 'nav', route: '/',                 locate: { href: '/settings/api-keys' }, perm: 'sys.api_key.manage' },
];

// ── LIST CREATE BUTTONS (gated; located by data-testid from Task 2) ───────────
export const CREATE_BUTTONS: Control[] = [
  { feature: 'Create customer',        kind: 'button', route: '/customers',        locate: { testId: 'customer-create' },        perm: 'master.customer.manage' },
  { feature: 'Create vendor',          kind: 'button', route: '/vendors',          locate: { testId: 'vendor-create' },          perm: 'master.vendor.manage' },
  { feature: 'Create tax invoice',     kind: 'button', route: '/tax-invoices',     locate: { testId: 'tax-invoice-create' },     perm: 'sales.tax_invoice.create', vatOnly: true },
  { feature: 'Create receipt',         kind: 'button', route: '/receipts',         locate: { testId: 'receipt-create' },         perm: 'sales.receipt.create' },
  { feature: 'Create payment voucher', kind: 'button', route: '/payment-vouchers', locate: { testId: 'payment-voucher-create' }, perm: 'purchase.payment_voucher.create' },
  { feature: 'Create purchase order',  kind: 'button', route: '/purchase-orders',  locate: { testId: 'purchase-order-create' },  perm: 'purchase.purchase_order.create' },
  { feature: 'Create invoice',         kind: 'button', route: '/invoices',         locate: { testId: 'billing-note-create' },    perm: 'sales.billing_note.manage' },
  { feature: 'Create quotation',       kind: 'button', route: '/quotations',       locate: { testId: 'quotation-create' },       perm: 'sales.quotation.manage' },
];

// ── SETTINGS/COMPANY controls (own-company profile vs §4.6 tax card) ──────────
export const SETTINGS_COMPANY_BUTTONS: Control[] = [
  { feature: 'Edit registered address', kind: 'button', route: '/settings/company', locate: { testId: 'cp-edit-address' }, perm: 'master.company_profile.manage' },
  { feature: 'Upload logo',             kind: 'button', route: '/settings/company', locate: { testId: 'cp-logo-upload' },  perm: 'master.company_profile.manage' },
  { feature: 'Save company profile',    kind: 'button', route: '/settings/company', locate: { testId: 'cp-soft-save' },    perm: 'master.company_profile.manage' },
  { feature: 'Paid-up capital (CIT)',   kind: 'button', route: '/settings/company', locate: { testId: 'cp-paidup-card' },  perm: 'master.company.manage' },
];

// Detail-page lifecycle buttons need a seeded document in the right STATUS; the spec
// seeds those per company in a beforeAll (see DETAIL_FIXTURES in the spec) and fills `route`.
```

> Note: `cp-edit-address` and `cp-paidup-card` test-ids may not exist yet — add them in Task 2's file set (the address edit `<button onClick={openAddr}>` and a wrapper on `<PaidUpCapitalCard>`), alongside the create-link test-ids.

- [ ] **Step 2: Commit**

```bash
git add frontend/e2e/helpers/rbac-manifest.ts
git commit -m "test(rbac): add FE control->permission manifest (nav + buttons)"
```

---

## Task 4: Add `mePermissions` + `vatMode` helpers

**Files:**
- Modify: `frontend/e2e/_helpers.ts`

- [ ] **Step 1: Add helpers**

```ts
// Live grants of the currently logged-in user (via the BFF proxy, cookie auth).
export async function mePermissions(page: Page): Promise<{ isSuperAdmin: boolean; permissions: string[] }> {
  const r = await page.request.get('/api/proxy/me/permissions');
  if (!r.ok()) throw new Error(`/me/permissions ${r.status()}`);
  return r.json();
}

export async function vatMode(page: Page): Promise<boolean> {
  const r = await page.request.get('/api/proxy/system/info');
  return r.ok() ? Boolean((await r.json()).vat_mode ?? (await r.json()).vatMode) : true;
}
```

- [ ] **Step 2: Commit**

```bash
git add frontend/e2e/_helpers.ts
git commit -m "test(rbac): add mePermissions + vatMode e2e helpers"
```

---

## Task 5: The Cartesian FE gating spec (assert + screenshot + emit results)

**Files:**
- Create: `frontend/e2e/rbac-ui-gating.spec.ts`
- Create (output): `frontend/e2e/screenshots/rbac/` , `frontend/e2e/.artifacts/`

**Expected-visibility rule (the heart):**
`expected = me.isSuperAdmin || (control.alwaysVisible) || (control.superAdminOnly ? me.isSuperAdmin : me.permissions.includes(control.perm))`, then **AND** `(!control.vatOnly || company.vatMode)`. Assert the DOM presence of the located element equals `expected`.

- [ ] **Step 1: Write the spec**

```ts
import { test, expect, type Page } from '@playwright/test';
import { login, logout, mePermissions } from './_helpers';
import { NAV, CREATE_BUTTONS, SETTINGS_COMPANY_BUTTONS, ROLES, COMPANIES, usernameFor, type Control } from './helpers/rbac-manifest';
import { mkdirSync, writeFileSync } from 'node:fs';

type Row = { company: string; role: string; feature: string; perm: string; expected: boolean; actual: boolean; pass: boolean };
const results: Row[] = [];

function expectedVisible(c: Control, me: { isSuperAdmin: boolean; permissions: string[] }, vatMode: boolean): boolean {
  let vis: boolean;
  if (me.isSuperAdmin) vis = true;
  else if (c.alwaysVisible) vis = true;
  else if (c.superAdminOnly) vis = false;
  else vis = !!c.perm && me.permissions.includes(c.perm);
  if (c.vatOnly && !vatMode) vis = false;
  return vis;
}

async function presence(page: Page, c: Control): Promise<boolean> {
  const loc = c.locate.href
    ? page.locator(`a[href="${c.locate.href}"]`)
    : page.getByTestId(c.locate.testId!);
  // settle: wait a beat for client gates (useMePermissions) to resolve
  await page.waitForTimeout(250);
  return (await loc.count()) > 0 && await loc.first().isVisible().catch(() => false);
}

for (const company of COMPANIES) {
  test.describe(`RBAC UI gating — ${company.key} company (id ${company.companyId})`, () => {
    for (const role of ROLES) {
      test(`${role}`, async ({ page }) => {
        const user = usernameFor(company.userPrefix, role);
        await login(page, user);
        const me = await mePermissions(page);

        // NAV + list-create buttons (+ settings/company buttons).
        const navControls = NAV;
        const pageControls = [...CREATE_BUTTONS, ...SETTINGS_COMPANY_BUTTONS];

        // NAV is on every page; check from the dashboard.
        await page.goto('/');
        for (const c of navControls) {
          const actual = await presence(page, c);
          const expected = expectedVisible(c, me, company.vatMode);
          results.push({ company: company.key, role, feature: c.feature, perm: c.perm ?? '(none)', expected, actual, pass: actual === expected });
          expect.soft(actual, `${company.key}/${role} nav "${c.feature}"`).toBe(expected);
        }

        // Page buttons: visit each route once, check its controls.
        const byRoute = new Map<string, Control[]>();
        for (const c of pageControls) byRoute.set(c.route, [...(byRoute.get(c.route) ?? []), c]);
        for (const [route, controls] of byRoute) {
          await page.goto(route);
          for (const c of controls) {
            const actual = await presence(page, c);
            const expected = expectedVisible(c, me, company.vatMode);
            results.push({ company: company.key, role, feature: c.feature, perm: c.perm ?? '(none)', expected, actual, pass: actual === expected });
            expect.soft(actual, `${company.key}/${role} btn "${c.feature}" on ${route}`).toBe(expected);
          }
        }

        // Visual: capture the dashboard (nav) + settings/company per role.
        mkdirSync('e2e/screenshots/rbac', { recursive: true });
        await page.goto('/');
        await page.waitForTimeout(400);
        await page.screenshot({ path: `e2e/screenshots/rbac/${company.key}-${role}-nav.png`, fullPage: true });

        await logout(page);
      });
    }
  });
}

test.afterAll(() => {
  mkdirSync('e2e/.artifacts', { recursive: true });
  writeFileSync('e2e/.artifacts/rbac-gating-results.json', JSON.stringify(results, null, 2));
  const fails = results.filter((r) => !r.pass);
  if (fails.length) console.log(`RBAC gating mismatches: ${fails.length}\n` + fails.map((f) => `  ${f.company}/${f.role} ${f.feature}: expected ${f.expected} got ${f.actual}`).join('\n'));
});
```

> Detail-page lifecycle buttons (PV approve/post, TI post, PO approve/cancel, VI post, payroll approve/post/pay) require a seeded document in the correct STATUS per company. Add a `DETAIL_FIXTURES` block: in a `beforeAll`, log in as `rbac_<co>_company_admin`, create (via `/api/proxy`) a Draft PV / Draft TI / Draft+Approved PO / Draft VI / Draft payroll run, record their ids, and append detail `Control`s with `route: '/payment-vouchers/<id>'` etc. + `detail: true`. Keep this additive — if a fixture can't be created on the non-VAT company (e.g. TI), skip those controls there (they're `vatOnly`). **This is the one place to extend after the nav/create matrix is green.**

- [ ] **Step 2: Run it (stack must be up — see Pre-flight)**

Run (from `frontend\`): `node node_modules\@playwright\test\dist\cli.js test e2e/rbac-ui-gating.spec.ts`
Expected: 24 tests (12 roles × 2 companies). All pass; `e2e/.artifacts/rbac-gating-results.json` written; screenshots under `e2e/screenshots/rbac/`.

- [ ] **Step 3: Triage mismatches**

Any `expect.soft` failure means FE gate ≠ live grant. Two legitimate causes:
  1. **A real FE gap** (a gated-endpoint button not wrapped, or wrong scope) → fix the page's `PermissionGate`.
  2. **A manifest error** (wrong perm/route) → fix `rbac-manifest.ts`.
Re-run until green. Do NOT "fix" by loosening the assertion.

- [ ] **Step 4: Commit**

```bash
git add frontend/e2e/rbac-ui-gating.spec.ts frontend/e2e/screenshots/rbac
git commit -m "test(rbac): Cartesian FE permission-gating e2e (all roles x VAT/non-VAT) + screenshots"
```

---

## Task 6: Generate the combined user manual

**Files:**
- Create: `scripts/gen-rbac-manual.mjs`
- Create: `docs/manual/rbac-ui-guide.md`

The manual is **one combined doc** (not per role): a master matrix (feature rows × role columns, ✓/✗) computed from the manifest + each role's expected visibility, with VAT vs non-VAT columns where they differ, plus an embedded screenshot gallery and a plain-language "what each role can do" summary.

- [ ] **Step 1: Write the generator**

```js
// node scripts/gen-rbac-manual.mjs  — reads the e2e results JSON + screenshots, writes the manual.
import { readFileSync, writeFileSync, existsSync, readdirSync } from 'node:fs';

const results = JSON.parse(readFileSync('frontend/e2e/.artifacts/rbac-gating-results.json', 'utf8'));
const roles = [...new Set(results.map((r) => r.role))];
const features = [...new Set(results.map((r) => r.feature))];
const cell = (company, role, feature) => {
  const row = results.find((r) => r.company === company && r.role === role && r.feature === feature);
  return row ? (row.expected ? '✓' : '') : '–';
};
const matrix = (company, title) => {
  let s = `\n### ${title}\n\n| Feature | ${roles.join(' | ')} |\n|---|${roles.map(() => '---').join('|')}|\n`;
  for (const f of features) s += `| ${f} | ${roles.map((r) => cell(company, r, f)).join(' | ')} |\n`;
  return s;
};
const shots = existsSync('frontend/e2e/screenshots/rbac')
  ? readdirSync('frontend/e2e/screenshots/rbac').filter((f) => f.endsWith('.png')) : [];

let md = `# TEAS — RBAC UI Guide (who sees what)\n\n`;
md += `> GENERATED by \`scripts/gen-rbac-manual.mjs\` from the \`rbac-ui-gating\` e2e run. Do not hand-edit the matrices.\n`;
md += `> ✓ = the control is shown to that role · blank = hidden · SUPER_ADMIN sees everything (bypass). VAT-only features are hidden on a non-VAT company.\n\n`;
md += `## How gating works\n\nThe sidebar and each action button are shown only when the signed-in user's role holds the matching permission (super-admins bypass). This is a UX aid — the backend enforces the same rule on every request (\`RbacCartesianTests\`). A non-VAT company hides ใบกำกับภาษี / ใบลดหนี้-เพิ่มหนี้ / ภ.พ.30.\n`;
md += `\n## Visibility matrix — VAT company\n${matrix('vat', 'All features × roles (VAT)')}`;
md += `\n## Visibility matrix — non-VAT company\n${matrix('nonvat', 'All features × roles (non-VAT)')}`;
md += `\n## Screenshots (sidebar per role)\n\n`;
for (const s of shots) md += `### ${s.replace('.png', '')}\n\n![${s}](../../frontend/e2e/screenshots/rbac/${s})\n\n`;
writeFileSync('docs/manual/rbac-ui-guide.md', md);
console.log(`wrote docs/manual/rbac-ui-guide.md (${features.length} features × ${roles.length} roles)`);
```

- [ ] **Step 2: Generate + sanity-check**

Run (from repo root): `node scripts/gen-rbac-manual.mjs`
Expected: `docs/manual/rbac-ui-guide.md` written; open it — VAT matrix shows e.g. `Tax invoices` ✓ for AR_CLERK/ACCOUNTANT/CHIEF/COMPANY_ADMIN/SUPER_ADMIN, blank for TAX_OFFICER/WAREHOUSE_STAFF; non-VAT matrix has `Tax invoices`/`ภ.พ.30` blank for everyone.

- [ ] **Step 3: Commit**

```bash
git add scripts/gen-rbac-manual.mjs docs/manual/rbac-ui-guide.md
git commit -m "docs(rbac): combined RBAC UI guide generated from the e2e gating run"
```

---

## Task 7: Final gate + tracking

- [ ] **Step 1: Full e2e gate (this spec twice — determinism)**

Run twice (stack up): `node node_modules\@playwright\test\dist\cli.js test e2e/rbac-ui-gating.spec.ts`
Expected: 24/24 pass both runs, 0 mismatches in the results JSON.

- [ ] **Step 2: Regenerate the manual + confirm it matches**

Run: `node scripts/gen-rbac-manual.mjs` ; confirm matrices unchanged from Task 6 (deterministic).

- [ ] **Step 3: Record + commit**

Prepend a `progress.md` entry (date, what shipped, 24/24 ×2, screenshot count, manual path) and tick this plan in `plan.md`. Commit `progress.md` + `plan.md`.

---

## 2. Compliance / coverage notes
- The e2e is **UX-grade**; the security boundary is `RbacCartesianTests` (BE). State this in the manual so no one mistakes a hidden button for enforcement.
- "ทุกปุ่ม" coverage = the manifest. Completeness is guarded by cross-checking the manifest against `docs/rbac/endpoint-permission-map.generated.md`: any gated endpoint whose only UI trigger is an ungated button is a finding (gate it or list it as BE-enforced-only with rationale). Controls intentionally NOT gated (settings/* create — page is nav-gated by the same manage perm; `/new` in-form Save/Post — reached via a gated entry + BE-enforced) are listed in the manual's "BE-enforced only" appendix so the coverage story is explicit.
- VAT/non-VAT: every role is tested on BOTH companies; `vatOnly` controls (ใบกำกับภาษี / CN / DN / ภ.พ.30) must be hidden on company 3 for ALL roles including SUPER_ADMIN.

## 3. Risks / mitigations
- **Client gates resolve async** (`useMePermissions` query) → `presence()` waits 250ms + uses `isVisible`; if flaky, await a known-stable post-load marker before checking. `expect.soft` collects all mismatches per role instead of bailing on the first.
- **Seed users persist in `accounting_dev`** (and teas_test) — harmless dev cruft; idempotent. They are `rbac_*` so easy to spot/clean.
- **Detail-fixture creation** (Task 5 note) is the fiddly part (status-dependent docs); land the nav + create + settings matrix green FIRST (Tasks 1–6), then extend to detail lifecycle buttons.
- **next start vs next dev** — must rebuild before e2e; never run `next build` with `next dev` live (§6).
- Screenshots add repo weight (24 nav PNGs). Acceptable; if too heavy, gitignore the gallery and keep only the matrices.

## 4. Open choices (pick before/while executing)
- **Screenshot depth:** default = 1 per role (sidebar/dashboard, 24 total) + the text matrix covers every control. Alternative = also screenshot each list/detail page per role (~hundreds) — heavier manual. Recommend the default; expand only if Ham wants visual proof per page.
- **Detail lifecycle buttons:** include in v1 (Task 5 note) or defer to a follow-up? Recommend include for PV + payroll (SoD-critical) at minimum.

## 5. Estimate
~1 session. Tasks 1–4 (~1.5h: seed + manifest + helpers + test-ids), Task 5 (~2h: spec + triage), Task 6 (~0.5h: generator), detail-fixtures (~1h if included), gate+docs (~0.5h).

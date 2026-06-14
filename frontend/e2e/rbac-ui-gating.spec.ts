import { test, expect, type Page } from '@playwright/test';
import { login, mePermissions, waitForNavGates } from './_helpers';
import {
  NAV, CREATE_BUTTONS, SETTINGS_COMPANY_BUTTONS,
  ROLES, COMPANIES, usernameFor, type Control,
} from './helpers/rbac-manifest';
import { seedDetailFixtures, detailControls } from './helpers/rbac-detail-fixtures';
import { mkdirSync, writeFileSync } from 'node:fs';

// FE-only UX-gating proof (the security boundary is the backend RbacCartesianTests).
// Per company × per role: log in the seeded user, read their live grants, and assert
// each manifest control's DOM presence equals the expected visibility rule.
type Row = {
  company: string; role: string; kind: string; feature: string; route: string;
  perm: string; expected: boolean; actual: boolean; pass: boolean;
};
const results: Row[] = [];

// Detail-page lifecycle controls are seeded per company in beforeAll (VAT company
// only — see rbac-detail-fixtures). Empty for the non-VAT company.
const detailByCompany: Record<string, Control[]> = { vat: [], nonvat: [] };

function expectedVisible(
  c: Control, me: { isSuperAdmin: boolean; permissions: string[] }, vatMode: boolean,
): boolean {
  let vis: boolean;
  if (me.isSuperAdmin) vis = true;
  else if (c.alwaysVisible) vis = true;
  else if (c.superAdminOnly) vis = false;
  else vis = !!c.perm && me.permissions.includes(c.perm);
  // Detail buttons also need the page-load READ perm (a role with the action perm
  // but not read 403s on the doc GET → the page renders no buttons at all).
  if (!me.isSuperAdmin && c.readPerm) vis = vis && me.permissions.includes(c.readPerm);
  if (c.vatOnly && !vatMode) vis = false;
  return vis;
}

function locator(page: Page, c: Control) {
  // Nav links are located INSIDE the sidebar only — the dashboard body also links
  // to some routes (e.g. the MascotGreeting CTA → /reports/sales-summary), which
  // would otherwise satisfy a bare a[href] match regardless of the gate.
  return c.locate.href
    ? page.getByTestId('app-sidebar').locator(`a[href="${c.locate.href}"]`)
    : page.getByTestId(c.locate.testId!);
}

// Returns DOM presence of the control. When it is EXPECTED visible, wait for it —
// detail pages render their action bar only after the doc GET resolves, so an
// immediate check would race the load. When expected hidden, a PermissionGate
// returns null synchronously (gates already settled via nav-gates-ready), so no
// wait is needed; checking immediately also catches a wrongly-shown control.
async function checkControl(page: Page, c: Control, expected: boolean): Promise<boolean> {
  const loc = locator(page, c);
  if (expected) {
    await loc.first().waitFor({ state: 'visible', timeout: c.detail ? 8000 : 3000 }).catch(() => {});
  }
  if ((await loc.count()) === 0) return false;
  return loc.first().isVisible().catch(() => false);
}

for (const company of COMPANIES) {
  test.describe(`RBAC UI gating — ${company.key} company (id ${company.companyId})`, () => {
    test.beforeAll(async ({ browser }) => {
      if (!company.vatMode) return; // detail fixtures: VAT reference company only.
      test.setTimeout(120_000);
      const ctx = await browser.newContext();
      const page = await ctx.newPage();
      try {
        const ids = await seedDetailFixtures(page, company.userPrefix);
        detailByCompany[company.key] = detailControls(ids);
      } finally {
        await ctx.close();
      }
    });

    for (const role of ROLES) {
      test(`${role}`, async ({ page }) => {
        test.setTimeout(90_000); // ~12 navigations × (goto + gate settle) per role.
        const user = usernameFor(company.userPrefix, role);
        await login(page, user);
        const me = await mePermissions(page);

        // NAV is in the shell on every page — check it once from the dashboard.
        await page.goto('/');
        await waitForNavGates(page);
        for (const c of NAV) {
          const expected = expectedVisible(c, me, company.vatMode);
          const actual = await checkControl(page, c, expected);
          results.push({ company: company.key, role, kind: c.kind, feature: c.feature, route: c.route, perm: c.perm ?? '(none)', expected, actual, pass: actual === expected });
          expect.soft(actual, `${company.key}/${role} nav "${c.feature}"`).toBe(expected);
        }

        // Page buttons (create + settings + detail-lifecycle): visit each route once.
        const pageControls = [...CREATE_BUTTONS, ...SETTINGS_COMPANY_BUTTONS, ...(detailByCompany[company.key] ?? [])];
        const byRoute = new Map<string, Control[]>();
        for (const c of pageControls) byRoute.set(c.route, [...(byRoute.get(c.route) ?? []), c]);
        for (const [route, controls] of byRoute) {
          await page.goto(route);
          await waitForNavGates(page);
          for (const c of controls) {
            const expected = expectedVisible(c, me, company.vatMode);
            const actual = await checkControl(page, c, expected);
            results.push({ company: company.key, role, kind: c.kind, feature: c.feature, route, perm: c.perm ?? '(none)', expected, actual, pass: actual === expected });
            expect.soft(actual, `${company.key}/${role} btn "${c.feature}" on ${route}`).toBe(expected);
          }
        }

        // Visual: capture the dashboard sidebar for the manual gallery.
        mkdirSync('e2e/screenshots/rbac', { recursive: true });
        await page.goto('/');
        await waitForNavGates(page);
        await page.screenshot({ path: `e2e/screenshots/rbac/${company.key}-${role}-nav.png`, fullPage: true });
      });
    }
  });
}

test.afterAll(() => {
  mkdirSync('e2e/.artifacts', { recursive: true });
  writeFileSync('e2e/.artifacts/rbac-gating-results.json', JSON.stringify(results, null, 2));
  const fails = results.filter((r) => !r.pass);
  if (fails.length) {
    console.log(
      `RBAC gating mismatches: ${fails.length}\n` +
      fails.map((f) => `  ${f.company}/${f.role} ${f.feature} (${f.perm}) on ${f.route}: expected ${f.expected} got ${f.actual}`).join('\n'),
    );
  } else {
    console.log(`RBAC gating: ${results.length} checks, 0 mismatches.`);
  }
});

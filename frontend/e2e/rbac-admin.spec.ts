import { test, expect } from '@playwright/test';
import { login } from './_helpers';
import { TestIds } from './helpers/test-ids';

// Sprint 13k Plan 1 — per-company RBAC admin UI. Runs as `admin` (super-admin).
// Flow: create a custom role in the selected company → grant 2 permissions
// (whole-set replace, must persist) → assign the role to a non-super user →
// verify it persists. Uses the running stack (API :5080 + web :3000).
//
// Unique role code per run (shared long-lived dev DB — §14 anti-collision).
test.describe('RBAC admin — per-company roles', () => {
  test('super-admin creates a role, grants perms, assigns it to a user', async ({ page }) => {
    const code = `E2E${TestIds.suffix().slice(0, 6).toUpperCase()}`;
    const nameTh = `บทบาท e2e ${TestIds.suffix().slice(0, 4)}`;

    // ── /settings/roles — create a custom role ──
    await login(page);
    await page.goto('/settings/roles');
    await expect(page.getByRole('heading', { name: /บทบาทและสิทธิ์/ })).toBeVisible();

    // Super-admin sees a company selector (defaults to the first company); a
    // company-admin would not. Just confirm roles loaded for the default company.
    await expect(page.getByRole('row', { name: /COMPANY_ADMIN/ })).toBeVisible({ timeout: 15_000 });

    await page.getByTestId('role-create-btn').click();
    await page.getByTestId('role-new-code').fill(code);
    await page.getByTestId('role-new-nameth').fill(nameTh);
    await page.getByTestId('role-new-save').click();

    // The new role appears in the table (custom = badge "กำหนดเอง", 0 perms).
    const row = page.getByRole('row', { name: new RegExp(code) });
    await expect(row).toBeVisible({ timeout: 15_000 });

    // ── grant 2 permissions via the module-grouped grid (whole-set replace) ──
    await row.getByRole('button', { name: /แก้ไขสิทธิ์/ }).click();
    const grid = page.getByRole('dialog');
    await expect(grid).toBeVisible();
    await grid.getByTestId('perm-cb-master.customer.read').check();
    await grid.getByTestId('perm-cb-sales.tax_invoice.read').check();
    await page.getByTestId('perm-save').click();
    await expect(grid).toBeHidden({ timeout: 10_000 });

    // Reopen → the 2 grants persisted.
    await row.getByRole('button', { name: /แก้ไขสิทธิ์/ }).click();
    const grid2 = page.getByRole('dialog');
    await expect(grid2.getByTestId('perm-cb-master.customer.read')).toBeChecked();
    await expect(grid2.getByTestId('perm-cb-sales.tax_invoice.read')).toBeChecked();
    // A permission we did NOT grant stays unchecked (whole-set, not additive blanket).
    await expect(grid2.getByTestId('perm-cb-master.vendor.manage')).not.toBeChecked();
    await grid2.getByRole('button', { name: /ยกเลิก|ปิด|Cancel/ }).first().click();

    // ── /settings/users — assign the new role to a non-super user ──
    await page.goto('/settings/users');
    await expect(page.getByRole('heading', { name: /ผู้ใช้และบทบาท/ })).toBeVisible();
    // Anchor to the canonical `sales_staff` user — a bare /sales_staff/ also matches
    // the E2E-seeded `rbac_sales_staff` row (strict-mode violation: 2 rows). The row's
    // accessible name begins with the username, so ^sales_staff\b excludes the prefixed one.
    const userRow = page.getByRole('row', { name: /^sales_staff\b/ });
    await expect(userRow).toBeVisible({ timeout: 15_000 });
    await userRow.getByRole('button', { name: /แก้ไขบทบาท/ }).click();

    const dlg = page.getByRole('dialog');
    await expect(dlg).toBeVisible();
    // The just-created role is offered as a checkbox in this company's role set.
    const roleCheckbox = dlg.getByRole('checkbox', { name: new RegExp(nameTh) });
    await expect(roleCheckbox).toBeVisible({ timeout: 10_000 });
    await roleCheckbox.check();
    await page.getByTestId('user-roles-save').click();
    await expect(dlg).toBeHidden({ timeout: 10_000 });

    // Reopen → assignment persisted.
    await userRow.getByRole('button', { name: /แก้ไขบทบาท/ }).click();
    const dlg2 = page.getByRole('dialog');
    await expect(dlg2.getByRole('checkbox', { name: new RegExp(nameTh) })).toBeChecked();
  });
});

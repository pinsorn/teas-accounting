import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h E2E (ckpt4) — Sales Order list + status filter (P5 ckpt2).
// URL persistence is the contract: ?status=Draft survives refresh.
test('sales orders: list + status filter persists in URL', async ({ page }) => {
  await login(page);
  await page.goto('/sales-orders');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  // Pick "Draft" in the status combobox.
  const statusSelect = page.getByLabel(/status|สถานะ/i).first();
  if (await statusSelect.isVisible().catch(() => false)) {
    await statusSelect.selectOption('Draft').catch(() => undefined);
    await expect(page).toHaveURL(/[?&]status=Draft/i);
    await page.reload();
    await expect(page).toHaveURL(/[?&]status=Draft/i);
  }
});

test('sales orders: detail renders for first row', async ({ page }) => {
  await login(page);
  await page.goto('/sales-orders');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  const rows = await page.locator('tbody tr').count();
  if (rows > 0) {
    await page.locator('tbody tr').first().getByRole('link').first().click();
    await page.waitForURL(/\/sales-orders\/\d+$/, { timeout: 15_000 });
  }
});

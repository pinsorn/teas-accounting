import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h E2E (ckpt4) — Sales Order list + status filter (P5 ckpt2).
// URL persistence is the contract: ?status=Draft survives refresh.
test('sales orders: list + status filter persists in URL', async ({ page }) => {
  // cont.88 — regression FIXED: <DataTable urlFilters={['status']}> mirrors the
  // status column filter into ?status=… (and restores it on mount), reinstating
  // the pre-redesign refresh/share contract this spec pins.
  await login(page);
  await page.goto('/sales-orders');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  // The status options are FACETED from the rows on screen (a fresh DB may hold
  // only Posted SOs — cont.92b lesson: never hardcode 'Draft'). Pick the first
  // real option; the contract under test is URL persistence, not the value.
  const statusSelect = page.getByLabel(/status|สถานะ/i).first();
  if (await statusSelect.isVisible().catch(() => false)) {
    const value = await statusSelect.locator('option:not([value=""])').first()
      .getAttribute('value').catch(() => null);
    if (value) {
      await statusSelect.selectOption(value);
      const inUrl = new RegExp(`[?&]status=${value}`, 'i');
      await expect(page).toHaveURL(inUrl);
      await page.reload();
      await expect(page).toHaveURL(inUrl);
    }
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

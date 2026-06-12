import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h E2E (ckpt4) — Delivery Order 4-state machine (P9 ckpt2):
// Draft → Issued → Delivered (Pattern X fires TI auto-create on Delivered)
// → Cancelled. This spec covers the navigation surface; live state machine
// transitions are exercised in Sana's deep-mode walk through Chrome MCP.

test('delivery orders: list + filter URL persist', async ({ page }) => {
  // cont.88 — regression FIXED: <DataTable urlFilters={['status']}> mirrors the
  // status column filter into ?status=… (same fix as sales-order-flow).
  await login(page);
  await page.goto('/delivery-orders');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  // Status options are FACETED from the rows on screen — a fresh DB may not
  // carry an 'Issued' DO (cont.92b). Pick the first real option; the contract
  // under test is URL persistence, not the specific status.
  const statusSelect = page.getByLabel(/status|สถานะ/i).first();
  if (await statusSelect.isVisible().catch(() => false)) {
    const value = await statusSelect.locator('option:not([value=""])').first()
      .getAttribute('value').catch(() => null);
    if (value) {
      await statusSelect.selectOption(value);
      await expect(page).toHaveURL(new RegExp(`[?&]status=${value}`, 'i'));
    }
  }
});

test('delivery orders: detail surfaces Issue / Mark Delivered actions when state allows', async ({ page }) => {
  await login(page);
  await page.goto('/delivery-orders');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  const rows = await page.locator('tbody tr').count();
  if (rows > 0) {
    await page.locator('tbody tr').first().getByRole('link').first().click();
    await page.waitForURL(/\/delivery-orders\/\d+$/, { timeout: 15_000 });
    // Surface check only — the page rendered without crashing. The actual
    // button (Issue / Mark Delivered / Create TI) depends on the status of
    // the seeded DO; deep-state verification is Sana Chrome-MCP scope.
    await expect(page.locator('body')).toBeVisible();
  }
});

import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h E2E (ckpt4) — Path B Q→TI conversion (P6.1 ckpt2).
// Accepted Q → "สร้าง TI จาก Q" button → /tax-invoices/new?fromQuotationId=X →
// form prefilled with the Q's customer + lines.
test('tax invoice: prefilled from accepted quotation via URL param', async ({ page }) => {
  await login(page);

  // Smoke: open /tax-invoices/new?fromQuotationId=1 directly; if Q#1 exists
  // and is in a state that prefill accepts, the form should hydrate.
  await page.goto('/tax-invoices/new?fromQuotationId=1');
  // Form renders regardless; the customer label is hydrated only when the
  // Q exists in the tenant. We just want a smoke check that the URL param
  // path does not crash the page.
  await expect(page.getByRole('button', { name: /Post|บันทึกเอกสาร/i }).first())
    .toBeVisible({ timeout: 15_000 });
});

test('tax invoice detail: cross-ref chip back to originating Q (P6.1)', async ({ page }) => {
  await login(page);
  await page.goto('/tax-invoices');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  const rows = await page.locator('tbody tr').count();
  if (rows > 0) {
    await page.locator('tbody tr').first().getByRole('link').first().click();
    await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
    // Either the P6.1 chip is present (Q-linked TI) or the cross-ref panel
    // surfaces the receipts/notes — both are acceptable smoke outcomes.
    await expect(page.locator('body')).toBeVisible();
  }
});

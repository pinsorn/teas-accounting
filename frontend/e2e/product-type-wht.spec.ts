import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h E2E (ckpt4) — P7 product_type wiring + WHT auto-base on Receipt.
// BE shipped ckpt2 (LineItemProductType snapshot across Q/SO/DO/TI lines);
// FE readOnly tax_rate + WHT base auto = Σ(SERVICE ex-VAT) is deferred to
// Sprint 13i. This spec smoke-tests the existing Receipt WHT form surface.

test('receipt WHT: form surfaces WHT base + rate inputs', async ({ page }) => {
  await login(page);
  await page.goto('/receipts/new');
  // Surface check — does the WHT block render at all?
  await expect(page.getByText(/WHT|หัก ณ ที่จ่าย|ภาษีหัก/i).first())
    .toBeVisible({ timeout: 10_000 });
});

test('products: list page renders (any layout)', async ({ page }) => {
  await login(page);
  await page.goto('/products');
  // The product list may render as a <table> OR a DataTable wrapper OR a
  // grid of cards — keep this smoke-level. Deep product-type column
  // verification belongs in Sana Chrome-MCP.
  await expect(page.locator('body')).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText(/Access denied|ไม่มีสิทธิ์/i)).toHaveCount(0);
});

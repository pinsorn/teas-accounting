import { test } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// Captures the screens Sana asked for in Report-Backend5 §5.4 (visual fidelity check).
// Output: frontend/screenshots/*.png. Not a behavioural test — pure capture.
const DIR = 'screenshots';

test('capture key screens', async ({ page }) => {
  // TI create/post + 4 page visits with fixed settle waits > 30s default.
  test.setTimeout(120_000);
  await login(page);
  await page.screenshot({ path: `${DIR}/01-dashboard.png`, fullPage: true });

  await page.goto('/tax-invoices/new');
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/02-tax-invoice-create.png`, fullPage: true });

  const tiId = await createAndPostTaxInvoice(page);
  await page.goto(`/credit-notes/new?fromTaxInvoiceId=${tiId}&reason=AmountError`);
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/03-credit-note-create.png`, fullPage: true });

  await page.goto('/number-gaps');
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/04-number-gaps.png`, fullPage: true });

  // Mobile breakpoint of the TI list.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/tax-invoices');
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/05-tax-invoices-mobile.png`, fullPage: true });
});

import { test } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// Captures the screens Sana asked for in Report-Backend5 §5.4 (visual fidelity check).
// Output: frontend/screenshots/*.png. Not a behavioural test — pure capture.
const DIR = 'screenshots';

test('capture key screens', async ({ page }) => {
  await login(page);
  await page.screenshot({ path: `${DIR}/01-dashboard.png`, fullPage: true });

  await page.goto('/tax-invoices/new');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/02-tax-invoice-create.png`, fullPage: true });

  const tiId = await createAndPostTaxInvoice(page);
  await page.goto(`/credit-notes/new?fromTaxInvoiceId=${tiId}&reason=AmountError`);
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/03-credit-note-create.png`, fullPage: true });

  await page.goto('/number-gaps');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/04-number-gaps.png`, fullPage: true });

  // Mobile breakpoint of the TI list.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/tax-invoices');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/05-tax-invoices-mobile.png`, fullPage: true });
});

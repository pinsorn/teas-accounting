import { test } from '@playwright/test';
import { login } from './_helpers';

// Sprint-5 visual-fidelity capture (Report-Backend6 §screenshots). Pure capture,
// not behavioural. Output: frontend/screenshots/s5-*.png.
const DIR = 'screenshots';

test('capture purchase-slice screens', async ({ page }) => {
  await login(page);

  await page.goto('/vendors');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/s5-01-vendors-list.png`, fullPage: true });

  await page.goto('/vendors/new');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/s5-02-vendor-create.png`, fullPage: true });

  await page.goto('/payment-vouchers');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/s5-03-payment-vouchers.png`, fullPage: true });

  await page.goto('/wht-certificates');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/s5-04-wht-certificates.png`, fullPage: true });

  // Mobile breakpoint of the new sidebar "ซื้อ" section.
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/vendors');
  await page.waitForLoadState('networkidle');
  await page.screenshot({ path: `${DIR}/s5-05-vendors-mobile.png`, fullPage: true });
});

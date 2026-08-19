import { test } from '@playwright/test';
import { login } from './_helpers';

// R2 Leg3 (throwaway) — anomaly screenshot for L3-9 (disposal date before acquisition date).
test('screenshot: asset F detail showing the date-order anomaly', async ({ page }) => {
  await login(page);
  await page.goto('/fixed-assets/5');
  await page.waitForLoadState('networkidle');
  await page.screenshot({
    path: 'Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/5667c374-e2c0-4998-b10c-b993b4182367/scratchpad/l3-9-date-order-anomaly.png',
    fullPage: true,
  });
});

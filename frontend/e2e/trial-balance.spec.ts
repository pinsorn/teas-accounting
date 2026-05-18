import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 9 A1 — the Trial Balance double-entry invariant (Σ Dr == Σ Cr) must
// ALWAYS hold; the page surfaces it as a success badge. Any imbalance is a GL
// bug and would flip the badge to badge-error / "ไม่สมดุล".
test('trial balance is always balanced (Dr == Cr)', async ({ page }) => {
  await login(page);
  await page.goto('/reports/trial-balance');

  const badge = page.getByTestId('tb-balanced');
  await expect(badge).toBeVisible({ timeout: 15_000 });
  await expect(badge).toContainText(/Dr = Cr/i);
  await expect(badge).toHaveClass(/badge-success/);
  await expect(badge).not.toContainText(/ไม่สมดุล|UNBALANCED/i);
});

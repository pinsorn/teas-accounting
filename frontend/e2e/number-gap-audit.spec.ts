import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// A correctly-numbered ledger (ON CONFLICT allocation) must show the clean state.
test('number gap audit renders the clean (no-gaps) state', async ({ page }) => {
  await login(page);
  await page.goto('/number-gaps');

  // success status with the "no gaps" message; the error paragraph must be absent.
  // (Don't assert getByRole('alert')==0 — the sonner Toaster always renders an alert
  //  live-region; unrelated to the page's audit state.)
  const ok = page.getByRole('status');
  await expect(ok).toBeVisible({ timeout: 15_000 });
  await expect(ok).toContainText(/ครบถ้วน|intact/i);
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

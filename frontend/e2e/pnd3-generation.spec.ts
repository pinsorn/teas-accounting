import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 9 C2 — ภ.ง.ด.3 generator (AP-side WHT, individual payees). Backend
// payee-type routing is covered by the Api integration tests; this asserts the
// generator endpoint + UI render the form for a period.
test('ภ.ง.ด.3 generator previews the WHT return', async ({ page }) => {
  await login(page);
  await page.goto('/tax-filings/pnd3');

  await page.getByRole('button', { name: /แสดงตัวอย่าง|preview/i }).first().click();

  const status = page.getByTestId('tf-status');
  await expect(status).toBeVisible({ timeout: 15_000 });
  await expect(status).toContainText(/Preview/i);
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

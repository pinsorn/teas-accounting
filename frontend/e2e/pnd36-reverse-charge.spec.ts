import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 9 C5 — ภ.พ.36 reverse-charge. The finalize auto-JV (Dr 1170 / Cr 2151,
// net 0) + balance is verified by the Api integration test; this asserts the
// page previews and discloses the JV posting behaviour to the accountant.
test('ภ.พ.36 reverse-charge previews and discloses the auto-JV', async ({ page }) => {
  await login(page);
  await page.goto('/tax-filings/pnd36');

  await page.getByRole('button', { name: /แสดงตัวอย่าง|preview/i }).first().click();

  const status = page.getByTestId('tf-status');
  await expect(status).toBeVisible({ timeout: 15_000 });
  await expect(status).toContainText(/Preview/i);
  // The Dr 1170 / Cr 2151 reverse-charge behaviour must be disclosed.
  const jvNote = page.getByTestId('pnd36-jv-note');
  await expect(jvNote).toBeVisible();
  await expect(jvNote).toContainText(/1170/);
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

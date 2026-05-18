import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 9 A2 (R-Q1a) — P&L is flat Revenue − Expense by BU this sprint. The
// GP/COGS deferral MUST be disclosed explicitly in the payload note (the spec:
// "Don't silently omit — tell consumers explicitly"). The page renders it.
test('profit & loss discloses the Phase-2 GP/COGS deferral note', async ({ page }) => {
  await login(page);
  await page.goto('/reports/profit-loss');

  const dates = page.locator('input[type="date"]');
  await dates.nth(0).fill('2026-01-01');
  await dates.nth(1).fill('2026-12-31');

  const note = page.getByTestId('pl-note');
  await expect(note).toBeVisible({ timeout: 15_000 });
  await expect(note).toContainText(/Phase 2/i);
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

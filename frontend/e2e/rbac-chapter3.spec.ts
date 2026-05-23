import { test, expect, type Page } from '@playwright/test';

async function loginAs(page: Page, username: string, password: string) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill(username);
  await page.locator('input[type="password"]').fill(password);
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 13h E2E (ckpt4) — Chapter 3 RBAC: demo-accountant should reach every
// sales surface (Q / SO / DO / TI / RC / CN / DN / BN) without "Access denied".
// P1 (ckpt1) closed the seed gap and P6.2 (ckpt3) added the BN role grants
// — this spec verifies the matrix at the navigation layer.

const SALES_PATHS = [
  '/quotations',
  '/sales-orders',
  '/delivery-orders',
  '/tax-invoices',
  '/receipts',
  '/credit-notes',
  '/debit-notes',
  '/invoices',
];

for (const path of SALES_PATHS) {
  test(`demo-accountant can open ${path}`, async ({ page }) => {
    await loginAs(page, 'demo-accountant', 'Demo@1234');
    await page.goto(path);
    await expect(page.getByText(/Access denied|ไม่มีสิทธิ์/i)).toHaveCount(0);
    await expect(page.locator('body')).toBeVisible();
  });
}

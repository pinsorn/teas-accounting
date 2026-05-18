import { test, expect } from '@playwright/test';

async function login(page: import('@playwright/test').Page) {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

// Sprint 9 B5 — the ภ.พ.30 generator. Preview computes the RD form line-by-line
// (sales taxable/zero-rated/exempt categorised by tax-code, ม.82/6 claim ratio,
// net VAT). Backend categorisation correctness is covered by the Api
// integration tests; this asserts the generator endpoint + UI render the form.
test('ภ.พ.30 generator previews the VAT return form', async ({ page }) => {
  await login(page);
  await page.goto('/reports/pnd30');

  await page.getByRole('button', { name: /แสดงตัวอย่าง|preview/i }).first().click();

  const status = page.getByTestId('pnd30-status');
  await expect(status).toBeVisible({ timeout: 15_000 });
  await expect(status).toContainText(/Preview/i);
  // The RD form must render the net-VAT-payable line.
  await expect(page.getByText(/ภาษีที่ต้องชำระสุทธิ|Net VAT payable/i)).toBeVisible();
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

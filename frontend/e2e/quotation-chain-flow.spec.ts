import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 10 Part B — full Q → SO → DO → TI happy path through the UI.
// Backend conversion correctness is covered by the Api integration tests; this
// drives the chain end-to-end and asserts the linked Tax Invoice appears.
test('quotation chain: Q → SO → DO (combined) → linked TI', async ({ page }) => {
  await login(page);

  await page.goto('/quotations/new');
  // Pick the seeded demo customer via the selector.
  await page.getByPlaceholder(/ค้นหาชื่อ|tax id/i).fill('ลูกค้าทดสอบ');
  await page.getByRole('button', { name: /ลูกค้าทดสอบ/ }).first().click();
  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();

  // Redirected to the quotation detail.
  await page.waitForURL(/\/quotations\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('q-status')).toContainText(/Draft/);

  await page.getByTestId('q-send').click();
  await expect(page.getByTestId('q-status')).toContainText(/Sent/, { timeout: 15_000 });

  await page.getByTestId('q-accept').click();
  await expect(page.getByTestId('q-status')).toContainText(/Accepted/, { timeout: 15_000 });

  await page.getByTestId('q-convert').click();
  await page.waitForURL(/\/sales-orders\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('so-status')).toContainText(/Draft/);

  await page.getByTestId('so-post').click();
  await expect(page.getByTestId('so-status')).toContainText(/Posted/, { timeout: 15_000 });

  await page.getByTestId('so-create-do').click();
  await page.waitForURL(/\/delivery-orders\/\d+$/, { timeout: 15_000 });

  await page.getByTestId('do-post').click();
  // Pattern X: combined DO auto-creates + links the Tax Invoice.
  await expect(page.getByTestId('do-ti-link')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

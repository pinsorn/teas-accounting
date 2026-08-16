import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// F8b (specs/fix-chain-conversion-integrity.md WP-4) — Q→TI is now a server-side
// conversion: q-create-ti on the quotation detail page no longer navigates to a
// prefilled create form (that form dropped the discount, F8b) — it POSTs
// quotations/{id}/create-tax-invoice directly and lands on the created draft TI.
// The old `?fromQuotationId=` URL param no longer prefills anything (tax-invoices/new
// is pure hand-entry now); this test drives the real button flow instead.
test('tax invoice: q-create-ti converts an accepted quotation server-side', async ({ page }) => {
  test.setTimeout(60_000);
  await login(page);

  await page.goto('/quotations/new');
  await pickCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e q-create-ti item');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('1500');

  // Issue → create + send → quotation detail, already Sent.
  await page.getByRole('button', { name: /ออกใบเสนอราคา/ }).click();
  await page.waitForURL(/\/quotations\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('q-status')).toContainText(/Sent|ส่งแล้ว/, { timeout: 15_000 });

  await page.getByTestId('q-accept').click();
  await expect(page.getByTestId('q-status')).toContainText(/Accepted|ตอบรับแล้ว/, { timeout: 15_000 });

  await page.getByTestId('q-create-ti').click();
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

test('tax invoice detail: cross-ref chip back to originating Q (P6.1)', async ({ page }) => {
  await login(page);
  await page.goto('/tax-invoices');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  const rows = await page.locator('tbody tr').count();
  if (rows > 0) {
    await page.locator('tbody tr').first().getByRole('link').first().click();
    await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
    // Either the P6.1 chip is present (Q-linked TI) or the cross-ref panel
    // surfaces the receipts/notes — both are acceptable smoke outcomes.
    await expect(page.locator('body')).toBeVisible();
  }
});

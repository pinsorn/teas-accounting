import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// Sprint 10 Part B + Sprint 13e P2/P4 — full Q → SO → DO → TI happy path
// through the rebuilt forms. "ออกใบเสนอราคา" (Issue) both creates the draft
// and sends it, landing on the quotation detail (status=Sent).
test('quotation chain: Q (issue) → SO → DO (combined) → linked TI', async ({ page }) => {
  await login(page);

  await page.goto('/quotations/new');
  // Customer MODAL pick (EntityPickerModal).
  await pickCustomer(page);

  // One line via the shared LineItemsTable (product picker = free text here).
  await page.getByLabel('รายละเอียด 1').fill('e2e chain item');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('1500');

  // Issue → create + send → quotation detail, already Sent.
  await page.getByRole('button', { name: /ออกใบเสนอราคา/ }).click();
  await page.waitForURL(/\/quotations\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('q-status')).toContainText(/Sent|ส่งแล้ว/, { timeout: 15_000 });

  await page.getByTestId('q-accept').click();
  await expect(page.getByTestId('q-status')).toContainText(/Accepted|ตอบรับแล้ว/, {
    timeout: 15_000,
  });

  await page.getByTestId('q-convert').click();
  await page.waitForURL(/\/sales-orders\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('so-status')).toContainText(/Draft|ร่าง/);

  await page.getByTestId('so-post').click();
  await expect(page.getByTestId('so-status')).toContainText(/Posted|บันทึกแล้ว/, {
    timeout: 15_000,
  });

  await page.getByTestId('so-create-do').click();
  await page.waitForURL(/\/delivery-orders\/\d+$/, { timeout: 15_000 });

  await page.getByTestId('do-post').click();
  // Pattern X: combined DO auto-creates + links the Tax Invoice.
  await expect(page.getByTestId('do-ti-link')).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// Sprint 10 Part B + Sprint 13e P2/P4 — full Q → SO → DO → TI happy path
// through the rebuilt forms. "ออกใบเสนอราคา" (Issue) both creates the draft
// and sends it, landing on the quotation detail (status=Sent).
test('quotation chain: Q (issue) → SO → DO (combined) → linked TI', async ({ page }) => {
  // Q + SO + DO transitions + Invoice create — exceeds the 30s default.
  test.setTimeout(120_000);
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

  // PRODUCT CHANGE (cont.69 Phase 1, documented in SalesOrderDeliveryServices):
  // `do-post` became `do-issue` → `do-mark-delivered`, and the Pattern-X
  // combined-DO auto-TI on Delivered was REMOVED — the linear flow now issues
  // the Tax Invoice from the Invoice (ใบแจ้งหนี้) step, never from delivery.
  // Follow the new chain: DO Delivered → create Invoice → chain links back.
  await page.getByTestId('do-issue').click();
  // Wait for the Issued state to settle (the issue refetch re-renders the
  // action bar; a plain click can chase an unstable button — gotcha §16 family).
  await expect(page.locator('body')).toContainText(/ออกเอกสารแล้ว|Issued/, { timeout: 15_000 });
  await expect(async () => {
    await page.getByTestId('do-mark-delivered').click({ force: true });
    await expect(page.locator('body')).toContainText(/ส่งมอบแล้ว|Delivered/, { timeout: 3_000 });
  }).toPass({ timeout: 25_000 });

  await page.getByTestId('do-create-invoice').click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  // The Invoice detail's DocumentChain carries the upstream DO reference.
  await expect(
    page.getByTestId('document-chain').locator('a[href^="/delivery-orders/"]'),
  ).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

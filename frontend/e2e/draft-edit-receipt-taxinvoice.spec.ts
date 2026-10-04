import { test, expect, type Page } from '@playwright/test';
import { login, createAndPostTaxInvoice, pickCustomer, pickTaxInvoice, detailDocNo } from './_helpers';

// specs/draft-edit-receipt-taxinvoice.md §6 E1/E2 — web draft-edit for Tax Invoice and Receipt.
// Needs the WP-1 backend (PUT + GET draft-input) on the local stack.

/** Click a save/post button and capture the id from the create POST's JSON response. */
async function saveAndCaptureId(page: Page, urlPart: string, key: string, button: RegExp | string, exact = false) {
  const [res] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(urlPart) && r.request().method() === 'POST' && r.ok()),
    page.getByRole('button', { name: button, exact }).click(),
  ]);
  return Number((await res.json())[key]);
}

test('E1: edit a draft tax invoice, then post it', async ({ page }) => {
  test.setTimeout(90_000);
  await login(page);
  await page.goto('/tax-invoices/new');
  await pickCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e draft-edit item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  const id = await saveAndCaptureId(page, '/tax-invoices', 'tax_invoice_id', /บันทึกร่าง|Save draft/i);

  await page.goto(`/tax-invoices/${id}`);
  await page.getByTestId('ti-edit').click();
  await page.waitForURL(new RegExp(`/tax-invoices/${id}/edit$`));

  // Prefilled from draft-input; change qty 1 -> 2 and save (PUT) -> back on the detail page.
  await expect(page.getByLabel('รายละเอียด 1')).toHaveValue('e2e draft-edit item');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByRole('button', { name: /บันทึกร่าง|Save draft/i }).click();
  await page.waitForURL(new RegExp(`/tax-invoices/${id}$`), { timeout: 15_000 });
  await expect(page.locator('main')).toContainText('2,000.00');
  await expect(page.locator('main')).toContainText('140.00');
  await expect(page.locator('main')).toContainText('2,140.00');

  await page.getByTestId('ti-post-action').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await expect(page.locator('body')).toContainText(/-TI-\d{4}/, { timeout: 15_000 });
  await expect(page.locator('main')).toContainText('2,140.00');
});

test('E2: edit a draft receipt (applied amount), then post it', async ({ page }) => {
  test.setTimeout(120_000);
  await login(page);
  await createAndPostTaxInvoice(page); // 1,070.00 incl. VAT
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/receipts/new');
  await pickCustomer(page);
  await pickTaxInvoice(page, 1, tiDocNo);
  await page.getByLabel('ยอดชำระ 1').fill('500');
  const id = await saveAndCaptureId(page, '/receipts', 'receipt_id', 'บันทึก', true);

  await page.goto(`/receipts/${id}`);
  await page.getByTestId('rc-edit').click();
  await page.waitForURL(new RegExp(`/receipts/${id}/edit$`));

  await expect(page.getByLabel('ยอดชำระ 1')).toHaveValue(/500/);
  await page.getByLabel('ยอดชำระ 1').fill('1070');
  await page.getByRole('button', { name: 'บันทึก', exact: true }).click();
  await page.waitForURL(new RegExp(`/receipts/${id}$`), { timeout: 15_000 });
  await expect(page.locator('main')).toContainText('1,070.00');

  // Receipt detail posts directly (no confirm dialog, unlike the tax invoice page).
  await page.getByTestId('rc-post-action').click();
  await expect(page.locator('body')).toContainText(/-RC-\d{4}/, { timeout: 15_000 });
});

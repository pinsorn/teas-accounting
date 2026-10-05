import { test, expect, type Page } from '@playwright/test';
import { login, createAndPostTaxInvoice, pickCustomer, pickTaxInvoice, detailDocNo, mePermissions } from './_helpers';

// specs/cancel-reissue-sales-docs.md §6 T21 — cancel + reissue via the UI (TI), plus a receipt cancel leg.
// Needs the WP-1..4 backend/FE on the local stack (seed 645 grants the .cancel codes to SUPER_ADMIN).

/** Open the cancel modal by testid, pick the first real reason, type free text, confirm. */
async function cancelViaModal(page: Page, triggerTestId: string, text: string) {
  await page.getByTestId(triggerTestId).click();
  const modal = page.getByTestId('cancel-modal');
  await expect(modal).toBeVisible();
  const select = modal.getByTestId('cancel-reason-code');
  const firstCode = await select.locator('option:not([value=""])').first().getAttribute('value');
  await select.selectOption(firstCode!);
  await modal.getByTestId('cancel-reason-text').fill(text);
  await modal.getByTestId('cancel-confirm').click();
}

test('T21a: post TI -> cancel-and-reissue -> edit description -> post replacement', async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  const perms = await mePermissions(page);
  expect(perms.isSuperAdmin || perms.permissions.includes('sales.tax_invoice.cancel')).toBeTruthy();

  const origId = await createAndPostTaxInvoice(page);
  const origDocNo = await detailDocNo(page, 'TI');

  // Cancel-and-reissue -> lands on the replacement's EDIT page.
  const [res] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(`/tax-invoices/${origId}/cancel-and-reissue`) && r.request().method() === 'POST'),
    cancelViaModal(page, 'ti-cancel-reissue', 'e2e cancel+reissue'),
  ]);
  expect(res.ok(), await res.text()).toBeTruthy();
  await page.waitForURL(/\/tax-invoices\/\d+\/edit$/, { timeout: 15_000 });
  const replId = Number(page.url().match(/\/tax-invoices\/(\d+)\/edit$/)![1]);
  expect(replId).not.toBe(origId);

  // Edit the description (amount-locked fields untouched) and save.
  await expect(page.getByLabel('รายละเอียด 1')).toHaveValue('e2e item');
  await page.getByLabel('รายละเอียด 1').fill('e2e item (corrected)');
  await page.getByRole('button', { name: /บันทึกร่าง|Save draft/i }).click();
  await page.waitForURL(new RegExp(`/tax-invoices/${replId}$`), { timeout: 15_000 });
  await expect(page.getByTestId('ti-replacement-banner')).toBeVisible();

  // Post the replacement.
  await page.getByTestId('ti-post-action').click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await expect(page.locator('main')).toContainText(/-TI-\d{4}/, { timeout: 15_000 });
  await expect(page.getByTestId('ti-post-action')).toHaveCount(0, { timeout: 15_000 });

  // Replacement paper carries the reference line (paper API + the rendered preview).
  const paper = await page.request.get(`/api/proxy/tax-invoices/${replId}/paper`);
  expect(paper.ok()).toBeTruthy();
  expect(JSON.stringify(await paper.json())).toContain('ยกเลิกและออกแทนฉบับเดิม');
  await expect(page.locator('.paper-wrap')).toContainText('ยกเลิกและออกแทนฉบับเดิม');
  await expect(page.locator('.paper-wrap')).toContainText(origDocNo);

  // Original: cancelled banner + link to the replacement.
  await page.goto(`/tax-invoices/${origId}`);
  const banner = page.getByTestId('ti-cancelled-banner');
  await expect(banner).toContainText('ยกเลิก', { timeout: 15_000 });
  const link = page.getByTestId('ti-replacement-link');
  await expect(link).toBeVisible();
  await link.click();
  await page.waitForURL(new RegExp(`/tax-invoices/${replId}$`), { timeout: 15_000 });
});

test('T21b: post receipt -> cancel -> Voided banner', async ({ page }) => {
  test.setTimeout(150_000);
  await login(page);
  await createAndPostTaxInvoice(page);
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/receipts/new');
  await pickCustomer(page);
  await pickTaxInvoice(page, 1, tiDocNo);
  await page.getByLabel('ยอดชำระ 1').fill('1070');
  const [created] = await Promise.all([
    page.waitForResponse((r) => r.url().includes('/receipts') && r.request().method() === 'POST' && r.ok()),
    page.getByRole('button', { name: 'บันทึก', exact: true }).click(),
  ]);
  const rcId = Number((await created.json()).receipt_id);
  await page.goto(`/receipts/${rcId}`);
  await page.getByTestId('rc-post-action').click(); // no confirm dialog on receipts
  await expect(page.locator('body')).toContainText(/-RC-\d{4}/, { timeout: 15_000 });

  const [res] = await Promise.all([
    page.waitForResponse((r) => r.url().includes(`/receipts/${rcId}/cancel`) && r.request().method() === 'POST'),
    cancelViaModal(page, 'rc-cancel', 'e2e receipt cancel'),
  ]);
  expect(res.ok(), await res.text()).toBeTruthy();
  await expect(page.getByTestId('rc-cancelled-banner')).toBeVisible({ timeout: 15_000 });
  await expect(page.locator('main')).toContainText(/Voided|ยกเลิก/);
});

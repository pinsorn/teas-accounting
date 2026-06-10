import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice, pickCustomer, pickTaxInvoice, detailDocNo } from './_helpers';

// login → post a TI → issue a Receipt applied to it → post → see it in the list.
// Heavy multi-document flow: ~18s warm on `next dev` (the suite's 30s default
// assumes `next start`) — give it headroom so route-compile jitter can't tip it.
test('issue a receipt against a posted tax invoice', async ({ page }) => {
  test.setTimeout(60_000);
  await login(page);
  await createAndPostTaxInvoice(page);
  // Redesign: taxInvoiceId is now a typeahead picker that searches by doc_no
  // (filling the numeric id leaves no TI selected → draft create 422 → no
  // confirm dialog). Scrape the doc no off the detail page we just landed on.
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/receipts/new');
  await pickCustomer(page);
  await pickTaxInvoice(page, 1, tiDocNo);
  await page.getByLabel('appliedAmount 1').fill('1070'); // 1000 + 7% VAT

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();

  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });
  await expect(page.locator('body')).toContainText(/-RC-\d{4}/);

  await page.goto('/receipts');
  await expect(page.locator('table')).toContainText(/-RC-\d{4}/);
});

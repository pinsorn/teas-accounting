import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// login → post TI → create a Credit Note linked to it (reason required) → post →
// CN appears in the list referencing the original TI.
test('credit note corrects a posted tax invoice', async ({ page }) => {
  await login(page);
  const tiId = await createAndPostTaxInvoice(page);

  await page.goto(`/credit-notes/new?fromTaxInvoiceId=${tiId}&reason=AmountError`);
  // originalTaxInvoiceId is prefilled from the query; set amount + reason text.
  await page.getByLabel('adjustmentSubtotal').fill('500');
  await page.getByLabel('taxRate').fill('0.07');
  await page.locator('textarea').fill('แก้ไขจำนวนเงินที่ออกผิด (e2e)');

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(/irreversible|ไม่สามารถแก้ไข|ยืนยันเหตุผล/i);
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();

  await page.waitForURL(/\/credit-notes\/\d+$/, { timeout: 15_000 });
  await expect(page.locator('body')).toContainText(/-CN-\d{4}/);
  // Detail references the original posted TI by its allocated doc number + the reason.
  await expect(page.locator('body')).toContainText(/-TI-\d{4}/);
  await expect(page.locator('body')).toContainText('AmountError');
});

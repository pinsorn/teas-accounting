import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// login → post a TI → issue a Receipt applied to it → post → see it in the list.
test('issue a receipt against a posted tax invoice', async ({ page }) => {
  await login(page);
  const tiId = await createAndPostTaxInvoice(page);

  await page.goto('/receipts/new');
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();
  await page.getByLabel('taxInvoiceId 1').fill(String(tiId));
  await page.getByLabel('appliedAmount 1').fill('1070'); // 1000 + 7% VAT

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm Post|ยืนยัน Post/i }).click();

  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });
  await expect(page.locator('body')).toContainText(/-RC-\d{4}/);

  await page.goto('/receipts');
  await expect(page.locator('table')).toContainText(/-RC-\d{4}/);
});

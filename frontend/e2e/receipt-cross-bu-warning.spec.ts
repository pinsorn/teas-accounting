import { test, expect, type Page } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// Sprint 8 — a receipt that settles two TIs tagged to different Business Units
// must post (no block) and surface the cross-BU warning on the detail page.

async function createBu(page: Page, code: string) {
  await page.goto('/settings/business-units');
  await page.getByRole('button', { name: /เพิ่มหน่วยธุรกิจ|Add Business Unit/ })
    .click({ force: true }); // sonner toast from a prior save can overlap the button
  const modal = page.getByRole('dialog');
  await expect(modal).toBeVisible();
  await modal.getByText(/^รหัส \*/).locator('xpath=following::input[1]').fill(code);
  await modal.getByText(/^ชื่อ \(ไทย\) \*/).locator('xpath=following::input[1]')
    .fill(`สาย ${code}`);
  await modal.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await expect(page.locator('table')).toContainText(code);
}

async function postTiWithBu(page: Page, code: string): Promise<number> {
  await page.goto('/tax-invoices/new');
  await pickCustomer(page);
  // BU <select> — exact option label is "<code> — <nameTh>" (no regex per Playwright).
  await page.getByLabel('หน่วยธุรกิจ').selectOption({ label: `${code} — สาย ${code}` });
  await page.getByLabel('รายละเอียด 1').fill('cross-bu item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  return Number(page.url().match(/\/tax-invoices\/(\d+)$/)![1]);
}

test('cross-BU receipt posts and shows the cross-BU warning', async ({ page }) => {
  await login(page);

  const codeA = `XBUA${Date.now().toString().slice(-6)}`;
  const codeB = `XBUB${Date.now().toString().slice(-6)}`;
  await createBu(page, codeA);
  await createBu(page, codeB);

  const tiA = await postTiWithBu(page, codeA);
  const tiB = await postTiWithBu(page, codeB);

  await page.goto('/receipts/new');
  await pickCustomer(page);

  await page.getByLabel('taxInvoiceId 1').fill(String(tiA));
  await page.getByLabel('appliedAmount 1').fill('1070');
  await page.getByRole('button', { name: /เพิ่มรายการ|addApply|Add/ }).first().click();
  await page.getByLabel('taxInvoiceId 2').fill(String(tiB));
  await page.getByLabel('appliedAmount 2').fill('1070');

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();

  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });
  // Cross-BU warning alert (allowed, not blocked) lists both BU codes.
  // Scope to the warning div — Next's route-announcer is also role=alert.
  const alert = page.locator('.alert-warning');
  await expect(alert).toContainText(/ครอบคลุม/);
  await expect(alert).toContainText(codeA);
  await expect(alert).toContainText(codeB);
});

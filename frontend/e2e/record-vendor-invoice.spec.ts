import { test, expect } from '@playwright/test';
import { login, createVendor, pickVendor } from './_helpers';

// login → create vendor → record a Vendor Invoice → post → VI-NNNN + Posted.
test('record and post a vendor invoice', async ({ page }) => {
  await login(page);
  const code = await createVendor(page);

  await page.goto('/vendor-invoices/new');
  await pickVendor(page, code);

  await page.getByText(/เลขที่ใบกำกับภาษีของผู้ขาย|Vendor's tax invoice no/)
    .locator('xpath=following::input[1]').fill('VTI-E2E-001');

  // category select (ExpenseCategorySelector) — pick the seeded SVC.
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]')
    .selectOption({ label: 'ค่าบริการ (SVC)' });

  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('e2e service');
  await page.getByText(/จำนวนเงิน \(ก่อน VAT\)|Amount \(ex-VAT\)/)
    .locator('xpath=following::input[1]').fill('1000');

  await page.getByRole('button', { name: /^บันทึกเอกสาร \(Post\)|^Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm Post|ยืนยัน Post/i }).click();

  await page.waitForURL(/\/vendor-invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.locator('body')).toContainText(/-VI-\d{4}/);
  await expect(page.locator('body')).toContainText(/บันทึกแล้ว|Posted/);
});

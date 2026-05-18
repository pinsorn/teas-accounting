import { test, expect } from '@playwright/test';
import { login, createVendor, pickVendor } from './_helpers';

// The creator cannot approve their own PV (SoD, CLAUDE.md §12.1). The attempt
// fails and the document stays Draft.
test('creator self-approve is blocked; PV stays Draft', async ({ page }) => {
  await login(page, 'admin');
  const code = await createVendor(page);

  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]')
    .selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('e2e sod');
  await page.getByText(/^มูลค่าก่อนภาษี|^Subtotal/).first()
    .locator('xpath=following::input[1]').fill('500');

  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });

  // admin == creator → Approve must fail; status must remain Draft.
  await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
  // give the failed request + toast a moment, then assert the invariant.
  await page.waitForTimeout(1500);
  await expect(page.locator('body')).toContainText(/ร่าง|Draft/);
  await expect(page.locator('body')).not.toContainText(/อนุมัติแล้ว|Approved/);
});

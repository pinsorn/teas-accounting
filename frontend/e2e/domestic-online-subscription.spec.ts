import { test, expect } from '@playwright/test';
import { login, logout, createVendor, pickVendor } from './_helpers';

// Sprint 8.7 — domestic auto-charge (Scenario A): manual self-withhold toggle.
// Gross-up: expense = subtotal + vat + wht, bank = subtotal + vat.
test('domestic online subscription — manual self-withhold gross-up', async ({ page }) => {
  await login(page, 'admin');
  const code = await createVendor(page);   // domestic VAT-registered vendor

  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]').selectOption({ label: 'ค่าบริการ (SVC)' });

  // Domestic → toggle is editable + default OFF; turn it ON manually.
  const swToggle = page.locator('label:has-text("Self-withhold mode") input[type="checkbox"]');
  await expect(swToggle).toBeEnabled();
  await swToggle.check();
  await expect(swToggle).toBeChecked();

  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('Meta ads auto-charge');
  await page.getByText(/^มูลค่าก่อนภาษี|^Subtotal/).first()
    .locator('xpath=following::input[1]').fill('10000');
  await page.getByText(/^หัก ณ ที่จ่าย|^WHT$/).first()
    .locator('xpath=following::input[1]').fill('0.03');

  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });
  const pvUrl = page.url();

  await logout(page);
  await login(page, 'approver');
  await page.goto(pvUrl);
  await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
  await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });
  const postBtn = page.getByRole('button', { name: /บันทึกเอกสาร \(Post\)|^Post$/ });
  await expect(postBtn).toBeVisible({ timeout: 10_000 });
  await expect(async () => {
    await postBtn.click({ force: true });
    await page.waitForResponse(
      (r) => /\/payment-vouchers\/\d+\/post$/.test(r.url()) && r.request().method() === 'POST',
      { timeout: 3_000 });
  }).toPass({ timeout: 25_000 });
  await expect(page.locator('body')).toContainText(/บันทึกแล้ว|Posted/, { timeout: 10_000 });
  await expect(page.locator('body')).toContainText(/Self-withhold/);
});

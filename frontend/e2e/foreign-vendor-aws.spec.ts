import { test, expect, type Page } from '@playwright/test';
import { login, logout, pickVendor } from './_helpers';

// Sprint 8.7 — foreign vendor without Thai VAT-D (AWS): PV auto self-withhold
// WHT 15% (gross-up) + ภ.พ.36 flag. Detail shows the Self-withhold badge.

async function createForeignVendor(page: Page, code: string) {
  await page.goto('/vendors/new');
  await page.getByText(/รหัสผู้ขาย|Vendor code/).locator('xpath=following::input[1]').fill(code);
  await page.getByText(/^ชื่อ \(ไทย\)|Name \(Thai\)/).locator('xpath=following::input[1]')
    .fill('Amazon Web Services Inc.');
  // Foreign toggle → country US, no VAT-D.
  await page.locator('label:has-text("ต่างประเทศ") input[type="checkbox"]').check();
  await page.locator('select[aria-label="ประเทศ"]').selectOption('US');
  await page.getByRole('button', { name: /บันทึกผู้ขาย|Save vendor/ }).click();
  await page.waitForURL(/\/vendors$/, { timeout: 15_000 });
}

test('foreign vendor (AWS) — auto self-withhold 15% gross-up + PND.36', async ({ page }) => {
  // Redesigned create pages are slower — the old 30s default is too tight.
  test.setTimeout(120_000);
  await login(page, 'admin');
  const code = `AWS${Date.now().toString().slice(-6)}`;
  await createForeignVendor(page, code);

  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]').selectOption({ label: 'ค่าบริการ (SVC)' });

  // Self-withhold auto-ON + locked + warning chip for foreign-no-VAT-D.
  // Toggle is now a daisyUI checkbox labelled by the Thai self-withhold text.
  const swToggle = page.getByRole('checkbox', { name: /ออกภาษีให้เอง/ });
  await expect(swToggle).toBeChecked();
  await expect(swToggle).toBeDisabled();
  await expect(page.locator('body')).toContainText(/ภ.พ.36|PND\.36/);

  await page.getByRole('textbox', { name: /^รายละเอียด/ }).first().fill('AWS cloud');
  await page.getByRole('spinbutton', { name: /^มูลค่าก่อนภาษี/ }).first().fill('3500');
  await page.getByRole('spinbutton', { name: /หัก ณ ที่จ่าย/ }).first().fill('0.15');

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

  // Self-withhold badge visible at audit time (detail badge is Thai-only now).
  await expect(page.locator('body')).toContainText(/ออกภาษีให้เอง/);
});

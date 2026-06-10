import { test } from '@playwright/test';
import { login, createVendor, pickVendor } from './_helpers';

// Sprint-6 visual capture (Report-Backend8 §screenshots). Pure capture.
const DIR = 'screenshots';

test('capture sprint-6 screens', async ({ page }) => {
  // Vendor create + 2 VI forms + PV form + fixed settle waits — needs more
  // than the 30s default since the design swap.
  test.setTimeout(120_000);
  await login(page);
  const code = await createVendor(page);

  await page.goto('/vendor-invoices/new');
  await pickVendor(page, code);
  await page.getByText(/เลขที่ใบกำกับภาษีของผู้ขาย|Vendor's tax invoice no/)
    .locator('xpath=following::input[1]').fill('VTI-SHOT');
  // VI line rows gained a 2nd <select> (ProductType) — the label-xpath now
  // strict-mode 2-matches; target the testid like record-vendor-invoice does.
  await page.getByTestId('expense-category-select').first()
    .selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/s6-01-vendor-invoice-new.png`, fullPage: true });

  await page.goto('/vendor-invoices');
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/s6-02-vendor-invoices-list.png`, fullPage: true });

  await page.goto('/payment-vouchers/new');
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/s6-03-payment-voucher-new.png`, fullPage: true });

  // A posted VI detail (settlement progress + Settle-with-PV).
  await page.goto('/vendor-invoices/new');
  await pickVendor(page, code);
  await page.getByText(/เลขที่ใบกำกับภาษีของผู้ขาย|Vendor's tax invoice no/)
    .locator('xpath=following::input[1]').fill('VTI-SHOT2');
  await page.getByTestId('expense-category-select').first()
    .selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('shot');
  await page.getByText(/จำนวนเงิน \(ก่อน VAT\)|Amount \(ex-VAT\)/)
    .locator('xpath=following::input[1]').fill('1000');
  await page.getByRole('button', { name: /^บันทึกเอกสาร \(Post\)|^Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/vendor-invoices\/\d+$/, { timeout: 15_000 });
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/s6-04-vendor-invoice-detail.png`, fullPage: true });

  // A PV draft detail showing the Approve button + SoD hint.
  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]').selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('shot pv');
  await page.getByText(/^มูลค่าก่อนภาษี|^Subtotal/).first()
    .locator('xpath=following::input[1]').fill('500');
  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });
  await page.waitForTimeout(1200); // networkidle never settles since the design swap (topbar polling)
  await page.screenshot({ path: `${DIR}/s6-05-payment-voucher-detail.png`, fullPage: true });
});

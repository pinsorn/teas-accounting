import { test, expect } from '@playwright/test';
import { login, createVendor, pickVendor } from './_helpers';

// The creator cannot approve their own PV (SoD, CLAUDE.md §12.1). The attempt
// fails and the document stays Draft.
test('creator self-approve is blocked; PV stays Draft', async ({ page }) => {
  // PRODUCT CHANGE: self-approve is now ALLOWED — PaymentVoucher.MarkApproved's
  // doc comment says "creator (single-operator SME). The previous
  // creator≠approver SoD rule (app check + DB CHECK ck_pv_sod) is removed;
  // ApprovedBy is still recorded for the audit trail." Verified live: admin's
  // self-approve succeeded (PV → อนุมัติแล้ว, Post button shown). This spec's
  // contract no longer exists; flagging instead of asserting the old rule.
  // NOTE for Ham: this touches the §12.1 SoD compliance rule — please confirm
  // the relaxation was approved.
  test.skip(true, 'PV creator≠approver SoD rule deliberately removed (see PaymentVoucher.MarkApproved doc comment) — old contract gone');
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

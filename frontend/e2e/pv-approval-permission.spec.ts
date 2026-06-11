import { test, expect } from '@playwright/test';
import { login, createVendor, pickVendor } from './_helpers';

// cont.77 (Ham decision) — the creator≠approver SoD rule on PaymentVoucher was
// REMOVED for the single-operator SME case (app check + DB CHECK ck_pv_sod both
// dropped; see PaymentVoucher.MarkApproved doc comment). Approval is now
// PERMISSION-based, with ApprovedBy still recorded for the audit trail.
// This spec pins the new contract (rewritten cont.88 from the old SoD spec):
// a creator holding the approve permission CAN self-approve their own PV.
test('PV approval is permission-based: creator can self-approve', async ({ page }) => {
  await login(page, 'admin');
  const code = await createVendor(page);

  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]')
    .selectOption({ label: 'ค่าบริการ (SVC)' });
  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('e2e permission-based approve');
  await page.getByText(/^มูลค่าก่อนภาษี|^Subtotal/).first()
    .locator('xpath=following::input[1]').fill('500');

  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });

  // admin == creator → self-approve SUCCEEDS (permission-based, not SoD).
  await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
  await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });
});

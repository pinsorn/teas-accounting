import { test, expect } from '@playwright/test';
import { login, logout, createVendor, pickVendor } from './_helpers';

// admin creates a PV (WHT 3%) draft → approver (different user) approves + posts →
// PV Posted, 50 ทวิ issued + its PDF downloads (200).
test('payment voucher with WHT: SoD create→approve→post + 50 tawi', async ({ page }) => {
  // Vendor create + PV form + user switch + approve/post — the redesigned
  // pages need more than the 30s default (approver's PV detail was still on
  // "กำลังโหลด…" when the old budget ran out).
  test.setTimeout(120_000);
  await login(page, 'admin');
  const code = await createVendor(page);

  await page.goto('/payment-vouchers/new');
  await pickVendor(page, code);
  await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
    .locator('xpath=following::select[1]')
    .selectOption({ label: 'ค่าบริการ (SVC)' });

  await page.getByText(/^รายละเอียด|^Description/).first()
    .locator('xpath=following::input[1]').fill('e2e consulting');
  await page.getByText(/^มูลค่าก่อนภาษี|^Subtotal/).first()
    .locator('xpath=following::input[1]').fill('1000');
  // WHT 3% (last numeric input in the line row).
  await page.getByText(/^หัก ณ ที่จ่าย|^WHT$/).first()
    .locator('xpath=following::input[1]').fill('0.03');

  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });
  const pvUrl = page.url();
  await expect(page.locator('body')).toContainText(/ร่าง|Draft/);

  // SoD: a *different* user approves then posts.
  await logout(page);
  await login(page, 'approver');
  await page.goto(pvUrl);
  await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
  await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });
  // Approve triggers a refetch that re-renders the action bar; the Post button
  // only appears once the PV is Approved. Wait for it to be actionable, then
  // force-click (sonner toast transiently overlays the bar — gotcha §16) and
  // wait for the actual POST response so the assertion can't race the request.
  const postBtn = page.getByRole('button', { name: /บันทึกเอกสาร \(Post\)|^Post$/ });
  await expect(postBtn).toBeVisible({ timeout: 10_000 });
  // The Approved→Post render + sonner toast make a single click racy; retry the
  // click until the POST actually fires (gotcha §16 family).
  await expect(async () => {
    await postBtn.click({ force: true });
    await page.waitForResponse(
      (r) => /\/payment-vouchers\/\d+\/post$/.test(r.url()) && r.request().method() === 'POST',
      { timeout: 3_000 });
  }).toPass({ timeout: 25_000 });
  await expect(page.locator('body')).toContainText(/บันทึกแล้ว|Posted/, { timeout: 10_000 });
  await expect(page.locator('body')).toContainText(/-PV-/);

  // 50 ทวิ certificate issued + PDF served.
  const list = await page.request.get('/api/proxy/wht-certificates?limit=1');
  expect(list.ok()).toBeTruthy();
  const body = await list.json();
  expect(body.items.length).toBeGreaterThan(0);
  const certId = body.items[0].whtCertificateId;
  const pdf = await page.request.get(`/api/proxy/wht-certificates/${certId}/pdf`);
  expect(pdf.status()).toBe(200);
});

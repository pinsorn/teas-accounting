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

  // Create-form redesign: line-item fields are now accessible-named controls —
  // the ProductPicker exposes `รายละเอียด N` (textbox), and the numeric inputs
  // are spinbuttons labelled by their (label-text + suffix) "มูลค่าก่อนภาษี *"
  // and "หัก ณ ที่จ่าย %". The old getByText(...).xpath=following::input[1]
  // pattern no longer resolves (and "มูลค่าก่อนภาษี" also appears in the totals).
  await page.getByRole('textbox', { name: 'รายละเอียด 1' }).fill('e2e consulting');
  await page.getByRole('spinbutton', { name: /^มูลค่าก่อนภาษี/ }).fill('1000');
  // B1(a) (army B-bn F1) — the FE now client-blocks Save whenever a line has a WHT rate
  // but no selected Income Type (50ทวิ), so a rate-only entry (no type picked) can no
  // longer reach Draft-save. Pick any real Income Type (index 0 is "— ไม่หัก —"); the
  // exact WHT rate is then set explicitly below, overriding whatever rate this type
  // auto-filled, so the test doesn't depend on which type seed data puts first.
  await page.getByTestId('pv-line-wht-type').selectOption({ index: 1 });
  // WHT 3% (the per-line "หัก ณ ที่จ่าย %" numeric input). This is a PERCENT-displayed
  // field (PercentRateInput) — '3' means 3%, not '0.03' (which resolves to 0.03%, army B-bn
  // B2). On 1,000 base that's a 30.00 WHT amount, asserted below via the issued 50 ทวิ cert.
  await page.getByRole('spinbutton', { name: /^หัก ณ ที่จ่าย/ }).fill('3');

  await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });
  const pvUrl = page.url();
  await expect(page.locator('body')).toContainText(/ร่าง|Draft/);

  // SoD: a *different* user approves then posts.
  await logout(page);
  await login(page, 'approver');
  await page.goto(pvUrl);
  // WP3 3.6 — approve now opens a ConfirmActionDialog (role=dialog) instead of
  // acting immediately; must click its own confirm button (common.confirm,
  // "ยืนยัน"/"Confirm") to actually fire the mutation.
  await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
  const approveDialog = page.getByRole('dialog');
  await expect(approveDialog).toBeVisible({ timeout: 5_000 });
  await approveDialog.getByRole('button', { name: /^ยืนยัน$|^Confirm$/ }).click();
  await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });
  // Approve triggers a refetch that re-renders the action bar; the Post button
  // only appears once the PV is Approved. Wait for it to be actionable, then
  // force-click (sonner toast transiently overlays the bar — gotcha §16) and
  // wait for the actual POST response so the assertion can't race the request.
  const postBtn = page.getByRole('button', { name: /บันทึกเอกสาร \(Post\)|^Post$/ });
  await expect(postBtn).toBeVisible({ timeout: 10_000 });
  // Post also opens a ConfirmActionDialog now — click the trigger, then its
  // own confirm button. The Approved→Post render + sonner toast make a single
  // click racy; retry the whole open-dialog→confirm sequence until the POST
  // actually fires (gotcha §16 family).
  await expect(async () => {
    await postBtn.click({ force: true });
    const postDialog = page.getByRole('dialog');
    await expect(postDialog).toBeVisible({ timeout: 3_000 });
    await postDialog.getByRole('button', { name: /^ยืนยัน$|^Confirm$/ }).click();
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
  const cert = body.items[0];
  // army B-bn B2 — the field previously took a raw fraction ('0.03' = 0.03%, not 3%); a
  // wrong-order-of-magnitude WHT amount went unasserted. 1,000 base * 3% = 30.00.
  expect(cert.whtAmount).toBe(30);
  const certId = cert.whtCertificateId;
  const pdf = await page.request.get(`/api/proxy/wht-certificates/${certId}/pdf`);
  expect(pdf.status()).toBe(200);

  // F-A (specs/fix-e2e-v1260-findings.md) — the on-screen paper foot must not double-subtract
  // WHT: Grand Total = backend Total + Wht, Net Payable = backend Total (PaperFootPlan.cs's
  // canonical semantics — summary.total IS the net when wht is set). Compare the LIVE DOM
  // against the SAME canonical /paper DTO the screen renders from (never a hardcoded expected
  // number — VAT on this line isn't asserted anywhere in this test) so this assertion would have
  // caught the shipped bug (screen showed Net = Grand − 2×WHT: e.g. Grand 850/Net 700 for a doc
  // whose PDF correctly showed 1,000/-150/850).
  const pvIdMatch = pvUrl.match(/\/payment-vouchers\/(\d+)$/);
  const pvId = pvIdMatch![1];
  const paperRes = await page.request.get(`/api/proxy/payment-vouchers/${pvId}/paper`);
  expect(paperRes.ok()).toBeTruthy();
  const { summary } = await paperRes.json();
  expect(summary.wht).toBe(cert.whtAmount);   // sanity: same 30 the cert carries

  const grandText = await page.locator('.paper-totals .row', { hasText: 'จำนวนเงินรวมทั้งสิ้น' })
    .locator('.v').innerText();
  const netText = await page.locator('.paper-totals .row.total', { hasText: 'ยอดเงินรับสุทธิ' })
    .locator('.v').innerText();
  const grand = parseFloat(grandText.replace(/,/g, ''));
  const net = parseFloat(netText.replace(/[^0-9.]/g, ''));   // strip ฿ + nbsp

  expect(grand).toBeCloseTo(summary.total + summary.wht, 2);   // Grand = Total + WHT
  expect(net).toBeCloseTo(summary.total, 2);                    // Net = Total, not re-subtracted
});

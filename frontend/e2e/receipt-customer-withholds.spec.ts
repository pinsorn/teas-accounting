import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// Sprint 8.6 (R-B4) — customer withholds tax on a B2B service receipt. No
// Product master, so the user overrides the WHT base manually to the service
// portion (here the whole 1,000 net). cash = 1,070 − 30 = 1,040.
test('receipt: customer withholds WHT (manual base override)', async ({ page }) => {
  await login(page);
  const tiId = await createAndPostTaxInvoice(page); // 1,000 net + 70 VAT = 1,070

  await page.goto('/receipts/new');
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();
  await page.getByLabel('taxInvoiceId 1').fill(String(tiId));
  await page.getByLabel('appliedAmount 1').fill('1070');

  // Toggle WHT on, pick the SVC type (rate auto-fills 3%), override base to
  // the 1,000 service portion → WHT 30, cash 1,040.
  await page.getByText('ลูกค้าหัก ภาษี ณ ที่จ่าย').click();
  await page.getByLabel('ประเภทเงินได้').selectOption({ label: 'SVC — ค่าบริการ (3.00%)' });
  const whtBox = page.locator('div.rounded-lg.border').filter({ hasText: 'WHT' });
  await whtBox.locator('input[type="number"]').nth(1).fill('1000');  // base
  await page.getByLabel(/50ทวิ|50tawi no\./i).first().fill('WHT-2026-E2E');

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  const confirmBtn = dialog.getByRole('button', { name: /Confirm Post|ยืนยัน Post/i });
  // Retry the confirm click until the receipt POST actually fires (sonner
  // 'Draft saved' toast + dialog re-render race — gotcha §16 family).
  await expect(async () => {
    await confirmBtn.click({ force: true });
    await page.waitForResponse(
      (r) => /\/receipts\/\d+\/post$/.test(r.url()) && r.request().method() === 'POST',
      { timeout: 3_000 });
  }).toPass({ timeout: 25_000 });

  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });
  // Detail page shows the WHT section with the net cash received.
  await expect(page.locator('body')).toContainText(/หัก ภาษี ณ ที่จ่าย|Withholding tax/);
  await expect(page.locator('body')).toContainText('WHT-2026-E2E');
});

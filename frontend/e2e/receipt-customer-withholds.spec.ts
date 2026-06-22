import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice, pickCustomer, pickTaxInvoice, detailDocNo } from './_helpers';

// Sprint 8.6 (R-B4) — customer withholds tax on a B2B service receipt. No
// Product master, so the user overrides the WHT base manually to the service
// portion (here the whole 1,000 net). cash = 1,070 − 30 = 1,040.
test('receipt: customer withholds WHT (manual base override)', async ({ page }) => {
  await login(page);
  await createAndPostTaxInvoice(page); // 1,000 net + 70 VAT = 1,070
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/receipts/new');
  await pickCustomer(page);
  // Redesign: taxInvoiceId is a typeahead picker keyed by doc_no, not id.
  await pickTaxInvoice(page, 1, tiDocNo);
  // Redesign: the applied-amount cell's aria-label is t('appliedAmount') = "ยอดชำระ"
  // (rc.appliedAmount in messages/th.json), rendered as spinbutton "ยอดชำระ N".
  await page.getByLabel('ยอดชำระ 1').fill('1070');

  // Toggle WHT on. Redesign: the WHT section is now PER-LINE — a WhtTypeSelect
  // (custom listbox, aria-label "ประเภทเงินได้ N") + base input ("ฐาน WHT N")
  // per TI line, replacing the old single select + box.
  await page.getByRole('checkbox', { name: 'ลูกค้าหัก ภาษี ณ ที่จ่าย' }).check();
  // (aria-haspopup="listbox" maps the trigger button to role=combobox)
  await page.getByRole('combobox', { name: 'ประเภทเงินได้ 1' }).click();
  // Options commit on onMouseDown inside the portal-positioned FloatingListbox
  // (can sit outside the viewport) — dispatch the event directly.
  const svc = page.locator('#wht-type-listbox').getByRole('button', { name: /SVC/ }).first();
  await svc.waitFor({ state: 'attached', timeout: 10_000 });
  await svc.dispatchEvent('mousedown');
  await page.getByLabel('ฐาน WHT 1').fill('1000'); // base override
  await page.getByLabel(/เลขที่ใบ 50ทวิ/).first().fill('WHT-2026-E2E');

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  const confirmBtn = dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i });
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

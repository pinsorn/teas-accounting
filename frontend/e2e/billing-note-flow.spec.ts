import { test, expect } from '@playwright/test';
import { login, pickCustomer, createAndPostTaxInvoice, detailDocNo, pickTaxInvoice } from './_helpers';

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) end-to-end.
// "ออกใบแจ้งหนี้" (Issue) creates the draft and allocates doc_no, landing on
// the BN detail page with status=Issued.
// R2/WP-7 (2026-08-12) — the manual "mark settled" button/endpoint is deleted; a
// posted Receipt is now the ONLY way to Settled (I7). The default e2e company
// (companyId 1) is VAT-registered, so the Invoice settles indirectly: it groups a
// posted Tax Invoice, and paying that TI off in full auto-flips the (already-Issued)
// Invoice to Settled — ReceiptService.cs:497-534 (Sprint 13i C6), the same mechanism
// the "group multiple tax invoices" test below exercises for chip display only.
test('billing note: create → issue → receipt → settled', async ({ page }) => {
  test.setTimeout(60_000);
  await login(page);

  // A standalone posted Tax Invoice to group into the Invoice (BillingNote).
  await createAndPostTaxInvoice(page);
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/invoices/new');
  await pickCustomer(page);

  // Group the just-posted TI and leave the line grid untouched — the BN then
  // inherits the TI's totals exactly (server-side line auto-generation), so
  // paying the TI off in full also pays off the BN.
  await page.getByLabel('ใบกำกับภาษีที่รวม').click();
  await page.locator('#taxinvoice-listbox')
    .getByRole('button', { name: new RegExp(tiDocNo) }).first().click();
  await expect(page.getByTestId('bn-ti-chips').locator('.badge')).toHaveCount(1);

  await page.getByTestId('bn-issue').click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  const bnUrl = page.url();
  await expect(page.getByTestId('bn-status')).toContainText(/Issued|ออกแล้ว/, {
    timeout: 15_000,
  });

  // Settle the grouped Tax Invoice via a posted Receipt — the only remaining path
  // to Settled now that the manual mark-settled button/endpoint is gone.
  await page.goto('/receipts/new');
  await pickCustomer(page);
  await pickTaxInvoice(page, 1, tiDocNo);
  await page.getByLabel('ยอดชำระ 1').fill('1070'); // 1000 + 7% VAT, matches createAndPostTaxInvoice
  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });

  // Back on the Invoice — the receipt auto-flipped it to Settled.
  await page.goto(bnUrl);
  await expect(page.getByTestId('bn-status')).toContainText(/Settled|ชำระครบแล้ว/, {
    timeout: 15_000,
  });
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

// Sprint 13i C7 — multi-TI grouping via the dedicated join table. Pick a customer,
// then group the posted TaxInvoices it has into the BN via the multi-select picker.
// Each pick renders a removable chip; the BN detail shows the same chips from the
// join table. Tolerant of seed depth: asserts as many chips as TIs were available
// (up to 2), and skips if the customer has no posted TI.
test('billing note: group multiple tax invoices via join table', async ({ page }) => {
  await login(page);
  await page.goto('/invoices/new');

  await pickCustomer(page);

  // Open the multi-TI picker (customer-scoped, Posted-only).
  await page.getByLabel('ใบกำกับภาษีที่รวม').click();
  const available = await page.locator('#taxinvoice-listbox button').count();
  // ponytail: test.skip silently passes when seed has no posted TI (false-green risk).
  // TODO: seed a posted TI via API in beforeEach so this condition is never true in CI.
  // For now, keep the skip but make the reason explicit and loud.
  // If you see this skip fire in CI, the seed is missing a posted TI for the demo customer.
  test.skip(available === 0, '[ponytail] SEED MISSING: no posted TI for this customer — test skipped, not passing. Fix: seed a posted TI via API in test setup.');

  const toPick = Math.min(available, 2);
  for (let i = 0; i < toPick; i++) {
    // Re-open the picker each pick (it closes on select).
    if (i > 0) await page.getByLabel('ใบกำกับภาษีที่รวม').click();
    await page.locator('#taxinvoice-listbox button').nth(0).click();
  }
  await expect(page.getByTestId('bn-ti-chips').locator('.badge')).toHaveCount(toPick);

  // One line, then issue → detail shows the same chips from the join table.
  await page.getByLabel('รายละเอียด 1').fill('e2e bn multi-ti');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByTestId('bn-issue').click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('bn-ti-chips').locator('a')).toHaveCount(toPick);
});

// Companion: Draft delete path (proves the 409 + hard-delete contract from P6.2).
test('billing note: create draft → delete', async ({ page }) => {
  await login(page);

  await page.goto('/invoices/new');
  await pickCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e bn to delete');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('500');

  // Save Draft → land back on list. Pick the top row (latest) and delete.
  await page.getByTestId('bn-save-draft').click();
  await page.waitForURL(/\/invoices(\?.*)?$/, { timeout: 15_000 });

  // Open the latest draft row.
  await page.locator('tbody tr').first().getByRole('link').first().click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('bn-status')).toContainText(/Draft|ร่าง/);

  // Redesign: window.confirm() was replaced by the useConfirm() in-page
  // alertdialog ("ยืนยันการทำรายการ" with ยกเลิก/ยืนยัน buttons).
  await page.getByTestId('bn-delete').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'ยืนยัน', exact: true }).click();
  await page.waitForURL(/\/invoices(\?.*)?$/, { timeout: 15_000 });
});

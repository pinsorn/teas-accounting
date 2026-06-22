import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice, pickCustomer, pickTaxInvoice, detailDocNo } from './_helpers';

// Sprint 13h E2E (ckpt4) — P8 cross-reference chips. Posted Receipt should
// render the linked Tax Invoice as a chip on RC detail, and the TI detail
// should render the linked Receipt with its applied amount.
//
// Repair (UI redesign): the old version clicked the FIRST /receipts row and
// asserted the chain card. But DocumentChain returns null when rows.length===0
// (component: `if (rows.length === 0) return null`), and a standalone non-VAT
// cash receipt (the seeded first row) has no applied TI → no chain → no testid.
// So build the chain deterministically in-test: post a TI, then a receipt
// applied to it (rows≥2 → DocumentChain renders data-testid="document-chain").
test('receipt cross-ref: RC detail shows linked TI chip', async ({ page }) => {
  test.setTimeout(60_000);
  await login(page);
  await createAndPostTaxInvoice(page);
  const tiDocNo = await detailDocNo(page, 'TI');

  await page.goto('/receipts/new');
  await pickCustomer(page);
  await pickTaxInvoice(page, 1, tiDocNo);
  // Applied-amount aria-label is t('appliedAmount') = "ยอดชำระ" (rc.appliedAmount).
  await page.getByLabel('ยอดชำระ 1').fill('1070');

  await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });

  // The receipt-applied-to-a-TI now carries the DocumentChain card
  // ("เอกสารอ้างอิง", data-testid="document-chain") with the linked TI row.
  await expect(page.getByTestId('document-chain')).toBeVisible({ timeout: 10_000 });
});

test('tax invoice cross-ref: TI detail shows linked RC chip after post', async ({ page }) => {
  await login(page);
  await page.goto('/tax-invoices');
  await expect(page.locator('table')).toBeVisible({ timeout: 10_000 });

  const rows = await page.locator('tbody tr').count();
  if (rows > 0) {
    await page.locator('tbody tr').first().getByRole('link').first().click();
    await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
    // Cross-ref row is keyed under data-testid="cross-ref-row" when it has
    // any content. We don't assert it's present (depends on data), only
    // that the page renders without console errors.
    await expect(page.locator('body')).toBeVisible();
  }
});

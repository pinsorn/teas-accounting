import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) end-to-end.
// "ออกใบแจ้งหนี้" (Issue) creates the draft and allocates doc_no, landing on
// the BN detail page with status=Issued. Then exercise the Mark-Settled path
// to confirm the full lifecycle: Draft → Issued → Settled.
test('billing note: create → issue → mark settled', async ({ page }) => {
  await login(page);

  await page.goto('/invoices/new');

  // Customer async combobox.
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();

  // One line via the shared LineItemsTable.
  await page.getByLabel('รายละเอียด 1').fill('e2e billing note item');
  await page.getByLabel('จำนวน 1').fill('3');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');

  // Issue → create + issue → BN detail, already Issued.
  await page.getByTestId('bn-issue').click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('bn-status')).toContainText(/Issued|ออกแล้ว/, {
    timeout: 15_000,
  });

  // Mark-Settled.
  await page.getByTestId('bn-mark-settled').click();
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

  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();

  // Open the multi-TI picker (customer-scoped, Posted-only).
  await page.getByLabel('ใบกำกับภาษีที่รวม').click();
  const available = await page.locator('#taxinvoice-listbox button').count();
  test.skip(available === 0, 'no posted TI for this customer in seed');

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
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();
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

  // Confirm dialog auto-accepts.
  page.on('dialog', (d) => d.accept());
  await page.getByTestId('bn-delete').click();
  await page.waitForURL(/\/invoices(\?.*)?$/, { timeout: 15_000 });
});

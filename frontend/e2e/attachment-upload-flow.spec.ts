import { test, expect } from '@playwright/test';
import { login } from './_helpers';
import { TestIds } from './helpers/test-ids';

// Sprint 11 — upload → list → delete an attachment on a posted VI detail.
// Backend round-trip/validation is covered by the Api integration tests; this
// drives the polymorphic UI section end-to-end through the BFF proxy.
test('attachment upload + soft-delete on a Vendor Invoice', async ({ page }) => {
  // Unique per run — a previously failed run can leave an undeleted
  // "vendor-bill.pdf" row on the same first-VI detail (§14: no teardown),
  // which would break the toHaveCount(0) assertion below.
  const fileName = `vendor-bill-${TestIds.suffix()}.pdf`;
  await login(page);

  await page.goto('/vendor-invoices');
  // Open the first VI detail (the integration DB always has VIs from the
  // record-vendor-invoice gate spec). Scope to table rows so the "+ New"
  // link (/vendor-invoices/new) is not matched.
  await page.locator('table a[href^="/vendor-invoices/"]').first().click();
  await page.waitForURL(/\/vendor-invoices\/\d+$/, { timeout: 15_000 });

  const section = page.getByTestId('attachments-section');
  await expect(section).toBeVisible({ timeout: 15_000 });

  await page.getByTestId('att-upload-open').click();
  await page.getByTestId('att-file').setInputFiles({
    name: fileName,
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF'),
  });
  await page.getByTestId('att-category').selectOption('TAX_INVOICE');
  await page.getByTestId('att-desc').fill('e2e vendor bill');
  await page.getByTestId('att-submit').click();

  // Row appears + count ≥ 1.
  await expect(page.getByTestId('att-row').filter({ hasText: fileName }))
    .toBeVisible({ timeout: 15_000 });

  // Soft-delete. Redesign: window.confirm() was replaced by the useConfirm()
  // in-page alertdialog ("ยืนยันการทำรายการ" with ยกเลิก/ยืนยัน buttons).
  await page.getByTestId('att-row').filter({ hasText: fileName })
    .getByTestId('att-delete').click();
  await page.getByRole('alertdialog').getByRole('button', { name: 'ยืนยัน', exact: true }).click();
  await expect(page.getByTestId('att-row').filter({ hasText: fileName }))
    .toHaveCount(0, { timeout: 15_000 });

  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

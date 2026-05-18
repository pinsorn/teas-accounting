import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 11 — upload → list → delete an attachment on a posted VI detail.
// Backend round-trip/validation is covered by the Api integration tests; this
// drives the polymorphic UI section end-to-end through the BFF proxy.
test('attachment upload + soft-delete on a Vendor Invoice', async ({ page }) => {
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
    name: 'vendor-bill.pdf',
    mimeType: 'application/pdf',
    buffer: Buffer.from('%PDF-1.4\n1 0 obj<<>>endobj\ntrailer<<>>\n%%EOF'),
  });
  await page.getByTestId('att-category').selectOption('TAX_INVOICE');
  await page.getByTestId('att-desc').fill('e2e vendor bill');
  await page.getByTestId('att-submit').click();

  // Row appears + count ≥ 1.
  await expect(page.getByTestId('att-row').filter({ hasText: 'vendor-bill.pdf' }))
    .toBeVisible({ timeout: 15_000 });

  // Soft-delete (confirm dialog auto-accept).
  page.once('dialog', (d) => d.accept());
  await page.getByTestId('att-row').filter({ hasText: 'vendor-bill.pdf' })
    .getByTestId('att-delete').click();
  await expect(page.getByTestId('att-row').filter({ hasText: 'vendor-bill.pdf' }))
    .toHaveCount(0, { timeout: 15_000 });

  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong/i)).toHaveCount(0);
});

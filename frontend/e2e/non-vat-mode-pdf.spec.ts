import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice } from './_helpers';

// Sprint 8.5 — runs ONLY against a VatMode=false stack (Tax__VatMode=false on the
// API process). VatMode is process-global env, so this spec is executed in a
// dedicated second pass; the other 15 specs run against the normal VatMode=true
// stack. See Report-Backend11 §4 (e2e-harness mechanism note).
//
// Authoritative PDF-label correctness is covered by DocumentLabelsTests (unit,
// deterministic) + the manual ×8 inspection. Here we assert the cleanest
// VatMode=false observable: e-Tax CTA must NOT leak (ม.3 อัฏฐ — VAT-only), and
// the document is still issuable (PDF downloads).
test('non-VAT mode hides e-Tax CTA and still issues the document', async ({ page }) => {
  await login(page);
  await createAndPostTaxInvoice(page); // lands on /tax-invoices/{id}

  // e-Tax is VAT-registered only — XML + resend must be absent under VatMode=false.
  await expect(page.getByRole('button', { name: 'ดาวน์โหลด XML' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'ส่งอีเมลอีกครั้ง' })).toHaveCount(0);

  // The doc is still issuable — PDF button present and download succeeds.
  const pdfBtn = page.getByRole('button', { name: 'ดาวน์โหลด PDF' });
  await expect(pdfBtn).toBeVisible();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    pdfBtn.click(),
  ]);
  expect(await download.path()).toBeTruthy();
});

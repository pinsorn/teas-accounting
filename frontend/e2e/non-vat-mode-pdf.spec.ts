import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// Per-company VAT mode (2026-06-12) — VAT mode now lives on master.companies, so
// the old mechanism (a dedicated second pass with Tax__VatMode=false on the API
// process) is DEAD. Seed 440 provides a dedicated non-VAT tenant (company 3,
// user nonvat-admin / Admin@1234) and this spec runs FIRST-CLASS in the normal
// pass by logging into that company. Same instance serves VAT (company 1) and
// non-VAT (company 3) side by side.
//
// Authoritative PDF-label correctness is covered by DocumentLabelsTests (unit);
// here we assert the cleanest per-company non-VAT observables (ม.86 — only a
// VAT-registered company may issue ใบกำกับภาษี):
//  * ภ.พ.30 nav entry hidden (vatOnly filter, driven by /system/info vat_mode)
//  * a billing note still issues, with NO VAT row on the detail
//  * e-Tax CTA absent (ม.3 อัฏฐ — VAT-only)
//  * the document is still printable (PDF downloads)
test('non-VAT company: no VAT artifacts, billing note still issues', async ({ page }) => {
  test.setTimeout(90_000);
  await login(page, 'nonvat-admin');

  // ม.86 — ภ.พ.30 is VAT-only; the sidebar must not offer it to a non-VAT company.
  await expect(page.getByRole('link', { name: /ภ\.พ\.30/ })).toHaveCount(0);

  // Billing note (the non-VAT revenue path) still works end-to-end.
  await page.goto('/invoices/new');
  await pickCustomer(page, 'ลูกค้า', /ลูกค้านิติ/); // seed-440 company-3 customer
  await page.getByLabel('รายละเอียด 1').fill('e2e non-vat item');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByTestId('bn-issue').click();
  await page.waitForURL(/\/invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.getByTestId('bn-status')).toContainText(/Issued|ออกแล้ว/, { timeout: 15_000 });

  // No VAT row anywhere on the non-VAT document detail (single-Total summary).
  await expect(page.locator('main')).not.toContainText('ภาษีมูลค่าเพิ่ม');

  // e-Tax is VAT-registered only — XML + resend must be absent.
  await expect(page.getByRole('button', { name: 'ดาวน์โหลด XML' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'ส่งอีเมลอีกครั้ง' })).toHaveCount(0);

  // The document is still printable — PDF lives in the PrintMenu dropdown.
  await page.getByText('พิมพ์ / PDF', { exact: false }).first().click();
  const pdfBtn = page.getByRole('button', { name: /ดาวน์โหลด PDF/ }).first();
  await expect(pdfBtn).toBeVisible();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    pdfBtn.click(),
  ]);
  expect(await download.path()).toBeTruthy();
});

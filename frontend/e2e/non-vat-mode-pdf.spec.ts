import { test, expect } from '@playwright/test';
import { login, createAndPostTaxInvoice, detailDocNo } from './_helpers';

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
  test.setTimeout(90_000); // TI create+post alone can take ~25s post-redesign
  await login(page);
  await createAndPostTaxInvoice(page); // lands on /tax-invoices/{id}

  // Per the header this spec is only meaningful on a VatMode=false stack (the
  // dedicated second pass). On the normal VAT stack the e-Tax CTA legitimately
  // renders, so SKIP instead of failing (previously this "passed" only because
  // the async e-Tax section hadn't rendered when toHaveCount(0) sampled it).
  await detailDocNo(page, 'TI'); // waits until the detail data has rendered
  const vatStack = (await page.locator('main').innerText()).includes('ภาษีมูลค่าเพิ่ม 7%');
  test.skip(vatStack, 'VatMode=true stack — non-VAT assertions run only in the dedicated VatMode=false pass');

  // e-Tax is VAT-registered only — XML + resend must be absent under VatMode=false.
  await expect(page.getByRole('button', { name: 'ดาวน์โหลด XML' })).toHaveCount(0);
  await expect(page.getByRole('button', { name: 'ส่งอีเมลอีกครั้ง' })).toHaveCount(0);

  // The doc is still issuable — PDF lives in the PrintMenu dropdown now
  // (design swap): a <label class="btn">พิมพ์ / PDF</label> trigger with menu
  // item "ดาวน์โหลด PDF" (plain) or "ดาวน์โหลด PDF (สำเนา)" (tracked docs).
  await page.getByText('พิมพ์ / PDF', { exact: false }).first().click();
  const pdfBtn = page.getByRole('button', { name: /ดาวน์โหลด PDF/ }).first();
  await expect(pdfBtn).toBeVisible();
  const [download] = await Promise.all([
    page.waitForEvent('download'),
    pdfBtn.click(),
  ]);
  expect(await download.path()).toBeTruthy();
});

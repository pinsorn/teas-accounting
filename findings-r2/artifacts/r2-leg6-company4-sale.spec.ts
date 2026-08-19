import { test, expect } from '@playwright/test';
import { login, waitForNavGates } from './_helpers';

// R2 Leg6 item 1 (THROWAWAY — not part of the permanent suite) — round-1 leftover:
// company 4 (NV-ร้านนอนแวต2, non-VAT) never got an end-to-end sale walked through the
// real UI. Non-VAT companies use the billing note (ใบแจ้งหนี้) as their terminal
// revenue document — there is no tax invoice path (ม.86/4). This spec:
//  1. switches super-admin `admin` into company 4,
//  2. creates a customer (company 4 currently has zero customers),
//  3. attempts to issue one billing note end-to-end — discovered BLOCKED (L6-1: a
//     non-nullable backend DTO field vs. a null the FE always sends for non-VAT
//     companies); captured as evidence, not asserted as a pass,
//  4. confirms the UI refuses a Tax Invoice for this non-VAT company (nav hidden +
//     direct URL guarded) — this half DOES pass.
test('company 4 (non-VAT): attempt end-to-end sale (hits L6-1) + TI refusal', async ({ page }) => {
  test.setTimeout(90_000);
  await login(page, 'admin');

  // Switch to company 4 via the super-admin CompanySwitcher.
  await page.getByRole('button', { name: 'สลับบริษัท' }).click();
  await page.getByRole('button', { name: /NV-ร้านนอนแวต2/ }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
  await waitForNavGates(page);

  // Sanity: still non-VAT context (Topbar/company switched correctly).
  await expect(page.getByRole('link', { name: /ภ\.พ\.30/ })).toHaveCount(0);
  await expect(page.getByRole('link', { name: 'ใบกำกับภาษี' })).toHaveCount(0);

  // Company 4 has no customers yet — create one via the UI.
  const suffix = Date.now().toString().slice(-6);
  const custCode = `E2EC4${suffix}`;
  const custName = `ลูกค้า co4 e2e ${suffix}`;
  await page.goto('/customers/new');
  await page.getByLabel('รหัสลูกค้า').fill(custCode);
  await page.getByLabel('ชื่อ (ไทย)').fill(custName);
  // Uncheck VAT-registered (default true) so no tax id / branch code required.
  await page.getByRole('checkbox', { name: 'จดทะเบียน VAT' }).uncheck();
  await page.getByRole('button', { name: 'บันทึก' }).click();
  await page.waitForURL(/\/customers$/, { timeout: 15_000 });

  // Attempt to issue one billing note end-to-end referencing the new customer.
  // KNOWN BLOCKER (L6-1, filed in findings-leg6.md): BillingLineInput.TaxCodeId is a
  // non-nullable `int` server-side, but a non-VAT company's LineItemsTable never shows
  // the tax-rate picker (so taxCodeId/taxCode on every line stay null) — every billing
  // note create for a non-VAT company 400s. Confirmed via direct API bisection too.
  // This step is expected to FAIL — captured as evidence, not asserted as a pass.
  await page.goto('/invoices/new');
  await page.getByRole('button', { name: /^เลือกลูกค้า$|ค้นหาชื่อ หรือเลขผู้เสียภาษี/ }).first().click();
  const custDialog = page.getByRole('dialog');
  await custDialog.getByRole('textbox').fill(suffix);
  await custDialog.getByRole('button', { name: new RegExp(custName) }).click();
  await page.getByLabel('รายละเอียด 1').fill('e2e co4 item');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('500');
  await page.getByTestId('bn-issue').click();
  // Give the (failing) request a moment to settle, then capture whatever state results
  // — a toast (if any renders) plus a full-page screenshot as evidence for L6-1.
  await page.waitForTimeout(2000);
  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-1-company4-billing-note-400.png',
    fullPage: true,
  });
  const stillOnCreateForm = /\/invoices\/new$/.test(page.url());
  console.log(`R2LEG6 L6-1 billing-note create still-on-form=${stillOnCreateForm} url=${page.url()}`);

  // Direct-URL guard: a non-VAT company must not be able to reach the tax-invoice
  // create form even by typing the URL (nav already hides the link, checked above).
  await page.goto('/tax-invoices/new');
  await expect(page.getByText('ฟีเจอร์นี้ใช้ได้เฉพาะกิจการที่จดทะเบียน VAT', { exact: false }))
    .toBeVisible({ timeout: 10_000 });
  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-1-company4-ti-refused.png',
    fullPage: true,
  });
});

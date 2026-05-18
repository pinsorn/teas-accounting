import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 5 shippable critical path: create a vendor (master CRUD) and see it in
// the list. NOTE: Sana asked for record-vendor-invoice + payment-voucher-with-wht,
// but the VendorInvoice backend and PV approve/SoD are flagged & paused in
// Question-Backend5 (B1/B2) — those two specs need that backend and are NOT faked
// here. This exercises the unblocked subset end-to-end (vendor form + the
// gotcha-#2 nullable /vendors list binding).
test('record a vendor and see it in the list', async ({ page }) => {
  await login(page);

  // Unique code per run — integration DB has no teardown (runtime-gotchas §14).
  // Name must NOT contain the code: the list shows code AND name in separate
  // cells, so an embedded code makes a getByRole('cell',{name:code}) match two
  // cells (runtime-gotchas §5 — keep the assertion target unambiguous).
  const code = `E2EVEND-${Date.now().toString().slice(-7)}`;
  const name = 'ผู้ขายทดสอบอีทูอี';

  await page.goto('/vendors/new');
  await page.getByText(/รหัสผู้ขาย|Vendor code/).locator('xpath=following::input[1]').fill(code);
  await page.getByText(/^ชื่อ \(ไทย\)|Name \(Thai\)/).locator('xpath=following::input[1]').fill(name);

  await page.getByRole('button', { name: /บันทึกผู้ขาย|Save vendor/ }).click();

  await page.waitForURL(/\/vendors$/, { timeout: 15_000 });
  // The /vendors list is paginated (OrderBy VendorCode, Take pageSize) and the
  // integration DB has no teardown (runtime-gotchas §14): after many gate runs
  // the new E2EVEND-* row is off page 1. Filter by the unique code so the
  // assertion is robust to data accumulation (§14 family — Phase-2 cleanup).
  await page.getByPlaceholder(/ค้นหา|Search/i).fill(code);
  await expect(page.getByRole('cell', { name: code, exact: true }))
    .toBeVisible({ timeout: 15_000 });
});

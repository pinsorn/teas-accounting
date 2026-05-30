import { expect, type Page } from '@playwright/test';
import { TestIds } from './helpers/test-ids';

export async function login(page: Page, username = 'admin') {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill(username);
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();
  await page.waitForURL('**/', { timeout: 15_000 });
}

export async function logout(page: Page) {
  await page.getByRole('button', { name: /ออกจากระบบ|sign out/i }).click();
  await page.waitForURL('**/login', { timeout: 15_000 });
}

/**
 * Pick a customer in the CustomerSelector MODAL (EntityPickerModal). The selector
 * renders a trigger button whose accessible name (when empty) is the placeholder
 * text "ค้นหาชื่อ หรือเลขผู้เสียภาษี" (the ChevronDown icon is aria-hidden); clicking
 * it opens a role=dialog with a search input + a <ul> of result <button>s.
 */
export async function pickCustomer(page: Page, search = 'ลูกค้า', name: RegExp = /ลูกค้าทดสอบ/) {
  await page.getByRole('button', { name: 'ค้นหาชื่อ หรือเลขผู้เสียภาษี' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox').fill(search);
  await dialog.getByRole('button', { name }).click();
}

/** Create + post a Tax Invoice via the UI; returns its numeric id from the detail URL. */
export async function createAndPostTaxInvoice(page: Page): Promise<number> {
  await page.goto('/tax-invoices/new');
  // Customer is the CustomerSelector MODAL — open it via the trigger button, then
  // search + pick inside the dialog (replaced the old inline combobox/listbox).
  await pickCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  const m = page.url().match(/\/tax-invoices\/(\d+)$/);
  return Number(m![1]);
}

/** Create a vendor via the UI; returns its unique code. */
export async function createVendor(page: Page): Promise<string> {
  // §14 (resolved Sprint 14.5): random suffix via shared TestIds, was Date.now().
  const code = TestIds.vendorCode('E2EV');
  await page.goto('/vendors/new');
  await page.getByText(/รหัสผู้ขาย|Vendor code/).locator('xpath=following::input[1]').fill(code);
  await page.getByText(/^ชื่อ \(ไทย\)|Name \(Thai\)/).locator('xpath=following::input[1]')
    .fill('ผู้ขาย e2e จำกัด');
  await page.getByRole('button', { name: /บันทึกผู้ขาย|Save vendor/ }).click();
  await page.waitForURL(/\/vendors$/, { timeout: 15_000 });
  return code;
}

/**
 * Pick a vendor in the VendorSelector MODAL (EntityPickerModal) by its code/label
 * fragment. The selector is a trigger button whose accessible name (when empty) is
 * "ค้นหาชื่อ หรือรหัสผู้ขาย"; clicking it opens a role=dialog with a search input +
 * result <button>s. Search by the supplied fragment, then click the first result.
 */
export async function pickVendor(page: Page, query: string) {
  await page.getByRole('button', { name: 'ค้นหาชื่อ หรือรหัสผู้ขาย' }).click();
  const dialog = page.getByRole('dialog');
  await dialog.getByRole('textbox').fill(query);
  await dialog.getByRole('button', { name: query }).first().click();
}

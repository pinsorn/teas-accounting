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

/** Create + post a Tax Invoice via the UI; returns its numeric id from the detail URL. */
export async function createAndPostTaxInvoice(page: Page): Promise<number> {
  await page.goto('/tax-invoices/new');
  // Customer input is role=combobox; the Sprint-8 BU <select> is also a combobox,
  // so scope by the customer search placeholder to stay unambiguous.
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();
  await page.getByLabel('รายละเอียด 1').fill('e2e item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await dialog.getByRole('button', { name: /Confirm Post|ยืนยัน Post/i }).click();
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

/** Pick a vendor in the async VendorSelector combobox by its code/label fragment. */
export async function pickVendor(page: Page, query: string) {
  const box = page.getByRole('combobox').first();
  await box.click();
  await box.fill(query);
  await page.getByRole('listbox').getByRole('button').first().click();
}

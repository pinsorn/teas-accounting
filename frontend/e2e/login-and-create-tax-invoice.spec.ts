import { test, expect } from '@playwright/test';

// Happy path: login → create draft TI → Post (irreversible confirm) → detail.
// Backend demo seed provides company 1 + customer_id 1 (C-DEMO-001).
test('login then create and post a tax invoice', async ({ page }) => {
  await page.goto('/login');
  await page.getByRole('textbox', { name: /ชื่อผู้ใช้|username/i }).fill('admin');
  await page.locator('input[type="password"]').fill('Admin@1234');
  await page.getByRole('button', { name: /เข้าสู่ระบบ|sign in/i }).click();

  // Middleware lets us into the dashboard once the httpOnly cookie is set.
  await page.waitForURL('**/', { timeout: 15_000 });
  await expect(page).toHaveURL(/\/$/);

  await page.goto('/tax-invoices/new');

  // CustomerSelector: debounced async combobox → pick the demo-seed customer.
  // (Sprint-8 BU <select> is also role=combobox — scope by the search placeholder.)
  await page.getByPlaceholder('ค้นหาชื่อ หรือเลขผู้เสียภาษี').fill('ลูกค้า');
  await page.getByRole('listbox').getByRole('button', { name: /ลูกค้าทดสอบ/ }).click();

  // LineItemsTable: inputs are labelled "<field> <rowNo>".
  await page.getByLabel('รายละเอียด 1').fill('บริการทดสอบ e2e');
  await page.getByLabel('จำนวน 1').fill('2');
  await page.getByLabel('ราคา/หน่วย 1').fill('500');

  // Post → confirm dialog (irreversible warning) → confirm.
  await page.getByRole('button', { name: /^Post|บันทึกเอกสาร/ }).click();
  const dialog = page.getByRole('dialog');
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText(/irreversible|ไม่สามารถแก้ไข/i);
  await dialog.getByRole('button', { name: /Confirm Post|ยืนยัน Post/i }).click();

  // Lands on the detail page with an allocated doc number (MM-YYYY-TI-NNNN).
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  await expect(page.locator('body')).toContainText(/-TI-\d{4}/);
});

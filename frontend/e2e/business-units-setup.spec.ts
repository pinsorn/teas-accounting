import { test, expect } from '@playwright/test';
import { login } from './_helpers';
import { TestIds } from './helpers/test-ids';

// Sprint 8 — create a Business Unit through the settings UI and see it listed.
// Sprint 14.5 — §14 fix: random code per run (was Date-based, low entropy).
test('create a business unit in settings', async ({ page }) => {
  await login(page);

  const code = TestIds.businessUnitCode();
  await page.goto('/settings/business-units');

  // open the create modal
  await page.getByRole('button', { name: /เพิ่มหน่วยธุรกิจ|Add Business Unit/ }).click();
  const modal = page.getByRole('dialog');
  await expect(modal).toBeVisible();

  // code input is the field after the "รหัส *" label; nameTh after "ชื่อ (ไทย) *"
  await modal.getByText(/^รหัส \*/).locator('xpath=following::input[1]').fill(code);
  await modal.getByText(/^ชื่อ \(ไทย\) \*/).locator('xpath=following::input[1]')
    .fill('สายธุรกิจ e2e');

  await modal.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();

  // row shows in the (includeInactive) table
  await expect(page.locator('table')).toContainText(code);
});

import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 8.6 — WHT-type master: super-admin creates a type, then changes its
// rate with an effective date (closed/open row pair).
test('wht-type: create + effective-date rate change', async ({ page }) => {
  await login(page);
  const code = `EVT${Date.now().toString().slice(-6)}`;

  await page.goto('/settings/wht-types');
  await page.getByRole('button', { name: /เพิ่มประเภท|Add type/ }).click();
  const modal = page.getByRole('dialog');
  await expect(modal).toBeVisible();
  await modal.getByText(/^รหัส \*/).locator('xpath=following::input[1]').fill(code);
  await modal.getByText(/^ชื่อ \(ไทย\) \*/).locator('xpath=following::input[1]').fill('อีเวนต์ e2e');
  await modal.getByText(/ม\.40|s\.40/).locator('xpath=following::input[1]').fill('3');
  await modal.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
  await expect(page.locator('table')).toContainText(code);

  // Change its rate effective a future date — a new open row is added.
  await page.getByRole('row', { name: new RegExp(code) })
    .getByRole('button', { name: /เปลี่ยนอัตรา|Change rate/ }).click();
  const rateModal = page.getByRole('dialog');
  await expect(rateModal).toBeVisible();
  await rateModal.getByText(/อัตราใหม่|New rate/).locator('xpath=following::input[1]').fill('4');
  await rateModal.locator('input[type="date"]').fill('2026-06-01');
  await rateModal.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();

  // Two rows for the code now: the closed one + the new open one.
  await expect(page.getByRole('row', { name: new RegExp(code) })).toHaveCount(2);
});

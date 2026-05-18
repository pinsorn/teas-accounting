import { test, expect } from '@playwright/test';
import { login } from './_helpers';

// Sprint 10 Part A — create a Product through the settings UI and see it listed.
test('create a product in settings', async ({ page }) => {
  await login(page);

  const code = `E2EP${Date.now().toString().slice(-6)}`;
  await page.goto('/settings/products');

  await page.getByRole('button', { name: /เพิ่มสินค้า\/บริการ|New product/ }).click();
  const modal = page.locator('.modal-box');
  await expect(modal).toBeVisible();

  await modal.getByText(/^รหัส \(SKU\)$|^Code \(SKU\)$/)
    .locator('xpath=following::input[1]').fill(code);
  await modal.getByText(/^ชื่อ \(ไทย\)$|^Name \(TH\)$/)
    .locator('xpath=following::input[1]').fill('บริการ e2e');

  await modal.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();

  await expect(page.locator('table')).toContainText(code);
});

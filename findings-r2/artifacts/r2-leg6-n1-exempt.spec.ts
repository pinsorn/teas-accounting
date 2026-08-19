import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// R2 Leg6 item 2 (THROWAWAY) — N1 live re-verify (fix commit 047fe95, never seen
// live through a real browser): an EXEMPT_GOOD product picked into a sales line
// must show 0% VAT on screen and resolve to an exempt tax code server-side,
// through BOTH doors — create, and edit+re-save of the same draft (Aug-16 line
// hydration bug 24623/24657 made re-save a real regression risk for this exact
// scenario: productType/taxCodeId used to get dropped on the edit round-trip).
test('N1: exempt product locks 0% VAT on create and survives edit-resave', async ({ page }) => {
  test.setTimeout(90_000);
  await login(page, 'admin'); // company 1 (Demo, VAT)

  // Company 1 has zero products today — create a fresh EXEMPT_GOOD one.
  const code = `E2EEXM${Date.now().toString().slice(-6)}`;
  const name = `สินค้ายกเว้นภาษี e2e ${Date.now().toString().slice(-4)}`;
  await page.goto('/settings/products');
  await page.getByRole('button', { name: /เพิ่มสินค้า\/บริการ/ }).click();
  const pmodal = page.locator('.modal-box');
  await expect(pmodal).toBeVisible();
  await pmodal.getByText(/^รหัส \(SKU\)$/).locator('xpath=following::input[1]').fill(code);
  await pmodal.getByText(/^ชื่อ \(ไทย\)$/).locator('xpath=following::input[1]').fill(name);
  await pmodal.getByText(/^ประเภท$/).locator('xpath=following::select[1]').selectOption('EXEMPT_GOOD');
  await pmodal.getByRole('button', { name: /^บันทึก$/ }).click();
  await expect(page.locator('table')).toContainText(code, { timeout: 10_000 });

  // Create a quotation, picking the exempt product into line 1.
  await page.goto('/quotations/new');
  // Default pickCustomer regex (/ลูกค้าทดสอบ/) is now ambiguous — company 1 has both
  // "ลูกค้าทดสอบ จำกัด" and "บริษัท SALES ลูกค้าทดสอบ จำกัด" — anchor to the exact name.
  await pickCustomer(page, 'ลูกค้าทดสอบ', /^ลูกค้าทดสอบ จำกัด/);
  await page.getByRole('button', { name: 'เลือกจากรายการ' }).first().click();
  const pdialog = page.getByRole('dialog');
  await expect(pdialog).toBeVisible();
  await pdialog.getByRole('textbox', { name: 'ค้นหาด้วยชื่อ หรือรหัสสินค้า' }).fill(name);
  await pdialog.getByRole('button', { name: new RegExp(name) }).click();
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('100');

  // Screen must show the locked 0% VAT badge for this exempt line.
  const badge = page.locator('span[title="อัตราภาษีล็อกตามสินค้า/บริการที่เลือก"]');
  await expect(badge).toHaveText('0%');
  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-2-n1-create-0pct.png',
    fullPage: true,
  });

  await page.getByRole('button', { name: /Save Draft|บันทึกร่าง/i }).click();
  await page.waitForURL(/\/quotations(\?.*)?$/, { timeout: 15_000 });

  // Find the newly-created draft (top of list, newest first) and open it.
  await page.locator('tbody tr').first().getByRole('link').first().click();
  await page.waitForURL(/\/quotations\/(\d+)$/, { timeout: 15_000 });
  const qid = Number(page.url().match(/\/quotations\/(\d+)$/)![1]);
  console.log(`R2LEG6 N1 quotation_id(create)=${qid}`);

  // Door-1 evidence: read the stored line straight from the API right after create,
  // BEFORE any edit, so door 1 and door 2 are provably separate checks.
  const afterCreate = await page.request.get(`/api/proxy/quotations/${qid}`);
  const afterCreateBody = await afterCreate.json();
  console.log(`R2LEG6 N1 door1(after-create) line0=${JSON.stringify(afterCreateBody.lines?.[0])}`);

  // Door 2: edit + re-save without changing anything — the pair must survive.
  await page.getByTestId('q-edit').click();
  await page.waitForURL(/\/quotations\/\d+\/edit$/, { timeout: 15_000 });
  const badge2 = page.locator('span[title="อัตราภาษีล็อกตามสินค้า/บริการที่เลือก"]');
  await expect(badge2).toHaveText('0%', { timeout: 10_000 });
  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-2-n1-edit-0pct.png',
    fullPage: true,
  });
  await page.getByRole('button', { name: /^บันทึก$/ }).click();
  await page.waitForURL(new RegExp(`/quotations/${qid}$`), { timeout: 15_000 });

  console.log(`R2LEG6 N1 product_code=${code} quotation_id(final)=${qid}`);
});

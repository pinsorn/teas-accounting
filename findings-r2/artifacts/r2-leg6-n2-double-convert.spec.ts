import { test, expect } from '@playwright/test';
import { login, pickCustomer } from './_helpers';

// R2 Leg6 item 3 (THROWAWAY) — N2 live re-verify: convert an accepted quotation to
// a Tax Invoice, POST that TI, then attempt to convert the SAME quotation again.
// Expected: refused with a typed `quotation.already_invoiced` error surfaced sanely
// in the UI (a toast, not a raw 500/blank page), and exactly one Posted TI ends up
// referencing the quotation_id (verified separately via DB).
test('N2: converting an already-invoiced quotation a second time is refused sanely', async ({ page }) => {
  test.setTimeout(90_000);
  await login(page, 'admin');

  await page.goto('/quotations/new');
  // Default pickCustomer regex (/ลูกค้าทดสอบ/) is now ambiguous — company 1 has both
  // "ลูกค้าทดสอบ จำกัด" and "บริษัท SALES ลูกค้าทดสอบ จำกัด" — anchor to the exact name.
  await pickCustomer(page, 'ลูกค้าทดสอบ', /^ลูกค้าทดสอบ จำกัด/);
  await page.getByLabel('รายละเอียด 1').fill('e2e N2 double-convert item');
  await page.getByLabel('จำนวน 1').fill('1');
  await page.getByLabel('ราคา/หน่วย 1').fill('1000');

  // Issue → create + send → quotation detail, already Sent.
  await page.getByRole('button', { name: /ออกใบเสนอราคา/ }).click();
  await page.waitForURL(/\/quotations\/\d+$/, { timeout: 15_000 });
  const qid = Number(page.url().match(/\/quotations\/(\d+)$/)![1]);
  console.log(`R2LEG6 N2 quotation_id=${qid}`);
  await expect(page.getByTestId('q-status')).toContainText(/Sent|ส่งแล้ว/, { timeout: 15_000 });

  // S11 added a ConfirmActionDialog gate on accept/reject — click accept, then confirm.
  await page.getByTestId('q-accept').click();
  await page.getByRole('dialog').locator('button.btn-warning').click();
  await expect(page.getByTestId('q-status')).toContainText(/Accepted|ตอบรับแล้ว/, { timeout: 15_000 });

  // First conversion: create the draft TI, then POST it.
  await page.getByTestId('q-create-ti').click();
  await page.waitForURL(/\/tax-invoices\/\d+$/, { timeout: 15_000 });
  const tiId = Number(page.url().match(/\/tax-invoices\/(\d+)$/)![1]);
  console.log(`R2LEG6 N2 tax_invoice_id(first)=${tiId}`);

  await page.getByTestId('ti-post-action').click();
  const postDialog = page.getByRole('dialog');
  await expect(postDialog).toBeVisible();
  await postDialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
  // Posting closes the dialog and the draft-only post CTA disappears once Posted.
  await expect(page.getByTestId('ti-post-action')).toHaveCount(0, { timeout: 15_000 });

  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-3-n2-first-ti-posted.png',
    fullPage: true,
  });

  // Second attempt: go back to the SAME quotation, click convert again.
  await page.goto(`/quotations/${qid}`);
  await page.getByTestId('q-create-ti').click();

  // Expect a sane, typed refusal — a toast, no crash / raw error page — and NO
  // navigation away from the quotation detail (the create call must have failed).
  await expect(page.getByText(/เกิดข้อผิดพลาด|Something went wrong|Application error/i)).toHaveCount(0);
  // sonner toast renders in a [data-sonner-toast] region; assert some error surfaced.
  const toast = page.locator('[data-sonner-toast]').last();
  await expect(toast).toBeVisible({ timeout: 10_000 });
  const toastText = await toast.innerText();
  console.log(`R2LEG6 N2 second-attempt toast="${toastText}"`);
  await page.screenshot({
    path: 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad\\l6-3-n2-second-attempt-toast.png',
    fullPage: true,
  });

  // Still on the quotation detail page — no navigation happened on failure.
  expect(page.url()).toMatch(new RegExp(`/quotations/${qid}$`));

  console.log(`R2LEG6 N2 SUMMARY qid=${qid} first_ti_id=${tiId}`);
});

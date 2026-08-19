import { test, expect } from '@playwright/test';
import { writeFileSync } from 'node:fs';
import { login } from './_helpers';

// Round-2 testing swarm, Leg 1 (Payroll) — ad-hoc scratch spec, NOT committed.
// Walks: employee creation (UI), the draft->approve->post->pay lifecycle for the
// existing API-seeded run (id 2, period 202607), the deduction "edit door" idempotency
// check, and the SSO/PND1 filing-artifact buttons. Screenshots on anomalies go to the
// session scratchpad.
const SHOT_DIR = 'Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/5667c374-e2c0-4998-b10c-b993b4182367/scratchpad';

test.describe.serial('Leg1 payroll browser cycle', () => {
  test('create employee via UI', async ({ page }) => {
    await login(page);
    await page.goto('/settings/employees');
    await page.getByRole('button', { name: 'เพิ่มพนักงาน' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    await dialog.getByLabel('รหัสพนักงาน *').fill('L1EMPG');
    await dialog.getByLabel('เลขบัตรประชาชน *').fill('1100000000017');
    await dialog.getByLabel('คำนำหน้า').fill('นาย');
    await dialog.getByLabel('ชื่อ *').fill('บราวเซอร์');
    await dialog.getByLabel('นามสกุล *').fill('ทดสอบจี');
    await dialog.getByLabel('เงินเดือน *').fill('35000');
    await dialog.getByLabel('วันเริ่มงาน *').fill('2020-01-01');
    await dialog.getByRole('button', { name: 'บันทึก' }).click();
    await expect(page.getByText('L1EMPG')).toBeVisible({ timeout: 10_000 });
    await page.screenshot({ path: `${SHOT_DIR}/leg1-01-employee-created.png`, fullPage: true });
  });

  test('draft run 2 shows correct totals on screen', async ({ page }) => {
    await login(page);
    await page.goto('/payroll');
    await expect(page.getByRole('link', { name: /07\/2026/ }).first()).toBeVisible({ timeout: 10_000 });
    await page.screenshot({ path: `${SHOT_DIR}/leg1-02-payroll-list.png`, fullPage: true });
    await page.getByRole('link', { name: /07\/2026/ }).first().click();
    await expect(page).toHaveURL(/\/payroll\/\d+$/);
    // totals from run 2 (verified independently against ThaiPitCalculator by hand + DB earlier)
    await expect(page.locator('body')).toContainText('363,225.81');
    await expect(page.locator('body')).toContainText('20,102.08');
    await expect(page.locator('body')).toContainText('338,748.73');
    await page.screenshot({ path: `${SHOT_DIR}/leg1-03-run-detail-draft.png`, fullPage: true });
  });

  test('deduction edit door: apply once, reopen+resave unchanged -> DB idempotent', async ({ page }) => {
    await login(page);
    await page.goto('/payroll');
    await page.getByRole('link', { name: /07\/2026/ }).first().click();
    await expect(page).toHaveURL(/\/payroll\/\d+$/);

    // Apply a real deduction to L1EMPA2's row (5000, with a reason).
    const empRow = page.locator('tr', { hasText: 'L1EMPA2' });
    await empRow.getByRole('spinbutton', { name: /รายการหัก .*สมชาย/ }).fill('5000');
    await empRow.getByRole('textbox', { name: /เหตุผลรายการหัก .*สมชาย/ }).fill('ทดสอบหักเงิน L1-edit-door');
    await page.getByRole('button', { name: 'บันทึกรายการหัก' }).click();
    await expect(page.getByText('บันทึกรายการหักแล้ว')).toBeVisible({ timeout: 10_000 });
    // net for A2 should now read 30000 - 875 - 5000 = 24125.00
    await expect(page.locator('body')).toContainText('24,125.00');
    await page.screenshot({ path: `${SHOT_DIR}/leg1-04-deduction-applied.png`, fullPage: true });

    // Reopen: reload the page, DO NOT change any field, resave.
    await page.reload();
    await expect(page.locator('body')).toContainText('24,125.00');
    await page.getByRole('button', { name: 'บันทึกรายการหัก' }).click();
    await expect(page.getByText('บันทึกรายการหักแล้ว')).toBeVisible({ timeout: 10_000 });
    await page.screenshot({ path: `${SHOT_DIR}/leg1-05-deduction-resaved-unchanged.png`, fullPage: true });
  });

  test('approve -> post -> pay through the UI, plus filing-artifact buttons', async ({ page }) => {
    await login(page);
    await page.goto('/payroll');
    await page.getByRole('link', { name: /07\/2026/ }).first().click();
    await expect(page).toHaveURL(/\/payroll\/\d+$/);

    // Idempotent under re-runs: approve only if still Draft (an earlier partial run of this
    // same spec already approved run 2 with a ฿5,000 deduction on L1EMPA2 applied+resaved —
    // evidenced in leg1-04/05/06 screenshots).
    const approveBtn = page.getByRole('button', { name: 'อนุมัติ' });
    if (await approveBtn.isVisible().catch(() => false)) {
      await approveBtn.click();
      await expect(page.getByText('อนุมัติแล้ว')).toBeVisible({ timeout: 10_000 });
    }
    await page.screenshot({ path: `${SHOT_DIR}/leg1-06-approved.png`, fullPage: true });

    // Post (confirm dialog) — idempotent under re-runs (already posted in a prior partial run,
    // docNo 07-2026-PR-0001 / journalId 11, confirmed via psql before this run).
    const postBtn = page.getByRole('button', { name: 'บันทึกบัญชี' });
    if (await postBtn.isVisible().catch(() => false)) {
      await postBtn.click();
      const confirmDlg = page.getByRole('alertdialog').filter({ hasText: 'ยืนยันบันทึกบัญชี' });
      await expect(confirmDlg).toBeVisible({ timeout: 5_000 });
      await confirmDlg.getByRole('button', { name: 'ยืนยัน' }).click();
      await expect(page.getByText('บันทึกบัญชีแล้ว')).toBeVisible({ timeout: 15_000 });
    }
    await page.screenshot({ path: `${SHOT_DIR}/leg1-07-posted.png`, fullPage: true });

    // SSO file button — company profile has NO SsoEmployerAccountNo configured, so this
    // should refuse with the sso_batch.missing_employer_account typed error (not a raw 500).
    const [ssoResp] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/sso/file'), { timeout: 15_000 }),
      page.getByRole('button', { name: 'สปส.1-10 (ไฟล์)' }).first().click(),
    ]);
    // capture status + body for the findings record regardless of outcome
      const ssoStatus = ssoResp.status();
      const ssoBody = await ssoResp.text().catch(() => '<binary>');
      writeFileSync(`${SHOT_DIR}/leg1-sso-file-response.json`,
        JSON.stringify({ status: ssoStatus, body: ssoBody.slice(0, 2000) }, null, 2));
    await page.screenshot({ path: `${SHOT_DIR}/leg1-08-sso-file-clicked.png`, fullPage: true });

    // PND1 PDF button — capture bytes for later tax-id inspection.
    const [pndResp] = await Promise.all([
      page.waitForResponse((r) => r.url().includes('/pnd1/pdf'), { timeout: 15_000 }),
      page.getByRole('button', { name: 'ภ.ง.ด.1 (PDF)' }).first().click(),
    ]);
    const pndStatus = pndResp.status();
    if (pndStatus === 200) {
      const buf = await pndResp.body();
      writeFileSync(`${SHOT_DIR}/leg1-pnd1.pdf`, buf);
    } else {
      const body = await pndResp.text().catch(() => '<binary>');
      writeFileSync(`${SHOT_DIR}/leg1-pnd1-error.json`,
        JSON.stringify({ status: pndStatus, body: body.slice(0, 2000) }, null, 2));
    }
    await page.screenshot({ path: `${SHOT_DIR}/leg1-09-pnd1-clicked.png`, fullPage: true });

    // Pay — idempotent under re-runs.
    const payBtn = page.getByRole('button', { name: 'จ่ายแล้ว' });
    if (await payBtn.isVisible().catch(() => false)) {
      await payBtn.click();
      const payDlg = page.getByRole('dialog').filter({ hasText: 'ยืนยันว่าได้โอนเงินเดือน' });
      await expect(payDlg).toBeVisible({ timeout: 5_000 });
      await payDlg.getByRole('button', { name: 'ยืนยันการจ่าย' }).click();
      await expect(page.getByText('บันทึกการจ่ายแล้ว')).toBeVisible({ timeout: 15_000 });
    }
    await page.screenshot({ path: `${SHOT_DIR}/leg1-10-paid.png`, fullPage: true });
  });

  test('fetch pnd1 PDF bytes directly via authenticated session (post-click capture came back empty)', async ({ page }) => {
    await login(page);
    const res = await page.request.get('/api/proxy/payroll/runs/2/pnd1/pdf');
    writeFileSync(`${SHOT_DIR}/leg1-pnd1-status.txt`, String(res.status()));
    if (res.ok()) {
      const buf = await res.body();
      writeFileSync(`${SHOT_DIR}/leg1-pnd1-v2.pdf`, buf);
    } else {
      writeFileSync(`${SHOT_DIR}/leg1-pnd1-v2-error.json`, await res.text());
    }
  });

  test('new draft run for closed period 202608 (turned out to be current-month auto-open, see findings)', async ({ page }) => {
    await login(page);
    await page.goto('/payroll');
    await page.getByRole('button', { name: 'สร้างรอบจ่าย' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    const periodInput = dialog.getByLabel('งวดเดือน *');
    await periodInput.fill('');
    await periodInput.fill('202608');
    await dialog.getByLabel('วันที่จ่าย *').fill('2026-08-20');
    await dialog.getByRole('button', { name: 'สร้างรอบจ่าย' }).click();
    await expect(page.getByText('สร้างรอบจ่ายแล้ว')).toBeVisible({ timeout: 10_000 });
    await page.screenshot({ path: `${SHOT_DIR}/leg1-11-run-202608-created.png`, fullPage: true });

    // open it, approve, then attempt post -> expect a toast (typed error), run stays Approved.
    await page.goto('/payroll');
    await page.getByRole('link', { name: /08\/2026/ }).first().click();
    await expect(page).toHaveURL(/\/payroll\/\d+$/);
    await page.getByRole('button', { name: 'อนุมัติ' }).click();
    await expect(page.getByText('อนุมัติแล้ว')).toBeVisible({ timeout: 10_000 });

    await page.getByRole('button', { name: 'บันทึกบัญชี' }).click();
    const confirmDlg = page.getByRole('alertdialog').filter({ hasText: 'ยืนยันบันทึกบัญชี' });
    await expect(confirmDlg).toBeVisible({ timeout: 5_000 });
    await confirmDlg.getByRole('button', { name: 'ยืนยัน' }).click();
    // Expect an error toast, NOT a "posted" success toast, NOT a raw crash/blank page.
    await page.waitForTimeout(3000);
    await page.screenshot({ path: `${SHOT_DIR}/leg1-12-post-closed-period-result.png`, fullPage: true });
  });

  test('genuinely future/closed period 202609: draft, approve, post refuses with typed error', async ({ page }) => {
    await login(page);
    await page.goto('/payroll');
    await page.getByRole('button', { name: 'สร้างรอบจ่าย' }).click();
    const dialog = page.getByRole('dialog');
    await expect(dialog).toBeVisible();
    const periodInput = dialog.getByLabel('งวดเดือน *');
    await periodInput.fill('');
    await periodInput.fill('202609');
    await dialog.getByLabel('วันที่จ่าย *').fill('2026-09-15');
    await dialog.getByRole('button', { name: 'สร้างรอบจ่าย' }).click();
    await expect(page.getByText('สร้างรอบจ่ายแล้ว')).toBeVisible({ timeout: 10_000 });
    await page.screenshot({ path: `${SHOT_DIR}/leg1-13-run-202609-created.png`, fullPage: true });

    await page.goto('/payroll');
    await page.getByRole('link', { name: /09\/2026/ }).first().click();
    await expect(page).toHaveURL(/\/payroll\/\d+$/);
    await page.getByRole('button', { name: 'อนุมัติ' }).click();
    await expect(page.getByText('อนุมัติแล้ว')).toBeVisible({ timeout: 10_000 });

    await page.getByRole('button', { name: 'บันทึกบัญชี' }).click();
    const confirmDlg = page.getByRole('alertdialog').filter({ hasText: 'ยืนยันบันทึกบัญชี' });
    await expect(confirmDlg).toBeVisible({ timeout: 5_000 });
    await confirmDlg.getByRole('button', { name: 'ยืนยัน' }).click();
    await page.waitForTimeout(3000);
    await page.screenshot({ path: `${SHOT_DIR}/leg1-14-post-future-period-result.png`, fullPage: true });
    // The status badge must NOT read "บันทึกบัญชีแล้ว" (Posted) -- it must still say Approved.
    const stillApproved = await page.getByText('บันทึกบัญชีแล้ว').isVisible().catch(() => false);
    writeFileSync(`${SHOT_DIR}/leg1-202609-still-posted-badge-visible.txt`, String(stillApproved));
  });
});

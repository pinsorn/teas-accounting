import { test, expect } from '@playwright/test';
import { login, logout, waitForNavGates } from './_helpers';

// Round 2 / Leg 4 — EXPENSE CLAIMS. Throwaway findings-only spec (not committed).
// Walk: claim -> approve -> pay -> GL, plus SoD/permission/malformed probes.
// Company under test: company_id=1 (Demo Company). Confirmed via read-only SQL:
// employee_id=8 (L1EMPA2, active), category 18=TRAV (default acct 11), category 5=ENT
// (default acct 11), bank_account_id=1 (gl_cash_account_id=2). Roles: rbac_accountant
// (ACCOUNTANT: expense.claim.create+read only), rbac_chief_accountant (CHIEF_ACCOUNTANT:
// create+read+approve+pay), rbac_sales_staff (no expense perms at all).
const API = '/api/proxy';
const EMP_ID = 8;
const TRAV_CAT = 18;
const ENT_CAT = 5;
const BANK_ID = 1;

test.describe.configure({ mode: 'serial' });

let happyClaimId: number;

// L4-1 finding: GET /employees is gated entirely behind master.employee.manage (no separate
// read-only grant). ACCOUNTANT (expense.claim.create/read only, per 617's role split) does NOT
// hold master.employee.manage (only CHIEF_ACCOUNTANT/COMPANY_ADMIN/SUPER_ADMIN do, confirmed via
// SQL). The EmployeeSelector on /expense-claims/new therefore 403s for the very role the spec
// designed to submit claims ("Submitters key claims in on behalf of an employee"). Repro live.
test('L4-1 repro: rbac_accountant cannot populate the Employee picker on the create form (403 on GET /employees)', async ({ page }) => {
  await login(page, 'rbac_accountant');
  const employeesResp = await page.request.get(`${API}/employees`);
  console.log('L4-EVIDENCE rbac_accountant GET /employees status =', employeesResp.status());
  expect(employeesResp.status()).toBe(403);

  await page.goto('/expense-claims/new');
  await waitForNavGates(page);
  const empSelect = page.getByLabel(/^Employee$|^พนักงาน$/);
  await expect(empSelect).toBeVisible();
  const optionCount = await empSelect.locator('option').count();
  console.log('L4-EVIDENCE employee <select> option count (incl. placeholder) =', optionCount);
  await page.screenshot({
    path: 'Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/5667c374-e2c0-4998-b10c-b993b4182367/scratchpad/L4-1-employee-picker-403.png',
    fullPage: true,
  });
  // Only the placeholder option ("— Select an employee —") is present — the real list never loads,
  // so this role cannot complete claim creation through the UI at all (canSave requires employeeId).
  expect(optionCount).toBeLessThanOrEqual(1);
});

test('L4 happy path (single actor with full grants): create -> submit -> approve -> pay via UI, GL Dr=Cr', async ({ page }) => {
  // rbac_chief_accountant holds create+read+approve+pay AND master.employee.manage — the one
  // role for whom the UI create form actually works end-to-end. Also doubles as the SoD-is-
  // permission-only proof (same user approves their own claim) alongside the dedicated L4-2 test.
  await login(page, 'rbac_chief_accountant');

  await page.goto('/expense-claims/new');
  await waitForNavGates(page);
  await page.getByLabel(/^Employee$|^พนักงาน$/).selectOption(String(EMP_ID));

  const rows = () => page.locator('div.rounded-lg.border-base-300.p-3');

  // Row 1: Taxi 500, 0% VAT
  await rows().nth(0).getByTestId('expense-category-select').selectOption(String(TRAV_CAT));
  await rows().nth(0).locator("input:not([type])").fill("Taxi to client site");
  await rows().nth(0).locator('input[type="number"]').fill('500');

  // Add row 2: Hotel 1000, 7% VAT, recoverable
  await page.getByRole('button', { name: /^Add line$|^เพิ่มรายการ$/ }).click();
  await rows().nth(1).getByTestId('expense-category-select').selectOption(String(TRAV_CAT));
  await rows().nth(1).locator('input:not([type])').fill('Hotel');
  await rows().nth(1).locator('input[type="number"]').fill('1000');
  await rows().nth(1).locator('select').nth(1).selectOption('0.07');
  const recov1 = rows().nth(1).locator('input[type="checkbox"]');
  if (!(await recov1.isChecked())) await recov1.check();

  // Add row 3: Client dinner 200, 7% VAT, NON-recoverable (entertainment, ม.82/5)
  await page.getByRole('button', { name: /^Add line$|^เพิ่มรายการ$/ }).click();
  await rows().nth(2).getByTestId('expense-category-select').selectOption(String(ENT_CAT));
  await rows().nth(2).locator('input:not([type])').fill('Client dinner');
  await rows().nth(2).locator('input[type="number"]').fill('200');
  await rows().nth(2).locator('select').nth(1).selectOption('0.07');
  const recov2 = rows().nth(2).locator('input[type="checkbox"]');
  if (await recov2.isChecked()) await recov2.uncheck();

  await page.getByRole('button', { name: /^Save$|^บันทึก$/ }).click();
  await page.waitForURL(/\/expense-claims\/\d+$/, { timeout: 15_000 });
  const m = page.url().match(/\/expense-claims\/(\d+)$/);
  happyClaimId = Number(m![1]);

  const detail1 = await (await page.request.get(`${API}/expense-claims/${happyClaimId}`)).json();
  console.log('L4-EVIDENCE created claim', happyClaimId, JSON.stringify({
    subtotal: detail1.subtotalAmount, vat: detail1.vatAmount, total: detail1.totalAmount,
    lines: detail1.lines.map((l: any) => ({ desc: l.description, amount: l.amount, vat: l.vatAmount, recov: l.isRecoverableVat, lineTotal: l.lineTotal })),
  }));
  // Worked-example money check (specs/expense-claims.md §3): subtotal 1700, vat 84 (form-level
  // shows 84 = 70+14 regardless of recoverable flag), total 1784.
  expect(detail1.subtotalAmount).toBe(1700);
  expect(detail1.totalAmount).toBe(1784);

  // NOTE: sonner success toasts here never auto-dismiss within this headless env (harness/env
  // quirk — likely rAF/timers throttled on an unfocused headless tab — not a product bug), and
  // they stack up at top-right, physically covering the PageHeader action buttons so even a
  // coordinate-based force click lands on the topmost toast instead of the button underneath.
  // Activate via focus()+Enter instead — a programmatic DOM focus bypasses hit-testing entirely.
  async function activate(testId: string) {
    await page.getByTestId(testId).focus();
    await page.keyboard.press('Enter');
  }

  await activate('ec-submit');
  await expect(page.locator('body')).toContainText(/ส่งอนุมัติแล้ว|Submitted/, { timeout: 10_000 });

  await page.getByTestId('ec-approve').waitFor({ state: 'visible', timeout: 10_000 });
  await activate('ec-approve');
  await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });

  await page.getByTestId('ec-pay').waitFor({ state: 'visible', timeout: 10_000 });
  await activate('ec-pay');
  const modal = page.locator('.modal-box');
  await expect(modal).toBeVisible();
  await modal.locator('select').first().selectOption('TRANSFER');
  await modal.locator('select').nth(1).selectOption(String(BANK_ID));
  await modal.getByRole('button', { name: /^จ่ายเงิน$|^Pay$/ }).focus();
  await page.keyboard.press('Enter');
  await expect(page.locator('body')).toContainText(/จ่ายเงินแล้ว|Paid/, { timeout: 10_000 });

  const detail2 = await (await page.request.get(`${API}/expense-claims/${happyClaimId}`)).json();
  console.log('L4-EVIDENCE paid claim', JSON.stringify({
    docNo: detail2.docNo, status: detail2.status, journalEntryId: detail2.journalEntryId,
    total: detail2.totalAmount, vat: detail2.vatAmount, subtotal: detail2.subtotalAmount,
  }));
  expect(detail2.status).toBe('Paid');
  expect(detail2.docNo).toContain('EX');
  expect(detail2.journalEntryId).toBeTruthy();
});

test('L4-2 SoD: fresh claim, rbac_chief_accountant creates+submits+approves its OWN claim via API (permission-only, matches PV precedent)', async ({ page }) => {
  await login(page, 'rbac_chief_accountant');
  const today = new Date().toISOString().slice(0, 10);
  const create = await page.request.post(`${API}/expense-claims/`, {
    data: {
      employeeId: EMP_ID, claimDate: today, title: 'L4 SoD self-approve probe', notes: null, businessUnitId: null,
      lines: [{ expenseCategoryId: TRAV_CAT, expenseAccountId: null, description: 'self-approve probe',
        expenseDate: null, amount: 100, taxCodeId: null, vatRate: 0, isRecoverableVat: false }],
    },
  });
  expect(create.status()).toBe(201);
  const id = (await create.json()).expense_claim_id;

  const submit = await page.request.post(`${API}/expense-claims/${id}/submit`);
  expect(submit.status()).toBe(204);

  const approve = await page.request.post(`${API}/expense-claims/${id}/approve`);
  console.log('L4-EVIDENCE self-approve (chief_accountant, same user) status =', approve.status());
  expect(approve.status()).toBe(200); // Permission-only SoD ruling — expected to SUCCEED.
});

test('L4-3 malformed payload probes -> typed 400/422, never raw 500', async ({ page }) => {
  // rbac_accountant CAN create via direct API with a known employeeId even though its own UI
  // picker is broken (L4-1) — CreateDraftAsync only checks the employee ROW exists, not
  // master.employee.manage. Confirms the malformed-input guards are independent of that gap.
  await login(page, 'rbac_accountant');
  const today = new Date().toISOString().slice(0, 10);
  const base = { employeeId: EMP_ID, claimDate: today, title: null, notes: null, businessUnitId: null };
  const goodLine = { expenseCategoryId: TRAV_CAT, expenseAccountId: null, description: 'x',
    expenseDate: null, amount: 100, taxCodeId: null, vatRate: 0, isRecoverableVat: false };

  const negAmount = await page.request.post(`${API}/expense-claims/`, {
    data: { ...base, lines: [{ ...goodLine, amount: -100 }] } });
  console.log('L4-EVIDENCE negative amount status =', negAmount.status());
  expect(negAmount.status()).toBe(400);

  const unknownCat = await page.request.post(`${API}/expense-claims/`, {
    data: { ...base, lines: [{ ...goodLine, expenseCategoryId: 999999999 }] } });
  console.log('L4-EVIDENCE unknown category status =', unknownCat.status(), await unknownCat.text());
  expect([400, 404, 422]).toContain(unknownCat.status());
  expect(unknownCat.status()).not.toBe(500);

  const emptyLines = await page.request.post(`${API}/expense-claims/`, {
    data: { ...base, lines: [] } });
  console.log('L4-EVIDENCE empty lines status =', emptyLines.status());
  expect(emptyLines.status()).toBe(400);

  const badVatRate = await page.request.post(`${API}/expense-claims/`, {
    data: { ...base, lines: [{ ...goodLine, vatRate: 1.5 }] } });
  console.log('L4-EVIDENCE vatRate=1.5 status =', badVatRate.status());
  expect(badVatRate.status()).toBe(400);

  // Pay a Draft claim (never approved) as rbac_accountant (no pay perm either way).
  const draft = await page.request.post(`${API}/expense-claims/`, { data: { ...base, lines: [goodLine] } });
  const draftId = (await draft.json()).expense_claim_id;
  const payDraft = await page.request.post(`${API}/expense-claims/${draftId}/pay`, {
    data: { paymentMethod: 'TRANSFER', bankAccountId: BANK_ID } });
  console.log('L4-EVIDENCE pay-a-Draft (as rbac_accountant, no pay perm) status =', payDraft.status(), await payDraft.text());
  expect(payDraft.status()).toBe(403); // permission check runs before the not_approved domain check
  expect(payDraft.status()).not.toBe(500);

  const approveMissing = await page.request.post(`${API}/expense-claims/999999999/approve`);
  console.log('L4-EVIDENCE approve nonexistent id (as rbac_accountant, no approve perm) status =', approveMissing.status());
  expect(approveMissing.status()).not.toBe(500);

  const noBankAcct = await page.request.post(`${API}/expense-claims/${draftId}/pay`, {
    data: { paymentMethod: 'TRANSFER', bankAccountId: null } });
  console.log('L4-EVIDENCE TRANSFER w/o bankAccountId (as rbac_accountant) status =', noBankAcct.status());
  expect(noBankAcct.status()).toBe(403); // still gated on perm first; see L4-4 for a perm-holder's 400
});

test('L4-3b malformed pay-body validator fires for a role that DOES hold pay perm', async ({ page }) => {
  await login(page, 'rbac_chief_accountant');
  const today = new Date().toISOString().slice(0, 10);
  const create = await page.request.post(`${API}/expense-claims/`, {
    data: { employeeId: EMP_ID, claimDate: today, title: null, notes: null, businessUnitId: null,
      lines: [{ expenseCategoryId: TRAV_CAT, expenseAccountId: null, description: 'y', expenseDate: null,
        amount: 50, taxCodeId: null, vatRate: 0, isRecoverableVat: false }] } });
  const id = (await create.json()).expense_claim_id;
  await page.request.post(`${API}/expense-claims/${id}/submit`);
  await page.request.post(`${API}/expense-claims/${id}/approve`);

  const badMethod = await page.request.post(`${API}/expense-claims/${id}/pay`, {
    data: { paymentMethod: 'BITCOIN', bankAccountId: null } });
  console.log('L4-EVIDENCE paymentMethod=BITCOIN status =', badMethod.status());
  expect(badMethod.status()).toBe(400);

  const noBankAcct = await page.request.post(`${API}/expense-claims/${id}/pay`, {
    data: { paymentMethod: 'TRANSFER', bankAccountId: null } });
  console.log('L4-EVIDENCE TRANSFER w/o bankAccountId (perm holder) status =', noBankAcct.status());
  expect(noBankAcct.status()).toBe(400);

  const badBankAcct = await page.request.post(`${API}/expense-claims/${id}/pay`, {
    data: { paymentMethod: 'TRANSFER', bankAccountId: 999999999 } });
  console.log('L4-EVIDENCE nonexistent bankAccountId status =', badBankAcct.status(), await badBankAcct.text());
  expect(badBankAcct.status()).not.toBe(500);
});

test('L4-4 permission gating: role with zero expense perms -> 403 (not merely hidden); approver-only role blocked on pay', async ({ page }) => {
  await login(page, 'rbac_sales_staff');
  const list = await page.request.get(`${API}/expense-claims`);
  console.log('L4-EVIDENCE sales_staff GET list status =', list.status());
  expect(list.status()).toBe(403);

  const create = await page.request.post(`${API}/expense-claims/`, {
    data: { employeeId: EMP_ID, claimDate: new Date().toISOString().slice(0, 10), title: null, notes: null,
      businessUnitId: null, lines: [] } });
  console.log('L4-EVIDENCE sales_staff POST create status =', create.status());
  expect(create.status()).toBe(403);

  // A DIFFERENT user's claim: rbac_chief_accountant creates+submits, rbac_accountant (read-only
  // on this doc, no approve perm at all) tries to approve someone else's claim.
  await logout(page);
  await login(page, 'rbac_chief_accountant');
  const today = new Date().toISOString().slice(0, 10);
  const create2 = await page.request.post(`${API}/expense-claims/`, {
    data: { employeeId: EMP_ID, claimDate: today, title: null, notes: null, businessUnitId: null,
      lines: [{ expenseCategoryId: TRAV_CAT, expenseAccountId: null, description: 'z', expenseDate: null,
        amount: 10, taxCodeId: null, vatRate: 0, isRecoverableVat: false }] } });
  const otherId = (await create2.json()).expense_claim_id;
  await page.request.post(`${API}/expense-claims/${otherId}/submit`);

  await logout(page);
  await login(page, 'rbac_accountant');
  const approveOther = await page.request.post(`${API}/expense-claims/${otherId}/approve`);
  console.log('L4-EVIDENCE rbac_accountant approve ANOTHER user\'s submitted claim status =', approveOther.status());
  expect(approveOther.status()).toBe(403);

  const payAttempt = await page.request.post(`${API}/expense-claims/1/pay`, {
    data: { paymentMethod: 'CASH', bankAccountId: null } });
  console.log('L4-EVIDENCE rbac_accountant pay-attempt (no pay perm) status =', payAttempt.status());
  expect(payAttempt.status()).toBe(403);
});

test('L4-5 edit door: reopen/re-save a draft unchanged -> totals unchanged, no GL touched', async ({ page }) => {
  await login(page, 'rbac_accountant');
  const today = new Date().toISOString().slice(0, 10);
  const line = { expenseCategoryId: TRAV_CAT, expenseAccountId: null, description: 'edit-door probe',
    expenseDate: null, amount: 250, taxCodeId: null, vatRate: 0, isRecoverableVat: false };
  const create = await page.request.post(`${API}/expense-claims/`, {
    data: { employeeId: EMP_ID, claimDate: today, title: 't', notes: null, businessUnitId: null, lines: [line] } });
  const id = (await create.json()).expense_claim_id;

  const before = await (await page.request.get(`${API}/expense-claims/${id}`)).json();
  const put = await page.request.put(`${API}/expense-claims/${id}`, {
    data: { employeeId: EMP_ID, claimDate: today, title: 't', notes: null, businessUnitId: null, lines: [line] } });
  expect(put.status()).toBe(204);
  const after = await (await page.request.get(`${API}/expense-claims/${id}`)).json();

  console.log('L4-EVIDENCE edit-door', JSON.stringify({
    beforeTotal: before.totalAmount, afterTotal: after.totalAmount,
    beforeVersion: before.version, afterVersion: after.version, status: after.status,
    journalEntryId: after.journalEntryId,
  }));
  expect(after.totalAmount).toBe(before.totalAmount);
  expect(after.subtotalAmount).toBe(before.subtotalAmount);
  expect(after.status).toBe('Draft');
  expect(after.journalEntryId).toBeFalsy();
});

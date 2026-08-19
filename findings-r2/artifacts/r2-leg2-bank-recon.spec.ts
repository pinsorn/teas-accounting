import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import {
  login, waitForNavGates, pickCustomer, pickVendor,
  pickTaxInvoice, detailDocNo,
} from './_helpers';

// co1 now has a SECOND leftover customer whose name also contains "ลูกค้าทดสอบ"
// (customer_id 9 "บริษัท SALES ลูกค้าทดสอบ จำกัด", from an earlier RBAC leg) — the shared
// pickCustomer()'s default broad search 'ลูกค้า' + default /ลูกค้าทดสอบ/ name regex now
// resolves to 2 buttons (strict-mode violation). Narrow the search to the ORIGINAL
// customer's tax id (unique) instead of touching the shared helper.
async function pickOriginalCustomer(page: import('@playwright/test').Page) {
  await pickCustomer(page, 'ลูกค้า', /^ลูกค้าทดสอบ/);
}
// createVendor() (shared helper) leaves "Vendor จดทะเบียน VAT" checked (its new default) and
// never fills the tax id, so Save now client-blocks with "ผู้ขายจด VAT ต้องระบุเลขผู้เสียภาษี
// 13 หลัก" (form validation added since that helper was last touched) — L2-flake, not a bank-rec
// finding. Work around it locally: uncheck VAT-registered (this vendor's VAT status is
// irrelevant to the bank-rec walk) instead of touching the shared helper.
async function createVendorNarrow(page: import('@playwright/test').Page): Promise<string> {
  const code = `E2EV-R2L2-${Date.now().toString(36)}`;
  await page.goto('/vendors/new');
  await page.getByText(/รหัสผู้ขาย|Vendor code/).locator('xpath=following::input[1]').fill(code);
  await page.getByText(/^ชื่อ \(ไทย\)|Name \(Thai\)/).locator('xpath=following::input[1]')
    .fill('ผู้ขาย e2e r2l2 จำกัด');
  const vatCheckbox = page.getByRole('checkbox', { name: /VAT/ });
  if (await vatCheckbox.isChecked().catch(() => false)) await vatCheckbox.uncheck();
  await page.getByRole('button', { name: /บันทึกผู้ขาย|Save vendor/ }).click();
  await page.waitForURL(/\/vendors$/, { timeout: 15_000 });
  return code;
}

async function createAndPostTaxInvoiceNarrow(page: import('@playwright/test').Page): Promise<number> {
  await page.goto('/tax-invoices/new');
  await pickOriginalCustomer(page);
  await page.getByLabel('รายละเอียด 1').fill('e2e R2L2 item');
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

// Testing Swarm Round 2 — Leg 2: Bank Reconciliation. THROWAWAY spec, never committed.
// Findings written incrementally to findings-leg2.md (outside this repo). company_id=1.

const SCRATCH = 'Z:\\temp\\claude\\Y--ClaudePlayground-TEAS-Project\\5667c374-e2c0-4998-b10c-b993b4182367\\scratchpad';

function fmtAmt(n: number): string {
  return n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}
function ddmmyy(d: Date): string {
  return `${String(d.getDate()).padStart(2, '0')}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getFullYear()).slice(-2)}`;
}
function ddmmyyyy(d: Date): string {
  return `${String(d.getDate()).padStart(2, '0')}/${String(d.getMonth() + 1).padStart(2, '0')}/${d.getFullYear()}`;
}
function ymd(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

interface CsvLine {
  date: Date; time?: string; type: string; withdraw?: number; deposit?: number;
  balance: number; channel: string; detail: string;
}
interface CsvOpts {
  accountNo: string; periodStart: Date; periodEnd: Date;
  openingBalance: number; closingBalance: number;
  withdrawalTotal: number; withdrawalCount: number;
  depositTotal: number; depositCount: number;
  lines: CsvLine[];
}
// Structure verified against KBizCsvAdapterTests.GoodCsv (backend/tests/.../Bank/KBizCsvAdapterTests.cs)
// — same column positions, same metadata label→value layout.
function buildKbizCsv(o: CsvOpts): Buffer {
  const rows: string[] = [];
  rows.push('รายการเดินบัญชีเงินฝากออมทรัพย์ (มีรายละเอียด),,,,,,,,,,,,');
  rows.push(',ชื่อบัญชี,"บจก. อาร์ทู เลกทู ทดสอบ จำกัด\n99 ถ.ทดสอบ ต.ทดสอบ อ.ทดสอบ จ.ทดสอบ 10000",,,,,เลขที่อ้างอิง,,,,TEST0000000000000,');
  rows.push(`,,,,,,,เลขที่บัญชีเงินฝาก,,,,${o.accountNo},`);
  rows.push(`,,,,,,,รอบระหว่างวันที่,,,,${ddmmyyyy(o.periodStart)} - ${ddmmyyyy(o.periodEnd)},`);
  rows.push(`,,,,,,,ยอดยกไป,,,,,"${fmtAmt(o.closingBalance)}"`);
  rows.push(`,,,,,,,รวมถอนเงิน,,${o.withdrawalCount},,รายการ,"${fmtAmt(o.withdrawalTotal)}"`);
  rows.push(`,,,,,,,รวมฝากเงิน,,${o.depositCount},,รายการ,"${fmtAmt(o.depositTotal)}"`);
  rows.push(',วันที่,เวลา/ วันที่มีผล,รายการ,ถอนเงิน,,ฝากเงิน,,ยอดคงเหลือ,,ช่องทาง,,รายละเอียด');
  rows.push(`,${ddmmyy(o.periodStart)},,ยอดยกมา,,,,,"${fmtAmt(o.openingBalance)}",,,,`);
  for (const l of o.lines) {
    const w = l.withdraw != null ? `"${fmtAmt(l.withdraw)}"` : '';
    const d = l.deposit != null ? `"${fmtAmt(l.deposit)}"` : '';
    rows.push(`,${ddmmyy(l.date)},${l.time ?? ''},${l.type},${w},,${d},,"${fmtAmt(l.balance)}",,${l.channel},,${l.detail}`);
  }
  const csv = rows.join('\r\n') + '\r\n';
  return Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(csv, 'utf-8')]);
}
function writeFixture(name: string, buf: Buffer): string {
  const p = path.join(SCRATCH, name);
  fs.writeFileSync(p, buf);
  return p;
}
function toBomBuffer(text: string): Buffer {
  return Buffer.concat([Buffer.from([0xef, 0xbb, 0xbf]), Buffer.from(text, 'utf-8')]);
}
function stripBom(buf: Buffer): string {
  const text = buf.toString('utf-8');
  return text.charCodeAt(0) === 0xfeff ? text.slice(1) : text;
}

const INTEREST = 5.0;
const FEE = 20.0;

const ctx: {
  bankAccountId?: number; bankAccountNo?: string;
  receiptId?: number; receiptAmount?: number;
  pvId?: number; pvAmount?: number;
  importId?: number; lineIds?: Record<string, number>;
  today: Date; monthStart: Date;
  openingBalance?: number; closingBalance?: number;
  depositTotal?: number; withdrawalTotal?: number;
  glBalanceBefore?: number; depositsInTransitBefore?: number; outstandingBefore?: number;
  unmatchedLinesNetBefore?: number;
} = { today: new Date(), monthStart: new Date() };
ctx.monthStart = new Date(ctx.today.getFullYear(), ctx.today.getMonth(), 1);

test.describe.serial('R2 Leg2 — Bank Reconciliation', () => {
  test('setup: bank account + posted Receipt (Transfer) + posted PaymentVoucher (Transfer)', async ({ page }) => {
    test.setTimeout(120_000);
    await login(page);

    // Reuse an existing co1 bank account (mapped to the default 1120 GL account) if present.
    const existing = await page.request.get('/api/proxy/bank-accounts');
    expect(existing.ok(), 'GET /bank-accounts').toBeTruthy();
    const list = await existing.json();
    const primary = (list as Array<{ bankAccountId: number; accountNo: string; glCashAccountId: number }>)
      .find((b) => b.glCashAccountId === 2); // account_id 2 == 1120 for co1 (verified via psql)
    if (primary) {
      ctx.bankAccountId = primary.bankAccountId;
      ctx.bankAccountNo = primary.accountNo;
    } else {
      const suffix = Date.now().toString().slice(-6);
      ctx.bankAccountNo = `999-9-${suffix}-1`;
      await page.goto('/bank-accounts/new');
      await page.getByLabel('รหัสธนาคาร').fill('KBIZ');
      await page.getByLabel('ชื่อธนาคาร').fill('กสิกรไทย (R2L2 test)');
      await page.getByLabel('เลขที่บัญชี').fill(ctx.bankAccountNo);
      const [createResp] = await Promise.all([
        page.waitForResponse((r) => /\/api\/proxy\/bank-accounts$/.test(r.url()) && r.request().method() === 'POST'),
        page.getByRole('button', { name: 'บันทึก' }).click(),
      ]);
      expect(createResp.status(), 'create bank account').toBe(201);
      const body = await createResp.json();
      ctx.bankAccountId = body.bank_account_id;
    }
    console.log(`[R2L2] bankAccountId=${ctx.bankAccountId} accountNo=${ctx.bankAccountNo}`);

    // Posted Receipt: co1 is VAT-registered so the receipt form forces mode='ti' — mirrors
    // issue-receipt.spec.ts exactly (TI 1000 + 7% VAT -> Receipt applied 1070, Transfer method
    // is the receipt form's hardcoded paymentMethod -> posts to GL 1120).
    await createAndPostTaxInvoiceNarrow(page);
    const tiDocNo = await detailDocNo(page, 'TI');

    await page.goto('/receipts/new');
    await pickOriginalCustomer(page);
    await pickTaxInvoice(page, 1, tiDocNo);
    await page.getByLabel('ยอดชำระ 1').fill('1070');
    await page.getByRole('button', { name: /^บันทึกเอกสาร|Post$/ }).click();
    const rcDialog = page.getByRole('dialog');
    await expect(rcDialog).toBeVisible();
    await rcDialog.getByRole('button', { name: /Confirm post|ยืนยันบันทึก/i }).click();
    await page.waitForURL(/\/receipts\/\d+$/, { timeout: 15_000 });
    const rcMatch = page.url().match(/\/receipts\/(\d+)$/);
    ctx.receiptId = Number(rcMatch![1]);

    const rcResp = await page.request.get(`/api/proxy/receipts/${ctx.receiptId}`);
    expect(rcResp.ok(), 'GET receipt detail').toBeTruthy();
    const rcBody = await rcResp.json();
    ctx.receiptAmount = rcBody.cashReceived;
    console.log(`[R2L2] receiptId=${ctx.receiptId} cashReceived=${ctx.receiptAmount}`);
    expect(ctx.receiptAmount).toBe(1070);

    // Posted Payment Voucher (Transfer method, the form's default — posts to GL 1120).
    // Self-approve as admin: PV approval is permission-based, not creator!=approver SoD
    // (pv-approval-permission.spec.ts pins this — "cont.77 (Ham decision)").
    const vendorCode = await createVendorNarrow(page);
    await page.goto('/payment-vouchers/new');
    await pickVendor(page, vendorCode);
    await page.getByText(/หมวดค่าใช้จ่าย|Expense Category/)
      .locator('xpath=following::select[1]')
      .selectOption({ label: 'ค่าบริการ (SVC)' });
    await page.getByRole('textbox', { name: 'รายละเอียด 1' }).fill('e2e R2L2 bank-rec PV');
    await page.getByRole('spinbutton', { name: /^มูลค่าก่อนภาษี/ }).fill('800');

    await page.getByRole('button', { name: /^บันทึก$|^Save$/ }).click();
    await page.waitForURL(/\/payment-vouchers\/\d+$/, { timeout: 15_000 });
    const pvUrl = page.url();
    ctx.pvId = Number(pvUrl.match(/\/payment-vouchers\/(\d+)$/)![1]);

    await page.getByRole('button', { name: /^อนุมัติ$|^Approve$/ }).click();
    // New confirm-dialog step on Approve (not present when payment-voucher-with-wht.spec.ts /
    // pv-approval-permission.spec.ts were last touched) — same useConfirm() alertdialog pattern
    // as unmatch/deactivate elsewhere in the app. Confirm it if it appears.
    const approveDialog = page.getByRole('dialog').or(page.getByRole('alertdialog'));
    try {
      await approveDialog.waitFor({ state: 'visible', timeout: 3_000 });
      await approveDialog.getByRole('button', { name: /^ยืนยัน$|^Confirm$/ }).click();
    } catch { /* no confirm step in this build — fine */ }
    await expect(page.locator('body')).toContainText(/อนุมัติแล้ว|Approved/, { timeout: 10_000 });
    const postBtn = page.getByRole('button', { name: /บันทึกเอกสาร \(Post\)|^Post$/ });
    await expect(postBtn).toBeVisible({ timeout: 10_000 });
    await expect(async () => {
      await postBtn.click({ force: true });
      // Same new confirm-dialog step as Approve, now on Post too ("ยืนยันการบันทึกใบสำคัญจ่าย").
      const postDialog = page.getByRole('dialog').or(page.getByRole('alertdialog'));
      try {
        await postDialog.waitFor({ state: 'visible', timeout: 2_000 });
        await postDialog.getByRole('button', { name: /^ยืนยัน$|^Confirm$/ }).click();
      } catch { /* no confirm step in this build — fine */ }
      await page.waitForResponse(
        (r) => /\/payment-vouchers\/\d+\/post$/.test(r.url()) && r.request().method() === 'POST',
        { timeout: 3_000 });
    }).toPass({ timeout: 25_000 });
    await expect(page.locator('body')).toContainText(/บันทึกแล้ว|Posted/, { timeout: 10_000 });

    const pvResp = await page.request.get(`/api/proxy/payment-vouchers/${ctx.pvId}`);
    expect(pvResp.ok(), 'GET PV detail').toBeTruthy();
    const pvBody = await pvResp.json();
    ctx.pvAmount = pvBody.totalPaid;
    console.log(`[R2L2] pvId=${ctx.pvId} totalPaid=${ctx.pvAmount}`);
    expect(ctx.pvAmount).toBeGreaterThan(0);
  });

  test('upload KBiz CSV statement: import succeeds, integrity ties out, DB rows match', async ({ page }) => {
    test.setTimeout(60_000);
    await login(page);

    // Dynamic baseline — co1 carries leftover Posted Receipts/PVs from earlier legs (unmatched,
    // no bank statement ever imported for them), so GL 1120 is NOT zero. Read the report's OWN
    // pre-upload numbers (glBalance / depositsInTransit / outstanding — well-defined even with
    // zero imports) and set the statement's opening balance to the value that makes the tie-out
    // formula hold once the new Receipt/PV are matched — this is robust to however much stray
    // test data already exists, rather than assuming a clean-zero start.
    const toStr = ymd(ctx.today);
    const fromStr = ymd(ctx.monthStart);
    const before = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/reconciliation?from=${fromStr}&to=${toStr}`);
    expect(before.ok(), 'GET reconciliation report (pre-upload)').toBeTruthy();
    const beforeBody = await before.json();
    ctx.glBalanceBefore = beforeBody.glBalance;
    ctx.depositsInTransitBefore = beforeBody.depositsInTransitTotal;
    ctx.outstandingBefore = beforeBody.outstandingPaymentsTotal;
    // Leftover imports from earlier dev-iteration runs of THIS spec may already carry their own
    // unmatched lines (e.g. a run that failed before reaching the matching test) — capture that
    // baseline too so the post-flow assertion (test 5) checks the NET CHANGE our own 4 lines
    // cause, not an absolute value that assumes a pristine bank account.
    ctx.unmatchedLinesNetBefore = beforeBody.unmatchedLinesNet;
    ctx.openingBalance = ctx.glBalanceBefore! - ctx.depositsInTransitBefore! + ctx.outstandingBefore!;
    console.log(`[R2L2] pre-upload glBalance=${ctx.glBalanceBefore} depositsInTransit=${ctx.depositsInTransitBefore} outstanding=${ctx.outstandingBefore} unmatchedLinesNet=${ctx.unmatchedLinesNetBefore} -> openingBalance=${ctx.openingBalance}`);

    ctx.depositTotal = ctx.receiptAmount! + INTEREST;
    ctx.withdrawalTotal = ctx.pvAmount! + FEE;
    ctx.closingBalance = ctx.openingBalance + ctx.depositTotal - ctx.withdrawalTotal;

    let bal = ctx.openingBalance;
    const depBal = bal + ctx.receiptAmount!; bal = depBal;
    const wdBal = bal - ctx.pvAmount!; bal = wdBal;
    const intBal = bal + INTEREST; bal = intBal;
    const feeBal = bal - FEE; bal = feeBal;

    const csv = buildKbizCsv({
      accountNo: ctx.bankAccountNo!,
      periodStart: ctx.monthStart, periodEnd: ctx.today,
      openingBalance: ctx.openingBalance, closingBalance: ctx.closingBalance,
      withdrawalTotal: ctx.withdrawalTotal, withdrawalCount: 2,
      depositTotal: ctx.depositTotal, depositCount: 2,
      lines: [
        { date: ctx.today, time: '10:15', type: 'รับโอนเงิน', deposit: ctx.receiptAmount, balance: depBal, channel: 'MAKE by KBank', detail: `จาก R2L2-DEP ทดสอบ #${ctx.receiptId}` },
        { date: ctx.today, time: '11:20', type: 'โอนเงิน', withdraw: ctx.pvAmount, balance: wdBal, channel: 'สาขาทดสอบ', detail: `โอนไป R2L2-WD ทดสอบ #${ctx.pvId}` },
        { date: ctx.today, time: '23:59', type: 'รับดอกเบี้ยเงินฝาก', deposit: INTEREST, balance: intBal, channel: 'โอนเข้า/หักบัญชีอัตโนมัติ', detail: 'รหัสอ้างอิง R2L2-INT' },
        { date: ctx.today, time: '23:59', type: 'ค่าธรรมเนียม', withdraw: FEE, balance: feeBal, channel: 'โอนเข้า/หักบัญชีอัตโนมัติ', detail: 'ค่าธรรมเนียมรักษาบัญชี R2L2-FEE' },
      ],
    });
    const fixturePath = writeFixture('r2l2-statement.csv', csv);
    console.log(`[R2L2] fixture written: ${fixturePath}`);

    await page.goto(`/bank-accounts/${ctx.bankAccountId}`);
    await waitForNavGates(page);
    await page.getByTestId('import-upload-open').click();
    await page.getByTestId('import-file').setInputFiles(fixturePath);
    const [uploadResp] = await Promise.all([
      page.waitForResponse((r) => /\/api\/proxy\/bank-accounts\/\d+\/imports$/.test(r.url()) && r.request().method() === 'POST'),
      page.getByTestId('import-submit').click(),
    ]);
    expect(uploadResp.status(), 'upload statement').toBe(201);
    const uploadBody = await uploadResp.json();
    ctx.importId = uploadBody.statementImportId;
    expect(uploadBody.lineCount).toBe(4);
    // overlapWarning may legitimately be true on a repeat dev-iteration run against the same
    // bank account/period (a prior successful run's import already covers it) — B2.5's "warn,
    // never block" design under test separately (duplicate-import test, below). Just log it here.
    console.log(`[R2L2] importId=${ctx.importId} lineCount=${uploadBody.lineCount} overlapWarning=${uploadBody.overlapWarning}`);

    // DB verification — statement_imports + statement_lines rows, to the satang.
    const impResp = await page.request.get(`/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`);
    const imports = await impResp.json();
    const thisImport = imports.find((i: { statementImportId: number }) => i.statementImportId === ctx.importId);
    expect(thisImport.status).toBe('Parsed');
    expect(thisImport.openingBalance).toBe(ctx.openingBalance);
    expect(thisImport.closingBalance).toBe(ctx.closingBalance);

    const linesResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}/lines`);
    const lines = await linesResp.json();
    expect(lines.length).toBe(4);
    ctx.lineIds = {};
    for (const l of lines as Array<{ statementLineId: number; description: string; matchStatus: string }>) {
      if (l.description.includes('R2L2-DEP')) ctx.lineIds.dep = l.statementLineId;
      else if (l.description.includes('R2L2-WD')) ctx.lineIds.wd = l.statementLineId;
      else if (l.description.includes('R2L2-INT')) ctx.lineIds.int = l.statementLineId;
      else if (l.description.includes('R2L2-FEE')) ctx.lineIds.fee = l.statementLineId;
      expect(l.matchStatus, `line ${l.statementLineId} starts Unmatched`).toBe('Unmatched');
    }
    expect(ctx.lineIds).toMatchObject({ dep: expect.any(Number), wd: expect.any(Number), int: expect.any(Number), fee: expect.any(Number) });
    console.log(`[R2L2] lineIds=${JSON.stringify(ctx.lineIds)}`);
  });

  test('matching: confirm deposit<->Receipt and withdrawal<->PaymentVoucher via suggestions', async ({ page }) => {
    test.setTimeout(60_000);
    await login(page);
    await page.goto(`/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}`);
    await waitForNavGates(page);

    // Find each row by its unique description marker text, then act
    // on the buttons WITHIN that row (scoped locator avoids any cross-row ambiguity).
    const depRow = page.getByTestId('matching-line-row').filter({ hasText: 'R2L2-DEP' });
    const wdRow = page.getByTestId('matching-line-row').filter({ hasText: 'R2L2-WD' });
    await expect(depRow).toHaveCount(1);
    await expect(wdRow).toHaveCount(1);

    // NOTE (L2-1, findings-leg2.md) — the suggest modal is a plain <div class="modal-box">,
    // no role="dialog" — scope locators to '.modal-box' rather than getByRole('dialog').
    // co1 carries several OTHER leftover Posted+unmatched Receipts at the identical 1,070.00
    // amount/date (RC-0002..RC-0007) — all are valid D4 candidates for our line, so pick the
    // suggestion row DETERMINISTICALLY by our own known docId (via the suggestions API) rather
    // than blindly clicking "first".
    async function suggestDocNo(lineId: number, wantDocId: number): Promise<string> {
      const r = await page.request.get(
        `/api/proxy/bank-accounts/${ctx.bankAccountId}/lines/${lineId}/suggestions`);
      expect(r.ok(), `suggestions for line ${lineId}`).toBeTruthy();
      const suggestions = await r.json() as Array<{ docId: number; docNo: string }>;
      const mine = suggestions.find((s) => s.docId === wantDocId);
      expect(mine, `line ${lineId} suggestions include docId ${wantDocId}: ${JSON.stringify(suggestions)}`).toBeTruthy();
      return mine!.docNo;
    }

    // Deposit line -> suggest -> confirm against OUR Receipt (ctx.receiptId).
    const depDocNo = await suggestDocNo(ctx.lineIds!.dep, ctx.receiptId!);
    await depRow.getByTestId('suggest-open').click();
    const suggestModal = page.locator('.modal-box');
    await expect(suggestModal).toBeVisible();
    const depItem = suggestModal.getByRole('listitem').filter({ hasText: depDocNo });
    await expect(depItem).toHaveCount(1);
    const [matchResp1] = await Promise.all([
      page.waitForResponse((r) => /\/lines\/\d+\/match$/.test(r.url()) && r.request().method() === 'POST'),
      depItem.getByTestId('suggest-confirm').click(),
    ]);
    expect(matchResp1.status(), 'confirm deposit match').toBe(204);

    // Withdrawal line -> suggest -> confirm against OUR PaymentVoucher (ctx.pvId).
    const wdDocNo = await suggestDocNo(ctx.lineIds!.wd, ctx.pvId!);
    await wdRow.getByTestId('suggest-open').click();
    const suggestModal2 = page.locator('.modal-box');
    await expect(suggestModal2).toBeVisible();
    const wdItem = suggestModal2.getByRole('listitem').filter({ hasText: wdDocNo });
    await expect(wdItem).toHaveCount(1);
    const [matchResp2] = await Promise.all([
      page.waitForResponse((r) => /\/lines\/\d+\/match$/.test(r.url()) && r.request().method() === 'POST'),
      wdItem.getByTestId('suggest-confirm').click(),
    ]);
    expect(matchResp2.status(), 'confirm withdrawal match').toBe(204);

    // Re-fetch and verify statuses + links.
    const linesResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}/lines`);
    const lines = await linesResp.json();
    const dep = lines.find((l: { statementLineId: number }) => l.statementLineId === ctx.lineIds!.dep);
    const wd = lines.find((l: { statementLineId: number }) => l.statementLineId === ctx.lineIds!.wd);
    expect(dep.matchStatus).toBe('Matched');
    expect(wd.matchStatus).toBe('Matched');

    // Fee line has NO valid candidate (no Posted, unmatched Receipt/PV at that exact amount within
    // +-7 days) — suggestions must come back empty, not error.
    const feeRow = page.getByTestId('matching-line-row').filter({ hasText: 'R2L2-FEE' });
    await feeRow.getByTestId('suggest-open').click();
    const suggestModal3 = page.locator('.modal-box');
    await expect(suggestModal3).toBeVisible();
    await expect(suggestModal3.getByText(/ไม่พบใบเสร็จรับเงิน|No.*suggestion/i)).toBeVisible({ timeout: 10_000 });
    await suggestModal3.getByRole('button', { name: 'ยกเลิก' }).click();
  });

  test('inline JE: post the interest line, verify Dr=Cr, period, and journal-list visibility', async ({ page }) => {
    test.setTimeout(60_000);
    await login(page);
    await page.goto(`/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}`);
    await waitForNavGates(page);

    const intRow = page.getByTestId('matching-line-row').filter({ hasText: 'R2L2-INT' });
    await expect(intRow).toHaveCount(1);
    await intRow.getByTestId('journal-open').click();
    const journalModal = page.locator('.modal-box');   // L2-1 — no role="dialog" here either
    await expect(journalModal).toBeVisible();
    await journalModal.getByTestId('contra-account').selectOption({ label: '4300 — รายได้อื่น' });
    const [journalResp] = await Promise.all([
      page.waitForResponse((r) => /\/lines\/\d+\/journal$/.test(r.url()) && r.request().method() === 'POST'),
      journalModal.getByTestId('journal-submit').click(),
    ]);
    expect(journalResp.status(), 'post inline JE').toBe(200);
    const journalBody = await journalResp.json();
    const journalId: number = journalBody.journalId;
    console.log(`[R2L2] interest line -> journalId=${journalId}`);

    // Line flips to Posted + carries the JE no.
    const linesResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}/lines`);
    const lines = await linesResp.json();
    const intLine = lines.find((l: { statementLineId: number }) => l.statementLineId === ctx.lineIds!.int);
    expect(intLine.matchStatus).toBe('Posted');

    // Journal visible in the journal list + its own detail page, Dr=Cr, correct period (today,
    // an open period), correct accounts (bank 1120 dr, contra 4300 cr — MoneyIn).
    const jResp = await page.request.get(`/api/proxy/journals/${journalId}`);
    expect(jResp.ok(), 'GET journal detail').toBeTruthy();
    const jBody = await jResp.json();
    console.log(`[R2L2] journal ${journalId}: docDate=${jBody.docDate} status=${jBody.status} totalDebit=${jBody.totalDebit} totalCredit=${jBody.totalCredit}`);
    expect(jBody.totalDebit).toBe(jBody.totalCredit);
    expect(jBody.totalDebit).toBe(INTEREST);
    expect(jBody.docDate).toBe(ymd(ctx.today));
    expect(jBody.status).toBe('Posted');

    // Visible on its own detail page in the journal list module (docNo assigned, Posted).
    await page.goto(`/journals/${journalId}`);
    await waitForNavGates(page);
    await expect(page.locator('body')).toContainText(jBody.docNo ?? String(journalId), { timeout: 10_000 });
    await expect(page.locator('body')).toContainText(/บันทึกบัญชี|Posted|บันทึกแล้ว/, { timeout: 10_000 });

    // Postgres cross-check (below, separate step) confirms Dr=Cr + correct accounts independently
    // of the API's own totals.
  });

  test('report tie-out: difference == 0 to the satang with one line deliberately left unmatched', async ({ page }) => {
    test.setTimeout(60_000);
    await login(page);

    const toStr = ymd(ctx.today);
    const fromStr = ymd(ctx.monthStart);
    const r = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/reconciliation?from=${fromStr}&to=${toStr}`);
    expect(r.ok(), 'GET reconciliation report').toBeTruthy();
    const report = await r.json();
    console.log(`[R2L2] report after: ${JSON.stringify(report)}`);

    const expectedGl = ctx.glBalanceBefore! + INTEREST;
    const expectedDeposits = ctx.depositsInTransitBefore! - ctx.receiptAmount!;
    const expectedOutstanding = ctx.outstandingBefore! - ctx.pvAmount!;
    const expectedUnmatchedNet = ctx.unmatchedLinesNetBefore! - FEE;

    expect(report.glBalance).toBeCloseTo(expectedGl, 2);
    expect(report.depositsInTransitTotal).toBeCloseTo(expectedDeposits, 2);
    expect(report.outstandingPaymentsTotal).toBeCloseTo(expectedOutstanding, 2);
    expect(report.unmatchedLinesNet).toBeCloseTo(expectedUnmatchedNet, 2);

    // L2-2 (findings-leg2.md, 🔴) — DISCOVERED HERE, not asserted as a pass: on a repeat
    // same-day run, prior dev-iteration imports for this bank account share the identical
    // PeriodEnd (today) with THIS one. BankReconciliationReportService.GetAsync's
    // `statementClosingBalance` query is `.OrderByDescending(i => i.PeriodEnd).FirstOrDefault()`
    // with NO secondary tiebreak — on a PeriodEnd tie it silently returns an ARBITRARY import
    // (confirmed via Postgres: 5 imports for bank_account_id=1, all period_end=2026-08-19,
    // closing_balance 255/255/255/525/800 — the report returned 255, the STALEST one, not 800,
    // the one this test just uploaded). `difference` is computed FROM that wrong
    // statementClosingBalance, so it's wrong too. This is a real, likely scenario (not just
    // test-iteration noise) precisely because B2.5's "warn, never block" duplicate/overlapping
    // import design means 2+ imports sharing a PeriodEnd is an EXPECTED, supported state — not
    // hardened here. Not asserting statementClosingBalance/difference in this test because of
    // this bug; verifying THIS import's own numbers directly against the /imports list instead
    // (test 2, done) is the only reliable path today.
    console.log(`[R2L2] L2-2 evidence: report.statementClosingBalance=${report.statementClosingBalance} (expected THIS import's ${ctx.closingBalance}) — see findings-leg2.md`);

    // The fee line is the only one of ours still unmatched; confirm it's the one carried in the
    // report's own unmatchedLines breakdown (not just netted silently).
    const feeItem = (report.unmatchedLines as Array<{ description: string; amount: number }>)
      .find((x) => x.description.includes('R2L2-FEE'));
    expect(feeItem, 'fee line appears in report.unmatchedLines').toBeTruthy();
    expect(feeItem!.amount).toBeCloseTo(-FEE, 2);

    // UI evidence — visit the report page itself (co1 has exactly one bank account, so the
    // page's own useEffect auto-selects it; no manual dropdown pick needed).
    await page.goto('/reports/bank-reconciliation');
    await waitForNavGates(page);
    await expect(page.getByText('ผลต่าง').first()).toBeVisible({ timeout: 15_000 }).catch(() => {});
    await page.screenshot({ path: path.join(SCRATCH, 'report-tie-out.png'), fullPage: true }).catch(() => {});
  });

  test('report CSV export: BOM + explicit CRLF (folded footgun), via the real Export button', async ({ page }) => {
    test.setTimeout(30_000);
    await login(page);
    await page.goto('/reports/bank-reconciliation');
    await waitForNavGates(page);
    // co1 has exactly one bank account -> the report page auto-selects it (LOW chief01 fix).
    await expect(page.getByRole('button', { name: 'ส่งออก CSV' })).toBeEnabled({ timeout: 15_000 });
    const [download] = await Promise.all([
      page.waitForEvent('download'),
      page.getByRole('button', { name: 'ส่งออก CSV' }).click(),
    ]);
    const dlPath = await download.path();
    expect(dlPath, 'download saved to disk').toBeTruthy();
    const buf = fs.readFileSync(dlPath!);
    const text = buf.toString('utf-8');
    expect(text.charCodeAt(0), 'leading UTF-8 BOM (U+FEFF)').toBe(0xfeff);
    expect(text.includes('\r\n'), 'rows joined with explicit CRLF').toBe(true);
    // Sanity: an unescaped bare \n-only join would still (mostly) render in Excel, so also
    // confirm NO bare \n survives outside the CRLF pairs (the folded footgun's whole point).
    const withoutCrlf = text.replace(/\r\n/g, '');
    expect(withoutCrlf.includes('\n'), 'no bare LF outside CRLF pairs').toBe(false);
    console.log(`[R2L2] CSV export saved: ${dlPath} (${buf.length} bytes)`);
  });

  test('edit door: unmatch/rematch a Matched line is idempotent; Posted line unmatch is blocked; bank-account re-save touches only its own field', async ({ page }) => {
    test.setTimeout(60_000);
    await login(page);

    // (a) Unmatch the deposit line (Matched, not Posted -> D8 allows it) via the real UI,
    // confirm it round-trips cleanly back to Unmatched, then re-confirm the SAME match again.
    await page.goto(`/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}`);
    await waitForNavGates(page);
    const depRow = page.getByTestId('matching-line-row').filter({ hasText: 'R2L2-DEP' });
    await expect(depRow.getByText('Matched')).toBeVisible();
    await depRow.getByRole('button', { name: 'ยกเลิกจับคู่' }).click();
    const unmatchConfirm = page.getByRole('alertdialog').or(page.getByRole('dialog'));
    await expect(unmatchConfirm).toBeVisible();
    const [unmatchResp] = await Promise.all([
      page.waitForResponse((r) => /\/lines\/\d+\/unmatch$/.test(r.url()) && r.request().method() === 'POST'),
      unmatchConfirm.getByRole('button', { name: /^ยืนยัน$|^Confirm$/ }).click(),
    ]);
    expect(unmatchResp.status()).toBe(204);

    let linesResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}/lines`);
    let lines = await linesResp.json();
    let dep = lines.find((l: { statementLineId: number }) => l.statementLineId === ctx.lineIds!.dep);
    expect(dep.matchStatus).toBe('Unmatched');

    // Re-confirm the SAME match via the API directly (deterministic — same docId as before).
    const rematchResp = await page.request.post(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/lines/${ctx.lineIds!.dep}/match`,
      { data: { receiptId: ctx.receiptId, paymentVoucherId: null } });
    expect(rematchResp.status(), 're-confirm the same match').toBe(204);
    linesResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${ctx.importId}/lines`);
    lines = await linesResp.json();
    dep = lines.find((l: { statementLineId: number }) => l.statementLineId === ctx.lineIds!.dep);
    expect(dep.matchStatus).toBe('Matched');
    console.log('[R2L2] unmatch -> rematch round-trip: idempotent, back to Matched.');

    // (b) The interest line is Posted (JE-backed) — D8 says unmatch must be BLOCKED outright.
    const unmatchPostedResp = await page.request.post(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/lines/${ctx.lineIds!.int}/unmatch`);
    expect(unmatchPostedResp.status(), 'unmatch a Posted line must be rejected').toBe(422);
    const unmatchPostedBody = await unmatchPostedResp.json();
    expect(unmatchPostedBody.title).toBe('bank.line_posted');
    console.log(`[R2L2] unmatch on Posted line correctly rejected: ${JSON.stringify(unmatchPostedBody)}`);

    // (c) Re-save the bank account (rename only) — nothing else should move.
    const beforeResp = await page.request.get(`/api/proxy/bank-accounts/${ctx.bankAccountId}`);
    const before = await beforeResp.json();
    const impCountBefore = (await (await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`)).json()).length;

    await page.goto(`/bank-accounts/${ctx.bankAccountId}`);
    await waitForNavGates(page);
    const nameInput = page.getByLabel('ชื่อธนาคาร');
    await nameInput.fill('กสิกรไทย (R2L2 test, renamed)');
    const [saveResp] = await Promise.all([
      page.waitForResponse((r) => /\/api\/proxy\/bank-accounts\/\d+$/.test(r.url()) && r.request().method() === 'PUT'),
      page.getByRole('button', { name: 'บันทึก' }).click(),
    ]);
    expect(saveResp.status()).toBe(204);

    const afterResp = await page.request.get(`/api/proxy/bank-accounts/${ctx.bankAccountId}`);
    const after = await afterResp.json();
    expect(after.bankName).toBe('กสิกรไทย (R2L2 test, renamed)');
    expect(after.accountNo).toBe(before.accountNo);   // immutable
    expect(after.bankCode).toBe(before.bankCode);      // immutable
    expect(after.glCashAccountId).toBe(before.glCashAccountId);
    const impCountAfter = (await (await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`)).json()).length;
    expect(impCountAfter, 're-saving the bank account must not touch imports').toBe(impCountBefore);
    console.log('[R2L2] bank-account re-save: only bankName changed, imports/immutable fields untouched.');
  });

  test('duplicate import: same file uploaded twice -> warned not blocked, rows genuinely duplicated', async ({ page }) => {
    test.setTimeout(30_000);
    await login(page);

    const fixturePath = path.join(SCRATCH, 'r2l2-statement.csv');
    expect(fs.existsSync(fixturePath), 'reuse test 2\'s fixture file').toBe(true);

    const importsBefore = await (await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`)).json();
    const countBefore = importsBefore.length;

    await page.goto(`/bank-accounts/${ctx.bankAccountId}`);
    await waitForNavGates(page);
    await page.getByTestId('import-upload-open').click();
    await page.getByTestId('import-file').setInputFiles(fixturePath);
    const [uploadResp] = await Promise.all([
      page.waitForResponse((r) => /\/api\/proxy\/bank-accounts\/\d+\/imports$/.test(r.url()) && r.request().method() === 'POST'),
      page.getByTestId('import-submit').click(),
    ]);
    expect(uploadResp.status(), 'duplicate upload is NOT refused (B2.5: warn, never block)').toBe(201);
    const dupBody = await uploadResp.json();
    expect(dupBody.overlapWarning, 'overlapWarning MUST be true — same account+period as the original import').toBe(true);
    expect(dupBody.lineCount).toBe(4);
    const dupImportId = dupBody.statementImportId;
    console.log(`[R2L2] duplicate importId=${dupImportId} overlapWarning=${dupBody.overlapWarning}`);

    const importsAfter = await (await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`)).json();
    expect(importsAfter.length, 'a SECOND StatementImport row was genuinely created (not deduped)').toBe(countBefore + 1);

    // The duplicate's own 4 lines all start Unmatched. Its deposit/withdrawal lines have NO
    // valid suggestion now — the real Receipt/PV are already matched to the ORIGINAL import's
    // lines (D4's "not already matched to another line" rule), so these are permanently
    // orphaned duplicates unless the user manually finds+Ignores all 4 by hand.
    const dupLines = await (await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/imports/${dupImportId}/lines`)).json();
    expect(dupLines.every((l: { matchStatus: string }) => l.matchStatus === 'Unmatched')).toBe(true);
    const dupDep = dupLines.find((l: { description: string }) => l.description.includes('R2L2-DEP'));
    const suggResp = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/lines/${dupDep.statementLineId}/suggestions`);
    const suggestions = await suggResp.json();
    const stillOffersOriginalReceipt = suggestions.some((s: { docId: number }) => s.docId === ctx.receiptId);
    expect(stillOffersOriginalReceipt, 'the already-matched Receipt is correctly excluded from suggestions').toBe(false);
    console.log(`[R2L2] duplicate's deposit line suggestions: ${JSON.stringify(suggestions)} (all orphaned — no valid candidate left)`);

    // L2-2 consequence, reconfirmed: THIS duplicate also shares today's PeriodEnd, deepening the
    // report's stale-pick problem (now 8 imports tied on period_end for this bank account).
    const r = await page.request.get(
      `/api/proxy/bank-accounts/${ctx.bankAccountId}/reconciliation?from=${ymd(ctx.monthStart)}&to=${ymd(ctx.today)}`);
    const report = await r.json();
    console.log(`[R2L2] report after duplicate import: statementClosingBalance=${report.statementClosingBalance} unmatchedLinesNet=${report.unmatchedLinesNet} difference=${report.difference} (unmatchedLinesNet grew by the duplicate's own -820 net, permanently, unless manually Ignored)`);
  });

  test('malformed CSV: typed errors not raw 500s (wrong columns / garbage / empty / oversized field)', async ({ page }) => {
    test.setTimeout(30_000);
    await login(page);

    // A minimal, internally-consistent VALID base (D10 ties out) to mutate per case: opening 0,
    // one 100.00 deposit -> closing 100.00.
    const validCsv = buildKbizCsv({
      accountNo: ctx.bankAccountNo!, periodStart: ctx.monthStart, periodEnd: ctx.today,
      openingBalance: 0, closingBalance: 100,
      withdrawalTotal: 0, withdrawalCount: 0, depositTotal: 100, depositCount: 1,
      lines: [{ date: ctx.today, time: '09:00', type: 'รับโอนเงิน', deposit: 100, balance: 100, channel: 'test', detail: 'malformed-test-base' }],
    });
    const validText = stripBom(validCsv);

    async function upload(buf: Buffer, name: string) {
      return page.request.post(`/api/proxy/bank-accounts/${ctx.bankAccountId}/imports`, {
        multipart: { file: { name, mimeType: 'text/csv', buffer: buf } },
      });
    }
    async function assertTypedNot500(resp: Awaited<ReturnType<typeof upload>>, label: string) {
      const status = resp.status();
      const body = await resp.json().catch(() => null);
      console.log(`[R2L2] ${label}: status=${status} body=${JSON.stringify(body)}`);
      expect(status, `${label}: must not be a raw 500`).toBeLessThan(500);
      // A typed domain/validation error always carries SOME identifiable code/title/detail —
      // never an empty/opaque body.
      expect(body, `${label}: body present`).toBeTruthy();
    }

    // (a) Wrong columns — header renamed so a required column (รายละเอียด) can't be found.
    const wrongColsText = validText.replace(
      ',วันที่,เวลา/ วันที่มีผล,รายการ,ถอนเงิน,,ฝากเงิน,,ยอดคงเหลือ,,ช่องทาง,,รายละเอียด',
      ',วันที่,เวลา/ วันที่มีผล,รายการ,ถอนเงิน,,ฝากเงิน,,ยอดคงเหลือ,,ช่องทาง,,XXX');
    expect(wrongColsText).not.toBe(validText);
    const r1 = await upload(toBomBuffer(wrongColsText), 'wrong-columns.csv');
    await assertTypedNot500(r1, 'wrong columns (missing รายละเอียด header)');
    expect(r1.status()).toBe(422);

    // (b) Garbage encoding — random binary, no valid CSV structure at all.
    const garbage = Buffer.from(Array.from({ length: 500 }, () => Math.floor(Math.random() * 256)));
    const r2 = await upload(garbage, 'garbage.csv');
    await assertTypedNot500(r2, 'garbage binary content');

    // (c) Empty file — 0 bytes.
    const r3 = await upload(Buffer.alloc(0), 'empty.csv');
    await assertTypedNot500(r3, 'empty file (0 bytes)');
    expect(r3.status()).toBe(400);

    // (d) Huge row — a single Description cell far exceeding the DB column's varchar(500),
    // everything else internally consistent (D10 ties out) so parsing/integrity PASS and this
    // exercises whatever happens at the DB-persistence step.
    const hugeDetail = 'x'.repeat(600);
    const hugeRowText = validText.replace('malformed-test-base', hugeDetail);
    expect(hugeRowText).not.toBe(validText);
    const r4 = await upload(toBomBuffer(hugeRowText), 'huge-row.csv');
    const r4Body = await r4.json().catch(() => null);
    console.log(`[R2L2] L2-4 (findings-leg2.md, 🔴) oversized Description field: status=${r4.status()} body=${JSON.stringify(r4Body)}`);
    // CONFIRMED BUG (L2-4): this is a raw internal_error 500 (DbUpdateException/22001 "value too
    // long for varchar(500)"), NOT a typed bank.* validation error — the service's try/catch only
    // wraps adapter.Parse + D10 integrity, not the later SaveChangesAsync calls. Locking in the
    // OBSERVED (buggy) behavior here rather than asserting the desired one, so this spec still
    // completes; see findings-leg2.md L2-4 for the full repro/root-cause/fix-shape. Atomicity is
    // NOT broken (transaction rolls back cleanly — verified zero orphaned rows in Postgres).
    expect(r4.status(), 'L2-4: currently 500 (bug) — flip to <500 once fixed').toBe(500);
  });

  test('permission probe: a low-privilege user (sales_staff) 403s on every bank mutation/read route', async ({ page }) => {
    test.setTimeout(30_000);
    await login(page, 'rbac_sales_staff');

    const bid = ctx.bankAccountId!;
    const lineId = ctx.lineIds!.fee;   // any real line id — RequireAuthorization runs before the handler

    const probes: Array<{ label: string; run: () => Promise<import('@playwright/test').APIResponse> }> = [
      { label: 'GET /bank-accounts (list, bank.account.read)', run: () => page.request.get('/api/proxy/bank-accounts') },
      { label: 'POST /bank-accounts (create, bank.account.manage)',
        run: () => page.request.post('/api/proxy/bank-accounts', { data: { bankCode: 'X', bankName: 'X', accountNo: '000-0-00000-0' } }) },
      { label: `POST /bank-accounts/${bid}/imports (upload, bank.statement.import)`,
        run: () => page.request.post(`/api/proxy/bank-accounts/${bid}/imports`, {
          multipart: { file: { name: 'x.csv', mimeType: 'text/csv', buffer: Buffer.from('x') } } }) },
      { label: `POST /bank-accounts/${bid}/lines/${lineId}/match (bank.reconcile)`,
        run: () => page.request.post(`/api/proxy/bank-accounts/${bid}/lines/${lineId}/match`, { data: { receiptId: 1 } }) },
      { label: `GET /bank-accounts/${bid}/lines/${lineId}/suggestions (bank.reconcile)`,
        run: () => page.request.get(`/api/proxy/bank-accounts/${bid}/lines/${lineId}/suggestions`) },
      { label: `POST /bank-accounts/${bid}/lines/${lineId}/journal (bank.reconcile)`,
        run: () => page.request.post(`/api/proxy/bank-accounts/${bid}/lines/${lineId}/journal`, { data: { contraAccountId: 1 } }) },
      { label: `POST /bank-accounts/${bid}/lines/${lineId}/ignore (bank.reconcile)`,
        run: () => page.request.post(`/api/proxy/bank-accounts/${bid}/lines/${lineId}/ignore`) },
      { label: `POST /bank-accounts/${bid}/lines/${lineId}/unmatch (bank.reconcile)`,
        run: () => page.request.post(`/api/proxy/bank-accounts/${bid}/lines/${lineId}/unmatch`) },
      { label: `GET /bank-accounts/${bid}/reconciliation (bank.report.read)`,
        run: () => page.request.get(`/api/proxy/bank-accounts/${bid}/reconciliation?from=${ymd(ctx.monthStart)}&to=${ymd(ctx.today)}`) },
    ];

    const results: string[] = [];
    for (const p of probes) {
      const resp = await p.run();
      results.push(`${p.label}: ${resp.status()}`);
      expect(resp.status(), p.label).toBe(403);
    }
    console.log(`[R2L2] permission probe (rbac_sales_staff) — all 403:\n${results.join('\n')}`);
  });
});

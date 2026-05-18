import { test, expect } from '@playwright/test';
import { login, logout } from './_helpers';

// Sprint 7-half / KI-01: purchase.payment_voucher.{create,post,read} are now
// seeded + granted (180). A non-super AP clerk can run the PV lifecycle; a
// sales user (no purchase perms) is denied. All via the BFF proxy (cookie auth).
const API = '/api/proxy';
const today = new Date().toISOString().slice(0, 10);

test('ap_clerk can create→post a PV; approver approves (SoD)', async ({ page }) => {
  // ── admin (super) seeds a vendor + finds the SVC category ─────────────────
  await login(page, 'admin');
  const code = `RBAC-${Date.now().toString().slice(-7)}`;
  const mk = await page.request.post(`${API}/vendors/`, {
    data: {
      vendorCode: code, vendorType: 'Corporate', nameTh: 'ผู้ขาย rbac',
      nameEn: null, taxId: null, branchCode: null, branchName: null,
      vatRegistered: true, address: null, contactPerson: null, phone: null,
      email: null, paymentTermDays: 30, defaultCurrency: 'THB',
      defaultWhtTypeCode: null,
    },
  });
  expect(mk.status()).toBe(201);
  const vendors = await (await page.request.get(
    `${API}/vendors?search=${code}&pageSize=10`)).json();
  const vendorId = vendors[0].vendorId;
  const cats = await (await page.request.get(`${API}/expense-categories`)).json();
  const categoryId = cats.find((c: { categoryCode: string }) => c.categoryCode === 'SVC').categoryId;

  // ── ap_clerk (NOT super) creates the PV — was 403 before 180 ──────────────
  await logout(page);
  await login(page, 'ap_clerk');
  const create = await page.request.post(`${API}/payment-vouchers/`, {
    data: {
      docDate: today, vendorId, expenseCategoryId: categoryId,
      paymentMethod: 'Transfer', chequeNo: null, chequeDate: null,
      bankAccountId: null, currencyCode: 'THB', exchangeRate: 1,
      description: 'rbac e2e', notes: null,
      lines: [{
        expenseAccountId: null, description: 'rbac line', amount: 1000,
        taxCodeId: null, vatRate: 0, isRecoverableVat: true,
        whtTypeId: null, whtRate: 0,
      }],
      vendorInvoiceId: null,
    },
  });
  expect(create.status()).toBe(201);
  const pvId = (await create.json()).payment_voucher_id;

  // ── approver (different user — SoD) approves ──────────────────────────────
  await logout(page);
  await login(page, 'approver');
  const ap = await page.request.post(`${API}/payment-vouchers/${pvId}/approve`);
  expect(ap.status()).toBe(200);

  // ── back to ap_clerk: post + read ─────────────────────────────────────────
  await logout(page);
  await login(page, 'ap_clerk');
  const post = await page.request.post(`${API}/payment-vouchers/${pvId}/post`);
  expect(post.status()).toBe(200);
  const get = await page.request.get(`${API}/payment-vouchers/${pvId}`);
  expect(get.status()).toBe(200);
  expect((await get.json()).status).toBe('Posted');
});

test('sales_staff has no purchase perms → 403', async ({ page }) => {
  await login(page, 'sales_staff');
  const res = await page.request.get(`${API}/payment-vouchers/1`);
  expect(res.status()).toBe(403);
});

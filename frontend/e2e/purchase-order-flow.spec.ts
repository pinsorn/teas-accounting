import { test, expect } from '@playwright/test';
import { login, logout } from './_helpers';
import { attachVendorTaxInvoice } from './helpers/attachments';

// Sprint 12 — internal Purchase Order end-to-end (Answer-Sana-Backend17 DoD).
// Covers: create (ap_clerk) → approve by a DIFFERENT user (SoD, approver) →
// Outstanding-PO report lists it → mark-sent → a linked Vendor Invoice is
// posted → PO auto-closes (linked VI total ≥ 95% of PO) → Outstanding-PO
// drops it → the VI carries the linked-PO reference. All over the BFF proxy
// with cookie auth, mirroring payment-voucher-non-super-rbac.spec.ts.
const API = '/api/proxy';
const today = new Date().toISOString().slice(0, 10);

test('PO lifecycle: create → SoD approve → linked VI auto-closes → outstanding drops it', async ({ page }) => {
  // ── admin seeds a VAT vendor + resolves the SVC expense category ──────────
  await login(page, 'admin');
  const code = `POE2E-${Date.now().toString().slice(-7)}`;
  const mk = await page.request.post(`${API}/vendors/`, {
    data: {
      vendorCode: code, vendorType: 'Corporate', nameTh: 'ผู้ขาย po e2e',
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
  const categoryId = cats.find(
    (c: { categoryCode: string }) => c.categoryCode === 'SVC').categoryId;

  // ── ap_clerk (NOT super) creates a Draft PO — net 1000 + 7% VAT = 1070 ────
  await logout(page);
  await login(page, 'ap_clerk');
  const created = await page.request.post(`${API}/purchase-orders/`, {
    data: {
      docDate: today, expectedDeliveryDate: today, vendorId,
      businessUnitId: null, currencyCode: 'THB', exchangeRate: 1,
      notes: null, internalNotes: null,
      lines: [{
        productId: null, descriptionTh: 'PO e2e line', quantity: 1,
        uomText: 'ชิ้น', unitPrice: 1000, discountPercent: 0,
        taxCodeId: 1, taxCode: 'VAT7', taxRate: 0.07, notes: null,
      }],
    },
  });
  expect(created.status()).toBe(201);
  const poId = (await created.json()).purchase_order_id;

  // ── ap_clerk cannot approve their own PO (SoD CHECK ck_po_sod) ────────────
  const selfApprove = await page.request.post(`${API}/purchase-orders/${poId}/approve`);
  expect(selfApprove.status()).toBeGreaterThanOrEqual(400);

  // ── approver (different user) approves → Approved + a doc number ──────────
  await logout(page);
  await login(page, 'approver');
  const ap = await page.request.post(`${API}/purchase-orders/${poId}/approve`);
  expect(ap.status()).toBe(200);

  await logout(page);
  await login(page, 'admin');
  let po = await (await page.request.get(`${API}/purchase-orders/${poId}`)).json();
  expect(po.status).toBe('Approved');
  expect(po.docNo).toBeTruthy();
  const poDocNo: string = po.docNo;

  // ── Outstanding-PO report lists the still-open PO ─────────────────────────
  const before = await (await page.request.get(
    `${API}/reports/outstanding-po?as_of=${today}`)).json();
  expect(before.rows.some((r: { poId: number }) => r.poId === poId)).toBe(true);

  // ── ap_clerk marks it sent to the vendor ─────────────────────────────────
  await logout(page);
  await login(page, 'ap_clerk');
  const sent = await page.request.post(`${API}/purchase-orders/${poId}/mark-sent`);
  expect(sent.status()).toBe(204);

  // ── admin records + posts a Vendor Invoice linked to the PO (full amount) ─
  await logout(page);
  await login(page, 'admin');
  const viCreate = await page.request.post(`${API}/vendor-invoices/`, {
    data: {
      docDate: today, vendorId, vendorTaxInvoiceNo: `TIV-${poId}`,
      vendorTaxInvoiceDate: today, vatClaimPeriod: null,
      currencyCode: 'THB', exchangeRate: 1, notes: null,
      purchaseOrderId: poId,
      lines: [{
        expenseCategoryId: categoryId, expenseAccountId: null,
        description: 'VI from PO', amount: 1000, vatRate: 0.07,
      }],
      hasInputVat: true,
    },
  });
  expect(viCreate.status()).toBe(201);
  const viId = (await viCreate.json()).vendor_invoice_id;

  // C — vendor's ใบกำกับภาษีซื้อ file is required for VI Post (audit evidence).
  await attachVendorTaxInvoice(page.request, API, viId);

  const viPost = await page.request.post(`${API}/vendor-invoices/${viId}/post`);
  expect(viPost.status()).toBe(200);
  const postBody = await viPost.json();
  // 1070 / 1070 = 100% → within the 105% tolerance → no over-receipt chip.
  expect(postBody.poOverReceiptWarning ?? null).toBeNull();

  // ── PO auto-closed (linked VI ≥ 95% of PO total) ─────────────────────────
  po = await (await page.request.get(`${API}/purchase-orders/${poId}`)).json();
  expect(po.status).toBe('Closed');

  // ── Outstanding-PO no longer lists the closed PO ─────────────────────────
  const after = await (await page.request.get(
    `${API}/reports/outstanding-po?as_of=${today}`)).json();
  expect(after.rows.some((r: { poId: number }) => r.poId === poId)).toBe(false);

  // ── The Vendor Invoice carries the linked-PO reference (badge data) ──────
  const vi = await (await page.request.get(`${API}/vendor-invoices/${viId}`)).json();
  expect(vi.purchaseOrderId).toBe(poId);
  expect(vi.purchaseOrderDocNo).toBe(poDocNo);
});

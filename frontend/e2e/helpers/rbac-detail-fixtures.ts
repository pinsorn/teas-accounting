// Seeds the documents the RBAC UI-gating spec needs to exercise DETAIL-page
// lifecycle buttons (approve / post / cancel / create-from), each in the exact
// STATUS where its button is eligible. Created through the BFF proxy (so GL /
// numbering / immutability all fire — never hand-seeded past Draft).
//
// Scope: the VAT reference company (id 1) only. It is the one tenant with the
// full master-data chain (vendors, expense categories, customers, VAT tax codes);
// the non-VAT demo company (id 3) has none of those, and detail-button gating is
// a SCOPE proof (identical PermissionGate mechanism either way) — the dual-company
// story is the vatOnly hide/show already proven by the nav + create matrix.
//
// SoD: Purchase Orders carry a DB CHECK (ck_po_sod) that rejects self-approval, so
// the PO is created by AP_CLERK and approved by APPROVER (two different seeded
// users). Payment Vouchers are permission-based (no SoD CHECK since cont.77), so
// COMPANY_ADMIN creates + approves them.
import { type Page, expect } from '@playwright/test';
import { login } from '../_helpers';
import { usernameFor, type Control } from './rbac-manifest';
import { TestIds } from './test-ids';

const API = '/api/proxy';
const today = new Date().toISOString().slice(0, 10);
const claimPeriod = new Date().getFullYear() * 100 + (new Date().getMonth() + 1);

export type DetailIds = {
  pvDraftId: number;     // PV Draft   → pv-approve (+ pv-create-vi, VAT vendor, no VI)
  pvApprovedId: number;  // PV Approved→ pv-post
  poDraftId: number;     // PO Draft   → po-approve, po-cancel
  poApprovedId: number;  // PO Approved→ po-create-pv, po-mark-sent, po-close
  viDraftId: number;     // VI Draft   → vi-post
  tiDraftId: number;     // TI Draft   → ti-post-action
};

async function postOk(page: Page, path: string, data: unknown, label: string): Promise<any> {
  const r = await page.request.post(`${API}${path}`, data === undefined ? undefined : { data });
  if (r.status() >= 300) throw new Error(`${label} → ${r.status()} ${await r.text()}`);
  // Some creates (e.g. POST /vendors/) return 201 with an EMPTY body, and the
  // transitions return 200/204 with none — only parse JSON when there is a body.
  const body = await r.text();
  return body ? JSON.parse(body) : null;
}

/** Seed every detail fixture in the VAT reference company. Mutates nothing global. */
export async function seedDetailFixtures(page: Page, userPrefix: string): Promise<DetailIds> {
  const admin = usernameFor(userPrefix, 'COMPANY_ADMIN');
  const apClerk = usernameFor(userPrefix, 'AP_CLERK');
  const approver = usernameFor(userPrefix, 'APPROVER');

  // ── master data (as company admin) ─────────────────────────────────────────
  await login(page, admin);

  const vendorCode = TestIds.vendorCode('RBACUI');
  await postOk(page, '/vendors/', {
    vendorCode, vendorType: 'Corporate', nameTh: 'ผู้ขาย rbac-ui e2e', nameEn: null,
    taxId: null, branchCode: null, branchName: null, vatRegistered: true, address: null,
    contactPerson: null, phone: null, email: null, paymentTermDays: 30,
    defaultCurrency: 'THB', defaultWhtTypeCode: null,
  }, 'vendor create');
  const vendors = await (await page.request.get(`${API}/vendors?search=${vendorCode}&pageSize=10`)).json();
  const vendorId = (Array.isArray(vendors) ? vendors : vendors.items ?? [])[0].vendorId;

  const cats = await (await page.request.get(`${API}/expense-categories`)).json();
  const category = (cats as Array<{ categoryCode: string; categoryId: number }>)
    .find((c) => c.categoryCode === 'SVC') ?? cats[0];
  expect(category, 'an expense category must exist in company 1').toBeTruthy();
  const categoryId = category.categoryId;

  const customersRes = await (await page.request.get(`${API}/customers`)).json();
  const customers = Array.isArray(customersRes) ? customersRes : customersRes.items ?? [];
  expect(customers.length, 'a customer must exist in company 1').toBeGreaterThan(0);
  const customerId = customers[0].customerId;

  const pvBody = (desc: string) => ({
    docDate: today, vendorId, expenseCategoryId: categoryId, paymentMethod: 'Transfer',
    chequeNo: null, chequeDate: null, bankAccountId: null, currencyCode: 'THB',
    exchangeRate: 1, description: desc, notes: null,
    lines: [{ expenseAccountId: null, description: desc, amount: 1000, taxCodeId: 1, vatRate: 0.07, isRecoverableVat: true }],
  });
  const pvDraftId = (await postOk(page, '/payment-vouchers/', pvBody('PV draft (rbac-ui)'), 'PV draft')).payment_voucher_id;
  const pvApprovedId = (await postOk(page, '/payment-vouchers/', pvBody('PV approved (rbac-ui)'), 'PV for-approve')).payment_voucher_id;
  await postOk(page, `/payment-vouchers/${pvApprovedId}/approve`, undefined, 'PV approve');

  const viDraftId = (await postOk(page, '/vendor-invoices/', {
    docDate: today, vendorId, vendorTaxInvoiceNo: `RBACUI-${Date.now()}`, vendorTaxInvoiceDate: today,
    vatClaimPeriod: claimPeriod, currencyCode: 'THB', exchangeRate: 1, notes: null, purchaseOrderId: null,
    lines: [{ expenseCategoryId: categoryId, expenseAccountId: null, description: 'VI draft (rbac-ui)', amount: 1000, vatRate: 0.07 }],
    hasInputVat: true,
  }, 'VI draft')).vendor_invoice_id;

  const tiDraftId = (await postOk(page, '/tax-invoices/', {
    docDate: today, customerId, isTaxInclusive: false, currencyCode: 'THB', exchangeRate: 1,
    notes: null, paymentTerms: null, dueDate: null,
    lines: [{ productId: null, productCode: null, descriptionTh: 'TI draft (rbac-ui)', quantity: 1, uomId: 1, uomText: 'ชิ้น', unitPrice: 1000, discountPercent: 0, taxCodeId: 1, taxCode: 'VAT7', taxRate: 0.07 }],
  }, 'TI draft')).tax_invoice_id;

  // ── PO: SoD — AP_CLERK creates, APPROVER approves ──────────────────────────
  const poBody = (note: string) => ({
    docDate: today, expectedDeliveryDate: today, vendorId, businessUnitId: null,
    currencyCode: 'THB', exchangeRate: 1, notes: note, internalNotes: null,
    lines: [{ productId: null, descriptionTh: note, quantity: 1, uomText: 'ชิ้น', unitPrice: 1000, discountPercent: 0, taxCodeId: 1, taxCode: 'VAT7', taxRate: 0.07, notes: null }],
  });
  await login(page, apClerk);
  const poDraftId = (await postOk(page, '/purchase-orders/', poBody('PO draft (rbac-ui)'), 'PO draft')).purchase_order_id;
  const poApprovedId = (await postOk(page, '/purchase-orders/', poBody('PO approved (rbac-ui)'), 'PO for-approve')).purchase_order_id;
  await login(page, approver);
  await postOk(page, `/purchase-orders/${poApprovedId}/approve`, undefined, 'PO approve');

  return { pvDraftId, pvApprovedId, poDraftId, poApprovedId, viDraftId, tiDraftId };
}

/** Build the detail-page Controls for the seeded ids (VAT reference company). */
export function detailControls(ids: DetailIds): Control[] {
  const pvRead = 'purchase.payment_voucher.read';
  const poRead = 'purchase.purchase_order.read';
  return [
    { feature: 'PV detail: approve',           kind: 'button', detail: true, route: `/payment-vouchers/${ids.pvDraftId}`,    locate: { testId: 'pv-approve' },    perm: 'purchase.payment_voucher.approve', readPerm: pvRead },
    // NB: pv-create-vi is intentionally NOT asserted — beyond its own scope
    // (purchase.vendor_invoice.create) it renders only when the linked VENDOR
    // loads as VAT-registered (canCreateVi = vendor?.vatRegistered && no VI), so a
    // role without vendor-read sees nothing regardless of the action grant. It is a
    // cross-document "create-from" convenience, not a lifecycle gate; vendor_invoice
    // .create stays proven at the backend boundary (RbacCartesianTests).
    { feature: 'PV detail: post',              kind: 'button', detail: true, route: `/payment-vouchers/${ids.pvApprovedId}`, locate: { testId: 'pv-post' },       perm: 'purchase.payment_voucher.post',    readPerm: pvRead },
    { feature: 'PO detail: approve',           kind: 'button', detail: true, route: `/purchase-orders/${ids.poDraftId}`,     locate: { testId: 'po-approve' },    perm: 'purchase.purchase_order.approve',  readPerm: poRead },
    { feature: 'PO detail: cancel',            kind: 'button', detail: true, route: `/purchase-orders/${ids.poDraftId}`,     locate: { testId: 'po-cancel' },     perm: 'purchase.purchase_order.cancel',   readPerm: poRead },
    { feature: 'PO detail: create PV',         kind: 'button', detail: true, route: `/purchase-orders/${ids.poApprovedId}`,  locate: { testId: 'po-create-pv' },  perm: 'purchase.payment_voucher.create',  readPerm: poRead },
    { feature: 'PO detail: mark sent',         kind: 'button', detail: true, route: `/purchase-orders/${ids.poApprovedId}`,  locate: { testId: 'po-mark-sent' },  perm: 'purchase.purchase_order.create',   readPerm: poRead },
    { feature: 'PO detail: close',             kind: 'button', detail: true, route: `/purchase-orders/${ids.poApprovedId}`,  locate: { testId: 'po-close' },      perm: 'purchase.purchase_order.cancel',   readPerm: poRead },
    { feature: 'VI detail: post',              kind: 'button', detail: true, route: `/vendor-invoices/${ids.viDraftId}`,     locate: { testId: 'vi-post' },       perm: 'purchase.vendor_invoice.post',     readPerm: 'purchase.vendor_invoice.read' },
    { feature: 'TI detail: post',              kind: 'button', detail: true, route: `/tax-invoices/${ids.tiDraftId}`,        locate: { testId: 'ti-post-action' }, perm: 'sales.tax_invoice.post',          readPerm: 'sales.tax_invoice.read', vatOnly: true },
  ];
}

// Single source of truth for the FE permission-gating e2e + manual generator.
// kind: 'nav' (sidebar link, located by href) | 'button' (action, located by data-testid).
// perm: required permission (super-admin always passes). vatOnly: hidden on non-VAT companies.
// alwaysVisible: rendered for any authenticated user (no gate). superAdminOnly: is_super_admin only.
//
// Mirrors frontend/components/app-shell/SidebarNav.tsx (nav `perm`) and the
// `<PermissionGate scope=…>` wrapping each gated button. Cross-checked against
// docs/rbac/endpoint-permission-map.generated.md so no gated action is missed.
export type Control = {
  feature: string;            // human label for the manual
  kind: 'nav' | 'button';
  route: string;              // page to visit (detail-button routes carry a seeded id, see spec)
  locate: { href?: string; testId?: string };
  perm?: string;
  readPerm?: string;          // detail buttons: the page-load READ perm — the detail page
                              // won't render any action button if the doc GET 403s, so a role
                              // holding the action perm but not this read perm still sees nothing.
  vatOnly?: boolean;
  alwaysVisible?: boolean;
  superAdminOnly?: boolean;
  detail?: boolean;           // true = button lives on a detail page (needs a seeded doc id)
};

// 12 system roles (matches sys.roles / 110_seed_roles_and_permissions.sql exactly).
export const ROLES = [
  'SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AR_CLERK','AP_CLERK',
  'SALES_STAFF','PURCHASING_STAFF','WAREHOUSE_STAFF','APPROVER','AUDITOR','TAX_OFFICER',
] as const;

export const COMPANIES = [
  { key: 'vat',    companyId: 1, vatMode: true,  userPrefix: 'rbac_' },
  { key: 'nonvat', companyId: 3, vatMode: false, userPrefix: 'rbac_nv_' },
] as const;

export const usernameFor = (prefix: string, role: string) => prefix + role.toLowerCase();

// ── NAV (mirror SidebarNav.tsx, in section order) ─────────────────────────────
export const NAV: Control[] = [
  { feature: 'Dashboard',            kind: 'nav', route: '/', locate: { href: '/' }, alwaysVisible: true },
  { feature: 'Customers',            kind: 'nav', route: '/', locate: { href: '/customers' }, perm: 'master.customer.read' },
  { feature: 'Quotations',           kind: 'nav', route: '/', locate: { href: '/quotations' }, perm: 'sales.quotation.manage' },
  { feature: 'Sales orders',         kind: 'nav', route: '/', locate: { href: '/sales-orders' }, perm: 'sales.sales_order.manage' },
  { feature: 'Delivery orders',      kind: 'nav', route: '/', locate: { href: '/delivery-orders' }, perm: 'sales.delivery_order.manage' },
  { feature: 'Invoices (billing)',   kind: 'nav', route: '/', locate: { href: '/invoices' }, perm: 'sales.billing_note.read' },
  { feature: 'Tax invoices',         kind: 'nav', route: '/', locate: { href: '/tax-invoices' }, perm: 'sales.tax_invoice.read', vatOnly: true },
  { feature: 'Receipts',             kind: 'nav', route: '/', locate: { href: '/receipts' }, perm: 'sales.receipt.read' },
  { feature: 'Credit notes',         kind: 'nav', route: '/', locate: { href: '/credit-notes' }, perm: 'sales.credit_note.read', vatOnly: true },
  { feature: 'Debit notes',          kind: 'nav', route: '/', locate: { href: '/debit-notes' }, perm: 'sales.debit_note.read', vatOnly: true },
  { feature: 'Number gaps',          kind: 'nav', route: '/', locate: { href: '/number-gaps' }, perm: 'report.audit.read' },
  { feature: 'Vendors',              kind: 'nav', route: '/', locate: { href: '/vendors' }, perm: 'master.vendor.manage' },
  { feature: 'Purchase orders',      kind: 'nav', route: '/', locate: { href: '/purchase-orders' }, perm: 'purchase.purchase_order.read' },
  { feature: 'Payment vouchers',     kind: 'nav', route: '/', locate: { href: '/payment-vouchers' }, perm: 'purchase.payment_voucher.read' },
  { feature: 'Vendor invoices',      kind: 'nav', route: '/', locate: { href: '/vendor-invoices' }, perm: 'purchase.vendor_invoice.read' },
  { feature: 'WHT certificates',     kind: 'nav', route: '/', locate: { href: '/wht-certificates' }, perm: 'purchase.wht.read' },
  { feature: 'Payroll',              kind: 'nav', route: '/', locate: { href: '/payroll' }, perm: 'payroll.run.manage' },
  { feature: 'Tax summary',          kind: 'nav', route: '/', locate: { href: '/reports/tax-summary' }, perm: 'report.profit_loss.read' },
  { feature: 'Trial balance',        kind: 'nav', route: '/', locate: { href: '/reports/trial-balance' }, perm: 'report.trial_balance.read' },
  { feature: 'Profit & loss',        kind: 'nav', route: '/', locate: { href: '/reports/profit-loss' }, perm: 'report.profit_loss.read' },
  { feature: 'Sales summary',        kind: 'nav', route: '/', locate: { href: '/reports/sales-summary' }, perm: 'report.profit_loss.read' },
  { feature: 'ภ.พ.30',               kind: 'nav', route: '/', locate: { href: '/reports/pnd30' }, perm: 'tax.pnd30.read', vatOnly: true },
  { feature: 'Outstanding PO',       kind: 'nav', route: '/', locate: { href: '/reports/outstanding-po' }, perm: 'purchase.purchase_order.read' },
  { feature: 'AP aging',             kind: 'nav', route: '/', locate: { href: '/reports/ap-aging' }, perm: 'purchase.purchase_order.read' },
  { feature: 'Tax filings',          kind: 'nav', route: '/', locate: { href: '/tax-filings' }, perm: 'tax.filing.read' },
  { feature: 'Documents',            kind: 'nav', route: '/', locate: { href: '/documents' }, alwaysVisible: true },
  { feature: 'Missing WHT cert',     kind: 'nav', route: '/', locate: { href: '/tax-filings/missing-wht-cert' }, perm: 'tax.pnd53.read' },
  { feature: 'WHT receivable',       kind: 'nav', route: '/', locate: { href: '/reports/wht-receivable' }, perm: 'tax.pnd53.read' },
  { feature: 'Company profile (own)',kind: 'nav', route: '/', locate: { href: '/settings/company' }, alwaysVisible: true },
  { feature: 'Companies (tax cfg)',  kind: 'nav', route: '/', locate: { href: '/settings/companies' }, superAdminOnly: true },
  { feature: 'Roles admin',          kind: 'nav', route: '/', locate: { href: '/settings/roles' }, perm: 'sys.role.manage' },
  { feature: 'Users admin',          kind: 'nav', route: '/', locate: { href: '/settings/users' }, perm: 'sys.user.manage' },
  { feature: 'Products',             kind: 'nav', route: '/', locate: { href: '/settings/products' }, perm: 'master.product.manage' },
  { feature: 'Business units',       kind: 'nav', route: '/', locate: { href: '/settings/business-units' }, perm: 'master.business_unit.manage' },
  { feature: 'Employees',            kind: 'nav', route: '/', locate: { href: '/settings/employees' }, perm: 'master.employee.manage' },
  { feature: 'WHT types',            kind: 'nav', route: '/', locate: { href: '/settings/wht-types' }, perm: 'tax.wht_type.manage' },
  { feature: 'Expense categories',   kind: 'nav', route: '/', locate: { href: '/settings/expense-categories' }, perm: 'sys.expense_category.manage' },
  { feature: 'API keys',             kind: 'nav', route: '/', locate: { href: '/settings/api-keys' }, perm: 'sys.api_key.manage' },
];

// ── LIST CREATE BUTTONS (gated; located by data-testid from Task 2) ───────────
export const CREATE_BUTTONS: Control[] = [
  { feature: 'Create customer',        kind: 'button', route: '/customers',        locate: { testId: 'customer-create' },        perm: 'master.customer.manage' },
  { feature: 'Create vendor',          kind: 'button', route: '/vendors',          locate: { testId: 'vendor-create' },          perm: 'master.vendor.manage' },
  { feature: 'Create tax invoice',     kind: 'button', route: '/tax-invoices',     locate: { testId: 'tax-invoice-create' },     perm: 'sales.tax_invoice.create', vatOnly: true },
  { feature: 'Create receipt',         kind: 'button', route: '/receipts',         locate: { testId: 'receipt-create' },         perm: 'sales.receipt.create' },
  { feature: 'Create payment voucher', kind: 'button', route: '/payment-vouchers', locate: { testId: 'payment-voucher-create' }, perm: 'purchase.payment_voucher.create' },
  { feature: 'Create purchase order',  kind: 'button', route: '/purchase-orders',  locate: { testId: 'purchase-order-create' },  perm: 'purchase.purchase_order.create' },
  { feature: 'Create invoice',         kind: 'button', route: '/invoices',         locate: { testId: 'billing-note-create' },    perm: 'sales.billing_note.manage' },
  { feature: 'Create quotation',       kind: 'button', route: '/quotations',       locate: { testId: 'quotation-create' },       perm: 'sales.quotation.manage' },
];

// ── SETTINGS/COMPANY soft-profile controls (master.company_profile.manage) ────
// NB: the paid-up-capital card (master.company.manage, super-only, §4.6) is NOT
// asserted here — it self-hides on an async GET /companies inside the component,
// a second async condition the nav-gates-ready sentinel does not cover, and its
// gating story is already proven by the super-only "Companies (tax cfg)" nav +
// the backend RbacCartesianTests. It is listed in the manual's super-only appendix.
export const SETTINGS_COMPANY_BUTTONS: Control[] = [
  { feature: 'Edit registered address', kind: 'button', route: '/settings/company', locate: { testId: 'cp-edit-address' }, perm: 'master.company_profile.manage' },
  { feature: 'Upload logo',             kind: 'button', route: '/settings/company', locate: { testId: 'cp-logo-upload' },  perm: 'master.company_profile.manage' },
  { feature: 'Save company profile',    kind: 'button', route: '/settings/company', locate: { testId: 'cp-soft-save' },    perm: 'master.company_profile.manage' },
];

// Detail-page lifecycle buttons need a seeded document in the right STATUS; the spec
// seeds those per company in a beforeAll (see DETAIL_FIXTURES in the spec) and fills `route`.

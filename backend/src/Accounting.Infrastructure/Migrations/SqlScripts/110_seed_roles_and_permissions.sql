-- System roles (CLAUDE.md §4.1)
INSERT INTO sys.roles (role_code, role_name, description, is_system) VALUES
    ('SUPER_ADMIN',      'Super Admin',         'System-wide config, multi-tenant',            TRUE),
    ('COMPANY_ADMIN',    'Company Admin',       'Per-company admin',                            TRUE),
    ('CHIEF_ACCOUNTANT', 'Chief Accountant',    'สมุห์บัญชี — approves journals, closes period', TRUE),
    ('ACCOUNTANT',       'Accountant',          'Day-to-day bookkeeping',                       TRUE),
    ('AR_CLERK',         'AR Clerk',            'Sales documents, customer billing',            TRUE),
    ('AP_CLERK',         'AP Clerk',            'Vendor invoices, payments',                    TRUE),
    ('SALES_STAFF',      'Sales Staff',         'Quotation, Sales Order',                       TRUE),
    ('PURCHASING_STAFF', 'Purchasing Staff',    'PR, PO',                                       TRUE),
    ('WAREHOUSE_STAFF',  'Warehouse Staff',     'Stock movement',                               TRUE),
    ('APPROVER',         'Approver',            'Approval workflows',                           TRUE),
    ('AUDITOR',          'Auditor',             'Read-only audit',                              TRUE),
    ('TAX_OFFICER',      'Tax Officer',         'External สรรพากร audit role',                  TRUE)
ON CONFLICT (role_code) DO NOTHING;

-- Permissions — keep in sync with Accounting.Api.Authorization.Permissions
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.company.manage',          'master', 'company',           'manage', 'CRUD on Company master'),
    ('master.branch.manage',           'master', 'branch',            'manage', 'CRUD on Branch master'),
    ('master.customer.manage',         'master', 'customer',          'manage', 'CRUD on Customer master'),
    ('master.vendor.manage',           'master', 'vendor',            'manage', 'CRUD on Vendor master'),
    ('master.coa.manage',              'master', 'coa',               'manage', 'CRUD on Chart of Accounts'),
    ('sys.user.manage',                'sys',    'user',              'manage', 'User mgmt'),
    ('sys.role.manage',                'sys',    'role',              'manage', 'Role/permission assignment'),
    ('sys.doc_prefix.manage',          'sys',    'document_prefix',   'manage', 'Document prefix registry'),
    ('sys.expense_category.manage',    'sys',    'expense_category',  'manage', 'Expense category master'),
    ('gl.journal.create',              'gl',     'journal',           'create', 'Create journal voucher'),
    ('gl.journal.post',                'gl',     'journal',           'post',   'Post journal voucher'),
    ('gl.journal.read',                'gl',     'journal',           'read',   'View journal voucher'),
    ('sales.tax_invoice.create',       'sales',  'tax_invoice',       'create', 'Create Tax Invoice'),
    ('sales.tax_invoice.post',         'sales',  'tax_invoice',       'post',   'Post Tax Invoice'),
    ('sales.tax_invoice.read',         'sales',  'tax_invoice',       'read',   'View Tax Invoice'),
    ('report.audit.read',              'report', 'audit',             'read',   'Number-gap / audit reports')
ON CONFLICT (permission_code) DO NOTHING;

-- Super Admin → all permissions
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r CROSS JOIN sys.permissions p
WHERE r.role_code = 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

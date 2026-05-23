-- Sprint 13h P6.2 — Billing Note (ใบแจ้งหนี้/ใบวางบิล) permissions.
-- Keep in sync with Accounting.Api.Authorization.Permissions.Sales.BillingNote*.
-- Additive + idempotent. Mirrors the 270 seed pattern.
-- NB: never put curly braces in seed comments — EF ExecuteSqlRawAsync
-- treats them as format placeholders and fails at boot.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.billing_note.read',   'sales', 'billing_note', 'read',   'Billing Note list + detail GET'),
    ('sales.billing_note.manage', 'sales', 'billing_note', 'manage', 'Billing Note CRUD + lifecycle')
ON CONFLICT (permission_code) DO NOTHING;

-- Read tier — accountants, sales staff, AR clerks, auditors can view.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'sales.billing_note.read'
WHERE r.role_code IN (
    'SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT',
    'AR_CLERK','SALES_STAFF','AUDITOR')
ON CONFLICT DO NOTHING;

-- Manage tier — same roles that can manage Quotation/SO/DO can manage BN.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'sales.billing_note.manage'
WHERE r.role_code IN (
    'SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT',
    'AR_CLERK','SALES_STAFF')
ON CONFLICT DO NOTHING;

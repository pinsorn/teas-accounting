-- Sprint 13h P1 — Chapter 3 RBAC seed gap fix.
-- Adds master.customer.read + missing sales-side grants so demo-accountant
-- (ACCOUNTANT + AR_CLERK + AP_CLERK) and other non-admin roles can complete
-- the Q→SO→DO→TI→RC→CN/DN flow.
--
-- Additive + idempotent (ON CONFLICT DO NOTHING throughout).
-- Mirrors the 110 / 180 / 270 seed patterns. SUPER_ADMIN is granted the
-- full set elsewhere via cross-join and is intentionally omitted here.

-- 1. New permission code: master.customer.read (Sprint 13h).
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.customer.read', 'master', 'customer', 'read', 'Customer list + detail GET')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant master.customer.read to read-tier roles.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.customer.read'
WHERE r.role_code IN (
    'COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT',
    'AR_CLERK','SALES_STAFF','AP_CLERK','AUDITOR')
ON CONFLICT DO NOTHING;

-- 3. Grant master.customer.manage to write-tier roles.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.customer.manage'
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT')
ON CONFLICT DO NOTHING;

-- 4. Grant sales.tax_invoice.read to read-tier (ACCOUNTANT was missing).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'sales.tax_invoice.read'
WHERE r.role_code IN (
    'COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT',
    'AR_CLERK','SALES_STAFF','AUDITOR')
ON CONFLICT DO NOTHING;

-- 5. Grant sales.quotation/sales_order/delivery_order manage to ACCOUNTANT
--    (Sprint 10 seed 270 missed this role).
--    NB: never put curly braces in seed comments — EF ExecuteSqlRawAsync
--    treats them as format placeholders and fails at boot.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'sales.quotation.manage','sales.sales_order.manage','sales.delivery_order.manage')
WHERE r.role_code = 'ACCOUNTANT'
ON CONFLICT DO NOTHING;

-- 6. Grant sales.receipt and sales.credit_note/debit_note create+post
--    to ACCOUNTANT (also missing — same root cause as KI-01 / Sprint 13h scope).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'sales.receipt.create','sales.receipt.post',
    'sales.credit_note.create','sales.credit_note.post',
    'sales.debit_note.create','sales.debit_note.post')
WHERE r.role_code = 'ACCOUNTANT'
ON CONFLICT DO NOTHING;

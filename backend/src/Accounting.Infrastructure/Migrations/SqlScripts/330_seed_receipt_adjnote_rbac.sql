-- Sprint 13i B1 — SR2 RBAC complete grants.
-- Adds dedicated read perms for Receipt + Credit Note + Debit Note, and grants
-- the full read/create/post matrix across all sales roles. Sprint 13h seed 320
-- only granted ACCOUNTANT the create/post side; read-tier roles (AUDITOR,
-- SALES_STAFF) and AR_CLERK had no Receipt/CN/DN read path at all.
--
-- Additive + idempotent (ON CONFLICT DO NOTHING throughout). Mirrors 320.
-- NB: never put curly braces in seed comments — EF ExecuteSqlRawAsync treats
-- them as format placeholders and fails at boot.

-- 1. New read permission codes.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.receipt.read',     'sales', 'receipt',     'read', 'View Receipts'),
    ('sales.credit_note.read', 'sales', 'credit_note', 'read', 'View Credit Notes'),
    ('sales.debit_note.read',  'sales', 'debit_note',  'read', 'View Debit Notes')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant read to read-tier roles (everyone who can see the sales surfaces).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'sales.receipt.read', 'sales.credit_note.read', 'sales.debit_note.read')
WHERE r.role_code IN (
    'COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT',
    'AR_CLERK','SALES_STAFF','AUDITOR')
ON CONFLICT DO NOTHING;

-- 3. Grant create + post to write-tier roles.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'sales.receipt.create','sales.receipt.post',
    'sales.credit_note.create','sales.credit_note.post',
    'sales.debit_note.create','sales.debit_note.post')
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AR_CLERK')
ON CONFLICT DO NOTHING;

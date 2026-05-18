-- Sprint 10 A7 — Product master permissions. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Master. Additive + idempotent.
-- manage = CRUD (COMPANY_ADMIN + CHIEF_ACCOUNTANT + AR_CLERK, + SUPER_ADMIN
-- symmetry); read = ALL roles (line/auto-pickup needs every doc role to read).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.product.manage', 'master', 'product', 'manage', 'Create / edit / deactivate products'),
    ('master.product.read',   'master', 'product', 'read',   'View product master')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON (
        (p.permission_code = 'master.product.manage'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','AR_CLERK'))
     OR (p.permission_code = 'master.product.read'))   -- read → every role
ON CONFLICT DO NOTHING;

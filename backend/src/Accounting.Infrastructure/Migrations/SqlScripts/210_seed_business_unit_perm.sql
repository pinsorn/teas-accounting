-- Sprint 8 — master.business_unit.manage permission + grants. Additive,
-- idempotent (ON CONFLICT). Mirrors 180. Perms only, no user seeds (no $ literal).
-- Keep in sync with Accounting.Api.Authorization.Permissions.Master.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.business_unit.manage', 'master', 'business_unit', 'manage', 'CRUD on Business Unit master')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.business_unit.manage'
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT')
ON CONFLICT DO NOTHING;

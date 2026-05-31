-- Payroll P-C seed: run lifecycle permissions. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Payroll.*. Idempotent (ON CONFLICT).
-- SoD split mirrors PV: manage (create/draft) · post · pay.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('payroll.run.manage', 'payroll', 'run', 'manage', 'Create/edit draft payroll runs'),
    ('payroll.run.post',   'payroll', 'run', 'post',   'Approve + post a payroll run to the GL'),
    ('payroll.run.pay',    'payroll', 'run', 'pay',    'Mark a posted payroll run as paid')
ON CONFLICT (permission_code) DO NOTHING;

-- Grants: payroll is sensitive HR/finance → SUPER_ADMIN + company admin + chief accountant only
-- (mirror 440 employee-master grant). Same three roles hold all three payroll permissions.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN ('payroll.run.manage', 'payroll.run.post', 'payroll.run.pay')
WHERE r.role_code IN ('SUPER_ADMIN', 'COMPANY_ADMIN', 'CHIEF_ACCOUNTANT')
ON CONFLICT DO NOTHING;

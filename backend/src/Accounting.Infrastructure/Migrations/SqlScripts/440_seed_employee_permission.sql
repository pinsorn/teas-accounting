-- Payroll P-A seed: the Employee-master permission. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Master.EmployeeManage. Idempotent (ON CONFLICT).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.employee.manage', 'master', 'employee', 'manage', 'Manage Employees (payroll master)')
ON CONFLICT (permission_code) DO NOTHING;

-- Grants: payroll data is sensitive HR/finance → SUPER_ADMIN + company admin + chief
-- accountant only (NOT general accountants/clerks). Mirror 140's grant pattern.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.employee.manage'
WHERE r.role_code IN ('SUPER_ADMIN', 'COMPANY_ADMIN', 'CHIEF_ACCOUNTANT')
ON CONFLICT DO NOTHING;

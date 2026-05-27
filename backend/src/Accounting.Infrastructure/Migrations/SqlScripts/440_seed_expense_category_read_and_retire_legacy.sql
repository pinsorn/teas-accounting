-- BP-01 (RV2) — two changes, both additive + idempotent.
--
-- (A) New read-only permission `sys.expense_category.read` so PV/VI-creating roles can populate
--     the expense-category picker. The GET /expense-categories endpoint was relaxed from `manage`
--     to `read` (MasterEndpoints.cs); write (POST) still needs `manage`. Grant `read` to the same
--     role set that already holds the purchase create/read perms (180) PLUS the admins that hold
--     `manage` — because the GET now requires `read`, a manage-only role would otherwise lose list
--     access. (SUPER_ADMIN bypasses the check anyway; granted for completeness.)
--     Keep in sync with Accounting.Api.Authorization.Permissions.Sys.ExpenseCatRead.
--
-- (D) Retire the 3 legacy ad-hoc company-1 categories (SVC/OFF/ADS) superseded by the §17.3
--     canonical codes seeded in 430 (PROF/OFFI/MARK). Deactivate only (is_active=false) — never
--     delete: existing Payment Vouchers reference these category ids by FK, and tests may pick
--     them by code; deactivating just removes them from the new-document picker. RENT/ENT are
--     canonical and stay active.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sys.expense_category.read', 'sys', 'expense_category', 'read', 'View Expense Categories')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'sys.expense_category.read'
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AP_CLERK')
ON CONFLICT DO NOTHING;

UPDATE sys.expense_categories
   SET is_active = FALSE
 WHERE company_id = 1
   AND category_code IN ('SVC','OFF','ADS')
   AND is_active = TRUE;

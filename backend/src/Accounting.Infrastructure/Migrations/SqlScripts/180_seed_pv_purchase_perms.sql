-- Sprint 7-half KI-01 fix (plan.md section 23.1). The
-- purchase.payment_voucher.create/post/read permission constants exist in
-- Accounting.Api.Authorization.Permissions and gate PaymentVoucherEndpoints, but
-- no seed ever inserted the rows (110 had 14 non-purchase perms; 140 only added
-- vendor_invoice.* + payment_voucher.approve) so non-super users got 403.
-- Additive + idempotent (ON CONFLICT). 110/140 untouched. NO C# changes.
-- Keep in sync with Accounting.Api.Authorization.Permissions.Purchase.
--
-- NOTE: password_hash uses pgcrypto crypt() (gen_salt bf,12 = standard $2a
-- bcrypt, BCrypt.Net-verifiable) on purpose. A literal '$2a$12$...' string here
-- makes Npgsql's whole-file ExecuteSqlRaw parser treat $2/$12 as positional
-- params and throw FormatException ("Expected an ASCII digit"). pgcrypto is
-- created by both DbInitializer and PostgresFixture before scripts run.

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('purchase.payment_voucher.create', 'purchase', 'payment_voucher', 'create', 'Create Payment Voucher'),
    ('purchase.payment_voucher.post',   'purchase', 'payment_voucher', 'post',   'Post Payment Voucher'),
    ('purchase.payment_voucher.read',   'purchase', 'payment_voucher', 'read',   'View Payment Voucher')
ON CONFLICT (permission_code) DO NOTHING;

-- Grants -- same role set as 140's vendor_invoice.create/post/read for symmetry.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'purchase.payment_voucher.create',
    'purchase.payment_voucher.post',
    'purchase.payment_voucher.read')
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AP_CLERK')
ON CONFLICT DO NOTHING;

-- Non-super test users for the RBAC e2e (160 only seeds the super-admin
-- approver). DEV/SMOKE ONLY. Password = Admin@1234, hashed via pgcrypto bcrypt
-- (wf 12). is_super_admin = FALSE so the permission check is actually exercised.
INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES
    (3, 'ap_clerk', 'ap_clerk@teas.local',
     crypt('Admin@1234', gen_salt('bf', 12)),
     'AP Clerk', FALSE, TRUE, 0, FALSE, now(), now(), 0),
    (4, 'sales_staff', 'sales_staff@teas.local',
     crypt('Admin@1234', gen_salt('bf', 12)),
     'Sales Staff', FALSE, TRUE, 0, FALSE, now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 3, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r WHERE r.role_code = 'AP_CLERK'
ON CONFLICT DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 4, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r WHERE r.role_code = 'SALES_STAFF'
ON CONFLICT DO NOTHING;

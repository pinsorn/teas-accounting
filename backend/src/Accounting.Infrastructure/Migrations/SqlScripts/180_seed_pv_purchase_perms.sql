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

-- NOTE (first-run-bootstrap spec, 2026-06-17): the demo ap_clerk/sales_staff users that used
-- to live here were moved to 181_seed_demo_pv_users.sql (DEMO allowlist) so a fresh prod clone
-- seeds NO placeholder users. The permission catalog + grants above are SYSTEM data and stay here.

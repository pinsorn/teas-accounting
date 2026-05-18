-- Bootstrap admin user + a demo VAT-registered customer for company 1.
-- DEV/SMOKE ONLY. Password = 'Admin@1234' (BCrypt wf=12). Idempotent.

INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES (
    1, 'admin', 'admin@teas.local',
    '$2a$12$tcDd4AW644FX6PtGLdQrr.DwipxLCdjgT8/a1HbPL6Vwy/Je6yx6u',
    'System Admin',
    TRUE, TRUE, 0, FALSE,
    now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

-- Assign admin → SUPER_ADMIN in company 1 / branch 1 so the JWT carries company context.
INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 1, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r
WHERE r.role_code = 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

-- Demo VAT-registered customer (ม.86/4 #3 needs tax_id + branch_code).
INSERT INTO master.customers (
    company_id, customer_code, customer_type, name_th,
    tax_id, branch_code, vat_registered, billing_address,
    credit_limit, payment_term_days, default_currency, is_active, created_at)
VALUES (
    1, 'C-DEMO-001', 'CORPORATE', 'ลูกค้าทดสอบ จำกัด',
    '0105556123453', '00000', TRUE, '99 ถ.ทดสอบ กรุงเทพฯ 10110',
    0, 30, 'THB', TRUE, now())
ON CONFLICT (company_id, customer_code) DO NOTHING;

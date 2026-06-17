-- 181_seed_demo_pv_users.sql — DEMO/SMOKE ONLY (split out of 180 by first-run-bootstrap spec, 2026-06-17).
--
-- Non-super test users for the RBAC e2e (160 only seeds the super-admin approver). These belong to
-- demo company 1 and are useless on a fresh prod install, so they live in the DEMO allowlist and are
-- applied ONLY when Database:SeedDemoData=true. The purchase.payment_voucher.* PERMISSIONS + grants
-- they exercise stay in 180 (SYSTEM) — those are real catalog data the app needs regardless.
--
-- Password = 'Admin@1234', hashed via pgcrypto bcrypt (wf 12). is_super_admin = FALSE so the
-- permission check is actually exercised. Idempotent (ON CONFLICT). NB: pgcrypto crypt() not a
-- literal '$2a$...' string (Npgsql would treat $2/$12 as positional params and throw).

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
FROM sys.roles r WHERE r.role_code = 'AP_CLERK' AND r.company_id = 1
ON CONFLICT DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 4, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r WHERE r.role_code = 'SALES_STAFF' AND r.company_id = 1
ON CONFLICT DO NOTHING;

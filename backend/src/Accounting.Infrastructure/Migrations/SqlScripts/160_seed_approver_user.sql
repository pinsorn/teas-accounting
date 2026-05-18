-- Second user for B2 Segregation-of-Duties flows (creator ≠ approver).
-- DEV/SMOKE ONLY. Same password as admin ('Admin@1234', BCrypt wf=12). Idempotent.
-- Super-admin so it bypasses the purchase-permission grants (the non-super Purchase
-- RBAC seed is a separate pre-existing gap — see Report-Backend8 flag).

INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES (
    2, 'approver', 'approver@teas.local',
    '$2a$12$tcDd4AW644FX6PtGLdQrr.DwipxLCdjgT8/a1HbPL6Vwy/Je6yx6u',
    'PV Approver',
    TRUE, TRUE, 0, FALSE,
    now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 2, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r
WHERE r.role_code = 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

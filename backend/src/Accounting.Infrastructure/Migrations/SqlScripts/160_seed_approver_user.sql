-- Second user for B2 Segregation-of-Duties flows (creator ≠ approver).
-- DEV/SMOKE ONLY. Same password as admin ('Admin@1234', BCrypt wf=12). Idempotent.
--
-- Codex UI review 2026-08-20 R1 — was SUPER_ADMIN (bypassing every permission check, including
-- the RBAC/Users/Companies admin surface), defeating the Segregation of Duties this user exists
-- to demonstrate: the same login could create AND approve, and could edit its own permissions.
-- The old justification ("non-super Purchase RBAC seed is a separate pre-existing gap") is
-- closed: 140_seed_vendor_invoice_prefix_and_pv_approve.sql already grants
-- purchase.payment_voucher.approve to the APPROVER role — exactly what this user needs. Now a
-- plain non-super user holding ONLY the APPROVER role (role membership, same as any other
-- APPROVER-role holder — mirrors how 181_seed_demo_pv_users.sql grants ap_clerk/sales_staff
-- their roles, not a special-cased user).
--
-- Fresh installs only (160 is applied-once) — an existing DB where this already ran as
-- SUPER_ADMIN is repaired by 642_demote_approver_from_super_admin.sql.

INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
VALUES (
    2, 'approver', 'approver@teas.local',
    '$2a$12$tcDd4AW644FX6PtGLdQrr.DwipxLCdjgT8/a1HbPL6Vwy/Je6yx6u',
    'PV Approver',
    FALSE, TRUE, 0, FALSE,
    now(), now(), 0)
ON CONFLICT (user_id) DO NOTHING;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 2, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r
WHERE r.role_code = 'APPROVER'
ON CONFLICT DO NOTHING;

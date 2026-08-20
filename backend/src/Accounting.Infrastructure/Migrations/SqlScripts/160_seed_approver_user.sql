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
--
-- Tier-2 F-1 (specs/fix-codex-review-2026-08-20.md) — 160 is also replayed on the DOCUMENTED
-- post-510 path: DbInitializer deliberately does not record a SKIPPED demo script as applied
-- (DbInitializer.cs:141-148), so a DB first booted with SeedDemoData=false then later flipped to
-- true runs 160 for the FIRST time on a boot where 510 has ALREADY converted the role catalog to
-- per-company copies (636's runbook exercises exactly this). At that point there is NO global
-- (company_id IS NULL) APPROVER row — only per-company copies exist — so the original
-- `WHERE r.role_code = 'APPROVER'` (no company predicate, no bypass) either matches ZERO rows
-- under a real NOBYPASSRLS role (silently leaving approver with NO role at all) or, under a
-- connection that bypasses RLS via table ownership/superuser (teas_test/dev — memory: "RLS masked
-- by superuser tests"), matches EVERY company's own APPROVER copy — the INSERT's SELECT then
-- fans out one user_roles row per company (all hardcoded to company_id=1/branch_id=1 on the GRANT
-- itself), and PermissionLookup — which resolves permissions by role_id with no role-company
-- predicate — unions in every one of those companies' own APPROVER grants: a cross-tenant
-- permission leak. Fixed: `(r.company_id = 1 OR r.company_id IS NULL)` matches whichever shape is
-- actually present (global on a true fresh install, PRE-510 in file order; company-1's own copy
-- on a post-510 replay) while excluding every OTHER company's copy — and `SET LOCAL
-- app.bypass_rls` (same idiom as 642) makes the company-1 branch visible under RLS regardless of
-- boot ordering. A bare `company_id = 1` (641's own predicate style) would WRONGLY match nothing
-- on a true fresh install, where only the global row exists at 160's file-order position — do not
-- copy that predicate here.

SET LOCAL app.bypass_rls = 'on';

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
WHERE r.role_code = 'APPROVER' AND (r.company_id = 1 OR r.company_id IS NULL)
ON CONFLICT DO NOTHING;

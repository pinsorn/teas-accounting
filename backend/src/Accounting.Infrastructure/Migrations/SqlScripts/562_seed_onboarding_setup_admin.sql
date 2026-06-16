-- 562_seed_onboarding_setup_admin.sql — DEV/SMOKE ONLY (onboarding-wizard walkthrough).
-- A super-admin with NO company role assignment. LoginService picks the primary
-- (company,branch) from the first active role assignment; with none, the JWT carries
-- company_id=0 / branch_id=0 — the "no company yet" signal that the (dashboard) layout
-- uses to redirect to /onboarding (companyId===0 && isSuperAdmin). See spec
-- docs/superpowers/specs/2026-06-16-onboarding-switcher-nonvat-ch0.md.
--
-- Username 'setup-admin' / password 'Setup@1234' (BCrypt wf=12 via crypt+gen_salt('bf',12),
-- mirrors 130/440/550). is_super_admin = TRUE.
--
-- ⚠️ Deliberately NO sys.user_roles row — that omission IS the companyId=0 mechanism.
--    Do NOT copy the role-insert from 130/550 here.
-- Idempotent: guarded on both user_id and username so a re-apply is a no-op.

INSERT INTO sys.users (
    user_id, username, email, password_hash, full_name,
    is_super_admin, is_active, failed_login_count, must_change_password,
    created_at, updated_at, version)
SELECT
    9001, 'setup-admin', 'setup-admin@teas.local',
    crypt('Setup@1234', gen_salt('bf', 12)),
    'Setup Super Admin (no company)',
    TRUE, TRUE, 0, FALSE,
    now(), now(), 0
WHERE NOT EXISTS (
    SELECT 1 FROM sys.users
    WHERE user_id = 9001 OR username = 'setup-admin'
);

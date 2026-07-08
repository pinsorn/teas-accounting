-- Year-end closing (specs/year-end-closing.md D8/D2) — gl.year.close gates BOTH
-- close-year and reopen-year, granted to the same roles as gl.period.close
-- (CHIEF_ACCOUNTANT + COMPANY_ADMIN) + SUPER_ADMIN.
--
-- Idempotent; runs once (tracked). After 600 (lexical order). Insert-first/grant-second
-- in THIS file (520-before-530 seed-ordering bug — never split across files). NB: never
-- put curly braces here (EF ExecuteSqlRawAsync treats them as string.Format placeholders).
--
-- PROD HOTFIX (2026-07-09, v1.15.0 startup failure, troubles-wiki.md "Startup SqlScript
-- writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod") — step 4 below
-- reads sys.roles, which is 600's G3 group (system-global: company_id IS NULL OR
-- company_id = app.company_id OR app.bypass_rls). At startup NO app.company_id GUC is set, so
-- without a bypass only the company_id IS NULL rows are visible — the per-company fan-out
-- SELECT silently returns zero rows (no error, no crash) and grants nothing to any real
-- company. This is invisible on teas_test (the test DB connects as a Postgres SUPERUSER, which
-- bypasses RLS entirely — RbacMatrixTests was green there for the wrong reason). Fix: bypass
-- RLS for this script's own transaction via app.bypass_rls — EXACTLY the G3 policy's stated
-- purpose ("RBAC cross-company mgmt / cross-company audit writes", 600_superadmin_scoped_rls.sql
-- G3 comment). SET LOCAL is transaction-scoped and DbInitializer.ApplyScriptsAsync runs each
-- script in its own transaction, so this can never leak into any other script or request.
SET LOCAL app.bypass_rls = 'on';

-- 1. New permission code.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('gl.year.close', 'gl', 'year', 'close', 'Close/reopen fiscal year')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant to SUPER_ADMIN (system-global; explicit because 110's cross-join ran before this code existed).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'gl.year.close'
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

-- 3. Add to the per-company copy template so NEW companies inherit it — same role set as
--    gl.period.close (per specs/year-end-closing.md D8).
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, 'gl.year.close'
FROM (VALUES ('CHIEF_ACCOUNTANT'), ('COMPANY_ADMIN')) AS v(role_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. Fan out to every existing company's matching roles (idempotent; mirrors seed_company_roles).
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'gl.year.close'
WHERE r.role_code IN ('CHIEF_ACCOUNTANT', 'COMPANY_ADMIN')
  AND r.company_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
  );

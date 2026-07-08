-- Year-end closing (specs/year-end-closing.md D8/D2) — gl.year.close gates BOTH
-- close-year and reopen-year, granted to the same roles as gl.period.close
-- (CHIEF_ACCOUNTANT + COMPANY_ADMIN) + SUPER_ADMIN.
--
-- Idempotent; runs once (tracked). After 600 (lexical order). Insert-first/grant-second
-- in THIS file (520-before-530 seed-ordering bug — never split across files). NB: never
-- put curly braces here (EF ExecuteSqlRawAsync treats them as string.Format placeholders).

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

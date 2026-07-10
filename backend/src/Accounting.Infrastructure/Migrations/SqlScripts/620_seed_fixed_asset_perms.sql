-- Fixed Assets (specs/fixed-assets.md §8.3) — 4 new fixedasset.* permission codes.
-- Role split (Ham ruling, open question 2): COMPANY_ADMIN + CHIEF_ACCOUNTANT get all four
-- (read/manage/dispose/depreciation.run); ACCOUNTANT gets read/manage/depreciation.run only
-- (NOT dispose — disposal requires the higher role). SUPER_ADMIN (system-global) always gets
-- every fixedasset.% code.
--
-- Idempotent; runs once (tracked). Insert-first/grant-second in THIS file (RBAC seed-ordering
-- bug — never split across files). NO curly braces anywhere (ExecuteSqlRawAsync treats them as
-- string.Format placeholders — troubles-wiki 613).
--
-- VERBATIM structure copy of 617_seed_expense_claim_perms.sql (troubles-wiki "Startup SqlScript
-- writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod"). Step 4 reads
-- sys.roles, which is 600's G3 group (system-global: company_id IS NULL OR company_id =
-- app.company_id OR app.bypass_rls). At startup no app.company_id GUC is set, so without a
-- bypass only the company_id IS NULL rows are visible and the per-company fan-out SELECT
-- silently returns zero rows (no error, no crash) — invisible on teas_test (SUPERUSER
-- connection bypasses RLS entirely). Fix: bypass RLS for this script's own transaction via
-- app.bypass_rls. SET LOCAL is transaction-scoped and DbInitializer.ApplyScriptsAsync runs
-- each script in its own transaction, so this can never leak into any other script or request.
SET LOCAL app.bypass_rls = 'on';

-- 1. New permission codes.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('fixedasset.read',              'fixedasset', 'asset',        'read',    'View fixed assets / depreciation runs'),
    ('fixedasset.manage',            'fixedasset', 'asset',        'manage',  'Create/edit/activate/cancel fixed assets'),
    ('fixedasset.dispose',           'fixedasset', 'asset',        'dispose', 'Dispose/write off fixed assets (posts the GL journal entry)'),
    ('fixedasset.depreciation.run',  'fixedasset', 'depreciation', 'run',     'Generate/post monthly depreciation runs')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant every fixedasset.% code to SUPER_ADMIN (system-global; explicit — a cross-join seed
-- run before these codes existed would not have picked them up).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code LIKE 'fixedasset.%'
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

-- 3. Add to the per-company copy template so NEW companies inherit the split correctly.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, v.permission_code
FROM (VALUES
    ('COMPANY_ADMIN',    'fixedasset.read'),
    ('COMPANY_ADMIN',    'fixedasset.manage'),
    ('COMPANY_ADMIN',    'fixedasset.dispose'),
    ('COMPANY_ADMIN',    'fixedasset.depreciation.run'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.read'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.manage'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.dispose'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.depreciation.run'),
    ('ACCOUNTANT',       'fixedasset.read'),
    ('ACCOUNTANT',       'fixedasset.manage'),
    ('ACCOUNTANT',       'fixedasset.depreciation.run')
) AS v(role_code, permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. Fan out to every existing company's matching roles (idempotent; same role/code split as
-- step 3, mirrors seed_company_roles).
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.roles r
JOIN (VALUES
    ('COMPANY_ADMIN',    'fixedasset.read'),
    ('COMPANY_ADMIN',    'fixedasset.manage'),
    ('COMPANY_ADMIN',    'fixedasset.dispose'),
    ('COMPANY_ADMIN',    'fixedasset.depreciation.run'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.read'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.manage'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.dispose'),
    ('CHIEF_ACCOUNTANT', 'fixedasset.depreciation.run'),
    ('ACCOUNTANT',       'fixedasset.read'),
    ('ACCOUNTANT',       'fixedasset.manage'),
    ('ACCOUNTANT',       'fixedasset.depreciation.run')
) AS v(role_code, permission_code) ON v.role_code = r.role_code
JOIN sys.permissions p ON p.permission_code = v.permission_code
WHERE r.company_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
  );

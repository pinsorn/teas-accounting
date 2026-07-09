-- Bank reconciliation (specs/bank-reconciliation.md D5) — 5 new bank.* permission codes,
-- granted to COMPANY_ADMIN + CHIEF_ACCOUNTANT + ACCOUNTANT (+ SUPER_ADMIN system-global).
--
-- Idempotent; runs once (tracked). After 600 (lexical order). Insert-first/grant-second in
-- THIS file (520-before-530 seed-ordering bug — never split across files). NB: never put
-- curly braces here (EF ExecuteSqlRawAsync treats them as string.Format placeholders).
--
-- VERBATIM structure copy of 610_seed_year_close_perms.sql (troubles-wiki.md "Startup
-- SqlScript writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod").
-- Step 4 reads sys.roles, which is 600's G3 group (system-global: company_id IS NULL OR
-- company_id = app.company_id OR app.bypass_rls). At startup no app.company_id GUC is set,
-- so without a bypass only the company_id IS NULL rows are visible and the per-company
-- fan-out SELECT silently returns zero rows (no error, no crash) — invisible on teas_test
-- (SUPERUSER connection bypasses RLS entirely). Fix: bypass RLS for this script's own
-- transaction via app.bypass_rls — EXACTLY the G3 policy's stated purpose. SET LOCAL is
-- transaction-scoped and DbInitializer.ApplyScriptsAsync runs each script in its own
-- transaction, so this can never leak into any other script or request.
SET LOCAL app.bypass_rls = 'on';

-- 1. New permission codes.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('bank.account.read',     'bank', 'account',   'read',      'View bank accounts'),
    ('bank.account.manage',   'bank', 'account',   'manage',    'Create/update/deactivate bank accounts'),
    ('bank.statement.import', 'bank', 'statement', 'import',    'Import a bank statement (CSV/PDF)'),
    ('bank.reconcile',        'bank', 'line',      'reconcile', 'Match/unmatch/post/ignore statement lines'),
    ('bank.report.read',      'bank', 'report',    'read',      'View the bank reconciliation report')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant to SUPER_ADMIN (system-global; explicit because 110's cross-join ran before these codes existed).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code LIKE 'bank.%'
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

-- 3. Add to the per-company copy template so NEW companies inherit it.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, p.permission_code
FROM (VALUES ('COMPANY_ADMIN'), ('CHIEF_ACCOUNTANT'), ('ACCOUNTANT')) AS v(role_code)
CROSS JOIN sys.permissions p
WHERE p.permission_code LIKE 'bank.%'
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. Fan out to every existing company's matching roles (idempotent; mirrors seed_company_roles).
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code LIKE 'bank.%'
WHERE r.role_code IN ('COMPANY_ADMIN', 'CHIEF_ACCOUNTANT', 'ACCOUNTANT')
  AND r.company_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
  );

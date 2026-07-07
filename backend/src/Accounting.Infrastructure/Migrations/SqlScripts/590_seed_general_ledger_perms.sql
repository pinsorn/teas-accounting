-- General Ledger report (บัญชีแยกประเภท) — new permission code, same grant set as
-- report.trial_balance.read (Ham decision 2026-07-07: read-only drill-down report,
-- full scope). gl.journal.read (JE detail endpoint) was verified already seeded
-- (110_seed_roles_and_permissions.sql) AND granted (530_seed_rbac_grant_reconcile.sql,
-- section B8: ACCOUNTANT/CHIEF_ACCOUNTANT/AUDITOR/TAX_OFFICER/COMPANY_ADMIN) — no
-- change needed here.
--
-- Idempotent; runs once (tracked). After 585 (lexical order). Insert-first/grant-second
-- in THIS file (520-before-530 seed-ordering bug — never split across files). NB: never
-- put curly braces here (EF ExecuteSqlRawAsync treats them as string.Format placeholders).

-- 1. New permission code.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('report.general_ledger.read', 'report', 'general_ledger', 'read', 'View general ledger')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant to SUPER_ADMIN (system-global; explicit because 110's cross-join ran before this code existed).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'report.general_ledger.read'
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

-- 3. Add to the per-company copy template so NEW companies inherit it — same role set as
--    report.trial_balance.read (530 section B3).
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, 'report.general_ledger.read'
FROM (VALUES ('TAX_OFFICER'), ('AUDITOR'), ('ACCOUNTANT'), ('CHIEF_ACCOUNTANT'), ('COMPANY_ADMIN')) AS v(role_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. Fan out to every existing company's matching roles (idempotent; mirrors seed_company_roles).
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'report.general_ledger.read'
WHERE r.role_code IN ('TAX_OFFICER', 'AUDITOR', 'ACCOUNTANT', 'CHIEF_ACCOUNTANT', 'COMPANY_ADMIN')
  AND r.company_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
  );

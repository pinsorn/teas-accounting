-- Sprint 13k Plan 2 — split master.company_profile.manage out of master.company.manage.
--
-- WHY (Ham decision 2026-06-14): /company-profile/* (registered address, logo, contact/soft
-- fields = a company admin editing their OWN company) shared master.company.manage with
-- /companies (VAT/tax config), which is SUPER_ADMIN-only by §4.6. A company admin therefore
-- could not edit their own profile without breaching §4.6. This adds a dedicated permission
-- for the profile surface so COMPANY_ADMIN can manage it while /companies tax config stays
-- super-only on master.company.manage.
--
-- Idempotent; runs once (tracked). After 530 (lexical order). NB: never put curly braces here.

-- 1. New permission code.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.company_profile.manage', 'master', 'company_profile', 'manage',
     'Manage company soft profile (registered address, logo, contact)')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Grant to SUPER_ADMIN (system-global; explicit because 110's cross-join ran before this code existed).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.company_profile.manage'
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

-- 3. Add to the per-company copy template so NEW companies inherit it.
INSERT INTO sys.role_permission_templates (role_code, permission_code) VALUES
    ('COMPANY_ADMIN', 'master.company_profile.manage')
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. Fan out to every existing company's COMPANY_ADMIN role (idempotent; mirrors seed_company_roles).
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'master.company_profile.manage'
WHERE r.role_code = 'COMPANY_ADMIN' AND r.company_id IS NOT NULL
  AND NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
  );

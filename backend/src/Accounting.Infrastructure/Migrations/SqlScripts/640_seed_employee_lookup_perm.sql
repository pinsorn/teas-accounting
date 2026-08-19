-- specs/fix-r2-u6-employee-lookup.md (L4-1) — GET /employees/lookup is a NEW, narrower
-- endpoint (name-only DTO: employeeId/employeeCode/fullNameTh, no salary/national-id/bank) gated
-- by a NEW code master.employee.lookup, registered OUTSIDE the existing master.employee.manage
-- group. /employees (full CRUD, payroll data) stays exactly as-is — this script never touches
-- master.employee.manage.
--
-- Grant set: every role that holds expense.claim.create (the claim-form Employee picker's
-- caller) gets master.employee.lookup too — derived DYNAMICALLY in SQL from the EXISTING
-- expense.claim.create grants (not hardcoded role names), so a custom tenant role that already
-- holds expense.claim.create (now or created later) automatically inherits lookup access instead
-- of hitting the same 403 with no remedy. At seed time this resolves to (at minimum) ACCOUNTANT,
-- CHIEF_ACCOUNTANT, COMPANY_ADMIN — the 3 roles 617_seed_expense_claim_perms.sql's template
-- grants expense.claim.create to.
--
-- Insert-first/grant-second in THIS file (memory: rbac-seed-ordering-footgun — a grant whose
-- code is inserted by a LATER-numbered script silently no-ops).
--
-- MECHANISM + RLS: identical to 627/629 (see those files' headers) — sys.permissions and
-- sys.role_permission_templates carry no company_id / RLS, safe without any GUC. sys.roles +
-- sys.role_permissions are under FORCE RLS (510), so the per-company sync (step 3) and the
-- direct-grant regression guard (step 4, mirrors 629's step 5b — Opus Tier-2 review F1,
-- 2026-07-21: a role holding expense.claim.create ONLY via a direct sys.role_permissions grant,
-- e.g. RbacAdminService.SetRolePermissionsAsync or a company-local custom role from
-- CreateRoleAsync, has NO row in sys.role_permission_templates for step 2/3 to see) both loop per
-- company under a transaction-local set_config('app.company_id', ...), NOBYPASSRLS, no superuser
-- assumption.
--
-- Idempotent; runs once (tracked in sys.applied_sql_scripts). Numbered 640 → after 639 (639 is
-- reserved for U2's possible seed; not taken by this script).
-- NB: NEVER put curly braces anywhere in this file (EF ExecuteSqlRawAsync treats them as
-- string.Format placeholders and fails at boot — troubles-wiki 2026-08-18 regex-quantifier scar).

-- 1. Code-first insert. sys.permissions carries no company_id / RLS — safe without any GUC.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('master.employee.lookup', 'master', 'employee', 'lookup',
     'Name-only employee lookup for document pickers (no payroll data)')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Top up the copy template — every role_code that currently holds expense.claim.create in the
--    template also gets master.employee.lookup, so NEW companies inherit it correctly.
--    sys.role_permission_templates carries no company_id / RLS — safe without any GUC.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'master.employee.lookup'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'expense.claim.create'
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 3-4. Per-company sync, looped under FORCE RLS.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        -- 3. Re-sync the (now topped-up) template to every existing per-company role — heals any
        --    company whose role already exists but is missing the newly-templated grant.
        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT r.role_id, p.permission_id, r.company_id
        FROM sys.role_permission_templates t
        JOIN sys.roles r       ON r.role_code = t.role_code AND r.company_id = c.company_id
        JOIN sys.permissions p ON p.permission_code = t.permission_code
        WHERE t.permission_code = 'master.employee.lookup'
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions rp
            WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
          );

        -- 4. DIRECT-GRANT REGRESSION GUARD (mirrors 629 step 5b) — a role holding
        --    expense.claim.create ONLY via a direct sys.role_permissions grant (no template row)
        --    must still resolve master.employee.lookup.
        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT rp.role_id, pl.permission_id, rp.company_id
        FROM sys.role_permissions rp
        JOIN sys.permissions pc ON pc.permission_id = rp.permission_id AND pc.permission_code = 'expense.claim.create'
        JOIN sys.permissions pl ON pl.permission_code = 'master.employee.lookup'
        WHERE rp.company_id = c.company_id
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions x
            WHERE x.role_id = rp.role_id AND x.permission_id = pl.permission_id
          );
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

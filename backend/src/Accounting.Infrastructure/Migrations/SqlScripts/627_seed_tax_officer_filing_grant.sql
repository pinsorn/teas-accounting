-- CRIT-2 (specs/fix-swarm-crit-numbering-rbac.md) — TAX_OFFICER cannot run any tax filing
-- (ภ.พ.30 / ภ.ง.ด.3/53/54/36/51/50, VAT registration prefill) despite the role holding
-- tax.pnd30.read etc. Root cause: TaxFilingEndpoints.cs actually gates preview + PDF + .txt/batch
-- export on tax.filing.preview (list history on tax.filing.read), and
-- 530_seed_rbac_grant_reconcile.sql never granted either to TAX_OFFICER — a pure role-seed gap
-- (the VAT numbers themselves tie out; not a computation bug).
--
-- Scope (Fable judgement call, per spec): TAX_OFFICER gets preview + read (view filing history) +
-- the read-only .txt/PDF export — everything TaxFilingEndpoints.cs gates behind tax.filing.preview
-- or tax.filing.read. It does NOT get tax.filing.finalize — that stays a Chief/Admin-only action
-- (a separate SoD line; mode=finalize is additionally guarded in-handler regardless of this grant).
--
-- Both permission codes already exist (241_seed_tax_filing_perms.sql). Re-inserted here anyway,
-- idempotently, as the sanctioned code-first-then-grant defensive habit (memory:
-- rbac-seed-ordering-footgun — a grant silently no-ops if the code doesn't exist yet when this
-- script runs).
--
-- MECHANISM (per-company world, post-510 — mirrors 530's Phase D pattern): role_permissions
-- carries company_id and new companies clone grants from sys.role_permission_templates via
-- sys.seed_company_roles, NOT from role_permissions directly. So this script (1) tops up the
-- TEMPLATE, then (2) re-syncs the full template to every existing per-company TAX_OFFICER role.
--
-- RLS: 510_per_company_roles_reconcile.sql put sys.roles + sys.role_permissions under FORCE ROW
-- LEVEL SECURITY (company_isolation, keyed on current_setting('app.company_id')) — sys.permissions
-- and sys.role_permission_templates are global/unprotected (no company_id column). Runs under the
-- NOBYPASSRLS app role, no superuser assumption (memory: v1.22.0 died on 625 running as superuser).
-- Step 3 therefore loops per company with a transaction-local set_config('app.company_id', ...)
-- (mirrors 625) — without it, the sys.roles JOIN's SELECT side would be silently filtered to ZERO
-- rows by RLS (no error, just a no-op grant) rather than reaching every company.
--
-- Idempotent; runs once (tracked in sys.applied_sql_scripts). Numbered 627 → after 241 (permission
-- codes already exist) and after 510/530 (per-company role_permission_templates mechanism is live).
-- NB: NEVER put curly braces in this file (EF ExecuteSqlRawAsync treats them as string.Format
-- placeholders and fails at boot).

-- 1. Code-first (defensive; both codes already exist from 241) — ON CONFLICT DO NOTHING.
--    sys.permissions carries no company_id / RLS — safe without any GUC.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('tax.filing.preview', 'tax', 'filing', 'preview', 'Preview a tax filing (ภ.พ.30 / ภ.ง.ด.)'),
    ('tax.filing.read',    'tax', 'filing', 'read',    'View historical tax filings')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Top up the copy template with the TAX_OFFICER grants. sys.role_permission_templates carries
--    no company_id / RLS either — safe without any GUC.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, v.permission_code
FROM (VALUES
    ('TAX_OFFICER', 'tax.filing.preview'),
    ('TAX_OFFICER', 'tax.filing.read')
) AS v(role_code, permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 3. Re-sync the full template to every existing per-company TAX_OFFICER role (idempotent),
--    looped per company under FORCE RLS. Mirrors 530's Phase D2 resync — heals any company whose
--    TAX_OFFICER role already exists but is missing the newly-templated grants.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT r.role_id, p.permission_id, r.company_id
        FROM sys.role_permission_templates t
        JOIN sys.roles r       ON r.role_code = t.role_code AND r.company_id = c.company_id
        JOIN sys.permissions p ON p.permission_code = t.permission_code
        WHERE t.role_code = 'TAX_OFFICER'
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions rp
            WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
          );
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

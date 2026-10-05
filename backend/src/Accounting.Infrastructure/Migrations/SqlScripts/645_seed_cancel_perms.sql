-- specs/cancel-reissue-sales-docs.md section 3.2.4 + 10.1 O1 (WP-1) - seed the three cancel permission codes.
--   sales.tax_invoice.cancel, sales.receipt.cancel -> CHIEF_ACCOUNTANT and COMPANY_ADMIN.
--   sales.billing_note.cancel -> same two roles AND mirror-granted to every role (template and
--   per-company role_permissions) that already holds sales.billing_note.manage, because the
--   Invoice cancel endpoint moves from .manage to .cancel and nobody may lose invoice-cancel.
--
-- Insert-first/grant-second in THIS file (a grant whose code is inserted by a later-numbered
-- script silently no-ops).
--
-- RLS: sys.permissions and sys.role_permission_templates carry no company_id / RLS, so steps 1-2
-- need no GUC. sys.roles and sys.role_permissions are under FORCE RLS, and startup runs as the app
-- role (NOBYPASSRLS) with no app.company_id, so step 3 loops per company under a transaction-local
-- set_config (pattern of 640). Without the loop the SELECT side is filtered to zero rows silently.
--
-- Idempotent; NOT EXISTS / ON CONFLICT guards throughout. Runs once (sys.applied_sql_scripts).
-- Do not put literal curly-brace characters anywhere in this file, comments included.

-- 1. Code-first insert.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.tax_invoice.cancel', 'sales', 'tax_invoice', 'cancel',
     'Cancel / cancel-and-reissue a posted Tax Invoice'),
    ('sales.receipt.cancel', 'sales', 'receipt', 'cancel',
     'Cancel / cancel-and-reissue a posted Receipt'),
    ('sales.billing_note.cancel', 'sales', 'billing_note', 'cancel',
     'Cancel an Invoice (billing note), reversing its accrual JE when one was posted')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Template grants (new companies inherit them).
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT r.role_code, p.permission_code
FROM (VALUES ('CHIEF_ACCOUNTANT'), ('COMPANY_ADMIN')) AS r(role_code)
CROSS JOIN (VALUES ('sales.tax_invoice.cancel'), ('sales.receipt.cancel'), ('sales.billing_note.cancel'))
     AS p(permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 2b. O1 mirror: every template role holding sales.billing_note.manage also gets the cancel code.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'sales.billing_note.cancel'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'sales.billing_note.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 3. Per-company sync, looped under FORCE RLS.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        -- 3a. Template re-sync to every existing per-company role for the three new codes.
        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT r.role_id, p.permission_id, r.company_id
        FROM sys.role_permission_templates t
        JOIN sys.roles r       ON r.role_code = t.role_code AND r.company_id = c.company_id
        JOIN sys.permissions p ON p.permission_code = t.permission_code
        WHERE t.permission_code IN ('sales.tax_invoice.cancel', 'sales.receipt.cancel', 'sales.billing_note.cancel')
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions rp
            WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
          );

        -- 3b. O1 mirror on the live grants: any role (template-derived or direct grant, including
        --     custom roles) holding sales.billing_note.manage also gets sales.billing_note.cancel.
        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT rp.role_id, pc.permission_id, rp.company_id
        FROM sys.role_permissions rp
        JOIN sys.permissions pm ON pm.permission_id = rp.permission_id
                               AND pm.permission_code = 'sales.billing_note.manage'
        JOIN sys.permissions pc ON pc.permission_code = 'sales.billing_note.cancel'
        WHERE rp.company_id = c.company_id
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions x
            WHERE x.role_id = rp.role_id AND x.permission_id = pc.permission_id
          );
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

-- 4. O1 mirror for the system-global role (SUPER_ADMIN, company_id IS NULL): it holds
--    sales.billing_note.manage explicitly and is outside the per-company loop above. Same
--    transaction-local bypass as 620 (SET LOCAL is scoped to this script's own transaction).
SET LOCAL app.bypass_rls = 'on';
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT rp.role_id, pc.permission_id
FROM sys.role_permissions rp
JOIN sys.roles r        ON r.role_id = rp.role_id AND r.company_id IS NULL
JOIN sys.permissions pm ON pm.permission_id = rp.permission_id
                       AND pm.permission_code = 'sales.billing_note.manage'
JOIN sys.permissions pc ON pc.permission_code = 'sales.billing_note.cancel'
WHERE NOT EXISTS (
    SELECT 1 FROM sys.role_permissions x
    WHERE x.role_id = rp.role_id AND x.permission_id = pc.permission_id
);

-- 4b. SUPER_ADMIN (system-global, company_id IS NULL) also gets the tax invoice and receipt cancel
--     codes, like 620 gives it every new code. Still under the bypass set in step 4.
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN ('sales.tax_invoice.cancel', 'sales.receipt.cancel')
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

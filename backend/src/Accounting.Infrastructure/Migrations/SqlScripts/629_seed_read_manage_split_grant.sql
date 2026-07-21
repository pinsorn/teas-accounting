-- specs/fix-swarm-findings-all.md WP6 (follow-up from WP2 / 628's header) — read/manage split for
-- quotations, sales-orders, delivery-orders, vendors, business-units.
--
-- WP2 (628) found these 5 resources gate BOTH list/get/PDF (read) AND every write/lifecycle action
-- on ONE combined `.manage` code, so AUDITOR (or any read-only role) could never be granted read
-- visibility without also getting write. WP6 splits each into `<resource>.read` (list/get/PDF —
-- matches the SalesChainEndpoints.cs / MasterEndpoints.cs vendor group / BusinessUnitEndpoints.cs
-- endpoint changes shipped in the same commit) while every write/lifecycle route KEEPS `.manage`.
-- Mirrors the 3 existing in-repo precedents: Customer (master.customer.read/manage), BankAccount
-- (bank.account.read/manage), ExpenseCategory (sys.expense_category.read/manage).
--
-- New codes:
--   sales.quotation.read       (pairs with sales.quotation.manage)
--   sales.sales_order.read     (pairs with sales.sales_order.manage)
--   sales.delivery_order.read  (pairs with sales.delivery_order.manage)
--   master.vendor.read         (pairs with master.vendor.manage)
--   master.business_unit.read  (pairs with master.business_unit.manage)
--
-- REGRESSION GUARD (the risky part): every role whose TEMPLATE currently holds the paired `.manage`
-- code must also get the new `.read` code, else that role silently loses list/detail/PDF access the
-- instant the endpoint split ships (its manage grant no longer covers reads, per SalesChainEndpoints/
-- MasterEndpoints/BusinessUnitEndpoints removing the group-level manage-only gate). Step 2 below
-- derives the affected role set DYNAMICALLY from sys.role_permission_templates (SELECT role_code ...
-- WHERE permission_code = '<manage>') rather than hardcoding a role list, so any custom/future grant
-- of the manage code is automatically covered too.
--
-- REGRESSION GUARD PART 2 (Opus Tier-2 review F1, 2026-07-21): the template is NOT the only place a
-- `.manage` grant can live. RbacAdminService.SetRolePermissionsAsync (RbacAdminService.cs:119) writes
-- custom per-company grants straight to sys.role_permissions with no backing template row, and
-- CreateRoleAsync (line 174) makes company-local custom roles that never exist in the template at
-- all. Step 2's template-derived top-up cannot see either case — a custom role/grant holding one of
-- the 5 `.manage` codes would silently lose read access on deploy. Step 5b below fixes this by
-- operating directly on sys.role_permissions (the EFFECTIVE per-company grants), inside the same
-- per-company loop as step 5's template resync, so both the templated AND the custom-grant paths are
-- covered.
--
-- Also grants (per specs/fix-swarm-findings-all.md WP6 + swarm-findings/audit01.md):
--   * AUDITOR gets all 5 new .read codes (the original WP2 ask — read-only audit visibility).
--   * master.business_unit.read ALSO goes to AR_CLERK, AP_CLERK, SALES_STAFF, PURCHASING_STAFF,
--     WAREHOUSE_STAFF, TAX_OFFICER, APPROVER — the read-heavy roles 628's header identified as
--     lacking master.business_unit.manage today (confirmed via seed grep), so business-unit-tagged
--     document forms 403 the BU picker for them (~25 console 403s app-wide). None of these 7 hold
--     the manage code, so step 2's dynamic top-up does not reach them — granted explicitly here.
--
-- NOT granted: write/lifecycle access to any of the 5 resources for any role that didn't already
-- hold the corresponding .manage code. This script only ever adds a .read grant, never a .manage one.
--
-- MECHANISM + RLS: identical to 627/628 (see those files' headers) — top up
-- sys.role_permission_templates (global, no RLS), then re-sync per company under a transaction-local
-- set_config('app.company_id', ...) loop, under the NOBYPASSRLS app role, no superuser assumption.
--
-- Idempotent; runs once (tracked in sys.applied_sql_scripts). Numbered 629 -> after 628.
-- NB: NEVER put curly braces in this file (EF ExecuteSqlRawAsync treats them as string.Format
-- placeholders and fails at boot).

-- 1. Code-first insert of the 5 new read codes (module/resource match each paired manage code's
--    original seed row from 110/210/270). sys.permissions carries no company_id / RLS, safe without
--    any GUC.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.quotation.read',      'sales',  'quotation',      'read', 'View / list / PDF Quotation'),
    ('sales.sales_order.read',    'sales',  'sales_order',    'read', 'View / list / PDF Sales Order'),
    ('sales.delivery_order.read', 'sales',  'delivery_order', 'read', 'View / list / PDF Delivery Order'),
    ('master.vendor.read',        'master', 'vendor',         'read', 'View / list Vendor'),
    ('master.business_unit.read', 'master', 'business_unit',  'read', 'View / list Business Unit')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. REGRESSION GUARD — dynamically re-grant the new .read to every role whose template currently
--    holds the paired .manage code. sys.role_permission_templates carries no company_id / RLS
--    either — safe without any GUC. Derived from the table itself, not hardcoded, so a custom grant
--    of the manage code (now or in the future) is covered automatically.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'sales.quotation.read'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'sales.quotation.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'sales.sales_order.read'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'sales.sales_order.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'sales.delivery_order.read'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'sales.delivery_order.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'master.vendor.read'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'master.vendor.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT t.role_code, 'master.business_unit.read'
FROM sys.role_permission_templates t
WHERE t.permission_code = 'master.business_unit.manage'
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 3. AUDITOR gets all 5 new .read codes (the original WP2 read-only-visibility ask that 628
--    couldn't grant without this split).
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, v.permission_code
FROM (VALUES
    ('AUDITOR', 'sales.quotation.read'),
    ('AUDITOR', 'sales.sales_order.read'),
    ('AUDITOR', 'sales.delivery_order.read'),
    ('AUDITOR', 'master.vendor.read'),
    ('AUDITOR', 'master.business_unit.read')
) AS v(role_code, permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 4. master.business_unit.read to the read-heavy roles that don't hold business_unit.manage today
--    (628 header) — kills the BU-picker 403 spam app-wide for these roles.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, 'master.business_unit.read'
FROM (VALUES
    ('AR_CLERK'), ('AP_CLERK'), ('SALES_STAFF'), ('PURCHASING_STAFF'),
    ('WAREHOUSE_STAFF'), ('TAX_OFFICER'), ('APPROVER')
) AS v(role_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 5. Re-sync the full template to every existing per-company role (idempotent), looped per company
--    under FORCE RLS. Scoped to just the 5 new .read codes — covers every role_code that now holds
--    one of them in the template (steps 2-4 above), whatever that role_code turns out to be, without
--    re-touching unrelated grants.
--
-- 5b. DIRECT-GRANT REGRESSION GUARD (Opus F1) — catches a role holding a `.manage` code ONLY in
--     sys.role_permissions (custom grant via RbacAdminService.SetRolePermissionsAsync, or a
--     company-local custom role from CreateRoleAsync) with no template row for step 2/5 to see.
--     Operates on the EFFECTIVE per-company grants directly instead of the template. Same
--     per-company loop, same FORCE RLS scoping, runs AFTER the template resync above.
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
        WHERE t.permission_code IN (
            'sales.quotation.read', 'sales.sales_order.read', 'sales.delivery_order.read',
            'master.vendor.read', 'master.business_unit.read')
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions rp
            WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
          );

        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT rp.role_id, pr.permission_id, rp.company_id
        FROM sys.role_permissions rp
        JOIN sys.permissions pm ON pm.permission_id = rp.permission_id
        JOIN (VALUES
            ('sales.quotation.manage',      'sales.quotation.read'),
            ('sales.sales_order.manage',    'sales.sales_order.read'),
            ('sales.delivery_order.manage', 'sales.delivery_order.read'),
            ('master.vendor.manage',        'master.vendor.read'),
            ('master.business_unit.manage', 'master.business_unit.read')
        ) AS pairs(manage_code, read_code) ON pairs.manage_code = pm.permission_code
        JOIN sys.permissions pr ON pr.permission_code = pairs.read_code
        WHERE rp.company_id = c.company_id
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions x
            WHERE x.role_id = rp.role_id AND x.permission_id = pr.permission_id
          );
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

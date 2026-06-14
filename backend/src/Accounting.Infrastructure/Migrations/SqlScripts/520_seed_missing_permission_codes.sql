-- Sprint 13k — close a pre-existing RBAC seed gap exposed by the per-company role admin UI.
--
-- 14 permission codes are declared in Accounting.Api.Authorization.Permissions (the C# source
-- of truth, 66 codes) AND enforced on endpoints via RequireAuthorization, but were never inserted
-- into sys.permissions by any historical seed. Effect: those endpoints were reachable ONLY by
-- super-admins (is_super_admin flag bypass) — no role could be granted them, because the grant
-- target did not exist. The new admin UI lists all 66 catalog codes, so an admin would otherwise
-- get rbac.unknown_permission when trying to grant one of the 14.
--
-- This script makes the catalog GRANTABLE (additive, idempotent) and re-grants SUPER_ADMIN the
-- full set so admin's /me/permissions reflects all 66. It does NOT auto-grant the new codes to any
-- other role — that is a deliberate policy choice now made through the admin UI.
--
-- Runs after 510 (per-company conversion). SUPER_ADMIN is the single system-global role
-- (company_id IS NULL); its grants stay global (company_id NULL). No curly braces (EF format).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('gl.period.close',          'gl',      'period',         'close',  'Close an accounting period'),
    ('sales.receipt.create',     'sales',   'receipt',        'create', 'Create Receipt'),
    ('sales.receipt.post',       'sales',   'receipt',        'post',   'Post Receipt'),
    ('sales.credit_note.create', 'sales',   'credit_note',    'create', 'Create Credit Note'),
    ('sales.credit_note.post',   'sales',   'credit_note',    'post',   'Post Credit Note'),
    ('sales.debit_note.create',  'sales',   'debit_note',     'create', 'Create Debit Note'),
    ('sales.debit_note.post',    'sales',   'debit_note',     'post',   'Post Debit Note'),
    ('purchase.wht.read',        'purchase','wht',            'read',   'View WHT certificates'),
    ('tax.vat_register.read',    'tax',     'vat_register',   'read',   'View VAT register'),
    ('tax.pnd30.read',           'tax',     'pnd30',          'read',   'View ภ.พ.30'),
    ('tax.pnd3.read',            'tax',     'pnd3',           'read',   'View ภ.ง.ด.3'),
    ('tax.pnd53.read',           'tax',     'pnd53',          'read',   'View ภ.ง.ด.53'),
    ('report.trial_balance.read','report',  'trial_balance',  'read',   'View trial balance'),
    ('report.profit_loss.read',  'report',  'profit_loss',    'read',   'View profit & loss')
ON CONFLICT (permission_code) DO NOTHING;

-- Re-grant SUPER_ADMIN the full permission set (the original 110 cross-join only covered the
-- codes that existed when it ran). Global grants -> company_id NULL.
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, NULL
FROM sys.roles r CROSS JOIN sys.permissions p
WHERE r.role_code = 'SUPER_ADMIN' AND r.company_id IS NULL
ON CONFLICT DO NOTHING;

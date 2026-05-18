-- Sprint 9 C7 — tax-filing lifecycle permissions (built in Part B, reused by
-- Part C). Keep in sync with Accounting.Api.Authorization.Permissions.Tax.
-- Additive + idempotent (ON CONFLICT). CHIEF_ACCOUNTANT = all 3; ACCOUNTANT =
-- preview + read only; SUPER_ADMIN / COMPANY_ADMIN = all 3 for symmetry
-- (super-admin also bypasses the policy, but the rows keep grants explicit).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('tax.filing.preview',  'tax', 'filing', 'preview',  'Preview a tax filing (ภ.พ.30 / ภ.ง.ด.)'),
    ('tax.filing.finalize', 'tax', 'filing', 'finalize', 'Finalize + lock a tax filing'),
    ('tax.filing.read',     'tax', 'filing', 'read',     'View historical tax filings')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON (
        (p.permission_code IN ('tax.filing.preview','tax.filing.finalize','tax.filing.read')
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT'))
     OR (p.permission_code IN ('tax.filing.preview','tax.filing.read')
            AND r.role_code = 'ACCOUNTANT'))
ON CONFLICT DO NOTHING;

-- Sprint 8.6 — AR-side WHT enablers for the demo company. Additive + idempotent.
-- (1) 1180 WHT-Receivable (Current Asset, DR) — GlAccountsOptions.WhtReceivableAccount.
-- (2) tax.wht_type.manage permission + grants. No $-literal (gotcha §17 safe).
-- Column set matches seed 120 (chart_of_accounts has no name_en/subtype columns).

INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type, normal_balance,
     is_header, is_active, created_at)
VALUES
    (1, '1180', 'ภาษีหัก ณ ที่จ่ายค้างรับ', 'ASSET', 'DR', FALSE, TRUE, now())
ON CONFLICT (company_id, account_code) DO NOTHING;

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('tax.wht_type.manage', 'tax', 'wht_type', 'manage', 'CRUD + rate-change on WHT types')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'tax.wht_type.manage'
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT')
ON CONFLICT DO NOTHING;

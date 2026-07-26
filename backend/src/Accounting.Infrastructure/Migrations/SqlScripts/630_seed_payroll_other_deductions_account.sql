-- Payroll other-deductions counterpart account for EVERY existing company.
-- Additive + idempotent; kept in sync with GlAccountsOptions and DefaultChartOfAccounts.

INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_type, normal_balance, is_header, is_active, created_at)
SELECT c.company_id, a.account_code, a.account_name_th, a.account_type, a.normal_balance, FALSE, TRUE, now()
FROM master.companies c
CROSS JOIN (VALUES
    ('2180', 'เงินหักจากพนักงานค้างนำส่ง', 'LIABILITY', 'CR')
) AS a(account_code, account_name_th, account_type, normal_balance)
ON CONFLICT (company_id, account_code) DO NOTHING;

-- Year-end closing (specs/year-end-closing.md D2/B2) — seed the 3300 Retained Earnings
-- equity account into every EXISTING company's chart of accounts. New companies get it via
-- DefaultChartOfAccounts in MasterDataServices.cs (CompanyService.CreateAsync). Idempotent;
-- zero-balance until a fiscal year is closed, so this is a no-op change to every existing
-- report (dropped by the balance sheet's zero-row filter) — safe for co2/co3 demo data too.
-- NB: never put curly braces here (EF ExecuteSqlRawAsync treats them as string.Format
-- placeholders).

INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_name_en, account_type, normal_balance,
     is_header, is_active, created_at)
SELECT c.company_id, '3300', 'กำไรสะสม', 'Retained Earnings', 'EQUITY', 'CR', FALSE, TRUE, now()
FROM master.companies c
WHERE NOT EXISTS (
    SELECT 1 FROM master.chart_of_accounts a
    WHERE a.company_id = c.company_id AND a.account_code = '3300'
)
ON CONFLICT (company_id, account_code) DO NOTHING;

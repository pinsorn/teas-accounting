INSERT INTO master.chart_of_accounts
    (company_id, account_code, account_name_th, account_name_en, account_type, normal_balance, is_header, is_active, created_at)
SELECT c.company_id, '5000', 'ต้นทุนขาย', 'Cost of Goods Sold', 'EXPENSE', 'DR', FALSE, TRUE, now()
FROM master.companies c
WHERE NOT EXISTS (
    SELECT 1 FROM master.chart_of_accounts a
    WHERE a.company_id = c.company_id AND a.account_code = '5000'
);

UPDATE sys.expense_categories ec SET default_expense_account_id = a.account_id
FROM master.chart_of_accounts a
WHERE ec.category_code = 'COGS' AND a.company_id = ec.company_id AND a.account_code = '5000'
  AND ec.default_expense_account_id IS DISTINCT FROM a.account_id;

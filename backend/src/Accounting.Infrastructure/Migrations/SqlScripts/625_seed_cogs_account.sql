-- v1.22.x F-9: seed 5000 Cost of Goods Sold into every company + remap the COGS
-- expense category. Prod runs SqlScripts under the NOBYPASSRLS app role with
-- FORCE ROW LEVEL SECURITY on these tables, so each company's rows are written
-- under a transaction-local app.company_id pin (same mechanism as
-- CompanyService.CreateAsync). Idempotent; safe to re-run.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        INSERT INTO master.chart_of_accounts
            (company_id, account_code, account_name_th, account_name_en, account_type, normal_balance, is_header, is_active, created_at)
        SELECT c.company_id, '5000', 'ต้นทุนขาย', 'Cost of Goods Sold', 'EXPENSE', 'DR', FALSE, TRUE, now()
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = '5000'
        );

        UPDATE sys.expense_categories ec SET default_expense_account_id = a.account_id
        FROM master.chart_of_accounts a
        WHERE ec.category_code = 'COGS' AND ec.company_id = c.company_id
          AND a.company_id = c.company_id AND a.account_code = '5000'
          AND ec.default_expense_account_id IS DISTINCT FROM a.account_id;
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

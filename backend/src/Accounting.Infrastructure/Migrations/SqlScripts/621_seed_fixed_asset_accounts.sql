-- Fixed assets (specs/fixed-assets.md §2) — seed the 5 FA GL accounts into every
-- EXISTING company's chart of accounts. New companies get them via DefaultChartOfAccounts.
-- Idempotent; zero-balance until first depreciation/disposal (dropped by the balance
-- sheet's zero-row filter) — safe for co2/co3 demo data. chart_of_accounts is a G1
-- (never-bypassable) tenant table: pin app.company_id per company, do NOT add a bypass
-- arm and do NOT use a bare multi-company INSERT (prod 42501, 2026-07-09).
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);
        INSERT INTO master.chart_of_accounts
            (company_id, account_code, account_name_th, account_name_en, account_type,
             normal_balance, is_header, is_active, created_at)
        SELECT c.company_id, v.code, v.th, v.en, v.acct_type, v.normal_bal, FALSE, TRUE, now()
        FROM (VALUES
            ('1610','อุปกรณ์และเครื่องใช้สำนักงาน','Office Equipment (Fixed Asset)','ASSET','DR'),
            ('1690','ค่าเสื่อมราคาสะสม','Accumulated Depreciation','ASSET','CR'),
            ('5450','ค่าเสื่อมราคา','Depreciation Expense','EXPENSE','DR'),
            ('4200','กำไรจากการจำหน่ายสินทรัพย์','Gain on Disposal of Assets','REVENUE','CR'),
            ('5460','ขาดทุนจากการจำหน่ายสินทรัพย์','Loss on Disposal of Assets','EXPENSE','DR')
        ) AS v(code, th, en, acct_type, normal_bal)
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = v.code)
        ON CONFLICT (company_id, account_code) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;

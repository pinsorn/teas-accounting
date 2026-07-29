-- Director/shareholder loan + interest expense + other income, for EVERY existing company.
-- New companies get them via DefaultChartOfAccounts (MasterDataServices.cs).
-- Additive + idempotent; all three are zero-balance on arrival (dropped by the balance sheet's
-- zero-row filter) — safe for co2/co3 demo data.
-- master.chart_of_accounts is a G1 (never-bypassable) tenant table: pin app.company_id per
-- company, do NOT add a bypass arm, do NOT use a bare multi-company INSERT — startup runs with
-- app.company_id UNSET under the NOBYPASSRLS `teas` role and every row would 42501
-- (prod v1.24.0, 2026-07-28, rolled back clean). teas_test connects as superuser and cannot
-- catch this. Mirrors 621/630.
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
            ('2190','เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น','Director & Shareholder Loan','LIABILITY','CR'),
            ('5500','ดอกเบี้ยจ่าย','Interest Expense','EXPENSE','DR'),
            ('4300','รายได้อื่น','Other Income','REVENUE','CR')
        ) AS v(code, th, en, acct_type, normal_bal)
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = v.code)
        ON CONFLICT (company_id, account_code) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;

-- Payroll other-deductions counterpart account for EVERY existing company.
-- Additive + idempotent; kept in sync with GlAccountsOptions and DefaultChartOfAccounts.
-- chart_of_accounts is a G1 (never-bypassable) tenant table: pin app.company_id per company,
-- do NOT add a bypass arm and do NOT use a bare multi-company INSERT — a bare
-- `INSERT ... SELECT ... FROM master.companies CROSS JOIN (VALUES ...)` has no
-- app.company_id set for any row, so Postgres' implicit WITH CHECK (reusing the
-- company_isolation USING clause) rejects every row with 42501 under the app's
-- NOBYPASSRLS `teas` role. teas_test never catches this because it connects as a
-- Postgres superuser, which bypasses RLS unconditionally (prod 42501, 2026-07-28,
-- v1.24.0 deploy — rolled back clean). Mirrors 621_seed_fixed_asset_accounts.sql.
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
            ('2180','เงินหักจากพนักงานค้างนำส่ง','Employee Deductions Payable','LIABILITY','CR')
        ) AS v(code, th, en, acct_type, normal_bal)
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = v.code)
        ON CONFLICT (company_id, account_code) DO NOTHING;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;

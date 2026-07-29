-- F-B (specs/fix-e2e-v1260-findings.md) — repoint the INTR (ดอกเบี้ยจ่าย) expense category's
-- default account from 5200 (Service Expense) to 5500 (Interest Expense, exists since v1.25.0),
-- for EVERY existing company. New companies get the fix directly via DefaultExpenseCategorySpecs
-- (MasterDataServices.cs). Also covers the legacy '81010' §17.3 code some companies' INTR row may
-- point at (430_seed_expense_categories_full.sql's COALESCE fallback resolved to 5200 when 81010
-- was never actually seeded as a real chart_of_accounts row, but this guards the case where it was).
--
-- sys.expense_categories (schema is sys, NOT master) is a G1 (never-bypassable) FORCE-RLS table
-- (600_superadmin_scoped_rls.sql line 18 — same list master.chart_of_accounts is also in): pin
-- app.company_id per company. Do NOT use a bare cross-company UPDATE — startup runs with
-- app.company_id UNSET under the NOBYPASSRLS `teas` role and every row would be RLS-invisible to
-- both the read and the write (v1.22.0/v1.24.0 class of bug; see 630/631/632). teas_test connects
-- as superuser and cannot catch this.
--
-- UPDATE only rows still pointing at the company's OWN 5200/81010 account — never touch a
-- user-customized mapping. Idempotent: the WHERE clause is itself the guard, safe to rerun.
-- No curly braces. UTF-8.
DO $do$
DECLARE c RECORD;
DECLARE acct5500 BIGINT;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        SELECT account_id INTO acct5500
          FROM master.chart_of_accounts
         WHERE company_id = c.company_id AND account_code = '5500';

        IF acct5500 IS NOT NULL THEN
            UPDATE sys.expense_categories ec
               SET default_expense_account_id = acct5500
             WHERE ec.company_id = c.company_id
               AND ec.category_code = 'INTR'
               AND ec.default_expense_account_id IN (
                     SELECT account_id FROM master.chart_of_accounts
                      WHERE company_id = c.company_id AND account_code IN ('5200', '81010'));
        END IF;
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;

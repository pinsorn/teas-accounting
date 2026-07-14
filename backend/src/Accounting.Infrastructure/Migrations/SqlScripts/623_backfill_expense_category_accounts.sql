-- WP1.5b (specs/fix-purchase-ux-findings-2026-07-14.md, F20) — backfill
-- sys.expense_categories.default_expense_account_id where NULL for EXISTING tenants (new
-- companies get a default at CreateAsync via WP1.5a's DefaultExpenseCategories helper). A
-- category with a NULL default (e.g. Repttown's hand-created COGS) is savable via the API but
-- throws vi/pv.expense_account_missing (422) the moment a document line tries to use it.
--
-- RLS (troubles-wiki.md "Startup SqlScript … RLS'd tables fails 42501 or silently no-ops on
-- prod"; memory "RLS masked by superuser tests"): sys.expense_categories AND
-- master.chart_of_accounts are G1 tables — ENABLE + FORCE ROW LEVEL SECURITY, plain
-- company_isolation USING (company_id = app.company_id GUC), NO bypass arm
-- (600_superadmin_scoped_rls.sql). At API startup NO app.company_id GUC is set, so a bare
-- multi-company UPDATE..FROM would read/write NOTHING (silently 0 rows, no error) under the
-- prod NOBYPASSRLS app role. teas_test/dev connect as Postgres SUPERUSER (RLS bypassed
-- entirely), so this bug class is INVISIBLE there — mirrors 611_seed_retained_earnings_account.sql's
-- per-company set_config loop exactly. master.companies itself carries no RLS (tenant root), so
-- the company id list read below is unfiltered. Idempotent (WHERE …IS NULL); no curly braces
-- anywhere (EF ExecuteSqlRawAsync treats them as string.Format placeholders).
--
-- CoA reality (MasterDataServices.cs DefaultChartOfAccounts): every CreateAsync-onboarded
-- company has "5200" (ค่าใช้จ่ายค่าบริการ / Service Expense) but NO "51010" and no dedicated
-- COGS account — so "5200" is the universal fallback here too (mirrors WP1.5a's remap). An
-- is_cogs category prefers a REAL cost-of-sales account only if this tenant's CoA happens to
-- have one (e.g. company 1's demo/custom chart); otherwise it also falls back to 5200 — a
-- safe, overridable default that never touches GL posting math (the accountant can re-map the
-- category, or a document line can override expenseAccountId explicitly).
DO $do$
DECLARE c RECORD;
BEGIN
  FOR c IN SELECT company_id FROM master.companies LOOP
    PERFORM set_config('app.company_id', c.company_id::text, true);

    UPDATE sys.expense_categories ec
       SET default_expense_account_id = (
         SELECT coa.account_id FROM master.chart_of_accounts coa
          WHERE coa.company_id = c.company_id AND coa.is_header = FALSE
            AND coa.account_code = COALESCE(
              CASE WHEN ec.is_cogs THEN (
                SELECT x.account_code FROM master.chart_of_accounts x
                 WHERE x.company_id = c.company_id AND x.is_header = FALSE
                   AND x.account_code IN ('51010','5000','5110')
                 ORDER BY x.account_code LIMIT 1) END,
              '5200')
          LIMIT 1)
     WHERE ec.company_id = c.company_id
       AND ec.default_expense_account_id IS NULL;
  END LOOP;

  PERFORM set_config('app.company_id', '', true);
END
$do$;

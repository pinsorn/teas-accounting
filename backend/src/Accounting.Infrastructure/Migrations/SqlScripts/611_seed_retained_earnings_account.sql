-- Year-end closing (specs/year-end-closing.md D2/B2) — seed the 3300 Retained Earnings
-- equity account into every EXISTING company's chart of accounts. New companies get it via
-- DefaultChartOfAccounts in MasterDataServices.cs (CompanyService.CreateAsync). Idempotent;
-- zero-balance until a fiscal year is closed, so this is a no-op change to every existing
-- report (dropped by the balance sheet's zero-row filter) — safe for co2/co3 demo data too.
-- NB: never put curly braces here (EF ExecuteSqlRawAsync treats them as string.Format
-- placeholders) — including inside comments (a prior draft of 613 broke on exactly this).
--
-- PROD HOTFIX (2026-07-09, v1.15.0 startup failure, troubles-wiki.md "Startup SqlScript
-- writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod") — a plain
-- multi-company INSERT..SELECT FROM master.companies was rejected at startup: 42501 "new row
-- violates row-level security policy for table chart_of_accounts". chart_of_accounts is 600's
-- G1 group (plain company_isolation: company_id = app.company_id GUC, NO bypass arm — G1 is
-- deliberately never-bypassable tenant data, unlike G3's cross-company-admin bypass). At
-- startup no app.company_id GUC is set, so a bare INSERT can never satisfy the policy's
-- implicit WITH CHECK (Postgres reuses USING as WITH CHECK when none is given). Invisible on
-- teas_test (SUPERUSER connection bypasses RLS entirely). Fix: pin app.company_id to EACH
-- company in turn (mirrors 510_per_company_roles_reconcile.sql's sys.seed_company_roles
-- fan-out loop) instead of adding a bypass arm — G1 tables never get one, by design; do NOT
-- touch the 600/612 policies for this. master.companies itself carries no RLS policy (not in
-- 010_rls_policies.sql's or 600's G1/G2/G3 table lists — it IS the tenant root, not a
-- tenant-owned child table), so reading the company id list here is unfiltered.
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        INSERT INTO master.chart_of_accounts
            (company_id, account_code, account_name_th, account_name_en, account_type, normal_balance,
             is_header, is_active, created_at)
        SELECT c.company_id, '3300', 'กำไรสะสม', 'Retained Earnings', 'EQUITY', 'CR', FALSE, TRUE, now()
        WHERE NOT EXISTS (
            SELECT 1 FROM master.chart_of_accounts a
            WHERE a.company_id = c.company_id AND a.account_code = '3300'
        )
        ON CONFLICT (company_id, account_code) DO NOTHING;
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END
$do$;

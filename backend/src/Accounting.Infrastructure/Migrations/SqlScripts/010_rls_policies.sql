-- Row-Level Security: every business table that carries company_id gets a policy
-- driven by the current_setting('app.company_id') value pinned by TenantMiddleware.
-- Belt-and-braces with the EF Core global query filter — defense in depth.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'master.branches',
        'master.chart_of_accounts',
        'master.customers',
        'master.vendors',
        'sys.expense_categories',
        'sys.number_sequences',
        'sys.api_keys',
        'tax.tax_codes',
        'gl.journal_entries'
    ];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);

        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END $$;

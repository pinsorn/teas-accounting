-- H1 (review 2026-07-04) — RLS backstop for 8 ITenantOwned tables that carry
-- company_id NOT NULL but had NO row-level-security policy (EF global query filter
-- only). Under a prod NOBYPASSRLS role a forgotten WHERE / IgnoreQueryFilters / raw
-- SQL on any of these leaks every tenant's rows (CLAUDE.md §4.7). Mirrors 572/573:
-- ENABLE + FORCE + fail-closed company_isolation. Verified 2026-07-04 (dbcheck): all 8
-- have company_id NOT NULL and RLS was OFF.
--
-- Ordering: this runs AFTER the 1xx–5xx seeds, so fresh-DB reference seeds
-- (wht_types, products, periods) insert BEFORE RLS is enabled — no seed breakage.
-- Idempotent (DROP POLICY IF EXISTS). Applied once by DbInitializer.ApplyScriptsAsync.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'master.products',
        'tax.wht_types',
        'tax.wht_certificates',
        'tax.tax_filings',
        'sys.idempotency_keys',
        'sys.attachments',
        'gl.accounting_periods',
        'etax.submissions'
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

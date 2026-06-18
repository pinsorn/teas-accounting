-- Sales-chain RLS backstop (CLAUDE.md §4.7) — Q → SO → DO carry company_id but had no
-- row-level-security policy (EF global query filter only). Add ENABLE + FORCE + tenant policy,
-- mirroring 010/322/500. Belt-and-braces with the EF filter: a forgotten WHERE in application
-- code still can't leak another tenant's rows.
--
-- Only the headers carry company_id; quotation_lines / sales_order_lines / delivery_order_lines
-- inherit isolation through their FK to the header + the EF filter (same as tax_invoice_lines —
-- no own company_id, no own policy). receipts is handled in 570; tax_adjustment_notes in 571.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'sales.quotations',
        'sales.sales_orders',
        'sales.delivery_orders'
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

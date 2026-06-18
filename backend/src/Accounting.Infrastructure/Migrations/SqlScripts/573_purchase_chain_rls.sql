-- Purchase-chain RLS backstop (CLAUDE.md §4.7) — 08-HIGH-2.
-- payment_vouchers and purchase_orders carry company_id but had no RLS policy
-- (EF global query filter only). Add ENABLE + FORCE + tenant policy, mirroring
-- 010/060/572. Belt-and-braces with the EF filter.
--
-- Lines (pv_lines, po_lines, vi_lines) have NO own company_id column; they inherit
-- isolation through their FK to the header + the EF filter — same as tax_invoice_lines.
-- vendor_invoices already covered by 060. No own policy for lines.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'purchase.payment_vouchers',
        'purchase.purchase_orders'
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

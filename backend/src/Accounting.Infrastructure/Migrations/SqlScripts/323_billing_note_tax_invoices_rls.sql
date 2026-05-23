-- Sprint 13i C7 — RLS for the BN ↔ TI join table. Mirrors 322_billing_notes_rls.sql
-- (CLAUDE.md §4.7). The join table carries company_id so it isolates on its own;
-- it also inherits isolation via the FK to billing_notes.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'sales.billing_note_tax_invoices'
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

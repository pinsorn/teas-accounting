-- Sprint 13h P6.2 — Billing Note RLS. Belt-and-braces with EF Core global
-- query filter (CLAUDE.md §4.7). Only billing_notes carries company_id;
-- billing_note_lines inherits isolation via FK to billing_notes.

DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
        'sales.billing_notes'
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

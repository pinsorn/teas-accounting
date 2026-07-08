-- Year-end closing (specs/year-end-closing.md D3) — gl.fiscal_year_closes RLS. Mirrors
-- 600_superadmin_scoped_rls.sql's G1 group (plain company_isolation, no super-admin/bypass
-- arm — that arm is retired repo-wide; fiscal_year_closes is not cross-company scanned).
-- Assumes the EF migration created the table first — DbInitializer runs migrations before
-- SqlScripts.

ALTER TABLE gl.fiscal_year_closes ENABLE ROW LEVEL SECURITY;
ALTER TABLE gl.fiscal_year_closes FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON gl.fiscal_year_closes;
CREATE POLICY company_isolation ON gl.fiscal_year_closes
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

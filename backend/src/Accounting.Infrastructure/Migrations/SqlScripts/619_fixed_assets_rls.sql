-- Fixed Assets (specs/fixed-assets.md §8.2) — G1 company_isolation for all THREE new tables.
-- Mirrors 616_expense_claims_rls.sql verbatim (plain tenant data, never cross-company scanned;
-- G1 tables never get a bypass arm, by design). Assumes the EF migration created the tables
-- first — DbInitializer runs migrations before SqlScripts. DDL-only (CREATE POLICY) -> no
-- 42501 risk at apply time.

ALTER TABLE fixedasset.fixed_assets ENABLE ROW LEVEL SECURITY;
ALTER TABLE fixedasset.fixed_assets FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON fixedasset.fixed_assets;
CREATE POLICY company_isolation ON fixedasset.fixed_assets
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

ALTER TABLE fixedasset.depreciation_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE fixedasset.depreciation_runs FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON fixedasset.depreciation_runs;
CREATE POLICY company_isolation ON fixedasset.depreciation_runs
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

ALTER TABLE fixedasset.depreciation_run_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE fixedasset.depreciation_run_lines FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON fixedasset.depreciation_run_lines;
CREATE POLICY company_isolation ON fixedasset.depreciation_run_lines
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

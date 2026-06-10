-- Phase C-C — RLS on tax.cit_year_summaries + tax.cit_adjustments (multi-tenant; mirror 010/480).
-- The table SCHEMA is owned by the EF migration AddCitYearStores (DbInitializer MigrateAsync runs it
-- BEFORE this script). This file only adds tenant row-level-security. Idempotent; re-run = no-op.

ALTER TABLE tax.cit_year_summaries ENABLE ROW LEVEL SECURITY;
ALTER TABLE tax.cit_year_summaries FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON tax.cit_year_summaries;
CREATE POLICY company_isolation ON tax.cit_year_summaries
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

ALTER TABLE tax.cit_adjustments ENABLE ROW LEVEL SECURITY;
ALTER TABLE tax.cit_adjustments FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON tax.cit_adjustments;
CREATE POLICY company_isolation ON tax.cit_adjustments
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

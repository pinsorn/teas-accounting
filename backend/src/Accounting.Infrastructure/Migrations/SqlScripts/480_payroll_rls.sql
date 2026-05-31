-- Payroll P-C — RLS on payroll.payroll_runs + payroll.payslips (multi-tenant; mirror 010/430).
-- The table SCHEMA is owned by the EF migration AddPayrollRun (DbInitializer MigrateAsync runs it
-- BEFORE this script). This file only adds tenant row-level-security. Idempotent; re-run = no-op.

ALTER TABLE payroll.payroll_runs ENABLE ROW LEVEL SECURITY;
ALTER TABLE payroll.payroll_runs FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON payroll.payroll_runs;
CREATE POLICY company_isolation ON payroll.payroll_runs
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

ALTER TABLE payroll.payslips ENABLE ROW LEVEL SECURITY;
ALTER TABLE payroll.payslips FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON payroll.payslips;
CREATE POLICY company_isolation ON payroll.payslips
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

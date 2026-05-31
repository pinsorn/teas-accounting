-- Payroll P-A — RLS on master.employees (multi-tenant; mirror 010/060/200 pattern).
-- The table SCHEMA is owned by the EF migration AddEmployeeMaster, which DbInitializer
-- applies (MigrateAsync) BEFORE this script. This file only adds what EF doesn't manage:
-- row-level-security tenant isolation. Idempotent; re-run = no-op.

ALTER TABLE master.employees ENABLE ROW LEVEL SECURITY;
ALTER TABLE master.employees FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON master.employees;
CREATE POLICY company_isolation ON master.employees
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

-- Bank reconciliation (specs/bank-reconciliation.md D1) — bank.* RLS. Mirrors
-- 600_superadmin_scoped_rls.sql's G1 group (plain company_isolation, no bypass arm — all
-- three bank.* tables are plain tenant data, never cross-company scanned; G1 tables never
-- get a bypass arm, by design). Assumes the EF migration created the tables first —
-- DbInitializer runs migrations before SqlScripts.

ALTER TABLE bank.bank_accounts ENABLE ROW LEVEL SECURITY;
ALTER TABLE bank.bank_accounts FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON bank.bank_accounts;
CREATE POLICY company_isolation ON bank.bank_accounts
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

ALTER TABLE bank.statement_imports ENABLE ROW LEVEL SECURITY;
ALTER TABLE bank.statement_imports FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON bank.statement_imports;
CREATE POLICY company_isolation ON bank.statement_imports
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

ALTER TABLE bank.statement_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE bank.statement_lines FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON bank.statement_lines;
CREATE POLICY company_isolation ON bank.statement_lines
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

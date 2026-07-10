-- Expense Claims (specs/expense-claims.md §2) — expense.* RLS. Mirrors
-- 614_bank_reconciliation_rls.sql's G1 group (plain company_isolation, no bypass arm — both
-- expense.* tables are plain tenant data, never cross-company scanned; G1 tables never get a
-- bypass arm, by design). Assumes the EF migration created the tables first —
-- DbInitializer runs migrations before SqlScripts. DDL-only (CREATE POLICY) -> no 42501 risk
-- at apply time.

ALTER TABLE expense.expense_claims ENABLE ROW LEVEL SECURITY;
ALTER TABLE expense.expense_claims FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON expense.expense_claims;
CREATE POLICY company_isolation ON expense.expense_claims
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

ALTER TABLE expense.expense_claim_lines ENABLE ROW LEVEL SECURITY;
ALTER TABLE expense.expense_claim_lines FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON expense.expense_claim_lines;
CREATE POLICY company_isolation ON expense.expense_claim_lines
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
    );

-- Sprint 8 — Business Units. SCHEMA (master.business_units table,
-- companies.requires_business_unit, business_unit_id on tax_invoices/receipts/
-- tax_adjustment_notes/journal_lines, FKs, indexes) is owned by the EF migration
-- 20260517021031_AddBusinessUnits, which DbInitializer applies (MigrateAsync)
-- BEFORE this script. This file only adds what EF migrations don't manage:
-- RLS on the new master table + the TI immutability trigger update. Same split
-- as 060 (VendorInvoice). Idempotent; re-run = no-op.
--
-- NO BACKFILL: legacy posted documents keep business_unit_id = NULL forever
-- (immutability). Reports support an "include unspecified" filter to surface them.

-- RLS on master.business_units (multi-tenant; mirror 010/060 pattern).
ALTER TABLE master.business_units ENABLE ROW LEVEL SECURITY;
ALTER TABLE master.business_units FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON master.business_units;
CREATE POLICY company_isolation ON master.business_units
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

-- TI immutability (040) — add business_unit_id to the blocked-column list so a
-- posted Tax Invoice's BU can never change (CLAUDE.md §4.2). CREATE OR REPLACE =
-- idempotent; replaces 040's function body verbatim + the new clause.
-- Receipt/CN/DN have no DB immutability trigger (app-level MarkPosted only) — BU
-- there is snapshot-at-draft and has no post-draft update path; out of scope to
-- add new triggers this sprint (Answer-Sana-Backend9 §5.6).
CREATE OR REPLACE FUNCTION sales.fn_enforce_ti_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no              IS DISTINCT FROM NEW.doc_no
         OR OLD.doc_date            IS DISTINCT FROM NEW.doc_date
         OR OLD.tax_point_date      IS DISTINCT FROM NEW.tax_point_date
         OR OLD.supplier_tax_id     IS DISTINCT FROM NEW.supplier_tax_id
         OR OLD.supplier_branch_code IS DISTINCT FROM NEW.supplier_branch_code
         OR OLD.customer_id         IS DISTINCT FROM NEW.customer_id
         OR OLD.subtotal_amount     IS DISTINCT FROM NEW.subtotal_amount
         OR OLD.tax_amount          IS DISTINCT FROM NEW.tax_amount
         OR OLD.total_amount        IS DISTINCT FROM NEW.total_amount
         OR OLD.business_unit_id    IS DISTINCT FROM NEW.business_unit_id
         OR OLD.company_id          IS DISTINCT FROM NEW.company_id
         OR OLD.branch_id           IS DISTINCT FROM NEW.branch_id)
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted Tax Invoice (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

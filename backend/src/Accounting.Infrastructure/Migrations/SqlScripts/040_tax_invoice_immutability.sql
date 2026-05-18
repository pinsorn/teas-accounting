-- Tax Invoice posting immutability per CLAUDE.md §4.2 / ม.86/4.
-- Once status = POSTED, critical fields cannot change. Errors require Credit Note + Reissue.

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

DROP TRIGGER IF EXISTS trg_ti_immutable ON sales.tax_invoices;
CREATE TRIGGER trg_ti_immutable
    BEFORE UPDATE ON sales.tax_invoices
    FOR EACH ROW EXECUTE FUNCTION sales.fn_enforce_ti_immutability();

CREATE OR REPLACE FUNCTION sales.fn_no_delete_posted_ti() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft Tax Invoice (doc_no=%, status=%)',
            OLD.doc_no, OLD.status
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_ti_no_delete_posted ON sales.tax_invoices;
CREATE TRIGGER trg_ti_no_delete_posted
    BEFORE DELETE ON sales.tax_invoices
    FOR EACH ROW EXECUTE FUNCTION sales.fn_no_delete_posted_ti();

-- RLS on tax_invoices
ALTER TABLE sales.tax_invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales.tax_invoices FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON sales.tax_invoices;
CREATE POLICY company_isolation ON sales.tax_invoices
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

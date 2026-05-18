-- Vendor Invoice posting immutability — mirrors 040 (tax_invoices), CLAUDE.md §4.2.
-- Once status = POSTED, critical fields (incl. the ม.82/4 legal refs + vat_claim_period)
-- cannot change. settled_amount / settlement_status are deliberately NOT frozen — they
-- mutate when a Payment Voucher settles the VI (Sprint-6 wiring).

CREATE OR REPLACE FUNCTION purchase.fn_enforce_vi_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no                   IS DISTINCT FROM NEW.doc_no
         OR OLD.doc_date                 IS DISTINCT FROM NEW.doc_date
         OR OLD.vendor_tax_invoice_no    IS DISTINCT FROM NEW.vendor_tax_invoice_no
         OR OLD.vendor_tax_invoice_date  IS DISTINCT FROM NEW.vendor_tax_invoice_date
         OR OLD.vat_claim_period         IS DISTINCT FROM NEW.vat_claim_period
         OR OLD.subtotal_amount          IS DISTINCT FROM NEW.subtotal_amount
         OR OLD.vat_amount               IS DISTINCT FROM NEW.vat_amount
         OR OLD.non_recoverable_vat_amount IS DISTINCT FROM NEW.non_recoverable_vat_amount
         OR OLD.total_amount             IS DISTINCT FROM NEW.total_amount
         OR OLD.vendor_id                IS DISTINCT FROM NEW.vendor_id
         OR OLD.vendor_tax_id            IS DISTINCT FROM NEW.vendor_tax_id
         OR OLD.company_id               IS DISTINCT FROM NEW.company_id
         OR OLD.branch_id                IS DISTINCT FROM NEW.branch_id)
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted Vendor Invoice (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_vendor_invoices_immutable_after_post ON purchase.vendor_invoices;
CREATE TRIGGER tg_vendor_invoices_immutable_after_post
    BEFORE UPDATE ON purchase.vendor_invoices
    FOR EACH ROW EXECUTE FUNCTION purchase.fn_enforce_vi_immutability();

CREATE OR REPLACE FUNCTION purchase.fn_no_delete_posted_vi() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft Vendor Invoice (doc_no=%, status=%)',
            OLD.doc_no, OLD.status
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_vendor_invoices_no_delete_posted ON purchase.vendor_invoices;
CREATE TRIGGER tg_vendor_invoices_no_delete_posted
    BEFORE DELETE ON purchase.vendor_invoices
    FOR EACH ROW EXECUTE FUNCTION purchase.fn_no_delete_posted_vi();

-- RLS on vendor_invoices (carries company_id). Lines / applications inherit isolation
-- through their FK + the EF global query filter — same pattern as tax_invoice_lines.
ALTER TABLE purchase.vendor_invoices ENABLE ROW LEVEL SECURITY;
ALTER TABLE purchase.vendor_invoices FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON purchase.vendor_invoices;
CREATE POLICY company_isolation ON purchase.vendor_invoices
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

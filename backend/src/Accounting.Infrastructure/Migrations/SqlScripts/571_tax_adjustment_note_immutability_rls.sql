-- Tax Adjustment Note (CN/DN) posting immutability + RLS — mirrors 040 / 060, CLAUDE.md §4.2.
-- ใบลดหนี้ (Credit Note, ม.86/10) / ใบเพิ่มหนี้ (Debit Note, ม.86/9). Once status = POSTED its
-- critical post-fields (doc number, tax-point dates, the original-TI link, customer identity,
-- amounts) cannot change — the note is itself a tax document under ม.86/9-10 and is immutable.
--
-- Allowlist only (mirror 040/060): print_count / original_printed_at, updated_at and the version
-- concurrency token are NOT frozen. note_type / prefix_code / reason are part of the legal
-- snapshot (ม.86/9 #5 / ม.86/10 #5 require the rationale on the document) and ARE frozen.

CREATE OR REPLACE FUNCTION sales.fn_enforce_adjnote_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no                  IS DISTINCT FROM NEW.doc_no
         OR OLD.prefix_code             IS DISTINCT FROM NEW.prefix_code
         OR OLD.note_type               IS DISTINCT FROM NEW.note_type
         OR OLD.doc_date                IS DISTINCT FROM NEW.doc_date
         OR OLD.tax_point_date          IS DISTINCT FROM NEW.tax_point_date
         OR OLD.original_tax_invoice_id IS DISTINCT FROM NEW.original_tax_invoice_id
         OR OLD.reason                  IS DISTINCT FROM NEW.reason
         OR OLD.customer_id             IS DISTINCT FROM NEW.customer_id
         OR OLD.customer_tax_id         IS DISTINCT FROM NEW.customer_tax_id
         OR OLD.customer_branch_code    IS DISTINCT FROM NEW.customer_branch_code
         OR OLD.subtotal_amount         IS DISTINCT FROM NEW.subtotal_amount
         OR OLD.tax_amount              IS DISTINCT FROM NEW.tax_amount
         OR OLD.total_amount            IS DISTINCT FROM NEW.total_amount
         OR OLD.total_amount_thb        IS DISTINCT FROM NEW.total_amount_thb
         OR OLD.tax_rate                IS DISTINCT FROM NEW.tax_rate
         OR OLD.currency_code           IS DISTINCT FROM NEW.currency_code
         OR OLD.exchange_rate           IS DISTINCT FROM NEW.exchange_rate
         OR OLD.company_id              IS DISTINCT FROM NEW.company_id
         OR OLD.branch_id               IS DISTINCT FROM NEW.branch_id)
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted Tax Adjustment Note (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_adjnote_immutable_after_post ON sales.tax_adjustment_notes;
CREATE TRIGGER tg_adjnote_immutable_after_post
    BEFORE UPDATE ON sales.tax_adjustment_notes
    FOR EACH ROW EXECUTE FUNCTION sales.fn_enforce_adjnote_immutability();

CREATE OR REPLACE FUNCTION sales.fn_no_delete_posted_adjnote() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft Tax Adjustment Note (doc_no=%, status=%)',
            OLD.doc_no, OLD.status
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_adjnote_no_delete_posted ON sales.tax_adjustment_notes;
CREATE TRIGGER tg_adjnote_no_delete_posted
    BEFORE DELETE ON sales.tax_adjustment_notes
    FOR EACH ROW EXECUTE FUNCTION sales.fn_no_delete_posted_adjnote();

-- RLS on tax_adjustment_notes (carries company_id). §4.7 — belt-and-braces with the EF Core
-- global query filter, same policy expression as 040/060/322/500.
ALTER TABLE sales.tax_adjustment_notes ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales.tax_adjustment_notes FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON sales.tax_adjustment_notes;
CREATE POLICY company_isolation ON sales.tax_adjustment_notes
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

-- Receipt posting immutability + RLS — mirrors 040 (tax_invoices) / 060 (vendor_invoices),
-- CLAUDE.md §4.2. A posted ใบเสร็จรับเงิน (Receipt) is immutable: once status = POSTED its
-- critical post-fields cannot change. Corrections = void + reissue, never an in-place edit.
--
-- Allowlist only (mirror 040/060): print_count / original_printed_at (reprints mark สำเนา/COPY),
-- updated_at and version (the concurrency token) are deliberately NOT frozen — they mutate
-- legitimately after POST. wht_amount / cash_received are part of the posted snapshot and ARE
-- frozen. The status column itself is not in the diff list so a future VOID transition is still
-- permitted; the no-delete trigger blocks physical removal of any non-draft row.

CREATE OR REPLACE FUNCTION sales.fn_enforce_receipt_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no            IS DISTINCT FROM NEW.doc_no
         OR OLD.doc_date          IS DISTINCT FROM NEW.doc_date
         OR OLD.customer_id        IS DISTINCT FROM NEW.customer_id
         OR OLD.customer_tax_id    IS DISTINCT FROM NEW.customer_tax_id
         OR OLD.amount            IS DISTINCT FROM NEW.amount
         OR OLD.total_amount      IS DISTINCT FROM NEW.total_amount
         OR OLD.total_amount_thb  IS DISTINCT FROM NEW.total_amount_thb
         OR OLD.wht_amount        IS DISTINCT FROM NEW.wht_amount
         OR OLD.cash_received     IS DISTINCT FROM NEW.cash_received
         OR OLD.currency_code     IS DISTINCT FROM NEW.currency_code
         OR OLD.exchange_rate     IS DISTINCT FROM NEW.exchange_rate
         OR OLD.company_id        IS DISTINCT FROM NEW.company_id
         OR OLD.branch_id         IS DISTINCT FROM NEW.branch_id)
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted Receipt (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_receipts_immutable_after_post ON sales.receipts;
CREATE TRIGGER tg_receipts_immutable_after_post
    BEFORE UPDATE ON sales.receipts
    FOR EACH ROW EXECUTE FUNCTION sales.fn_enforce_receipt_immutability();

CREATE OR REPLACE FUNCTION sales.fn_no_delete_posted_receipt() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft Receipt (doc_no=%, status=%)',
            OLD.doc_no, OLD.status
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS tg_receipts_no_delete_posted ON sales.receipts;
CREATE TRIGGER tg_receipts_no_delete_posted
    BEFORE DELETE ON sales.receipts
    FOR EACH ROW EXECUTE FUNCTION sales.fn_no_delete_posted_receipt();

-- RLS on receipts (carries company_id). receipt_lines / receipt_applications /
-- receipt_wht_lines inherit isolation through their FK + the EF global query filter —
-- same pattern as tax_invoice_lines (no own company_id, no own policy).
ALTER TABLE sales.receipts ENABLE ROW LEVEL SECURITY;
ALTER TABLE sales.receipts FORCE ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON sales.receipts;
CREATE POLICY company_isolation ON sales.receipts
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

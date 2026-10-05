-- specs/cancel-reissue-sales-docs.md section 3.2.2 (WP-1) - header immutability triggers v3 for the
-- cancel + reissue feature. CREATE OR REPLACE only; 570 and 583 are apply-once and are NOT edited.
-- The triggers (trg_ti_immutable, tg_receipts_immutable_after_post) are already bound to these
-- function names, so no trigger is re-created here.
--
-- Tax Invoice (supersedes 583): same frozen list as 583, plus
--   * replaces_tax_invoice_id (set only by the reissue path while DRAFT, frozen from then on);
--   * journal_entry_id, frozen only once it holds a value (it is stamped by a second UPDATE right
--     after MarkPosted, so NULL to value must stay legal; value to anything else is blocked);
--   * once VOIDED, the cancel audit columns and the reversal JE link are frozen as well.
-- The only legal status change out of DRAFT stays POSTED to VOIDED.
--
-- Receipt (supersedes 570): the guard widens from POSTED-only to any non-DRAFT status so a VOIDED
-- receipt is no longer fully mutable, the 570 field list is kept, the same additions as the Tax
-- Invoice are applied, and 583's status rule is added (only POSTED to VOIDED once out of DRAFT).
--
-- Idempotent. Do not put literal curly-brace characters anywhere in this file, comments included -
-- ExecuteSqlRawAsync parses the whole script as a composite format string.

CREATE OR REPLACE FUNCTION sales.fn_enforce_ti_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
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
         OR OLD.branch_id           IS DISTINCT FROM NEW.branch_id
         OR OLD.replaces_tax_invoice_id IS DISTINCT FROM NEW.replaces_tax_invoice_id
         OR (OLD.journal_entry_id IS NOT NULL
             AND OLD.journal_entry_id IS DISTINCT FROM NEW.journal_entry_id)
         OR (OLD.status = 'VOIDED'
             AND (OLD.cancel_reason_code IS DISTINCT FROM NEW.cancel_reason_code
               OR OLD.cancel_reason      IS DISTINCT FROM NEW.cancel_reason
               OR OLD.cancelled_at       IS DISTINCT FROM NEW.cancelled_at
               OR OLD.cancelled_by       IS DISTINCT FROM NEW.cancelled_by
               OR OLD.reversal_journal_entry_id IS DISTINCT FROM NEW.reversal_journal_entry_id)))
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of a posted/voided Tax Invoice (doc_no=%, status=%)',
                OLD.doc_no, OLD.status
                USING ERRCODE = 'check_violation';
        END IF;

        IF OLD.status IS DISTINCT FROM NEW.status
           AND NOT (OLD.status = 'POSTED' AND NEW.status = 'VOIDED')
        THEN
            RAISE EXCEPTION 'Illegal Tax Invoice status transition % to % (only POSTED to VOIDED is allowed once posted)',
                OLD.status, NEW.status
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE OR REPLACE FUNCTION sales.fn_enforce_receipt_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
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
         OR OLD.branch_id         IS DISTINCT FROM NEW.branch_id
         OR OLD.replaces_receipt_id IS DISTINCT FROM NEW.replaces_receipt_id
         OR (OLD.journal_entry_id IS NOT NULL
             AND OLD.journal_entry_id IS DISTINCT FROM NEW.journal_entry_id)
         OR (OLD.status = 'VOIDED'
             AND (OLD.cancel_reason_code IS DISTINCT FROM NEW.cancel_reason_code
               OR OLD.cancel_reason      IS DISTINCT FROM NEW.cancel_reason
               OR OLD.cancelled_at       IS DISTINCT FROM NEW.cancelled_at
               OR OLD.cancelled_by       IS DISTINCT FROM NEW.cancelled_by
               OR OLD.reversal_journal_entry_id IS DISTINCT FROM NEW.reversal_journal_entry_id)))
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted/voided Receipt (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;

        IF OLD.status IS DISTINCT FROM NEW.status
           AND NOT (OLD.status = 'POSTED' AND NEW.status = 'VOIDED')
        THEN
            RAISE EXCEPTION 'Illegal Receipt status transition % to % (only POSTED to VOIDED is allowed once posted)',
                OLD.status, NEW.status
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

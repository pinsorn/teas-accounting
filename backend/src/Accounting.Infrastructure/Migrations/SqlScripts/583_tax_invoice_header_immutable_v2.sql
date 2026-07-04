-- M2 (review 2026-07-04) — fix two gaps in the Tax Invoice header immutability trigger.
--
-- The effective function (040, redefined by 200_add_business_units.sql) guarded only
-- `IF OLD.status = 'POSTED'` and did NOT list `status` among the locked fields, so
-- (a) a POSTED→DRAFT status flip (un-post) via direct SQL passed the trigger, and
-- thereafter every field was editable, and (b) a VOIDED invoice's doc_no/amounts were
-- mutable (the guard only fired for POSTED). The sibling LINE trigger already used
-- `<> 'DRAFT'` (correct); this brings the header into line.
--
-- Status handling: once the row leaves DRAFT, the critical business fields are frozen AND
-- the only legal status change is the void transition POSTED→VOIDED (§4.3 — a voided number
-- is retained, never reused). Un-posting (POSTED→DRAFT) and any change out of VOIDED are
-- blocked. Posting (DRAFT→POSTED) is unaffected: OLD.status is 'DRAFT' so the guard is skipped.
--
-- Field list = the CURRENT effective definition (200's set, incl. business_unit_id). Non-critical
-- post-time updates (print tracking, e-Tax status) stay allowed. CREATE OR REPLACE rebinds the
-- existing trg_ti_immutable trigger. Idempotent; applied once by DbInitializer.ApplyScriptsAsync.

CREATE OR REPLACE FUNCTION sales.fn_enforce_ti_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        -- Critical business fields are frozen once posted/voided.
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
            RAISE EXCEPTION 'Cannot modify critical fields of a posted/voided Tax Invoice (doc_no=%, status=%)',
                OLD.doc_no, OLD.status
                USING ERRCODE = 'check_violation';
        END IF;

        -- Status is terminal once out of DRAFT, except the single legal void POSTED→VOIDED.
        -- This blocks un-posting (POSTED→DRAFT) and any transition out of VOIDED.
        IF OLD.status IS DISTINCT FROM NEW.status
           AND NOT (OLD.status = 'POSTED' AND NEW.status = 'VOIDED')
        THEN
            RAISE EXCEPTION 'Illegal Tax Invoice status transition %→% (only POSTED→VOIDED is allowed once posted)',
                OLD.status, NEW.status
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

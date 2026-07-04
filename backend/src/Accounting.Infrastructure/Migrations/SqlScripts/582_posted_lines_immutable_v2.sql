-- H7 (review 2026-07-04) — close the re-parent hole in the 580 posted-line triggers.
--
-- 580 computed the parent id as NEW.<fk> on UPDATE and checked ONLY that parent's
-- status. So an UPDATE that moves a line from a POSTED document to a DRAFT one sets
-- NEW.<fk> = the draft → passes, and the posted document silently loses a line.
-- Fix: on UPDATE/DELETE also check the OLD parent (the line's current owner) — a
-- line whose current parent is non-DRAFT cannot be edited, deleted, OR re-parented.
-- On UPDATE additionally keep the NEW-parent check (can't move a line ONTO a posted doc).
--
-- INSERT is deliberately NOT guarded. Two of these documents are created-as-posted:
-- GlPostingService (GlPostingService.cs:377-382 attaches Lines, MarkPosted, then one
-- SaveChanges → journal_lines are INSERTed with the parent already POSTED in the same
-- tx) and ReceiptService has an auto-post create path (ReceiptService.cs:457). A
-- BEFORE INSERT guard checking parent status would reject those legitimate flows. The
-- residual "raw INSERT a line onto a posted doc" vector is direct-SQL-only (an actor
-- with that access can also DISABLE the trigger) and self-inconsistent (line sum then
-- diverges from the M2-locked header total). Re-parent — the app-reachable hole — is
-- what this closes. Idempotent; applied once by DbInitializer.ApplyScriptsAsync.

-- ── Tax Invoice lines ──────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sales.fn_ti_lines_immutable() RETURNS trigger AS $$
DECLARE st_old text; st_new text;
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') THEN
        SELECT status INTO st_old FROM sales.tax_invoices WHERE tax_invoice_id = OLD.tax_invoice_id;
        IF st_old IS NOT NULL AND st_old <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot % a line of a non-draft Tax Invoice (tax_invoice_id=%, status=%)',
                lower(TG_OP), OLD.tax_invoice_id, st_old USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        SELECT status INTO st_new FROM sales.tax_invoices WHERE tax_invoice_id = NEW.tax_invoice_id;
        IF st_new IS NOT NULL AND st_new <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot move a line onto a non-draft Tax Invoice (tax_invoice_id=%, status=%)',
                NEW.tax_invoice_id, st_new USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_ti_lines_immutable ON sales.tax_invoice_lines;
CREATE TRIGGER trg_ti_lines_immutable
    BEFORE UPDATE OR DELETE ON sales.tax_invoice_lines
    FOR EACH ROW EXECUTE FUNCTION sales.fn_ti_lines_immutable();

-- ── Vendor Invoice lines ─────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION purchase.fn_vi_lines_immutable() RETURNS trigger AS $$
DECLARE st_old text; st_new text;
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') THEN
        SELECT status INTO st_old FROM purchase.vendor_invoices WHERE vendor_invoice_id = OLD.vendor_invoice_id;
        IF st_old IS NOT NULL AND st_old <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot % a line of a non-draft Vendor Invoice (vendor_invoice_id=%, status=%)',
                lower(TG_OP), OLD.vendor_invoice_id, st_old USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        SELECT status INTO st_new FROM purchase.vendor_invoices WHERE vendor_invoice_id = NEW.vendor_invoice_id;
        IF st_new IS NOT NULL AND st_new <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot move a line onto a non-draft Vendor Invoice (vendor_invoice_id=%, status=%)',
                NEW.vendor_invoice_id, st_new USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_vi_lines_immutable ON purchase.vendor_invoice_lines;
CREATE TRIGGER trg_vi_lines_immutable
    BEFORE UPDATE OR DELETE ON purchase.vendor_invoice_lines
    FOR EACH ROW EXECUTE FUNCTION purchase.fn_vi_lines_immutable();

-- ── Receipt lines ────────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sales.fn_receipt_lines_immutable() RETURNS trigger AS $$
DECLARE st_old text; st_new text;
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') THEN
        SELECT status INTO st_old FROM sales.receipts WHERE receipt_id = OLD.receipt_id;
        IF st_old IS NOT NULL AND st_old <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot % a line of a non-draft Receipt (receipt_id=%, status=%)',
                lower(TG_OP), OLD.receipt_id, st_old USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        SELECT status INTO st_new FROM sales.receipts WHERE receipt_id = NEW.receipt_id;
        IF st_new IS NOT NULL AND st_new <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot move a line onto a non-draft Receipt (receipt_id=%, status=%)',
                NEW.receipt_id, st_new USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_receipt_lines_immutable ON sales.receipt_lines;
CREATE TRIGGER trg_receipt_lines_immutable
    BEFORE UPDATE OR DELETE ON sales.receipt_lines
    FOR EACH ROW EXECUTE FUNCTION sales.fn_receipt_lines_immutable();

-- ── Journal lines (GL backbone) ────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION gl.fn_journal_lines_immutable() RETURNS trigger AS $$
DECLARE st_old text; st_new text;
BEGIN
    IF TG_OP IN ('UPDATE','DELETE') THEN
        SELECT status INTO st_old FROM gl.journal_entries WHERE journal_id = OLD.journal_id;
        IF st_old IS NOT NULL AND st_old <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot % a line of a non-draft Journal Entry (journal_id=%, status=%)',
                lower(TG_OP), OLD.journal_id, st_old USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        SELECT status INTO st_new FROM gl.journal_entries WHERE journal_id = NEW.journal_id;
        IF st_new IS NOT NULL AND st_new <> 'DRAFT' THEN
            RAISE EXCEPTION 'Cannot move a line onto a non-draft Journal Entry (journal_id=%, status=%)',
                NEW.journal_id, st_new USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_journal_lines_immutable ON gl.journal_lines;
CREATE TRIGGER trg_journal_lines_immutable
    BEFORE UPDATE OR DELETE ON gl.journal_lines
    FOR EACH ROW EXECUTE FUNCTION gl.fn_journal_lines_immutable();

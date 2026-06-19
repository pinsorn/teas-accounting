-- Posted-document LINE immutability — CLAUDE.md §4.2 / ม.86/4.
--
-- Header immutability is enforced on the *header* tables by 020 (journal_entries),
-- 040 (tax_invoices), 060 (vendor_invoices), 570 (receipts). The matching *_lines tables had
-- NO DB-level guard — isolation relied on FK + EF query filter + app code only — so a raw
-- `UPDATE sales.tax_invoice_lines SET line_amount=...` / `DELETE` against a POSTED document's
-- lines tripped no trigger (cross-validation 2026-06-19, finding B7). §4.2 requires enforcement
-- at the DB AND the app layer. These BEFORE UPDATE/DELETE triggers close the gap: once the parent
-- document leaves DRAFT (POSTED or VOIDED), its lines cannot be modified or deleted.
--
-- The ONE legitimate post-time line write (TaxInvoiceService.PostAsync snapshots Product.product_code
-- onto the lines) is flushed while the TI is still DRAFT, in its own SaveChanges BEFORE the header
-- flips to POSTED (see the comment there) — so this trigger needs no per-column allow-list and stays
-- trivially correct + uniform across all four tables. Idempotent (CREATE OR REPLACE / DROP IF EXISTS),
-- applied once by DbInitializer.ApplyScriptsAsync.

-- ── Tax Invoice lines ──────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sales.fn_ti_lines_immutable() RETURNS trigger AS $$
DECLARE st text; pid bigint;
BEGIN
    IF TG_OP = 'DELETE' THEN pid := OLD.tax_invoice_id; ELSE pid := NEW.tax_invoice_id; END IF;
    SELECT status INTO st FROM sales.tax_invoices WHERE tax_invoice_id = pid;
    IF st IS NOT NULL AND st <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot % a line of a non-draft Tax Invoice (tax_invoice_id=%, status=%)',
            lower(TG_OP), pid, st USING ERRCODE = 'check_violation';
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
DECLARE st text; pid bigint;
BEGIN
    IF TG_OP = 'DELETE' THEN pid := OLD.vendor_invoice_id; ELSE pid := NEW.vendor_invoice_id; END IF;
    SELECT status INTO st FROM purchase.vendor_invoices WHERE vendor_invoice_id = pid;
    IF st IS NOT NULL AND st <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot % a line of a non-draft Vendor Invoice (vendor_invoice_id=%, status=%)',
            lower(TG_OP), pid, st USING ERRCODE = 'check_violation';
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
DECLARE st text; pid bigint;
BEGIN
    IF TG_OP = 'DELETE' THEN pid := OLD.receipt_id; ELSE pid := NEW.receipt_id; END IF;
    SELECT status INTO st FROM sales.receipts WHERE receipt_id = pid;
    IF st IS NOT NULL AND st <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot % a line of a non-draft Receipt (receipt_id=%, status=%)',
            lower(TG_OP), pid, st USING ERRCODE = 'check_violation';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_receipt_lines_immutable ON sales.receipt_lines;
CREATE TRIGGER trg_receipt_lines_immutable
    BEFORE UPDATE OR DELETE ON sales.receipt_lines
    FOR EACH ROW EXECUTE FUNCTION sales.fn_receipt_lines_immutable();

-- ── Journal lines (GL backbone — append-only after post) ──────────────────────────
CREATE OR REPLACE FUNCTION gl.fn_journal_lines_immutable() RETURNS trigger AS $$
DECLARE st text; pid bigint;
BEGIN
    IF TG_OP = 'DELETE' THEN pid := OLD.journal_id; ELSE pid := NEW.journal_id; END IF;
    SELECT status INTO st FROM gl.journal_entries WHERE journal_id = pid;
    IF st IS NOT NULL AND st <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot % a line of a non-draft Journal Entry (journal_id=%, status=%)',
            lower(TG_OP), pid, st USING ERRCODE = 'check_violation';
    END IF;
    IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_journal_lines_immutable ON gl.journal_lines;
CREATE TRIGGER trg_journal_lines_immutable
    BEFORE UPDATE OR DELETE ON gl.journal_lines
    FOR EACH ROW EXECUTE FUNCTION gl.fn_journal_lines_immutable();

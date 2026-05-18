-- Posted JournalEntry rows are immutable on the critical fields.
-- A correction = new reversing JE + reissue (CLAUDE.md §4.2, §17.1).

CREATE OR REPLACE FUNCTION gl.fn_enforce_je_immutability() RETURNS trigger AS $$
BEGIN
    IF OLD.status = 'POSTED' THEN
        IF (OLD.doc_no       IS DISTINCT FROM NEW.doc_no
         OR OLD.doc_date     IS DISTINCT FROM NEW.doc_date
         OR OLD.posting_date IS DISTINCT FROM NEW.posting_date
         OR OLD.total_debit  IS DISTINCT FROM NEW.total_debit
         OR OLD.total_credit IS DISTINCT FROM NEW.total_credit
         OR OLD.company_id   IS DISTINCT FROM NEW.company_id
         OR OLD.branch_id    IS DISTINCT FROM NEW.branch_id)
        THEN
            RAISE EXCEPTION 'Cannot modify critical fields of posted journal entry (doc_no=%)', OLD.doc_no
                USING ERRCODE = 'check_violation';
        END IF;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_je_immutable ON gl.journal_entries;
CREATE TRIGGER trg_je_immutable
    BEFORE UPDATE ON gl.journal_entries
    FOR EACH ROW EXECUTE FUNCTION gl.fn_enforce_je_immutability();

CREATE OR REPLACE FUNCTION gl.fn_no_delete_posted_je() RETURNS trigger AS $$
BEGIN
    IF OLD.status <> 'DRAFT' THEN
        RAISE EXCEPTION 'Cannot delete non-draft journal entry (doc_no=%)', OLD.doc_no
            USING ERRCODE = 'check_violation';
    END IF;
    RETURN OLD;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_je_no_delete_posted ON gl.journal_entries;
CREATE TRIGGER trg_je_no_delete_posted
    BEFORE DELETE ON gl.journal_entries
    FOR EACH ROW EXECUTE FUNCTION gl.fn_no_delete_posted_je();

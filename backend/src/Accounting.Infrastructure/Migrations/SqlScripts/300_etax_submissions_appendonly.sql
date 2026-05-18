-- Sprint 13c — etax.submissions is an append-only legal audit trail (≥5 yr,
-- พรบ.การบัญชี ม.10). UPDATE/DELETE rejected by trigger; mirrors the
-- audit.activity_log pattern (030). Idempotent. Runs after the
-- AddETaxSubmissionsAudit EF migration has created the table.

CREATE OR REPLACE FUNCTION etax.fn_etax_submission_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'etax.submissions rows are immutable (cannot %, submission_id=%)',
        TG_OP, OLD.submission_id
        USING ERRCODE = 'check_violation';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_etax_sub_no_update ON etax.submissions;
CREATE TRIGGER trg_etax_sub_no_update
    BEFORE UPDATE ON etax.submissions
    FOR EACH ROW EXECUTE FUNCTION etax.fn_etax_submission_immutable();

DROP TRIGGER IF EXISTS trg_etax_sub_no_delete ON etax.submissions;
CREATE TRIGGER trg_etax_sub_no_delete
    BEFORE DELETE ON etax.submissions
    FOR EACH ROW EXECUTE FUNCTION etax.fn_etax_submission_immutable();

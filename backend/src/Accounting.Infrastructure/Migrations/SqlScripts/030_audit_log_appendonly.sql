-- audit.activity_log is append-only. Revoke UPDATE/DELETE at the database role level.
-- The application connects as `accounting_app`; only DBAs can mutate this table.

CREATE OR REPLACE FUNCTION audit.fn_audit_log_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit.activity_log rows are immutable (cannot %, activity_id=%)',
        TG_OP, OLD.activity_id
        USING ERRCODE = 'check_violation';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_audit_no_update ON audit.activity_log;
CREATE TRIGGER trg_audit_no_update
    BEFORE UPDATE ON audit.activity_log
    FOR EACH ROW EXECUTE FUNCTION audit.fn_audit_log_immutable();

DROP TRIGGER IF EXISTS trg_audit_no_delete ON audit.activity_log;
CREATE TRIGGER trg_audit_no_delete
    BEFORE DELETE ON audit.activity_log
    FOR EACH ROW EXECUTE FUNCTION audit.fn_audit_log_immutable();

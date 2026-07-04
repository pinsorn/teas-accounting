-- M1 (review 2026-07-04) — make audit.activity_log append-only actually enforced.
--
-- 030's header comment claims "Revoke UPDATE/DELETE at the database role level" but NO
-- REVOKE statement was ever present, and its row-level BEFORE UPDATE/DELETE triggers do
-- NOT fire on TRUNCATE (a statement-level op). So the migration-owning / app role could
-- `TRUNCATE audit.activity_log` and wipe the legal 5-year trail (พรบ.การบัญชี ม.14, §4.8)
-- untriggered. Close it two ways:
--   1) a BEFORE TRUNCATE statement trigger (role-agnostic; blocks even the table owner —
--      only bypassable by DISABLE TRIGGER, which needs ownership the app role lacks in prod);
--   2) the REVOKE the 030 comment always promised (defense in depth; conditional on the role
--      existing so dev/test — which lack `accounting_app` — don't error).
-- Append-only never truncates in prod; a test/dev reset uses DROP DATABASE, not TRUNCATE,
-- so this does not interfere. Idempotent; applied once by DbInitializer.ApplyScriptsAsync.

CREATE OR REPLACE FUNCTION audit.fn_audit_log_no_truncate() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit.activity_log is append-only — TRUNCATE is forbidden (§4.8, 5-year retention)'
        USING ERRCODE = 'check_violation';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_audit_no_truncate ON audit.activity_log;
CREATE TRIGGER trg_audit_no_truncate
    BEFORE TRUNCATE ON audit.activity_log
    FOR EACH STATEMENT EXECUTE FUNCTION audit.fn_audit_log_no_truncate();

-- REVOKE mutation rights the 030 comment promised. PUBLIC first (always present); then the
-- documented app role only if it exists on this instance.
REVOKE UPDATE, DELETE, TRUNCATE ON audit.activity_log FROM PUBLIC;

DO $$
BEGIN
    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'accounting_app') THEN
        REVOKE UPDATE, DELETE, TRUNCATE ON audit.activity_log FROM accounting_app;
    END IF;
END $$;

-- M12 (review 2026-07-04) — RLS backstop for audit.activity_log, follow-up AFTER H2 (Workers
-- per-company pin) + M3 (e-Tax retry scan pin) landed. Deferred until then: a fail-closed policy
-- on this nullable-company audit table would have blocked audit INSERTs from any still-unpinned
-- background path. Mirrors 581_missing_tables_rls.sql's ENABLE + FORCE + company_isolation shape,
-- but activity_log.company_id is NULLABLE (system-wide rows), so the policy adds an explicit
-- `OR company_id IS NULL` arm alongside the usual pinned-company / super-admin arms. This USING
-- clause is ALSO the INSERT WITH-CHECK (no separate WITH CHECK given) — every audit-write path
-- was verified (2026-07-04) to write a row that satisfies it before this policy was enabled:
--   - ActivityRecorder.Record (the ~40 call sites across Sales/Purchase/Payroll/Master/Identity)
--     always writes an explicit int company_id — never NULL. For ordinary document actions that
--     id is the entity's OWN CompanyId, loaded via the standard tenant-scoped query, so for a
--     non-super-admin it is ALWAYS equal to the pinned app.company_id.
--   - The three call sites where the written company_id can legitimately differ from the pinned
--     app.company_id — CompanySwitchService.SwitchAsync (target company being switched INTO),
--     RbacAdminService's ResolveTargetCompany-driven writes (super-admin cross-company RBAC
--     management), and OAuthEndpoints POST /oauth/authorize accept (super-admin granting for any
--     active company) — are ALL gated behind `tenant.IsSuperAdmin`, and TenantMiddleware pins
--     app.is_super_admin SESSION-WIDE for the whole request from that same flag (not per-query),
--     so at the moment of each such INSERT the connection already satisfies the policy's
--     `OR is_super_admin` arm regardless of the company_id value written.
--   - Two direct db.Set<ActivityLog>().Add(...) bypasses of ActivityRecorder
--     (ApiKeyService.AuditAsync, PrintTrackingService.Log) write _tenant.CompanyId / the entity's
--     own tenant-scoped CompanyId respectively — same safety argument applies.
--   - No SQL seed script inserts into audit.activity_log directly, and neither Accounting.Workers
--     nor the e-Tax retry path (ETaxRetryWorker/ETaxSubmissionPipeline) writes to this table at
--     all today (grep-confirmed) — H2/M3 are a sequencing precaution here, not an active risk.
--   - No path currently writes company_id = NULL (no "system row" producer exists yet); the
--     `OR company_id IS NULL` arm is forward-looking per the design and is exercised by this
--     dispatch's proving test via a direct INSERT, not by any app code path today.
-- Idempotent (DROP POLICY IF EXISTS; ENABLE/FORCE ROW LEVEL SECURITY are safe to re-run).
-- Applied once by DbInitializer.ApplyScriptsAsync (tracked in sys.applied_sql_scripts).

ALTER TABLE audit.activity_log ENABLE ROW LEVEL SECURITY;
ALTER TABLE audit.activity_log FORCE ROW LEVEL SECURITY;

DROP POLICY IF EXISTS company_isolation ON audit.activity_log;
CREATE POLICY company_isolation ON audit.activity_log
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR company_id IS NULL
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

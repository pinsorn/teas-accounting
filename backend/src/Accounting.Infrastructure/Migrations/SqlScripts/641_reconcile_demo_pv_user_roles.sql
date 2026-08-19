-- Cleanup unit C11 (specs/fix-c11-seed181-roles.md) — repairs an EXISTING database where
-- 181_seed_demo_pv_users.sql already ran (recorded in sys.applied_sql_scripts) but under the RLS
-- footgun that made its user_roles INSERTs silent no-ops (troubles-wiki.md "Fresh reseed:
-- ap_clerk/sales_staff login 401s auth.no_company_assignment (RLS-seed footgun in
-- 181_seed_demo_pv_users.sql)"). 181 itself is patched in place (SET LOCAL app.bypass_rls) for
-- databases where it has never run — that fix cannot reach a database where 181 is already
-- tracked as applied, since DbInitializer/PostgresFixture skip any script name already present in
-- sys.applied_sql_scripts. This script is the one-time reconciliation for that case, mirroring
-- 636_reconcile_missing_company_roles.sql's own precedent for the identical class of problem
-- (an already-applied seed whose write silently no-opped under RLS).
--
-- Confirmed live against teas_test (2026-08-19, read-only psql probe, superuser connection so RLS
-- is bypassed for the probe): 181_seed_demo_pv_users.sql is recorded applied; sys.roles holds
-- AP_CLERK/SALES_STAFF for company_id=1; sys.users holds ap_clerk (user_id=3)/sales_staff
-- (user_id=4), both active; sys.user_roles has ZERO rows for user_id IN (3,4) AND company_id=1.
--
-- SYSTEM, not added to DbInitializer.DemoScripts — same reasoning 636/637/638 already document
-- for a reconcile script that references company-1-shaped literals but only ever REPAIRS existing
-- rows, never CREATES placeholder data: both INSERTs below require the target sys.users row and
-- the sys.roles row to already exist (NOT EXISTS guards below check both), so on a fresh prod
-- install (no company 1, no ap_clerk/sales_staff users — 130/160/181 are all DEMO-gated) this
-- script's SELECTs match nothing and it is a silent, safe no-op. Being a brand-new script name
-- also means it is guaranteed to run on the next boot of ANY database regardless of the current or
-- historical SeedDemoData value, with no operator DELETE FROM sys.applied_sql_scripts step needed.
--
-- RUNTIME SECURITY CONTEXT (troubles-wiki.md "Startup SqlScript writing/reading G1/G3 RLS'd
-- tables ..."): this script runs at API startup / PostgresFixture init, one transaction, BEFORE
-- TenantMiddleware — app.company_id/app.bypass_rls are UNSET. sys.roles is G3
-- (600_superadmin_scoped_rls.sql): USING (company_id IS NULL OR company_id = app.company_id OR
-- app.bypass_rls) — without a bypass the SELECT ... FROM sys.roles below would match zero rows
-- under a real NOBYPASSRLS prod role, exactly reproducing 181's own original bug inside its own
-- repair script. sys.user_roles carries no RLS at all (not in 600's G1/G2/G3 arrays) — the write
-- side was never gated. SET LOCAL is transaction-scoped (auto-reverts; DbInitializer/
-- PostgresFixture run each script in its own transaction), so this can never leak into another
-- script or a real request. Same idiom as 610/615/617/620/636/639/640.
--
-- Idempotent: both INSERTs are guarded by NOT EXISTS against sys.user_roles, and 181's own
-- ON CONFLICT DO NOTHING on the same primary key (user_id, role_id, company_id, branch_id) is
-- still in force. A second run of this script finds the rows already present and touches nothing.
--
-- NB: NEVER put curly braces anywhere in this file — DbInitializer runs it through
-- ExecuteSqlRawAsync, which treats brace characters as string.Format placeholders and fails at boot.

SET LOCAL app.bypass_rls = 'on';

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 3, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r
WHERE r.role_code = 'AP_CLERK' AND r.company_id = 1
  AND EXISTS (SELECT 1 FROM sys.users u WHERE u.user_id = 3)
  AND NOT EXISTS (
    SELECT 1 FROM sys.user_roles ur
    WHERE ur.user_id = 3 AND ur.role_id = r.role_id AND ur.company_id = 1 AND ur.branch_id = 1
  );

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT 4, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.roles r
WHERE r.role_code = 'SALES_STAFF' AND r.company_id = 1
  AND EXISTS (SELECT 1 FROM sys.users u WHERE u.user_id = 4)
  AND NOT EXISTS (
    SELECT 1 FROM sys.user_roles ur
    WHERE ur.user_id = 4 AND ur.role_id = r.role_id AND ur.company_id = 1 AND ur.branch_id = 1
  );

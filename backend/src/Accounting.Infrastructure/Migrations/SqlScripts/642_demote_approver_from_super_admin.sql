-- Codex UI review 2026-08-20 R1 (specs/fix-codex-review-2026-08-20.md) — repairs an EXISTING
-- database where 160_seed_approver_user.sql already ran (recorded in sys.applied_sql_scripts)
-- under the OLD content that made the demo `approver` user a SUPER_ADMIN — bypassing every
-- permission check and defeating the Segregation-of-Duties (B2) flow this user exists to
-- demonstrate. 160 itself is patched in place (is_super_admin = FALSE, APPROVER role instead of
-- SUPER_ADMIN) for databases where it has never run — that fix cannot reach a database where 160
-- is already tracked as applied, since DbInitializer/PostgresFixture skip any script name already
-- present in sys.applied_sql_scripts. This script is the one-time reconciliation for that case,
-- mirroring 641_reconcile_demo_pv_user_roles.sql's own precedent (an already-applied seed whose
-- content needed correcting after the fact) and its exact identity discipline: the target user
-- must match 160's own seeded pins (username AND email), never a bare numeric id, so a real prod
-- user who coincidentally holds user_id=2 is never touched.
--
-- SYSTEM, not added to DbInitializer.DemoScripts — same reasoning 636/637/638/641 already
-- document: this script only ever REPAIRS an existing row identity-matched to 160's own literals
-- (username='approver' AND email='approver@teas.local'); on a fresh prod install (no such user)
-- every statement below matches nothing and is a silent, safe no-op. Being a brand-new script
-- name also guarantees it runs on the next boot of ANY database regardless of the current or
-- historical SeedDemoData value, with no operator DELETE FROM sys.applied_sql_scripts step needed.
--
-- RUNTIME SECURITY CONTEXT (troubles-wiki.md "Startup SqlScript writing/reading G1/G3 RLS'd
-- tables ..."): this script runs at API startup / PostgresFixture init, one transaction, BEFORE
-- TenantMiddleware — app.company_id/app.bypass_rls are UNSET. sys.roles is G3
-- (600_superadmin_scoped_rls.sql): USING (company_id IS NULL OR company_id = app.company_id OR
-- app.bypass_rls) — without a bypass, reading the per-company APPROVER role (company_id = 1)
-- below would match zero rows under a real NOBYPASSRLS prod role (unlike 160 itself, which reads
-- the role catalog EARLY in a fresh boot's file order, before 510 converts it to per-company
-- copies — this script runs on an ALREADY-migrated database, where only the per-company copy
-- exists). sys.users/sys.user_roles carry no RLS at all (not in 600's G1/G2/G3 arrays). SET LOCAL
-- is transaction-scoped (auto-reverts; DbInitializer/PostgresFixture run each script in its own
-- transaction) — same idiom as 610/615/617/620/636/639/640/641.
--
-- Idempotent: the is_super_admin UPDATE is a plain SET (safe to repeat, and additionally guarded
-- by "AND is_super_admin = TRUE" so a second run touches nothing); the SUPER_ADMIN role DELETE
-- matches at most the one row 160 created (scoped to company_id=1/branch_id=1, 160's own scope)
-- and deletes nothing once already removed; the APPROVER role INSERT is NOT EXISTS-guarded. A
-- second run of this script finds the user already repaired and touches nothing further.
--
-- NB: NEVER put curly braces anywhere in this file — DbInitializer runs it through
-- ExecuteSqlRawAsync, which treats brace characters as string.Format placeholders and fails at boot.

SET LOCAL app.bypass_rls = 'on';

UPDATE sys.users
SET is_super_admin = FALSE
WHERE username = 'approver' AND email = 'approver@teas.local' AND is_super_admin = TRUE;

DELETE FROM sys.user_roles ur
USING sys.users u, sys.roles r
WHERE ur.user_id = u.user_id AND ur.role_id = r.role_id
  AND u.username = 'approver' AND u.email = 'approver@teas.local'
  AND r.role_code = 'SUPER_ADMIN' AND ur.company_id = 1 AND ur.branch_id = 1;

INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
SELECT u.user_id, r.role_id, 1, 1, DATE '2026-01-01'
FROM sys.users u
JOIN sys.roles r ON r.role_code = 'APPROVER' AND r.company_id = 1
WHERE u.username = 'approver' AND u.email = 'approver@teas.local'
  AND NOT EXISTS (
    SELECT 1 FROM sys.user_roles ur
    WHERE ur.user_id = u.user_id AND ur.role_id = r.role_id AND ur.company_id = 1 AND ur.branch_id = 1
  );

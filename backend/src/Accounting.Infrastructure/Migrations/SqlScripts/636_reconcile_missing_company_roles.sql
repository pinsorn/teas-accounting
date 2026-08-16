-- Unit D / F1 repair (specs/fix-company-roles-seed-ordering.md) — a company created via raw SQL
-- AFTER 510_per_company_roles_reconcile.sql was recorded as applied has NO per-company role
-- catalogue at all: sys.seed_company_roles(company_id) is only ever invoked by 510's own
-- one-time fan-out loop (step 4) and by CompanyService.CreateAsync for companies created
-- through the app. Reproduced: boot once with Database:SeedDemoData=false (SYSTEM scripts incl.
-- 510 run; master.companies is empty so 510's loop is a no-op), then flip SeedDemoData=true for
-- a later boot — the DEMO company scripts (120/400/440) insert companies 1-3 AFTER 510 is
-- already tracked as applied, so nobody ever calls seed_company_roles for them and every
-- non-super-admin login 401s (auth.no_company_assignment).
--
-- This repairs the CATALOGUE only: any company with ZERO rows in sys.roles gets the full
-- per-company role set via the existing, already-idempotent sys.seed_company_roles(). The
-- NOT EXISTS predicate below only matches TOTAL absence — that is exactly F1's failure mode
-- (seed_company_roles never ran for the company at all), not a partial catalogue; calling
-- seed_company_roles for every company would also be safe (it is idempotent), but scoping to
-- companies missing every role states the intent more clearly, per this spec's own guidance.
--
-- Trap 6 — user_roles is a SEPARATE question, deliberately NOT touched here: on a database
-- that reached the broken state, the DEMO user-assignment scripts (e.g.
-- 181_seed_demo_pv_users.sql, 400/440's own sys.user_roles INSERTs, 550_seed_rbac_e2e_users.sql)
-- already ran against the empty catalogue, assigned NOTHING (their per-company-scoped JOINs
-- matched zero rows), and are permanently recorded in sys.applied_sql_scripts — they will never
-- re-run on their own. Repairing the catalogue does not retroactively populate their output.
-- Reconciling those assignments on an already-broken environment means re-applying those
-- specific DEMO scripts (every one of them is ON CONFLICT-idempotent, so simply
-- `DELETE FROM sys.applied_sql_scripts WHERE script_name IN (...)` then restart the app is
-- sufficient) — that is a deploy/runbook action for an operator to take deliberately on an
-- affected DEMO-seeded environment, not something this script should do as a side effect: this
-- script is SYSTEM (always applied, including on prod, which never has those DEMO rows to begin
-- with), so it must stay agnostic to which DEMO scripts exist or what they assign.
--
-- Prevention (this defect not recurring on a FRESH install) is handled separately: 120/400/440
-- now each self-heal by calling sys.seed_company_roles for their own company immediately after
-- inserting it, guarded by to_regprocedure() so a normal single-boot fresh install (where 510
-- has not yet defined the function when 120 runs, numerically 120 < 510) is unaffected — 510's
-- own fan-out still covers that path unchanged.
--
-- RLS (trap 3): sys.roles / sys.role_permissions are G3 (600_superadmin_scoped_rls.sql) —
-- USING (company_id IS NULL OR company_id = app.company_id OR app.bypass_rls). DbInitializer
-- runs every script with NO app.company_id set, so without a bypass this script's own
-- SELECT/INSERT would see/write only rows with company_id IS NULL — the fan-out below would
-- either find EVERY company "missing roles" forever (SELECT-side filtered) or 42501 on the
-- INSERT (WITH CHECK, no bypass) under a real NOBYPASSRLS role — silent-or-loud, never a partial
-- fix. Bypass for this script's own transaction only; SET LOCAL is transaction-scoped and
-- DbInitializer.ApplyScriptsAsync runs each script in its own transaction, so it can never leak
-- into another script or a real request. Same idiom as 610/615/617/620
-- (troubles-wiki.md "Startup SqlScript writing/reading G1/G3 RLS'd tables ...").
--
-- Verify (troubles-wiki "G3 fan-out" tell — the data, not the exit code):
--   SELECT company_id FROM master.companies c
--   WHERE NOT EXISTS (SELECT 1 FROM sys.roles r WHERE r.company_id = c.company_id);
-- must be empty after this script runs.
--
-- Idempotent: sys.seed_company_roles only inserts rows that are missing, and this script only
-- calls it for companies whose catalogue is currently EMPTY — a second run finds no such
-- companies and touches nothing.
--
-- NB: NEVER put curly braces anywhere in this file (not even in a comment) — DbInitializer runs
-- it through ExecuteSqlRawAsync, which treats brace characters as string.Format placeholders and
-- fails at boot.

SET LOCAL app.bypass_rls = 'on';

DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN
        SELECT comp.company_id
        FROM master.companies comp
        WHERE NOT EXISTS (SELECT 1 FROM sys.roles r WHERE r.company_id = comp.company_id)
    LOOP
        PERFORM sys.seed_company_roles(c.company_id);
    END LOOP;
END
$do$;

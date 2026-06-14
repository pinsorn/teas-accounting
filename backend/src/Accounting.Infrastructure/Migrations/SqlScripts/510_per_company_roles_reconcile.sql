-- Sprint 13k — Per-company RBAC reconcile (CLAUDE.md §4.7 multi-tenant isolation).
--
-- Converts the historical GLOBAL role catalog (one row per role_code) into PER-COMPANY role
-- copies, remaps user_roles to the per-company copy, captures a copy template for new companies,
-- then locks down sys.roles / sys.role_permissions with a (company_id, role_code) unique index,
-- a NOT-NULL-except-SUPER_ADMIN check, FKs, and RLS.
--
-- Ordering: EF migrations run BEFORE these SQL scripts (DbInitializer.MigrateAsync ->
-- ApplyScriptsAsync). This script is numbered 510 so it runs AFTER 110..500 have seeded the
-- global roles+grants. It treats the current global non-super roles+grants as the COPY TEMPLATE.
--
-- SUPER_ADMIN stays a single system-global role (company_id IS NULL). Super-admin power is the
-- is_super_admin user flag (PermissionHandler bypass), NOT a per-company role; the seeded
-- admin/approver user_roles -> SUPER_ADMIN rows are intentionally left untouched (company context).
--
-- Runs once (tracked in sys.applied_sql_scripts), as a single atomic statement batch.
-- NB: NEVER put curly braces anywhere in this file — EF ExecuteSqlRawAsync treats them as
-- string.Format placeholders and fails at boot.

-- 1. Template tables: the canonical role set + grants used to bootstrap NEW companies.
CREATE TABLE IF NOT EXISTS sys.role_templates (
    role_code   TEXT PRIMARY KEY,
    role_name   TEXT    NOT NULL,
    description TEXT,
    is_system   BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE TABLE IF NOT EXISTS sys.role_permission_templates (
    role_code       TEXT NOT NULL,
    permission_code TEXT NOT NULL,
    PRIMARY KEY (role_code, permission_code)
);

-- Populate templates from the CURRENT global non-super roles+grants (the seeded matrix), while
-- the global rows still exist.
INSERT INTO sys.role_templates (role_code, role_name, description, is_system)
SELECT r.role_code, r.role_name, r.description, r.is_system
FROM sys.roles r
WHERE r.company_id IS NULL AND r.role_code <> 'SUPER_ADMIN'
ON CONFLICT (role_code) DO NOTHING;

INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT r.role_code, p.permission_code
FROM sys.role_permissions rp
JOIN sys.roles r       ON r.role_id = rp.role_id
JOIN sys.permissions p ON p.permission_id = rp.permission_id
WHERE r.company_id IS NULL AND r.role_code <> 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

-- 2. Swap the global unique(role_code) for a per-company unique(company_id, role_code) BEFORE we
--    clone — otherwise the per-company copies (same role_code) collide with the global rows.
--    Drop whatever unique index currently covers exactly (role_code) — name-independent.
DO $do$
DECLARE idx TEXT;
BEGIN
    FOR idx IN
        SELECT i.relname
        FROM pg_index x
        JOIN pg_class i     ON i.oid = x.indexrelid
        JOIN pg_class t     ON t.oid = x.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = 'sys' AND t.relname = 'roles' AND x.indisunique
          AND (
                SELECT array_agg(a.attname::text ORDER BY a.attname::text)
                FROM pg_attribute a
                WHERE a.attrelid = t.oid AND a.attnum = ANY (x.indkey)
              ) = ARRAY['role_code']::text[]
    LOOP
        EXECUTE format('DROP INDEX IF EXISTS sys.%I', idx);
    END LOOP;
END
$do$;

CREATE UNIQUE INDEX IF NOT EXISTS ix_roles_company_role_code
    ON sys.roles (company_id, role_code);

-- Guarantee at most one system-global role per code (only SUPER_ADMIN today).
CREATE UNIQUE INDEX IF NOT EXISTS ux_roles_global_role_code
    ON sys.roles (role_code) WHERE company_id IS NULL;

-- 3. Reusable fan-out: clone all template roles+grants into one company (idempotent).
--    Reused by Accounting.Infrastructure CompanyService.CreateAsync for new tenants.
CREATE OR REPLACE FUNCTION sys.seed_company_roles(p_company_id INT)
RETURNS VOID
LANGUAGE plpgsql
AS $fn$
BEGIN
    INSERT INTO sys.roles (company_id, role_code, role_name, description, is_system)
    SELECT p_company_id, t.role_code, t.role_name, t.description, t.is_system
    FROM sys.role_templates t
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.roles r
        WHERE r.company_id = p_company_id AND r.role_code = t.role_code
    );

    INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
    SELECT r.role_id, p.permission_id, p_company_id
    FROM sys.role_permission_templates t
    JOIN sys.roles r       ON r.company_id = p_company_id AND r.role_code = t.role_code
    JOIN sys.permissions p ON p.permission_code = t.permission_code
    WHERE NOT EXISTS (
        SELECT 1 FROM sys.role_permissions rp
        WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
    );
END;
$fn$;

-- 4. Fan out to every existing company.
DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM sys.seed_company_roles(c.company_id);
    END LOOP;
END
$do$;

-- 5. Remap user_roles from a global non-super role to the same-company copy.
UPDATE sys.user_roles ur
SET role_id = nr.role_id
FROM sys.roles oldr, sys.roles nr
WHERE ur.role_id = oldr.role_id
  AND oldr.company_id IS NULL
  AND oldr.role_code <> 'SUPER_ADMIN'
  AND nr.company_id = ur.company_id
  AND nr.role_code  = oldr.role_code;

-- 6. Delete the now-orphaned global non-super roles (grants cascade). The guard keeps the delete
--    safe if any remap was missed (FK RESTRICT would block it anyway).
DELETE FROM sys.roles r
WHERE r.company_id IS NULL
  AND r.role_code <> 'SUPER_ADMIN'
  AND NOT EXISTS (SELECT 1 FROM sys.user_roles ur WHERE ur.role_id = r.role_id);

-- 7. Referential integrity + invariants (data is clean now).
ALTER TABLE sys.roles DROP CONSTRAINT IF EXISTS fk_roles_company;
ALTER TABLE sys.roles
    ADD CONSTRAINT fk_roles_company
    FOREIGN KEY (company_id) REFERENCES master.companies (company_id) ON DELETE CASCADE;

ALTER TABLE sys.role_permissions DROP CONSTRAINT IF EXISTS fk_role_permissions_company;
ALTER TABLE sys.role_permissions
    ADD CONSTRAINT fk_role_permissions_company
    FOREIGN KEY (company_id) REFERENCES master.companies (company_id) ON DELETE CASCADE;

-- company_id required for every role except the single system-global SUPER_ADMIN.
ALTER TABLE sys.roles DROP CONSTRAINT IF EXISTS ck_roles_company_required;
ALTER TABLE sys.roles
    ADD CONSTRAINT ck_roles_company_required
    CHECK (company_id IS NOT NULL OR role_code = 'SUPER_ADMIN');

-- 8. RLS — belt-and-braces tenant isolation (mirror 010_rls_policies). MUST be last, after all
--    DML, so the reconcile's own writes are never filtered. NULL company_id = system-global
--    (visible to every authenticated tenant; only SUPER_ADMIN qualifies).
DO $do$
DECLARE
    tbl text;
    tables text[] := ARRAY['sys.roles', 'sys.role_permissions'];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END
$do$;

-- 550_seed_rbac_e2e_users.sql — DEV/SMOKE ONLY (e2e RBAC UI gating).
-- One login per role in company 1 (VAT) + company 3 (non-VAT). Password 'Admin@1234'.
-- Mirrors 130/440 (crypt(... gen_salt('bf',12))). Idempotent. NB: no curly braces.
--
-- Username convention (consumed by frontend/e2e/helpers/rbac-manifest.ts):
--   company 1 (VAT):     rbac_<rolecode_lower>      uid 5101..5112
--   company 3 (non-VAT): rbac_nv_<rolecode_lower>   uid 5201..5212
-- SUPER_ADMIN users carry is_super_admin = TRUE (flag bypass); all others FALSE
-- and are assigned the matching PER-COMPANY role of that company.

DO $do$
DECLARE
    r   RECORD;
    uid INT;
    cfg RECORD;
BEGIN
    FOR cfg IN
        SELECT 1 AS company_id, 1 AS branch_id, 5100 AS uid_base, 'rbac_'    AS px
        UNION ALL
        SELECT 3, 3, 5200, 'rbac_nv_'
    LOOP
        FOR r IN
            SELECT role_code, ROW_NUMBER() OVER (ORDER BY role_code) AS rn
            FROM (SELECT DISTINCT role_code FROM sys.role_templates
                  UNION SELECT 'SUPER_ADMIN') t
        LOOP
            uid := cfg.uid_base + r.rn::INT;
            INSERT INTO sys.users (
                user_id, username, email, password_hash, full_name,
                is_super_admin, is_active, failed_login_count, must_change_password,
                created_at, updated_at, version)
            VALUES (
                uid,
                cfg.px || lower(r.role_code),
                cfg.px || lower(r.role_code) || '@e2e.local',
                crypt('Admin@1234', gen_salt('bf', 12)),
                'E2E ' || r.role_code,
                (r.role_code = 'SUPER_ADMIN'), TRUE, 0, FALSE,
                now(), now(), 0)
            ON CONFLICT (user_id) DO NOTHING;

            -- Assign to the per-company role of this company (SUPER_ADMIN = the global row).
            INSERT INTO sys.user_roles (user_id, role_id, company_id, branch_id, valid_from)
            SELECT uid, ro.role_id, cfg.company_id, cfg.branch_id, DATE '2026-01-01'
            FROM sys.roles ro
            WHERE ro.role_code = r.role_code
              AND (ro.company_id = cfg.company_id
                   OR (r.role_code = 'SUPER_ADMIN' AND ro.company_id IS NULL))
            ON CONFLICT DO NOTHING;
        END LOOP;
    END LOOP;
END
$do$;

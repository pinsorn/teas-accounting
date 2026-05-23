-- ============================================================
-- tools/wht-dedupe.sql  —  Sprint 13f P1 (defensive maintenance)
-- ============================================================
-- Context
--   tax.wht_types is effective-dated. The live UNIQUE index
--   ix_wht_types_company_id_code_effective_from (created by migration
--   20260517073242_AddARWhtSupport, which first DROPS the older 2-col
--   UNIQUE (company_id,code)) means any DbInitializer-bootstrapped DB
--   ALWAYS has a UNIQUE on wht_types and CANNOT accumulate the
--   ADS×2 / RENT×2 / SVC×2 duplicates that were live-observed pre-Sprint-13d.
--   All three WHT seeds (120, 220, 400) are already
--   `ON CONFLICT (company_id, code, effective_from) DO NOTHING`.
--
--   The current accounting_dev is verified clean (0 duplicate groups).
--   This script is a NON-SCHEMA, idempotent maintenance tool for any
--   *legacy* environment that was bootstrapped before the UNIQUE existed
--   and still carries duplicates (it would otherwise be impossible to add
--   the UNIQUE there). It is NOT a migration (no schema change) — same
--   class as tools/dev-db-resync.sql.
--
-- Safety
--   * Idempotent — on a clean DB every statement affects 0 rows.
--   * FK-safe — repoints master.customers.default_wht_type_id,
--     sales.receipts.wht_type_id and master.products.default_wht_type_id
--     from any to-be-deleted duplicate id to the KEPT id BEFORE deleting.
--   * Keeps the LOWEST wht_type_id per (company_id, code, effective_from)
--     (the original row any older document already references).
--   * Wrapped in a transaction; ROLLBACKs on any error.
-- ============================================================

BEGIN;

-- Map every duplicate row → the surviving (lowest) id in its group.
CREATE TEMP TABLE _wht_dupe_map ON COMMIT DROP AS
SELECT w.wht_type_id           AS dup_id,
       k.keep_id               AS keep_id
FROM   tax.wht_types w
JOIN (
    SELECT company_id, code, effective_from,
           MIN(wht_type_id) AS keep_id
    FROM   tax.wht_types
    GROUP  BY company_id, code, effective_from
    HAVING COUNT(*) > 1
) k
  ON  w.company_id = k.company_id
  AND w.code = k.code
  AND w.effective_from = k.effective_from
WHERE w.wht_type_id <> k.keep_id;

-- Repoint FK references off the doomed duplicate ids.
UPDATE master.customers c
SET    default_wht_type_id = m.keep_id
FROM   _wht_dupe_map m
WHERE  c.default_wht_type_id = m.dup_id;

UPDATE sales.receipts r
SET    wht_type_id = m.keep_id
FROM   _wht_dupe_map m
WHERE  r.wht_type_id = m.dup_id;

UPDATE master.products p
SET    default_wht_type_id = m.keep_id
FROM   _wht_dupe_map m
WHERE  p.default_wht_type_id = m.dup_id;

-- Delete the duplicates (references now point at the kept rows).
DELETE FROM tax.wht_types w
USING  _wht_dupe_map m
WHERE  w.wht_type_id = m.dup_id;

-- Belt-and-suspenders: ensure the UNIQUE exists (no-op where present;
-- protective on a legacy DB that somehow lacks it — now safe because
-- duplicates were just removed above).
CREATE UNIQUE INDEX IF NOT EXISTS ix_wht_types_company_id_code_effective_from
    ON tax.wht_types (company_id, code, effective_from);

COMMIT;

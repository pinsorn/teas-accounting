-- ============================================================
-- tools/dev-db-resync.sql  —  Sprint 14.5 §14 one-time repair
-- ============================================================
-- Purpose
--   The long-lived shared dev DB (accounting_dev) has NO test teardown.
--   sys.number_sequences.current_value is the running-number allocator;
--   the next allocation is current_value + 1. Iterative e2e re-runs left
--   posted document rows whose embedded running number is HIGHER than the
--   allocator's current_value (the failed/retried allocations did not
--   commit). The next allocation then re-issues an existing number and
--   trips UNIQUE (company_id, branch_id, doc_no) — observed as the
--   `ix_journal_entries` 500 in external-api-microservice.spec.ts.
--
--   This resyncs current_value up to MAX(running number actually present)
--   per (company_id, branch_id, prefix_code, sub_prefix, period) so the
--   next allocation is collision-free.
--
-- Properties
--   * IDEMPOTENT — guarded by `current_value < observed_max`; re-running
--     after a clean state updates zero rows.
--   * NON-DESTRUCTIVE — only advances the counter; never lowers it,
--     never deletes/edits any document row (compliance: posted docs are
--     immutable, §4.2).
--   * One-time dev maintenance — lives in tools/, NOT Migrations/.
--
-- doc_no grammar (CLAUDE.md §0): MM-YYYY-PREFIX-NNNN
--   and for Payment Vouchers: MM-YYYY-PV-CATEGORY-NNNN
--   part 1 = period_month, part 2 = period_year, part 3 = prefix_code,
--   (PV only) part 4 = sub_prefix, last part = zero-padded running number.
-- ============================================================

BEGIN;

-- 1. GL journal entries (gl.journal_entries) — the actively-blocking site.
--    doc_no = MM-YYYY-<PREFIX>-NNNN, sub_prefix = '' (no category segment).
UPDATE sys.number_sequences ns
SET    current_value = src.max_no,
       last_issued_at = NOW()
FROM (
    SELECT je.company_id,
           je.branch_id,
           SPLIT_PART(je.doc_no, '-', 3)                              AS prefix_code,
           SPLIT_PART(je.doc_no, '-', 2)::INT                         AS period_year,
           SPLIT_PART(je.doc_no, '-', 1)::INT                         AS period_month,
           MAX(REGEXP_REPLACE(SPLIT_PART(je.doc_no, '-', 4),
                              '^0+', '')::INT)                         AS max_no
    FROM   gl.journal_entries je
    WHERE  je.doc_no IS NOT NULL
      AND  je.status <> 'DRAFT'
      AND  je.doc_no ~ '^[0-9]{2}-[0-9]{4}-[A-Z]+-[0-9]+$'
    GROUP  BY 1, 2, 3, 4, 5
) src
WHERE ns.company_id   = src.company_id
  AND ns.branch_id    = src.branch_id
  AND ns.prefix_code  = src.prefix_code
  AND ns.sub_prefix   = ''
  AND ns.period_year  = src.period_year
  AND ns.period_month = src.period_month
  AND ns.current_value < src.max_no;

-- 2. Sales tax invoices / receipts (sales.tax_invoices) — same 4-part grammar.
UPDATE sys.number_sequences ns
SET    current_value = src.max_no,
       last_issued_at = NOW()
FROM (
    SELECT ti.company_id,
           ti.branch_id,
           SPLIT_PART(ti.doc_no, '-', 3)                              AS prefix_code,
           SPLIT_PART(ti.doc_no, '-', 2)::INT                         AS period_year,
           SPLIT_PART(ti.doc_no, '-', 1)::INT                         AS period_month,
           MAX(REGEXP_REPLACE(SPLIT_PART(ti.doc_no, '-', 4),
                              '^0+', '')::INT)                         AS max_no
    FROM   sales.tax_invoices ti
    WHERE  ti.doc_no IS NOT NULL
      AND  ti.status = 'POSTED'
      AND  ti.doc_no ~ '^[0-9]{2}-[0-9]{4}-[A-Z]+-[0-9]+$'
    GROUP  BY 1, 2, 3, 4, 5
) src
WHERE ns.company_id   = src.company_id
  AND ns.branch_id    = src.branch_id
  AND ns.prefix_code  = src.prefix_code
  AND ns.sub_prefix   = ''
  AND ns.period_year  = src.period_year
  AND ns.period_month = src.period_month
  AND ns.current_value < src.max_no;

-- 3. Payment Vouchers (purchase.payment_vouchers) — 5-part grammar with a
--    mandatory CATEGORY sub-prefix (CLAUDE.md §12.1):
--    MM-YYYY-PV-<CATEGORY>-NNNN  → part 4 = sub_prefix, part 5 = number.
UPDATE sys.number_sequences ns
SET    current_value = src.max_no,
       last_issued_at = NOW()
FROM (
    SELECT pv.company_id,
           pv.branch_id,
           SPLIT_PART(pv.doc_no, '-', 3)                              AS prefix_code,
           SPLIT_PART(pv.doc_no, '-', 4)                              AS sub_prefix,
           SPLIT_PART(pv.doc_no, '-', 2)::INT                         AS period_year,
           SPLIT_PART(pv.doc_no, '-', 1)::INT                         AS period_month,
           MAX(REGEXP_REPLACE(SPLIT_PART(pv.doc_no, '-', 5),
                              '^0+', '')::INT)                         AS max_no
    FROM   purchase.payment_vouchers pv
    WHERE  pv.doc_no IS NOT NULL
      AND  pv.status = 'POSTED'
      AND  pv.doc_no ~ '^[0-9]{2}-[0-9]{4}-[A-Z]+-[A-Z0-9]+-[0-9]+$'
    GROUP  BY 1, 2, 3, 4, 5, 6
) src
WHERE ns.company_id   = src.company_id
  AND ns.branch_id    = src.branch_id
  AND ns.prefix_code  = src.prefix_code
  AND ns.sub_prefix   = src.sub_prefix
  AND ns.period_year  = src.period_year
  AND ns.period_month = src.period_month
  AND ns.current_value < src.max_no;

COMMIT;

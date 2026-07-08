-- Defensive hardening of tax.v_number_gaps (troubles-wiki.md "Test-seeded DocNo poisons
-- v_number_gaps"; specs/year-end-closing.md Tier-2 follow-up). 050_number_gap_audit_view.sql's
-- regexp_match(doc_no, backslash-d-plus-dollar)[1]::int casts the trailing digit-run of EVERY
-- posted doc_no (sales.tax_invoices / gl.journal_entries / purchase.payment_vouchers, ALL
-- companies) before any company_id filter is applied — one row anywhere in the DB with an
-- 11+ digit trailing run overflows int4 (22003) and breaks the view for EVERY company's query,
-- not just the offending one. Two defensive changes only, semantics otherwise identical:
--   1. Cast to bigint instead of int internally (bigint safely holds up to 19 digits).
--   2. Length-cap the captured digit run at 18 chars (max 18-digit value is comfortably inside
--      bigint range) — a run longer than 18 is treated as no-match (seq_no = NULL), same as the
--      existing "no trailing digits at all" case, so it never contributes to MAX(seq_no)/
--      generate_series. No real doc_no (NumberSequenceService-issued) ever approaches 18 digits;
--      this only guards against garbage/malformed data.
-- The FINAL exposed missing_seq_no column stays plain int (cast back down at the outer SELECT)
-- — NumberGapReportService.cs's Row(string Series, int MissingSeqNo) and
-- Sprint1HardeningTests's SqlQueryRaw of int both depend on that exact column type; real gap
-- numbers are always small so this downcast is safe for every legitimate row.
-- Idempotent (CREATE OR REPLACE); does NOT edit 050 itself (apply-once tracking — 613
-- supersedes 050's view definition on both existing and fresh DBs, lexically after 612).
-- Do not put literal curly-brace characters anywhere in this file, including comments — EF
-- ExecuteSqlRawAsync parses the WHOLE script as a composite format string and throws even with
-- zero real parameters (this exact self-referential mistake broke the first draft of this file).

CREATE OR REPLACE VIEW tax.v_number_gaps AS
WITH issued AS (
    SELECT company_id, doc_no,
           CASE
               WHEN length((regexp_match(doc_no, '(\d+)$'))[1]) <= 18
               THEN (regexp_match(doc_no, '(\d+)$'))[1]::bigint
               ELSE NULL
           END                                                AS seq_no,
           regexp_replace(doc_no, '-\d+$', '')                AS series
    FROM (
        SELECT company_id, doc_no FROM sales.tax_invoices
            WHERE status = 'POSTED' AND doc_no IS NOT NULL
        UNION ALL
        SELECT company_id, doc_no FROM gl.journal_entries
            WHERE status = 'POSTED' AND doc_no IS NOT NULL
        UNION ALL
        SELECT company_id, doc_no FROM purchase.payment_vouchers
            WHERE status = 'POSTED' AND doc_no IS NOT NULL
    ) d
),
bounds AS (
    SELECT company_id, series, MAX(seq_no) AS max_no
    FROM issued
    GROUP BY company_id, series
)
SELECT b.company_id,
       b.series,
       g::int AS missing_seq_no
FROM bounds b
CROSS JOIN LATERAL generate_series(1, b.max_no) AS g
LEFT JOIN issued i
       ON i.company_id = b.company_id
      AND i.series     = b.series
      AND i.seq_no     = g
WHERE i.doc_no IS NULL;

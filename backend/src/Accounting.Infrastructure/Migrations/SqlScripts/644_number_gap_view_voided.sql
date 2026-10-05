-- specs/cancel-reissue-sales-docs.md section 3.2.3 (WP-1). A cancelled (VOIDED) Tax Invoice keeps its
-- number (the voided number is retained, never reused), so tax.v_number_gaps must count VOIDED
-- tax invoices as issued or every cancellation would be reported as a gap. Body = 613 verbatim
-- (bigint internally, 18-char length guard, outer int cast) with only the Tax Invoice arm widened
-- to status IN (POSTED, VOIDED). Journal entries and payment vouchers are unchanged.
-- CREATE OR REPLACE VIEW; 613 is apply-once and is not edited. Idempotent.
-- Do not put literal curly-brace characters anywhere in this file, comments included.

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
            WHERE status IN ('POSTED', 'VOIDED') AND doc_no IS NOT NULL
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

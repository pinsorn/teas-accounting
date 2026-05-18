-- Number-gap audit (CLAUDE.md §4.3: sequential, no gaps). A row here = a missing
-- sequence number within the issued range for a (company, series) — i.e. a compliance
-- defect. With the atomic ON CONFLICT allocation that runs on the caller's transaction,
-- a rolled-back post never consumes a number, so this view must stay EMPTY in practice.
-- Auditors / the Sprint-1 regression test query this directly.

CREATE OR REPLACE VIEW tax.v_number_gaps AS
WITH issued AS (
    SELECT company_id, doc_no,
           (regexp_match(doc_no, '(\d+)$'))[1]::int          AS seq_no,
           regexp_replace(doc_no, '-\d+$', '')               AS series
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
       g AS missing_seq_no
FROM bounds b
CROSS JOIN LATERAL generate_series(1, b.max_no) AS g
LEFT JOIN issued i
       ON i.company_id = b.company_id
      AND i.series     = b.series
      AND i.seq_no     = g
WHERE i.doc_no IS NULL;

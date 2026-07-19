-- CRIT-1 (specs/fix-swarm-crit-numbering-rbac.md) — sys.number_sequences.current_value can drift
-- BELOW the true MAX(doc_no) already present in a doc table (historical rows / teas_test +
-- migration-squash resets / a bucket whose counter was never advanced — see spec §"Drift ORIGIN").
-- NumberSequenceService.NextAsync's UPSERT is concurrency-safe but hands out an already-used number
-- when the counter itself is behind, surfacing as 23505 at the document's own SaveChanges.
--
-- This reconcile lifts every (company,branch,prefix,sub,year,month) bucket to
-- GREATEST(current_value, true_max) across every table that owns a doc_no — NEVER lowers a bucket,
-- inserts missing buckets. Idempotent; safe to re-run (only ever raises current_value, or no-ops
-- once every bucket already sits at/above its true max).
--
-- doc_no format (DocumentNumber.cs, confirmed): MM-YYYY-PREFIX[-SUB]-NNNN. month=substr(1,2),
-- year=substr(4,4), seq=trailing 4-6 digits, middle=text between "MM-YYYY-" and the final "-NNNN",
-- prefix=split_part(middle,'-',1), sub=middle with the leading "prefix-" stripped ('' if none).
--
-- Table list — VERIFIED against the actual EF configurations (Persistence/Configurations/**), not
-- guessed. The IMPLEMENTATION CONTRACT's draft list included "sales.invoices", which does not exist
-- as a table (TaxInvoice maps to sales.tax_invoices, already covered below) — omitted. ADDED
-- tax.wht_certificates (WHERE direction='P' — Payable certs only; Receivable certs carry the
-- customer's own cert number, not ours, per ReceiptService/ReceiptWhtLine, and are excluded exactly
-- like the table's own unique index ix_wht_certificates_company_id_doc_no does via its
-- direction='P' filter): PaymentVoucherService.PostAsync allocates its WT-NNNN doc_no through the
-- SAME NextAsync/sys.number_sequences path as every other document, so it carries the identical
-- drift risk and was not in the contract's draft list — a real gap the CRIT-1 fix would otherwise
-- leave open. Every table below carries a branch_id column (confirmed via each entity + EF
-- configuration), so branch_id is read directly off each row rather than defaulted.
--
-- RLS: sys.number_sequences (010_rls_policies.sql) and every doc table below (010/322/323/480/
-- 570/571/572/573/581/600/612/614/616/619_*rls.sql) carry FORCE ROW LEVEL SECURITY, company_isolation
-- policy keyed on current_setting('app.company_id'). Runs under the NOBYPASSRLS app role — NO
-- superuser assumption (memory: v1.22.0 died on 625 running as superuser). Mirrors 625's per-company
-- set_config('app.company_id', ...) loop (transaction-local via the 3rd `true` arg) rather than the
-- app.is_super_admin bypass, so every read+write below is scoped to exactly the company whose row it
-- touches — no cross-tenant leak even if the loop is interrupted.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        WITH raw_docs AS (
            SELECT company_id, branch_id, doc_no FROM gl.journal_entries        WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM purchase.purchase_orders   WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM purchase.vendor_invoices   WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM purchase.payment_vouchers  WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.tax_invoices         WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.receipts             WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.quotations          WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.sales_orders        WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.delivery_orders     WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.billing_notes       WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM sales.tax_adjustment_notes WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM payroll.payroll_runs      WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM expense.expense_claims    WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM fixedasset.fixed_assets   WHERE company_id = c.company_id AND doc_no IS NOT NULL
            UNION ALL
            SELECT company_id, branch_id, doc_no FROM tax.wht_certificates
                WHERE company_id = c.company_id AND doc_no IS NOT NULL AND direction = 'P'
        ),
        parsed AS (
            SELECT
                company_id, branch_id,
                substr(doc_no, 1, 2)::int AS period_month,
                substr(doc_no, 4, 4)::int AS period_year,
                (regexp_match(doc_no, '-([0-9]+)$'))[1] AS seq_str,
                substr(doc_no, 9) AS after_period
            FROM raw_docs
            -- Curly-brace-free on purpose (NEVER put curly braces anywhere in this file — EF's
            -- ExecuteSqlRawAsync runs it through string.Format, which treats a literal brace as a
            -- composite-format placeholder and throws; bounded regex quantifiers would break
            -- DbInitializer at boot). A plain '+' is equally correct here: this WHERE is a
            -- defensive shape filter, and the trailing digit run is re-extracted exactly by the
            -- regexp_match above regardless of its length.
            WHERE doc_no ~ '^(0[1-9]|1[0-2])-[0-9][0-9][0-9][0-9]-[A-Z][A-Z]+(-[A-Z0-9]+)*-[0-9]+$'
        ),
        grouped AS (
            SELECT
                company_id, branch_id, period_year, period_month,
                substr(after_period, 1, length(after_period) - length(seq_str) - 1) AS middle,
                seq_str::int AS seq
            FROM parsed
        ),
        buckets AS (
            SELECT
                company_id, branch_id, period_year, period_month,
                split_part(middle, '-', 1) AS prefix_code,
                CASE WHEN middle = split_part(middle, '-', 1) THEN ''
                     ELSE substr(middle, length(split_part(middle, '-', 1)) + 2)
                END AS sub_prefix,
                MAX(seq) AS max_seq
            FROM grouped
            GROUP BY company_id, branch_id, period_year, period_month, middle
        )
        INSERT INTO sys.number_sequences
            (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at)
        SELECT company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, max_seq, now()
        FROM buckets
        ON CONFLICT (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month)
        DO UPDATE SET
            current_value = GREATEST(sys.number_sequences.current_value, EXCLUDED.current_value),
            last_issued_at = CASE WHEN EXCLUDED.current_value > sys.number_sequences.current_value
                                   THEN EXCLUDED.last_issued_at
                                   ELSE sys.number_sequences.last_issued_at END;
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

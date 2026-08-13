-- H1 (specs/fix-duplicate-tax-doc-numbers.md) — branch scopes sys.number_sequences but the printed
-- doc_no carries no branch segment (DocumentNumber.Build: MM-YYYY-PREFIX[-SUB]-NNNN), so any company
-- driven through two channels (web UI mints from branch 0, API key / MCP mints from a real branch id)
-- ran two counters into one visible number space. WP-1 (INumberSequenceService.NextAsync) now always
-- binds branch_id=0 — "company-wide" — for every NEW allocation. This script is the one-time reconcile
-- so the FIRST post-deploy allocation lands one past the highest number the company has EVER shown a
-- human, not one past whatever the branch-0 bucket happened to be sitting at.
--
-- Modeled on 626_reconcile_number_sequences.sql (CRIT-1) line for line. Three differences from 626 at
-- authoring time: (1) the final GROUP BY drops branch_id — one company-wide bucket per
-- (prefix,sub,year,month) instead of one per branch; (2) the INSERT writes the literal 0 into
-- branch_id — 0 now means "company-wide", not "the web-UI branch"; (3) this header. Plus two later
-- defensive additions 626 does NOT have (Tier-2 FIX 2, 2026-08-14 — see specs/fix-duplicate-tax-doc-
-- numbers.md attempt log): (4) the seq cast is guarded so an oversized garbage digit-run can never
-- overflow int4; (5) an entirely-garbage bucket is excluded rather than written with a NULL
-- current_value. 626 carries neither guard — out of scope, "do not edit 626". Never lowers a bucket,
-- only ever lifts the branch-0 row to GREATEST(current_value, true_max across ALL branches' historical
-- rows). Idempotent; safe to re-run. Does NOT touch any doc_no — no existing document is renumbered (I2).
--
-- doc_no format (DocumentNumber.cs, confirmed): MM-YYYY-PREFIX[-SUB]-NNNN. month=substr(1,2),
-- year=substr(4,4), seq=trailing digit run, middle=text between "MM-YYYY-" and the final "-NNNN",
-- prefix=split_part(middle,'-',1), sub=middle with the leading "prefix-" stripped ('' if none).
--
-- Table list — the SAME 15-table union as 626 (verified against the EF configurations again for this
-- spec, F1.2): gl.journal_entries, purchase.purchase_orders, purchase.vendor_invoices,
-- purchase.payment_vouchers, sales.tax_invoices, sales.receipts, sales.quotations, sales.sales_orders,
-- sales.delivery_orders, sales.billing_notes, sales.tax_adjustment_notes, payroll.payroll_runs,
-- expense.expense_claims, fixedasset.fixed_assets, tax.wht_certificates (direction='P' only).
--
-- RLS: sys.number_sequences (010_rls_policies.sql) and every doc table above carry FORCE ROW LEVEL
-- SECURITY, company_isolation policy keyed on current_setting('app.company_id'). Runs under the
-- NOBYPASSRLS app role at boot, with NO session GUCs set yet (DbInitializer runs SqlScripts before any
-- middleware) — mirrors 626's per-company set_config('app.company_id', ...) loop (transaction-local via
-- the 3rd `true` arg) so every read and write below is scoped to exactly the company whose row it
-- touches. Without the pin every SELECT on these tables returns zero rows under RLS and the INSERT
-- silently does nothing — this is the exact failure class documented in troubles-wiki.
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
                -- Overflow guard (F21 / troubles-wiki 22003; 613 hit this same class in
                -- v_number_gaps): an 11+ digit trailing run in a garbage/malformed doc_no
                -- overflows int4 on the cast below and rolls back the WHOLE script, which is
                -- never recorded in sys.applied_sql_scripts and so retries on every boot.
                -- sys.number_sequences.current_value is declared int (not bigint, unlike 613's
                -- view), so the guard bounds the digit run to <= 9 characters -- the largest
                -- length for which EVERY possible value (max 999,999,999) is unconditionally
                -- inside int4 range (2,147,483,647) -- rather than routing through bigint first:
                -- a bigint intermediate would only move the overflow to this INSERT's implicit
                -- bigint-to-int assignment cast for any 10-18 digit garbage run, not remove it.
                -- A too-long run is treated as "no match" (NULL), same as parsed already treats
                -- "no trailing digits at all" -- MAX(seq) skips it and it never contributes to
                -- current_value. No real doc_no (NumberSequenceService-issued, 4-digit NNNN) ever
                -- approaches 9 digits; this only guards against garbage/malformed data (626's own
                -- copy of this cast carries the identical unguarded risk -- out of scope, 626 is
                -- not to be edited).
                CASE WHEN length(seq_str) <= 9 THEN seq_str::int ELSE NULL END AS seq
            FROM parsed
        ),
        -- DIFFERENCE 1 from 626: GROUP BY drops branch_id — one company-wide bucket per
        -- (prefix,sub,year,month), taking the MAX seq ever issued by ANY branch in that bucket.
        buckets AS (
            SELECT
                company_id, period_year, period_month,
                split_part(middle, '-', 1) AS prefix_code,
                CASE WHEN middle = split_part(middle, '-', 1) THEN ''
                     ELSE substr(middle, length(split_part(middle, '-', 1)) + 2)
                END AS sub_prefix,
                MAX(seq) AS max_seq
            FROM grouped
            -- A bucket where EVERY row was excluded by the length guard above (all-garbage,
            -- no legitimate sibling) would otherwise produce max_seq = NULL here, and
            -- sys.number_sequences.current_value is NOT NULL — the INSERT below would then
            -- fail with 23502 (not-null violation), rolling back the WHOLE script, which is
            -- never recorded in sys.applied_sql_scripts and so retries on every boot. Same
            -- failure MODE as the int4-overflow guard exists to prevent, different SQLSTATE.
            -- A bucket with no surviving legitimate seq simply emits no row (nothing to lift).
            WHERE seq IS NOT NULL
            GROUP BY company_id, period_year, period_month, middle
        )
        -- DIFFERENCE 2 from 626: the literal 0 replaces the (now-dropped) branch_id column —
        -- every row this script writes lands in the company-wide bucket.
        INSERT INTO sys.number_sequences
            (company_id, branch_id, prefix_code, sub_prefix, period_year, period_month, current_value, last_issued_at)
        SELECT company_id, 0, prefix_code, sub_prefix, period_year, period_month, max_seq, now()
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

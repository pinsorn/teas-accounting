-- H1 (specs/fix-duplicate-tax-doc-numbers.md) §3.5 — tax.v_number_gaps (613/050) finds MISSING
-- numbers only; it groups by (company_id, series) with series = doc_no minus the trailing -NNNN, so a
-- cross-branch DUPLICATE pair contributes one distinct series value and the view stays empty. That is
-- how /reports/number-gaps answered hasGaps:false over the very period that held H1's duplicates. This
-- new view finds the complementary case: the SAME doc_no appearing more than once for one company.
--
-- Do not edit 613 (F23 — editing an already-applied script is a no-op on an existing DB; a superseding
-- change is always a new numbered file). tax.v_number_gaps is unchanged and still correct at what it
-- does.
--
-- Same 15-table union as 626/634 (doc_no IS NOT NULL; tax.wht_certificates restricted to
-- direction='P' — Receivable certs carry the customer's own cert number, not ours). No numeric cast
-- anywhere — group on the string doc_no directly (F21: a cast-to-int/bigint on the trailing digit run
-- is unnecessary here and 613 already shows the overflow footgun a cast invites; a duplicate-detection
-- view needs no arithmetic on the sequence at all).
--
-- RLS: deliberately NO RLS on this view (matches tax.v_number_gaps exactly — F14), because it spans
-- every company for a future cross-tenant audit. NumberGapReportService MUST filter company_id =
-- _tenant.CompanyId itself; a missing filter there is a cross-tenant leak, not a cosmetic bug.
--
-- Do not put literal curly-brace characters anywhere in this file, including comments (F20 — EF's
-- ExecuteSqlRawAsync runs the whole script through string.Format).

CREATE OR REPLACE VIEW tax.v_duplicate_doc_numbers AS
WITH docs AS (
    SELECT 'gl.journal_entries'::text          AS tbl, company_id, branch_id, doc_no FROM gl.journal_entries        WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'purchase.purchase_orders',              company_id, branch_id, doc_no FROM purchase.purchase_orders    WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'purchase.vendor_invoices',              company_id, branch_id, doc_no FROM purchase.vendor_invoices    WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'purchase.payment_vouchers',             company_id, branch_id, doc_no FROM purchase.payment_vouchers   WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.tax_invoices',                    company_id, branch_id, doc_no FROM sales.tax_invoices          WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.receipts',                        company_id, branch_id, doc_no FROM sales.receipts              WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.quotations',                      company_id, branch_id, doc_no FROM sales.quotations            WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.sales_orders',                    company_id, branch_id, doc_no FROM sales.sales_orders          WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.delivery_orders',                 company_id, branch_id, doc_no FROM sales.delivery_orders       WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.billing_notes',                   company_id, branch_id, doc_no FROM sales.billing_notes         WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'sales.tax_adjustment_notes',            company_id, branch_id, doc_no FROM sales.tax_adjustment_notes  WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'payroll.payroll_runs',                  company_id, branch_id, doc_no FROM payroll.payroll_runs        WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'expense.expense_claims',                company_id, branch_id, doc_no FROM expense.expense_claims      WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'fixedasset.fixed_assets',               company_id, branch_id, doc_no FROM fixedasset.fixed_assets     WHERE doc_no IS NOT NULL
    UNION ALL
    SELECT 'tax.wht_certificates',                  company_id, branch_id, doc_no FROM tax.wht_certificates        WHERE doc_no IS NOT NULL AND direction = 'P'
)
SELECT company_id, tbl, doc_no, count(*) AS copies,
       array_agg(DISTINCT branch_id) AS branch_ids
FROM docs
GROUP BY company_id, tbl, doc_no
HAVING count(*) > 1;

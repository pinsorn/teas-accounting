-- ⚠️ CORRECTED 2026-08-12 after the first real run. The original guessed several names wrong and
-- one guess silently returned ZERO ROWS instead of erroring — the dangerous kind. Verified facts:
--   * prod database is **teas** (the deployed appsettings says accounting_dev; that value is stale)
--   * employees live in **master.employees**, not payroll.employees
--   * payroll_runs has total_gross_taxable / total_gross_non_taxable / total_sso_employee —
--     there is no total_gross or total_sso column
--   * **status values are UPPERCASE** ('ISSUED' / 'SETTLED' / 'POSTED'). A title-case comparison
--     matches nothing and reports a clean bill of health that is simply false. Always use upper().
-- Run it as: sudo -u postgres psql -d teas -f audit-subsatang.sql
--
-- WP-6.1 — legacy sub-satang data audit. READ-ONLY. Run on PROD before deploying R1.
--
-- Why: R1's precision guard (JournalEntry.MarkPosted) rejects any amount with more than
-- 2 decimals. That is correct for NEW data, but on a company that ALREADY holds >2dp values
-- it turns three lifecycle operations into dead-ends with no in-app remedy:
--   * year-end close / reopen  (YearCloseService sums already-posted line amounts)
--   * paying an already-posted payroll run (PayrollRunService posts the stored TotalNet)
--   * the WP-2 non-VAT AR backfill (posts outstanding derived from posted documents)
-- The error tells the user to "restate in satang", which is impossible on immutable history.
--
-- This script only COUNTS and LISTS. It changes nothing. Read its output before R1 ships.
-- Every query is per-company so the blast radius is a number, not a guess.

\echo '=== 1. Posted journal lines with >2dp (the year-close blocker) ==='
SELECT je.company_id,
       count(*)                                   AS bad_lines,
       count(DISTINCT je.journal_id)              AS bad_entries,
       min(je.doc_date)                           AS earliest,
       max(je.doc_date)                           AS latest
FROM gl.journal_lines jl
JOIN gl.journal_entries je ON je.journal_id = jl.journal_id
WHERE round(jl.debit_amount, 2)  <> jl.debit_amount
   OR round(jl.credit_amount, 2) <> jl.credit_amount
GROUP BY je.company_id
ORDER BY je.company_id;

\echo '=== 1b. …the same rows, itemised (feed the remediation design) ==='
SELECT je.company_id, je.journal_id, je.doc_no, je.doc_date, je.description,
       jl.line_no, jl.debit_amount, jl.credit_amount, jl.account_id
FROM gl.journal_lines jl
JOIN gl.journal_entries je ON je.journal_id = jl.journal_id
WHERE round(jl.debit_amount, 2)  <> jl.debit_amount
   OR round(jl.credit_amount, 2) <> jl.credit_amount
ORDER BY je.company_id, je.doc_date, je.journal_id, jl.line_no
LIMIT 500;

\echo '=== 1c. Which ACCOUNT TYPES are affected — revenue/expense means year-close is blocked ==='
SELECT je.company_id, coa.account_type, count(*) AS bad_lines
FROM gl.journal_lines jl
JOIN gl.journal_entries je ON je.journal_id = jl.journal_id
JOIN gl.chart_of_accounts coa ON coa.account_id = jl.account_id
WHERE round(jl.debit_amount, 2)  <> jl.debit_amount
   OR round(jl.credit_amount, 2) <> jl.credit_amount
GROUP BY je.company_id, coa.account_type
ORDER BY je.company_id, coa.account_type;

\echo '=== 2. Posted-but-unpaid payroll runs with >2dp totals (these strand at Pay) ==='
SELECT company_id, payroll_run_id, period_year_month, status,
       total_gross_taxable, total_net, total_pit, total_sso_employee
FROM payroll.payroll_runs
WHERE (round(total_net,   2) <> total_net
    OR round(total_gross_taxable, 2) <> total_gross_taxable
    OR round(total_pit,   2) <> total_pit
    OR round(total_sso_employee, 2) <> total_sso_employee)
ORDER BY company_id, period_year_month;

\echo '=== 3. Employees whose base salary is >2dp (the source of future payroll blockage) ==='
SELECT company_id, employee_id, employee_code, base_salary, is_active
FROM master.employees
WHERE round(base_salary, 2) <> base_salary
ORDER BY company_id, is_active DESC, employee_code;

\echo '=== 4. Document lines from the four proven pollution paths ==='
SELECT 'expense_claim' AS src, company_id, count(*) AS bad_rows
FROM expense.expense_claim_lines l
JOIN expense.expense_claims c ON c.expense_claim_id = l.expense_claim_id
WHERE round(l.amount, 2) <> l.amount
GROUP BY company_id
UNION ALL
SELECT 'payment_voucher', c.company_id, count(*)
FROM purchase.payment_voucher_lines l
JOIN purchase.payment_vouchers c ON c.payment_voucher_id = l.payment_voucher_id
WHERE round(l.amount, 2) <> l.amount
GROUP BY c.company_id
UNION ALL
SELECT 'vendor_invoice', c.company_id, count(*)
FROM purchase.vendor_invoice_lines l
JOIN purchase.vendor_invoices c ON c.vendor_invoice_id = l.vendor_invoice_id
WHERE round(l.line_amount, 2) <> l.line_amount
GROUP BY c.company_id
UNION ALL
SELECT 'tax_invoice', c.company_id, count(*)
FROM sales.tax_invoice_lines l
JOIN sales.tax_invoices c ON c.tax_invoice_id = l.tax_invoice_id
WHERE round(l.line_amount, 2) <> l.line_amount
GROUP BY c.company_id
ORDER BY 1, 2;

\echo '=== 5. Company name lookup, so the numbers above are readable ==='
SELECT company_id, name_th, vat_registered FROM master.companies ORDER BY company_id;

-- Interpreting the result:
--   * Section 1c with revenue/expense rows for a company => that company CANNOT close its
--     fiscal year once R1 ships. This is the finding that gates the deploy.
--   * Section 2 rows => those payroll runs cannot be paid. They need a decision before deploy.
--   * Section 3 rows => fix the master data first; R1 blocks NEW ones but not existing.
--   * All sections empty for the live tenants => R1 is safe to ship as-is, and co5/co7 are
--     handled by the already-planned wipe+reseed.

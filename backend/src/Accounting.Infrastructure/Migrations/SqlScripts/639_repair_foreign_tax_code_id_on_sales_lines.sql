-- fix-r2-u2 (L6-4, specs/fix-r2-u2-billing-tax-integrity.md §3.3) — repairs a foreign
-- tax_code_id stranded on sales-chain line rows written before the F13/N1 SalesLineBackstop
-- ladder existed. QuotationChainServices/SalesOrderDeliveryServices/BillingNoteService's
-- copy-forward builders inherit a source line's (tax_code_id, tax_code) pair VERBATIM (no
-- re-resolution) — a line written by pre-F13 code can carry ANOTHER COMPANY'S tax_code_id.
-- Live proof (accounting_dev, 2026-08-19): co3's whole QT 2 → SO 4 → DO 4 → BN 3 chain (8 rows,
-- 2 lines each) stores tax_code_id=1, which is co1's VAT7 row — behind the string 'VAT0'. All 8
-- rows have tax_rate=0/tax_amount=0 (money is unharmed; only referential identity is wrong).
--
-- Repair rule — for every line where tax_code_id <> 0 AND no row exists in tax.tax_codes with
-- that tax_code_id for the line's own document's company:
--   (a) if the company's own master holds a code equal, case-insensitively, to the line's
--       stored tax_code string → set tax_code_id to that master row's id (lowest id if two
--       case-variants collide — the unique index is case-SENSITIVE, see SalesLineBackstop.cs
--       "OrderBy(TaxCodeId) BEFORE GroupBy" comment);
--   (b) otherwise → set tax_code_id = 0 (SalesLineBackstop.SYNTHETIC_TAX_CODE_ID).
-- This is the SAME rule SalesLineBackstop.SanitizeInheritedTaxCode applies at the app layer for
-- every NEW copy going forward — this script is the one-time backfill for rows that predate it.
--
-- This script writes tax_code_id and NOTHING ELSE. Not tax_code, not tax_rate, not any amount,
-- not a header total, not updated_at — every other column stays byte-identical (I1).
--
-- Class B (id valid for the OWN company, but the string disagrees with the master — 2 rows,
-- both sales.tax_invoice_lines, co1, 'V7' vs master 'VAT7') is DELIBERATELY NOT repaired here:
-- the id is already correct there, the string is a document SNAPSHOT (rewriting it changes what
-- a reprinted document shows — a business decision, not a startup migration's to make), and
-- sales.tax_invoice_lines carries the posted-line immutability trigger (582) which would abort
-- this script's transaction and boot-loop the API on any UPDATE against a non-DRAFT parent.
-- Class B is reported by a separate deploy probe (P4 in the spec's §7), never written.
--
-- Excluded tables and why: sales.tax_invoice_lines, sales.receipt_lines,
-- purchase.vendor_invoice_lines, gl.journal_lines all carry BEFORE UPDATE immutability triggers
-- (SqlScripts/570/580/582) — an UPDATE there on a posted/settled parent raises check_violation
-- and aborts the whole startup transaction. purchase.purchase_order_lines and
-- purchase.payment_voucher_lines are out of this unit's scope (0 violating rows today; a
-- separate prevention-only finding is filed for Fable).
--
-- RUNTIME SECURITY CONTEXT (mandatory to get right — troubles-wiki.md "Startup SqlScript
-- writing/reading G1/G3 RLS'd tables fails 42501 or silently no-ops on prod (green on
-- teas_test)"): this script runs at API startup, DbInitializer.ApplyScriptsAsync, one
-- transaction per script, BEFORE TenantMiddleware ever runs — app.company_id is UNSET, no
-- session GUCs at all. Prod's app role (teas) is NOBYPASSRLS; dev's role (accounting) has
-- rolbypassrls=t; teas_test connects as a superuser — both dev and test SILENTLY BYPASS RLS,
-- which is why this class of bug is invisible locally (memory "rls-masked-by-superuser-tests").
-- sales.quotations / sales.sales_orders / sales.delivery_orders / sales.billing_notes and
-- tax.tax_codes are G1
-- (600_superadmin_scoped_rls.sql): USING (company_id = app.company_id GUC), NO bypass arm — so
-- without the per-company set_config loop below, every read of those tables returns ZERO rows
-- under the prod role and this script "succeeds" having repaired nothing. master.companies
-- carries no RLS (tenant root) — it is readable unfiltered and drives the loop. The 4 UPDATE
-- targets (sales.*_lines) carry NO RLS at all — the write itself is never policy-filtered; only
-- the header/master JOIN needs the GUC pinned. No INSERT (no 23505), no DDL, no new
-- FK/constraint (no 23503) — this script cannot boot-loop the API.
--
-- The m.company_id = h.company_id / t.company_id = h.company_id predicates below are
-- LOAD-BEARING, not decoration. Under prod RLS they are redundant (the policy already scopes
-- tax.tax_codes to the pinned company); under dev/test BYPASSRLS they are the ONLY thing that
-- scopes the join — drop them and the script becomes a no-op in dev (id 1 "exists" globally, so
-- co3's rows look clean) while behaving correctly in prod: the exact inverted-masking trap this
-- unit exists to avoid. Keep them.
--
-- Idempotent: a second run finds no row matching the NOT EXISTS predicate (every repaired row
-- now holds either 0 or a real own-company id) and updates nothing. Company-agnostic and a
-- no-op on any database with no violating rows (prod included) — SYSTEM script, not added to
-- DbInitializer.DemoScripts.
--
-- Verify (the data, not the exit code) — must be empty after this script runs:
--   WITH t AS (
--    SELECT 'quotation_lines' tbl, q.company_id co, l.tax_code_id tcid FROM sales.quotation_lines l JOIN sales.quotations q ON q.quotation_id=l.quotation_id
--    UNION ALL SELECT 'sales_order_lines', s.company_id, l.tax_code_id FROM sales.sales_order_lines l JOIN sales.sales_orders s ON s.sales_order_id=l.sales_order_id
--    UNION ALL SELECT 'delivery_order_lines', d.company_id, l.tax_code_id FROM sales.delivery_order_lines l JOIN sales.delivery_orders d ON d.delivery_order_id=l.delivery_order_id
--    UNION ALL SELECT 'billing_note_lines', b.company_id, l.tax_code_id FROM sales.billing_note_lines l JOIN sales.billing_notes b ON b.billing_note_id=l.billing_note_id)
--   SELECT tbl, count(*) FROM t
--   WHERE tcid <> 0 AND NOT EXISTS (SELECT 1 FROM tax.tax_codes tc WHERE tc.tax_code_id=t.tcid AND tc.company_id=t.co)
--   GROUP BY 1;
--
-- Cost on teas_test (~629 companies, memory "teas-test-fixture-apply-once"): 629 companies × 4
-- statements. PostgresFixture already sets CommandTimeout(300); the RLS test for this script
-- (SalesLineTaxCodeRepairRlsTests.cs) sets CommandTimeout=300 on its own command too.
--
-- NB: NEVER put curly braces anywhere in this file (not even in a comment) — DbInitializer runs
-- it through ExecuteSqlRawAsync, which treats brace characters as string.Format placeholders and
-- fails at boot.

SET LOCAL app.company_id = '';

DO $do$
DECLARE c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies ORDER BY company_id LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        UPDATE sales.quotation_lines l
        SET tax_code_id = COALESCE(
                (SELECT m.tax_code_id FROM tax.tax_codes m
                  WHERE m.company_id = h.company_id
                    AND lower(m.code) = lower(l.tax_code)
                  ORDER BY m.tax_code_id LIMIT 1), 0)
        FROM sales.quotations h
        WHERE h.quotation_id = l.quotation_id
          AND h.company_id = c.company_id
          AND l.tax_code_id <> 0
          AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                           WHERE t.tax_code_id = l.tax_code_id
                             AND t.company_id = h.company_id);

        UPDATE sales.sales_order_lines l
        SET tax_code_id = COALESCE(
                (SELECT m.tax_code_id FROM tax.tax_codes m
                  WHERE m.company_id = h.company_id
                    AND lower(m.code) = lower(l.tax_code)
                  ORDER BY m.tax_code_id LIMIT 1), 0)
        FROM sales.sales_orders h
        WHERE h.sales_order_id = l.sales_order_id
          AND h.company_id = c.company_id
          AND l.tax_code_id <> 0
          AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                           WHERE t.tax_code_id = l.tax_code_id
                             AND t.company_id = h.company_id);

        UPDATE sales.delivery_order_lines l
        SET tax_code_id = COALESCE(
                (SELECT m.tax_code_id FROM tax.tax_codes m
                  WHERE m.company_id = h.company_id
                    AND lower(m.code) = lower(l.tax_code)
                  ORDER BY m.tax_code_id LIMIT 1), 0)
        FROM sales.delivery_orders h
        WHERE h.delivery_order_id = l.delivery_order_id
          AND h.company_id = c.company_id
          AND l.tax_code_id <> 0
          AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                           WHERE t.tax_code_id = l.tax_code_id
                             AND t.company_id = h.company_id);

        UPDATE sales.billing_note_lines l
        SET tax_code_id = COALESCE(
                (SELECT m.tax_code_id FROM tax.tax_codes m
                  WHERE m.company_id = h.company_id
                    AND lower(m.code) = lower(l.tax_code)
                  ORDER BY m.tax_code_id LIMIT 1), 0)
        FROM sales.billing_notes h
        WHERE h.billing_note_id = l.billing_note_id
          AND h.company_id = c.company_id
          AND l.tax_code_id <> 0
          AND NOT EXISTS (SELECT 1 FROM tax.tax_codes t
                           WHERE t.tax_code_id = l.tax_code_id
                             AND t.company_id = h.company_id);
    END LOOP;
    PERFORM set_config('app.company_id', '', true);
END
$do$;

-- 600_superadmin_scoped_rls.sql
-- Bugfix 2026-07-08: super admin saw the UNION of all companies. Every company_isolation
-- policy carried `OR app.is_super_admin`, and TenantMiddleware pinned that GUC from the
-- logged-in user's flag. This recreates EVERY company_isolation policy so data scope is
-- driven SOLELY by app.company_id. Legitimate cross-tenant service/admin paths use the
-- NEW, LOCAL-only `app.bypass_rls` GUC (never set from a user identity, never by
-- TenantMiddleware). Only the tables those paths touch keep an `OR app.bypass_rls` arm.
-- Idempotent (DROP POLICY IF EXISTS + ENABLE/FORCE re-runnable). DDL only. MUST sort last.
-- Recreates ONLY the company_isolation POLICY — immutability triggers in 040/060/570/571
-- are untouched. Applied once by DbInitializer.ApplyScriptsAsync.

-- G1 — tenant-data tables: scope strictly to the pinned company. NO bypass arm.
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY[
    'master.chart_of_accounts','master.customers','master.vendors','master.employees',
    'master.products','master.business_units','sys.expense_categories','sys.number_sequences',
    'sys.idempotency_keys','sys.attachments','tax.tax_codes','tax.wht_types',
    'tax.wht_certificates','tax.tax_filings','tax.cit_year_summaries','tax.cit_adjustments',
    'gl.journal_entries','gl.accounting_periods','sales.tax_invoices','sales.receipts',
    'sales.tax_adjustment_notes','sales.billing_notes','sales.billing_note_tax_invoices',
    'sales.quotations','sales.sales_orders','sales.delivery_orders','purchase.vendor_invoices',
    'purchase.payment_vouchers','purchase.purchase_orders','payroll.payroll_runs','payroll.payslips'
];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                );
        $pol$, tbl);
    END LOOP;
END $$;

-- G2 — service-scanner tables: pinned company OR the explicit LOCAL service bypass.
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY['sys.api_keys','etax.submissions','master.branches'];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.bypass_rls', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END $$;

-- G3 — system-global tables: NULL company = global row (visible to all), else pinned,
-- OR the LOCAL service bypass (RBAC cross-company mgmt / cross-company audit writes).
DO $$
DECLARE
    tbl text;
    tables text[] := ARRAY['sys.roles','sys.role_permissions','audit.activity_log'];
BEGIN
    FOREACH tbl IN ARRAY tables LOOP
        EXECUTE format('ALTER TABLE %s ENABLE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('ALTER TABLE %s FORCE ROW LEVEL SECURITY;', tbl);
        EXECUTE format('DROP POLICY IF EXISTS company_isolation ON %s;', tbl);
        EXECUTE format($pol$
            CREATE POLICY company_isolation ON %s
                USING (
                    company_id IS NULL
                    OR company_id = NULLIF(current_setting('app.company_id', true), '')::INT
                    OR COALESCE(NULLIF(current_setting('app.bypass_rls', true), '')::BOOLEAN, FALSE)
                );
        $pol$, tbl);
    END LOOP;
END $$;

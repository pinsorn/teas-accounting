-- specs/fix-swarm-findings-all.md WP2 + WP4 (swarm-findings/audit01.md HIGH #3 + MED cit;
-- swarm-findings/appr01.md HIGH "ต้องทำ/แจ้งเตือน" widget) — AUDITOR read-visibility gap +
-- APPROVER dashboard-widget 403.
--
-- WP2 root cause: AUDITOR only held the AR/sales-document read subset (18 perms, all read-only).
-- ~10 modules + 3 reports + CIT preview all 403 at the API but render a normal "ไม่มีข้อมูล" empty
-- state — indistinguishable from a genuinely empty company (audit01.md HIGH #3; co5 has real POs the
-- auditor can't see). Fix: grant AUDITOR every EXISTING read-only permission code that covers those
-- modules. READ ONLY — no create/post/approve/finalize granted, verified per-code against the actual
-- endpoint RequireAuthorization call (not the finding's prose, which used placeholder names that do
-- not all match the real catalog).
--
-- purchase.purchase_order.read also covers /reports/outstanding-po and /reports/ap-aging
-- (PurchaseOrderEndpoints.cs gates both report routes on the SAME `read` policy as PO list/detail —
-- no separate report.* code exists). tax.filing.preview also unlocks the CIT worksheet
-- (CitEndpoints.cs gates years/profile/adjustments-list on this one shared code, same as every other
-- filing type — there is no CIT-only permission). bank.report.read is the reconciliation REPORT
-- only; bank.reconcile (suggestions/confirm/unmatch/journal) stays ungranted — that gate is
-- write-capable, not pure read.
--
-- NOT granted here (documented, not silently dropped — the finding's exact code names don't exist):
--   sales.quotation.read / sales.sales_order.read / sales.delivery_order.read — SalesChainEndpoints.cs
--     gates BOTH read (list/get) and every write/lifecycle action (send/accept/reject/cancel/convert/
--     issue/mark-delivered/create-ti/create-invoice) on ONE combined code (*.manage per doc type). No
--     separate read code exists; granting *.manage to AUDITOR would hand it write/lifecycle access,
--     violating the READ-ONLY requirement. Needs its own follow-up spec (split into .read/.manage per
--     route, mirroring the Customer/BankAccount/ExpenseCat read+manage split already in this
--     codebase) — a bigger, regression-risked diff (must re-grant .read to every existing .manage
--     holder across 3 endpoint groups / ~15 routes so their EXISTING access doesn't break) than a
--     grant-only seed script should attempt.
--   master.vendor.read — VendorEndpoints.cs (MasterEndpoints.cs) gates list/get AND create/update on
--     ONE combined master.vendor.manage. Same reasoning as above; same follow-up needed.
--   master.business_unit.read — BusinessUnitEndpoints.cs gates list/get AND create/update/deactivate
--     on ONE combined master.business_unit.manage. Same reasoning. This is also the ~25-console-403
--     BU gap the finding calls out, and it affects nearly every read-heavy role (AR_CLERK, AP_CLERK,
--     SALES_STAFF, PURCHASING_STAFF, WAREHOUSE_STAFF, TAX_OFFICER, APPROVER — none hold
--     BusinessUnitManage today), not just AUDITOR — flagged as the highest-value follow-up.
--
-- WP4 root cause: GET /reports/pending-agent-approvals (dashboard "ต้องทำ" widget) is gated on
-- sales.tax_invoice.read (ReportEndpoints.cs) — APPROVER never held it (appr01.md HIGH), so the
-- widget silently 403s every load and shows a false "all clear". COMPANY_ADMIN/CHIEF_ACCOUNTANT (the
-- other PO/PV-approving roles) already hold sales.tax_invoice.read via
-- 320_seed_chapter3_rbac.sql — only APPROVER needs the top-up.
-- NOTE for the widget's own design (not a grant gap, no code change here): the endpoint counts ONLY
-- drafts with CreatedViaApiKeyName != null (doc comment: "count of DRAFT documents created via API
-- key (MCP agent) awaiting human approval" — M4a), and the FE card is explicitly agent-scoped (Bot
-- icon, agentType-typed i18n copy in app/(dashboard)/page.tsx). It is NOT a general human-approval
-- inbox — appr01's PO/PV drafts (created by other swarm agents through the normal browser UI, not an
-- API key) were never going to be counted by this widget even with the grant fixed; that is the
-- widget behaving exactly as designed, not a second bug. The separate "no approval inbox exists"
-- finding is real UX but explicitly out of scope here (Ponytail — no new inbox page); this script
-- only fixes the false-403-as-"all clear" for genuinely agent-created drafts, which is the achievable
-- grant-only fix.
--
-- Both idempotent code-first inserts below are defensive (memory: rbac-seed-ordering-footgun) — every
-- code granted here already exists in sys.permissions from its owning feature's seed script;
-- re-inserted anyway per the sanctioned habit, ON CONFLICT DO NOTHING.
--
-- MECHANISM + RLS: identical to 627 (see that file's header) — top up sys.role_permission_templates,
-- then re-sync per company under a transaction-local set_config('app.company_id', ...) loop, under
-- the NOBYPASSRLS app role, no superuser assumption.
--
-- Idempotent; runs once (tracked in sys.applied_sql_scripts). Numbered 628 -> after 627.
-- NB: NEVER put curly braces in this file (EF ExecuteSqlRawAsync treats them as string.Format
-- placeholders and fails at boot).

-- 1. Code-first (defensive; every code already exists from its owning feature's seed). Descriptions
--    match each code's original seed row verbatim. ON CONFLICT DO NOTHING — sys.permissions carries
--    no company_id / RLS, safe without any GUC.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('purchase.purchase_order.read',  'purchase',   'purchase_order',  'read',    'View / list / PDF PO'),
    ('purchase.vendor_invoice.read',  'purchase',   'vendor_invoice',  'read',    'View Vendor Invoice'),
    ('purchase.payment_voucher.read', 'purchase',   'payment_voucher', 'read',    'View Payment Voucher'),
    ('expense.claim.read',            'expense',    'claim',           'read',    'View expense claims'),
    ('bank.account.read',             'bank',       'account',         'read',    'View bank accounts'),
    ('fixedasset.read',               'fixedasset', 'asset',           'read',    'View fixed assets / depreciation runs'),
    ('bank.report.read',              'bank',       'report',          'read',    'View the bank reconciliation report'),
    ('tax.filing.preview',            'tax',        'filing',          'preview', 'Preview a tax filing (ภ.พ.30 / ภ.ง.ด.)'),
    ('sales.tax_invoice.read',        'sales',      'tax_invoice',     'read',    'View Tax Invoice')
ON CONFLICT (permission_code) DO NOTHING;

-- 2. Top up the copy template. sys.role_permission_templates carries no company_id / RLS either —
--    safe without any GUC.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, v.permission_code
FROM (VALUES
    ('AUDITOR', 'purchase.purchase_order.read'),
    ('AUDITOR', 'purchase.vendor_invoice.read'),
    ('AUDITOR', 'purchase.payment_voucher.read'),
    ('AUDITOR', 'expense.claim.read'),
    ('AUDITOR', 'bank.account.read'),
    ('AUDITOR', 'fixedasset.read'),
    ('AUDITOR', 'bank.report.read'),
    ('AUDITOR', 'tax.filing.preview'),
    ('APPROVER', 'sales.tax_invoice.read')
) AS v(role_code, permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 3. Re-sync the full template to every existing per-company AUDITOR/APPROVER role (idempotent),
--    looped per company under FORCE RLS. Mirrors 627's Phase-D2-style resync — heals any company
--    whose role already exists but is missing the newly-templated grants.
DO $$
DECLARE
    c RECORD;
BEGIN
    FOR c IN SELECT company_id FROM master.companies LOOP
        PERFORM set_config('app.company_id', c.company_id::text, true);

        INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
        SELECT r.role_id, p.permission_id, r.company_id
        FROM sys.role_permission_templates t
        JOIN sys.roles r       ON r.role_code = t.role_code AND r.company_id = c.company_id
        JOIN sys.permissions p ON p.permission_code = t.permission_code
        WHERE t.role_code IN ('AUDITOR', 'APPROVER')
          AND NOT EXISTS (
            SELECT 1 FROM sys.role_permissions rp
            WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
          );
    END LOOP;

    PERFORM set_config('app.company_id', '', true);
END $$;

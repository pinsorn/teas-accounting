-- Sprint 13k Plan 2 — RBAC grant reconcile (Cartesian audit Phase D).
--
-- WHY: the role×permission Cartesian audit found a large block of operational permissions that
-- were enforced on endpoints but granted to NO non-super role, making several seeded roles unable
-- to perform their documented job:
--   • ROOT CAUSE 1 (seed-ordering bug): 320/330 INTENDED to grant the 6 sales receipt/CN/DN
--     create+post pairs to ACCOUNTANT/AR_CLERK/CHIEF_ACCOUNTANT/COMPANY_ADMIN, but those grant
--     statements ran BEFORE 520 inserted the permission codes into sys.permissions, so the
--     `JOIN sys.permissions` matched zero rows and the grants silently no-op'd. 520 then inserted
--     the codes granting them to SUPER_ADMIN only — which got rationalised post-hoc as a
--     "default-unassigned, grant via UI" policy. It was a bug, not a policy.
--   • ROOT CAUSE 2 (never granted): tax-register/return reads, financial-report reads, audit reads,
--     WHT-cert reads, period-close, manual journals, tax-invoice issuance, vendor master, and PO
--     creation were never granted to any non-super role by any seed — e.g. TAX_OFFICER ("External
--     สรรพากร audit role") could read zero tax reports; AR_CLERK ("Sales documents, customer
--     billing") could not issue a tax invoice; PURCHASING_STAFF ("PR, PO") could not create a PO.
--
-- Ham authorised fixing the illogical grants (2026-06-14: "แก้ได้เลย ถ้าอันไหนไม่สมเหตุสมผล").
-- Every grant below is justified by the role's seeded description (110). The reconciled state is
-- recorded in docs/rbac/role-permission-matrix.md for Ham's review.
--
-- ⚠️ ONE LINE ITEM NEEDS HAM'S EXPLICIT NOD (privilege escalation) — see section D:
--   granting sys.role.manage + sys.user.manage to COMPANY_ADMIN so the per-company admin UI
--   (Plan 1) is usable by company admins. Bounded by ResolveTargetCompany (own company only) and
--   by SUPER_ADMIN being non-assignable. If Ham prefers company admins to instead be assigned the
--   global SUPER_ADMIN role, delete section D and re-run.
--
-- DELIBERATELY LEFT SUPER-ADMIN-ONLY: master.company.manage — §4.6 (company tax config is
--   super-admin-only). It is shared by /companies (tax config) AND /company-profile/* (soft info),
--   so company-admins currently cannot edit their own profile without breaching §4.6. That shared
--   permission is itself a finding flagged to Ham (profile-edit needs its own permission).
--
-- MECHANISM (per-company world, post-510): role_permissions carries company_id and new companies
-- clone grants from sys.role_permission_templates via sys.seed_company_roles — NOT from
-- role_permissions. So this script (1) tops up the TEMPLATE, then (2) re-syncs the full template to
-- every existing per-company role (idempotent, NOT EXISTS — mirrors seed_company_roles), which both
-- applies the new grants and heals any company that drifted. Future companies inherit via the
-- updated template.
--
-- Idempotent; runs once (tracked in sys.applied_sql_scripts). Numbered 530 → after 520 so every
-- permission code already exists. NB: NEVER put curly braces in this file (EF ExecuteSqlRawAsync
-- treats them as string.Format placeholders and fails at boot).

-- 1. Top up the copy template with the intended (role_code, permission_code) grants.
INSERT INTO sys.role_permission_templates (role_code, permission_code)
SELECT v.role_code, v.permission_code
FROM (VALUES
    -- A. Sales receipt / credit note / debit note create+post — restore 320/330 intent
    --    (no-op'd by the seed-ordering bug above).
    ('ACCOUNTANT','sales.receipt.create'),       ('ACCOUNTANT','sales.receipt.post'),
    ('AR_CLERK','sales.receipt.create'),         ('AR_CLERK','sales.receipt.post'),
    ('CHIEF_ACCOUNTANT','sales.receipt.create'), ('CHIEF_ACCOUNTANT','sales.receipt.post'),
    ('COMPANY_ADMIN','sales.receipt.create'),    ('COMPANY_ADMIN','sales.receipt.post'),
    ('ACCOUNTANT','sales.credit_note.create'),       ('ACCOUNTANT','sales.credit_note.post'),
    ('AR_CLERK','sales.credit_note.create'),         ('AR_CLERK','sales.credit_note.post'),
    ('CHIEF_ACCOUNTANT','sales.credit_note.create'), ('CHIEF_ACCOUNTANT','sales.credit_note.post'),
    ('COMPANY_ADMIN','sales.credit_note.create'),    ('COMPANY_ADMIN','sales.credit_note.post'),
    ('ACCOUNTANT','sales.debit_note.create'),       ('ACCOUNTANT','sales.debit_note.post'),
    ('AR_CLERK','sales.debit_note.create'),         ('AR_CLERK','sales.debit_note.post'),
    ('CHIEF_ACCOUNTANT','sales.debit_note.create'), ('CHIEF_ACCOUNTANT','sales.debit_note.post'),
    ('COMPANY_ADMIN','sales.debit_note.create'),    ('COMPANY_ADMIN','sales.debit_note.post'),

    -- B1. Tax Invoice issuance (ม.86/4 core sales) — AR_CLERK "Sales documents, customer billing".
    ('AR_CLERK','sales.tax_invoice.create'),         ('AR_CLERK','sales.tax_invoice.post'),
    ('ACCOUNTANT','sales.tax_invoice.create'),       ('ACCOUNTANT','sales.tax_invoice.post'),
    ('CHIEF_ACCOUNTANT','sales.tax_invoice.create'), ('CHIEF_ACCOUNTANT','sales.tax_invoice.post'),
    ('COMPANY_ADMIN','sales.tax_invoice.create'),    ('COMPANY_ADMIN','sales.tax_invoice.post'),

    -- B2. VAT register + ภ.พ.30/ภ.ง.ด.3/ภ.ง.ด.53 reads — TAX_OFFICER "External สรรพากร audit",
    --     AUDITOR "Read-only audit", plus the accounting tier.
    ('TAX_OFFICER','tax.vat_register.read'), ('TAX_OFFICER','tax.pnd30.read'),
    ('TAX_OFFICER','tax.pnd3.read'),         ('TAX_OFFICER','tax.pnd53.read'),
    ('AUDITOR','tax.vat_register.read'),     ('AUDITOR','tax.pnd30.read'),
    ('AUDITOR','tax.pnd3.read'),             ('AUDITOR','tax.pnd53.read'),
    ('ACCOUNTANT','tax.vat_register.read'),  ('ACCOUNTANT','tax.pnd30.read'),
    ('ACCOUNTANT','tax.pnd3.read'),          ('ACCOUNTANT','tax.pnd53.read'),
    ('CHIEF_ACCOUNTANT','tax.vat_register.read'), ('CHIEF_ACCOUNTANT','tax.pnd30.read'),
    ('CHIEF_ACCOUNTANT','tax.pnd3.read'),         ('CHIEF_ACCOUNTANT','tax.pnd53.read'),
    ('COMPANY_ADMIN','tax.vat_register.read'),    ('COMPANY_ADMIN','tax.pnd30.read'),
    ('COMPANY_ADMIN','tax.pnd3.read'),            ('COMPANY_ADMIN','tax.pnd53.read'),

    -- B3. Trial balance + P&L reads (financial statements).
    ('TAX_OFFICER','report.trial_balance.read'),      ('TAX_OFFICER','report.profit_loss.read'),
    ('AUDITOR','report.trial_balance.read'),          ('AUDITOR','report.profit_loss.read'),
    ('ACCOUNTANT','report.trial_balance.read'),       ('ACCOUNTANT','report.profit_loss.read'),
    ('CHIEF_ACCOUNTANT','report.trial_balance.read'), ('CHIEF_ACCOUNTANT','report.profit_loss.read'),
    ('COMPANY_ADMIN','report.trial_balance.read'),    ('COMPANY_ADMIN','report.profit_loss.read'),

    -- B4. Audit trail + number-gap reads (per-document /activity, /reports/number-gaps) —
    --     AUDITOR core; chief/tax/admin oversight.
    ('AUDITOR','report.audit.read'),     ('TAX_OFFICER','report.audit.read'),
    ('CHIEF_ACCOUNTANT','report.audit.read'), ('COMPANY_ADMIN','report.audit.read'),

    -- B5. WHT certificate reads (50 ทวิ) — AP_CLERK "Vendor invoices, payments", plus tax/audit/acct.
    ('AP_CLERK','purchase.wht.read'),         ('ACCOUNTANT','purchase.wht.read'),
    ('CHIEF_ACCOUNTANT','purchase.wht.read'), ('COMPANY_ADMIN','purchase.wht.read'),
    ('TAX_OFFICER','purchase.wht.read'),      ('AUDITOR','purchase.wht.read'),

    -- B6. Period close — CHIEF_ACCOUNTANT description literally: "approves journals, closes period".
    ('CHIEF_ACCOUNTANT','gl.period.close'), ('COMPANY_ADMIN','gl.period.close'),

    -- B7. Vendor master — AP_CLERK "Vendor invoices", PURCHASING_STAFF "PR, PO".
    ('AP_CLERK','master.vendor.manage'),         ('PURCHASING_STAFF','master.vendor.manage'),
    ('CHIEF_ACCOUNTANT','master.vendor.manage'), ('COMPANY_ADMIN','master.vendor.manage'),

    -- B8. Manual journals — ACCOUNTANT "Day-to-day bookkeeping" creates; CHIEF "approves journals"
    --     posts (maker-checker SoD). Read for the accounting/audit tier.
    ('ACCOUNTANT','gl.journal.create'),       ('CHIEF_ACCOUNTANT','gl.journal.create'),
    ('COMPANY_ADMIN','gl.journal.create'),
    ('CHIEF_ACCOUNTANT','gl.journal.post'),   ('COMPANY_ADMIN','gl.journal.post'),
    ('ACCOUNTANT','gl.journal.read'),         ('CHIEF_ACCOUNTANT','gl.journal.read'),
    ('AUDITOR','gl.journal.read'),            ('TAX_OFFICER','gl.journal.read'),
    ('COMPANY_ADMIN','gl.journal.read'),

    -- B9. Purchase orders — PURCHASING_STAFF "PR, PO" (create + read; approve stays with APPROVER).
    ('PURCHASING_STAFF','purchase.purchase_order.create'),
    ('PURCHASING_STAFF','purchase.purchase_order.read'),

    -- B10. APPROVER reviews the PV before approving it.
    ('APPROVER','purchase.payment_voucher.read'),

    -- C. Company-admin configuration (per-company admin scope) + accounting config for the chief.
    ('COMPANY_ADMIN','master.coa.manage'),          ('COMPANY_ADMIN','master.branch.manage'),
    ('COMPANY_ADMIN','sys.doc_prefix.manage'),      ('COMPANY_ADMIN','sys.expense_category.manage'),
    ('CHIEF_ACCOUNTANT','master.coa.manage'),       ('CHIEF_ACCOUNTANT','sys.expense_category.manage'),

    -- D. ⚠️ RBAC self-administration (needs Ham's explicit confirmation — see header).
    ('COMPANY_ADMIN','sys.role.manage'), ('COMPANY_ADMIN','sys.user.manage')
) AS v(role_code, permission_code)
ON CONFLICT (role_code, permission_code) DO NOTHING;

-- 2. Re-sync the full template to every existing per-company role (idempotent). Mirrors
--    sys.seed_company_roles' grant step: clone (role_code, permission_code) template rows into
--    sys.role_permissions for each company's matching role, carrying that role's company_id, only
--    where the grant is missing. Heals the 14 silently-missing grants on all existing companies.
INSERT INTO sys.role_permissions (role_id, permission_id, company_id)
SELECT r.role_id, p.permission_id, r.company_id
FROM sys.role_permission_templates t
JOIN sys.roles r       ON r.role_code = t.role_code AND r.company_id IS NOT NULL
JOIN sys.permissions p ON p.permission_code = t.permission_code
WHERE NOT EXISTS (
    SELECT 1 FROM sys.role_permissions rp
    WHERE rp.role_id = r.role_id AND rp.permission_id = p.permission_id
);

-- Sprint 5.5 seed: VI document prefix, new Purchase permissions, B2 backfill.
-- Idempotent (ON CONFLICT). New script (110/100 already applied & tracked).

-- VI prefix (number-gap audit completeness — VI is a fiscal, expense doc).
INSERT INTO sys.document_prefixes
    (prefix_code, document_type,    description_th,         description_en,   requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('VI',       'VENDOR_INVOICE',  'บันทึกใบกำกับภาษีซื้อ',  'Vendor Invoice', FALSE, TRUE, TRUE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;

-- New permissions — keep in sync with Accounting.Api.Authorization.Permissions.Purchase.
INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('purchase.vendor_invoice.create',  'purchase', 'vendor_invoice',  'create',  'Create Vendor Invoice'),
    ('purchase.vendor_invoice.post',    'purchase', 'vendor_invoice',  'post',    'Post Vendor Invoice'),
    ('purchase.vendor_invoice.read',    'purchase', 'vendor_invoice',  'read',    'View Vendor Invoice'),
    ('purchase.payment_voucher.approve','purchase', 'payment_voucher', 'approve', 'Approve Payment Voucher (B2 SoD)')
ON CONFLICT (permission_code) DO NOTHING;

-- Grants: SUPER_ADMIN gets everything; admins/accountants get the AP set; approve to
-- approver-type roles (Answer-Sana-Question-Backend5 §B2).
INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'purchase.vendor_invoice.create','purchase.vendor_invoice.post',
    'purchase.vendor_invoice.read','purchase.payment_voucher.approve')
WHERE r.role_code = 'SUPER_ADMIN'
ON CONFLICT DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'purchase.vendor_invoice.create','purchase.vendor_invoice.post','purchase.vendor_invoice.read')
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','ACCOUNTANT','AP_CLERK')
ON CONFLICT DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'purchase.payment_voucher.approve'
WHERE r.role_code IN ('COMPANY_ADMIN','CHIEF_ACCOUNTANT','APPROVER')
ON CONFLICT DO NOTHING;

-- B2 one-time backfill: existing POSTED PVs predate the approval state. Treat poster as
-- approver so the SoD CHECK + history are consistent. §6: SKIP rows with NULL posted_by
-- (defensive — shouldn't happen) and rows that would violate ck_pv_sod (approver=creator;
-- legacy single-user posts — leave approved_by NULL, the CHECK permits NULL).
UPDATE purchase.payment_vouchers
SET approved_by = posted_by,
    approved_at = posted_at
WHERE status = 'POSTED'
  AND approved_by IS NULL
  AND posted_by IS NOT NULL
  AND (created_by IS NULL OR created_by <> posted_by);

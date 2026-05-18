-- Sprint 12 — internal PO: doc prefix + permissions. Additive + idempotent.
-- Keep in sync with Accounting.Api.Authorization.Permissions.Purchase.
-- The 'PO' prefix was NOT pre-seeded in 100 (unlike QT/SO/DO) — added here.
-- PURCHASING_STAFF role is not in the seeded role set → AP_CLERK is the
-- purchasing analog (documented mechanism note → Report-Backend17).

INSERT INTO sys.document_prefixes
    (prefix_code, document_type,   description_th,  description_en,
     requires_etax, is_fiscal_doc, is_expense, is_active, created_at)
VALUES
    ('PO', 'PURCHASE_ORDER', 'ใบสั่งซื้อ', 'Purchase Order',
     FALSE, FALSE, TRUE, TRUE, NOW())
ON CONFLICT (prefix_code) DO NOTHING;

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('purchase.purchase_order.create',  'purchase', 'purchase_order', 'create',  'Create + edit Draft PO + cancel own'),
    ('purchase.purchase_order.approve', 'purchase', 'purchase_order', 'approve', 'Approve PO (SoD — cannot approve own)'),
    ('purchase.purchase_order.read',    'purchase', 'purchase_order', 'read',    'View / list / PDF PO'),
    ('purchase.purchase_order.cancel',  'purchase', 'purchase_order', 'cancel',  'Cancel an Approved PO')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON (
        (p.permission_code = 'purchase.purchase_order.read')   -- all working roles
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT',
                                'ACCOUNTANT','AP_CLERK','AR_CLERK','APPROVER')
     OR (p.permission_code = 'purchase.purchase_order.create'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','ACCOUNTANT','AP_CLERK'))
     OR (p.permission_code = 'purchase.purchase_order.approve'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','APPROVER'))
     OR (p.permission_code = 'purchase.purchase_order.cancel'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT')))
ON CONFLICT DO NOTHING;

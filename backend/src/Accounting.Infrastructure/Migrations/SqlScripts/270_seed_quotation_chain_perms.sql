-- Sprint 10 Part B — Q/SO/DO chain permissions. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Sales. Additive + idempotent.
-- Granted to the sales-document roles + admins (SALES_STAFF = "Quotation,
-- Sales Order" per role seed; AR_CLERK = sales docs / billing).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sales.quotation.manage',      'sales', 'quotation',      'manage', 'Quotation CRUD + lifecycle'),
    ('sales.sales_order.manage',    'sales', 'sales_order',    'manage', 'Sales Order CRUD + post + delivery'),
    ('sales.delivery_order.manage', 'sales', 'delivery_order', 'manage', 'Delivery Order CRUD + post + TI')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code IN (
    'sales.quotation.manage','sales.sales_order.manage','sales.delivery_order.manage')
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT','AR_CLERK','SALES_STAFF')
ON CONFLICT DO NOTHING;

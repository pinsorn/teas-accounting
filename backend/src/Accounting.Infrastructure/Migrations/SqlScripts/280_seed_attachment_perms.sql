-- Sprint 11 — attachment permissions. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Sys. Additive + idempotent.
-- read → all roles (broad, like product.read; tenant isolation + parent .read
-- inheritance still enforced app-side). upload → all doc-working roles.
-- delete → admins (others soft-delete their OWN uploads via the service check).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sys.attachment.upload', 'sys', 'attachment', 'upload', 'Upload file attachments'),
    ('sys.attachment.read',   'sys', 'attachment', 'read',   'List + download attachments'),
    ('sys.attachment.delete', 'sys', 'attachment', 'delete', 'Soft-delete any attachment')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON (
        (p.permission_code = 'sys.attachment.read')   -- every role
     OR (p.permission_code = 'sys.attachment.upload'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT',
                                'ACCOUNTANT','AP_CLERK','AR_CLERK','SALES_STAFF'))
     OR (p.permission_code = 'sys.attachment.delete'
            AND r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN','CHIEF_ACCOUNTANT')))
ON CONFLICT DO NOTHING;

-- Sprint 14 — external API key management permission. Keep in sync with
-- Accounting.Api.Authorization.Permissions.Sys.ApiKeyManage. Additive +
-- idempotent. Admins ONLY (creating a key mints a credential — high trust).

INSERT INTO sys.permissions (permission_code, module, resource, action, description) VALUES
    ('sys.api_key.manage', 'sys', 'api_key', 'manage', 'Create / rotate / revoke external API keys')
ON CONFLICT (permission_code) DO NOTHING;

INSERT INTO sys.role_permissions (role_id, permission_id)
SELECT r.role_id, p.permission_id
FROM sys.roles r
JOIN sys.permissions p ON p.permission_code = 'sys.api_key.manage'
WHERE r.role_code IN ('SUPER_ADMIN','COMPANY_ADMIN')
ON CONFLICT DO NOTHING;

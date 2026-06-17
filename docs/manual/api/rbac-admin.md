# RBAC Admin

การจัดการสิทธิ์ต่อบริษัท: บทบาท (roles), แค็ตตาล็อกสิทธิ์, ผู้ใช้ และการกำหนดบทบาทให้ผู้ใช้.

Per-company RBAC administration (mounted at `/admin/rbac`). Roles and the permission catalog are gated by `sys.role.manage`; user-role assignment is gated by `sys.user.manage`. All operations are company-scoped; a cross-company write returns `403`.

## Permission catalog
- `GET /admin/rbac/permissions` — static bilingual catalog of all permission codes (no DB). **Auth:** `sys.role.manage`. Returns `200`.

## Roles
All gated by `sys.role.manage`.
- `GET /admin/rbac/roles` — list roles. Query: `companyId?` (super-admin cross-company). Returns `200`.
- `GET /admin/rbac/roles/{id}` — role detail. Path: `id` (int). Returns `200`.
- `POST /admin/rbac/roles` — create role. Body: `roleCode`, `nameTh` (required), `description?`, `companyId?` (`CreateRoleRequest`). Returns `201` `{ roleId }`.
- `PUT /admin/rbac/roles/{id}` — update role. Body: `nameTh`, `description?` (`UpdateRoleRequest`). Returns `204`.
- `DELETE /admin/rbac/roles/{id}` — delete role. Returns `204`.
- `PUT /admin/rbac/roles/{id}/permissions` — set the role's permission codes. Body: `{ permissionCodes: string[] }` (`SetRolePermissionsRequest`). Returns `204`.

## Users
Gated by `sys.user.manage`.
- `GET /admin/rbac/users` — list users. Query: `companyId?`. Returns `200`.
- `PUT /admin/rbac/users/{id}/roles` — assign roles to a user. Path: `id` (long). Body: `{ roleIds: int[], companyId?: int }` (`SetUserRolesRequest`). Returns `204`.

> See also `GET /me/permissions` (in [auth-and-identity.md](auth-and-identity.md)) for the caller's own effective permissions.

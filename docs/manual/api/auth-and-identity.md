# Auth & Identity

หมวดนี้ครอบคลุมการเข้าสู่ระบบ การสลับบริษัท ข้อมูลตัวตนของผู้ใช้ คีย์ API และ external API v1.

Login, session, the current-user surface, BFF-managed API keys, and the token-or-key external `/api/v1` surface.

## Auth

### `POST /auth/login`
Authenticate and obtain a JWT.

- **Auth:** Anonymous.
- **Request body:** `username` (string, required), `password` (string, required), `mfaCode` (string, optional).
- **Response:** `200` `{ access_token, expires_at, token_type: "Bearer" }`. If MFA is enabled and no code supplied: `200` `{ mfa_required: true }`.
- **Notes:** The JWT field is `access_token`. `401` on bad credentials.

### `POST /auth/switch-company/{companyId}`
Re-scope the caller's session to another company (super-admin only).

- **Auth:** `master.company.manage` (super-admin only — handler also asserts `IsSuperAdmin`).
- **Path params:** `companyId` (int).
- **Response:** `200` `{ access_token, expires_at, token_type }` — a fresh JWT bound to the target company.
- **Notes:** RLS is pinned at the DB session, so a new token is the only way to move tenant. `403` for non-super-admin; `404` if the company is missing/inactive.

## Me

### `GET /me`
The caller's identity plus the companies they may operate as (for the company switcher / onboarding gate).

- **Auth:** Authenticated.
- **Response:** `200` `MeResponse` = `{ userId, username, companyId, branchId, isSuperAdmin, companyName, allowedCompanies: [{ id, nameTh, nameEn }] }`. A normal user sees only their own company; a super-admin sees all active companies. No tax fields are ever exposed (§10).

### `GET /me/permissions`
The caller's effective permissions and roles (used by the frontend PermissionGate).

- **Auth:** Authenticated.
- **Response:** `200` `{ permissions: string[], roles: string[], isSuperAdmin: bool }` — read straight from the JWT claims (no DB round-trip).

## API Keys

BFF/JWT-managed keys for the external `/api/v1` surface. Plaintext is returned **once** on create/rotate.

### `GET /api-keys`
List API keys.
- **Auth:** `sys.api_key.manage` (super-admin + company-admin).
- **Response:** `200` list of key metadata (no plaintext).

### `POST /api-keys`
Create a key.
- **Auth:** `sys.api_key.manage`.
- **Request body:** `name` (string, required), `scopes` (string[], required), `expiresAt` (DateTimeOffset, optional), `defaultBusinessUnitId` (int, optional).
- **Response:** `201` — includes the plaintext key **once**.

### `DELETE /api-keys/{id}`
Revoke a key.
- **Auth:** `sys.api_key.manage`. **Path:** `id` (long). **Response:** `204`.

### `POST /api-keys/{id}/rotate`
Rotate a key (new secret).
- **Auth:** `sys.api_key.manage`. **Path:** `id` (long). **Response:** `200` — new plaintext **once**.

## External API v1 (`/api/v1`)

Stable external surface, prefixed `/api/v1`. Same JWT/permission model (also accepts API keys with matching scopes). Permission codes mirror the internal ones.

### Tax Invoices
- `POST /api/v1/tax-invoices` — create draft. **Auth:** `sales.tax_invoice.create`.
- `POST /api/v1/tax-invoices/{id}/post` — post (assigns number). **Auth:** `sales.tax_invoice.post`.
- `GET /api/v1/tax-invoices` — list. **Auth:** `sales.tax_invoice.read`.
- `GET /api/v1/tax-invoices/{id}` — detail. **Auth:** `sales.tax_invoice.read`.

### Receipts
- `POST /api/v1/receipts` — create. **Auth:** `sales.receipt.create`.
- `POST /api/v1/receipts/{id}/post` — post. **Auth:** `sales.receipt.post`.
- `GET /api/v1/receipts` — list. **Auth:** `sales.receipt.read`.
- `GET /api/v1/receipts/{id}` — detail. **Auth:** `sales.receipt.read`.

### Quotations
- `POST /api/v1/quotations` — create. **Auth:** `sales.quotation.create`.
- `POST /api/v1/quotations/{id}/send` — send. **Auth:** `sales.quotation.send`.
- `GET /api/v1/quotations` — list. **Auth:** `sales.quotation.read`.
- `GET /api/v1/quotations/{id}` — detail. **Auth:** `sales.quotation.read`.

### Customers & Products
- `POST /api/v1/customers` — create. **Auth:** `master.customer.manage`.
- `GET /api/v1/customers` — list. **Auth:** `master.customer.read`.
- `GET /api/v1/customers/{id}` — detail. **Auth:** `master.customer.read`.
- `GET /api/v1/products` — list. **Auth:** `master.product.read`.
- `GET /api/v1/products/{id}` — detail. **Auth:** `master.product.read`.

### System
- `GET /api/v1/system/info` — instance/tax config info. **Auth:** `sys.system_info.read`.

> Bodies for `/api/v1` create routes match their internal equivalents (see `sales.md` / `master-data.md`). The external-permission codes (`sales.tax_invoice.create`, etc.) are seeded for API-key callers.

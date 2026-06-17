# API Reference — TEAS Backend

คู่มืออ้างอิง REST API สำหรับนักพัฒนา (developer reference). ทุก endpoint ในเอกสารนี้มีอยู่จริงในโค้ด (`Accounting.Api/Endpoints/*.cs`).

This is a developer-facing reference for the TEAS backend (ASP.NET Core Minimal APIs). Every route documented here exists in the endpoint source. Request/response shapes mirror `docs/api/openapi.yaml` and the DTO records in `Accounting.Application`.

## Base URL

| Environment | URL |
|---|---|
| Local dev | `http://localhost:5080` |

The backend must run with `ASPNETCORE_ENVIRONMENT=Development` locally.

## Authentication

1. `POST /auth/login` with `{ "username", "password", "mfaCode?" }` (JSON, camelCase).
2. On success the response contains `access_token` (JWT), `expires_at`, `token_type` = `"Bearer"`. If MFA is required the first response is `{ "mfa_required": true }` and you re-POST with `mfaCode`.
3. Send the token on every subsequent request:

```
Authorization: Bearer <access_token>
```

The token field is `access_token` (not `token`). The JWT carries the user id, `company_id`, `branch_id`, the super-admin flag, role claims and permission (`perm`) claims.

## Tenant model (multi-company)

- Every business table is scoped by `company_id`; PostgreSQL RLS + an EF global query filter enforce isolation. You never pass `company_id` in a request — it is pinned from the JWT.
- A normal user is bound to exactly one company (the one in their token).
- A **super-admin** may re-scope their session to another company with `POST /auth/switch-company/{companyId}`, which re-issues the JWT for the target tenant. `GET /me` returns the caller's identity plus the list of companies they may operate as.

## Permission model

Authorization is permission-based. Each protected route declares a required permission code (e.g. `sales.tax_invoice.create`). A user's roles grant permission codes, which are baked into the JWT as `perm` claims. In this reference the **Auth** line of each entry shows one of:

- a permission code → the caller needs that permission (or be super-admin);
- **Authenticated** → any valid token;
- **Anonymous** → no token required (only `POST /auth/login`).

See `GET /me/permissions` for the caller's effective permissions, and the `rbac-admin.md` category for managing roles/permissions.

## Error envelope (RFC 7807)

Errors are returned as `application/problem+json` (ProblemDetails). Domain/validation failures map to:

| Status | Meaning |
|---|---|
| 400 / 422 | Validation problem (FluentValidation `errors` dictionary on 400) |
| 401 | Missing/invalid token |
| 403 | Authenticated but lacking the required permission, or a cross-tenant write |
| 404 | Not found (or inactive/other-tenant resource) |
| 409 / 422 | Domain rule violation (e.g. posting an already-posted document) |
| 501 | Not implemented (e.g. `PUT /company-profile/hard`) |

A typical body: `{ "type": "urn:teas:error:<code>", "title": "<code>", "detail": "...", "status": <code> }`.

## Conventions

- Request bodies are JSON, **camelCase**. Money fields are decimals (4 dp). Dates are ISO `YYYY-MM-DD`; timestamps are ISO-8601 `DateTimeOffset`.
- `period` query params are `YYYYMM` integers; `year` params are CE (Gregorian) integers.
- PDF endpoints return `application/pdf`; some export endpoints return `text/plain` or `application/zip` (noted per entry).
- Document numbers are assigned only on **post/issue**, never on draft (§4.3), and posted fiscal documents are immutable (§4.2).
- Most business routes are at the root (`/tax-invoices`, `/reports/...`). The **external API v1** surface is prefixed `/api/v1/...` (see `auth-and-identity.md`).

## Categories

| Category | Scope |
|---|---|
| [Auth & Identity](auth-and-identity.md) | login, company switch, `/me`, API keys, external `/api/v1` |
| [Master Data](master-data.md) | companies, company profile, branches, customers, vendors, products, business units, chart of accounts, document prefixes, expense categories, WHT types |
| [Sales](sales.md) | quotation → sales order → delivery order → tax invoice → receipt, credit/debit notes, billing notes, cross-refs, activity, print |
| [Purchases](purchases.md) | purchase order, vendor invoice, payment voucher, WHT certificates |
| [Payroll](payroll.md) | employees, payroll runs, payslips, ภ.ง.ด.1/1ก, SSO, 50ทวิ |
| [Tax Filings](tax-filings.md) | ภ.พ.30, ภ.ง.ด.3/53/54/36, ภ.ง.ด.50/51, ภ.พ.01/09, CIT, e-Tax, VAT registers |
| [Reports](reports.md) | P&L, balance sheet, trial balance, tax/sales summary, AP aging, number gaps, WHT receivable |
| [RBAC Admin](rbac-admin.md) | roles, permissions catalog, users, user-role assignment |
| [System](system.md) | health, system info, VAT threshold, period close, journals, attachments |

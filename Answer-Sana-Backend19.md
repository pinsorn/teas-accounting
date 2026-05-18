# Answer-Sana-Backend19 — Sprint 14: External API Integration + Per-Key BU Binding

**Date:** 2026-05-18
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Service-to-service integration via API key + per-key Business Unit binding
**Gate:** **~6-7 days human-equivalent. Phased execution (P1 → P8).**
**Prereq:** Sprint 12 (Internal PO) ✅ + Sprint 13c (e-Tax tier infra) ✅ must ship first. Independent of Sprint 13b (User Manual).

> Sprint 14 unlocks **microservices integration** (Shopify, POS, internal apps) +
> **per-microservice BU binding** so each API key auto-tags documents with its
> business unit. Reptify Shopify key → all TI = `MM-YYYY-TI-REPT-NNNN` without
> microservice needing to know about BU. Defensive design: BU lock enforced at
> middleware, not trusted from request body.

---

## 0. Pre-spec audit (Sana)

| Check | Result | Sprint 14 action |
|---|---|---|
| `ApiKey` entity | ✅ Built Sprint 1-2 (`ApiKeyId, CompanyId, Name, KeyHash, KeyPrefix, ScopesJson, ExpiresAt, LastUsedAt, RevokedAt, IsActive`) | Extend (add `DefaultBusinessUnitId`) |
| `sys.api_keys` table + RLS policy | ✅ Built (per `010_rls_policies.sql`) | NO change to RLS — already tenant-aware |
| X-Api-Key middleware / auth handler | ❌ doesn't exist | **BUILD** — main P1 work |
| `/api/v1/*` namespace | ❌ all routes at root (`/tax-invoices`, `/receipts` etc.) | **BUILD** — mount additionally under v1 (keep root for BFF — additive, no break) |
| Idempotency-Key middleware | ❌ doesn't exist | **BUILD** |
| `sys.idempotency_keys` table | ❌ doesn't exist | **BUILD** |
| Standard error envelope per plan §20.7 | ⚠ DomainException middleware exists, format may differ | **REFACTOR** to spec envelope `{error: {code, message, details, trace_id}}` |
| Webhook outbound | ❌ doesn't exist | **OUT OF SCOPE** (Phase 2) |
| Rate limiting | ❌ doesn't exist | **OUT OF SCOPE** (Phase 2 — Cloudflare or app middleware) |
| API key scope enforcement | ⚠ ScopesJson field exists, no use | **BUILD** — extend permission check |
| Business Unit dimension (Sprint 8) | ✅ `business_unit_id` on TI/RC/CN/DN headers + JournalLine + sub-prefix numbering | **REUSE** — wire into ApiKey default + lock |
| `IBusinessUnitService` | ✅ Sprint 8 | NO change — query for default BU resolution |
| Number sequence sub-prefix infrastructure | ✅ Built (Sprint 5 PV + Sprint 8 BU) | NO change — pass BU code as sub_prefix |
| `Idempotency-Key` header (existing usage anywhere?) | ❌ none | greenfield |

---

## 1. Phasing

| Part | Theme | Estimate |
|---|---|---|
| **P1** | `X-Api-Key` auth middleware + ApiKey resolution → request context (incl. BU + scope) | ~1 d |
| **P2** | ApiKey CRUD endpoints + UI `/settings/api-keys` | ~1 d |
| **P3** | `/api/v1/*` namespace mount (additive over existing root routes) | ~0.5 d |
| **P4** | Idempotency-Key middleware + storage table + cleanup worker | ~1 d |
| **P5** | Standard error envelope per plan §20.7 | ~0.5 d |
| **P6** | ApiKey scope enforcement | ~0.5 d |
| **P7** | **Per-key BU binding** — auto-fill + lock + numbering reuse | ~1 d |
| **P8** | Tests + OpenAPI spec update + e2e | ~1 d |

Single phase, ordered execution. Each step = own commit + gate.

---

## 2. P1 — `X-Api-Key` auth middleware

### Behavior

```
[Request arrives at /api/v1/*]
    ↓
[X-Api-Key header check]
    ├─ Missing → 401 with error envelope (code='auth.missing_api_key')
    ├─ Present but invalid/expired/revoked → 401 ('auth.invalid_api_key')
    └─ Valid →
        - Update ApiKey.LastUsedAt = UtcNow (async, fire-and-forget so latency impact = 0)
        - Establish HttpContext.User principal with claims:
          - company_id (from ApiKey.CompanyId)
          - api_key_id (for audit log)
          - api_key_name (informational)
          - scopes (from ApiKey.ScopesJson)
          - default_business_unit_id (from ApiKey, nullable — see P7)
          - default_business_unit_code (resolved at request time, nullable)
          - is_api_key = true (distinguishes from human JWT)
        - Set ITenantContext.CompanyId from ApiKey
        - Set ITenantContext.UserId = NULL (NO user — it's a service)
        - Set ITenantContext.ApiKeyId = ApiKey.ApiKeyId (new field on ITenantContext)
        - Continue pipeline
```

### Storage hash

ApiKey stored as bcrypt or pgcrypto hash (NOT plaintext). KeyPrefix (first 8 chars + last 4) shown in UI for identification:

```
Display:  "key_abc12345...wxyz"
Storage:  KeyHash (bcrypt) + KeyPrefix "key_abc1...wxyz"
```

When creating → return plaintext key ONCE only ("save it now — you won't see it again"). Match Stripe pattern.

### Performance

LastUsedAt update: fire-and-forget via channel/queue OR rate-limit (update at most every 5 min). Goal: don't add DB write latency to every request. Tier 1 acceptable to update synchronously; production benchmark + switch if needed.

### Coexistence with JWT

Routes UNDER `/api/v1/*` accept **only** API key (no JWT).
Routes UNDER root (existing `/tax-invoices`, `/receipts` etc.) accept **only** JWT (the BFF cookie pattern).

DI:
```csharp
// Add ApiKey scheme alongside existing JWT scheme
builder.Services.AddAuthentication()
    .AddJwtBearer(...)                 // existing for BFF
    .AddScheme<ApiKeyOptions, ApiKeyHandler>("ApiKey", _ => { });

// Route auth policy
app.MapGroup("/api/v1")
    .RequireAuthorization(new AuthorizationPolicyBuilder("ApiKey")
        .RequireAuthenticatedUser()
        .Build());
```

---

## 3. P2 — ApiKey CRUD + UI

### Endpoints (admin via BFF, not external)

```
GET    /api-keys                       list (current company)
POST   /api-keys                       create new key, returns plaintext ONCE
                                         body: { name, scopes[], expires_at?, default_business_unit_id? }
                                         response: { api_key_id, name, key_prefix, plaintext: "key_..." }
DELETE /api-keys/{id}                  revoke (set RevokedAt + IsActive=false)
POST   /api-keys/{id}/rotate           generate new plaintext, old key invalid immediately
                                         returns new plaintext + key_prefix
```

Permissions: `sys.api_key.manage` (new) — granted to SUPER_ADMIN + COMPANY_ADMIN only.

### UI — `/settings/api-keys`

- List: name, prefix, scopes (badge), default_BU (badge), created_at, last_used_at, expires_at, revoked_at, status
- Create modal:
  - Name (required, descriptive)
  - Scopes (multi-select: `sales.tax_invoice.create`, `sales.receipt.create`, `sales.quotation.create`, etc.)
  - Default Business Unit (dropdown — list of company's active BUs OR "None (caller specifies per request)")
  - Expires at (optional date)
  - "Create" → modal shows plaintext key with copy-button + warning "Save now — won't show again"
- Rotate button per key — confirm dialog → generate + display new plaintext
- Revoke button — confirm + reason

### Audit

Every ApiKey CRUD action logged in `activity_logs` with diff. Plaintext NEVER logged.

---

## 4. P3 — `/api/v1/*` namespace mount (additive)

### Approach

Create a new endpoint module `ApiV1Endpoints.cs` that mounts a subset of existing services under `/api/v1/`. Existing root routes (`/tax-invoices`, `/receipts`, etc.) stay UNCHANGED for the frontend BFF.

```csharp
public static class ApiV1Endpoints
{
    public static IEndpointRouteBuilder MapExternalApiV1(this IEndpointRouteBuilder app)
    {
        var v1 = app.MapGroup("/api/v1")
                    .RequireAuthorization("ApiKey")
                    .AddEndpointFilter<IdempotencyFilter>();    // P4

        // Tax Invoices
        v1.MapPost("/tax-invoices", CreateTaxInvoice);
        v1.MapPost("/tax-invoices/{id:long}/post", PostTaxInvoice);
        v1.MapGet ("/tax-invoices/{id:long}", GetTaxInvoice);
        v1.MapGet ("/tax-invoices", ListTaxInvoices);

        // Receipts
        v1.MapPost("/receipts", CreateReceipt);
        v1.MapPost("/receipts/{id:long}/post", PostReceipt);
        v1.MapGet ("/receipts/{id:long}", GetReceipt);
        v1.MapGet ("/receipts", ListReceipts);

        // Quotations (Sprint 10)
        v1.MapPost("/quotations", CreateQuotation);
        v1.MapPost("/quotations/{id:long}/send", SendQuotation);
        v1.MapGet ("/quotations/{id:long}", GetQuotation);
        v1.MapGet ("/quotations", ListQuotations);

        // Customers (lookup + create — needed by integrations)
        v1.MapPost("/customers", CreateCustomer);
        v1.MapGet ("/customers", ListCustomers);
        v1.MapGet ("/customers/{id:long}", GetCustomer);

        // Products (lookup — to map external SKUs to internal product_id)
        v1.MapGet ("/products", ListProducts);
        v1.MapGet ("/products/{id:long}", GetProduct);

        // System info (already public-ish)
        v1.MapGet ("/system/info", GetSystemInfo);

        return app;
    }
}
```

### Implementation note

Handlers DELEGATE to the same service interfaces as root routes (`ITaxInvoiceService`, `IReceiptService`, etc.). No business logic duplication — just different mounting + auth.

Difference at v1 handlers:
- Auth = ApiKey (not JWT)
- Request body might have stripped-down DTO with `IdempotencyKey` field
- Response uses standard envelope (P5)
- BU auto-fill applied (P7)

### Scope minimum

Endpoints chosen for v1 = "what a microservice would actually call to bill customers." Excluded from v1:
- Settings / master CRUD beyond customer + product (only admins should manage these)
- Reports (microservices typically don't query reports)
- Tax filings (regulatory — internal use only)
- Internal PO / VI / PV (purchase-side internal workflows)
- Attachments (Phase 2 — when API supports file upload)
- e-Tax (e-Tax sign+send is internal pipeline, not exposed)

Easy to ADD endpoints to v1 later — additive pattern.

---

## 5. P4 — Idempotency-Key middleware + storage

### Schema

```
sys.idempotency_keys
  idempotency_key_id  BIGINT IDENTITY PK
  company_id          INT NN
  api_key_id          BIGINT NN FK sys.api_keys
  idempotency_key     VARCHAR(255) NN     -- client-supplied, e.g. "shopify-order-12345"
  request_hash        VARCHAR(64) NN      -- SHA256 of (method + path + body) — for strict-match check
  response_status     INT NN              -- HTTP status of the recorded response
  response_body       JSONB NN            -- recorded response body
  created_at          TIMESTAMPTZ NN
  expires_at          TIMESTAMPTZ NN      -- now + 24h
  UNIQUE(company_id, api_key_id, idempotency_key)
  INDEX ix_idemp_expiry (expires_at) WHERE expires_at > NOW()  -- for cleanup
```

### Filter behavior

```csharp
public sealed class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var req = ctx.HttpContext.Request;
        if (req.Method != "POST" && req.Method != "PUT" && req.Method != "PATCH")
            return await next(ctx);   // GET/DELETE skip — idempotent by definition

        var key = req.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrEmpty(key))
        {
            // Policy: REQUIRED for v1 mutations
            return Results.Problem(
                statusCode: 400,
                detail: "Missing Idempotency-Key header",
                type: "idempotency.required");
        }

        var hash = ComputeRequestHash(req);   // method + path + body
        var existing = await _store.GetAsync(companyId, apiKeyId, key, ct);

        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                return Results.Conflict(new { error = "idempotency.body_mismatch" });

            // Replay recorded response
            ctx.HttpContext.Response.StatusCode = existing.ResponseStatus;
            return existing.ResponseBody;
        }

        // First time — execute + record
        var result = await next(ctx);
        await _store.SaveAsync(companyId, apiKeyId, key, hash, ctx.HttpContext.Response.StatusCode, result, expires: now+24h, ct);
        return result;
    }
}
```

### Cleanup worker

Background service every 1h: `DELETE FROM sys.idempotency_keys WHERE expires_at < NOW()`. Index helps.

### Policy

`Idempotency-Key` **REQUIRED** for all v1 POST/PUT/PATCH. Tests assert 400 if missing. This is stricter than Stripe (where it's optional) — we go strict because financial doc creation = no-replay tolerance.

---

## 6. P5 — Standard error envelope per plan §20.7

### Format

```json
{
  "error": {
    "code": "validation_error",
    "message": "Customer tax_id is required for CORPORATE customer_type",
    "details": [
      { "field": "customer.tax_id", "issue": "required" }
    ],
    "trace_id": "abc123def456",
    "request_id": "req_xyz789"
  }
}
```

### Refactor `DomainExceptionMapper` middleware

- Map DomainException to envelope
- Map ValidationException (FluentValidation) to envelope with details[]
- Map unhandled exception to 500 with trace_id (NO stack trace exposed except in dev)
- `trace_id` from `Activity.Current?.Id` (already in pipeline via OpenTelemetry-friendly pattern)
- `request_id` from `HttpContext.TraceIdentifier`

### Per-error codes

Catalog stable error codes (CSV/enum reference):
- `auth.missing_api_key`
- `auth.invalid_api_key`
- `auth.expired_api_key`
- `auth.revoked_api_key`
- `auth.scope_required` (caller's ApiKey doesn't have scope X)
- `idempotency.required`
- `idempotency.body_mismatch`
- `validation_error`
- `tenant.cross_tenant_access`
- `business_unit.locked_mismatch` (P7)
- `business_unit.locked_missing` (P7)
- `period.closed`
- `document.immutable_after_post`
- ... add per existing DomainException codes

API consumers can program against these.

### Backward compat

Existing root routes (BFF) — keep current ValidationProblem RFC 7807 response shape (frontend expects this). ONLY `/api/v1/*` uses new envelope. Filter applied per-namespace.

---

## 7. P6 — ApiKey scope enforcement

### Concept

Each ApiKey has `ScopesJson` = JSON array of permission strings:
```json
["sales.tax_invoice.create", "sales.tax_invoice.read", "sales.receipt.create", "master.customer.read"]
```

When ApiKey-authenticated request hits an endpoint, the existing `RequireAuthorization(perm)` mechanism must:
1. If caller is JWT user → check user's role permissions (existing)
2. If caller is API key → check API key's ScopesJson (NEW)

### Implementation

Extend `PermissionAuthorizationHandler` to detect API key vs JWT:

```csharp
public async Task<bool> HandleAsync(string requiredPerm, ClaimsPrincipal user, ...)
{
    if (user.HasClaim("is_api_key", "true"))
    {
        var scopes = user.FindFirst("scopes")?.Value;  // CSV or JSON array
        return scopes is not null && scopes.Contains(requiredPerm);
    }
    // existing JWT user role-perm check
    return await CheckJwtUserPermission(user, requiredPerm);
}
```

### UI scope picker

In API key create modal, scope dropdown lists available permissions. Reasonable subset for external API:
- Sales: `sales.tax_invoice.{create,read,post}`, `sales.receipt.{create,read,post}`, `sales.quotation.{create,read,send}`
- Master: `master.customer.{read,manage}`, `master.product.read`
- System: `sys.system_info.read`

NOT exposable: admin permissions, master delete, tax filing actions, e-Tax internal.

---

## 8. P7 — **Per-key BU binding** (Ham's requirement)

### Concept

Each `ApiKey` can have an optional `DefaultBusinessUnitId`. When set:
- Auto-fill `business_unit_id` on document creation if request omits it
- **Lock**: if request specifies a DIFFERENT business_unit_id → reject with `business_unit.locked_mismatch`
- If company `requires_business_unit = true` AND key has no default + request also omits → reject with `business_unit.locked_missing`

When NOT set:
- Caller must specify per-request (subject to company.requires_business_unit flag)

### Schema extension

```
ALTER sys.api_keys ADD:
  default_business_unit_id  INT NULL FK master.business_units
```

CHECK: FK constraint enforces same-tenant + active BU.

### Middleware enhancement (P1 + P7)

ApiKey middleware sets `HttpContext.Items["default_business_unit_id"]` AND adds a claim `default_business_unit_id`.

### Service layer enhancement

For each create endpoint that accepts `business_unit_id` in body:

```csharp
public async Task<long> CreateTaxInvoiceAsync(CreateTaxInvoiceRequest req, CancellationToken ct)
{
    var keyBu = _httpContext.User.FindFirst("default_business_unit_id")?.Value;  // null if JWT user OR key has no default

    if (keyBu is not null)
    {
        // API key with bound BU
        if (req.BusinessUnitId is null)
            req = req with { BusinessUnitId = int.Parse(keyBu) };
        else if (req.BusinessUnitId.ToString() != keyBu)
            throw new DomainException("business_unit.locked_mismatch",
                $"This API key is bound to BU {keyBu}; request specified {req.BusinessUnitId}");
    }

    // company-level enforcement still applies (Sprint 8.6 R-Q logic)
    if (req.BusinessUnitId is null && company.RequiresBusinessUnit)
        throw new DomainException("business_unit.required", "...");

    // ... existing flow
}
```

Apply same pattern to:
- TaxInvoice
- Receipt
- TaxAdjustmentNote (CN/DN)
- Quotation
- SalesOrder
- DeliveryOrder
- (Future) PurchaseOrder, etc.

### Numbering integration

Already works: `TaxInvoiceService.PostAsync` passes `businessUnit.Code` as sub_prefix to `NumberSequenceService.NextAsync` (per Sprint 8). No change needed — BU resolution flows through normally.

Result: Reptify Shopify API key (DefaultBusinessUnitId=REPT) → all TI = `05-2026-TI-REPT-NNNN` automatically, with NO microservice-side BU awareness needed.

### Cross-BU receipts via API

Edge case: Receipt that applies to TIs from DIFFERENT BUs (Sprint 8 cross-BU pattern).

Behavior with API key BU lock:
- If applied TIs all share key's bound BU → OK (single-BU receipt)
- If applied TIs span multiple BUs → reject with `business_unit.cross_bu_not_allowed_for_this_key` (defensive — API key for Lab should not be receiving Reptify customer payments)

User-via-BFF still has cross-BU flexibility (Sprint 8.6 cross-BU receipt). API key callers are restricted by design.

---

## 9. P8 — Tests + OpenAPI + e2e

### Unit
- `ApiKeyHashTests` — bcrypt round-trip
- `ApiKeyResolverTests` — claims population from key
- `IdempotencyFilterTests` — replay, body mismatch, missing key
- `ApiKeyBuBindingTests` — auto-fill, lock-mismatch, cross-BU reject
- `ScopeAuthHandlerTests` — JWT path vs ApiKey path branching

### Integration
- `POST /api/v1/tax-invoices` with valid API key + Idempotency-Key + BU-bound key + no body BU → TI created with key's BU
- Same call + body BU = key's BU → OK
- Same call + body BU ≠ key's BU → 409 `business_unit.locked_mismatch`
- Replay (same Idempotency-Key, same body) → identical response, no duplicate TI
- Replay with different body → 409 `idempotency.body_mismatch`
- Missing Idempotency-Key → 400
- Invalid/expired/revoked key → 401 with envelope
- Key without `sales.tax_invoice.create` scope → 403 `auth.scope_required`
- Cross-tenant key → 404 (don't leak existence)
- LastUsedAt updates after successful call
- Cleanup worker removes expired idempotency rows
- All v1 endpoints reject JWT (only ApiKey accepted)
- All root endpoints reject ApiKey (only JWT accepted)

### e2e Playwright (×1 new)
- `external-api-microservice.spec.ts`:
  1. Admin login via UI → /settings/api-keys → create key for Reptify with scope=tax_invoice.create + default_BU=REPT
  2. Save plaintext key
  3. Use Playwright's `request` context (not browser) to call `/api/v1/tax-invoices` with key + Idempotency-Key
  4. Assert 201 + doc_no contains REPT
  5. Replay → 200 same response
  6. Replay with modified body → 409
  7. Try without BU in body → still REPT
  8. Try with body.business_unit_id=LAB → 409 lock_mismatch

### OpenAPI

Update `docs/api/openapi.yaml`:
- New top-level: `/api/v1/*` paths section
- Security scheme: `ApiKeyAuth` (header X-Api-Key) alongside existing BearerAuth
- All v1 paths use ApiKeyAuth
- Schemas may reuse existing DTOs OR define stripped-down `*ApiRequest` versions if needed
- Document Idempotency-Key header on all POST/PUT
- Document standard error envelope

Total Playwright: 30 prior (Sprint 13c) + 1 new = **31/31**.

---

## 10. Scope cuts — explicitly OUT

- ❌ **Webhook outbound** — Phase 2 (when first customer needs event notifications)
- ❌ **Rate limiting per API key** — Phase 2 (Cloudflare or app-layer middleware)
- ❌ **API key auto-rotation** — manual rotation via UI sufficient
- ❌ **OAuth 2.0 client credentials flow** — API key sufficient; OAuth Phase 2 if customer demands
- ❌ **API gateway** (Kong / Apigee) — Phase 2 SaaS scale
- ❌ **GraphQL** — REST only; GraphQL never planned
- ❌ **DDoS protection** — infrastructure-layer (Cloudflare); not app
- ❌ **API metrics dashboard** — basic LastUsedAt only; full metrics Phase 2
- ❌ **Multi-version coexistence** (v1 + v2) — only v1 exists; v2 added when first breaking change needed
- ❌ **File upload via API** — attachments via v1 = Phase 2
- ❌ **Approve actions via API key** — keep approve in BFF only (SoD = approver must be a human user, not a service)
- ❌ **Cross-BU receipts via API key** — defensive reject; only BFF users have flexibility
- ❌ **DELETE actions via API** — soft-cancel via specific action endpoints only (e.g. POST /api/v1/quotations/{id}/cancel); no generic DELETE

If any block → escalate per §8.

---

## 11. Cross-sprint dependencies

- Consumes Sprint 8 Business Units (`business_unit_id` field + sub-prefix numbering)
- Consumes Sprint 10 Product master (`/api/v1/products` lookup)
- Consumes Sprint 10 Quotation chain (`/api/v1/quotations`)
- Consumes Sprint 13c standard infrastructure conventions (Sprint 13c ships error envelope cleanup that may overlap with P5 here — verify alignment during P1)

---

## 12. Gates (all green, non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 |
| Domain tests | +N (BU binding, idempotency, scope) |
| Api tests | +N (full v1 endpoint matrix; auth scheme isolation BFF↔v1) |
| EF migration | `AddApiKeyBuBinding + AddIdempotencyKeys` clean |
| tsc / next build | 0 / 0 (+1 route `/settings/api-keys`) |
| Playwright | 30 + 1 new = **31/31** |
| Mirror | synced `Y:\AccountApp` |
| Auth isolation | v1 route rejects JWT; root route rejects ApiKey (cross-test) |
| BU lock enforcement | integration test confirms 409 on mismatch |
| OpenAPI sync | spec covers all v1 endpoints + ApiKeyAuth scheme |

---

## 13. DoD

1. P1: `X-Api-Key` middleware + ApiKey resolution + claim population + ITenantContext extension
2. P2: ApiKey CRUD endpoints + UI `/settings/api-keys` + plaintext-once display
3. P3: `/api/v1/*` namespace mounted with subset of endpoints
4. P4: Idempotency-Key middleware + `sys.idempotency_keys` table + cleanup worker
5. P5: Standard error envelope per plan §20.7 (applied to v1 only; root keeps RFC 7807)
6. P6: ApiKey scope enforcement (extend PermissionAuthorizationHandler)
7. P7: `DefaultBusinessUnitId` on ApiKey + auto-fill + lock + cross-BU receipt reject
8. P8: Tests (unit + integration + 1 e2e) + OpenAPI spec update
9. All gates green
10. Mirror sync `Y:\AccountApp`
11. plan.md §23.3 — strike Sprint 14
12. `Report-Backend19.md`

**Total: 12 DoD items.**

---

## 14. After this sprint

Phase 1 = production-ready foundation. Remaining for go-live:
- **Sprint 13b** (User Manual generator) — now includes external API integration guide as one chapter
- **External pen-test** (5-10 d external vendor)
- **First customer onboarding + data migration** (per `test/08-data-migration-test.md`)
- **Go-live checklist** (per `test/09-go-live-checklist.md`)
- **Real e-Tax UAT registration** (Phase 0 prerequisite — 4-6 wks lead time)

**Phase 1 production-ready ETA after Sprint 14:** ~2 weeks at current burn rate (Sprint 13b + pen-test + first customer).

---

## 15. Sample microservice integration (for spec validation)

```typescript
// Example: Shopify Reptify integration
const REPTIFY_KEY = process.env.TEAS_REPTIFY_API_KEY;  // key_abc12345...wxyz

async function createTaxInvoiceFromShopifyOrder(order) {
  const response = await fetch('https://api.your-teas.example/api/v1/tax-invoices', {
    method: 'POST',
    headers: {
      'X-Api-Key': REPTIFY_KEY,
      'Idempotency-Key': `shopify-order-${order.id}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({
      customer_id: await resolveCustomer(order.customer.email),
      doc_date: new Date().toISOString().split('T')[0],
      // business_unit_id intentionally OMITTED — API key auto-fills with REPT
      lines: order.items.map(item => ({
        product_id: await resolveProduct(item.sku),
        description_th: item.name_th,
        quantity: item.quantity,
        unit_price: item.price,
        tax_code_id: item.is_live_animal ? EXEMPT_LIVE : VAT_OUT_7,
      })),
    }),
  });

  if (!response.ok) {
    const err = await response.json();
    console.error(`TEAS error ${err.error.code}: ${err.error.message}`);
    throw new Error(err.error.message);
  }

  return await response.json();
  // Response: { tax_invoice_id, doc_no: "05-2026-TI-REPT-0042", ... }
}
```

Microservice didn't need to know about BU = clean separation of concerns.

---

**Build it. ~6-7 days human-equivalent. Phase: P1 → P8. Report back via Report-Backend19.**

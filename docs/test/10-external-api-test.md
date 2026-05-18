# 10 — External API Test (Service-to-service integration)

**Per `accounting-system-plan.md` §20** — TEAS exposes an external REST API for
service-to-service integration. Examples:
- E-commerce platform (Shopify, WooCommerce) → POST tax invoices into TEAS on order completion
- POS system → POST receipts at end of day
- Microservices in customer's broader infrastructure that need to issue bills

This chapter covers contract testing, integration testing, security testing of the
API surface that's exposed externally.

---

## 1. Scope

Endpoints under `/api/v1/*` (the external-facing API namespace). Plan §20.3 lists
target endpoints:

```
POST   /api/v1/tax-invoices
GET    /api/v1/tax-invoices/{id}
GET    /api/v1/tax-invoices?cursor=...
POST   /api/v1/receipts
POST   /api/v1/payment-vouchers
POST   /api/v1/customers
POST   /api/v1/products
GET    /api/v1/system/info
... webhooks (outbound) per §20.4
```

**Excluded from this chapter:**
- Internal `/api/*` (no `/v1/`) endpoints used by the frontend BFF — covered in ch.02
- Admin/super-admin endpoints (not exposed externally)

---

## 2. Authentication test

### 2.1 API key flow

Per plan §20.2 — clients authenticate via long-lived API key (in header).

| Test | Expected | Status |
|---|---|---|
| Valid API key in `X-Api-Key` header | 200 OK | ⏳ Phase 1 ปลาย |
| Missing `X-Api-Key` header | 401 Unauthorized | ⏳ |
| Invalid `X-Api-Key` value | 401 Unauthorized, no info leaked | ⏳ |
| Revoked API key (set `revoked_at`) | 401 with code `api_key_revoked` | ⏳ |
| API key from wrong tenant | 403 Forbidden (don't reveal cross-tenant existence) | ⏳ |
| Rate limit per key (e.g. 100 req/min) → exceeded | 429 Too Many Requests | ⏳ |
| API key rotation: new key works, old key returns 401 after deactivation | ⏳ |

### 2.2 API key management

| Test | Expected |
|---|---|
| Super-admin can create API key for any tenant | ✅ |
| Company admin can create API key for own tenant only | ✅ |
| Created API key returned exactly once (cannot retrieve later) | ✅ |
| API key has scoped permissions (subset of caller's perms) | ⏳ |
| List API keys shows masked value (no plaintext) | ⏳ |

---

## 3. Contract test (OpenAPI conformance)

Tool: **Schemathesis** (Python) or **Dredd** — runs against deployed API.

**Setup:**
```bash
schemathesis run \
  --base-url https://staging.teas.example/api/v1 \
  --header "X-Api-Key: $TEST_API_KEY" \
  docs/api/openapi.yaml \
  --checks all
```

**What it validates:**
- Every endpoint in OpenAPI exists in the running API
- Every request body matches the schema
- Every response matches its declared schema (incl. error responses)
- Status codes match
- Required headers present
- Response examples actually match real responses

**Run cadence:** nightly + on every API spec change.

**Pass criteria:** zero violations. Any contract drift → investigate.

---

## 4. Idempotency test (plan §20.5)

External APIs MUST support idempotent POST per RFC 7231 + Stripe pattern.

| Test | Expected |
|---|---|
| POST with `Idempotency-Key: abc123` → 201 with TI ID | success |
| Same `Idempotency-Key` re-POSTed within 24h → 200 with same TI ID (no duplicate) | dedup |
| Same `Idempotency-Key` with DIFFERENT body → 409 Conflict | strict match |
| Different `Idempotency-Key`, same logical content → 2 separate TI created | by design |
| `Idempotency-Key` after 24h TTL → treated as new | TTL respected |
| Missing `Idempotency-Key` → 400 (mandatory for POST per policy) | enforce |

**Storage:** dedicated `sys.idempotency_keys` table with (api_key_id, key, request_hash, response_body, expires_at).

---

## 5. Versioning test (plan §20.6)

| Test | Expected |
|---|---|
| `/api/v1/...` exists | 200 |
| `/api/v0/...` does NOT exist | 404 |
| `/api/v2/...` (future) coexists with v1 | both work |
| v1 endpoint stability: breaking change → forbidden without v-bump | manual review |
| Deprecation header (`Sunset:`) on v1 when v2 ships | future |

**Policy:** v1 contracts frozen at first external customer. Any breaking change → v2.

---

## 6. Webhook test (plan §20.4)

When TEAS notifies external systems of events (TI posted, payment received, etc).

| Test | Expected |
|---|---|
| Subscribe webhook URL to event "tax_invoice.posted" | webhook saved |
| TI POST → webhook fires within 5 seconds | delivered |
| Webhook payload schema matches docs | contract |
| Webhook delivery failure → retry with exponential backoff (max 5 retries over 24h) | resilient |
| Webhook signature (HMAC) verifies authentic origin | secure |
| Webhook receiver returns 5xx → retry; returns 4xx → don't retry (probably misconfig) | smart |
| Webhook dead-letter after max retries → logged + alert | observable |
| Replay attack: same payload submitted to receiver twice → receiver dedups via event_id | educate consumers |

**Mock webhook receiver setup:** use `ngrok` for staging tests; spin up mock HTTP server in CI.

---

## 7. Pagination test (plan §20.8)

All list endpoints use cursor pagination.

| Test | Expected |
|---|---|
| `GET /api/v1/tax-invoices` with no cursor → first page (default 25) | success |
| Response includes `next_cursor` if more results | true |
| Use `next_cursor` for page 2 → returns next 25 | continuity |
| Last page: `next_cursor: null` | true |
| Custom `limit=100` → 100 returned | success |
| `limit=10000` → capped at max (e.g. 200) | safety |
| Cursor stability across writes (new row inserted between page 1 and page 2 doesn't duplicate or skip) | ✅ |

---

## 8. Multi-tenant isolation via API

**Critical.** Every external API request must respect tenant boundary.

| Test | Expected |
|---|---|
| API key for tenant A creates TI → TI has company_id = A | ✅ |
| API key for tenant A reads TI ID belonging to tenant B → 404 (not 403, don't reveal) | ✅ |
| API key for tenant A reads `GET /tax-invoices` → only tenant A's TIs | ✅ |
| Cross-tenant FK reference attempt (customer_id from another tenant) → reject | ✅ |
| Webhook for tenant A NEVER includes tenant B's data even by accident | manual review |

---

## 9. Rate limiting

| Test | Expected |
|---|---|
| Per-API-key rate: e.g. 60 req/min sustained | 200 ok up to limit |
| Exceed rate → 429 with `Retry-After` header | informative |
| After retry-after seconds → 200 again | window resets |
| Burst tolerance: 10 reqs in 1 second within 60s window allowed | reasonable |
| Different API keys not affected by each other's rate | isolated |
| DDoS-level (1000s/sec) → upstream cloudflare/WAF blocks before app | infrastructure |

---

## 10. Error response contract

All error responses must follow a consistent envelope (plan §20.7):

```json
{
  "error": {
    "code": "validation_error",
    "message": "Customer tax_id is required for CORPORATE customer_type",
    "details": [
      { "field": "customer.tax_id", "issue": "required" }
    ],
    "trace_id": "abc123def456"
  }
}
```

| Test | Expected |
|---|---|
| 400 errors include field-level issues | ✅ contract |
| 401/403 have generic message (no info leak) | ✅ |
| 5xx errors have trace_id for support correlation | ✅ |
| No stack trace leaks in any environment except local-dev | ✅ |

---

## 11. Real-world integration scenarios

### EXT-01 — E-commerce → TI on order completion

**Scenario:** Shopify order completed → webhook fires to external integration service → service calls TEAS API to create TI.

**Steps:**
1. Mock Shopify webhook fires
2. Integration service maps Shopify customer → TEAS customer (lookup by email)
3. Integration service builds TEAS TI payload (lines from Shopify line items)
4. POST `/api/v1/tax-invoices` with Idempotency-Key = `shopify-order-{id}`
5. TEAS creates Draft TI
6. POST `/api/v1/tax-invoices/{id}/post` to finalize
7. TEAS returns TI with doc_no
8. TEAS webhook fires back to integration → confirms

**Pass criteria:** TI matches Shopify order amount + VAT + customer.

### EXT-02 — POS end-of-day batch

**Scenario:** POS system batches 200 receipts at end of day → bulk import.

**Steps:**
1. POS exports daily CSV (200 receipts)
2. Integration service POSTs each receipt with unique Idempotency-Key
3. TEAS processes each — some fail validation (e.g. customer not found)
4. Integration service handles per-receipt error (retry, skip, or fail batch)
5. End: TEAS daily Receipt total matches POS daily total

**Pass criteria:** Net zero discrepancy. Errors logged + reconciled per-record.

### EXT-03 — Microservice issuing PV for AWS bill

**Scenario:** Cost management microservice detects AWS bill → creates PV in TEAS.

**Steps:**
1. AWS Cost Explorer API → bill data
2. Microservice resolves "AWS Vendor" in TEAS Vendor master (created with is_foreign=true + no Thai VAT-D)
3. Microservice POST `/api/v1/payment-vouchers` with self_withhold_mode=true (auto from vendor flags)
4. TEAS creates PV, computes WHT 15% gross-up, sets requires_pnd36_reverse_charge=true
5. Microservice receives PV detail with computed expense

**Pass criteria:** GL matches expected gross-up math. Flag set for Sprint 9 ภ.พ.36 generator.

---

## 12. API performance

Per ch.06 targets, externally-facing endpoints must hit:

| Endpoint | p95 target |
|---|---|
| POST /api/v1/tax-invoices | < 700ms |
| GET /api/v1/tax-invoices/{id} | < 200ms |
| GET /api/v1/tax-invoices (list, 25) | < 400ms |
| POST /api/v1/receipts | < 500ms |
| POST /api/v1/payment-vouchers | < 1.5s (PDF gen included) |

Load test: dedicated k6 script with API key auth, runs nightly against staging.

---

## 13. Backward compatibility test

When changing the API:

| Change | Compatible? |
|---|---|
| Add a new optional field to response | YES |
| Add a new endpoint | YES |
| Add a new required field to request body | **NO** — breaking, v-bump needed |
| Remove a field from response | **NO** — breaking |
| Change field type | **NO** — breaking |
| Change status code semantics | **NO** — breaking |
| Add a new enum value to existing enum | YES (consumers should be defensive) |

Test: dedicated "v1-backward-compat" test suite runs every release. Replays old request bodies, verifies still works.

---

## 14. Documentation contract

| Item | Status |
|---|---|
| OpenAPI spec (`docs/api/openapi.yaml`) covers 100% of external endpoints | ✅ in sync (verified post Sprint 6) |
| Each endpoint has request + response examples | ✅ |
| Each endpoint has error response examples | partial |
| Authentication explained | ✅ |
| Idempotency explained with example | ⏳ Phase 1 ปลาย |
| Webhook signature verification explained with code sample | ⏳ Phase 1 ปลาย |
| Rate limits documented | ⏳ |
| Versioning policy documented | ⏳ |

---

## 15. Sample integration partner README

Provide a one-page integration guide for new API consumers:

```
# TEAS External API — Quickstart

1. Get your API key (Settings → API Keys → Create)
2. All requests:
   - URL: https://api.your-teas.example/api/v1
   - Header: X-Api-Key: <your-key>
   - Header: Idempotency-Key: <unique-per-logical-request>
3. Sample: Create Tax Invoice
   ...
4. Webhooks: subscribe + verify signature
   ...
5. Rate limits: 60 req/min default; ask for higher
6. Errors: structured envelope, see docs
7. Support: help@your-teas.example
```

Maintained at `docs/api/integration-guide.md`.

---

## 16. Test cadence

| Test | Frequency |
|---|---|
| Contract (Schemathesis) | nightly |
| Idempotency happy path | per release |
| Webhook delivery | per release |
| Rate limit | per release |
| Multi-tenant isolation | per release |
| Performance | weekly |
| Backward compat | per release |
| Real-world scenarios EXT-01/02/03 | per release |

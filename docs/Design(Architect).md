# Architecture Design Specification
## Thailand Enterprise Accounting System

**Version:** 1.0  
**Parent Doc:** `accounting-system-plan.md` v1.4  
**Companion Doc:** `Design(UI).md` v1.0  

---

## สารบัญ

1. System Overview
2. Logical Architecture
3. Microservices Breakdown
4. Data Layer
5. Storage Strategy
6. Authentication & Authorization
7. API Gateway & Service Mesh
8. Background Jobs & Scheduler
9. e-Tax Signing Service (Critical)
10. RD Open API Integration
11. SMTP Email Service
12. External API Architecture
13. Observability (Logs, Metrics, Traces)
14. Security
15. Deployment Topology
16. Disaster Recovery & Backup
17. Performance Targets
18. Configuration Management (.env)
19. Migration & Phase 2 (H2H Upgrade)
20. Tech Stack Recommendation

---

## 1. System Overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      EXTERNAL CONSUMERS                         │
│  Web UI (Browser) | Mobile App | E-commerce Webhook | CRM | POS │
└────────────────────────────────┬────────────────────────────────┘
                                 │ HTTPS (TLS 1.3)
                                 ↓
┌─────────────────────────────────────────────────────────────────┐
│             API GATEWAY (Kong / Azure API Mgmt / Traefik)       │
│   • TLS termination     • Rate limiting   • Auth validation     │
│   • Request routing     • Audit logging   • CORS                │
└────────────────────────────────┬────────────────────────────────┘
                                 │
        ┌────────────────────────┼────────────────────────┐
        ↓                        ↓                        ↓
┌──────────────┐         ┌──────────────┐         ┌──────────────┐
│ Auth Service │         │ Core Domain  │         │ Background   │
│ (Identity)   │         │ Services     │         │ Workers      │
└──────────────┘         └──────┬───────┘         └──────┬───────┘
                                │                        │
                                ↓                        ↓
            ┌───────────────────┴──────────────────────────────┐
            │              CORE DOMAIN SERVICES                │
            │ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐│
            │ │Sales Svc │ │Purchase  │ │Tax Svc   │ │ Master ││
            │ │          │ │Svc       │ │          │ │ Data   ││
            │ └──────────┘ └──────────┘ └──────────┘ └────────┘│
            │ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌────────┐│
            │ │GL Svc    │ │Reporting │ │Audit Svc │ │ Numbr  ││
            │ │          │ │          │ │          │ │ Svc    ││
            │ └──────────┘ └──────────┘ └──────────┘ └────────┘│
            └──────────────────┬───────────────────────────────┘
                               │
                               ↓
            ┌─────────────────────────────────────────────────┐
            │            INTEGRATION SERVICES                 │
            │ ┌────────────┐ ┌────────────┐ ┌──────────────┐  │
            │ │e-Tax       │ │RD Open API │ │SMTP Service  │  │
            │ │Signing Svc │ │Connector   │ │              │  │
            │ │(XAdES)     │ │            │ │              │  │
            │ └─────┬──────┘ └─────┬──────┘ └──────┬───────┘  │
            └──────┼─────────────┼──────────────┼─────────────┘
                   │             │              │
                   ↓             ↓              ↓
            ┌────────────┐  ┌─────────┐  ┌────────────┐
            │ CA Cert    │  │ RD API  │  │ Mail Relay │
            │ (PFX file) │  │ Servers │  │ (M365/SES) │
            └────────────┘  └─────────┘  └────────────┘

            ┌─────────────────────────────────────────────────┐
            │                 DATA LAYER                      │
            │ ┌──────────────┐ ┌──────────────┐ ┌──────────┐ │
            │ │MS SQL Server │ │WORM Storage  │ │ Redis    │ │
            │ │(OLTP +       │ │(Azure Blob   │ │ (cache + │ │
            │ │ Always On AG)│ │ Immutable)   │ │ sessions)│ │
            │ └──────────────┘ └──────────────┘ └──────────┘ │
            │ ┌──────────────┐ ┌──────────────┐               │
            │ │Read Replica  │ │Elasticsearch │               │
            │ │(Reporting)   │ │(Search/Logs) │               │
            │ └──────────────┘ └──────────────┘               │
            └─────────────────────────────────────────────────┘
```

---

## 2. Logical Architecture

### 2.1 Layering Pattern

```
[Presentation Layer]  ← Web UI, API consumers
       ↓
[API Gateway Layer]   ← Auth, routing, rate limit
       ↓
[Application Services] ← Business logic per domain
       ↓
[Domain Layer]        ← Entities, value objects, rules
       ↓
[Infrastructure]      ← DB, external integrations, message bus
```

### 2.2 Service Communication

- **Synchronous:** REST (HTTP/JSON) — for query + critical writes
- **Asynchronous:** Event bus (RabbitMQ / Azure Service Bus) — for cross-service events, webhooks
- **Internal:** gRPC optional for high-throughput service-to-service

---

## 3. Microservices Breakdown

### 3.1 Service Inventory

| Service | Bounded Context | Database Schema | Key Endpoints |
|---|---|---|---|
| **Identity & Auth** | User/role/session/MFA/API keys | `sys.*` | `/auth/*`, `/users/*` |
| **Master Data** | Customer, vendor, product, CoA | `master.*` | `/customers`, `/vendors`, `/products` |
| **Sales** | Quotation, SO, DO, TI, CN, DN | `sales.*` | `/quotations`, `/tax-invoices` |
| **Purchase** | PR, PO, GR, Vendor Invoice, PV | `purchase.*` | `/vendor-invoices`, `/payment-vouchers` |
| **Tax** | VAT register, ภ.พ.30, ภ.ง.ด.3/53 | `tax.*` | `/reports/vat-output-register`, `/tax-returns/*` |
| **GL** | Journal entry, period close | `gl.*` | `/journal-entries`, `/periods` |
| **Reporting** | Financial statements, custom reports | (read replica) | `/reports/*` |
| **Numbering** | Sequence generation | `sys.number_sequences` | internal only |
| **e-Tax Signing** | XAdES signing, PFX/HSM access | (no DB) | internal only — gRPC |
| **e-Tax Submission** | Email delivery + RD ack tracking | `etax.*` | internal — `/etax/submit` |
| **RD API Connector** | ภ.พ.30 / ภ.ง.ด. submission | (uses tax tables) | internal |
| **Notification** | Email (transactional), in-app, webhook outbound | `notif.*` | internal |
| **Audit** | Activity log, system events | `audit.*` | `/audit/*` (read-only) |
| **Job Scheduler** | Cron jobs, queue workers | `jobs.*` | internal |

### 3.2 Why microservices, not monolith?

Trade-off analysis:

| Aspect | Monolith | Microservices (chosen) |
|---|---|---|
| Initial dev speed | ⚡ Fast | 🐢 Slower |
| Compliance isolation | ⚠ tax/audit code mixed | ✓ tax service isolated |
| Independent deploy | ❌ | ✓ deploy e-Tax svc independently |
| Scaling | ⚠ scale all together | ✓ scale tax/reporting separately |
| Team allocation | OK for 1-2 devs | OK for 3+ teams |
| Operational overhead | low | medium (need orchestration) |

→ **For this system: hybrid approach** — start with **modular monolith** (clear bounded contexts in one deployable), extract to microservices when team/scale grows. Each "service" above maps to a **module** in monolith Phase 1.

---

## 4. Data Layer

### 4.1 MS SQL Server Setup

**Topology:**

```
┌──────────────────────────────────────────────┐
│  Always On Availability Group (AG)           │
│                                              │
│  ┌───────────┐ sync  ┌───────────┐ async    │
│  │ Primary   │──────►│ Secondary │──────►   │
│  │ Replica   │       │ (same DC) │   ┌──────┴────┐
│  │ (R/W)     │       │ (Read-only│   │ DR Replica│
│  └─────┬─────┘       │  optional)│   │ (Different│
│        │             └───────────┘   │  region)  │
│        ▼                              └───────────┘
│  ┌───────────┐
│  │ Reporting │ (snapshot replication, daily)
│  │ Replica   │
│  │ (Read     │
│  │  Heavy)   │
│  └───────────┘
└──────────────────────────────────────────────┘
```

### 4.2 Schema Separation

ใช้ SQL Server schemas เพื่อ logical separation:

- `sys.*` — users, roles, permissions, sequences, system params
- `master.*` — companies, branches, customers, vendors, products, CoA
- `tax.*` — tax codes, rates, returns, registers
- `sales.*` — quotation, SO, DO, TI, CN, DN, receipts
- `purchase.*` — PR, PO, GR, vendor invoices, PV, WHT cert
- `gl.*` — journal entries, periods, balances
- `cash.*` — bank transactions, statements, petty cash
- `asset.*` — fixed assets, depreciation
- `etax.*` — e-Tax submissions, batch runs
- `audit.*` — activity log, login attempts, period close log
- `notif.*` — email queue, webhook queue
- `jobs.*` — scheduled jobs, executions

### 4.3 Row-Level Security (RLS)

```sql
-- Multi-tenant isolation per company_id
CREATE SECURITY POLICY sec.company_filter
ADD FILTER PREDICATE sys.fn_company_predicate(company_id) ON sales.tax_invoices,
ADD FILTER PREDICATE sys.fn_company_predicate(company_id) ON purchase.vendor_invoices,
...
WITH (STATE = ON);
```

Session context set by API Gateway after auth:

```sql
EXEC sp_set_session_context @key=N'company_id', @value=@CompanyId, @read_only=1;
EXEC sp_set_session_context @key=N'user_id',    @value=@UserId,    @read_only=1;
```

### 4.4 Indexing Strategy

- **Clustered:** primary key (IDENTITY) ส่วนใหญ่
- **Non-clustered:**
  - `(company_id, doc_date)` on transaction tables
  - `(customer_id, doc_date)` on AR docs
  - `(vat_period_year, vat_period_month)` on tax-affected docs
  - Covering indexes for hot reports

### 4.5 Encryption

- **TDE (Transparent Data Encryption)** — at rest
- **Always Encrypted** — for PII columns:
  - `customers.tax_id`
  - `vendors.tax_id`
  - `vendors.bank_account_no`
  - `users.mfa_secret`

---

## 5. Storage Strategy

### 5.1 Storage Tiers

| Storage | Purpose | Tech | Retention |
|---|---|---|---|
| **Hot OLTP** | Active transactions, < 90 days | MS SQL Server primary | live |
| **Warm OLTP** | 90 days - 2 years | MS SQL Server (same DB, partition older) | 2 years |
| **Cold Archive** | 2-5+ years | MS SQL Server archive DB + Blob | 5 years legal min |
| **WORM Archive** | e-Tax XML, signed docs | Azure Blob Storage (Immutable Storage) / S3 Object Lock | 5 years minimum, **non-modifiable** |
| **Cache** | Sessions, hot lookups | Redis | TTL-based |
| **Search** | Full-text, audit logs | Elasticsearch | 90 days hot, archive after |
| **Reports** | Pre-aggregated stats | MS SQL Server materialized views | refresh nightly |

### 5.2 Document Storage Layout

```
WORM-bucket/
├── company_id=1/
│   ├── tax_invoices/
│   │   ├── 2026/
│   │   │   ├── 05/
│   │   │   │   ├── 05-2026-TI-0001.xml
│   │   │   │   ├── 05-2026-TI-0001.xml.sig
│   │   │   │   ├── 05-2026-TI-0001.pdf
│   │   │   │   ├── 05-2026-TI-0001.ack.json
│   │   │   │   └── ...
│   ├── credit_notes/
│   ├── debit_notes/
│   └── ...
```

Path is deterministic from doc_no → easy to find  
Each set of files (XML + sig + PDF + ack) forms a complete "evidentiary bundle" for audit

### 5.3 Object Lock Rules

- All XML files: **immutable for 5 years 90 days** (buffer past minimum)
- All signed certificates: immutable until cert + 5 years past expiry
- Delete protection: cannot bypass even with admin credentials (compliance hold)

---

## 6. Authentication & Authorization

### 6.1 Identity Provider

- **Built-in** for Phase 1 (`sys.users` table)
- **SAML 2.0 / OIDC** federation optional for Phase 2 (corporate SSO)

### 6.2 Token Strategy

```
[Login] → username + password + MFA OTP
   ↓
[Issued] → JWT access token (15 min) + opaque refresh token (7 days)
   ↓
[API Call] → Bearer access token in Authorization header
   ↓
[Verify] → API Gateway verifies signature + checks revocation list
   ↓
[Set Context] → company_id, user_id, roles → DB session_context
```

### 6.3 RBAC Implementation

- Permissions defined in `sys.permissions` (granular: `module.resource.action`)
- Roles bundle permissions
- User-Role assignment per company per branch
- API Gateway checks permission before forwarding to service

### 6.4 API Key Authentication (External API)

- Format: `sk_live_xxxxx...` (long random)
- Hash stored (SHA-256), not plaintext
- HMAC signature header for request integrity:
  ```
  Authorization: ApiKey sk_live_xxx
  X-Signature: sha256=<HMAC of request body + timestamp>
  X-Timestamp: 1747276800
  ```
- Timestamp ±5 min skew tolerance (prevent replay)

---

## 7. API Gateway & Service Mesh

### 7.1 Gateway Responsibilities

- **TLS termination** (cert-manager + Let's Encrypt)
- **Request authentication** (JWT/API key validation)
- **Rate limiting** (per-user, per-API-key, per-IP)
- **Request routing** to backend services
- **CORS handling**
- **Logging + audit** (every request logged with trace ID)
- **Request transformation** (if needed, e.g. add tenant header)

### 7.2 Recommended Tech

- **Kong** (open source, plugin ecosystem)
- หรือ **Azure API Management** (if on Azure)
- หรือ **Traefik** (lightweight, Kubernetes-native)

### 7.3 Rate Limit Tiers

| Consumer Type | Limit |
|---|---|
| Web UI (user JWT) | 1,000 req/min |
| API Key — Standard | 100 req/min |
| API Key — Premium | 1,000 req/min |
| Internal services | 10,000 req/min |
| RD API connector | Per RD spec |

---

## 8. Background Jobs & Scheduler

### 8.1 Job Categories

| Category | Examples | Tech |
|---|---|---|
| **Recurring (cron-based)** | ภ.พ.30 auto-submit at 23:00, daily backup, deadline alerts | Quartz / Hangfire / cron + queue |
| **Event-driven** | e-Tax submission after TI post, webhook delivery | RabbitMQ / Azure Service Bus |
| **Heavy batch** | Year-end close, depreciation run, financial statement gen | Workers with checkpointing |
| **Retry queues** | Failed e-Tax retry, failed webhook retry | Dead-letter queue + exponential backoff |

### 8.2 Critical Scheduled Jobs

```
Job Name                       | Schedule                  | Action
-------------------------------|---------------------------|----------------------------
generate-monthly-pnd30-draft   | Day 1 of month, 08:00     | Generate draft ภ.พ.30
pnd30-deadline-alert           | Day 13, 14, 15 09:00      | Email accountant if not submitted
pnd30-auto-submit-safety       | Day 15, 23:00             | Auto-submit if Auto Mode + draft
pnd3-53-deadline-alert         | Day 5, 6, 7 09:00         | Email accountant
pnd3-53-auto-submit-safety     | Day 7, 23:00              | Auto-submit if Auto Mode
etax-retry-rejected            | Hourly                    | Retry rejected e-Tax submissions
webhook-retry                  | Every 5 min               | Retry failed webhooks
backup-incremental             | Every 15 min              | DB log backup
backup-full                    | Daily 02:00               | DB full backup
period-close-reminder          | Day 25 of month, 09:00    | Email accountant: close period
certificate-expiry-alert       | Daily 09:00               | Alert if CA cert expires < 30d
api-key-expiry-alert           | Daily 09:00               | Alert API key owners
```

### 8.3 Job Idempotency

- ทุก job ต้อง idempotent — run ซ้ำได้ผลเดิม
- Use job execution log table — check before running
- Distributed lock (Redis) prevent duplicate runs across workers

---

## 9. e-Tax Signing Service (Critical Component)

### 9.1 Service Boundary

แยกเป็น **microservice เฉพาะ** — เป็น **security boundary**

```
┌───────────────────────────────────────────┐
│ e-Tax Signing Service                     │
│ ─────────────────────────────────────────│
│ Input:  XML document (UBL 2.1 + RD ext)  │
│ Output: Signed XML (XAdES-BES)           │
│                                           │
│ Internal:                                 │
│ ├ Cert manager (load PFX, cache key)     │
│ ├ Canonicalization (C14N)                │
│ ├ Hash (SHA-256)                         │
│ ├ Sign (RSA / ECDSA depending on cert)   │
│ ├ Embed signature in <Signature> element │
│ └ Validate output                        │
│                                           │
│ Phase 1 (PFX):                           │
│ - Load PFX from /secrets/                │
│ - Cache decrypted key in memory          │
│                                           │
│ Phase 2 (HSM):                           │
│ - PKCS#11 / Azure KV API                 │
│ - Key never leaves HSM                   │
│ - Sign operation requested per call      │
└───────────────────────────────────────────┘
```

### 9.2 Library Choices

**Phase 1 (PFX-based):**

| Language | Library |
|---|---|
| .NET | `System.Security.Cryptography.Xml.SignedXml` + `BouncyCastle.NetCoreSdk` for XAdES |
| Java | `org.apache.santuario:xmlsec` + `org.bouncycastle:bcprov-jdk18on` |
| Node | `xml-crypto` + custom XAdES wrapper |

**Phase 2 (HSM):**
- Azure Key Vault SDK (Key Vault Crypto Client) — recommended for cloud
- PKCS#11 library if on-prem HSM

### 9.3 Sign Flow

```python
# Pseudocode
def sign_tax_invoice_xml(xml: str, cert: Certificate, key_ref) -> SignedXml:
    # 1. Canonicalize
    canonical = canonicalize_c14n(xml)
    
    # 2. Hash
    digest = sha256(canonical)
    
    # 3. Build XAdES <ds:Signature> structure
    sig_info = build_signed_info(digest, cert)
    
    # 4. Sign signed_info
    signature_value = key_ref.sign(sig_info)  # PFX or HSM
    
    # 5. Build XAdES qualifying properties
    qp = build_signed_properties(cert, signing_time)
    
    # 6. Embed signature in XML
    signed_xml = embed_signature(xml, sig_info, signature_value, cert, qp)
    
    # 7. Verify (sanity check)
    assert verify_xades(signed_xml)
    
    return signed_xml
```

### 9.4 Key Rotation

- CA cert expires → procurement of new cert 60 days before
- Deploy new cert path → graceful rollover (sign with new, verify with both for 30 days)
- Old cert kept for verification of past documents

---

## 10. RD Open API Integration

### 10.1 Service Boundary

```
┌──────────────────────────────────────────┐
│ RD API Connector Service                 │
│ ─────────────────────────────────────────│
│ - OAuth2 token mgmt (auto-refresh)       │
│ - PND.30 submit                          │
│ - PND.3/53 submit                        │
│ - Status polling                         │
│ - Retry with exponential backoff         │
│ - Circuit breaker pattern                │
└──────────────────────────────────────────┘
```

### 10.2 Workflow Example — ภ.พ.30 Submit

```
[Tax Service] requests submission
    ↓
[RD Connector] fetches access token (OAuth2 client_credentials)
    ↓
[RD Connector] builds JSON payload per RD spec
    ↓
[RD Connector] POST /openapi/vat/pp30/submit
    ↓
[Wait response (timeout 30s)]
    ↓
[Parse response]
    - 200 OK → record filing_reference, return SUCCESS
    - 4xx (validation error) → return ERROR + details (no retry)
    - 5xx (server error) → retry up to 5 times with backoff
    ↓
[Update tax_returns table]
```

### 10.3 Circuit Breaker

หาก RD API ล่ม (5xx > 50% in 1 min):
- Open circuit → ไม่เรียกอีก 5 นาที
- Half-open → ลอง 1 request
- ถ้าผ่าน → close circuit

### 10.4 Manual Mode Path (no API call)

```
[Tax Service] requested file generation
    ↓
[RD Connector] generates XML file per RD spec (no API call)
    ↓
[Store file in tmp storage with signed URL]
    ↓
[Return download URL to UI]
```

---

## 11. SMTP Email Service

### 11.1 Provider

- **Production:** Microsoft 365 SMTP (with rate limits) หรือ Amazon SES (reliable, ค่าใช้จ่ายต่ำ)
- **Backup:** SendGrid / Mailgun (failover)

### 11.2 Email Types

| Type | Volume Estimate | Priority |
|---|---|---|
| Tax Invoice delivery (with XML+PDF) | Per TI issued | Critical |
| Quotation/SO/DO PDF | Per doc | High |
| Receipt | Per payment | High |
| ภ.พ.30 deadline alerts | Monthly batch | Medium |
| User notifications | Various | Medium |
| Audit alerts | Rare | High |

### 11.3 Compliance Concerns

- **e-Tax email** must include cc to `csemail@rd.go.th` — system enforces
- Email log retained 5 years (compliance evidence)
- Bounce/failure tracking — alert accountant if customer email bounces
- DKIM + SPF + DMARC setup mandatory (RD spam filter strict)

---

## 12. External API Architecture

### 12.1 API Standards

- **REST** + JSON (primary)
- **OpenAPI 3.0** spec generated automatically
- **Postman collection** + **SDK** for popular langs (.NET, Node, Python)

### 12.2 Request Flow

```
[Consumer]
   ↓ POST /api/v1/tax-invoices + API Key + HMAC sig
[API Gateway]
   ↓ verify key + sig + rate limit + log
[Sales Service]
   ↓ validate payload
   ↓ check VAT_MODE
   ↓ call Numbering Service → get next number
   ↓ persist Draft
   ↓
[If "Post" action]
   ↓ Numbering Svc → assign number
   ↓ event: tax_invoice.posted (publish)
   ↓ async: e-Tax Signing Svc → sign XML
   ↓ async: Email Svc → send to customer + RD
   ↓ async: Webhook Svc → notify consumers
   ↓
[Return response with doc_no, status, urls]
```

### 12.3 Webhook Delivery

```
[Event emitted]
   ↓ Notification Service catches
   ↓ Lookup subscribers for this event type
   ↓ For each subscriber:
   ↓   - Build payload + HMAC sig
   ↓   - POST to webhook URL (timeout 10s)
   ↓   - If 2xx → mark delivered
   ↓   - If 4xx → mark failed (no retry — bad URL/payload)
   ↓   - If 5xx/timeout → enqueue retry
   ↓     • Attempt 1: 1 min later
   ↓     • Attempt 2: 5 min later
   ↓     • Attempt 3: 30 min later
   ↓     • After 3 fails → dead-letter + admin alert
```

---

## 13. Observability

### 13.1 Logging

- **Structured logs** (JSON) shipping to **Elasticsearch / Azure Monitor / Datadog**
- **Levels:** DEBUG (dev), INFO (default), WARN, ERROR, FATAL
- **Required fields:** timestamp, service, level, request_id, user_id, company_id, message
- **Sensitive fields masked:** passwords, tokens, full Tax IDs (last 4 digits only)
- **Retention:** 90 days hot, 1 year warm, then S3 archive

### 13.2 Metrics

- **Prometheus** + **Grafana** dashboard
- **Key metrics:**
  - Request latency p50/p95/p99 per endpoint
  - Error rate per service
  - e-Tax submission success rate
  - Background job duration + failure rate
  - Database connection pool usage
  - CA cert expiry days remaining
  - RD API circuit breaker state

### 13.3 Tracing

- **OpenTelemetry** + **Jaeger / Tempo**
- Trace ID propagated through all services
- Spans: Gateway → Service → DB → external API

### 13.4 Alerting (PagerDuty / Opsgenie)

| Alert | Severity |
|---|---|
| e-Tax submission failure rate > 5% in 5 min | P1 |
| ภ.พ.30 auto-submit failed | P1 |
| CA cert expires in 7 days | P2 |
| DB primary down | P1 |
| API latency p99 > 5s | P2 |
| Disk usage > 80% | P3 |

---

## 14. Security

### 14.1 Defense in Depth

| Layer | Control |
|---|---|
| **Network** | VPC + private subnets, WAF (rules: OWASP Top 10), DDoS protection |
| **Transport** | TLS 1.3 only, HSTS, cert pinning for RD API |
| **API Gateway** | Auth, rate limit, request validation |
| **Application** | Input validation, parameterized queries (no SQL injection), CSRF tokens |
| **Data** | TDE + Always Encrypted + RLS |
| **Secrets** | Azure Key Vault / HashiCorp Vault (no .env in repo!) |
| **Backups** | Encrypted, restricted access |
| **Audit** | Immutable activity log, ledger tables |

### 14.2 Threat Model (top concerns)

1. **Tax ID / financial data theft** → encryption + access control + audit
2. **Tax Invoice tampering** → XAdES signature + immutable WORM storage
3. **Insider fraud** → SoD + maker-checker + audit trail
4. **DDoS / API abuse** → rate limit + WAF
5. **Credential compromise** → MFA + short token TTL + revocation
6. **Supply chain (deps)** → SCA scanning (Snyk, Dependabot), SBOM
7. **e-Tax CA key theft** → HSM Phase 2; PFX with strong password Phase 1

### 14.3 Compliance Audits

- **ISO 27001** alignment
- **PDPA** compliance — DPA, consent log, DSR (Data Subject Rights) workflow
- **PCI DSS** (if processing card payments — out of scope initially)
- **External pen-test** annually

---

## 15. Deployment Topology

### 15.1 Recommended: Azure Cloud

```
┌─────────────────────────────────────────────────────────────┐
│ Azure Region: Southeast Asia (Singapore)                    │
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐│
│ │ AKS Cluster (Kubernetes)                                ││
│ │ ├ API Gateway (Kong/AGIC)                              ││
│ │ ├ Application Services (containerized)                 ││
│ │ ├ Job Workers                                          ││
│ │ └ HPA scaling rules                                    ││
│ └─────────────────────────────────────────────────────────┘│
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐│
│ │ Azure SQL Managed Instance (or SQL Server on Azure VM) ││
│ │ + Always On AG                                          ││
│ │ + TDE + Always Encrypted                                ││
│ └─────────────────────────────────────────────────────────┘│
│                                                             │
│ ┌─────────────────────────────────────────────────────────┐│
│ │ Azure Blob Storage (Immutable Storage policy)          ││
│ │ Azure Key Vault (secrets, certs)                       ││
│ │ Azure Cache for Redis                                  ││
│ │ Azure Service Bus (event bus)                          ││
│ │ Azure Monitor / App Insights / Log Analytics           ││
│ └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘

DR Region: Asia East (Hong Kong / Japan East)
  - Async DB replica
  - Standby blob replication
  - Failover automation (~1 hr RTO)
```

### 15.2 CI/CD Pipeline

```
[Git commit (feature branch)]
    ↓
[PR opened]
    ↓ ├ Unit tests
    ↓ ├ Integration tests
    ↓ ├ Lint + format check
    ↓ ├ Security scan (Snyk)
    ↓ └ Code review (2 approvals)
[Merge to main]
    ↓
[Build images + push to Registry]
    ↓
[Deploy to Staging] (automatic)
    ↓ ├ Smoke tests
    ↓ ├ E2E tests
    ↓ └ Manual QA approval
[Deploy to Production] (manual gate)
    ↓ ├ Blue/green or canary deployment
    ↓ ├ Post-deploy verification
    ↓ └ Rollback automation if errors spike
```

### 15.3 Environments

- **Dev** — developer sandbox, mock RD API
- **QA** — integration testing, RD UAT sandbox
- **Staging** — production-mirror, RD UAT
- **Production** — live, RD production API

---

## 16. Disaster Recovery & Backup

### 16.1 Backup Strategy

| Asset | Frequency | Retention | Storage |
|---|---|---|---|
| Full DB | Daily 02:00 | 30 days | Azure Backup |
| Transaction log | Every 15 min | 7 days | Azure Backup |
| Yearly DB export | Year-end | 10 years (legal) | Azure Cool Blob |
| WORM e-Tax docs | Continuous | 5 years immutable | Already immutable |
| Configuration (env) | On change | All versions | Git (private) |
| Secrets | Versioned | All versions | Azure Key Vault |

### 16.2 RTO / RPO

- **RPO (Recovery Point Objective):** ≤ 5 min (sync replica)
- **RTO (Recovery Time Objective):** ≤ 1 hour
- **DR drill:** Quarterly, document outcome

### 16.3 Restore Procedures

ทดสอบ restore monthly จาก backup ไป staging — ถ้าคืนไม่ได้ = backup ไม่มีประโยชน์

---

## 17. Performance Targets

| Operation | Target |
|---|---|
| Login | < 500 ms |
| Document list (50 rows) | < 1 s |
| Document detail | < 500 ms |
| Document save (Draft) | < 1 s |
| Tax Invoice post (with sign + email) | < 3 s |
| Quotation PDF generation | < 2 s |
| ภ.พ.30 preview (single month) | < 5 s |
| Year-end close (10M GL lines) | < 30 min |
| External API throughput | ≥ 100 req/sec sustained |

### 17.1 Capacity Planning (Phase 1)

- ~1,000 TI/day estimate
- ~30k TI/month
- ~360k TI/year
- DB growth: ~50 GB/year
- Plan to scale: 10x in 3 years

---

## 18. Configuration Management (.env)

### 18.1 Hierarchy

```
.env.defaults      (committed — default values)
.env.{environment} (committed — dev/staging/production overrides)
.env.local         (gitignored — developer local overrides)
+ Secrets fetched from Key Vault at boot
```

### 18.2 Validation at Boot

ระบบ validate .env ตอน startup:
- ทุก required var มีค่า
- Format check (numbers, dates, URLs)
- Cross-dependency check (ถ้า PND30_SUBMISSION_MODE=auto → RD_API_CLIENT_ID required)
- Fail-fast if invalid (don't start app with broken config)

### 18.3 Tax Rate Change Procedure

```bash
# 1. PM/CFO approves rate change
# 2. DevOps creates PR updating .env.production
VAT_RATE=0.10
VAT_EFFECTIVE_FROM=2027-01-01

# 3. PR reviewed + approved
# 4. Schedule deploy near midnight 2026-12-31
# 5. Deploy
# 6. System auto-inserts new tax_rates row with effective_from
# 7. New transactions use new rate; old transactions unaffected
```

---

## 19. Migration & Phase 2 (H2H Upgrade)

### 19.1 Trigger Conditions

Upgrade Phase 1 (Email) → Phase 2 (H2H) เมื่อ:
- รายได้ > 30 ล้านบาท/ปี (RD bumps you out of Email tier)
- Volume > 100 TI/day (Email submission becomes bottleneck)
- Integration with B2B partners requiring H2H

### 19.2 Migration Steps

1. **Procure HSM** (Azure Key Vault Managed HSM แนะนำ — ~5-15k/เดือน)
2. **Re-key:** export cert from PFX, import to HSM (work with CA provider)
3. **Update Signing Service config:** switch from PFX to HSM provider
4. **Add RD H2H connector:** new microservice for SFTP/REST submission
5. **Register with RD as H2H Service Provider**
6. **Test in RD UAT** environment for 2-3 weeks
7. **Migrate one document type at a time** (gradual rollover)
8. **Switch all by VAT period change** (e.g., from period 2027-04 onwards)

### 19.3 No Schema Change Required

Schema designed in v1.0+ supports both:
- `etax.submissions.submission_method` ('EMAIL' / 'H2H_SFTP' / 'H2H_REST')
- `tax.digital_certificates.key_vault_ref` รองรับ HSM
- `etax.batch_runs` table มีอยู่แล้วสำหรับ H2H batch

---

## 20. Tech Stack Recommendation

### 20.1 Core

| Layer | Technology | Rationale |
|---|---|---|
| **Backend** | .NET 8 (C#) | First-class XAdES library (SignedXml), strong typing, mature ecosystem |
| **Frontend** | Next.js 15 (React + TypeScript) | SSR, good Thai font support, large talent pool |
| **DB** | MS SQL Server 2022 | Required by user, RLS, Always Encrypted |
| **Cache** | Redis 7 | Sessions, rate limits, distributed locks |
| **Event Bus** | Azure Service Bus หรือ RabbitMQ | Reliable, ordered, dead-letter |
| **Search/Logs** | OpenSearch / Elasticsearch | Audit log search, full-text |
| **Container** | Docker + Kubernetes (AKS) | Standard orchestration |
| **CI/CD** | GitHub Actions / Azure DevOps | Standard |
| **Monitoring** | Azure Monitor + Application Insights + Grafana | Standard observability |
| **API Gateway** | Kong / Azure API Mgmt | Mature, plugin rich |

### 20.2 Alternative Stack (if not .NET)

- **Backend:** Java 21 (Spring Boot) — XAdES via Apache Santuario + BouncyCastle
- **Backend:** Node.js (NestJS) — more dev work for XAdES but workable

→ ซานะแนะนำ **.NET 8** เพราะ:
1. XAdES signing native + reliable
2. MS SQL Server native support (best ORM Entity Framework / Dapper)
3. Enterprise features (background services, hosted services) มี out-of-box
4. Performance ดี
5. Talent pool ในไทยกว้าง

---

**— END OF ARCHITECTURE DESIGN —**

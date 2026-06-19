# e-Tax + Tax Filing — Environment Tiers (Test → UAT → Production)

**Date:** 2026-05-18
**Owner:** Sana → Claude Code (when Sprint 13c lands)
**Goal:** Swap from Tier 1 (local mock, no cert) → Tier 2 (real cert + RD UAT) → Tier 3 (production) via **config change only** — zero code edits per environment.

---

## 1. Audit — current state assessment (post Sprint 11)

| Component | File | Test→Prod-swap readiness | Notes |
|---|---|---|---|
| **`IETaxSigner`** interface | `Application/Abstractions/IETaxGateway.cs` | ✅ Ready | Clean abstraction; HSM impl can swap via DI |
| **`ETaxSigner`** PFX impl | `Infrastructure/ETax/ETaxSigner.cs` | ⚠ Present but **inert** | Loads PFX from config; fail-fast if missing. **No cert wired today** and `ETaxBehaviorOptions.Enabled = false`, so it never signs at runtime — code is swap-ready (production-safe shape) but not on a live path until a real PFX + `ETax:Enabled = true` are configured (plan.md §8). |
| **`XadesBesSigner`** pure logic | `Infrastructure/ETax/ETaxSigner.cs` | ✅ Ready | No IO/config; works with self-signed or CA cert identically |
| **`IETaxEmailSender`** interface | `Application/Abstractions/IETaxGateway.cs` | ✅ Ready | Clean abstraction |
| **`ETaxEmailSender`** MailKit impl | `Infrastructure/ETax/ETaxEmailSender.cs` | ✅ Ready | Supports plain SMTP (Tier 1 MailHog port 1025) + StartTLS auth (Tier 3) |
| **`IETaxXmlBuilder`** interface | `Application/Abstractions/IETaxGateway.cs` | ✅ Ready | Concrete impl is `ETaxXmlBuilder.cs` |
| **`ETaxBehaviorOptions`** kill switch | `Infrastructure/ETax/ETaxBehaviorOptions.cs` | ✅ Ready | Default `Enabled=false`; opt-in per env |
| **PFX path defensive check** | `ETaxSigner.SignAsync` | ✅ Ready | Throws clear `DomainException` if PFX missing (correct fail-fast shape) — but note the signer is inert by default (`Enabled = false`), so this guard only fires once e-Tax is switched on with a cert configured. |
| **`IFileStorageService`** (Sprint 11) | `Infrastructure/Storage/...` | ✅ Ready | LocalDisk Phase 1, Blob/S3 Phase 2 via DI swap |
| **`GlAccountsOptions`** | `appsettings` `GlAccounts` section | ✅ Ready | All GL codes via config |
| **RD Open API client** (for ภ.พ.30 auto-submit) | ❌ doesn't exist | ❌ **Gap** | Sprint 9 implemented Manual mode only; Auto-submit stubbed |
| **XSD schema validation** | ❌ doesn't exist | ❌ **Gap** | Signed XML goes to email without local schema validation; ETDA mกค.14-2563 XSD not in repo |
| **e-Tax submission audit trail** (`etax_submissions` table) | ❌ doesn't exist | ❌ **Gap** | Send result returned ephemerally, not persisted; cannot reconstruct history |
| **RD email dedup config** | `ETax:Email:RdCcAddress` | ✅ **Resolved (Sprint 13c)** | `Tax:EtaxDeliveryEmailCc` + `ETaxBehaviorOptions.RdCcAddress` deleted; single-source `ETax:Email:RdCcAddress` (grep-clean, plan.md §23.11). |
| **e-Tax enable switch** | `ETax:Enabled` + `ETax:AutoSendOnTaxInvoicePost` | ✅ **Resolved (Sprint 13c)** | `Tax:EtaxEnabled` deleted; two-tier remains (master capability + per-trigger). |
| **Customer-email override** (prevent accidental sends in Tier 2) | ❌ doesn't exist | ❌ **Gap** | UAT shouldn't email real customers — need redirect/whitelist |
| **Retry queue / dead-letter** for failed sends | ❌ doesn't exist | ❌ **Gap** | One-shot send + log; no automated retry per plan §13.1.2 intent |
| **HSM adapter implementation** | ❌ doesn't exist (only PFX impl) | ⚠ **Phase 2** | Interface allows; concrete `HsmETaxSigner` is Phase 2 work |
| **Receipt e-Tax** (`e-Receipt`) | ❌ doesn't exist | ❌ **Gap** | Plan §13 covers Receipt e-Tax; only TI signing built today |
| **Tier 1 dev infrastructure** (MailHog + MockServer) | ⚠ Partial | ⚠ **Gap** | appsettings.Development uses port 1025 (MailHog ready) but no Docker-compose set up; MockServer absent |
| **Self-signed test cert generator script** | ❌ doesn't exist | ❌ **Gap** | Devs must `openssl req -x509` manually; one-line script needed |

**Summary (original audit, post Sprint 11):** 11 ✅ ready, 4 ⚠ minor drift/duplication, 8 ❌ gaps.
**Status update (post Sprint 13c, 2026-05-18):** the two ⚠ config-duplication rows are **resolved**
(see updated rows above) and the §3 Tier-2 gaps shipped — see plan.md §23.11. Remaining caveat: the
`ETaxSigner` is present but **inert by default** (no cert wired, `Enabled = false`), so the e-Tax path is
config-gated, not live. Foundation strong (abstractions correct); the live RD path stays off until Ham orders.

---

## 2. Drift / duplication clean-up (Tier-0 housekeeping) — ✅ SHIPPED (Sprint 13c, 2026-05-18)

> **Status:** both clean-ups below are DONE (plan.md §23.11): `Tax:EtaxDeliveryEmailCc`,
> `ETaxBehaviorOptions.RdCcAddress` and `Tax:EtaxEnabled` were deleted (grep-clean); single-source
> `ETax:Email:RdCcAddress` + two-tier `ETax:Enabled`/`ETax:AutoSendOnTaxInvoicePost` remain. The
> diffs below are retained as the historical record of what was consolidated.

Two config-key duplications were consolidated to single source before Tier 2/3:

### 2.1 Email CC for RD

```diff
- Tax:EtaxDeliveryEmailCc       = "csemail@rd.go.th"   (legacy, used in...?)
- ETax:RdCcAddress              = "csemail@rd.go.th"   (used by ETaxEmailSender)
+ ETax:RdCcAddress              = "csemail@rd.go.th"   (single source)
```

`ETaxEmailSender` reads from `ETaxEmailOptions.RdCcAddress`. `ETaxBehaviorOptions` also has it (redundant — never read?). `Tax:EtaxDeliveryEmailCc` appears legacy.

**Refactor (small):** delete `Tax:EtaxDeliveryEmailCc` + `ETaxBehaviorOptions.RdCcAddress`. Keep `ETax:Email:RdCcAddress` only. Audit usage during Sprint 13c P1.

### 2.2 e-Tax enable flags

Currently three:
- `Tax:EtaxEnabled` — legacy, set by `Tax` section
- `ETax:Enabled` — master switch
- `ETax:AutoSendOnTaxInvoicePost` — granular trigger

**Refactor:** delete `Tax:EtaxEnabled`. Two-tier remains:
- `ETax:Enabled` — overall capability
- `ETax:AutoSendOnTaxInvoicePost` — granular per-trigger

---

## 3. Gap fixes needed for Tier-2 readiness (Sprint 13c scope)

### 3.1 e-Tax submission audit trail

New entity to persist every signing/sending attempt:

```
etax.submissions
  submission_id      BIGINT IDENTITY PK
  company_id         INT NN
  tax_invoice_id     BIGINT NN FK sales.tax_invoices
  attempted_at       TIMESTAMPTZ NN
  attempt_no         INT NN              -- 1, 2, 3 for retries
  outcome            ENUM   SignedOk | SendOk | SendFailed | RejectedByRd | NotApplicable
  xml_sha256         VARCHAR(64) NULL    -- present once signed
  signed_xml_path    VARCHAR NULL        -- storage path via IFileStorageService
  pdf_path           VARCHAR NULL
  email_message_id   VARCHAR NULL
  to_email_snapshot  VARCHAR NN          -- the address sent to (after redirect resolved)
  cc_email_snapshot  VARCHAR NULL
  smtp_response      VARCHAR NULL        -- success or error
  rd_ack_ref         VARCHAR NULL        -- Phase 2 when RD ack pipeline lands
  notes              VARCHAR NULL
  retry_after        TIMESTAMPTZ NULL    -- backoff
  ITenantOwned, append-only via trigger
```

Every TaxInvoiceService.PostAsync that triggers e-Tax → write a row. Every retry → new row with incremented `attempt_no`. Audit trail forever (legal req 5+ years per ม.10).

### 3.2 Customer-email redirect / whitelist (Tier 2 safety)

```csharp
public sealed class ETaxEmailOptions
{
    // ... existing fields ...

    // NEW (Tier 2 safety):
    public string? RedirectAllToEmail { get; init; }     // when set, ALL emails go here instead of customer + RD
                                                          // (UAT-only — production = null/empty)
    public string[]? WhitelistDomains { get; init; }     // when set, only emails matching these domains pass through
}
```

**Behavior:**
- Tier 1 (local dev): `RedirectAllToEmail = "dev-inbox@localhost"` → MailHog captures all
- Tier 2 (UAT): `RedirectAllToEmail = "uat-inbox@your-company.com"` → real send but to UAT mailbox, NOT customers
- Tier 3 (prod): `RedirectAllToEmail = null` → real customer + cc csemail@rd.go.th

This is the **most important safety mechanism for Tier 2** — accidental customer-facing sends during UAT testing = legal exposure.

### 3.3 XSD validation step

Pre-send local validation:
- Download ETDA standard มกค.14-2563 XSD set → store in `code/etax-schemas/`
- New service: `IETaxXmlValidator.ValidateAsync(signedXml, ct)` → returns list of violations
- ETaxSigner pipeline: build → sign → **validate** → email
- Tier 1 reasonable to skip if XSD download fails (test cert never matches RD chain anyway); Tier 2/3 mandatory

### 3.4 RD Open API client for tax filings

```csharp
public interface IRdEfilingClient
{
    Task<RdSubmissionResult> SubmitPnd30Async(byte[] payload, int companyId, int period, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd3Async(byte[] payload, ..., CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd53Async(...);
    Task<RdSubmissionResult> SubmitPnd54Async(...);
    Task<RdSubmissionResult> SubmitPnd36Async(...);
    Task<RdSubmissionStatus> GetStatusAsync(string submissionId, CancellationToken ct);
}

// Tier 1 impl: stub returning canned success/ack
// Tier 2/3 impl: real HTTP to efiling.rd.go.th/api/v1/... per RD API spec
```

DI-registered based on `RdApi:Provider` config:
- `"Mock"` (Tier 1) → `MockRdEfilingClient` returning fixed responses
- `"RdProduction"` (Tier 2/3) → `RdHttpEfilingClient` with real OAuth + endpoint

### 3.5 Retry queue + dead-letter

Currently `ETaxEmailSender.SendAsync` returns `ETaxDeliveryResult { Delivered=false, Error=ex.Message }` — but nothing schedules a retry.

**Refactor:** Add `IETaxSubmissionQueue` (in-memory for Tier 1, durable for Tier 2/3 — could use Postgres queue table or external like Hangfire/Quartz).

Behavior:
- TaxInvoiceService.PostAsync → enqueue → background worker dequeues + signs + sends
- On failure: exponential backoff (1m, 5m, 15m, 1h, 4h, 24h), then dead-letter
- Dead-letter visible in UI for manual intervention

Defer queue infrastructure to Phase 2 if scope tight — Tier 1 can be in-process best-effort + persist failure in etax_submissions for manual visibility.

### 3.6 Self-signed test cert generator script

`dev-tools/gen-test-cert.sh`:
```bash
#!/usr/bin/env bash
# Generate dev PFX with self-signed cert for XAdES testing.
# NOT for production. Subject mirrors expected Thai company cert format.

PASSWORD="${1:-DevPfxPassword}"
OUT="${2:-./dev-cert.pfx}"

openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout /tmp/dev-key.pem \
  -out    /tmp/dev-cert.pem \
  -subj "/C=TH/O=TEAS Dev Company/CN=teas-dev/serialNumber=0123456789012"

openssl pkcs12 -export \
  -out "$OUT" \
  -inkey /tmp/dev-key.pem \
  -in    /tmp/dev-cert.pem \
  -password "pass:$PASSWORD" \
  -name "TEAS Dev Signing Key"

rm -f /tmp/dev-key.pem /tmp/dev-cert.pem
echo "Generated: $OUT (password: $PASSWORD)"
```

Use:
```bash
./dev-tools/gen-test-cert.sh dev123 backend/secrets/dev-cert.pfx
# Then set ETax:Signing:PfxPath = "secrets/dev-cert.pfx" + PfxPassword = "dev123"
```

`.gitignore` rule: `secrets/*.pfx` (never commit certs).

### 3.7 Tier 1 docker-compose for full local stack

`docker-compose.dev.yml` additions:
```yaml
services:
  mailhog:
    image: mailhog/mailhog:latest
    ports:
      - "1025:1025"   # SMTP
      - "8025:8025"   # Web UI
    profiles: ["dev"]

  mockserver:
    image: mockserver/mockserver:latest
    ports:
      - "1080:1080"
    environment:
      MOCKSERVER_INITIALIZATION_JSON_PATH: "/config/initializerJson.json"
    volumes:
      - ./dev-tools/mockserver:/config
    profiles: ["dev"]
```

Init JSON: pre-canned RD API responses for Mock provider.

### 3.8 Receipt e-Tax (`e-Receipt`) — Phase 2 deferral

Plan §13 covers `e-Receipt` (signed XML for Receipts, parallel to e-TaxInvoice). Currently only TI signing wired. Per Phase 1 plan, e-Receipt = Phase 2 candidate when TI-side stable in production. Document explicitly.

---

## 4. Config layout — single canonical structure (post-cleanup)

```jsonc
"Tax": {
  "VatMode": true,           // VAT-registered company?
  "VatRate": 0.07,
  "VatEffectiveFrom": "...",
  "VatRounding": "HALF_UP",
  "VatDecimalPlaces": 2,
  "Pnd30SubmissionMode": "manual",
  "NonVatDocLabelTh": "...", "NonVatDocLabelEn": "..."
  // EtaxEnabled + EtaxDeliveryEmailCc REMOVED — now under ETax section
},

"ETax": {
  "Enabled": false,                          // master kill switch
  "AutoSendOnTaxInvoicePost": false,         // trigger per-event
  "Signing": {
    "PfxPath": "secrets/dev-cert.pfx",       // Tier 1: dev cert; Tier 3: CA-issued cert path
    "PfxPassword": "dev123"                  // Tier 3: from secret manager (Azure Key Vault etc.)
    // Phase 2: "HsmProvider": "AzureKeyVault", "HsmKeyName": "..."
  },
  "Email": {
    "Host": "localhost",                     // Tier 1: localhost (MailHog); Tier 3: real SMTP relay
    "Port": 1025,                            // Tier 1: 1025; Tier 3: 587 or 465
    "User": "", "Password": "",              // Tier 1: empty; Tier 3: SMTP creds
    "UseSsl": false,                         // Tier 1: false; Tier 3: true
    "From": "noreply@teas.local",            // Tier 3: noreply@your-company.com
    "RdCcAddress": "csemail@rd.go.th",       // RD's required CC — same value all tiers
    "RedirectAllToEmail": "dev-inbox@localhost",  // Tier 1: anywhere captured; Tier 2: UAT mailbox; Tier 3: null (real)
    "WhitelistDomains": null                 // Tier 2 alt safety; Tier 1/3: null
  },
  "Validation": {
    "XsdSchemaDir": "etax-schemas/",         // local XSD set
    "RequireSchemaPass": false               // Tier 1: skip ok; Tier 2/3: true
  },
  "Submission": {
    "RetryAttempts": 6,
    "BackoffSchedule": ["1m", "5m", "15m", "1h", "4h", "24h"]
  }
},

"RdApi": {
  "Provider": "Mock",                        // Tier 1: "Mock"; Tier 2: "RdUat"; Tier 3: "RdProduction"
  "BaseUrl": "http://localhost:1080",        // Tier 1: MockServer; Tier 2: efilinguat.rd.go.th (verify); Tier 3: efiling.rd.go.th
  "ServiceProviderId": "",                   // assigned by RD after Service Provider registration (Phase 0)
  "ApiKey": "",                              // from secret manager
  "TimeoutSeconds": 30
}
```

---

## 5. Tier transitions — operational runbook

### Tier 1 → Tier 2

**Prerequisites:**
- [ ] Company VAT-registered (ภ.พ.01)
- [ ] Digital Cert Class 2 purchased from CA (TDID/INET/CAT/NRCA) — lead time ~1 wk
- [ ] Registered with RD as Service Provider — Direct Filing — lead time 4-6 wk
- [ ] RD UAT credentials provisioned
- [ ] UAT mailbox configured (your-company-uat@your-company.com)

**Config changes (no code edit):**
1. `ETax:Signing:PfxPath` → path to CA-issued PFX (move out of repo to secret store)
2. `ETax:Signing:PfxPassword` → real password from secret store
3. `ETax:Email:Host/Port/User/Password/UseSsl` → real SMTP relay
4. `ETax:Email:From` → real corporate sender
5. `ETax:Email:RedirectAllToEmail` → UAT mailbox (CRITICAL — prevents customer emails during UAT)
6. `ETax:Validation:RequireSchemaPass` → `true`
7. `RdApi:Provider` → `"RdUat"`
8. `RdApi:BaseUrl` → RD UAT base URL
9. `RdApi:ServiceProviderId` → assigned ID
10. `RdApi:ApiKey` → from secret store

**Verification:**
- Send test TI → check UAT mailbox received → forward to RD UAT support for validation
- ภ.พ.30 preview/finalize via Mock then RdUat → compare outputs
- Audit log shows every attempt with outcome

### Tier 2 → Tier 3 (production cutover)

**Prerequisites:**
- [ ] UAT certified by RD
- [ ] External pen-test passed (no critical findings)
- [ ] Go-live checklist (`docs/test/09-go-live-checklist.md`) 100% checked
- [ ] DR runbook validated

**Config changes (no code edit):**
1. `ETax:Email:RedirectAllToEmail` → `null` (now sends to real customer)
2. `ETax:Email:Host/From` → production SMTP relay + sender
3. `RdApi:Provider` → `"RdProduction"`
4. `RdApi:BaseUrl` → `https://efiling.rd.go.th/api/v1` (verify exact endpoint)
5. (Other config typically unchanged from Tier 2 — UAT certs may even be reused if RD allows; otherwise swap)

**Verification:**
- Issue 1 test TI to internal test customer → real RD acks
- Watch audit log for first week intensively
- Daily ภ.พ.30 + ภ.ง.ด.x preview reconciliation

### Production rollback safety

If any failure → flip:
- `ETax:AutoSendOnTaxInvoicePost = false` → stops auto-send (TI POST still works, just no e-Tax)
- `Tax:Pnd30SubmissionMode = "manual"` → stops auto-submit, fall back to download+upload

Rollback = config-only. No code revert needed.

---

## 6. Tier 1 → 2 → 3 — config matrix

| Setting | Tier 1 (dev mock) | Tier 2 (UAT) | Tier 3 (production) |
|---|---|---|---|
| `ETax:Enabled` | true or false (per dev preference) | **true** | **true** |
| `ETax:AutoSendOnTaxInvoicePost` | false (manual testing) | **true** | **true** |
| `ETax:Signing:PfxPath` | `secrets/dev-cert.pfx` | CA-issued path | CA-issued path |
| `ETax:Signing:PfxPassword` | `dev123` | secret-store | secret-store |
| `ETax:Email:Host` | `localhost` | real SMTP | real SMTP |
| `ETax:Email:Port` | `1025` | `587` or `465` | `587` or `465` |
| `ETax:Email:UseSsl` | `false` | `true` | `true` |
| `ETax:Email:From` | `noreply@teas.local` | real corp sender | real corp sender |
| `ETax:Email:RedirectAllToEmail` | `dev@localhost` | **UAT mailbox** | `null` (real customer) |
| `ETax:Validation:RequireSchemaPass` | `false` | **true** | **true** |
| `RdApi:Provider` | `"Mock"` | `"RdUat"` | `"RdProduction"` |
| `RdApi:BaseUrl` | `http://localhost:1080` | RD UAT URL | RD prod URL |
| `RdApi:ServiceProviderId` | empty | assigned UAT | assigned prod |
| `RdApi:ApiKey` | empty | UAT key | prod key |
| `Tax:Pnd30SubmissionMode` | `"manual"` | `"manual"` then `"auto"` | `"auto"` (after UAT cert) |

---

## 7. Sprint 13c scope (proposed — replaces previous "e-Tax wiring" sketch)

**Title:** e-Tax pipeline production-readiness + Tier 1 mock infrastructure

**Scope (per gaps in §3):**
1. Config cleanup (§2) — remove `Tax:EtaxEnabled`, `Tax:EtaxDeliveryEmailCc`, `ETaxBehaviorOptions.RdCcAddress` duplicates
2. `etax.submissions` entity + persistence + audit-append-only trigger
3. `ETaxEmailOptions.RedirectAllToEmail` + behavior (CRITICAL safety)
4. `ETaxEmailOptions.WhitelistDomains` alternative safety
5. `IETaxXmlValidator` interface + LocalXsdValidator impl + `etax-schemas/` checked in
6. `IRdEfilingClient` interface + `MockRdEfilingClient` (Tier 1) + `RdHttpEfilingClient` skeleton (Tier 2/3 — empty methods + Bearer auth — actual wiring deferred until real RD credentials available)
7. `IETaxSubmissionQueue` minimal: in-process best-effort + persist failure for manual visibility (full retry queue Phase 2)
8. `dev-tools/gen-test-cert.sh`
9. `docker-compose.dev.yml` — MailHog + MockServer additions
10. `dev-tools/mockserver/initializerJson.json` — canned RD API responses
11. Documentation: this file (`docs/etax-environment-tiers.md`) + runbook update in CLAUDE.md or new `docs/ops/etax-runbook.md`
12. `TaxInvoiceService.PostAsync` integration — replace direct `ETaxSigner+Sender` call with submission queue + audit row write

**Estimate:** ~4-5 days human-equivalent. Single phase.

**Tests:**
- Unit: redirect logic, whitelist matching, retry backoff math
- Integration: TI POST with ETax:Enabled=true + MailHog → email captured + audit row inserted
- Integration: send failure → audit row with `outcome=SendFailed`
- Integration: XSD validator catches invalid XML
- Integration: MockRdEfilingClient returns expected canned response
- E2e: 1 new spec `etax-pipeline-mock.spec.ts` — full TI POST → MailHog Web UI → assert email present

---

## 8. Out of scope (Sprint 13c)

- ❌ HSM impl (`HsmETaxSigner`) — Phase 2 when first customer needs HSM
- ❌ Full durable retry queue (Hangfire/Quartz) — Phase 2 when load demands
- ❌ Real RD UAT integration — blocked on Service Provider registration (Phase 0)
- ❌ e-Receipt signing (Receipt-side) — Phase 2 per plan §13
- ❌ XML schema auto-update from ETDA — manual schema refresh
- ❌ Dead-letter UI — Phase 2 (visible via SQL query for now)

---

## 9. Living document

This file updates whenever:
- A tier transition is performed (record date + observations)
- RD changes API spec or endpoint
- New cert is provisioned
- A bug surfaces that affects swap-readiness

Cross-references:
- `accounting-system-plan.md` §13 (e-Tax design) + §22 (Phase 0 prerequisites)
- `etax-xades-spec.md` (XAdES signing details — single source of truth)
- `runtime-gotchas.md` (any e-Tax-specific catches)
- `test/04-compliance-test.md` §4 (e-Tax compliance assertions)
- `test/09-go-live-checklist.md` §3-4 (security + tax readiness gates)

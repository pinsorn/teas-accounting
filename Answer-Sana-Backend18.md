# Answer-Sana-Backend18 — Sprint 13c: e-Tax Production-Readiness + Tier 1 Mock Infrastructure

**Date:** 2026-05-18
**From:** Ham (via Sana, Cowork)
**To:** Claude Code
**Re:** Close all gaps for clean Tier 1 → 2 → 3 swap via config-only (no code edit per environment)
**Gate:** **Single-phase sprint ~4-5 days. Comprehensive audit done; gaps identified; refactor scope locked.**
**Prereq:** Sprint 12 (Internal PO) must ship + Report-Backend17 land. Sprint 13c is independent of Sprint 13b (User Manual generator).

> **Read first:** `docs/etax-environment-tiers.md` — full audit + tier matrix + runbook.
> This spec implements the 8 gaps identified there. Don't duplicate that doc;
> reference it for context.

---

## 0. Pre-spec audit (Sana — done in advance via dedicated audit doc)

| Check | Result | Sprint 13c action |
|---|---|---|
| `IETaxSigner` + `XadesBesSigner` | ✅ Already clean abstraction | NO change — keep as-is |
| `IETaxEmailSender` + MailKit impl | ✅ Plain SMTP + StartTLS swap-ready | Add fields to `ETaxEmailOptions` (RedirectAllToEmail, WhitelistDomains) |
| `IFileStorageService` (Sprint 11) | ✅ Same pattern, use for signed XML + PDF storage | Reuse — store signed XML + PDF via this |
| `ETaxBehaviorOptions.Enabled = false` default | ✅ Inert-by-default | NO change |
| PFX defensive check | ✅ Fail-fast pattern | NO change |
| `Tax:EtaxEnabled` + `Tax:EtaxDeliveryEmailCc` legacy keys | ⚠ Duplicate of `ETax:*` | **DELETE — Sprint 13c P1** |
| `ETaxBehaviorOptions.RdCcAddress` duplicate | ⚠ 3rd copy of csemail | **DELETE — keep only `ETax:Email:RdCcAddress`** |
| `etax.submissions` audit table | ❌ doesn't exist | **BUILD** |
| `RedirectAllToEmail` safety | ❌ doesn't exist | **BUILD — CRITICAL for Tier 2** |
| `IETaxXmlValidator` (XSD) | ❌ doesn't exist | **BUILD** |
| `IRdEfilingClient` (RD Open API) | ❌ doesn't exist | **BUILD interface + Mock impl + RealHttp skeleton** |
| Retry queue | ❌ doesn't exist | **BUILD minimal in-process best-effort** (full Phase 2) |
| Dev cert generator script | ❌ doesn't exist | **BUILD** |
| docker-compose.dev.yml MailHog + MockServer | ❌ doesn't exist | **BUILD** |
| e-Receipt signing | ❌ doesn't exist | **OUT OF SCOPE — Phase 2** |

---

## 1. Phasing (single phase but ordered execution)

| Step | Theme | Estimate |
|---|---|---|
| P1 | Config cleanup (drift removal) | ~0.5 d |
| P2 | `etax.submissions` audit entity + persistence | ~0.5 d |
| P3 | Email safety: `RedirectAllToEmail` + whitelist | ~0.5 d |
| P4 | XSD validator | ~0.5 d |
| P5 | `IRdEfilingClient` + Mock + Real skeleton | ~1 d |
| P6 | In-process submission queue + retry skeleton | ~0.5 d |
| P7 | Dev tools (gen-test-cert.sh + docker-compose.dev.yml + MockServer init JSON) | ~0.5 d |
| P8 | Documentation update + integration tests + e2e + gate | ~1 d |

Single phase — but each step has its own commit so revert is granular.

---

## 2. P1 — Config cleanup (drift removal)

**Delete:**
- `Tax:EtaxEnabled` (in `appsettings.json` + `appsettings.Development.json`)
- `Tax:EtaxDeliveryEmailCc` (same files)
- `ETaxBehaviorOptions.RdCcAddress` property (in `ETaxBehaviorOptions.cs`)

**Audit + replace usages:**
- `Tax:EtaxEnabled` → `ETax:Enabled`
- `Tax:EtaxDeliveryEmailCc` → `ETax:Email:RdCcAddress`
- `ETaxBehaviorOptions.RdCcAddress` (if read anywhere) → `ETaxEmailOptions.RdCcAddress`

**Verify:** grep entire codebase for the deleted keys after removal; build must pass. Any test referencing them must update.

**Risk:** low (deleted keys, not added). If a stray read exists → build break catches it immediately (not runtime).

---

## 3. P2 — `etax.submissions` audit entity

### Schema

```
etax.submissions
  submission_id      BIGINT IDENTITY PK
  company_id         INT NN                  -- RLS via app.company_id
  tax_invoice_id     BIGINT NN FK sales.tax_invoices
  attempted_at       TIMESTAMPTZ NN
  attempt_no         INT NN                  -- 1, 2, 3... for retries

  outcome            VARCHAR(20) NN          -- enum: 'SignedOk' | 'SendOk' | 'SendFailed' | 'RejectedByRd' | 'NotApplicable'

  -- Captured artifacts (paths via IFileStorageService — Sprint 11 reuse)
  xml_sha256         VARCHAR(64) NULL        -- present when signed
  signed_xml_path    VARCHAR(500) NULL       -- storage_path
  pdf_path           VARCHAR(500) NULL       -- if PDF/A-3 generated

  -- Email metadata
  email_message_id   VARCHAR(255) NULL
  to_email_snapshot  VARCHAR(255) NN         -- the address ACTUALLY sent to (after redirect resolved)
  cc_email_snapshot  VARCHAR(255) NULL       -- RD csemail or override
  redirect_applied   BOOL NN DEFAULT false   -- true if RedirectAllToEmail diverted from intended recipient
  intended_to_email  VARCHAR(255) NULL       -- when redirect_applied=true, original intended customer email

  -- SMTP / RD response
  smtp_response      VARCHAR(2000) NULL      -- success message or full error
  rd_ack_ref         VARCHAR(100) NULL       -- Phase 2 when RD ack pipeline lands
  rd_rejection_code  VARCHAR(50) NULL        -- Phase 2

  -- Retry coordination
  retry_after        TIMESTAMPTZ NULL        -- next-attempt scheduling
  dead_letter        BOOL NN DEFAULT false   -- terminal failure after all retries exhausted

  notes              VARCHAR(1000) NULL

  -- Audit
  created_at         TIMESTAMPTZ NN          -- = attempted_at (denorm for trigger consistency)

  INDEX ix_etax_sub_invoice  (company_id, tax_invoice_id, attempted_at DESC)
  INDEX ix_etax_sub_outcome  (company_id, outcome, attempted_at DESC)
  INDEX ix_etax_sub_dead     (company_id, dead_letter, attempted_at DESC) WHERE dead_letter = true
```

**Append-only trigger:** UPDATE/DELETE rejected by trigger (same pattern as `activity_logs` from Sprint 1). 5-year audit retention legal req.

**Migration:** `AddETaxSubmissionsAudit`.

**Service:** `IETaxSubmissionAudit.RecordAsync(...)` — called by submission pipeline at every state change.

---

## 4. P3 — Email safety (`RedirectAllToEmail` + whitelist)

### Extended config

```csharp
public sealed class ETaxEmailOptions
{
    public required string Host { get; init; }
    public int    Port     { get; init; } = 1025;
    public string? User    { get; init; }
    public string? Password { get; init; }
    public required string From   { get; init; }
    public bool    UseSsl  { get; init; } = false;
    public string  RdCcAddress { get; init; } = "csemail@rd.go.th";

    // NEW (Tier 2 safety):
    public string? RedirectAllToEmail { get; init; }    // when set, ALL emails (To + Cc) redirect here instead
    public string[]? WhitelistDomains { get; init; }    // alt: only emails matching pass through, others rejected
}
```

### Behavior in `ETaxEmailSender.SendAsync`

```csharp
// Resolve actual recipient (safety guard)
var (actualTo, actualCc, redirected) = ResolveRecipient(toEmail, _opts.RdCcAddress, _opts);

// If whitelist configured + actualTo doesn't match → throw + audit row 'SendFailed'
if (_opts.WhitelistDomains is { Length: > 0 } &&
    !_opts.WhitelistDomains.Any(d => actualTo.EndsWith("@" + d, StringComparison.OrdinalIgnoreCase)))
{
    throw new DomainException("etax.email.whitelist_violation",
        $"Recipient {actualTo} not in whitelist. Configured for {string.Join(",", _opts.WhitelistDomains)}");
}

// Build + send message with actualTo + actualCc
// Pass back `redirected` boolean to audit pipeline
```

### Resolver

```csharp
private (string To, string Cc, bool Redirected) ResolveRecipient(string intendedTo, string defaultCc, ETaxEmailOptions opts)
{
    if (!string.IsNullOrWhiteSpace(opts.RedirectAllToEmail))
    {
        // Tier 1 / Tier 2 safety: divert everything
        return (opts.RedirectAllToEmail, opts.RedirectAllToEmail, Redirected: true);
    }
    // Tier 3 (production): real send
    return (intendedTo, defaultCc, Redirected: false);
}
```

**Audit row** records both `intended_to_email` (original customer email) AND `to_email_snapshot` (actually sent to) + `redirect_applied=true`. Visible in any review query — clear forensic trail.

### Unit tests
- `EmailRedirect_TierOne_SendsToDevInbox`
- `EmailRedirect_TierThree_SendsToActualCustomer`
- `EmailWhitelist_AllowsApprovedDomain`
- `EmailWhitelist_RejectsNonApprovedDomain`

---

## 5. P4 — XSD validator

### Schemas

Download ETDA standard มกค.14-2563 XSDs (etda.or.th publishes them) → store under `code/etax-schemas/`:

```
etax-schemas/
  TaxInvoice.xsd
  Receipt.xsd                     -- for Phase 2 e-Receipt
  Common.xsd
  README.md                       -- source + version + last-checked date
```

### Interface + impl

```csharp
public interface IETaxXmlValidator
{
    Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct);
}

public sealed record XmlValidationResult(bool IsValid, IReadOnlyList<string> Errors);

public sealed class LocalXsdValidator : IETaxXmlValidator
{
    private readonly XmlSchemaSet _schemas;

    public LocalXsdValidator(IOptions<ETaxValidationOptions> opts)
    {
        _schemas = new XmlSchemaSet();
        var dir = opts.Value.XsdSchemaDir;
        if (!Directory.Exists(dir))
            return;  // empty schemas → ValidateAsync returns Valid=true (Tier 1 graceful skip)
        foreach (var xsd in Directory.EnumerateFiles(dir, "*.xsd"))
            _schemas.Add(null, xsd);
        _schemas.Compile();
    }

    public Task<XmlValidationResult> ValidateAsync(string xml, CancellationToken ct)
    {
        if (_schemas.Count == 0)
            return Task.FromResult(new XmlValidationResult(true, []));  // skip if no schema loaded

        var errors = new List<string>();
        var settings = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = _schemas
        };
        settings.ValidationEventHandler += (s, e) => errors.Add($"{e.Severity}: {e.Message}");

        using var sr = new StringReader(xml);
        using var reader = XmlReader.Create(sr, settings);
        while (reader.Read()) { /* drain to fire validation */ }

        return Task.FromResult(new XmlValidationResult(errors.Count == 0, errors));
    }
}
```

### Pipeline integration

`TaxInvoiceService.PostAsync` e-Tax flow:
1. Build XML (`IETaxXmlBuilder`)
2. Sign XML (`IETaxSigner`)
3. **Validate signed XML (`IETaxXmlValidator`)** — NEW step
4. If `ETax:Validation:RequireSchemaPass=true` AND `!IsValid` → record audit `outcome='SendFailed'`, abort
5. Send email (`IETaxEmailSender`)
6. Record audit `outcome='SendOk' | 'SendFailed'`

### Config

```jsonc
"ETax": {
  "Validation": {
    "XsdSchemaDir": "etax-schemas/",         // relative to ContentRoot
    "RequireSchemaPass": false               // Tier 1: false (graceful skip if no schema loaded)
                                              // Tier 2/3: true (mandatory)
  }
}
```

### Tests
- Valid XML against loaded schema → IsValid=true
- Invalid XML (missing required element) → IsValid=false, Errors populated
- Empty schema dir (Tier 1 graceful) → IsValid=true, Errors empty

---

## 6. P5 — `IRdEfilingClient` + Mock + Real skeleton

### Interface

```csharp
public interface IRdEfilingClient
{
    Task<RdSubmissionResult> SubmitPnd30Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd3Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd53Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd54Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionResult> SubmitPnd36Async(int companyId, int period, byte[] payload, CancellationToken ct);
    Task<RdSubmissionStatus> GetStatusAsync(string submissionId, CancellationToken ct);
}

public sealed record RdSubmissionResult(
    bool   Submitted,
    string SubmissionId,           // RD-issued submission tracking number
    string? AckReference,          // when acked
    string? Error,
    int    HttpStatusCode);

public sealed record RdSubmissionStatus(
    string SubmissionId,
    string Status,                 // 'Pending' | 'Acknowledged' | 'Rejected'
    string? AckReference,
    string? Error,
    DateTimeOffset CheckedAt);
```

### Tier 1 impl: `MockRdEfilingClient`

```csharp
public sealed class MockRdEfilingClient : IRdEfilingClient
{
    public Task<RdSubmissionResult> SubmitPnd30Async(int companyId, int period, byte[] payload, CancellationToken ct)
    {
        // Simulated success — for dev/test pipelines
        var submissionId = $"MOCK-PND30-{companyId}-{period}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        return Task.FromResult(new RdSubmissionResult(
            Submitted: true,
            SubmissionId: submissionId,
            AckReference: $"ACK-{submissionId}",
            Error: null,
            HttpStatusCode: 200));
    }
    // ... similar for other forms
    // GetStatusAsync returns 'Acknowledged' for any MOCK-* id
}
```

### Tier 2/3 impl skeleton: `RdHttpEfilingClient`

```csharp
public sealed class RdHttpEfilingClient : IRdEfilingClient
{
    private readonly HttpClient _http;
    private readonly RdApiOptions _opts;

    public RdHttpEfilingClient(HttpClient http, IOptions<RdApiOptions> opts)
    {
        _http = http;
        _opts = opts.Value;
        _http.BaseAddress = new Uri(_opts.BaseUrl);
        _http.Timeout = TimeSpan.FromSeconds(_opts.TimeoutSeconds);
    }

    public async Task<RdSubmissionResult> SubmitPnd30Async(int companyId, int period, byte[] payload, CancellationToken ct)
    {
        using var content = new ByteArrayContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/xml");

        using var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/pnd30")
        {
            Content = content
        };
        req.Headers.Add("X-Service-Provider-Id", _opts.ServiceProviderId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _opts.ApiKey);

        try
        {
            using var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);
            // TODO: parse response per real RD API spec when credentials available
            //       (response shape currently inferred from docs; verify against UAT)
            return new RdSubmissionResult(
                Submitted: resp.IsSuccessStatusCode,
                SubmissionId: ExtractSubmissionId(body) ?? "",
                AckReference: null,  // async ack; check via GetStatusAsync
                Error: resp.IsSuccessStatusCode ? null : body,
                HttpStatusCode: (int)resp.StatusCode);
        }
        catch (Exception ex)
        {
            return new RdSubmissionResult(
                Submitted: false,
                SubmissionId: "",
                AckReference: null,
                Error: ex.Message,
                HttpStatusCode: 0);
        }
    }
    // ... ExtractSubmissionId is a placeholder helper, to be implemented when RD response format confirmed
}

public sealed class RdApiOptions
{
    public required string Provider { get; init; }      // 'Mock' | 'RdUat' | 'RdProduction'
    public required string BaseUrl { get; init; }
    public string ServiceProviderId { get; init; } = "";
    public string ApiKey { get; init; } = "";
    public int TimeoutSeconds { get; init; } = 30;
}
```

### DI registration

```csharp
// in DependencyInjection.cs
var rdProvider = config["RdApi:Provider"] ?? "Mock";
if (rdProvider == "Mock")
    services.AddSingleton<IRdEfilingClient, MockRdEfilingClient>();
else
    services.AddHttpClient<IRdEfilingClient, RdHttpEfilingClient>();  // real impl, retries via HttpClient policies
```

### Tax Filing service integration

`Sprint 9` `TaxFilingService.FinalizeAsync` already supports manual mode. Add auto mode:
- If `Tax:Pnd30SubmissionMode='auto'` AND finalize → call `IRdEfilingClient.SubmitPnd30Async`
- Store `RdSubmissionResult.SubmissionId` + `AckReference` in `tax_filings.rd_ack_ref`
- Status polling via `GetStatusAsync` (Phase 2 — background job; this sprint just records initial response)

---

## 7. P6 — In-process submission queue (minimal)

**Goal:** prevent fire-and-forget loss. Persist intent + attempt + outcome to `etax.submissions`. If send fails → mark `retry_after` + audit row → background worker (`IHostedService`) periodically scans + retries.

**Simplest impl (no external infra):**

```csharp
public interface IETaxSubmissionPipeline
{
    Task EnqueueAsync(long taxInvoiceId, CancellationToken ct);
}

public sealed class ETaxSubmissionPipeline : IETaxSubmissionPipeline
{
    // ... calls Builder → Signer → Validator → Sender in sequence
    // ... records etax.submissions row at each stage
    // ... on failure, sets retry_after to now + backoff per attempt_no
}

public sealed class ETaxRetryWorker : BackgroundService
{
    // ... every 60s, query etax.submissions WHERE retry_after <= now AND outcome='SendFailed' AND !dead_letter
    // ... re-attempt; respect retry limit (6) before marking dead_letter=true
}
```

**Backoff schedule:** 1m, 5m, 15m, 1h, 4h, 24h (config'd in `ETax:Submission:BackoffSchedule`). After 6 attempts → dead-letter (visible via audit query, no auto-resurrection).

**Trade-off accepted:** in-process queue = lost on app restart (rows in DB persist; retry resumes on next worker tick). For Phase 1 acceptable. Phase 2 → consider durable queue (Hangfire / Quartz / Postgres LISTEN-NOTIFY).

---

## 8. P7 — Dev tools

### 7.1 `dev-tools/gen-test-cert.sh`

```bash
#!/usr/bin/env bash
set -euo pipefail
PASSWORD="${1:-DevPfxPassword}"
OUT="${2:-./secrets/dev-cert.pfx}"
mkdir -p "$(dirname "$OUT")"
TMP=$(mktemp -d)
openssl req -x509 -newkey rsa:2048 -nodes -days 365 \
  -keyout "$TMP/key.pem" -out "$TMP/cert.pem" \
  -subj "/C=TH/O=TEAS Dev Company/CN=teas-dev/serialNumber=0123456789012"
openssl pkcs12 -export -out "$OUT" \
  -inkey "$TMP/key.pem" -in "$TMP/cert.pem" \
  -password "pass:$PASSWORD" -name "TEAS Dev Signing Key"
rm -rf "$TMP"
echo "Generated: $OUT (password: $PASSWORD)"
echo "Add to .env or appsettings.Development.json:"
echo "  ETax:Signing:PfxPath = $OUT"
echo "  ETax:Signing:PfxPassword = $PASSWORD"
```

`.gitignore` rule: `secrets/*.pfx` + `secrets/*.pem`.

### 7.2 `docker-compose.dev.yml` (or extend existing)

```yaml
services:
  postgres:
    # ... existing ...

  mailhog:
    image: mailhog/mailhog:latest
    container_name: teas-mailhog
    ports:
      - "1025:1025"    # SMTP
      - "8025:8025"    # Web UI: http://localhost:8025
    restart: unless-stopped

  mockserver:
    image: mockserver/mockserver:latest
    container_name: teas-mockserver
    ports:
      - "1080:1080"
    environment:
      MOCKSERVER_INITIALIZATION_JSON_PATH: "/config/initializerJson.json"
    volumes:
      - ./dev-tools/mockserver/initializerJson.json:/config/initializerJson.json:ro
    restart: unless-stopped
```

### 7.3 `dev-tools/mockserver/initializerJson.json`

Pre-canned RD API responses for Mock provider:

```json
[
  {
    "httpRequest": {
      "method": "POST",
      "path": "/api/v1/pnd30"
    },
    "httpResponse": {
      "statusCode": 200,
      "headers": { "Content-Type": ["application/json"] },
      "body": {
        "submissionId": "MOCK-PND30-{{ random.uuid }}",
        "status": "Accepted",
        "ackReference": "ACK-MOCK-{{ now }}"
      }
    }
  },
  {
    "httpRequest": { "method": "POST", "path": "/api/v1/pnd3" },
    "httpResponse": { "statusCode": 200, "body": { "submissionId": "MOCK-PND3-{{ random.uuid }}", "status": "Accepted" } }
  },
  {
    "httpRequest": { "method": "POST", "path": "/api/v1/pnd53" },
    "httpResponse": { "statusCode": 200, "body": { "submissionId": "MOCK-PND53-{{ random.uuid }}", "status": "Accepted" } }
  },
  {
    "httpRequest": { "method": "POST", "path": "/api/v1/pnd54" },
    "httpResponse": { "statusCode": 200, "body": { "submissionId": "MOCK-PND54-{{ random.uuid }}", "status": "Accepted" } }
  },
  {
    "httpRequest": { "method": "POST", "path": "/api/v1/pnd36" },
    "httpResponse": { "statusCode": 200, "body": { "submissionId": "MOCK-PND36-{{ random.uuid }}", "status": "Accepted" } }
  },
  {
    "httpRequest": { "method": "GET", "path": "/api/v1/status/.*" },
    "httpResponse": { "statusCode": 200, "body": { "status": "Acknowledged", "ackReference": "ACK-DONE" } }
  }
]
```

### 7.4 Documentation updates

- `docs/etax-environment-tiers.md` (already written) — link from CLAUDE.md
- `CLAUDE.md` §X (new section "e-Tax environment switching") — brief pointer + Tier 1 startup steps:
  ```
  Tier 1 dev startup:
    1. ./dev-tools/gen-test-cert.sh
    2. docker compose -f docker-compose.dev.yml up -d mailhog mockserver postgres
    3. dotnet run --project backend/src/Accounting.Api
    4. MailHog UI: http://localhost:8025
    5. MockServer: http://localhost:1080
  ```

---

## 9. P8 — Tests + integration + e2e

### Unit
- `EmailRecipientResolverTests` — redirect on/off, whitelist match
- `XsdValidatorTests` — valid/invalid XML, empty schema dir graceful
- `BackoffScheduleTests` — attempt 1 → 1m, attempt 6 → 24h, attempt 7 → dead-letter

### Integration
- `EtaxPipeline_Tier1Mock_EndToEnd` — TI POST with `ETax:Enabled=true` + Mock SMTP + Mock RD client → email captured + audit row inserted + `outcome='SendOk'` + `redirect_applied=true`
- `EtaxPipeline_SignerMissingPfx_FailsGracefully` — empty PFX path → audit row `outcome='SignedOk=false'`, not crash
- `EtaxPipeline_XsdValidationFails_AbortsAndRecords` — inject invalid XML → audit `outcome='SendFailed'` with validation errors
- `EmailWhitelist_NonApprovedDomain_Rejects` — try send to outside-whitelist → 400-style domain exception, audit recorded
- `RetryWorker_PicksUpFailedSubmission` — seed `etax.submissions` with `outcome=SendFailed` + `retry_after=now-1m` → worker re-attempts → new audit row with attempt_no=2
- `RetryWorker_DeadLetters_After6Attempts` — simulate 6 failures → 7th attempt marks `dead_letter=true`, no further retry
- `MockRdEfilingClient_SubmitPnd30_ReturnsAcceptedShape` — Mock impl returns expected envelope
- `RdHttpEfilingClient_Skeleton_DoesntCrash_OnConfigOnly` — instantiate with real config, don't actually call RD (no UAT yet)

### e2e Playwright (×1 new)

`etax-pipeline-mock.spec.ts`:
1. Pre-setup: ensure MailHog running (or use a managed dev-server profile)
2. Login as accountant
3. Configure `ETax:Enabled=true` + `AutoSendOnTaxInvoicePost=true` via dev override
4. Create + POST a Tax Invoice
5. Wait briefly for pipeline
6. Hit MailHog API (`http://localhost:8025/api/v1/messages`) → assert 1 message captured
7. Assert email subject contains TI doc_no
8. Assert XML attachment present
9. Hit backend API `/etax/submissions?tax_invoice_id=...` → assert audit row exists with `outcome='SendOk'`

Total: 29 prior + 1 new = **30/30** Playwright.

---

## 10. Scope cuts — explicitly OUT (DO NOT improvise)

- ❌ **HSM impl (`HsmETaxSigner`)** — Phase 2 when first customer needs HSM (Azure Key Vault Managed HSM target)
- ❌ **Full durable retry queue** (Hangfire / Quartz) — Phase 2 when load demands
- ❌ **Real RD UAT integration** — blocked on Service Provider registration (Phase 0 prerequisite, 4-6 wks)
- ❌ **e-Receipt signing** (signed XML for Receipts) — Phase 2 per plan §13
- ❌ **XML schema auto-update from ETDA** — manual schema refresh
- ❌ **Dead-letter UI** — Phase 2 (visible via SQL query for now)
- ❌ **RD response parsing** (full ack flow) — Tier 2/3 + real UAT credentials needed
- ❌ **Status polling background job** — Phase 2 (Tier 1 mock returns immediate ack; real ack flow Phase 2)
- ❌ **OAuth flow for RD API** — using Bearer token in skeleton; OAuth callback flow Phase 2 if RD requires

If any block → escalate per §8.

---

## 11. Gates (non-negotiable)

| Gate | Expectation |
|---|---|
| Backend build | 0/0 (post config-key removal — verify no orphan reads) |
| Domain tests | +N (resolver, validator, backoff math) |
| Api tests | +N (pipeline end-to-end with Mock SMTP + Mock RD, redirect safety, whitelist, retry worker, dead-letter) |
| EF migration | `AddETaxSubmissionsAudit` clean |
| tsc / next build | 0 / 0 (no new frontend routes — UI for `/etax/submissions` audit viewer = Phase 2) |
| Playwright | 29 prior + 1 new = **30/30** (etax-pipeline-mock.spec.ts) |
| Mirror | synced `Y:\AccountApp` |
| Audit-append-only | UPDATE/DELETE on `etax.submissions` rejected by trigger |
| Config cleanup verification | grep result: 0 occurrences of `Tax:EtaxEnabled` and `Tax:EtaxDeliveryEmailCc` in entire codebase post-cleanup |
| Tier 1 startup smoke | `gen-test-cert.sh` + `docker compose up mailhog mockserver` + `dotnet run` → can POST a TI with `Enabled=true` → email in MailHog Web UI |

---

## 12. Definition of done (single phase — but 14 items)

1. P1: Config keys deleted (`Tax:EtaxEnabled`, `Tax:EtaxDeliveryEmailCc`, `ETaxBehaviorOptions.RdCcAddress`) + grep clean
2. P2: `etax.submissions` entity + EF config + migration + append-only trigger + `IETaxSubmissionAudit` service
3. P3: `ETaxEmailOptions.RedirectAllToEmail` + `WhitelistDomains` + resolver logic + tests
4. P4: `etax-schemas/` dir + XSD files committed + `IETaxXmlValidator` + `LocalXsdValidator` + pipeline integration + tests
5. P5: `IRdEfilingClient` + `MockRdEfilingClient` + `RdHttpEfilingClient` skeleton + DI selector + Tax Filing service integration
6. P6: `IETaxSubmissionPipeline` + `ETaxRetryWorker` BackgroundService + backoff schedule + dead-letter logic
7. P7a: `dev-tools/gen-test-cert.sh` + `.gitignore` rule for `secrets/*`
8. P7b: `docker-compose.dev.yml` MailHog + MockServer additions
9. P7c: `dev-tools/mockserver/initializerJson.json` canned responses
10. P7d: `CLAUDE.md` new "e-Tax environment switching" section + link to `docs/etax-environment-tiers.md`
11. P8a: Unit + integration tests per §9
12. P8b: `etax-pipeline-mock.spec.ts` e2e
13. All gates green; mirror sync
14. plan.md §23.3 — strike Sprint 13c
15. `Report-Backend18.md` per template

**Total: 15 DoD items.**

---

## 13. After this sprint

Phase 1 backbone + production-readiness COMPLETE. Remaining:
- **Sprint 13b** — User Manual generator (~8-12 d) — can start parallel after Sprint 13c lands
- **External pen-test** (5-10 d external vendor)
- **First-customer onboarding + data migration** (per `test/08-data-migration-test.md`)
- **Go-live checklist** walkthrough (`test/09-go-live-checklist.md`)
- **Real e-Tax UAT registration + transition** (Phase 0 work, 4-6 wks lead time, gated on customer VAT-registration)

Phase 1 production-ready: **after Sprint 13b + pen-test + first customer accepts**.

---

**Build it. Single phase ~4-5 days. Reference `docs/etax-environment-tiers.md` for context. Report back via Report-Backend18.**

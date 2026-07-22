# Spec — Implement RFC 7591 Dynamic Client Registration (make Claude connector work in prod)

Owner: Fable drives design (footgun: public unauth OAuth endpoint). opus-designer hardens the
technical design + security policy. sonnet-implementer types code FROM this spec. opus-reviewer
security-reviews. Fable runs gates + diff review + commit.

## Problem (confirmed, 2026-07-05)

Claude Desktop/Mobile MCP connector auto-registers its OAuth client via **DCR (RFC 7591)**. TEAS
does not implement DCR, so the connector errors "Automatic client registration isn't supported by
TEAS" (prod symptom logged in `specs/mcp-dcr-client-registration.md`, ref `ofid_f44ec015c7dec447`).

Live prod probe (through Cloudflare, 2026-07-05):
- `/.well-known/oauth-protected-resource` → 200 ✓
- `/.well-known/oauth-authorization-server` → 200 but **no `registration_endpoint`** field ✗
- `POST /mcp` no auth → 401 + correct `WWW-Authenticate` ✓
- `POST /oauth/register` → 401, no `client_id` ✗ (backend has no such endpoint; FE passthrough hits nothing)
- `GET /oauth/authorize` → 302 → login ✓

Everything works EXCEPT client registration. The pre-registered `teas-mcp` client + claude.ai
callback (fix `756d0d6`) only helps a client that can enter `client_id=teas-mcp` manually — the
claude.ai connector takes a URL only, so it MUST use DCR.

## Goal / definition of done

The Claude Desktop/Mobile connector, given ONLY the URL `https://teas.kazaki-rio.com/mcp`, completes:
discover → **DCR (auto client_id)** → authorize → consent (company pick) → token → a real tool call —
in **prod over Cloudflare**. No manual client_id anywhere.

## Requirements (Fable — scope; do NOT descope)

> **R8 added 2026-07-05 after Tier-2 review (finding F1).** Enabling DCR opened `redirect_uris` to any
> `https://` host (pre-DCR only the fixed seeder allowlist — claude.ai/loopback — could ever appear).
> The consent screen is now the SOLE human gate against a phished dynamically-registered client, yet it
> shows only the opaque `client_id` and never the redirect destination. Consent-transparency fix
> (below) is REQUIRED to ship DCR safely — it is not optional polish.


1. **Backend `/oauth/register`** — RFC 7591 anonymous POST, returns 201 with a generated `client_id`
   for a public/PKCE client. Path `oauth/register` (FE passthrough + middleware already public).
2. **Advertise `registration_endpoint`** in BOTH `/.well-known/oauth-authorization-server` and
   `/.well-known/openid-configuration`, pointing at `{App:BaseUrl}/oauth/register` (FE rewrite carries
   the internal→public swap, same as other endpoints — verify it does).
3. **Server-authoritative scope pinning** — the registration request's `scope`/permissions are IGNORED;
   the created client gets EXACTLY the fixed `McpScopes.All` set (never a `*.post` write scope) + the
   MCP resource + AuthorizationCode/RefreshToken/Code/offline_access — identical policy to
   `OpenIddictSeeder`. A registered client can NEVER self-grant write authority. This is the core
   security invariant. Reuse the seeder's descriptor-building logic (don't duplicate policy).
4. **redirect_uris validation** — required, non-empty; each must be `https://` OR loopback
   (`http://localhost` / `http://127.0.0.1`). Reject anything else with `invalid_redirect_uri`.
   Client type = Public (PKCE, no secret), ConsentType = Explicit (our consent page still gates every
   grant + company pick — the human is always in the loop).
5. **Abuse guard** — the endpoint is anonymous + world-reachable. Rate-limit it (reuse the existing
   rate-limiter infra). Decide dedup: if a POST arrives with redirect_uris already registered under an
   auto-DCR client, REUSE that client_id rather than creating a new row (prevents unbounded client-row
   growth from a client that re-registers). opus-designer decides the cleanest mechanism.
6. **Existing `teas-mcp` pre-registered client stays** (Codex CLI / manual fallback) — DCR is additive.
7. Tests: registration happy-path (201 + client_id + public + fixed scopes, NO write scope), redirect
   validation rejects non-https/non-loopback, scope-injection is ignored (request asks for a `.post`
   scope → created client has none), discovery lists `registration_endpoint`, and a DCR-registered
   client can round-trip authorize→token→/mcp. There is already a
   `backend/tests/Accounting.Api.Tests/OAuth/McpClientRegistrationTests.cs` — extend/align it, don't
   fork a parallel suite.
8. **Consent transparency (F1 fix).** The consent screen
   (`frontend/app/(dashboard)/oauth/consent/page.tsx`) MUST show the human WHERE the authorization code
   will be sent — the `redirect_uri` **host/origin** — so a foreign destination is catchable. For a
   dynamically-registered client (`client_id` starts with `dcr-`) add a caution line ("This application
   registered itself automatically — only continue if you started this connection"). The `redirect_uri`
   is already in scope on that page (read for validation, forwarded to the accept BFF) — just surface
   it. Keep the existing behaviour intact; do not weaken the login/membership gates.

## Open technical questions → opus-designer (resolve against the ACTUAL installed package)

- **A. Does OpenIddict 7.5 have a native DCR / client-registration endpoint** (a
  `SetClientRegistrationEndpointUris` + a `HandleClientRegistrationRequestContext`/`ProcessRegistration`
  event, plus a discovery `RegistrationEndpoint` on `HandleConfigurationRequestContext`)? Verify by
  inspecting the installed `OpenIddict.*` assemblies' public API — do NOT assume. If native support
  exists, prefer it (enable + an event handler that pins scopes/validates redirect_uris). If it does
  NOT, hand-roll a minimal-API `/oauth/register` endpoint (mirror `OAuthEndpoints.MapOAuthAuthorize`
  style) that builds an `OpenIddictApplicationDescriptor` and calls `IOpenIddictApplicationManager.CreateAsync`.
- **B. Advertising `registration_endpoint`** — if native, OpenIddict adds it; else add it via an inline
  `HandleConfigurationRequestContext` handler (same pattern already used at `Program.cs:219-224` for the
  `none` auth method) setting `context.RegistrationEndpoint`. Confirm the FE rewrite
  (`.well-known/oauth-authorization-server/route.ts`) carries it to the public origin.
- **C. Read `McpClientRegistrationTests.cs`** — what does it already assert? Does a partial DCR impl
  exist and was it disabled? Align the design to what the test expects (or fix the test if stale).
- **D. Client store growth / dedup** — cleanest way to avoid a new client row per connector re-add.
  (Deterministic client_id from a hash of sorted redirect_uris? Find-or-create?) Name the trade-off.
- **E. Refactor `OpenIddictSeeder`'s descriptor policy** into a shared helper both the seeder and the
  DCR endpoint call, so the server-fixed scope/permission policy lives in ONE place (Ponytail: reuse,
  don't duplicate the security-critical scope pinning).

## Verification gates (Fable owns)

- Build 0/0. Full OAuth suite green (×2 on teas_test per repo convention). New DCR tests green.
- `dotnet ef` NOT needed unless a new migration — the `oauth` application store already exists
  (`20260701072643_AddOpenIddict`). If opus-designer's design needs schema, that's a red flag → re-spec.
- Post-deploy (prod, needs Ham's creds): re-run the 5-gate probe — `registration_endpoint` present,
  `POST /oauth/register` → 201 + client_id, then add the connector in Claude Desktop and complete a
  tool call. (§6 of `docs/mcp-oauth-deploy-gates.md`.)

## Blast-radius cap

Backend: `Accounting.Api/OAuth/*` + `Program.cs` OpenIddict block + the seeder refactor. FE: at most the
existing `oauth/register/route.ts` (should need no change). Tests: the OAuth suite. NO change to token
issuance, refresh, RLS, tenant pinning, or the /mcp tool surface. Touching any of those = stop & re-spec.

## Design (opus-designer, 2026-07-05) — [x]

### Verdict on the open questions (with evidence)

**A — HAND-ROLLED. OpenIddict 7.5 has NO native server-side DCR.** Evidence (installed
`OpenIddict.Server` 7.5.0, `~/.nuget/packages/openiddict.server/7.5.0/lib/net10.0/OpenIddict.Server.dll`):
- `strings … | grep -i registration` on `OpenIddict.Server.dll` → **zero hits**. No
  `SetClientRegistrationEndpointUris`, no `HandleClientRegistrationRequestContext`,
  no `ProcessRegistration*` handler exists in the server pipeline.
- `OpenIddict.Server.AspNetCore.dll` → **zero** "registration" strings (no passthrough).
- Every "Registration" string in `OpenIddict.Abstractions.dll` is **client-side**
  (`OpenIddictClientRegistration`, provider integrations) — irrelevant to server DCR.
- Control check confirmed the grep works: `AuthorizationEndpoint`, `HandleConfigurationRequestContext`,
  `SetTokenEndpointUris` all resolve (count 1 each, deduped in the `#Strings` heap).
→ Build a minimal-API `POST /oauth/register` that calls `IOpenIddictApplicationManager.CreateAsync`,
mirroring the `OAuthEndpoints.MapOAuth*` style.

**B — inline `HandleConfigurationRequestContext` handler, `context.Metadata` dict.** Evidence:
`HandleConfigurationRequestContext` exposes `get_Metadata`, `get_Response`, `get_Transaction` but
**no** `RegistrationEndpoint` typed property (no `set_RegistrationEndpoint`), and Abstractions has
**no** `registration_endpoint` constant → use the **string literal** `"registration_endpoint"`.
The server contains an `AttachAdditionalMetadata` handler → the `Metadata` dictionary IS merged into
the discovery JSON, so `context.Metadata["registration_endpoint"] = <uri>` serializes. **Fold this
into the EXISTING inline handler at `Program.cs:219-224`** (the one adding `none`) — same event, one
registration. FE rewrite: **confirmed** — BOTH `frontend/app/.well-known/oauth-authorization-server/route.ts`
AND `…/openid-configuration/route.ts` do `body.replaceAll(BACKEND, publicOrigin)`. Because we build
the registration URI from `context.AuthorizationEndpoint` (already the request/BACKEND origin), the
existing rewrite carries it to the public origin in BOTH docs. **Requirement 2 met with ZERO FE changes.**

**C — `McpClientRegistrationTests.cs` does NOT test DCR.** It asserts the pre-registered `teas-mcp`
client is public/PKCE with Claude's redirect_uris + scopes, and that discovery lists `none`. There is
**no partial/disabled DCR impl** anywhere. The class XML-doc ("TEAS has no Dynamic Client
Registration…") goes **stale** once DCR lands — implementer must update that summary. DCR happy-path/
validation tests are **added to this same file** (don't fork). The discovery `registration_endpoint`
assertion belongs in `DiscoveryEndpointsTests.cs`; the authorize→token→/mcp round-trip reuses the
`BearerMcpRoundTripTests.AcquireOauthTokenAsync` harness (see test list).

**D — deterministic `client_id` = `"dcr-" + SHA256(sorted redirect_uris)[..32]`, find-or-create,
idempotent.** Trade-off: a predictable id + shared row for two clients with an identical redirect_uri
set — a **non-issue** here: every DCR client gets the identical server-fixed policy, `client_id` is
not a security boundary (per-user consent + company pick + PKCE + RBAC cap are), and there is no
`client_secret` (public client). Upside: the claude.ai connector always registers
`https://claude.ai/api/mcp/auth_callback` → **exactly one row for all claude.ai users**, directly
satisfying requirement 5 ("REUSE that client_id"). Residual (accepted, per requirement 5): dedup only
collapses *identical* re-registrations; distinct-uri flooding is bounded by the rate limiter alone.

**E — shared `McpClientDescriptorFactory.Build(...)`** (below) holds the fixed scope/permission policy;
both `OpenIddictSeeder` and the DCR endpoint call it. Scope pinning lives in ONE place.

**Migration: NONE.** Reuses the existing `oauth` application store (`20260701072643_AddOpenIddict`).

---

### File-by-file change list

1. **NEW `backend/src/Accounting.Api/OAuth/McpClientDescriptorFactory.cs`** — shared policy helper (E).
2. **EDIT `backend/src/Accounting.Api/OAuth/OpenIddictSeeder.cs`** — replace the inline descriptor
   build (lines ~54-78) with a call to the factory; keep the scope-seeding loop and the
   `DefaultRedirectUris.Union(configured)` logic (seeder-specific).
3. **EDIT `backend/src/Accounting.Api/OAuth/OAuthEndpoints.cs`** — add `MapOAuthRegister` + the
   `DcrRequest` record.
4. **EDIT `backend/src/Accounting.Api/Program.cs`** — (a) extend the existing inline discovery handler
   (~219) to advertise `registration_endpoint`; (b) add the `"dcr"` rate-limit policy inside the
   existing `AddRateLimiter` block (~306); (c) call `app.MapOAuthRegister();` after
   `app.MapOAuthAuthorize();` (line 496).
5. **EDIT tests** — `McpClientRegistrationTests.cs` (+DCR unit tests, fix stale doc),
   `DiscoveryEndpointsTests.cs` (+registration_endpoint), `BearerMcpRoundTripTests.cs` (parametrize
   harness + DCR round-trip).
6. **FE — NO CHANGE** (`oauth/register/route.ts` already forwards; both discovery routes already rewrite).

---

### 1. Shared policy helper (E) — NEW `McpClientDescriptorFactory.cs`

```csharp
using Accounting.Application.Abstractions;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// The server-authoritative descriptor for an MCP public/PKCE client. The scope/permission policy is
/// FIXED here (McpScopes.All + offline_access + the MCP resource) and lives ONLY here — both
/// OpenIddictSeeder (teas-mcp) and the DCR endpoint call this, so a client can NEVER self-grant a
/// *.post write scope. Requested scopes are never an input (spec §3, core security invariant).
/// </summary>
public static class McpClientDescriptorFactory
{
    public static OpenIddictApplicationDescriptor Build(
        string clientId, string displayName, string mcpResource, IEnumerable<Uri> redirectUris)
    {
        var d = new OpenIddictApplicationDescriptor
        {
            ClientId    = clientId,
            ClientType  = ClientTypes.Public,       // PKCE, no secret
            ConsentType = ConsentTypes.Explicit,    // /oauth/consent gates every grant + company pick
            DisplayName = displayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var s in McpScopes.All) d.Permissions.Add(Permissions.Prefixes.Scope + s);
        d.Permissions.Add(Permissions.Prefixes.Resource + mcpResource);
        foreach (var u in redirectUris) d.RedirectUris.Add(u);   // store the parsed Uri AS-IS (mirror seeder)
        return d;
    }
}
```

**Seeder refactor:** in `OpenIddictSeeder.StartAsync`, after computing `mcpResource` and the
`redirectUris` union, replace the hand-built `descriptor` with:
```csharp
var redirectUris = DefaultRedirectUris.Union(configured, StringComparer.OrdinalIgnoreCase)
                                      .Select(u => new Uri(u));
var descriptor = McpClientDescriptorFactory.Build(
    McpClientId, "TEAS Connect (MCP)", mcpResource, redirectUris);
```
Keep the `FindByClientIdAsync` → Create/Update block and the scope-manager seeding loop unchanged.

### 2. Endpoint + DTO (A, D) — add to `OAuthEndpoints.cs`

```csharp
// add: using System.Text.Json.Serialization; using System.Security.Cryptography; using System.Text;
//      using Microsoft.AspNetCore.Http; (Results/StatusCodes)

/// <summary>RFC 7591 DCR request. scope / grant_types / response_types / token_endpoint_auth_method
/// are ACCEPTED but IGNORED — the created client always gets the fixed server policy.</summary>
public sealed record DcrRequest
{
    [JsonPropertyName("redirect_uris")]              public List<string>? RedirectUris { get; init; }
    [JsonPropertyName("client_name")]                public string? ClientName { get; init; }
    [JsonPropertyName("scope")]                      public string? Scope { get; init; }
    [JsonPropertyName("grant_types")]                public List<string>? GrantTypes { get; init; }
    [JsonPropertyName("response_types")]             public List<string>? ResponseTypes { get; init; }
    [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; init; }
}

/// <summary>
/// RFC 7591 Dynamic Client Registration — ANONYMOUS POST. OpenIddict 7.5 has no native DCR
/// (verified: OpenIddict.Server.dll has zero registration handlers), so this is hand-rolled and
/// hardened: the request's scope/grants are IGNORED, redirect_uris are https/loopback-only, the
/// created client gets the fixed policy via McpClientDescriptorFactory, the client_id is
/// deterministic (dedup), and the endpoint is rate-limited (anonymous + world-reachable).
/// </summary>
public static IEndpointRouteBuilder MapOAuthRegister(this IEndpointRouteBuilder app)
{
    app.MapMethods("/oauth/register", ["POST"], async (
        HttpContext http, DcrRequest? body,
        IOpenIddictApplicationManager appMgr, IOptions<AppOptions> opt,
        CancellationToken ct) =>
    {
        var raw = body?.RedirectUris ?? [];
        if (raw.Count == 0)
            return DcrError("invalid_redirect_uri", "redirect_uris is required and must be non-empty.");

        var uris = new List<Uri>(raw.Count);
        foreach (var s in raw)
        {
            if (!Uri.TryCreate(s, UriKind.Absolute, out var u) || !IsAllowedRedirectUri(u))
                return DcrError("invalid_redirect_uri",
                    $"redirect_uri '{s}' must be an https URI or a loopback http URI.");
            uris.Add(u);
        }

        var clientId    = DeterministicClientId(uris);
        var mcpResource = $"{opt.Value.BaseUrl.TrimEnd('/')}/mcp";
        var displayName = string.IsNullOrWhiteSpace(body?.ClientName) ? "TEAS Connect (DCR)" : body!.ClientName!;
        var descriptor  = McpClientDescriptorFactory.Build(clientId, displayName, mcpResource, uris);

        // Find-or-create, idempotent + concurrency-safe. UpdateAsync-on-hit re-asserts the
        // server-fixed policy (a future McpScopes tightening propagates), mirroring the seeder.
        var existing = await appMgr.FindByClientIdAsync(clientId, ct);
        if (existing is not null)
        {
            await appMgr.UpdateAsync(existing, descriptor, ct);
        }
        else
        {
            try { await appMgr.CreateAsync(descriptor, ct); }
            catch (Exception)   // concurrent create → the unique client_id index rejected us
            {
                var raced = await appMgr.FindByClientIdAsync(clientId, ct);
                if (raced is null) throw;                       // a genuine failure, not a race
                await appMgr.UpdateAsync(raced, descriptor, ct);
            }
        }

        return Results.Json(new
        {
            client_id                  = clientId,
            client_id_issued_at        = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            redirect_uris              = uris.Select(u => u.AbsoluteUri).ToArray(),
            token_endpoint_auth_method = "none",                // public/PKCE, no secret
            grant_types                = new[] { "authorization_code", "refresh_token" },
            response_types             = new[] { "code" },
            scope                      = string.Join(' ', McpScopes.All),
            client_name                = displayName,
        }, statusCode: StatusCodes.Status201Created);           // NOTE: no client_secret (public client)
    })
    .AllowAnonymous()
    .RequireRateLimiting("dcr")
    .WithName("OAuthRegister");

    return app;

    // https OR loopback-http only (Uri.IsLoopback ⇒ localhost / 127.0.0.0/8 / ::1). Rejects custom
    // schemes + non-loopback http. Covers every documented Claude callback (claude.ai https + Claude
    // Code loopback); requirement 4.
    static bool IsAllowedRedirectUri(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps || (u.Scheme == Uri.UriSchemeHttp && u.IsLoopback);

    static string DeterministicClientId(IEnumerable<Uri> uris)
    {
        var joined = string.Join('\n', uris.Select(u => u.AbsoluteUri).OrderBy(s => s, StringComparer.Ordinal));
        var hash   = SHA256.HashData(Encoding.UTF8.GetBytes(joined));
        return "dcr-" + Convert.ToHexString(hash)[..32].ToLowerInvariant();   // disjoint from "teas-mcp" (§6)
    }

    static IResult DcrError(string error, string description) =>
        Results.Json(new { error, error_description = description },   // RFC 7591 §3.2.2
            statusCode: StatusCodes.Status400BadRequest);
}
```

### 3. Advertise `registration_endpoint` (B) — extend the existing handler at `Program.cs:219`

Add, INSIDE the existing `HandleConfigurationRequestContext` inline handler body (after the `none`
line, before `return default;`):
```csharp
// RFC 7591 — advertise the DCR endpoint in BOTH discovery docs (this handler fires for
// openid-configuration AND oauth-authorization-server). Build it from AuthorizationEndpoint so it
// carries the request/BACKEND origin → the FE .well-known rewrite swaps it to the public origin,
// exactly like authorization_endpoint/token_endpoint. Guard the null (a null-forgiving `!` here
// would 500 the whole discovery response).
if (context.AuthorizationEndpoint is { } authz)
    context.Metadata["registration_endpoint"] = new Uri(authz, "/oauth/register").AbsoluteUri;
```
**Fallback (only if the discovery test in T7 shows the field missing):** replace `context.Metadata[…]`
with `context.Response["registration_endpoint"] = …`. The `AttachAdditionalMetadata` handler makes
`Metadata` the expected path; the test is the gate that proves serialization.

### 4. Rate-limit policy (requirement 5) — add inside `AddRateLimiter(o => {…})` at `Program.cs:~306`

```csharp
// DCR (/oauth/register) is anonymous + world-reachable → per-IP fixed window, mirroring "login".
// Relies on M5 UseForwardedHeaders for the real caller IP through Cloudflare→Next→backend (else all
// DCR shares the Next passthrough IP bucket — still bounded, just coarser). 10/min/IP is ample for a
// connector that registers once.
o.AddPolicy("dcr", ctx => RateLimitPartition.GetFixedWindowLimiter(
    ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
    _ => new FixedWindowRateLimiterOptions
    {
        PermitLimit = 10, Window = TimeSpan.FromMinutes(1),
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst, QueueLimit = 0,
    }));
```
Then wire at `Program.cs:496`: `app.MapOAuthRegister();` immediately after `app.MapOAuthAuthorize();`.

---

### Test list (precise)

**`McpClientRegistrationTests.cs`** — add (update the stale class XML-doc to note DCR now exists;
use `factory.CreateClient()` for the HTTP POST and a `CreateScope()` +
`IOpenIddictApplicationManager` to inspect the created row, as the file already does):
- **T1 `Register_returns_201_public_client_with_fixed_scopes_and_no_write_scope`** — POST
  `{redirect_uris:["https://claude.ai/api/mcp/auth_callback"]}` → 201; body `client_id` non-empty,
  `token_endpoint_auth_method=="none"`, `grant_types` ⊇ {authorization_code, refresh_token},
  `scope` contains every `McpScopes.All`. Resolve the created app: `ClientType==Public`; permissions
  contain `scope:`+each `McpScopes.All` + `offline_access` + `resource:`+`/mcp`; PKCE requirement
  present; and **no** permission ends with any `McpScopes.ForbiddenSuffixes` (`.post` etc.).
- **T2 `Register_rejects_non_https_non_loopback_redirect_uri`** — `[Theory]` over
  `"http://evil.example.com/cb"`, `"http://192.168.1.10/cb"`, `"ftp://h/cb"`,
  `"com.example.app:/cb"` → each 400 with `error=="invalid_redirect_uri"`.
- **T3 `Register_ignores_requested_scope_and_grant_injection`** — POST
  `{redirect_uris:["http://localhost/callback"], scope:"sales.tax_invoice.post admin.super",
  grant_types:["client_credentials"]}` → 201; created client permissions contain **NO**
  `scope:sales.tax_invoice.post`, **NO** `admin.super`, **NO** client_credentials grant — only the
  fixed set. (Core invariant.)
- **T4 `Register_requires_redirect_uris`** — POST `{}` and POST `{redirect_uris:[]}` → both 400
  `invalid_redirect_uri`.
- **T5 `Register_is_idempotent_same_uris_return_same_client_id`** — POST twice with the same
  `redirect_uris` → identical `client_id`; `FindByClientIdAsync` returns exactly one app (dedup / no
  row growth).

**`DiscoveryEndpointsTests.cs`** — add (this is the gate for advisor's Metadata-serialization risk):
- **T6 `Authorization_server_metadata_advertises_registration_endpoint`** — GET
  `/.well-known/oauth-authorization-server` → `registration_endpoint` present, ends with `/oauth/register`.
- **T7 `Openid_configuration_advertises_registration_endpoint`** — GET
  `/.well-known/openid-configuration` → same (proves requirement 2 "BOTH").

**`BearerMcpRoundTripTests.cs`** — refactor + add:
- Parametrize `AcquireOauthTokenAsync(HttpClient, string clientId, string redirectUri)` (default the
  two existing callers to the `ClientId`/`RedirectUri` consts — a mechanical signature change).
- **T8 `Dcr_registered_client_round_trips_authorize_token_mcp`** — POST `/oauth/register`
  `{redirect_uris:["http://localhost:8765/callback"]}` → `client_id`; then
  `AcquireOauthTokenAsync(client, dcrClientId, "http://localhost:8765/callback")` → Bearer;
  `ConnectAsync` + `CallToolAsync("list_customers")` → `IsError != true`. Proves a DCR client is a
  first-class OAuth client end-to-end (requirement 7; prod/Cloudflare covered by the post-deploy probe).

---

### Risks / watch-items for reviewer + Fable

- **redirect_uri exact-match (footgun):** the endpoint stores the parsed `Uri` **exactly as the seeder
  does** (`RedirectUris.Add(new Uri(raw))`) — do NOT re-normalize through a different path. OpenIddict
  exact-string-matches the authorize-time `redirect_uri` against this; the seeder proves the Claude
  URIs round-trip. Reviewer: confirm no divergent normalization crept in.
- **Metadata serialization (one inferred link):** `AttachAdditionalMetadata` makes `context.Metadata`
  the right path, but T6/T7 are the empirical gate. If they fail, use the `context.Response[...]`
  fallback (documented above) — don't let an implementer thrash.
- **Fable preference (non-blocking):** dedicated `"dcr"` rate-limit policy (10/min/IP) vs. reusing the
  existing `"login"` policy. Recommend dedicated (clean, mirrors `login` exactly). No scope impact.
- **Accepted residual:** idempotent `UpdateAsync`-on-hit means every DCR POST is a DB write (bounded by
  the limiter); distinct-uri flooding is bounded by the limiter only (dedup collapses identical
  re-registers). Both are within requirement 5's stated intent.
- **No schema, no FE change, no token/refresh/RLS/tenant change** — inside the blast-radius cap.

## Attempt log

- 2026-07-05 Fable: spec created, requirements + open questions set. Dispatching opus-designer for design.
- 2026-07-05 opus-designer: Design section filled. Verdict A=hand-rolled (OpenIddict 7.5 has zero
  server-side DCR — evidenced by empty `registration` grep on OpenIddict.Server.dll). B via
  `context.Metadata["registration_endpoint"]` folded into the existing discovery handler; both FE
  discovery routes already rewrite → zero FE change. C: existing test doesn't cover DCR (no partial
  impl; doc stale). D=deterministic `dcr-`+hash(sorted redirect_uris), find-or-create. E=shared
  `McpClientDescriptorFactory`. No migration. Ready for sonnet-implementer + opus security review.
- 2026-07-05 Fable: DESIGN APPROVED. Build exactly the Design section. Rate-limit decision = dedicated
  `"dcr"` policy (10/min/IP), as designed. Implementer watch-items: (1) keep the seeder's teas-mcp
  descriptor byte-equivalent after the factory refactor (McpClientRegistrationTests asserts its shape);
  (2) if discovery test T6/T7 fails, use the documented `context.Response[...]` fallback — do NOT thrash;
  (3) confirm the `"dcr"` limiter keys on the same forwarded-IP source as `"login"` (consistency).
- 2026-07-05 sonnet-implementer: [x] Built exactly per Design. Files: NEW
  `McpClientDescriptorFactory.cs` (verbatim); `OpenIddictSeeder.cs` (descriptor build replaced with
  factory call, scope-seeding loop + redirect-uri union kept, teas-mcp descriptor byte-equivalent —
  `Teas_mcp_client_is_public_pkce_with_the_claude_redirect_uris_and_mcp_scopes` still passes
  unmodified); `OAuthEndpoints.cs` (+`MapOAuthRegister`, +`DcrRequest`); `Program.cs` (discovery
  handler extended, `"dcr"` rate-limit policy added, `app.MapOAuthRegister()` wired after
  `MapOAuthAuthorize()`); tests T1–T8 added to `McpClientRegistrationTests.cs` (+stale-doc fix),
  `DiscoveryEndpointsTests.cs`, `BearerMcpRoundTripTests.cs` (`AcquireOauthTokenAsync` parametrized).
  **Watch-item #2 (T6/T7) DID trigger, but not as anticipated**: switching `context.Metadata[...]` →
  `context.Response[...]`/`context.Transaction.Response[...]` per the documented fallback did NOT fix
  it (context has no direct `Response`; `Transaction.Response` gets wholesale-replaced later, so a
  direct write is discarded either way). Root-caused via the actual OpenIddict 7.5.0 source
  (fetched from GitHub tag 7.5.0): the handler's `SetOrder(int.MaxValue - 100_000)` ties EXACTLY with
  OpenIddict's own `AttachIssuer` (same constant), which runs BEFORE `AttachEndpoints` (+1_000) sets
  `context.AuthorizationEndpoint` — so the `if (context.AuthorizationEndpoint is { } authz)` guard was
  silently null and the key was never added, with EITHER sink. The pre-existing `none` addition to
  `TokenEndpointAuthenticationMethods` worked at that same order only because it's a HashSet read at
  the very end of the whole dispatch (order-independent); our code reads `AuthorizationEndpoint`
  synchronously, which is order-DEPENDENT. Fix: reverted to `context.Metadata[...]` (the original
  design, sink was never the bug) and moved `SetOrder` to `int.MaxValue - 50_000` (safely after
  `AttachEndpoints`). Confirmed by dumping the raw discovery JSON in a temporary debug assertion
  before/after — `registration_endpoint` now present, reverted the debug test back to the real
  assertion afterward. **Deviation from spec, noted per Ponytail (solution simplified, scope
  unchanged)**: same design intent (fold into the existing inline handler, `context.Metadata` sink)
  as approved — only the numeric `SetOrder` argument changed, plus an explanatory comment.
  **Evidence:** solution build **0 warnings / 0 errors** (`dotnet build`). OAuth suite **44/44 passed,
  0 skipped, ×2 consecutive** on teas_test (`--filter FullyQualifiedName~Accounting.Api.Tests.OAuth`).
  Broader `Accounting.Api.Tests.Mcp` namespace **36/36 passed** (no regressions). DCR tests T1–T8 all
  present and green in both runs (`Register_returns_201_public_client_with_fixed_scopes_and_no_write_scope`,
  `Register_rejects_non_https_non_loopback_redirect_uri` ×4 theory cases, `Register_ignores_requested_scope_and_grant_injection`,
  `Register_requires_redirect_uris`, `Register_is_idempotent_same_uris_return_same_client_id`,
  `Authorization_server_metadata_advertises_registration_endpoint`,
  `Openid_configuration_advertises_registration_endpoint`,
  `Dcr_registered_client_round_trips_authorize_token_mcp`). Security invariant proven: T1 asserts the
  created client's permissions contain every `McpScopes.All` scope + `offline_access` + the `/mcp`
  resource + the PKCE requirement, and **NO** permission ending in any `McpScopes.ForbiddenSuffixes`
  (`.post` etc.); T3 additionally proves a request injecting `sales.tax_invoice.post`, `admin.super`,
  and `client_credentials` yields a client with NONE of those — only the fixed policy. Not committed
  (per instructions) and not `git add`ed.
- 2026-07-05 sonnet-implementer (requirement 8 / F1 fix): [x] `frontend/app/(dashboard)/oauth/consent/page.tsx`
  now shows the parsed `redirect_uri` origin (`new URL(...).origin`, try/catch guarded) in a new
  `data-testid="consent-redirect-host"` row inside the existing client-info box, labelled via a new
  `oauthConsent.redirectLabel` i18n key. For `client_id` starting with `dcr-`, a
  `data-testid="consent-dcr-warning"` `role="alert" className="alert alert-warning"` block renders
  (matches the existing warning pattern in `PostConfirmDialog.tsx`), text from new key
  `oauthConsent.dcrWarning`. Both keys added to `frontend/messages/en.json` and `th.json` (same
  `oauthConsent` namespace the page already uses). No login/membership/accept-BFF/backend change —
  display-only. Evidence: `tsc --noEmit` exit 0; full `next build` exit 0 (`/oauth/consent` route
  compiles, 2.52 kB). Bengali glyph grep on `messages/` -> 0 matches (checked before AND after the
  Thai addition). No existing e2e/Playwright test references `consent-*` testids outside the page
  itself (grepped repo-wide), so no test breakage; new testids added proactively per spec ask.
  `next lint` could not run non-interactively (no eslint config file present in `frontend/`; `next
  lint` wants to scaffold one on first run) -- noted, not in this task's blast radius to add. Not
  committed.
- 2026-07-05 Fable: full-diff review APPROVE. Commit `6a8a233` -> PR #43 -> CI green (backend 7m55s,
  frontend 30s) -> merged main `f3351e5` -> release-please PR #44 -> merged -> tag `v1.11.0`
  (MinVer stamp `1.11.0+3c11c44`).
- 2026-07-05 Fable: DEPLOYED to prod (`ubuntu@158.69.197.154`, both tiers, self-contained swap +
  auto-rollback scripts). API: `DEPLOY_OK http=200 status=online registration_endpoint=present
  dcr_register=201` (DB backup taken; `none` still advertised, no regression). FE: `BUILD_OK` +
  `FE_DEPLOY_OK`. PUBLIC verify over Cloudflare: `registration_endpoint =
  https://teas.kazaki-rio.com/oauth/register`; `POST /oauth/register` -> 201 + `client_id
  dcr-924735bf7dd2ece45989e45e98825f1a`, scope = read/create/manage only (NO `.post` — invariant holds
  in prod). Server-side DONE + verified end-to-end. Remaining = the human Claude-Desktop connector test.

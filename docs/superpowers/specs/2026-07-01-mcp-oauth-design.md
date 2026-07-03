# TEAS Connect (MCP) — OAuth 2.1 Authorization Server — Design

**Date:** 2026-07-01
**Status:** DRAFT — Ham approved building OAuth (§11 gate cleared). Consulting Codex + AGY.
**Goal:** let **Claude Desktop + Mobile native connectors, Codex CLI, and Gemini Spark**
connect to TEAS Connect via **standard MCP OAuth** (not Claude-specific). Build on a
branch (`feat/mcp-oauth`) with tests; **do NOT run the prod migration / deploy without Ham**.

## 1. Why OAuth (recap)

Native connectors (Claude Desktop/Mobile, and per Ham's ask Codex + Gemini Spark) take a
**URL only** and require **OAuth 2.1 + PKCE** for private data; there is no static-header
field. TEAS is all-private → OAuth is mandatory for those clients. X-Api-Key (Claude Code,
Desktop-via-mcp-remote) keeps working unchanged.

MCP auth spec: protected resource returns **401 + `WWW-Authenticate: Bearer
resource_metadata="…"`** → client fetches **RFC 9728** `/.well-known/oauth-protected-resource`
→ discovers the AS via **RFC 8414** `/.well-known/oauth-authorization-server` → **RFC 7591**
Dynamic Client Registration (or a pre-registered client) → auth-code + **PKCE** → Bearer on `/mcp`.

## 2. Chosen approach — OpenIddict (reuse everything downstream)

**Library: OpenIddict `7.5.0`** (2026-04-22; targets .NET 10, EF Core 10 — verified). Ponytail
rung 4: use the library, never hand-roll an AS (security footgun). Provides `/authorize`,
`/token`, `/register` (DCR), discovery, PKCE, refresh, token validation. New dependency (Ham
approved). Packages (→ `backend/Directory.Packages.props` + Infrastructure & Api csproj):
`OpenIddict.AspNetCore` 7.5.0 (server+validation) + `OpenIddict.EntityFrameworkCore` 7.5.0
(stores; pulls Core/Server/Validation transitively).

**The load-bearing insight:** the OAuth access token, when validated on `/mcp`, only needs to
produce a `ClaimsPrincipal` carrying the SAME claims the X-Api-Key handler emits — `TenantClaims.CompanyId`,
`BranchId`, `Scopes` (CSV), an actor name (`ClaimTypes.Name` + `ApiKeyName`-equivalent), `IsApiKey`.
Then `HttpTenantContext` → RLS → `apiperm:*` scope gates → MCP tools all work **unchanged**.
So OAuth is an alternate *front door* to the exact same tenant/scope machinery.

### Endpoints (all under the backend, exposed publicly via Next passthrough like `/mcp`)
- `GET /.well-known/oauth-protected-resource` — RFC 9728, **anonymous**. Points to the AS + lists scopes.
- `GET /.well-known/oauth-authorization-server` (+ `/.well-known/openid-configuration`) — RFC 8414, **anonymous** (OpenIddict serves this).
- `POST /oauth/register` — RFC 7591 DCR, anonymous (OpenIddict).
- `GET /oauth/authorize` — interactive (browser). See §3.
- `POST /oauth/token` — code→token (PKCE), anonymous (client-authenticated).
- `/mcp` — now accepts **Bearer** (OpenIddict validation) in addition to X-Api-Key. 401 carries `WWW-Authenticate`.

### Storage
OpenIddict EF Core stores (applications/authorizations/scopes/tokens) in a new **`oauth`**
schema → **one EF migration** (the §11/§6 schema change; I own it, build-first, never `--no-build`).

## 3. The `/authorize` + consent flow (the hard part — reuse TEAS login)

TEAS auth = username/password → JWT in an httpOnly cookie (set by the Next BFF). The OAuth
authorize step must authenticate a human + let them consent. Proposed (fits Next+API):

1. Client opens `GET /oauth/authorize?client_id&redirect_uri&code_challenge&scope&state&resource`.
2. Backend validates the request (OpenIddict). If no valid TEAS session → **302 to the Next
   `/login?returnTo=/oauth/consent?…`**.
3. **Next `/oauth/consent` page** (logged-in): shows the client name + "TEAS Connect will
   **read + draft** documents (cannot post)" + a **company picker** (user's companies) + the
   requested scopes. User picks a company and approves.
4. On approve → BFF calls a backend `POST /oauth/authorize/accept` (with the session JWT) →
   OpenIddict issues the **authorization code** bound to {user, chosen company_id, granted
   scopes, PKCE challenge} → returns the redirect to `redirect_uri?code&state`.
5. Client `POST /oauth/token` with the code + PKCE verifier → **access token** whose claims =
   {company_id, branch_id (HQ), scopes (read+create, NO .post), actor="oauth:<user>"}.

**Scope safety (mirror the mcp-kind guard §M1):** the AS only grants the MCP read+create scope
set; **`*.post` is never grantable** to an MCP OAuth token (structural — an agent cannot post).

## 4. Resource server — `/mcp` accepts Bearer

- Add OpenIddict **validation** as a second auth scheme on `/mcp` (keep `ApiKey` too).
  `ApiKeyOnlyPolicy` becomes "ApiKey OR Bearer(OpenIddict)".
- A small claims-transform maps the validated OAuth token → the `TenantClaims.*` set
  `HttpTenantContext` expects (company_id from the token, scopes from the token's `scope`).
- 401 on `/mcp` must emit `WWW-Authenticate: Bearer resource_metadata="{BaseUrl}/.well-known/oauth-protected-resource"`.

## 5. Public routing (Next passthrough)
Add Next passthroughs (anonymous, like `app/mcp/route.ts`) for `/.well-known/oauth-*`,
`/oauth/register`, `/oauth/token` (forward as-is), and route `/oauth/authorize` +
`/oauth/consent` through Next (authorize 302s into the Next login/consent page). No cookie
forwarded to the token/register/well-known passthroughs (they're client-authenticated / public).

## 6. Client compatibility (AGY research, 2026-07-01 — confirmed)

**All four target clients support remote OAuth MCP with the SAME flow:** 401+`WWW-Authenticate`
→ RFC 9728 `/.well-known/oauth-protected-resource` → RFC 8414 `/.well-known/oauth-authorization-server`
→ **DCR (RFC 7591)** by default → **Auth Code + PKCE `S256`**. All have a **manual pre-registration
fallback** (enter client_id/secret) if DCR is unavailable. So: **support BOTH DCR and manual
pre-registration.** Current MCP spec revision = **2025-11-25** (SEP-985 aligned RFC 9728).

| Client | Register | Grant | Notes |
|---|---|---|---|
| Claude Desktop | DCR (+ manual in Advanced) | Auth code + PKCE S256 + `resource` | — |
| Claude Mobile | synced from claude.ai web | same | connectors added on web, auto-sync to mobile |
| Codex CLI | DCR via `codex mcp login <srv>` | same | fallback: `bearer_token_env_var` in `~/.codex/config.toml` |
| Gemini Spark | DCR (+ manual in web UI) | same | Gemini CLI has DCR quirks → manual fallback matters |

**NEW requirement (RFC 8707):** clients pass the **`resource` param** (the MCP server URL) on
authorize + token. The AS must **validate it and encode the target in the token `aud` claim**;
the resource server checks `aud` = its own `/mcp` URL. Add this to the authorize/token handlers
and the Bearer validation.

Client-specific setup strings (for P4 setup panel / docs): Claude = URL in "Add custom connector";
Codex = `codex mcp login teas` (or `~/.codex/config.toml`); Gemini Spark = "Connected Apps" web UI.

## 6b. Codex review (2026-07-01) — MUST-FIX, folded into the design (BINDING)

Codex confirmed the "OAuth token → same TenantClaims → reuse everything" approach is sound
but found 5 blockers + hardening. These are now requirements:

**Claim equivalence (the token MUST emit, via OpenIddict claim destinations on the access token):**
- `is_api_key = "true"` — **load-bearing**: `PermissionHandler`/`PermissionPolicyProvider` only
  read the CSV `scopes` when `is_api_key` is set; without it they fall back to JWT `perm` claims
  and **deny every MCP tool call**. (`PermissionRequirement.cs:17-40`, `TenantClaims.cs:12-16`.)
- `company_id`, `branch_id` — **positive + valid + related**; reject the token if either is 0
  (silent-zero already caused duplicate doc-number sequences). Hard-reject at validation.
- `sub` = the immutable **numeric TEAS user id**; `ClaimTypes.Name` = actor name.
- CSV `scopes` = the granted MCP set (normalized).
- **NEVER emit `is_super_admin=true`** — it bypasses the EF tenant filter AND sets the Postgres
  super-admin RLS flag (`AccountingDbContext.cs:143-145`, `TenantMiddleware.cs:35-37`). Hard exclude.
- No `api_key_id` (OAuth ≠ api key); provenance = user `sub` + `client_id` + token id (see audit).

**Authorization scheme (blocker #2):** `apiperm:*` policies hard-wire `.AddAuthenticationSchemes(ApiKey)`
(`PermissionPolicyProvider.cs:28-36`) → a Bearer principal is discarded/re-authed as ApiKey and
fails. **Refactor `PermissionPolicyProvider` + `ApiKeyOnlyPolicy` to accept BOTH `ApiKey` and the
OpenIddict validation scheme.** Use a **policy/forward scheme on `/mcp` that selects exactly ONE
credential** (ApiKey XOR Bearer); reject requests carrying both; centralize the `/mcp` 401 so
Bearer gets `WWW-Authenticate: Bearer resource_metadata=…` (not the ApiKey JSON body).

**Consent bridge (blocker #3):** do NOT round-trip `client_id`/`redirect_uri`/PKCE/`resource`/
scopes/company through editable browser fields. Use **OpenIddict's server-side authorization-request
cache** keyed on an **opaque one-time handle** bound to the TEAS session; the accept endpoint
**re-enters the validated OpenIddict pipeline** and finishes with `SignIn(principal,
OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)` (deny = `Forbid(...)`, never manual code
construction). BFF accept needs antiforgery + SameSite + Origin/Referer checks. OpenIddict validates
exact registered `redirect_uri` + PKCE **S256** before consent.

**Scope guard (blocker #4, positive allowlist):** define `McpAllowedScopes` (read+create only).
`granted = request.GetScopes() ∩ McpAllowedScopes`; reject unknown/`.post` as `invalid_scope`.
`principal.SetScopes(granted)` before `SignIn` (authoritative — not browser-posted). Token endpoint
**preserves** scopes from the code/refresh principal, never rebuilds; re-check no-`.post` on refresh.
Restrict each MCP application via `OpenIddictApplicationDescriptor.Permissions` (`Prefixes.Scope+scope`);
DCR assigns fixed MCP permissions **server-side** (clients can't self-grant `.post`).

**Tokens/compliance (blocker #5 + E):** access token **5–15 min**; **rotating refresh** with reuse
detection + short absolute lifetime (~1 workday). `company_id` baked at grant — switching the web
UI's active company must NOT retarget an existing token; refresh keeps the original company. On every
refresh revalidate user status + company membership/activation + revocation; revoke the token family
on membership removal / credential reset / refresh reuse. Partition `/mcp` **Bearer rate-limit** by
token/client (not the shared `__no_api_key` bucket — `Program.cs:150-169`). Audit: user `sub` + actor
+ `client_id` + auth/token id + company.

**DCR:** verify OpenIddict DCR support; if constrained, pre-register the supported clients with a
policy. (AGY is confirming which of Claude/Codex/Gemini need DCR vs a pre-registered client.)

## 7. Phased build (SDD; I own migration + security-critical bits)
- **P1 — AS foundation:** OpenIddict deps + EF stores + migration + discovery (RFC 8414/9728,
  anonymous) + `/token` + `/register` (DCR) + PKCE + seed the MCP scope set. Smoke test: discovery docs resolve.
- **P2 — authorize + consent:** `/oauth/authorize` (session-gated) + Next `/oauth/consent`
  (company picker) + `/oauth/authorize/accept` → code issuance. Scope guard (no `.post`).
- **P3 — resource server:** `/mcp` accepts Bearer + claims transform → `TenantClaims`; 401 +
  `WWW-Authenticate`. Keep X-Api-Key. Integration test: full OAuth round-trip → tool call.
- **P4 — FE + Next passthroughs:** OAuth endpoint passthroughs; setup panel adds the native-
  connector instructions (URL only). Gates: build 0/0 · tsc 0 · new tests ×2 green.
- **P5 — docs + PR** (no deploy; Ham runs migration + deploy).

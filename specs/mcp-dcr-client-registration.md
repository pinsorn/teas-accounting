# MCP OAuth — Dynamic Client Registration not supported (NEW finding, 2026-07-04)

**Priority: LAST.** Ham's instruction: fix this only AFTER every 2026-07-04 review fix is
done (H2, M3, M12, and ALL of Wave 4 including the low findings). Footgun (auth/OAuth) →
Fable co-authors the design, a worker implements, Codex/Opus Tier-2 review before commit.

## Symptom (screenshot, 2026-07-04)
Claude's MCP connector "Connect to TEAS" (`https://teas.kazaki-rio.com/mcp`) shows:
> ⚠️ Automatic client registration isn't supported by TEAS. Edit the connector and add an
> OAuth Client ID. If this persists, share this reference with support: "ofid_f44ec015c7dec447"

`ofid_…` is a Claude-side connector reference (for Anthropic support), not a TEAS id. "You
are not connected to TEAS yet." → the connect handshake fails at client registration.

## Diagnosis (hypothesis — VERIFY at resume)
Claude's MCP connector, on a fresh connect, tries **Dynamic Client Registration** (RFC 7591 /
OIDC DCR): it reads the AS metadata, finds (or expects) a `registration_endpoint`, and POSTs to
self-register a client to obtain a `client_id`. TEAS's OpenIddict AS almost certainly does NOT
enable/advertise the registration endpoint (OpenIddict ships DCR OFF by default), so the
connector has no way to auto-register → the "add an OAuth Client ID" fallback.
The rest of OAuth works (consent/refresh/scope — the 2026-07-04 H4/M11 fixes); this is the
missing DCR piece, which Claude's connector uses by default.

## Investigation steps (when resumed — warm start)
1. Fetch the AS metadata: `GET https://teas.kazaki-rio.com/.well-known/oauth-authorization-server`
   (and `/.well-known/openid-configuration`) — is `registration_endpoint` present? (Almost
   certainly absent.)
2. `backend/src/Accounting.Api/**` OpenIddict server config (Program.cs ~:105-159 + OAuth/):
   is the client-registration endpoint enabled (`SetClientRegistrationEndpointUris`, the DCR
   feature/handler)? How are clients registered today — `OpenIddictSeeder` seeding a STATIC
   client? (If so, DCR was never wired.)
3. Confirm what Claude's connector actually POSTs (redirect_uris, scopes, token_endpoint_auth_method)
   so the registration handler validates/echoes the right fields.

## Fix direction (design at resume; do NOT pre-commit an approach)
Enable OpenIddict Dynamic Client Registration (RFC 7591): expose + advertise a
`registration_endpoint`, add a handler that creates a client application with:
- redirect_uris validated (only Claude's callback host(s) — do not allow arbitrary),
- the MCP scope set (`McpScopes.All`) — NOT unlimited; note the H4 fix already binds the
  *granted* scopes to the consenting user's RBAC at consent, so a self-registered client still
  cannot exceed the user, but registration should still cap requestable scopes,
- rate-limiting on the registration endpoint (abuse surface — mirror the /auth rate-limit work
  from Wave 4 M4/M5),
- `token_endpoint_auth_method` = the public/none PKCE flow Claude uses (confirm from step 3).
Security review lens (Tier-2): can DCR be abused to register a client with off-host redirect_uris
(open redirect / token theft) or escalated scopes? Is the endpoint authenticated or open (RFC 7591
open registration vs protected)? Decide open-with-constraints vs an initial-access-token gate.

## Claude connector requirements (from claude.com/docs/connectors/building/authentication, 2026-07-04)
- **Redirect URIs the client MUST register** (this is likely broken even for a manual client):
  - `https://claude.ai/api/mcp/auth_callback` (hosted claude.ai / Desktop / mobile / Cowork)
  - `http://localhost/callback` AND `http://127.0.0.1/callback` (Claude Code, RFC 8252 loopback —
    port varies per session → the server must do **port-agnostic** matching, RFC 8252 §7.3).
- **DCR is OPTIONAL — three supported paths** (pick one):
  1. Implement `registration_endpoint` (RFC 7591 DCR) → Claude auto-registers. Seamless for any client.
  2. **CIMD** (Client ID Metadata Document): advertise `"client_id_metadata_document_supported": true`
     AND include `"none"` in `token_endpoint_auth_methods_supported`.
  3. **Manual "custom credentials"** (matches the screenshot's "add an OAuth Client ID"): pre-register
     ONE public client for Claude; Ham pastes its Client ID into the connector. Client Secret is
     OPTIONAL (public/PKCE client). Lowest effort, NO new open-registration surface.
- **Metadata (RFC 8414 AS metadata or OIDC discovery at `/.well-known/…`, OR a 401 with
  `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"`)** must expose:
  `code_challenge_methods_supported: ["S256"]`; `token_endpoint_auth_methods_supported` incl. `"none"`;
  protected-resource `resource` == the exact MCP URL; `authorization_servers` = issuer.
- **Already satisfied by TEAS** (per the 2026-07-04 H4/M11 review — verified SOUND): PKCE S256
  mandatory, `authorization_code` + `refresh_token` grants, refresh-token rotation, RFC 8707 audience,
  `invalid_grant` errors. So the gap is ONLY registration + (maybe) the redirect_uris + metadata fields.

## Recommended approach (Fable's lean read — confirm with Ham)
Option 3 (manual pre-registered public client) is the lowest-risk fix and matches what the connector
itself asks for: seed ONE OpenIddict client for Claude with the three redirect_uris above, public
(token_endpoint_auth_method=none), PKCE, `McpScopes.All` as permitted scopes; hand Ham the Client ID.
FIRST check `OpenIddictSeeder` — a Claude client may already exist but be MISSING the
`https://claude.ai/api/mcp/auth_callback` redirect_uri (which alone would break connect). If Ham wants
zero-touch for future clients, do Option 1 (DCR) instead — bigger surface, needs the registration
security review. Either way verify the AS metadata advertises the required fields + the loopback
redirect uses port-agnostic matching.

## Compliance / caution
This touches e-Tax-adjacent auth but not §4 tax rules. It IS a change to the OAuth AS surface →
ASK Ham before shipping (autonomy boundary: auth surface change), especially Option 1 (open/gated DCR).
Verify against the real prod endpoint (teas.kazaki-rio.com) — this is a live connector Ham uses.

---

## INVESTIGATION + Option 3 implementation (2026-07-04, Sonnet — dev only, NOT deployed)

### 1. `OpenIddictSeeder` — was a Claude client already seeded?
Yes, partially. `teas-mcp` already existed: `ClientType = ClientTypes.Public` (no secret),
`ConsentType = Explicit`, PKCE required (`Requirements.Features.ProofKeyForCodeExchange`), grants
`AuthorizationCode` + `RefreshToken`, scopes = `McpScopes.All` + `offline_access`. **Missing:** the
redirect_uris. `DefaultRedirectUris` hardcoded only `http://localhost:8765/callback` (the local/dev
+ integration-test loopback); real per-client callbacks were meant to come from prod-only config
`Oauth:RedirectUris` (env `Oauth__RedirectUris__N`), which is **not set anywhere in the repo**
(`docs/mcp-oauth-deploy-gates.md` §3 already flagged this as an open TODO before this branch — "add
each native connector's real callback URL... collect the exact values"). So the client existed but
could not complete a redirect to Claude — confirming the doc's own hypothesis almost verbatim.

### 2. Root cause confirmed: missing redirect_uri, NOT a missing client
`https://claude.ai/api/mcp/auth_callback` was absent from `teas-mcp`'s registered set. OpenIddict
validates the **exact** registered redirect_uri (confirmed empirically, see §"loopback" below) — a
mismatch alone produces `invalid_request` and blocks connect regardless of anything else being
correct. Fixed by extending `OpenIddictSeeder.DefaultRedirectUris` (hardcoded, not env-config — these
three URLs are public, standard, Claude-documented values, not per-deployment secrets) with:
`https://claude.ai/api/mcp/auth_callback`, `http://localhost/callback`, `http://127.0.0.1/callback`.
The seeder is idempotent (`FindByClientIdAsync` → `CreateAsync`/`UpdateAsync`) so this reconciles on
next prod API restart with no migration.

### 3. AS metadata — one gap found and fixed, one confirmed sound
- `code_challenge_methods_supported` → `["plain","S256"]`, includes `S256`. Sound, matches H4/M11
  review.
- `grant_types_supported` → `["authorization_code","refresh_token"]`. Sound.
- `token_endpoint_auth_methods_supported` → **was `["client_secret_post","private_key_jwt",
  "client_secret_basic"]` — "none" was ABSENT** (confirmed by querying the live discovery endpoint
  in-process). OpenIddict's built-in `AttachClientAuthenticationMethods` discovery handler never
  adds `"none"` on its own, even though a `ClientTypes.Public` client already authenticates with no
  secret today (proven by the 21+ existing OAuth round-trip tests). This is advertising-only, not an
  enforcement gap — fixed with one `UseInlineHandler` on `HandleConfigurationRequestContext`
  (`SetOrder(int.MaxValue - 100_000)` so it appends after the built-in handler populates the list).
  Verified: `token_endpoint_auth_methods_supported` now includes `"none"`.

### Loopback port-agnostic matching — STOP, flagged for Fable/Ham (per dispatch's stop condition)
**Confirmed empirically, NOT implemented.** Registered `http://127.0.0.1/callback` (implicit :80);
requested `http://127.0.0.1:54321/callback` at `/oauth/authorize` → OpenIddict rejected with
`invalid_request` / `error_uri: https://documentation.openiddict.com/errors/ID2043` ("The specified
'redirect_uri' is not valid for this client application"). **OpenIddict does exact string matching,
including port — there is no built-in RFC 8252 loopback exception.** If Claude Code's local OAuth
listener picks a genuinely ephemeral port each session (as RFC 8252 native apps typically do), the
two loopback URIs registered above will only work for the literal port implied (80) and will NOT
match a real Claude Code session using a random port. Making this port-agnostic would require a
custom handler that intercepts redirect_uri validation (e.g. running before
`OpenIddictServerHandlers.Authentication.ValidateClientRedirectUri` and normalizing away the port for
127.0.0.1/localhost hosts specifically) — a change to core redirect_uri validation logic, which is
squarely the kind of security-sensitive surface (open-redirect / token-theft risk if scoped wrong)
this dispatch said to stop and surface rather than force. **Not implemented.** The primary reported
symptom (hosted claude.ai connector, fixed HTTPS callback, no port) is unaffected by this gap and is
fixed by item 2 above. Decide separately whether/how to support Claude Code's loopback flow.

### CHANGED (dev only, NOT deployed)
- `backend/src/Accounting.Api/OAuth/OpenIddictSeeder.cs` — added 3 redirect_uris to
  `DefaultRedirectUris`.
- `backend/src/Accounting.Api/Program.cs` — added `using OpenIddict.Abstractions;` +
  one `HandleConfigurationRequestContext` inline handler appending `"none"`.
- NEW `backend/tests/Accounting.Api.Tests/OAuth/McpClientRegistrationTests.cs` — 2 tests: the
  seeded `teas-mcp` client is public/PKCE with the 3 redirect_uris + all MCP scopes + both grants
  (queries `IOpenIddictApplicationManager` directly); discovery advertises `"none"`.

### EVIDENCE
Build `dotnet build backend/Accounting.sln -c Debug` → 0 errors / 0 warnings. New tests 2/2 pass ×2
consecutive. `OAuth` + `Mcp` namespaces (69 tests, incl. all pre-existing regression guards) pass ×2
consecutive. Full `Accounting.Api.Tests` suite: 601 passed / 8 skipped / 0 failed (clean run).

### Client ID for Ham to paste into the connector
**`teas-mcp`** (`OpenIddictSeeder.McpClientId`) — public client, no secret, PKCE only.

### Prod-seeding steps Ham must run
1. Deploy this branch's backend (API republish) — `OpenIddictSeeder` is a real `IHostedService`;
   the new redirect_uris + the metadata handler take effect on the next API startup automatically,
   no migration, no manual DB edit.
2. Re-verify discovery live: `GET https://teas.kazaki-rio.com/.well-known/oauth-authorization-server`
   → confirm `token_endpoint_auth_methods_supported` includes `"none"`.
3. In Claude's connector ("Connect to TEAS" → Edit connector), paste **Client ID `teas-mcp`**, leave
   Client Secret blank (public/PKCE), and retry connect.
4. If Ham (or a user) ever wants Claude Code (CLI) loopback support to work reliably, that needs the
   port-agnostic-matching design decision above FIRST — flag before promising it.

### Tier-2 review flag (per dispatch — auth-surface change, be precise)
Both changes here are low-risk by construction (additive redirect_uris to an existing public client
already capped to `McpScopes.All`; a metadata-advertising-only addition with no enforcement change),
but this IS the OAuth AS surface — route through the same Tier-2 (Opus/Codex) review used for H4/M11
before merge, per CLAUDE.md's routing ladder for footgun-zone auth work. Specifically confirm: (a) the
3 hardcoded redirect_uris are the correct, current, exact values Claude's connector uses (they came
from the design spec, not re-derived here) — a wrong host would either fail closed (safe) or, if
sloppy, open a redirect to an unintended host; (b) the `"none"` metadata addition should not be
misread as weakening auth — it only ADVERTISES what `ClientTypes.Public` already does.

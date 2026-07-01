# MCP OAuth — Deploy Gates (for Ham)

Branch `feat/mcp-oauth`. The OAuth 2.1 AS (OpenIddict) is built + tested locally but **NOT deployed**.
This is the checklist to turn it on in prod. Nothing here runs automatically — Ham runs the migration
+ deploy (§10/§11).

## 1. Persistent signing + encryption certificates (REQUIRED before prod)

`Program.cs` currently uses `AddEphemeralEncryptionKey().AddEphemeralSigningKey()` — DEV only. Ephemeral
keys regenerate on every API restart → **all issued tokens (and refresh tokens) become invalid on each
restart/redeploy**. Before prod, replace with persistent X509 certs:

```csharp
o.AddEncryptionCertificate(<thumbprint-or-file>)
 .AddSigningCertificate(<thumbprint-or-file>);
```

Generate a pair (or reuse the instance's), store off-repo (like the MFA key / JWT signing key), and load
by thumbprint or a path in appsettings. Keep them stable across deploys.

## 2. `App__BaseUrl` MUST be the public HTTPS origin

Everything RFC-8707/9728 derives from `App:BaseUrl`:
- RFC 9728 `/.well-known/oauth-protected-resource` → `resource = {BaseUrl}/mcp`, `authorization_servers = [{BaseUrl}]`
- `RegisterResources({BaseUrl}/mcp)` + `AddAudiences({BaseUrl}/mcp)` (the token `aud`)
- The seeder's scope resources + the authorize redirect targets (`{BaseUrl}/login`, `{BaseUrl}/oauth/consent`)

Set prod env **`App__BaseUrl=https://teas.kazaki-rio.com`** (same value already needed for PDF/approval
deep-links, see cont.119). If it's wrong, discovery + aud + redirects all point at the wrong host and no
client can connect.

## 3. Real redirect URIs per client (seeder)

`OpenIddictSeeder` currently registers the `teas-mcp` client with ONE loopback redirect URI
(`http://localhost:8765/callback`) for local/dev + the integration test. Before prod, add each native
connector's real callback URL to `RedirectUris` (OpenIddict validates the **exact** registered URI):
- Claude Desktop/Mobile — the claude.ai connector callback
- Codex CLI — its loopback callback
- Gemini Spark — its "Connected Apps" callback

(Collect the exact values from each client's connector setup. OpenIddict rejects any unregistered
redirect_uri, so these must match precisely.)

## 4. Run the prod migration (build-first — §6 footgun)

One new migration `20260701072643_AddOpenIddict` creates the `oauth` schema (4 OpenIddict tables,
additive — no existing table touched). Apply on prod as part of the deploy. **NEVER `dotnet ef … --no-build`
after entity edits** — build the solution first. `DbInitializer.MigrateAsync` also applies it automatically
on API startup, so a normal API republish from this branch will run it. Take a DB backup first.

## 5. Deploy both tiers

- **API republish** (backend: OpenIddict AS, `/mcp` Bearer, discovery, seeder, migration).
- **FE rebuild** (Task 5: the `/.well-known` + `/oauth/*` passthroughs, the `/oauth/consent` page, the
  accept BFF, `middleware.ts` public paths, the setup panel). Without the FE passthroughs the backend
  OAuth endpoints are not publicly reachable (Cloudflare fronts Next only — same as `/mcp` in cont.119).
- Explicitly `git add` the NEW files (backend `OAuth/`, migration, tests; frontend `.well-known/`,
  `oauth/`, `api/oauth/`) — a `git add -u` misses them → CI fresh-checkout fails.

## 6. Verify over real Cloudflare (unverifiable locally)

After deploy, from outside:
- `GET https://teas.kazaki-rio.com/.well-known/oauth-protected-resource` → 200 JSON, `resource` ends `/mcp`.
- `GET …/.well-known/oauth-authorization-server` → 200, `code_challenge_methods_supported` has `S256`.
- `POST …/mcp` with no credential → **401 with `WWW-Authenticate: Bearer resource_metadata="…/.well-known/oauth-protected-resource"`** (the SSE/header pass-through over Cloudflare is the one thing local tests can't prove).
- Add the connector in Claude Desktop (URL only) → complete the login+consent → confirm a tool call works.

## 7. Deferred follow-ups (NOT blocking a first deploy, but track them)

- **DCR (RFC 7591 `/oauth/register`)** — not implemented; clients use the pre-registered `teas-mcp`
  client (manual fallback, spec §6). Add DCR if a target client can't use pre-registration.
- **Per-refresh membership revalidation (§6b)** — refresh currently re-issues from the stored principal
  (company-pinned, rotating, reuse-detected) but does NOT re-check that the user is still an active member
  of the company on each refresh. A removed/deactivated user's agent can keep refreshing until the **8-hr
  absolute refresh lifetime** expires (access tokens are 10 min). To close this to ≤10 min, add an
  OpenIddict `ProcessSignIn` event handler that, on `refresh_token` grant, revalidates user status +
  company membership and rejects (`invalid_grant`) + revokes the family otherwise.
- **Consent bridge mechanism** — OpenIddict 7.5 removed `EnableAuthorizationRequestCaching`, so the
  authorize params round-trip through the browser and are RE-VALIDATED server-side (client_id + exact
  registered redirect_uri + PKCE + server-side scope Normalize + membership check) rather than carried as
  an opaque handle. Security goal met (tampering → rejection/capping, not escalation); noted for review.

## Local verification already done (this branch)

Solution build 0 warnings / 0 errors · OAuth suite 21/21 (×2 on teas_test) · MCP smoke 36/36 (X-Api-Key
path unbroken) · Domain 147/147. Full OAuth round-trip (authorize→code→token→`/mcp` tool call, reads AND
writes) + refresh rotation/reuse + XOR guard + 401 WWW-Authenticate, all green in-process.

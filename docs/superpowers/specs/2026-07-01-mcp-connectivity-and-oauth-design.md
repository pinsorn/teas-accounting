# TEAS Connect (MCP) — Connectivity & Client-Auth Fix — Design

**Date:** 2026-07-01
**Author:** Claude (overseer) + Codex + AGY (review board — Ham asleep, full autonomy delegated)
**Status:** DRAFT — under review by Codex + AGY before implementation
**Trigger:** Ham — "TEAS Connect MCP doesn't even connect, and can't be used via Claude Desktop/Mobile. Fix it. This round AGY and Codex help check."

---

## 1. Problem statement

The MCP server (TEAS Connect) is fully built in-process in `Accounting.Api`
(`MapMcp("/mcp")`, tools in `Mcp/TeasMcpTools.cs`, X-Api-Key auth, draft-only write
safety). The **server logic works** (in-process smoke test passes). But **no MCP
client can reach or use it**. Two independent layers:

- **P1 — Connectivity (doesn't even connect):** the public host serves only the
  Next.js frontend; the .NET backend has no public ingress, so `/mcp` is
  unreachable from any client (even Claude Code, which *does* support static-header
  remote MCP).
- **P2 — Client auth (Desktop/Mobile):** even once reachable, Claude Desktop and
  Mobile remote connectors require OAuth 2.0; a static `X-Api-Key` header is not an
  accepted form for them. (Mobile cannot run the `mcp-remote` stdio bridge.)

## 2. Evidence (probed live, 2026-06-30/07-01)

| Probe | Result | Meaning |
|---|---|---|
| `POST https://teas.kazaki-rio.com/mcp` (valid Accept) | **307 → /login** | Next.js `middleware.ts` redirects; never reaches .NET |
| `GET .../api/v1/system/info` | **404** (`Vary: rsc, next-router-state-tree`) | Served by Next App Router, not backend |
| `GET api.teas.kazaki-rio.com/*` | **000** (DNS) | No `api.` subdomain |
| `GET .../health`, `/swagger` | **307 → /login** | Backend endpoints not publicly exposed |
| `GET .../.well-known/oauth-protected-resource` (+ AS, +/mcp) | **307 → /login** | No OAuth discovery exists |

Code confirming the topology:
- `frontend/middleware.ts:7` — `PUBLIC_PATHS = ['/login','/onboarding','/api','/_next','/favicon.ico']` — **`/mcp` absent** → no `access_token` cookie → 307 `/login`.
- `frontend/next.config.ts:17-19` — **no `/api`→backend rewrite**; "all backend access goes through BFF route handlers."
- `frontend/app/api/proxy/[...path]/route.ts:10` — browser→backend only via BFF, forwarding the JWT cookie to `BACKEND_API_URL` (internal `localhost:5080`).
- `backend/.../Program.cs:278-280` — `MapMcp("/mcp").RequireAuthorization(ApiKeyOnly).RequireRateLimiting(...)`.
- No OAuth: `AddJwtBearer` (Program.cs:71) is token **validation** only; no `/authorize`, `/token`, `/register`, or `.well-known` endpoints anywhere.

**Conclusion:** Cloudflare → Next.js only. The .NET backend (which hosts both `/mcp`
and the external `/api/v1` X-Api-Key surface) is reachable only through the Next BFF
proxy paths. The api-keys page snippet (`page.tsx:129-135`) hands users
`${origin}/mcp` = `teas.kazaki-rio.com/mcp`, which can never reach the MCP server.

## 3. P1 — Connectivity fix (single-origin BFF passthrough)

**Chosen approach (Ponytail: shortest diff, no VPS/infra change, reuses the existing
BFF pattern, keeps the snippet's single-origin assumption correct):**

1. Add a Next.js route handler `frontend/app/mcp/route.ts` that forwards
   `POST`/`GET`/`DELETE` to `${BACKEND_API_URL}/mcp`, passing the incoming
   **`X-Api-Key`** header (NOT the cookie), forwarding `Accept: application/json,
   text/event-stream` and `Content-Type`, and streaming `upstream.body` back
   **unbuffered** (mirrors `app/api/proxy/[...path]/route.ts`). `redirect: 'manual'`,
   `cache: 'no-store'`, Node runtime, `dynamic = 'force-dynamic'`.
2. Add `'/mcp'` to `PUBLIC_PATHS` in `middleware.ts` (auth is the X-Api-Key the
   handler forwards; the backend `ApiKeyOnlyPolicy` + per-tool `[Authorize]` still
   gate every call — no security loss).

**Why not nginx route `/mcp`→:5080 on the VPS:** cleaner separation, but needs
manual VPS access (plink), is outside the repo, and is not verifiable/committable
here. Keep as a documented alternative; revisit if streaming misbehaves through
Next/Cloudflare.

**Risks to verify before claiming done:**
- MCP **Streamable HTTP** (server `Stateless=true`) response may be `text/event-stream`
  (SSE) or `application/json`. The handler must stream without buffering; Cloudflare
  must not buffer SSE. → Verify with a real `initialize` handshake locally, then
  through the Next route, then (read-only) note prod behavior.
- Request-body streaming + correct header passthrough (no `Host` leakage, no cookie).
- `OPTIONS`/CORS: MCP clients are not browsers (no CORS preflight expected), but
  confirm the route doesn't 405 the methods clients use.

**Verification gates (P1):** real `initialize` + `tools/list` over (a) `:5080/mcp`
direct, (b) `localhost:3000/mcp` via the new route. `tsc --noEmit` 0. The existing
in-process smoke test still green.

## 4. P2 — Desktop/Mobile client auth (OAuth) — FACTS CONFIRMED (AGY + web, 2026-07-01)

**Confirmed by two independent sources (AGY web search + my own WebSearch of
support.anthropic.com / github.com/geelen/mcp-remote):**
- **Claude Mobile remote MCP = OAuth ONLY.** No static-header field; Mobile cannot run
  `npx mcp-remote` (the iOS/Android sandbox blocks a local Node process).
- **Claude Desktop in-app "Custom Connector" UI = OAuth only** (Client ID/Secret under
  Advanced). No static-header field either.
- **`mcp-remote` (stdio bridge, `claude_desktop_config.json`, DESKTOP ONLY)** can inject
  a static header: `--header "X-Api-Key: <key>"` (put the value in `env` + drop the
  space after the colon to dodge a Windows arg-mangling bug).
- **Anthropic connects to the MCP server from its CLOUD IP ranges** → the server must be
  **publicly reachable** (P1 satisfies this) and OAuth endpoints must be public + anonymous.

So: **Claude Code** works with a static key (CLI `--header` / `.mcp.json` `type:http`);
**Claude Desktop** works today via the `mcp-remote` bridge (no backend change); **Mobile
+ the native connector UI require OAuth** — no lazy escape, OAuth is mandatory for them.
Laziness applies to *how* we implement OAuth, not *whether*.

**Immediate (P1-only) client coverage shipped this session** (setup panel now emits all
three, `app/(dashboard)/settings/api-keys/page.tsx`): Claude Code ✅, Claude Desktop ✅
(mcp-remote), Mobile = OAuth note "in development".

MCP HTTP auth spec (current revision): protected server returns **401 +
`WWW-Authenticate`** advertising **RFC 9728** Protected Resource Metadata at
`/.well-known/oauth-protected-resource`; client then does **RFC 8414** AS metadata
discovery + **RFC 7591** Dynamic Client Registration, browser auth-code + PKCE, then
calls `/mcp` with a Bearer token.

**Options (recommendation pending Codex+AGY):**
- **A. OpenIddict-based OAuth 2.1 AS inside TEAS** — `/authorize` (reuses existing
  username/password login + a consent screen), `/token`, `/register` (DCR), the two
  `.well-known` docs, MCP server validates the issued Bearer + maps to company/scopes.
  New dependency + **DB tables (clients/tokens) = schema migration**. Bounded, native
  to the .NET stack. Likely recommendation.
- **B. Delegate to an external IdP** — TEAS has none; adds a 3rd-party + cost. Reject.
- **C. Desktop-only fallback** — document `npx mcp-remote ${url}/mcp --header
  "X-Api-Key: …"` for Desktop; Mobile unsupported. Zero backend work; fails the
  "Mobile" requirement.

**§11 compliance flag — ASK Ham before deploy:** an OAuth Authorization Server is a
new security subsystem + schema migration on a Revenue-Department-audited system.
Plan: design + implement + test on a **branch** (reversible) under the review board;
**do NOT deploy / run the prod migration** without Ham's explicit go. P1 ships first
(low-risk bug fix) regardless.

## 5. Decisions / open questions (for the review board)

1. P1 passthrough (Next route) vs nginx — recommend Next route (lazy, in-repo). ✅ default.
2. P2 OAuth scope — is Mobile a hard requirement tonight, or is "P1 + Desktop
   `mcp-remote` fallback" enough to ship, with full OAuth as a reviewed branch for
   Ham? (Leaning: ship P1 now; stage OAuth on a branch; Ham approves deploy.)
3. Does the existing X-Api-Key MCP path actually work over real HTTP? (smoke test
   bypasses transport) — verifying now.

## 6. Build order

1. ✅ Verify backend `/mcp` over real HTTP (local) — DONE. Authenticated `initialize`
   → 200 `text/event-stream` (SSE), `x-accel-buffering: no`, protocol 2025-06-18,
   serverInfo `Accounting.Api`. Server is healthy; the only gap was ingress.
2. ✅ **P1 SHIPPED + VERIFIED (local).** `frontend/app/mcp/route.ts` (X-Api-Key
   passthrough, streams SSE unbuffered) + `'/mcp'` added to `middleware.ts`
   PUBLIC_PATHS. Verified through Next :3000 → :5080: no-key → 401 JSON (not 307);
   with-key → 200 SSE `initialize`; `tools/list` returns scope-filtered tools;
   `tsc --noEmit` = 0. The existing snippet `${origin}/mcp` now resolves correctly.
   **Not yet deployed to prod** (frontend rebuild + VPS push = Ham's call / next step).
3. ✅ **P3 SHIPPED + VERIFIED (local).** The 9 list/filter MCP tools had optional
   params (`search`/`page`/`limit`/`status`/filters) marked **required** in the schema
   (no C# default → SDK requires them), so an agent omitting `search` got
   "missing required parameter 'search'". Added `= null` defaults (+ `CancellationToken
   ct = default`). Build 0/0; `list_customers` with `{}` now returns data. Backend-only
   → needs an API republish to reach prod (bundle with Ham's deploy).
4. ✅ **FE setup panel fixed + VERIFIED (tsc 0).** `api-keys/page.tsx` now emits correct
   per-client config: Claude Code (`type:http` + `X-Api-Key`), Claude Desktop
   (`mcp-remote` + env key), and a Mobile/OAuth "in development" note. i18n TH+EN added.
5. ⏳ Fold in Codex's deep verdict when it lands (AGY done; Codex runtime still running).
6. ⏳ P2 (Ham-gated): OpenIddict OAuth 2.1+PKCE AS + anonymous `.well-known/oauth-*`
   (RFC 9728/8414) + 401 `WWW-Authenticate` challenge on `/mcp` + Bearer validation
   mapping token→company+scopes. New dep + **schema migration** → build on a branch with
   tests; **do NOT run the prod migration / deploy without Ham** (§11 + §6). Execute via
   subagent-driven-development, phase by phase.
7. ⏳ progress.md prepended (done for this checkpoint); **no `git commit`** (§10 — Ham
   commits/deploys). Revoke the dev probe key `apiKeyId=5`.

## 7. Empirical appendix (local, 2026-07-01)

- `POST :5080/mcp` no key → 401 JSON `auth.missing_api_key`, **no `WWW-Authenticate`**
  (→ P2: clients can't discover OAuth). Auth runs before content-negotiation (no 406).
- `:5080/.well-known/oauth-*` → 401 (global fallback auth) → P2 discovery docs must be
  `AllowAnonymous`.
- `POST :5080/mcp` + valid mcp key → 200 `text/event-stream`, single SSE `event:
  message` frame with the JSON-RPC result. Server emits `x-accel-buffering: no` +
  `cache-control: no-cache,no-store` — the passthrough preserves these.
- Same through `:3000/mcp` (the new Next route) → identical 200 SSE. P1 proven.

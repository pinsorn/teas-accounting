# C3 — MCP agent surface (v1.27.1 prod, https://teas.kazaki-rio.com)

**Overall: NO CRIT.** The core invariant "agents DRAFT, humans POST" holds on the MCP
surface. No MCP-driven Posted state, no cross-company draft, no HTTP 500, no exploitable
`.post` capability observed. Two **defense-in-depth** gaps found (both currently
non-exploitable — see F1/F2). Company confirmed **co5** (JWT `company_id=5`, COMPANY_ADMIN
admin01). All 4 keys minted were revoked. 5 DRAFT journals left on co5 (ids below).

## PASS / FAIL per sub-area
- Attack 1 — forge `.post` on a key: **MOSTLY PASS** (direct/UPPERCASE/trailing-space/`.approve`
  all → 422 `api_key.mcp_cannot_post`). **1 gap (F1):** zero-width-space suffix bypasses the
  mcp-kind guard (non-exploitable).
- Attack 2 — post via MCP: **PASS.** No posting/approve tool exists; fake tool names →
  clean `-32602 Unknown tool`; even an integration key holding `gl.journal.post` sees no post tool.
- Attack 3 — cross-company draft: **PASS.** Draft schema has no `companyId` field (tenant bound
  to key); foreign account id → `je.account_not_found`; cross-tenant `get_journal id=1` → not found.
- Attack 4 — gate-bypass payloads on `create_manual_journal_draft`: **PASS.** All 11 payloads
  rejected cleanly or neutralized; zero HTTP 500.
- Attack 5 — auth attacks on the key: **PASS.** malformed/missing/empty/SQLi → clean 401;
  revoked key → 401 `auth.revoked_api_key`; no-scope key → empty `tools/list` + `-32600 Access
  forbidden` on call. Rate limit 120/min per key (configured; 25-burst under threshold).
- **F2 (defense-in-depth):** `/mcp` X-Api-Key auth is **kind-agnostic** — an `integration` key
  carrying `.post` authenticates at `/mcp` (harmless today only because no MCP tool consumes `.post`).

---

## F1 — mcp-kind `.post` guard bypassable via trailing zero-width unicode (denylist, not allowlist)
- **Severity:** LOW (defense-in-depth). **NOT a CRIT: confers zero capability** — see "why inert".
- **Where:** `ApiKeyService.EnforceMcpNoPostGuard` (`backend/src/Accounting.Infrastructure/Identity/ApiKeyService.cs:153`)
  uses `scope.Trim().EndsWith(".post", OrdinalIgnoreCase)` — a **denylist**. `CreateAsync` stores
  `req.Scopes` **raw**; `McpScopes.Normalize` (the allowlist ∩ `McpScopes.All`) exists but is
  **never called** on the create path. The validator only length-caps scopes (≤100 chars).
- **Repro (exact):** logged in as admin01 (co5), `POST /api/proxy/api-keys`
  `{"name":"c3-atk-1d","kind":"mcp","scopes":["gl.journal.read","gl.journal.post​"]}`
  (U+200B zero-width space appended to `gl.journal.post`).
- **Expected:** `422 api_key.mcp_cannot_post` (as the plain/UPPERCASE/trailing-space variants got).
- **Actual:** `HTTP 201` — key id=5 minted. Key list confirms stored scope literally
  `"gl.journal.post​"` on a **kind=mcp** key. `Trim()` does not strip U+200B, so
  `EndsWith(".post")` returns false and the denylist misses it.
- **Why inert today (why not CRIT):** `PermissionHandler` (`PermissionRequirement.cs`) splits the
  scope CSV with `TrimEntries` (whitespace only, not ZWSP) and matches with `StringComparer.Ordinal`
  (exact) — `gl.journal.post​` ≠ `gl.journal.post`, so it satisfies no policy. And no MCP tool
  is gated on any `.post` scope. A guard-bypassing string and an exact-permission-match are mutually
  exclusive here, so this specific bypass cannot post.
- **Why it still matters:** the stated structural invariant ("an mcp key structurally cannot hold
  any `.post` scope") is literally violated. It becomes exploitable if scope matching ever
  normalizes/trims unicode or switches to Contains/StartsWith, or if a `.post`-gated MCP tool is
  added. Root fix: normalize scopes against the `McpScopes.All` allowlist at mint (call the existing
  `McpScopes.Normalize`) instead of relying on the suffix denylist alone.

## F2 — `/mcp` endpoint authentication is kind-agnostic (integration `.post` key accepted)
- **Severity:** LOW–MEDIUM (defense-in-depth). **Not exploitable today** — no MCP tool consumes `.post`.
- **Where:** `ApiKeyAuthenticationHandler` (`Authorization/ApiKeyAuthentication.cs`) emits the key's
  `ScopesCsv` with **no `Kind` check**; the `/mcp` mount policy `McpAuthPolicy` (`Program.cs:310`)
  accepts the ApiKey scheme (OR OAuth Bearer) regardless of kind. The `.post` guard lives only on
  `kind==mcp` at mint. `integration` keys legitimately hold `.post` (M2M/`/api/v1` design) yet are
  also accepted at `/mcp`.
- **Repro:** minted `integration` key (id=7) with `["gl.journal.read","gl.journal.create",
  "gl.journal.post","sales.tax_invoice.post"]` → `201`; used it as `X-Api-Key` at `/mcp`
  `tools/list` → `200`, authenticates fine. (Only `create_manual_journal_draft` is exposed — no
  post tool — so no post is reachable.)
- **Expected (design intent):** the "agents draft, humans post" split on the MCP surface should be
  enforced by the credential, not only by the tool catalog.
- **Actual:** the split holds **solely because no MCP tool is gated on a `.post` scope**. If any
  `.post`-gated tool is ever added to `TeasMcpTools`, an integration key would satisfy it at `/mcp`.
- **Fix:** pin `/mcp` X-Api-Key auth to `kind==mcp`, or reject at the `/mcp` mount any principal
  whose scopes contain a forbidden suffix.

---

## Attack evidence log (all HTTP 200 at transport unless noted; errors are JSON-RPC/envelope)

### Attack 1 — mint `.post` on a key
| Payload (kind=mcp) | Result |
|---|---|
| `gl.journal.post` | 422 `api_key.mcp_cannot_post` ✅ |
| `gl.journal.POST` (case) | 422 ✅ |
| `gl.journal.post ` (trailing space) | 422 ✅ |
| `purchase.payment_voucher.approve` | 422 ✅ |
| `gl.journal.post​` (ZWSP) | **201 minted — F1** |

### Attack 2 — post via MCP
- `tools/call post_journal` / `approve_journal` / `create_manual_journal` → `-32602 Unknown tool`.
- `tools/list` (both mcp and integration keys) exposes only `create_manual_journal_draft`
  (a DRAFT tool) among journal tools; server instructions: "the agent can NEVER post/approve."

### Attack 3 — cross-company
- `create_manual_journal_draft` schema has no `companyId` param (tenant = key's company).
- line `accountId=1` (foreign) and `accountId=999999` → `[mcp.domain_rule] Account N not found in your company.`
- `get_journal journalId=1` (another tenant) → `[mcp.domain_rule] Journal 1 not found.`

### Attack 4 — gate-bypass on `create_manual_journal_draft` (accounts 52=1110, 54=1130 on co5)
| Case | Result |
|---|---|
| Balanced 100/100 | draft **JV-277** created (Draft) ✅ |
| Unbalanced 100/50 | `[mcp.validation] Total debit must equal total credit` ✅ |
| Nonexistent acct 999999 | `account_not_found` ✅ |
| Foreign acct id=1 | `account_not_found` ✅ |
| Empty lines `[]` | `[mcp.validation] must not be empty; needs at least 2 lines` ✅ |
| docDate=2020-01-01 (closed period) | **ignored**, `effectiveDocDate=2026-07-31`, draft **JV-278** ✅ |
| docDate=2099-12-31 (future) | **ignored**, `effectiveDocDate=2026-07-31`, draft **JV-279** ✅ |
| Injection in description+memo (`');DROP TABLE`, `<script>`, `${jndi:...}`) | stored as literal text, draft **JV-280** — no execution ✅ |
| Huge amount `1e30` (> decimal max) | `[mcp.bad_input] JSON value could not be converted` ✅ (no 500) |
| Malformed docDate `"not-a-date"` | `[mcp.bad_input]` ✅ (no 500) |
| Both Dr+Cr on one line | `[mcp.validation] each line pure debit or pure credit` ✅ |
| Negative amounts | `[mcp.validation] must be >= 0` ✅ |
| 100 lines balanced | draft **JV-281** created ✅ (no 500) |

**docDate is server-ignored and pinned to today (Asia/Bangkok)** → the closed-period and
future-date attack classes are structurally neutralized for this tool.

### Attack 5 — auth
| Case | Result |
|---|---|
| malformed key `key_totally_bogus_12345` | 401 `auth.invalid_api_key` ✅ |
| missing `X-Api-Key` header | 401 `auth.missing_api_key` ✅ |
| empty `X-Api-Key` | 401 `auth.missing_api_key` ✅ |
| SQLi-shaped key `' OR 1=1--` | 401 `auth.invalid_api_key` ✅ (no 500) |
| revoke key id=8 then use | 401 `auth.revoked_api_key` ✅ (immediate) |
| no-scope key `tools/list` | `{"tools":[]}` ✅ |
| no-scope key call `create_manual_journal_draft` | `-32600 Access forbidden` ✅ (no draft) |
| 25× burst `tools/list` | all 200 (per-key limit is 120/min — under threshold) |

---

## Cleanup / state left on prod (co5)
- **Keys minted then REVOKED:** id 5 (c3-atk-1d, mcp, holds `gl.journal.post​`),
  id 6 (c3-mcp-legit, mcp), id 7 (c3-int-post, integration), id 8 (c3-noscope, mcp).
  All confirmed `revoked` in the key list.
- **DRAFT journals left on co5 (per instructions — not posting; humans may delete):**
  JV-277, JV-278, JV-279, JV-280 (contains injection-string memo/description, harmless text),
  JV-281 (100 lines). All status=Draft, GL unmoved.

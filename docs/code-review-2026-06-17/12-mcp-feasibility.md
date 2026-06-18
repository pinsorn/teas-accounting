# MCP Server on TEAS — Feasibility & Build Spec — 2026-06-18

**Verdict: YES, and simpler than a proxy.** TEAS already ships the external surface (`/api/v1/*`, `X-Api-Key`, per-action RBAC scopes, tenant isolation via `ApiKeyResolver`→company_id + RLS, idempotency middleware). .NET has a first-party MCP SDK (`ModelContextProtocol` + `ModelContextProtocol.AspNetCore`, Microsoft+Anthropic) that hosts an MCP server **inside the existing ASP.NET Core app** — no separate Node/Python proxy.

## Architecture — in-process
Host MCP in `Accounting.Api`:
```csharp
builder.Services.AddMcpServer()
    .WithHttpTransport(o => o.Stateless = true)
    .AddAuthorizationFilters()        // [Authorize(...)] on tools
    .WithTools<TeasMcpTools>();
app.MapMcp();                         // MCP endpoint, behind the X-Api-Key scheme
```
Tools = C# methods (`[McpServerTool]`) that call the **existing Application services directly** (no HTTP hop) → reuse domain logic, `ITenantContext`, RLS, FluentValidation. Auth = the existing **X-Api-Key** scheme on the MCP endpoint; per-tool `[Authorize]` maps to the existing `apiperm:*` scopes.

## Two consumer profiles (one API, gated by key kind + scope)
| Profile | Who | Scopes | POST |
|---|---|---|---|
| **integration** (default) | other apps / ERP / e-commerce (deterministic) | full incl `*.post` | **direct** — they send exact data; correctness is their job. No human-approve (would break M2M). |
| **mcp** (new kind) | AI agents (LLM, via MCP) | `*.read` + `*.create` (draft) only | **NOT direct** — draft → human approves → human posts |

The guard lives in the **key's kind + scope**, NOT the endpoint (so M2M keeps direct post).

## API-key kind (new)
- `api_keys.kind` column: `integration` (default; backfill existing) | `mcp`.
- mcp keys: default scope = read + `*.create`; **reject granting `apiperm:*.post` to a kind=mcp key** (compliance belt — an agent key structurally cannot post).
- Key-mgmt UI/endpoint: create an `mcp` key (separate category from integration keys).

## Agentic write safety (chosen model: human-approve via deep-link)
The server already enforces *structural* correctness (VAT derived server-side, doc_date pinned, period gate, GL balance, THB-guard, validators) → an agent cannot post a malformed doc. Residual risk = *valid-but-wrong* (wrong customer/amount). Guard it with human approval:
1. Agent calls a **create-draft** tool → draft created (mutable, no number, no tax-point — reversible).
2. The tool **returns a deep-link URL** `${appBaseUrl}/<doctype>/{id}?action=approve` for the agent to show the user.
3. User clicks → **logs in with their own session** → draft page shows the **document preview** (existing) + an **"อนุมัติ & Post" CTA** → posts under the **user's** identity + `.post` permission.
4. **Security:** the URL is a plain deep-link, NOT a magic one-click-post token. The gate is the user's authenticated session + `.post` perm; the agent key has no `.post`. URL leak != post.
5. **Backstop (no forgotten drafts):** agent-created drafts are tagged (`api_key_name`); the dashboard "ต้องทำ/แจ้งเตือน" widget shows "X รออนุมัติ" with a link.

## Must-fix (before enabling MCP writes)
1. **Audit actor** — key-sourced writes currently log `UserId=null`; add `api_key_name` (+ kind) so §4.8 audit shows the agent. Doubles as the draft tag for the approval backstop.
2. **Per-key rate-limit** on `/api/v1` + the MCP endpoint (only `/auth/login` is throttled today).
3. Idempotency — already enforced (middleware).

## MVP build plan (subagent batches)
- **M1 (backend foundation):** `api_keys.kind` column + EF migration (backfill integration) - key-create accepts kind - guard (mcp key can't hold `.post`) - audit-actor (`api_key_name`) on key writes - per-key rate-limit. Tests.
- **M2 (backend MCP server):** add `ModelContextProtocol.AspNetCore` - `AddMcpServer`/`MapMcp` behind X-Api-Key - read tools (list/get tax-invoice, receipt, quotation, customer, product) + create-draft tools (return approval deep-link). Smoke test.
- **M3 (frontend):** `?action=approve` CTA on draft pages - tag/badge agent drafts - dashboard "รออนุมัติ" count - api-keys settings: create/show **mcp** kind - **when the user picks `mcp` kind, show a SETUP-INSTRUCTIONS panel** after creating the key: the MCP endpoint URL (`${appBaseUrl}/mcp` or whatever `MapMcp` mounts), the `X-Api-Key: <one-time key>` header, a copy-paste MCP client config snippet (e.g. Claude Desktop / Claude Code remote-HTTP MCP entry), and a short note (scope = read + create only; the agent drafts → you approve & post via the link). tsc gate.

## Effort
M1 = S+ (migration + guard + audit). M2 = M (package + tools; read-only S, then drafts). M3 = S–M. Read-only MCP alone (skip drafts/approval) = S.

## Risks / compliance
Multi-tenant isolation (key→company, RLS) already in place. Agentic post barred for mcp keys (kind+scope+guard). Immutability/numbering/§4 untouched (drafts only; humans post). Secret = the key (bcrypt-stored; rotation handled in the MCP host).

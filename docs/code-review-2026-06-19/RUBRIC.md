# TEAS Codebase Review — Shared Rubric (2026-06-19)

Both reviewers (Claude subagents + AGY/Gemini) score the **same 4 dimensions** so findings
cross-validate. Every finding MUST carry: **dimension · severity (critical/major/minor) ·
`file:line` · the problem · a concrete proposed fix**. No praise, no restating code.

Scope = **REVIEW + PROPOSE FIXES ONLY**. Do NOT edit source, do NOT build, do NOT commit.

---

## D1 — Compliance (§4 CLAUDE.md — the crown jewel). Cite the legal section per finding.
- Tax Invoice ม.86/4: all 8 mandatory fields present; VAT shown **separately**.
- Post-immutability (ม.86/4 #2): no code path edits/deletes a posted Tax Invoice; corrections = CN + reissue.
- Doc numbering (§4.3): `MM-YYYY-PREFIX-NNNN`, sequential, **no gaps**, monthly reset, number assigned **only on POST/Issue** (never Draft), voided numbers never reused.
- Multi-tenant (§4.7): every business query/join filters `company_id`; PostgreSQL RLS present.
- Audit trail (§4.8): every state change → `audit.activity_log`; critical post-fields immutable; no DELETE on any audit/log table.
- e-Tax boundary: Phase-1 scaffolding only — signer inert, no live RD submission claimed.
- **Look in `backend/src/Accounting.Infrastructure/.../Migrations/SqlScripts/`** — immutability + RLS are enforced in triggers/RLS, not just app code. A review that skips the SQL misses where the real guarantees live.

## D2 — Correctness
- Tenant leaks (missing `company_id` filter in any query/join).
- `.Result` / `.Wait()` / `Task.Run` in request/async paths.
- Money = `decimal` (4dp) everywhere, never `double`/`float`; rounding correctness.
- Dates: `DateTimeOffset` internally, `Asia/Bangkok` only at display, **CE calendar (never Buddhist) internally**; `doc_date` always = today in Asia/Bangkok (never trust user input).
- ProblemDetails out / domain exceptions in; FluentValidation before domain.

## D3 — Security — **center on the just-shipped MCP agentic surface** (newest, least-reviewed, highest-risk: commits dfe0636 / 03d54d6 / 06fc16f).
- MCP API-key scoping: scopes enforced server-side per tool; no scope bypass; keys hashed not plaintext.
- Agent doc-drafting + approval gates: agent can only DRAFT, human approval required to post; pending-approval state can't be bypassed.
- AuthZ on every endpoint (no missing `RequireAuthorization` / apiperm policy); JWT field `access_token`.
- No PII/secrets in logs; no plaintext PII/tokens at rest; no `localStorage` for sensitive data (FE).
- Input validation at trust boundaries; injection (SQL/`SET LOCAL app.company_id`) safety.

## D4 — Spec drift
- `docs/api/openapi.yaml` vs as-built endpoints (missing/extra/changed).
- `docs/accounting-system-plan.md` (as-built spec) vs reality.
- i18n parity: `messages/th.json` ↔ `messages/en.json` (TH primary).

---

## Output format (each reviewer → one markdown file)
```
## D1 Compliance
- [CRITICAL] file.cs:123 — <problem>. Fix: <concrete fix>. (ม.86/4 #6)
- [MAJOR] ...
## D2 Correctness
## D3 Security (MCP)
## D4 Spec drift
## Summary table: dimension × {critical, major, minor} counts
```

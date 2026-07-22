# Codebase Review — 2026-07-04

Full-codebase risk-directed review. All review dispatches are READ-ONLY:
no source edits, no `git commit`. Each lens writes its findings to
`_review/2026-07-04/<lens>.md` and nothing else.

Reference rulebook: `CLAUDE.md.bak` (§4 compliance, §5 conventions, §10 DO-NOT).
Layout: `backend/src/{Accounting.Api,.Application,.Domain,.Infrastructure,.Workers}`,
`backend/tests/`, `frontend/`, SQL in `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/`.

Finding format (every lens): severity (CRITICAL/HIGH/MEDIUM/LOW) · file:line ·
what · why it matters (cite rule, e.g. ม.86/4 #6) · suggested fix (1–2 lines).
Evidence required — quote the actual code. No speculative findings without a file:line.

## Checklist

- [x] P0 baseline gate → `_review/2026-07-04/baseline.md` (GREEN: build 0/0, 710 pass/8 skip/0 fail, tsc 0)
- [x] P1a compliance lens (Opus) → `compliance.md` (0 HIGH; 2 MED + 1 LOW)
- [x] P1b tenant-isolation lens (Opus) → `tenant-isolation.md` (2 HIGH + 1 MED + 1 LOW)
- [x] P1c auth/OAuth lens (security-auditor) → `auth-oauth.md` (0 HIGH; 3 MED + 1 LOW)
- [x] P2a backend quality lens (Sonnet) → `backend-quality.md` (1 new HIGH + dup HIGH + 1 MED)
- [x] P2b frontend quality lens (Sonnet) → `frontend-quality.md` (0 HIGH; 2 MED + LOWs)
- [x] P3 Codex cross-family pass → `codex-crossreview.md` (7 HIGH/3 MED/1 LOW; 2 conflicts w/ P1a/P1c)
- [x] P4 Fable consolidation → `_review/codebase-review-2026-07-04.md` (10 HIGH / 12 MED / 7 LOW)

## P0 — Baseline gate (Haiku, run-and-report only)

From `W:` (subst → backend): `dotnet build W:\Accounting.sln` expect 0 errors.
Tests: `cd W:\tests\Accounting.Api.Tests` with
`$env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'`
and `$env:TEAS_REPO_ROOT='Y:\ClaudePlayground\TEAS-Project'` → `dotnet test` (also Domain.Tests).
Env vars die per PowerShell call — set them in the SAME command as dotnet test.
Report pass/fail/SKIP counts verbatim (skips ≠ green — report skip count).
FE: from `frontend/`, `npx tsc --noEmit` expect 0.
Do NOT kill :5080, do NOT fix anything — report only.

## P1a — Compliance lens

Scope: Domain + Application + Api layers, SqlScripts (triggers), relevant tests.
1. Tax Invoice ม.86/4 — all 8 mandatory fields present in model/PDF/XML output.
2. Immutability after Post — no code path edits/deletes a posted doc; DB trigger
   AND app-layer enforcement both exist; corrections only via CN.
3. Doc numbering — `MM-YYYY-PREFIX-NNNN`, sequential no-gap, assigned only on
   POST/Issue (never Draft), voided numbers retained never reused, monthly reset.
   Look for race conditions in number allocation.
4. Audit trail — every state change writes `audit.activity_log`; no code deletes
   from audit/log tables; tax-config changes logged.
5. Money = decimal(4dp) everywhere (no double/float in money paths).
6. CE calendar internally; `Asia/Bangkok` only at display; `doc_date` never trusted
   from user input.

## P1b — Tenant-isolation lens

Scope: Infrastructure (DbContext, RLS SqlScripts, interceptors), Api middleware, all raw SQL.
1. Every business table: `company_id NOT NULL` + RLS policy present (diff table list vs policies).
2. `SET LOCAL app.company_id` per request — verify the path, incl. background Workers.
3. EF global query filter as backup — coverage vs entity list.
4. Raw SQL / Dapper / `FromSqlRaw` spots that bypass the filter.
5. Cross-tenant leak via joins, reports, aggregate endpoints, document-chain lookups.
6. ⚠️ Known trap: dev/test connect as SUPERUSER so RLS is bypassed in tests —
   review policies as if `SET ROLE teas` (NOBYPASSRLS). Prod-only failures happened before
   (empty-token login, seed 42501). Check what role prod connects as.

## P1c — Auth/OAuth lens

Scope: OAuth AS (v1.10.0, shipped Jul 3), JWT setup, login/returnTo redirect, MCP endpoint auth.
1. OAuth flows: PKCE, redirect_uri validation, token lifetime/rotation, client registration.
2. returnTo deep-link (v1.10.2): open-redirect — can returnTo point off-site?
3. JWT: signing key source/strength, alg confusion, expiry, claims validation.
4. Secrets in repo/config; password hashing; rate limiting on login.
5. RBAC enforcement gaps: endpoints missing permission checks (compare against docs/rbac map).
6. `localStorage`/cookie handling of tokens on FE.

## P2a — Backend quality lens

Scope: backend/src, spot-check tests.
1. `.Result`/`.Wait()`/`Task.Run` in request paths; missing `CancellationToken`.
2. PII in structured logs.
3. EF: N+1, missing `AsNoTracking` on reads, tracking leaks, transaction misuse.
4. FluentValidation coverage at trust boundaries; ProblemDetails consistency.
5. DbContext/DI lifetimes; Workers correctness (scoped services from singletons).
6. Dead code / duplicated service logic worth flagging (flag only, no refactors).

## P2b — Frontend quality lens

Scope: frontend/ (app, components, lib, messages, e2e helpers).
1. RSC boundaries — `'use client'` overuse/underuse; secrets/env leakage to client.
2. Sensitive data in `localStorage` (tokens?).
3. `messages/th.json` vs `en.json` key parity; hardcoded user-facing strings.
4. Zod validation gaps on forms; API error handling.
5. Leftover inert e-Tax UI beyond what was removed on Jul 3.
6. React Query patterns: cache invalidation after mutations, stale company-scoped data
   after company switch.

## P3 — Codex cross-family

Input: consolidated P1 findings + pointers to the hot files. Ask for blind spots,
disagreements, and anything Claude-family reviews systematically miss.

## P4 — Fable consolidation (never delegated)

Merge, dedupe, re-rank severity, verify each CRITICAL/HIGH personally against source,
triage lessons (troubles-wiki vs template), write final report
`_review/codebase-review-2026-07-04.md`. No fixes without Ham's go.

## Attempt log

- 2026-07-04: dispatched P0 + P1a/b/c + P2a/b in parallel (all read-only, disjoint output files).

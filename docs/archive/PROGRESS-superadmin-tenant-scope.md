# PROGRESS — superadmin-tenant-scope (2026-07-08)

## Done
- Investigation COMPLETE (prod DevTools + code). Root cause confirmed at both layers:
  - EF: AccountingDbContext.cs:152 `_tenant.IsSuperAdmin ||` in global query filter
  - RLS: `OR current_setting('app.is_super_admin')` arm in company_isolation policies
    (010_rls_policies.sql pattern, repeated in all later *_rls.sql scripts)
  - TenantMiddleware.cs pins app.is_super_admin per request
- User confirmed scope: fix it — super admin must be scoped to SELECTED company.
  Letterhead symptom (doc A rendered with company B header) expected to vanish with
  correct scoping; no separate fix.
- specs/superadmin-tenant-scope.md written (design deliverables D1–D7 + constraints)
- ROUTING-LOG.md entry added (Opus DESIGN justification)

## In-flight
- PR #58 open, CI watch running (background gh pr checks --watch)

## Done (pipeline)
1. [x] Opus design — APPROVED by Fable (app.bypass_rls LOCAL-only GUC; G1/G2/G3 policy shapes)
2. [x] Sonnet implement on feat/superadmin-tenant-scope (9 src + 8 test files, spec cap exact)
3. [x] Tier-2 Opus security review: APPROVE (F1 e-Tax pre-existing LOW; F2 RBAC whole-method
       bypass = accepted spec trade-off)
4. [x] Tier-3 gate: build 0 warn; 684/8/0 (== baseline); pg_policies legacy=0 bypass=6 total=37
5. [x] Fable full diff review — pass
6. [x] Commit b406528 + PR #58 (only feature files; CLAUDE.md/.gitignore/orchestration files
       deliberately NOT committed)

## Next (resume steps, in order)
1. CI green → merge PR #58 (squash per repo convention? previous PRs were merge commits — use
   default merge like #56/#57).
2. Release: release-please will open a release PR after merge (like #57) — merge it to tag.
3. Deploy: DB BACKUP FIRST (SqlScripts run at API startup — 600 recreates policies), then
   plink deploy per memory teas-prod-deploy-plink. SQL + code lockstep; rollback = full
   release revert + DB restore.
4. Prod smoke through PUBLIC domain: login ham_chatsang, switch Repttown(2)↔พงศ์สันต์(3) →
   quotations/customers/dashboard MUST differ; cross-company doc detail → 404; company-switch,
   OAuth MCP grant, API-key call, e-Tax retry tick still work (Tier-2 F1: exercise e-Tax).

## Pending gates
- CI on PR #58.

## Notes
- STATUS.md updated separately. Quota-guard warned 85% @ 17:32 (+7).
- Do NOT downgrade design to cheaper model; if Claude pool tight after reset, Codex
  is the arbitrage path for IMPLEMENTATION only, design stays Opus/Fable.

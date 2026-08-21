# PROGRESS — API docs sync (2026-08-21, insurance checkpoint at 92%)
In flight: Sonnet syncing docs/api/openapi.yaml + docs/manual/api/*.md to current routes
(spec: specs/fix-docs-api-sync.md). Known-missing list in the dispatch (employees/lookup, pp36
pdf, DELETE imports, expense-claims, fixed assets, activity routes, payroll artifacts, ReasonBody
validation notes). On resume: read worker report → Fable verify a sample of added routes vs
endpoints → commit "docs(api): ..." → push. Everything else today is DONE and pushed
(v2.3.1+v2.3.2, README, archive cleanup, publish/ removal, research note).

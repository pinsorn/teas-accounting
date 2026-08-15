# PROGRESS — MCP expansion v2 + Codex fix round (quota 95% wind-down, 2026-07-10 ~19:50)

## Done
- v1.17.0 (Cycles C+D) LIVE on prod.
- MCP expansion v2: commit 00f14df, Tier-2 APPROVE, Fable diff review PASS,
  **PR #69 CI green + MERGED to main**.
- Codex fix round IMPLEMENTED on branch fix/codex-review-2026-07-10 (stacked on
  feat/mcp-expansion-v2): all 10 items [x] in specs/fix-codex-review-2026-07-10.md,
  full suite 957 total / 0 fail / 8 skip (baseline 933+24 new). NOT committed,
  NOT yet Tier-2-reviewed.
- ROUTING-LOG has the Codex-Tier-2 routing entry (dispatch NOT yet sent — quota
  hit 95% before dispatch).

## Resume steps (in order)
1. Dispatch Codex (separate pool — allowed even before Claude reset) OR after
   reset Opus, for Tier-2 REVIEW of the uncommitted diff on
   fix/codex-review-2026-07-10. Lenses: §1 SET-narrowing vs pinned formula
   (T-scope-1..4 evidence in spec), migration = exactly 2 partial unique
   indexes on bank.statement_lines, account-validation fixes (#2/#5/#5b),
   concurrency fixes (#6 SaveGuarded), CSV injection helper (#7), parser
   strictness (#8 — KBiz fixtures still green).
2. Apply any review fixes (warm worker ad4b981a9183cccd5).
3. Fable diff review (money files: BankReconciliationReportService,
   FixedAssetService, ExpenseClaimService, migration).
4. Rebase fix/codex-review-2026-07-10 onto main (PR #69 merged — stash
   CLAUDE.md around rebase, Ham's WIP), commit (exclude CLAUDE.md), push,
   PR, CI, merge.
5. Release-please PR merge (--admin per troubles-wiki) → tag → build from
   OFFICIAL tag (worktree; verify MinVer stamp) → deploy BOTH tiers (MCP
   touched FE scope picker): pattern publish/v1.17.0/*, DB backup mandatory
   (EF migration MatchTargetUniqueness at startup), applied_sql_scripts
   baseline stays 68 (no new SqlScripts), md5 verify, re-scp same session,
   21-probe style + public E2E.
6. STATUS close-out + retro + finding triage (worker's EF ExecuteUpdateAsync
   exception-shape lesson → minions-assemble template, per its note).

## Ham pending decisions (also in STATUS.md)
- Codex #4 activation-after-run, #7 manual-confirm ±7d window,
  SelfWithholdMode PV amount question.

## Wakeup
ScheduleWakeup standing (fires ~18:42, chained prompt reads THIS file).
Quota resets ~20:45 per guard state at 86% reading.

## CLOSED 2026-07-10 ~20:20 — v1.18.0 LIVE
All resume steps completed: Opus Tier-2 (1 operational MAJOR → dedup gate, ran
clean), Fable diff review, rebase, PR #71 CI+merged, release #70 --admin,
build from official tag e81d95d, deploy API 21/21 + FE + public E2E green.

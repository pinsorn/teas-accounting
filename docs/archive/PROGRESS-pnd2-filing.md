# PROGRESS — ภ.ง.ด.2 filing (specs/pnd2-filing.md) — updated 2026-07-30 ~00:40

## ⭐ OVERNIGHT AUTHORIZATION (Ham, 2026-07-30 00:35, verbatim intent)
"ฝากจัดการทุกอย่างหน่อยนะ เช็ค เทส Deploy จะทำอะไรก็ทำได้เลย เดี๋ยวขอไปนอนก่อน" —
full autonomy: check/test/deploy as needed. Scope questions get NOTED for morning, not guessed.

## CURRENT STATE (v1.26.0 LIVE on prod)
- main = 2091dd7 (+release merge bf85772, tag v1.26.0); prod API+FE deployed, all probes green:
  int_ind=5/5, wrong_rate=0, efmig=1, script632=1, public /tax-filings/pnd2 = 401 (exists).
- IN FLIGHT: E2E leg on co7 via Chrome (resumed after quota cliff; had reached vendor-select in
  step 4, instructed to re-check state before writing — a posted PV must NOT be duplicated).
  Script: PV ดอกเบี้ย ฿1,000 → WHT 15% ฿150 → cert ภ.ง.ด.2 → preview/batch/tax-summary/Dr=Cr.
- STATUS.md updated (uncommitted) — commit after E2E result folded in.

## NEXT (overnight queue)
1. E2E returns → triage: findings fixed + hotfix-deployed if needed; commit STATUS/PROGRESS.
2. Fable personally reviews specs/doc-signature-and-foot-layout.md (1296 lines; §1.2 per-doctype
   audit + §security guard + pagination fallback ladder are the load-bearing bits; Ham approved
   the mockup + person-name-in-parens decision already folded in as §A4).
3. Dispatch signature/layout implementation per spec's WP split (NOT parallel with anything
   touching i18n/queries.ts). Engineering loop mandatory. Opus review high-risk WPs.
4. Full suite + tsc/next build gates → Fable reads diff → commit per verified unit.
5. Deploy ONLY when all gates green (Ham authorized); else leave committed + note for morning.
6. Morning report: single summary of everything.

---
# (original checkpoint below, 2026-07-29 ~18:50)

Checkpoint at 85% 5h-quota (resets ~19:35). main = 8c1b611, tree has UNCOMMITTED agent-file
edits (see below) + untracked config; prod = v1.25.0.

## Context
Live compliance defect confirmed: director interest via PV today → cert on ภ.ง.ด.3 @1%
instead of ภ.ง.ด.2 @15% (ม.50(2)). **No retro damage on prod**: only 5 certs exist, all
PayeeType/FormType-consistent, ZERO with income_type_code='4' (verified by direct psql
2026-07-29 — evidence doubles as the §A4/T10 pre-check: filter change regression-safe).

## Done
- Fable review of specs/pnd2-filing.md COMPLETE (all 703 lines incl. money/invariant
  sections I1–I7, §A4, §A5c). 4 spot-checks against code all confirmed:
  WhtFilingService.cs:41-49 PayeeType filters (double-count real), WhtBatchExportService.cs:31-44
  same hole, WhtTypeConfiguration.cs:27 unique (CompanyId,Code,EffectiveFrom) → ON CONFLICT valid,
  DbInitializer.cs:103-106 MigrateAsync BEFORE ApplyScriptsAsync → column exists before seed 632.
- **WP-A dispatched** to sonnet-implementer (background, in flight at checkpoint): A1 enum,
  A2 Pnd2IncomeCode columns + EF migration, A3 routing switch w/ Individual-only `when`,
  A4 positive FormType filters (Pnd2 generator itself deferred to WP-B), A5 seed 632 +
  DefaultWhtTypes 7-tuple. Cap 13 files. T4 test included. No commit, no full suite.
  Mid-flight directive sent: mandatory engineering loop + re-verify prior work.
- Ham feedback folded (per his explicit ask): **engineering loop** added to
  `.claude/agents/sonnet/implementer.md` (one item at a time, test-first RED→green,
  narrow test per iteration, 3-strikes→BLOCKED, evidence=output-not-exit-code) and
  **review loop** added to `.claude/agents/sonnet/reviewer.md` (spec-walk item-by-item,
  confirm-or-drop findings, verify implementer claims, review tests as hard as code,
  APPROVE requires per-lens evidence). BOTH FILES EDITED LOCALLY, not yet committed
  (they are untracked in this repo — Ham keeps .claude/ local).
- minions-assemble cloned to scratchpad
  (Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\6ade7177-9d1b-48a5-9bdf-17755afca153\scratchpad\minions-assemble)
  via `gh repo clone` (ssh remote fails; use gh/https). Target files exist at
  `.claude/agents/sonnet/{implementer,reviewer}.md`.

## In flight
- (none — WP-A returned and is Fable-reviewed; next dispatches deferred to quota reset)

## WP-A: DONE + Fable personal review PASS (2026-07-29 ~19:10, uncommitted)
14 files. Worker evidence: build clean; targeted 15/15 passed 0 skipped (real env);
RED→GREEN proof for T4 (reverted routing → test failed with Pnd3, restored → green);
teas_test T10 zero disagreeing rows; seed probe int_ind_rows==companies==45418, wrong_*=0;
glyph grep clean. Deviation (accepted): EFCore.NamingConventions default is
`pnd2income_code`, fixed with explicit `.HasColumnName("pnd2_income_code")` mirroring
Pnd30SubmissionMode precedent — migration regenerated before ever applied.
Fable read all money hunks personally: routing switch verbatim-spec, A4 filters positive,
seed 632 verbatim-spec, INT untouched, T4 exercises the real PV-post transition.
BLEMISH to fix in WP-B dispatch: garbage token "cont.85" in the INT-IND comment in
MasterDataServices.cs — have the warm worker clean it.

## Next steps on resume
1. ~~minions-assemble sync~~ DONE — pushed 5dd0f39 (both agent files, loop sections +
   July lesson tail that was missing from the template).
2. ~~WP-A~~ DONE (above).
3. Opus review of WP-A diff (lenses: RLS/seed correctness, double-count regression,
   enum-storage safety). Then:
4. WP-B (same worker, warm — SendMessage) + WP-C (fresh, parallel-safe: FE-only, no DB).
5. Tier-2 consolidated review → Tier-3 gate → Fable full suite (backgrounded, read log)
   → personal diff review → commit.
6. Deploy w/ MANDATORY DB backup (seed 632 + EF migration at startup) → §A5(d) row-count
   probe (`int_ind_rows == companies`, `wrong_rate == 0`, run as superuser) → public-domain
   probe `/tax-filings/pnd2`.
7. Open question for Ham: MCP write-side JV tools still awaiting his explicit approval.

## Gates pending
- WP-A Tier-1 evidence (build, targeted tests incl. new T4, T10 teas_test pre/post query).
- Full suite baseline compare (TaxFilings/Pnd50 flake pool is pre-existing).
- grep "ম" and "ד" over final diff.

## No checkpoint-commit made
Nothing verified-and-unclaimed to commit: WP-A still in flight; agent-file edits live in
untracked .claude/ (Ham's local config — do not commit without his say).

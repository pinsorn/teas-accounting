# PROGRESS — Cycle B — ✅ CLOSED: v1.16.0 DEPLOYED 2026-07-09 ~13:30

All B1-B5 shipped. PR #64 merged, v1.16.0 tagged+deployed, 13/13 API probes + FE +
public E2E green. Suite 882/0/8. Review chain: Opus B4 (2 fixed), Fable diff review
(tie-out sign flip — SPEC bug), sonnet cross-review (cumulative-window blocking +
CI storage-path test + hygiene). Prod seeds passed first try (RLS-safe patterns).

## Retro (Fable)
- SPEC REVIEW GAP: Fable skipped §report math during spec review (context economy) —
  the ONE skipped section held the sign-flip. Rule tightened in CLAUDE.md: money
  formulas/invariants are never skipped in Fable's spec review.
- Env-dependent test (real /var/teas on Linux CI) → kernel lesson folded to
  minions-assemble implementer template (storage-touching tests override the root).
- /tmp on prod gets reaped between sessions → re-scp deploy scripts every resume
  (folded into teas-prod-deploy memory).
- Worked well: staged B1-B5 single warm worker (context compounding paid off),
  two-stage migration protocol, probe-driven deploys, checkpoint/wakeup chains ×4.

# (was) CHECKPOINT @ quota 99% (2026-07-09 ~06:00)

Branch feat/bank-reconciliation. B1 ✅ committed. B2 ✅ committed. B3 ✅ DONE + Fable-reviewed
(password path verified: fresh hardcoded DomainException, no log/echo/inner-exception) —
**B3 NOT YET COMMITTED** (uncommitted on branch). Suite after B3: 862/0/8 (baseline 843
+ 9 B2 + 10 B3). Worker = warm sonnet, whole B1-B3 context.

## Resume steps (in order)
1. Quota check (~/.claude/quota-guard/state.json); not reset → chain ScheduleWakeup.
2. COMMIT B3 (Fable review already done): git add backend/src backend/tests
   backend/Directory.Packages.props (verify path: CPM file at backend/ root) →
   commit "feat(bank): bank reconciliation B3 - K-Plus PDF adapter (PdfPig,
   positional extraction, delta-derived direction, transient password)".
   Check git status for strays first (worker's PROGRESS-bank-reconciliation-b3.md
   at repo root = scratch, leave untracked).
3. Dispatch warm worker (SendMessage, same agent as B1-B3) → STAGE B4: matching
   engine + inline JE. MONEY PATH: touches GlPostingService (new PostManualEntryAsync
   per D7 — recon service calls EnsureOpenAsync BEFORE posting; IsClosingEntry=false).
   Spec sections D4 (exact match ±7d, one-to-one, POSTED only), D7, D8 (lifecycle:
   Posted lines can't unmatch), Tests for B4 (incl RLS under SET ROLE pg_database_owner,
   relative dates). Gates: build + new tests + full suite (baseline 862/0/8 + new).
4. B4 done → Opus Tier-2 REVIEW on B4 diff (money lenses: match candidates query
   correctness, EnsureOpenAsync ordering, PostManualEntryAsync vs closing poster,
   double-match race, tenant isolation) + ROUTING-LOG entry. Blocking findings → warm
   worker fixes → re-gate.
5. B4 committed → B5 (report + FE polish, same worker) → commit.
6. sonnet-reviewer cross-review B1-B3+B5 diff (lenses: spec compliance, parser
   regression, FE perm gates, i18n parity) — cheaper surfaces not yet cross-reviewed.
7. haiku-gate-runner consolidated gate (solo) → Fable reads remaining un-reviewed diff
   → push branch → PR (body per PR #60 style, deploy note: migration + scripts 614/615
   at startup, DB backup mandatory) → CI → merge --admin → release-please v1.16.0 →
   rebuild tarball (verify ProductVersion) → deploy per v1151 pattern (re-scp scripts —
   /tmp reaped; backup; probes incl bank.* perm fan-out count + public-domain 401s).
8. STATUS/PROGRESS close-out + retro. Then per Ham's standing orders: context heavy →
   STOP clean; else Cycle C (expense claims).

## Standing orders (Ham, asleep)
Autonomous through deploy; quota cliffs → checkpoint+wakeup chain; stop at phase
boundary if context degrades — new session resumes from this file + STATUS.md.

## Key facts for a cold resume
- Spec: specs/bank-reconciliation.md (living checklist, B1-B3 all [x] with evidence).
- Real bank samples STM_*.csv/pdf at repo root = Ham's data, gitignored, NEVER commit.
- PDF password for the K-Plus sample: Ham supplied in-session (06121996) — needed only
  if re-verifying the real PDF; never persist/log it.
- teas_test baseline skips = 8; PayrollRunServiceTests.Pnd1_* flaked once (unrelated).
- Deploy: ssh -i ~/.ssh/repttown_deploy ubuntu@158.69.197.154; scripts publish/deploy-*-.sh.

# <task title>

<!-- Copy to specs/<task>.md. Living document: the worker updates the
     checklist as it works; a retry uses the SAME file and grows the
     attempt log — never rewrite the spec for a retry.
     Skeleton evolved from the specs that survived multi-round Tier-2
     review (2026-07): the SECTIONS are the discipline — a section you
     skip is a class of bug you ship. Trivial 1-file zero-judgment fixes
     skip the spec entirely (routing ladder #1), so if you are here, the
     task earned the full skeleton. -->

## 0. Headline
<!-- One paragraph: what this ships and the single most important thing
     the designer discovered (e.g. "the upstream record is wrong — fix
     that first"). If investigation inverted the task's premise, say so
     HERE, not buried mid-document. -->

## 1. Facts established in code
<!-- file:line for EVERY claim — read, not inferred. Separate VERIFIED
     from ASSUMED and flag the assumptions. NEVER cite a gate/CI check as
     a safety net unless you verified it exists (file:line or command) —
     an imagined safety net licenses skipping the manual check.
     Fold in relevant troubles-wiki.md entries + env footguns: the
     implementer must not rediscover them. -->

## 2. Consumer sweep (mandatory when widening any seam)
<!-- Adding an enum member / discriminator value / form / status?
     Table EVERY consumer of that seam (switch/if/hand-enumerated list,
     backend + frontend + validators + reports + clone/copy-forward code)
     with an explicit disposition each: extend / deliberately skip
     (reasoning) / defer (troubles-wiki entry). Unswept consumers are
     where the bugs live — one unswept member cost two review REJECT
     rounds (2026-07-29). Delete this section only if no seam is widened. -->

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|

## 3. Design
<!-- Exact files, exact shapes, exact edge cases — precise enough that a
     mid-tier implementer needs zero judgment calls. Rejected alternatives
     get one line each with WHY (so they are not relitigated).
     Seeds/migrations: pin the runtime security context (role, RLS, GUCs)
     and how each read AND write satisfies policy; spec the deploy probe
     as ROW COUNTS, never exit codes.
     Money work: state the INVARIANT (what must not change: cash paid,
     Dr=Cr, AP clears), never just observable field values.
     Declared mirrors (FE copy of BE logic, screen==print): name BOTH
     sides and pin the shared semantics in one place both cite. -->

## 4. Invariants
<!-- Each invariant: one sentence + the named test that proves it (I1→T3).
     An invariant without a test is a wish. Include the "what is NOT
     changing" invariant for money-adjacent work. -->

## 5. Requirements checklist (per work package)
<!-- [ ] not started · [~] partial + note what remains · [x] done + evidence
     Each item: exact file(s), exact behavior, done-criterion.
     Group into WP-1/WP-2/... with explicit dependency + parallel-safety
     notes (shared files, shared test DB, same partial class). -->
- [ ]

## 6. Test list
<!-- T1..Tn mapped to invariants. Behavioural tests exercise the REAL
     transition (never seed the target state). Tests that cannot be
     automated are LISTED and reported honestly, never silently skipped. -->

## 7. Verification gates
<!-- command → expected output. Worker runs these before reporting done.
     Name which gate the ORCHESTRATOR runs (long full suite) vs the worker. -->

## 8. Out of scope
<!-- Explicit list, so scope-creep is a reviewable defect, not a judgment
     call. Known adjacent bugs → troubles-wiki entry, not a drive-by fix. -->

## 9. Blast-radius cap
<!-- "Max N files" — keep this NUMBER current: commissioning post-review
     remediation = update it in the same edit (a stale header survived two
     remediation rounds, 2026-07-29). Public-API changes allowed or not.
     Stop-and-re-spec triggers listed explicitly.
     Hitting the cap = stop-and-re-spec, never silent overrun. -->

## Attempt log
<!-- - <date> <worker>: <result / failure summary / evidence pasted> -->

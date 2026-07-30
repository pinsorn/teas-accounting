# PLAN — Test-system hardening (Fable, 2026-07-30)

Ham's question: "AI 100% ทั้งโปรเจกต์ แต่บั๊กนอนในระบบได้นาน = ระบบเทสห่วยใช่ไหม, implementer
เทสแบบ bias ไหม, ควรมี agent เทสแยกไหม" — answer: partly yes, and each weakness has a name.
This plan closes them class-by-class. Ordered by (observed escape risk × cost to fix).

## 0. Diagnosis — the six bug classes our 1,051-test suite structurally cannot see

| class | live example (all real, this week) | why the current suite is blind |
|---|---|---|
| A. Self-testing bias | FE/BE read `summary.total` with opposite meanings (700/850); each side's tests passed | the same brain writes code + its tests → tests encode the same misunderstanding |
| B. Oracle problem | director interest filed ภ.ง.ด.3 @1% for the system's whole life | the truth lives OUTSIDE the repo (RD documents); a test that asserts the wrong spec passes forever |
| C. Environment lies | 42501 RLS seed failures, 2 rolled-back releases | teas_test connects as superuser → RLS bypassed by construction |
| D. Data/seed bugs | INTR category → 5200; PND2 rows uneditable by validators | tests assert code paths, not cross-seed consistency invariants |
| E. Mirror drift | PaperFootPlan.cs vs PaperFoot.tsx | two implementations of one semantic, each self-consistent, no test compares them to EACH OTHER |
| F. Real-screen integration | PV detail page money display; SSO print preview | only visible on a rendered screen with real data; unit/API tests never look |

Note what is NOT broken: escapes-to-prod for the last two releases = 0, because review layers +
live E2E caught everything. The plan's goal is to catch these classes EARLIER and CHEAPER, and to
stop depending on heroics at the last gate.

## WS-1 — Independent acceptance tester (kills class A, helps E)

**New role: `.claude/agents/sonnet/acceptance-tester.md`** (sonnet; fresh spawn per feature).
Contract, the whole point in one rule:

> The tester reads the SPEC ONLY — requirements, invariants, external-truth citations. It is
> FORBIDDEN to read the implementer's diff, the implementer's tests, or the implementer's report
> before its own test list is written and committed to the spec's attempt log. Blindness is the
> mechanism that cuts the shared-brain bias; a tester that peeks inherits the same blind spots.

- Deliverable: acceptance tests asserting the spec's invariants (I-numbers) + a coverage-gap list
  ("invariant I4 has no automatable assertion — needs eyes").
- After writing its list, it runs them against the implemented tree and reports pass/fail.
- **Divergence protocol**: tester's expectation contradicts the implementer's passing test →
  automatic escalation to Fable. That contradiction IS the mirror-drift/bias signal (700/850 would
  have surfaced exactly here: spec says "Grand = pre-WHT total", FE test said otherwise).
- **Mutation-lite ("test the tests")**: for 2–3 load-bearing invariants per feature, temporarily
  negate the code path (the T9 HEAD-revert trick, now formalized) and confirm the suite goes RED.
  Capped at 2–3 — this is a spot-check, not a mutation-testing framework.
- Routing: MANDATORY for footgun-zone WPs (money/compliance/schema/security). SKIPPED for
  mechanical/1-file work — same anti-bloat logic as Tier-2 mode selection. Fable picks.
- Pipeline slot: parallel with Tier-2 review (both read-only vs the diff; test DB serialization
  still applies — tester runs its suite when the DB is free).

## WS-2 — Oracle discipline (kills class B)

The only class no tester can fix: if the spec misreads the world, writer and tester are wrong
together. Countermeasures are about anchoring to EXTERNAL truth:

1. **spec TEMPLATE gains an "External truth" subsection** (minions-assemble): any rule sourced
   from law/RD format/bank format MUST cite the primary document — file path under `docs/`,
   version, page — downloaded and read, never paraphrased from memory. (The pnd2 spec's
   FormatPND2V2_0.pdf download is the model; make it required, not heroic.)
2. **Golden corpus**: `docs/RD-Forms/` primary sources + golden-string tests (the existing
   WhtBatchFormatTests pattern) as the NORM for any external-format emitter, always carrying the
   honest T13-style caveat when never round-tripped against the real portal.
3. **Truth sweep (AGY, cheap pool)**: per compliance feature — and quarterly — AGY web-checks that
   every cited RD document is still the current version; report deltas only.

## WS-3 — Kill the environment lies (kills class C)

1. **Investigate the 9 standing skips first** (zero-cost lead): `PermissionLookupRlsTests.…NOBYPASSRLS…`
   is `[Skippable]` and currently SKIPPING — RLS-lane test infra partially exists and is likely
   just missing an env var/role. Wiring it may light up real RLS coverage for free.
2. **NOBYPASSRLS test lane**: second connection string (`TEAS_TEST_PG_RLS`, role `teas_rls`
   NOBYPASSRLS) + a small `[Collection]` that runs tenant-isolation and seed tests under it.
3. **Seed-runner gate**: a test that executes any NEW `SqlScripts/*.sql` against the RLS lane and
   asserts the deploy-probe row counts BEFORE deploy — turns the post-deploy probe into a
   pre-merge gate. This alone de-fangs the class that rolled back v1.22.0 and v1.24.0.
4. Deploy probes stay (defense in depth) — a green gate never replaces the prod row-count probe.

## WS-4 — Data-invariant + mirror-contract tests (kills classes D, E)

1. **Cross-seed consistency test class** (`SeedConsistencyTests`): pure assertions over seeded
   data — every expense category's default account exists and is the semantically right code
   family; every WHT type's form_type has a live routing arm AND passes the type validators;
   category WHT defaults resolve for both payee kinds. Would have caught INTR→5200 and the
   PND2-uneditable-row gap at write time, for pennies.
2. **Mirror-contract tests**: for each declared mirror pair, ONE shared fixture:
   - C# test emits `PaperFootPlan.Build(...)` results for N representative summaries to a JSON
     fixture file; a vitest (pure function — no jsdom needed) asserts the FE foot math produces
     identical rows from the same inputs. Both sides now test against a SHARED artifact instead of
     against themselves.
   - Start with the pair that just bit us (foot math); add the sign-box name-coalesce next.
3. **MIRRORS registry**: a short `docs/MIRRORS.md` listing every declared FE/BE mirror pair with
   its canonical side. Reviewer lens 5 and the acceptance tester both walk this list. New mirror
   without a registry entry = review finding.

## WS-5 — Standing E2E as a named tier (kills class F)

1. **Tier 4 — live acceptance (named, mandatory for money/compliance releases)**: scripted browser
   leg on prod (or staging) with REAL data; numbers verified on the actual screen; viewer-swap
   assertion for identity-bound features (doc approved by A shows A's signature whoever logs in);
   evidence = screenshots + numbers in PROGRESS. This tier caught 3 bugs this week that three
   review rounds + 1,051 tests missed — it has earned permanence.
2. **@critical Playwright subset in Tier 3**: repo already has e2e specs that never run in gates
   (stack started externally). Recipe for haiku-gate-runner: boot API + `next start` against
   teas_test, run a tagged @critical subset (PV-with-WHT numbers, paper render, ~5 min cap).
   Small, bounded, catches the render-integration class pre-deploy.
3. E2E legs keep the resume protocol (irreversible writes) already in CLAUDE.md.

## Governance / anti-bloat (the commander's constraints)

- Implementer KEEPS writing its own unit tests (RED-first) — fast feedback must stay with the
  writer. Independence is ADDED at the acceptance layer, never substituted for the inner loop.
- Tester tier is selective (footgun WPs), mutation-lite capped, @critical subset capped ~5 min,
  truth sweep on the AGY pool. Every new gate has a cost ceiling stated up front.
- **Earn-your-tax metric**: ROUTING-LOG retro tracks findings-caught-per-tier. Any tier that
  catches nothing across 3 consecutive footgun releases gets its mandate narrowed. Rules pay rent.
- NOT doing (decided): mutation-testing frameworks (heavy infra, marginal over targeted
  revert-checks); a prod-clone QA environment (cost); 100%-coverage crusades (vanity metric);
  moving all testing to a separate agent (kills the inner loop).
- Everything general goes back to minions-assemble: tester agent file, Tier-4 rule, External-truth
  template section, MIRRORS convention, RLS-lane kernel ("every test DB needs a non-privileged
  lane"). TEAS-specific details (role names, connection strings) stay in troubles-wiki.

## Sequencing

- **Phase 1 (next working session, ~1 dispatch each)**: acceptance-tester role file + routing rule
  (+ minions sync) · investigate the 9 PermissionLookupRlsTests skips · SeedConsistencyTests v1
  (categories + WHT types) · mirror fixture test for the foot math.
- **Phase 2**: NOBYPASSRLS lane + seed-runner gate · @critical e2e subset recipe for Tier 3.
- **Phase 3**: golden-corpus expansion + AGY truth-sweep cadence · MIRRORS.md complete · first
  earn-your-tax retro after 3 releases.
- Each item runs through the normal pipeline: spec → dispatch → review → gate. Phase 1 items are
  small enough to ride along after the current signature release ships.

# PROGRESS — WP-J document-level idempotency fence (2026-09-05)

Resume = read this + `specs/fix-idempotency-document-fence.md` checklist + attempt log. Never re-plan.
Ham's ruling 2026-09-05: option 1, full fence ("ทำให้เรียบร้อย"). Branch `gpt56-wpj-document-fence`
(off `19c24ed` = tip of PR #119's branch; rebase onto main after #119 merges). #119 itself: CI rerun
after `19c24ed` (docs-only) — check `gh pr checks 119`; Ham merges when ready (= prod deploy).

## Pipeline
1. [x] Fable spec draft (design: ambient key → service find-or-create under `pg_advisory_xact_lock` +
   partial UNIQUE, both saves in one tx; middleware sets the key).
2. [x] opus-designer hardening J1–J7 + J-extra RESOLVED (spec §3.9; 218k tokens, 22 min). Design deltas:
   third column `idempotency_request_hash`, named index `ux_<t>_idem`, factory-delegate DI, fence in the
   public wrappers, FNV-1a int4 lock pair, Complete-failure policy KEPT (middleware = 2 assignments).
3. [x] Fable full read + rulings R1 (TaxInvoice initializer stamp is the one permitted core edit),
   pipeline-order fact for T-J8, contract sharpening ACKNOWLEDGED — spec attempt log 2026-09-05.
4. [~] sonnet-implementer WP-1/WP-2 (Release build, isolated -o, teas_test slot, filtered tests; NO WP-3).
5. [ ] acceptance-tester BLIND WP-3 (T-F1, T-F2, T-J3..J7 per spec §4) — sole `dotnet test` runner.
6. [ ] opus-reviewer (lenses: lock/lookup ordering inside the tx, 23505 recovery + ChangeTracker state,
   RLS on the lookup, unkeyed-path regression, clone/convert sweep, migration).
7. [ ] Tier-3 full suite (Fable, detached + Monitor) + external-api e2e on a rebuilt local API.
8. [ ] Rebase onto main (after #119), PR, CI, report to Ham; Codex re-review welcome.

## Quota
5h 19% at 03:40 with one designer running; four Claude dispatches still to come — refresh this file
before the implementer dispatch and at every phase boundary; self-wake Monitor armed (b8rap0qlj).

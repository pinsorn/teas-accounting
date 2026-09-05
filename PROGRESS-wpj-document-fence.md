# PROGRESS — WP-J document-level idempotency fence (2026-09-05)

Resume = read this + `specs/fix-idempotency-document-fence.md` checklist + attempt log. Never re-plan.
Ham's ruling 2026-09-05: option 1, full fence ("ทำให้เรียบร้อย"). Branch `gpt56-wpj-document-fence`
(off `19c24ed` = tip of PR #119's branch; rebase onto main after #119 merges). #119: CI rerun GREEN
(backend + frontend) — Ham merges when ready (= prod deploy).

## Pipeline
1. [x] Fable spec draft.
2. [x] opus-designer hardening J1–J7 + J-extra RESOLVED (spec §3.9).
3. [x] Fable full read + rulings R1 / pipeline-order fact / contract sharpening ACKNOWLEDGED → `fed2bd2`.
4. [x] sonnet-implementer WP-1/WP-2 → `841eab9` (18 files; Release 0/0; filtered Api 162/0/0 + Domain
   188/0/0; migration DDL-only). Fable diff review PASS.
5. [x] acceptance-tester BLIND WP-3 DONE — `backend/tests/Accounting.Api.Tests/Hardening/IdempotencyDocumentFenceTests.cs`
   (1027 lines, UNTRACKED, not committed): 27 tests = **26 pass / 1 skip / 0 fail**; claim-first regression
   20/20. Passing: T-F1, T-F2×3 types, T-J3 (23505 names `ux_quotations_idem`), T-J4×3, T-J5a/b, T-J6, T-J7
   (RLS via pg_database_owner), T-J8×3, T-J9×3 (all three types stamp id/key/64-hex hash), T-J10 (6 cases),
   T-J11. SKIPPED: `F1b_Late_owner_after_full_takeover_converges_on_existing_document` — B gets
   **409 idempotency.in_progress after ~4.7 s** instead of taking over A's back-dated (10 min) claim.
   Tester parked it as "harness-only"; Fable has NOT accepted that — see ADJUDICATION below.
6. [x] opus-reviewer on `841eab9` = APPROVE-WITH-NITS (N1 `SalesOrderDeliveryServices.cs:470` internal
   caller → contract comments + spec §1/J8; N2 openapi reworded; N3 LockKey InvariantCulture; N4 lock-timeout
   raw 500 accepted).
   6b. [x] Fix batch N1/N2/N3 + blind suite COMMITTED `be87266`. Implementer verified: Release build 0/0;
   filtered Api run 2 = 188 pass / 0 fail / 1 skip (only F1b); Domain 188/0/0. Run 1 had the known
   `TenantIsolationTests.Customer_from_company_A_...` random-id flake (troubles-wiki), cleared on re-run — not
   a fence regression. Test file scanned (only F1b has Skip=; no prod leakage) before commit.
   6d. [~] F1b decisive diagnostic dispatched to warm tester (throwaway copy prints the claim-row state at
   B's 409: age/status/id → classifies harness vs product; reverts the copy after). Sole test runner; blocks Tier-3.
   6c. [ ] Spec Tier-2 records — run
   `python Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/485d6f4e-ebb5-4fb9-b1cd-3d278b885897/scratchpad/wpj-tier2.py`
   (tester finished writing the spec; safe now). Then commit the spec.

## ADJUDICATION — F1b 409 in_progress (RE-READ: takeover invariant IS proven; F1b is a harness edge)
CORRECTION to the earlier note: T-F1 (mandatory, lines 351-425) DOES prove takeover. It asserts B blocks on
the SAME advisory lock (line 391), and after release B MUST be 2xx (lines 419-421) with the SAME id as A
(line 424); A alone may be a tolerated 5xx. It PASSED. So two live owners inside the fence serialise onto one
document and the stale-takeover converges — the invariant holds, and the shipped claim-first store (T3/T11,
which also assert takeover with the same DB-`now()` back-date helper) is unaffected.
Store/middleware clock is consistent (verified): middleware passes `DateTimeOffset.UtcNow` (IdempotencyMiddleware.cs:91,141);
store predicate `created_at < now - staleAfter` (IdempotencyStore.cs:35,74,112); both test back-date helpers
use DB `now()`. No IClock override in either factory.
F1b (OPTIONAL per spec §4) uses an INTERFACE-level pause (`PausingQuotationDecorator` before the fence) that
§3.9-J4 explicitly says does NOT reproduce the real F1 window; there B got 409 in_progress instead of taking
over. The tester also found+fixed a real bug in F1b's OWN harness mid-run (per-scope latch → factory-level).
CHEAP DECISIVE DIAGNOSTIC (warm tester, AFTER the implementer frees the DB — never overlap test runs): un-skip
F1b; at the moment B gets 409, SELECT the `sys.idempotency_keys` row for that key and assert its state.
  - Row is B's FRESH claim (created_at recent, status NULL) yet B still 409 → real product gap → opus-debugger.
  - Row is still A's ORIGINAL (back-date didn't match, or not deleted) → harness bug → fix F1b, it passes.
Given T-F1 + T3/T11 all prove takeover, product gap is unlikely; this only closes the loop so an external
reviewer can't poke the skip. Committing the test file now with the honest `Skip` reason is fine as a checkpoint.

7. [ ] Tier-3 full suite (Fable, backgrounded `dotnet test` per project, TEAS_TEST_PG in the same call;
   baseline Domain 188/188, Api 1370 pass / 14 skips / 0 fail, + the new file 26/1) + rebuild local API and
   run `frontend/e2e/external-api-microservice.spec.ts` (API :5080 + next dev :3000).
8. [ ] Rebase onto main after #119 merges (or PR stacked on #119), `backend/VERSION` 2.3.3 → 2.4.0, PR body,
   CI, report to Ham; Codex re-review welcome. Then archive PROGRESS files to docs/archive/, STATUS.md, PLAN
   WP-J row → shipped, self-retro (lesson candidate: a blind test whose assertion tolerates the non-takeover
   outcome cannot prove serialisation — assert the contended request's SUCCESS, not just "no duplicate").

## Resume order after the quota cliff
6b build + filtered suites (implementer) → adjudicate F1b (H1 → H2) + strengthen T-F1 → commit code + test
+ run wpj-tier2.py + commit spec → Tier-3 → step 8.

## Quota
5h hit 97% at ~13:05 (reset ~14:05). Insurance paid at 85%: docs checkpoint `4159415`, ScheduleWakeup 13:39
(chain again if still >95%), self-wake Monitor bpgvbx6v0. 7-day 3%. Paused at wind-down; no work units in
flight (tester + reviewer done; implementer idle awaiting all-clear).

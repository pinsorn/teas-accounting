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
   6d. [x] F1b diagnostic DONE (tester): probe at B's 409 showed a FRESH claim row (id 1342, status NULL,
   age ~2s = a DELETE+re-INSERT takeover fired) yet B got 409 in_progress and the new claim went UNSERVICED.
   Bucket 2 = product-relevant, in the SHIPPED claim-first ClaimAsync/wait-loop (PR #119), NOT the fence.
   6e. [x] opus-debugger VERDICT: F1b was a TEST-HARNESS cold-start race, NOT a product defect. Old F1b gated on
   `Task.Delay(500)+IsCompleted==false` (satisfied by a slow cold WebApplicationFactory) → A hadn't claimed →
   back-date hit 0 rows → A/B raced (spurious 409 or deadlock). No takeover ever fired; the "fresh row" was a live
   claim. `IdempotencyStore.ClaimAsync`/`IdempotencyMiddleware` CORRECT + UNCHANGED (product diff empty). Fix
   test-only: `PauseReachedSignal` + assert back-date rowcount==1; un-skipped. Reachability argument CONFIRMED.
   Committed `c2afcfa` (F1b fix + wiki general-kernel + spec §4 T-F1b sync mandate). Gate: claim-first+fence 47/0/0.
7. [x] Tier-3 FULL suite GREEN: Domain **188/188**, Api **1397 passed / 0 failed / 14 skipped** (baseline
   1370+14; +27 = exactly the fence suite, skip count unchanged, F1b now runs). Release build 0/0.
   e2e `external-api-microservice.spec.ts` **1/1** against the Release API on :5080 (restarted; DbInitializer
   MigrateAsync applied the fence migration to accounting_dev — all 9 columns verified present).
8. [x] `backend/VERSION` 2.3.3 -> **2.4.0** (`2025b7b`); branch pushed; **PR #120** opened, base
   `gpt56-review-remediation` (STACKED on #119 so the diff is WP-J only). **CI GREEN on `719d4a0`: backend
   pass 24m38s (Linux + Postgres 16), frontend pass 1m46s.** Awaiting Ham: merge #119 then #120; GPT re-review welcome.
## Codex round 2 (`_review/Codex-WP-J-document-fence-review-2026-09-05.md`, HEAD 719d4a0) — 2×P2, both VERIFIED
- WPJ-F1 lifecycle → ruling **J9 RELEASE** (spec §3.6 J9, §3.3 Lifecycle bullet, §3.5 tombstone rejected):
  `DeleteDraftAsync` removes lines + document + the claim row in ONE tx; "permanent" narrowed to the document's
  lifetime in J2/J2b + openapi. Quotations only (TI/receipts have no delete path; v1 has no DELETE).
  CODE DONE + COMMITTED `237e59e` (implementer: Release 0/0; filtered Quotation|Fence|ClaimFirst|SalesChain
  106/0/0; sole caller = root route). Ham may still override to tombstone before merge.
- WPJ-F2 T-F1 sync → accepted; §4 now mandates pg_locks waiter counts + claim-id change + rowcounts +
  no-replay assertions. My earlier "T-F1 proves takeover" claim was too strong — corrected in the attempt log.
- [x] FRESH acceptance-tester DONE → `2218612`: T-J12 a/b/c + unfenced control; T-F1 rewritten with the 6
  bounded DB polls (proof sample: claimA 2002 → claimB 2004, waiters 1 → 2, both 201 non-replayed, final row
  = claimB/201); rowcount guards on F2/J4/J8 (J3 guarded by its 23505 throw — accepted). Fence suite
  **31/0/0**, claim-first **20/0/0**, T-F1 ×3 4–6 s. No divergences. Fable read the diff: no Skip=, no residue.
- [x] Dev API :5080 restarted (Release, no-build); external-api e2e **1/1** on the J9 build.
- [x] opus-reviewer on `08b24b6..2218612` = **APPROVE-WITH-NITS**, Codex WPJ-F1 + WPJ-F2 ruled CLOSED (7 lenses
  clean). Nits: N1 no RLS leg on the J9 DELETE → T-J12d; N2 T-F1 poll budget vs 30 s lock timeout → 5 s/step;
  N3 cross-type claim purge (benign) + N4 checklist → spec `1ef9b0e` (also pre-answers the round-3 pokes:
  cancel/reject wording, pre-existing read-before-tx guarded by the `Version` concurrency token).
- [x] Tier-3 round 2 GREEN: Domain 188/188, Api **1401 pass / 0 fail / 14 skip** (= +4 T-J12; baseline skips
  unchanged). e2e external-api 1/1 on the J9 build.
- [~] Tester (warm) released with ALL-CLEAR: T-J12d (RLS leg, raw-SQL variant under `pg_database_owner` +
  pinned `app.company_id`; own-company DELETE → 1, cross-company → 0) + T-F1 polls 5 s/step — edits reviewed
  by Fable; running fence (expect 32/0/0) + claim-first (20/0/0) filters now.
- [ ] Then commit the test file → push (one CI run) → CI → STATUS/PROGRESS final → Codex round 3 welcome.
9. [ ] After #119 merges: if the merge rewrites history (squash), rebase this branch onto `main` and retarget
   #120; else GitHub retargets it. Then archive PROGRESS-gpt56-review.md + this file to docs/archive/, update
   STATUS.md + PLAN WP-J row -> shipped, self-retro.
   Follow-ups: fold the acceptance-tester kernel (assert the rowcount of any DB-mutating test setup; never gate
   a WebApplicationFactory pause on Task.Delay+IsCompleted) into `.claude/agents/sonnet/acceptance-tester.md`
   AND commit back to minions-assemble. Plus the pre-existing WP-G (17 lint warnings, Playwright-in-CI,
   TenantIsolation random-id flake) and WP-I (purge worker blind under prod RLS).
## Resume order after the quota cliff
6b build + filtered suites (implementer) → adjudicate F1b (H1 → H2) + strengthen T-F1 → commit code + test
+ run wpj-tier2.py + commit spec → Tier-3 → step 8.

## Quota
5h hit 97% at ~13:05 (reset ~14:05). Insurance paid at 85%: docs checkpoint `4159415`, ScheduleWakeup 13:39
(chain again if still >95%), self-wake Monitor bpgvbx6v0. 7-day 3%. Paused at wind-down; no work units in
flight (tester + reviewer done; implementer idle awaiting all-clear).

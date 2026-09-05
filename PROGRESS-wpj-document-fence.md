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
   6b. [~] Fix batch N1-comments/N2/N3 ON DISK, UNCOMMITTED, UNBUILT (5 files: IdempotencyFenceLock.cs,
   QuotationChainServices.cs, ReceiptService.cs, TaxInvoiceService.cs, openapi.yaml). Fable read the diff = OK.
   Tester is DONE → obj/ + teas_test are FREE. Next: implementer (warm, id in ROUTING/transcript; or fresh
   Sonnet) runs `dotnet build backend -c Release` + filtered suites (incl. `~IdempotencyDocumentFence` and
   `~IdempotencyClaimFirst`), then Fable commits the 5 files + the new test file (explicit list).
   6c. [ ] Spec Tier-2 records — run
   `python Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/485d6f4e-ebb5-4fb9-b1cd-3d278b885897/scratchpad/wpj-tier2.py`
   (tester finished writing the spec; safe now). Then commit the spec.

## ADJUDICATION PENDING (Fable, first thing after reset) — F1b 409 in_progress
Read F1b at lines 442-480 of the test file: A fires with body marker → paused by `PausingQuotationDecorator`
(interface-level, before the fence) → after 500 ms `BackdateClaimAsync(1, apiKeyId, key, 10 min)` → B posts
same key/body → EXPECTED takeover + 201, OBSERVED 409 in_progress (= B's ClaimAsync saw A's claim as
NOT stale for the whole 2 s wait). Two hypotheses to test, in order:
  H1 (harness/product clock): the middleware's `now` for the stale check — if it comes from an `IClock` that
  `DocumentFenceApiFactory`/`IdempotencyApiFactory` overrides (fake/frozen clock) or is otherwise offset from
  DB `now()`, a row back-dated by DB time can look fresh. Check `IdempotencyMiddleware` (`now` source) and
  the factory's DI overrides; compare with `BackdateClaimAsync` (line 272) which uses DB time.
  H2 (product): stale-takeover path in `IdempotencyStore.ClaimAsync` fails for a claim whose owner is still
  connected (A holds a pinned connection + scope) — reproduce with F1b un-skipped + middleware logs.
  CRITICAL GAP FOUND: T-F1's assertion tolerates B being NON-2xx ("every 2xx carries the same id"), so T-F1
  PASSES EVEN IF THE TAKEOVER NEVER HAPPENS (B = 409, A = 201, one document). T-F1 therefore does NOT prove
  serialisation of two owners inside the fence. Fix: strengthen T-F1 (and F1b) to assert B's status is 2xx
  AND B's id == A's id (B MUST take over and converge). If strengthened T-F1 fails → the stale takeover is
  broken under these conditions → opus-debugger with the test as repro (this would also affect the shipped
  claim-first middleware in PR #119 — check T3/T11 of IdempotencyClaimFirstTests, which DO assert takeover,
  to see what differs: they back-date with the same helper?).
  Resolution owner: Fable adjudicates; the warm tester (or fresh Sonnet) edits the TEST; product fixes only
  via implementer after a confirmed root cause. Do not commit the test file with F1b skipped-as-harness
  until adjudicated; committing it with an honest `Skip` reason + this note is acceptable as a checkpoint.

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

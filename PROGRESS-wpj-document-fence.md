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
   188/0/0; migration DDL-only; DB probe proves the ambient channel is live). Fable diff review PASS.
5. [~] acceptance-tester BLIND WP-3 — file `backend/tests/Accounting.Api.Tests/Hardening/IdempotencyDocumentFenceTests.cs`
   written, test build 0/0; its filtered run was backgrounded (testhost PID 116032 since 12:25:45) and
   the agent stalled waiting for a notification → RESUMED (SendMessage) to poll in-turn and report
   T-F1/T-F1b/T-F2/T-J3..J11 (+ extension: T-F2/T-J8 × 3 types, T-J10 IsFenceCollision unit, T-J11
   LockKey culture). Then it runs the claim-first regression filter. Tester OWNS obj/ + teas_test until done.
6. [x] opus-reviewer on `841eab9` = APPROVE-WITH-NITS (N1 internal caller `SalesOrderDeliveryServices.cs:470`
   → contract comments + spec §1/J8; N2 openapi shared-param over-claim → reworded; N3 LockKey
   InvariantCulture; N4 lock-timeout raw 500 → accepted/recorded).
   6b. [~] Fix batch N1-comments/N2/N3 is ON DISK, UNCOMMITTED, UNBUILT (warm implementer edited only,
   Fable read the diff = OK). After the tester releases obj/ + DB: tell the implementer "all-clear:
   `dotnet build backend -c Release` + the same filtered suites", then Fable commits the 5 files.
   6c. [ ] Spec Tier-2 records (N1 §1 correction, N4 note, J8 invariant, attempt log) — script READY:
   `python Z:/temp/claude/Y--ClaudePlayground-TEAS-Project/485d6f4e-ebb5-4fb9-b1cd-3d278b885897/scratchpad/wpj-tier2.py`
   — run ONLY after the tester has finished writing the spec (it ticks WP-3 + appends its log).
7. [ ] Tier-3 full suite (Fable, one backgrounded `dotnet test` per project, TEAS_TEST_PG in the same call;
   baseline Domain 188/188, Api 1370 pass / 14 skips / 0 fail — the new file adds ~20) + rebuild local API
   and run `frontend/e2e/external-api-microservice.spec.ts` (needs API :5080 + next dev :3000 up).
8. [ ] Adjudicate any acceptance divergence (Fable, never silently reconciled) → fix via warm implementer.
9. [ ] Rebase onto main after #119 merges (or PR against main from this branch stacked on #119),
   `backend/VERSION` bump (2.3.3 → 2.4.0: new fence + migration), PR body, CI, report to Ham;
   Codex re-review welcome. Then archive PROGRESS-gpt56-review.md + this file to docs/archive/, STATUS.md,
   PLAN WP-J row → shipped, self-retro.

## Resume order after a quota cliff
tester result? (`git status` shows the test file; re-run its filter yourself if the agent is dead) →
implementer all-clear build → commit fixes → run wpj-tier2.py → Tier-3 → step 9.

## Quota
5h crossed 85% at ~12:40 (reset ~14:05). Insurance paid: this file + docs checkpoint commit + ScheduleWakeup
(13:39, chain again if still >95%) + self-wake Monitor bpgvbx6v0. 7-day 3%. Two warm workers in flight
(tester, implementer); no new dispatches planned — Fable runs Tier-3 itself.

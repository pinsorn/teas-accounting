# PROGRESS — Cleanup batch (Ham GO 2026-08-19 ~17:xx: "นอกจากข้อ 1 ทำทั้งหมดเลย")

Scope = everything parked EXCEPT server-migration-blocked items (co2 real-volume leg deferred —
needs prod-shaped data).

## Board
| Unit | What | Worker | State |
|---|---|---|---|
| C1 | U9 + McpScopes + 640-arm | sonnet | ✅ 1f9a2ab (263/263) |
| C2 | FE trio | sonnet | ✅ a1e9ff3 |
| C3 | e2e debt + Thai toast live check | sonnet | ✅ c288712 (+ found seed-181 bug → C11) |
| C4 | Proration DESIGN | opus | ✅ ratified a9353b6 |
| C5 | minions-assemble sync: fold general lessons into templates (read-only sub-dispatch rule; blocked-file-write fallback; dir-add sweeps untracked) | sonnet | ✅ pushed `9b3f940` |
| C6 | Spec backlog triage sweep | Explore | ✅ 48 DEAD·8 OBS·9 ALIVE → specs/TRIAGE-backlog-2026-08-19.md; spot-check 4/4 |
| C9 | Backlog stamping ×2 passes | haiku+sonnet | ✅ 74998e9 + 05fe73f |
| C10 | MIGRATION-CUTOVER-CHECKLIST.md consolidation | Fable | ✅ |
| C7 | Proration implement | sonnet | ✅ 528cf72 (Tier-2 APPROVE-WITH-NITS) |
| C8 | Final: re-wipe accounting_dev (C3's e2e runs repollute it), fresh boot probe, STATUS/PROGRESS close | Fable | pending |

## Parked for Ham
- CLAUDE.md drift: upstream minions-assemble ↔ TEAS local have diverged both directions (upstream
  has a self-wake Monitor block TEAS lacks; TEAS has weekly-85%-stop + newer wiki entries upstream
  lacks). Full reconciliation pass = Ham's call (changes the orchestrator's own contract).

## Rules in force
- ONE dotnet-test runner at a time (C1, then C7). C3's Playwright hits accounting_dev (disjoint DB) — parallel-safe.
- C3 repollutes the freshly wiped DB — EXPECTED; C8 re-wipes at the end (scripted, cheap).
- troubles-wiki entry to add at close (Fable): backend route changes must re-run RbacAuthMapTests pre-commit (a28718e shipped stale generated RBAC doc).

## Resume
Read this board; continue from first non-✅. Boot cmd + creds in PROGRESS-hard-test-r2.md.

## Final gate (2026-08-19 ~20:0x): FULL SUITE GREEN
Domain 188/188 · Api 1318/1332 (0 failed, 14 diag-gated skips = baseline) — includes C1/C3/C7/C11.
| C11 | seed-181 FORCE-RLS no-op → 181 patched + 641 reconcile | sonnet | ✅ f53ed0e (RED→GREEN, replay-idempotent) |
Remaining: C8 only — wipe accounting_dev (backup exists), ONE boot on rebuilt binaries (seeds
638–641), probes MUST include ap_clerk + sales_staff logins (the C11 regression) and user_roles
count, then STATUS/PROGRESS close. If session dies: run C8 per this note.

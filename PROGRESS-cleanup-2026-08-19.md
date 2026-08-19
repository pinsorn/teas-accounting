# PROGRESS — Cleanup batch (Ham GO 2026-08-19 ~17:xx: "นอกจากข้อ 1 ทำทั้งหมดเลย")

Scope = everything parked EXCEPT server-migration-blocked items (co2 real-volume leg deferred —
needs prod-shaped data).

## Board
| Unit | What | Worker | State |
|---|---|---|---|
| C1 | U9: PO TaxCodeId laundering (backend, U2 pattern) + McpScopes narrow to employee.lookup + seed-640 direct-grant-arm test | sonnet (test-runner slot) | 🔄 |
| C2 | FA Dispose/WriteOff modals role=dialog + PO form drop hardcoded taxCodeId:1 + back-dated-claim info note at pay (FE only) | sonnet | 🔄 |
| C3 | e2e suite debt: pickCustomer ambiguity, PV confirm-dialog specs, TenantIsolation fixture hygiene; + verify one Thai toast renders live (e14468f) | sonnet | 🔄 |
| C4 | Depreciation first-month proration + final-month plug — DESIGN (money formula, ม.65 ทวิ analysis; Ham greenlit the change) | opus-designer | 🔄 |
| C5 | minions-assemble sync: fold general lessons into templates (read-only sub-dispatch rule; blocked-file-write fallback; dir-add sweeps untracked) | sonnet | ✅ pushed `9b3f940` |
| C6 | Spec backlog triage sweep | Explore | ✅ 48 DEAD·8 OBS·9 ALIVE → specs/TRIAGE-backlog-2026-08-19.md; spot-check 4/4 |
| C9 | Mark 56 DEAD/OBSOLETE items in spec files + 2 troubles-wiki entries (from triage evidence) | haiku | pending (after C1 frees attention) |
| C10 | MIGRATION-CUTOVER-CHECKLIST.md consolidation | Fable | ✅ |
| C7 | C4's design → implement + tests | sonnet after C1 frees slot | pending |
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

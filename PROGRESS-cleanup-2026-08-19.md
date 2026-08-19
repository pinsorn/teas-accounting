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
| C5 | minions-assemble sync: fold general lessons into templates (read-only sub-dispatch rule; blocked-file-write fallback; dir-add sweeps untracked) | sonnet | 🔄 |
| C6 | Spec backlog triage sweep (pre-r2 open specs → dead/alive/absorbed classification) | Explore (read-only) | 🔄 |
| C7 | C4's design → implement + tests | sonnet after C1 frees slot | pending |
| C8 | Final: re-wipe accounting_dev (C3's e2e runs repollute it), fresh boot probe, STATUS/PROGRESS close | Fable | pending |

## Rules in force
- ONE dotnet-test runner at a time (C1, then C7). C3's Playwright hits accounting_dev (disjoint DB) — parallel-safe.
- C3 repollutes the freshly wiped DB — EXPECTED; C8 re-wipes at the end (scripted, cheap).
- troubles-wiki entry to add at close (Fable): backend route changes must re-run RbacAuthMapTests pre-commit (a28718e shipped stale generated RBAC doc).

## Resume
Read this board; continue from first non-✅. Boot cmd + creds in PROGRESS-hard-test-r2.md.

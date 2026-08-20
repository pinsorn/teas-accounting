# PROGRESS — Codex review fixes (paused at quota 98%, 2026-08-20)

## State
- Codex review (_review/code-review-2026-08-20.md): 4 findings, ALL Fable-verified REAL.
  F1 [P1] seeds 637/638 launder placeholder tax ID for ANY company → scope to demo identity.
  F2 [P1] seed 641 grants roles by bare user_id 3/4 → match by username, derive id by name.
  F3 [P2] delete import orphans the BANK_STATEMENT attachment (still downloadable).
  F4 [P1] delete-vs-match race: needs CONDITIONAL delete + count check, not check-then-delete.
- Fix worker (sonnet) was dispatched with full spec-in-prompt, then STOPPED on Ham's order at
  quota 98% — told to save partial edits + append state to specs/fix-codex-review-2026-08-20.md.
  Its state report may arrive as a notification; trust the SPEC FILE on disk over memory.

## Resume order (fresh window)
1. Read specs/fix-codex-review-2026-08-20.md attempt log (worker's checkpoint) + its state report.
2. Resume the worker warm (SendMessage) to finish per original dispatch (blast cap 10, targeted
   tests, no commits).
3. Fable verify + Opus Tier-2 (security lens: F1/F2 seed scoping, F4 conditional delete) → commit.
4. Full suite → push → release-please v2.3.1 patch → admin-merge after CI green.
5. Meanwhile Ham + Codex review UI on the local stack (accounts listed in the reply / boot recipe).

## Standing context
- v2.3.0 released (tag + GitHub). Prod NOT deployed (server migration; Coolify artifacts landed #113).
- Stack UP: API :5080 (HEAD build), FE :3000. accounting_dev clean except any UI-review activity.
- MIGRATION-CUTOVER-CHECKLIST.md = deploy-day source of truth (now also add: verify hardened
  637/638/641 behavior on prod's first boot — covered once this fix batch lands).

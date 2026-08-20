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

## Wave 2 — Codex UI review batch (2026-08-20 afternoon, all Fable-verified)
From _review/ui-edit-cancel-vat-nonvat-test + ui-document-creation-test + ui-codebase-review:
- IN FLIGHT: UI-1 stale paper preview (invalidate sweep) · UI-2 VatRate 0→7% (EF HasDefaultValue) ·
  UI-3 canned cancel reason — worker a734 (FE+company), test-slot coordinated.
- IN FLIGHT: F1–F4 (seeds 637/638/641 scoping + bank delete attachment/race) — warm worker a82f.
- QUEUED (wave 3, after current workers report):
  R1 [P1] seed 160 gives approver SUPER_ADMIN — demo SoD broken (verified: role + flags in SQL).
    Fix: non-super approver role w/ approval perms only; ruling by Fable: follow the rbac_approver
    template role's grant set. → warm backend worker.
  R3 [P2] activity routes all gated on Report.AuditRead; FE maps 403→"no history" (verified :43).
    FABLE RULING: a user who can READ the document sees its activity (per-doc-type read permission
    on each activity route); the global audit page keeps AuditRead. FE ActivityLog must render a
    distinct no-permission state, never empty-history, on query error. → backend half to warm
    worker, FE half to FE worker.
  R2 [P2] mobile overflow-hidden clips topbar/CompanySwitcher/date filters/P&L buttons at 390px
    (min-w-0/truncate/wrap per Codex suggestions) · R4 [P2] company selector a11y name ·
    R5 [P3] empty "ทางลัด" dashboard section → FE worker.
- Companies 4–5 + UIV-/UIN- data in accounting_dev = Codex UI-review evidence; keep until Ham
  says wipe (screenshots reference them).
- After all: full suite → Opus review (security lens: F1/F2/R1/R3 permission changes) → commits →
  push → v2.3.1.

## Checkpoint 2026-08-20 ~21:xx (quota 98%) — ALL CODE LANDED, release pending
Commits: 72b25ad F1-F4 · 3424fb0 UI-1/2/3 · 3054c89 R1+R3-be · 1b6d992 R2-R5+R3-fe ·
3aaafb0 F-3/N2 follow-up · <latest> F-1/F-2 REJECT remediation. Tier-2: REJECT → delta re-review
**APPROVE** (double-grant proven structurally impossible; catch narrow; FE list exact).
Final suite: Domain 188/188 · Api 1337/1352, 1 fail = TenantIsolationTests.Customer_from_company_A
— the KNOWN pre-existing self-collision flake (legacy 8.8k rows still in teas_test's id range;
C3's cleanup only prevents NEW leaks). Passed alone earlier today.

## Resume order
1. Confirm flake: run TenantIsolationTests alone → expect green. Optionally purge the legacy
   500000-699999 test companies from teas_test (read-only rule applies to accounting_dev, NOT
   teas_test — but purge via a test-context script, carefully).
2. Boot API (:5080) fresh → 642 + hardened 160/637/638/641 apply to accounting_dev → verify:
   approver is_super_admin=f + single APPROVER role; sales_staff activity 200 on own quotation.
   Restart FE :3000 (stale-chunk).
3. git push origin main → CI green → release-please PR (v2.3.1) → admin-merge → verify tag.
4. STATUS.md update: Codex review round closed (12 findings: 4 code + 3 UI-test + 5 UI-codebase,
   all fixed+reviewed). Note reviewer non-blocking leftovers: N1 auditor/tax-officer payroll
   activity (deliberate), N3 error-code nuance, F-3 residual (none — done).

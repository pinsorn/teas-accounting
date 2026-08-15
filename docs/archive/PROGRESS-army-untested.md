# PROGRESS — army-untested (2026-07-22)

Spec: specs/army-untested-2026-07-22.md (authoritative — waves, missions, hard rules).
Prod v1.22.10. Quota at plan time: 81%, 5h reset ≈ 14:30 (1784705400).

## Done
- [x] Spec written (waves A/B/B2/C, 11 untested areas mapped to legs)
- [x] Wave A1 dispatched (sonnet, background): co5 foreign vendor + read-only recon
      → swarm-findings/army/A1-prep.md
- [x] Ham asked (in-chat): B-1 non-VAT co create + grants, B-2 co5 API key (both super-admin-only)

## In-flight
- [~] A1 running. On completion: Fable reads A1-prep.md, verifies gates (1 mutation only,
      recon table complete), folds recon into Wave B dispatch prompts.

## Next (resume order)
1. [x] A1 verified + committed 87eeb73 (see spec checklist for recon results; e-Tax = NO UI,
       B-et must drive via TI post + artifact check).
2. Wait/confirm quota reset (cat ~/.claude/quota-guard/state.json — five_hour.pct low again).
3. **B-1/B-2 via Claude in Chrome — Ham confirmed (2026-07-22 12:2x) Chrome is LOGGED IN as
   super-admin and left it for us.** At wakeup: ToolSearch for browser/chrome tools (they were
   absent earlier — extension may connect later; re-check each wakeup). If present, Fable drives
   PERSONALLY (super-admin session + API key = secret — never a worker, never AGY):
   (a) B-1: /settings/companies → create "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด", vatRegistered=false,
       fill address; then grant the 10 UxSwarm users their same roles on it.
   (b) B-2: /settings/api-keys on co5 → create key scoped to agent/create-draft; save to
       Z:\temp\claude\...\scratchpad\co5-mcp-key.txt (NEVER in repo/git).
   If tools still absent at wakeup: proceed with co5 legs, re-check next wakeup, note for Ham.
4. [x] 2026-07-22 ~14:1x — Wave B co5 legs DISPATCHED parallel (6 sonnet, background):
   B-rc, B-ec, B-fa, B-br, B-et, B-bn. B-mcp still waiting on B-2 key.
   Chrome tools STILL not bridged at 14:05 wakeup (login alone insufficient — extension must
   connect to this CLI session) → B-1/B-2 remain Ham-blocked; told Ham.
   On each leg completion: Fable reads swarm-findings/army/<leg>.md, verifies gates, then commit
   per verified unit. All 6 done → consolidate (step 7).
5. If co6 exists: B2-nv → B2-pr → B2-ye STRICTLY SEQUENTIAL (B2-ye locks periods, runs LAST).
   STILL BLOCKED on Ham (B-1); B-mcp blocked on B-2 key. Chrome tools never appeared.
6. [x] ~16:2x Wave C1 dispatched (agy-runner): 50ทวิ + pnd54 PDFs vs official RD layouts →
   swarm-findings/army/C1-vision-forms.md. More C legs when B2 produces artifacts.
7. [~] CONSOLIDATED → specs/fix-army-findings-2026-07-22.md (WP-A CRITICAL money / WP-B WHT-type
   +stuck-PV / WP-C K-Plus 500 / WP-D FE nits / O1-O5 open Ham decisions).
   WP-A DONE + committed e17d232 (Fable diff review ✓, Opus APPROVE ✓, suite 921/8 ×2).
   Opus nit N1 (stale comment PaymentVoucherService.cs:291-293 says VI-linked can't
   self-withhold — now wrong) → fold into WP-B dispatch.
   ~17:0x WP-D dispatched (FE nits, background). Next: WP-D done → review → commit →
   WP-B (incl. N1 comment fix) → WP-C → deploy → live re-verify per spec plan.
   C1 vision done+committed 36fe28c (1 AGY false positive killed, O6 added).

## Session 2026-07-25 (goal: ลุยเต็ม, Ham away, Chrome connected w/ super-admin login)
- [x] B-1 partial: co6 created id=6 via Chrome (TIN 0105569000011) — 2 NEW BUGS → WP-E in fix spec
      (create ignores จด VAT=off; PUT /companies/6 → 500). co6 stuck VAT-flagged until WP-E deploys.
- [x] B-2 DONE: MCP key army-mcp-co5 on co5 → ~/.claude/teas-secrets/co5-mcp-key.txt (never commit).
- [~] WP-B RESUME dispatched (prev worker died at session limit mid-en.json; tree has partial impl).
- [~] B-mcp leg dispatched (uses the new key; prod-only, safe parallel with WP-B).
- NEXT (order): WP-B done → Fable diff review → Opus Tier-2 → commit → WP-C dispatch → WP-E
  dispatch → release v1.22.11 + deploy (plink per memory; DB backup first — new SqlScripts rule) →
  Chrome: flip co6 ไม่จด VAT + grant 10 UxSwarm users on co6 (Chrome-driving worker OK, no secrets)
  → B2-nv → B2-pr → B2-ye SEQUENTIAL on co6 → live re-verify probes per fix-spec Verification plan
  (B-rc chain ฿3,529.41, PV #19 unstick, K-Plus import, dep toast, EC badges) → Wave C2 vision on
  B2 artifacts → consolidate + STATUS + final tidy (stray B-*.pdf at repo root → move/delete).

## Pending gates
- A1 Tier-1 evidence review (Fable)
- Wave B leg reports ×7-9, Wave C report
- Post-army sanity: TB tie both cos, no cross-tenant, pm2 zero-500 window

## v1.22.11 DEPLOYED 2026-07-25 ~17:3x (Fable, SSH key)
- Fix arc COMPLETE + committed: WP-A e17d232, WP-D aaf62c5, WP-B 3835e96 (Opus REJECT->fix->APPROVE),
  WP-C b71e5cd, WP-E a8d54b4. Release PR #97 merged -> tag v1.22.11 -> published self-contained
  linux-x64 from the REAL path (MinVer 1.22.11+24ad992, 446 files/15 native libs), tar+scp md5-verified,
  api/unpacked swapped (unpacked.old kept), FE overlay + pnpm install + next build, pm2 both online.
- Pre-deploy DB backup /tmp/teas-pre-v12211-20260725-1730.dump. applied_sql_scripts 75 -> 75 (no new).
- Probes: localhost api /health 200, web 307; PUBLIC https / 307, /login 200, /mcp 401 (auth gate);
  footer shows v1.22.11. E3 verified LIVE: malformed MCP tools/call now returns "[mcp.arguments] ..."
  instead of the generic swallow.
- WP-E2 acceptance PASSED LIVE: co6 flipped to ไม่จด VAT via UI, toast บันทึกแล้ว, no 500.
- co6 users created (prod, Chrome/super-admin): nvadmin01/nvchief01/nvtax01 (COMPANY_ADMIN/
  CHIEF_ACCOUNTANT/TAX_OFFICER), pw UxSwarm-2026-NV1/NV2/NV3. 3 accounts = full B2 scope w/ SoD.
- IN FLIGHT: B2-nv (non-VAT full drive on co6) + V1 (post-deploy re-verify of every shipped fix on co5).
- NEXT: B2-pr (ภ.ง.ด.1/1ก edge cases on co6) -> B2-ye (year-end, co6 ONLY, LAST) -> Wave C2 vision on
  B2 PDFs -> final consolidation + STATUS.md + repo tidy (stray B-*.pdf at root, CLAUDE.md.bak).

## v1.22.12 DEPLOYED 2026-07-25 ~20:0x (Fable)
- Shipped: WP-F 479baae (PV prefill dual-flag), WP-G 2b6fc28 (non-VAT company gate on the PV path —
  Tier-2 rejected round 1 for under-paying/AP-stranding, round 2 APPROVE), WP-H 6b689be (RD/SSO
  filing PDFs on Payroll.RunManage OR tax.filing.preview — TAX_OFFICER was 403'd off ภ.ง.ด.1/1ก).
- CI green on 6b689be (backend job ~20min); release PR #98 merged --admin -> tag v1.22.12;
  published from the REAL path, MinVer 1.22.12+32fcb37; md5 verified server-side; api/unpacked
  swapped (unpacked.old kept); FE overlay + pnpm install + next build; pm2 both online.
- Pre-deploy DB backup /tmp/teas-pre-v12212-20260725-2002.dump. applied_sql_scripts 75 -> 75
  (WP-H needed no new SQL — the OR-set gate reuses an existing granted permission).
- Public probes: / 307, /login 200, /mcp 401; MCP get_company_info returns co5 data (auth+tools OK).
- IN FLIGHT: leg V2 = live verify of WP-F/WP-G/WP-H on prod (co5 + co6).
- REMAINING after V2: G4 nit (assert the IsRecoverableVat flag directly in the PV non-VAT test —
  filtered run, no full suite needed), then the army is closed. Everything else is O1-O12 = Ham's
  scope calls, listed in specs/fix-army-findings-2026-07-22.md and summarised in
  swarm-findings/army/VERDICT-army-2026-07-25.md.

## Checkpoint 2026-07-25 ~21:0x (quota 85%)
- ARMY CLOSED except one live proof. v1.22.11 + v1.22.12 both LIVE; V1 verified 10/11, V2 3/4.
- Ham-facing decision doc written: **DECISIONS-army-2026-07-25.md** (14 items grouped: 5 unbuilt /
  7 "do we want it" / 2 go-look-yourself, plus the shipped-bug ledger). Ham asked for exactly this.
- V3 (live 1170 proof on co6) BLOCKED — no monthly period-reopen exists anywhere (only reopen-year,
  which leaves monthly locks). My dispatch premise was wrong; B2-ye's report had said so. Filed O14.
- Created **co7** "บริษัท ทดสอบ NON-VAT 2 (DUMMY) จำกัด" (id=7, non-VAT, no closed periods) +
  nvadmin02/nvchief02 (UxSwarm-2026-NV4/NV5) as the replacement non-VAT playground. TaxId needed a
  valid Thai checksum — 0105569000029 (weights 13..2, check = (11 - sum%11) % 10).
- IN FLIGHT: V3b = the standalone-PV posted-JE proof on co7 (expense debit 1,070 / no input-VAT line
  / TotalPaid 1,070 / Dr=Cr).
- ALSO OPEN: G4 — the assertion IS added to PaymentVoucherNonVatCompanyTests.cs but UNVERIFIED (the
  haiku worker's TEAS_TEST_PG auth failed). Do NOT commit that file until a filtered run passes:
  `dotnet test backend/tests/Accounting.Api.Tests --filter "FullyQualifiedName~PaymentVoucherNonVatCompany"`
  with the correct TEAS_TEST_PG in the SAME command. If it fails, that means the prod gate does not
  set the flag — escalate, do not weaken the assertion.
- RESUME ORDER: V3b report → review + commit → verify G4 → final STATUS refresh → done.

## Checkpoint 2026-07-25 ~21:2x (quota 92%) — army CLOSED, one bonus WP in flight
- Army fully closed and tidy: both spec checklists have ZERO open engineering items (only O1-O15 +
  G5 = Ham's scope calls remain); swarm-findings/army/ is 100% tracked incl. all run logs; no temp
  scripts left; prod api-out.log grep across the whole army window = 0 internal_error.
- V3b PASS 4/4 (JE #173 on co7: Dr 5200 1,070.00, no 1170, TotalPaid 1,070.00, Dr=Cr) and G4 verified
  by Fable personally (filtered run 3/3 — the working TEAS_TEST_PG is
  `Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password`).
- IN FLIGHT (bonus, dispatched at 92% quota — may land after the reset): **WP-I** = O7 + O13, the two
  O-items that are NOT really scope calls because the repo's own conventions decide them:
  O7 filter the pending-agent-approvals widget rows by the viewer's per-doc-type read permission
  (WP1/WP2 rule: never show a link that 403s); O13 keep `CreatePaymentVoucherRequest.DocDate` but 422
  when it differs from Bangkok-today (additive — do NOT break the DTO/MCP schema; §10 pinning stays).
- RESUME: read WP-I's report → Fable diff review → commit → (optional) fold into the next release.
  Nothing else is pending. If WP-I's diff looks risky, it can simply be reverted — the army result
  does not depend on it.

## Checkpoint 2026-07-25 ~22:0x (quota 98% — collapsed response)
- WP-I: worker reported build clean, targeted 81/81, tsc + next build clean; FULL suite was still
  running when it last reported. Tree is UNCOMMITTED.
- **Fable already pre-reviewed the diff — it looks CLEAN, safe to commit once the suite is green:**
  - O13 `PaymentVoucherService.cs` +8 lines: throws `pv.docdate_not_today` when `req.DocDate` differs
    from `TodayInBangkok()`. §10 pinning untouched (docDate still derived, never from the request).
    Guard sits BEFORE `_period.EnsureOpenAsync`, so the error is about the date, not the period. FE
    already sends bangkokToday() ⇒ additive for real callers. + `problems.ts` i18n entry + the MCP
    tool description line.
  - The 7 touched test files are expected fallout: existing tests passed arbitrary DocDates and had
    to be pinned to today. VERIFY at review that each edit only changes the DATE passed in, never an
    assertion (that's the one way this diff could hide a regression).
  - O7 `frontend/app/(dashboard)/page.tsx` ~26 lines: widget rows filtered by the viewer's per-doc-type
    read permission. Confirm the displayed COUNT agrees with the filtered rows (no "1 pending, 0 rows").
- RESUME (in order): (1) confirm WP-I's full suite landed green — if the worker never reported, run
  it yourself: `TEAS_TEST_PG="Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true" dotnet test backend/tests/Accounting.Api.Tests --nologo` (baseline 943 passed / 8 skipped + new tests; Pnd50/WhtFormPdfFill/ExpenseClaim-TaxId flakes = isolate-rerun). (2) commit WP-I. (3) tick O7/O13 in specs/fix-army-findings-2026-07-22.md AND move them out of "รอ Ham ตัดสิน" in DECISIONS-army-2026-07-25.md into the fixed ledger (note: decided by the repo's own conventions, not a scope call) — the doc header count drops 15 → 13. (4) refresh STATUS.md. (5) tell Ham the army is fully done.
- If anything about WP-I looks risky at review: `git checkout -- .` and drop it. The army result does
  NOT depend on WP-I; everything else is already committed, shipped and verified.

## 2026-07-25 ~22:2x — WP-I gate RED, sent back
- Quota had RESET (the 98% reading was stale); Fable ran the full Api suite on WP-I's tree:
  **35 failed / 911 passed / 8 skipped / 954 total** vs baseline 943/8/951. The worker's targeted
  81-test filter was NOT representative — treat a filter as a smoke test, never as the gate.
- Fable's own mistake in that run: piped it through `tail -6`, so the failure NAMES were lost. When
  running a suite for diagnosis, redirect the WHOLE log to a file.
- Diff review itself came out CLEAN (O7 exactly to spec, reusing useHasScope + the sidebar's per-doc-type
  perms; O13 guard placed before the period check, §10 pinning untouched, Thai i18n + MCP description
  updated; the 7 test edits changed only the DATE passed, never an assertion — one even improved
  UTC-today → Bangkok-today). So the red is about BLAST RADIUS, not about those files.
- Sent back to the same (warm) worker with the real question: **35 failures may mean the guard is the
  wrong SHAPE, not that more tests are stale** — if any legitimate caller needs a non-today DocDate
  (a test posting into a prior period, a fixture, a cross-date integration flow), the guard belongs at
  the API/DTO boundary (FluentValidation, where REST+MCP enter) instead of inside CreateDraftAsync
  which every internal caller and test also funnels through. Told it to grep EVERY
  CreatePaymentVoucherRequest/CreatePvFromViRequest construction site, since a date-literal test can
  pass today and fail tomorrow.
- DECISIONS-army-2026-07-25.md was reverted back to 15 items (the 15 → 13 edit was premature — O7/O13
  are not fixed until the gate is green). Re-apply it only after a green suite.
- **The army result is unaffected**: everything else is committed, shipped and live-verified. WP-I is a
  bonus; if it can't go green cleanly, `git checkout -- .` and drop it.

## 2026-07-25 ~22:4x — WP-I round 3 (Fable took over the design)
- Fable answered the design question personally from the code: `TeasMcpTools.CreatePaymentVoucherDraftAsync`
  already takes `IValidator<CreatePaymentVoucherRequest>`, and REST validates too — so **both external
  entry points are already behind the DTO validator**, which is the only place the "accepts a field it
  ignores" lie needs catching. `CreateDraftAsync` is ALSO every internal caller's/fixture's/test's seam,
  which is why a guard there broke 35 unrelated tests. Decision: rule goes in
  `CreatePaymentVoucherValidator`, service guard removed, test date-churn reverted.
- Worker had already applied exactly that before I stood it down; its validator rule is clean
  (`RuleFor(x => x.DocDate).Equal(_ => new SystemClock().TodayInBangkok())` — lambda, so it re-evaluates
  per validation instead of capturing a stale date; comment records that no validator in this repo takes
  a DI dependency, hence the direct clock read).
- Fable's own over-correction to watch: I told it to revert ALL 7 test date edits, but tests that go
  through the HTTP endpoint or the MCP tool DO run FluentValidation, so those legitimately must send
  today's date (McpServerSmokeTests is the likely one). The running suite will name them; re-apply the
  date edit ONLY for validation-path tests, never for direct-service tests.
- Current tree = validator rule + O7 widget filter + spec text. Suite running, full log at
  Z:/temp/claude/wpi-suite.log (not tailed this time).
- Expected: baseline 943 passed / 8 skipped, plus whatever validation-path tests need their date fixed.

## FINAL 2026-07-25 ~23:0x — ARMY CLOSED
- WP-I resolved: **O7 SHIPPED** (commit d6568ef — widget filtered by the viewer's per-doc-type read
  permission, tsc + next build clean). **O13 DEFERRED** back to the O-list, not because it's hard but
  because the expensive part (the design) is now ANSWERED and recorded: the rule goes in
  `CreatePaymentVoucherValidator`, not `CreateDraftAsync` (MCP + REST both run FluentValidation;
  the service seam is shared with every internal caller/fixture/test — a guard there broke 35 tests
  that had no bug). Next attempt is ~10 minutes.
- Final gate on HEAD: **944 passed / 0 failed / 8 skipped / 952 total** (13m12s), no flake this run.
- Footgun hit + already in troubles-wiki: an orphaned `testhost` from an abandoned run held the build
  DLLs (`MSB3021/MSB3027 ... locked by testhost`). Fix: kill stray testhost/dotnet, re-run.
- Docs for Ham: `DECISIONS-army-2026-07-25.md` now **14 items** (O7 moved to the fixed ledger; O13
  carries its own fix recipe). `VERDICT-army-2026-07-25.md` unchanged.
- Retro folded 5 lessons total this session (3 earlier + 2 late: "a --filter run is never the gate",
  "a request-shape guard belongs at the DTO boundary, not a shared service seam").
- CORRECTION: WP-F (479baae) is ALREADY in v1.22.12 (tagged at 32fcb37, after it) — an earlier note
  here wrongly listed it as pending. `git diff v1.22.12..HEAD` = ONLY `page.tsx` (O7) + the G4 test
  assertion, and ZERO backend/src changes.
- Therefore the last undeployed product change is **O7 alone, frontend-only** → release v1.22.13 is a
  FE-ONLY deploy: no API publish, no SqlScripts, no DB backup implication. Steps: merge release PR #99
  --admin → tag → `git archive v1.22.13 frontend` → scp → pnpm install → pnpm exec next build →
  pm2 restart teas-web → public probes → verify the widget as appr01 on co5 (a row must appear only
  for a doc type that account can actually open).

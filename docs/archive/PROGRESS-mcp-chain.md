# PROGRESS — MCP document chain cycle (checkpoint @ quota 92%, 2026-07-13 ~10:10)

Spec (single source of truth): `specs/mcp-document-chain.md` — §A Ham rulings,
§B + "§B ADDITION" (BN optional hop, ruled ~10:00) + Fable CRUX rulings, §D approved
design (D1 reuse map w/ file:line, D2 exact enums, D3 JE pins, D5 guards, D6 guide
text, D7 scopes, D8 test list 1-13 + BN additions, D9 FE diff).

## State at checkpoint
- DONE: design phase complete + Fable-approved (D3 money section personally verified:
  4 JEs balanced). v1.19.0 (error surfacing + resolvers) LIVE on prod since ~02:15.
- IN-FLIGHT: sonnet implementer on worktree `Z:\temp\claude\wt-mcp-chain`
  (branch feat/mcp-document-chain off origin/main). Dispatched with full spec +
  BN scope addition queued to it via SendMessage. It does NOT commit.
  Its transcript: Z:\temp\claude\...\tasks\ad841603c48fe876b.output (JSONL — do not
  cat whole; worker reports back itself).
- Quota: 5h window 92%, resets ~11:50 (+101min from checkpoint). 7d: 55%.
  ScheduleWakeup chained (3600s hop 1 → remainder hop 2). New Claude dispatches
  FROZEN until reset; in-flight implementer allowed to finish/die on its own.

## Resume steps (fresh session or post-wakeup)
1. Read this file + STATUS.md + spec §F gates. Check quota state.json ≥ reset.
2. Check implementer status: worktree `git -C Z:\temp\claude\wt-mcp-chain status`
   + whether it reported back (task notification in transcript). Three cases:
   a. Reported DONE → Fable personal diff review (NEVER skip §C/D3-touching code:
      the 5 from-source builders, receipt Applications handling, PV VendorInvoiceId,
      VAT-receipt-vs-BN guard) → dispatch Opus Tier-2 (money lenses: JE pins D3 a-d,
      dedup guards D5, DraftCreated 8-site blast radius, migration McpDocumentChain
      additive-only) → Tier-3 haiku full gate (961/0/8 baseline + new) → commit in
      worktree (feat message per repo style + Co-Authored-By Fable) → PR → CI →
      merge → release-please admin-merge → tag → deploy.
   b. Died mid-work → SendMessage resume it (warm) with "continue from spec
      checkboxes"; if unresponsive, fresh sonnet dispatch: same worktree, spec
      checkboxes show remaining work.
   c. Stalled "waiting" → SendMessage: poll-don't-wait rule (see agent template).
3. Deploy notes THIS release: contains EF migration McpDocumentChain → DB backup
   MANDATORY pre-deploy (pattern: teas-backup-<stamp>-pre-v1200.sql.gz);
   probe sql_scripts count UNCHANGED (68) but migration adds nullable FKs —
   verify via information_schema post-deploy. Deploy scripts pattern:
   publish/v1.19.0/ (gitignored) + repo publish/v1.18.0 README pattern.
4. Post-deploy E2E at Repttown (BUTEST): Q→SO→(service-only skip DO)→IV(BillingNote)
   → send approvalLinkMarkdown links → WAIT for Ham to approve each hop → RC settle.
   Purchase: PO→VI(purchaseOrderId, expenseCategoryId from list_expense_categories)
   →PV(vendorInvoiceId). Verify get_workflow_guide returns non-VAT variant with
   ม.86/4 warning. New tools need a NEW connector session (tool-list cache memory).
5. Report to Ham: cycle summary + BN hop included + what awaits his approval clicks.

## Standing warnings for resume
- Implementer tokens come from the SAME 5h pool — if it is still running at resume,
  let it finish before dispatching anything else.
- Do NOT dispatch gate runner in parallel with any test-running worker.
- FE gate = pnpm tsc + FE tests; backend suite baseline 961/0/8.

## AMEND @ cliff (2026-07-13 ~10:2x)
- Implementer DIED on session limit at the FINAL step: it had finished implementation
  (incl. a REST endpoint added late) and was about to run the last full backend gate.
  Resume case (b): SendMessage the same worker id first (warm transcript) — instruct:
  run the full gate SYNCHRONOUSLY/poll-in-turn, report per original contract.
- ACTUAL reset: 13:30 Asia/Bangkok (API-reported session limit), NOT the 11:50 in the
  earlier state.json snapshot. Chain wakeups until then.

## CYCLE CLOSED (2026-07-13 ~17:3x) — v1.20.0 LIVE
- Implementation finished by a second worker (first died at quota cliff; audit-and-finish
  handover worked cleanly — 1 real gap found: missing Thai approval label for "invoices").
- Fable diff review PASS (all money files read) → Opus Tier-2 APPROVE all 5 lenses
  (JE pins verified against real posted rows; F1 pre-existing DO-branch VAT gap recorded
  in spec for a future cycle) → suite 984/992/0/8 twice → PR #75 → release #76 →
  v1.20.0 deployed, 27/27 API + 4/4 FE + public E2E green, migration applied
  (sys.__ef_migrations — probe footgun wiki'd).
- Live E2E started: quotation draft 7 (BUTEST, service-only) created via MCP —
  approvalLinkMarkdown CONFIRMED live. Remaining chain hops need Ham's approvals +
  a NEW connector session (tool-list cache) — steps for Ham in the final report.

## HOTFIX v1.20.1 checkpoint (@92%, 2026-07-13 ~19:0x)
- Implementation DONE in wt-teas-v1201 (branch fix/bn-settlement-flip, uncommitted):
  ReceiptService H1 (post-time over-applied guard `receipt.over_applied` + Settled flip
  for direct-BN applications), DocumentCrossRefService H2 (4 new chain edges),
  5 new tests. Full suite clean run: 989/997/0/8 (= baseline+5). H3 not reproduced
  (MarkSettled code cannot log Issued→Issued; spec documents next-time capture plan).
- NEXT (resume order): (1) Fable reads the 2-source-file diff (money lens on
  ReceiptService), (2) Tier-2 review — quota ≥85% so route to CODEX (separate pool,
  cross-family precedent; lenses: H1 guard race-safety at POST, tenancy of the SUM
  query, no regression to TI path), (3) commit in worktree + PR + CI + merge +
  release-please (fix: → v1.20.1) + deploy (NO migration this time — no DB backup
  gate needed beyond standard), (4) post-deploy: assert BN 4 can be marked settled /
  new receipt against it rejected, re-run chain E2E second receipt attempt expecting
  `[mcp.domain_rule]`-style rejection text, (5) close cycle in STATUS.
- Wakeup chained to 5h reset (~19:50) in case the cliff kills this session.

## v1.20.1 DEPLOYED + CYCLE FULLY CLOSED (2026-07-13 ~22:25)
- Hotfix live: 27/27 probes, version 1.20.1, migration state unchanged.
- H2 verified LIVE: get_document_chain(quotation 7) now resolves the full
  Q→SO→IV→RC chain across the skip-DO edge.
- H1 verified by tests (transition-exercising, would fail pre-fix) + Opus tx
  trace; live browser probe blocked by web session expiry post-restart (login
  = Ham-only). Pre-fix data corrected: BN 4 flipped to SETTLED via one-off SQL
  (matches financial reality; backups from 22:14 on prod).
- Lesson folded into implementer template: state-transition tests must
  exercise the transition, not seed the target state.
- H3 (MarkSettled Issued→Issued log) remains open-low; capture plan in spec.

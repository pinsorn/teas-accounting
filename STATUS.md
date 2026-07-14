# STATUS.md — orchestrator live board

## Now
- Goal (2026-07-14, Ham away): purchase-side UX/UI test on PROD (BU TEST) via
  Claude Chrome + refresh outdated manual ch.5. PROGRESS-purchase-uxtest.md =
  full findings log (F1–F27; top: F15 VAT-fraction field accepts "7"→700%,
  F16 ~25-30min token TTL w/ silent-401 UX, F20 COGS no default GL on Repttown,
  F27 non-VAT company can post recoverable input VAT).
- Phase: **PURCHASE-FINDINGS FIX PIPELINE COMPLETE — WP1–WP4 all landed on main (a86de78).**
  Prod UX test (F1–F29, commit 0106b0b) + manual ch.5 refresh (518e1ed) + all four fix
  work-packages shipped: WP3+WP4 (d88ee51), WP1 money/compliance (65b9b2b), WP2 auth/session
  (d5a9c69), WP3.4 PO close/reopen + WP4.9 SoD (a86de78). Every money/security diff got an
  Opus Tier-2 review (WP1 money APPROVE-WITH-FIXES → F-1/F-2/F-3 applied; WP2 security
  APPROVE-WITH-FIXES → F-A absolute-cap-bypass fixed). Fable read every money/security diff
  personally before each commit. Ham decisions D1–D7 all confirmed + implemented.
  **NOT DEPLOYED — all on main, no prod release cut.** Pending for a release:
  (1) EF: no new migration (623 is a startup SqlScript, Closed enum pre-existed) — but the
  623 backfill runs at API boot on prod → DB backup + per-company row-count probe mandatory.
  (2) Residuals (tracked in spec): F-C (post-modal re-login company context, fails safe),
  F-D (proxy Location, acceptable), F-5 (move rate bound into BuildLinesAsync, hardening),
  WP3.4 reopen has no PV-downstream check (PVs settle VIs not POs, so VI-Posted check suffices).
  (3) Manual ch.5: remove the F15 fraction-VAT admonition + re-capture 05.02 once percent-UI
  is visible in prod. (4) Pre-existing broken test found (NOT from this work):
  McpServerSmokeTests.E3_create_vendor_returns_id_code_name — baseline fails identically,
  needs separate triage. (5) Ham: rotate the password leaked in logs early this session.
  Ham's uncommitted edits (CLAUDE.md, specs/fix-codex-review, specs/mcp-document-chain) left
  intentionally untouched.
- Prior phase (superseded):
  Fix spec: specs/fix-purchase-ux-findings-2026-07-14.md (Opus design §Design + Ham
  decisions D1–D7 all confirmed). Prod UX test + manual ch.5 both DONE earlier.
  - WP3+WP4 (FE flow/polish, F2–F24) merged d88ee51 — Fable diff-reviewed, tsc green.
  - WP1 (money/compliance, F13/F15/F20/F27) merged 65b9b2b — non-VAT non-recoverable,
    percent-UI, vendor taxId rule, RLS-safe category backfill 623 + auto-seed. Opus
    Tier-2 money review APPROVE-WITH-FIXES (F-1/F-2/F-3 applied), 262 backend + 40 FE
    tests green. **623 SqlScript NOT deployed** — prod deploy needs DB backup +
    per-company row-count probe (design has both).
  - **RESUME AFTER QUOTA RESET (5h window, resets ~17:40 / ts 1784041200):**
    (1) WP2 auth/session — Sonnet implement from §Design WP2.1–2.4 (D6 sliding re-issue
    Option A: POST /auth/refresh + BFF route + FE keep-alive hook, absolute cap 8-12h +
    idle timeout; WP2.2 global 401 modal preserving form state; WP2.3 trailing-slash 308
    root cause — VERIFY-before-fix with Network panel, then app-wide slash removal +
    AbortController timeout + proxy Location hardening; WP2.4 Thai error toasts by code).
    Auth = security → Opus Tier-2 review after, then Fable diff + commit + ff main.
    (2) WP3.4 PO Closed status (D3 confirmed: Approved→Closed, no further VI/PV linking,
    drops from open-PO lists, activity-logged, reopen if no posted downstream) — needs
    backend status + /close endpoint. (3) WP4.9 SoD text-align (D4, trivial FE i18n).
    (4) F-5 residual hardening (move rate bound into BuildLinesAsync) — optional later.
    Ham's uncommitted edits (CLAUDE.md, specs/fix-codex-review, specs/mcp-document-chain)
    are intentional — DO NOT commit them.
  - Quota paused at 82% (block 95%) with WP2 a large footgun dispatch pending; wakeup
    chained to reset per Ham's "1hr loop = safe" rule.
- Prior goal: MCP document chain cycle (Ham approved 2026-07-13 morning, autonomous
  while away). Spec: specs/mcp-document-chain.md — §A carries ALL Ham rulings
  (per-hop draft tools, data-driven skip-DO, full-qty only, purchase side in,
  workflow guide + instructions, approvalLinkMarkdown, verify-then-advance).
- Phase: **DEPLOYED — v1.20.0 LIVE on prod** (2026-07-13 ~17:13, tag v1.20.0 @
  76e2467). PR #75 merged, release #76. EF migration `McpDocumentChain`
  (20260713032419) applied cleanly at boot: 3 additive nullable FK columns
  (tax_invoices.sales_order_id/delivery_order_id, billing_notes.sales_order_id)
  + 3 partial indexes + 3 FKs, zero data risk (all nullable, no backfill). API
  27/27 probes PASS (23 carried-forward regression + 4 new migration probes),
  FE 4/4 PASS (incl. sales-orders/[id] create-invoice route), public E2E green
  (login 200, /mcp 401, wellknown 200, sales-orders 307). DB backed up twice
  pre-migration (~/backups/teas-pre-v1.20.0-*.sql.gz on prod, 178895-178896B).
  Footgun found + wiki'd: this project's EF migrations-history table is
  `sys.__ef_migrations`, NOT the EF default `__EFMigrationsHistory` — a probe
  written against the default name 42P01s on prod. Scripts archived to
  publish/v1.20.0/ (deploy-api-v1200.sh, deploy-fe-v1200.sh, DEPLOY-README.md).
  Next: chain E2E at Repttown (real create_invoice_draft/document-chain walk
  through the live MCP connector), then close out this cycle.
- Prior: **v1.19.0 LIVE on prod** (2026-07-13 ~02:15): MCP error
  surfacing + 4 resolver tools + bank-match FE warning (#72). Deploy 23/23
  API probes + FE 3/3 + public E2E green. Post-deploy MCP probes CONFIRMED
  client now sees `[mcp.validation]`/`[mcp.domain_rule] ... (ม.86/4) ...`
  instead of the old generic swallow. Full night's story:
  PROGRESS-mcp-butest-sweep.md (Sana report → zero backend defects → SDK
  swallow bug fixed E2E). PRs #74 + release #73; wiki entry on the MCP
  client-SDK WhenWritingNull test footgun committed (5f52e1e).
- **HOTFIX v1.20.1 DEPLOYED to prod** (2026-07-13 ~22:15, tag v1.20.1 @
  14a1461, API-ONLY — no EF migration, no new SqlScripts, no FE change, FE
  deploy skipped entirely). Fixes H1 BLOCKER (direct-BN settlement never
  flipped Settled + no over-collection amount guard — double revenue
  possible on a settled BN) AND H2 (chain resolver missing the new
  mcp-document-chain SO↔BN/TI forward-FK edges — a chain anchored on a
  skip-DO/DO-direct TI or invoice never resolved its upstream SO/DO/Q).
  H3 (web MarkSettled Issued→Issued no-op logging) not in this release.
  Opus spot-reviewed the hotfix diff APPROVE (2 low-severity residuals
  accepted: F1 concurrent-partial-receipt race, web-only, low; F2 unused
  var, harmless) — see specs/mcp-document-chain.md HOTFIX section.
  Built from a fresh worktree `Z:\temp\claude\wt-teas-v1201-build` (NOT the
  dev worktree `wt-teas-v1201`, left untouched on `fix/bn-settlement-flip`),
  MinVer stamp confirmed `1.20.1`. API 27/27 probes PASS incl. all v1.20.0
  migration-state probes reasserted UNCHANGED (`total_sql_scripts=68`,
  `mcp_chain_migration_still_applied_once=1` against `sys.__ef_migrations`).
  Public E2E green (login 200, /mcp 401, wellknown 200 — bare paths, no
  `/api` prefix, backend has no ingress of its own). DB backed up twice
  pre-deploy (`~/backups/teas-pre-v1.20.1-*.sql.gz` on prod, 203440-203443B).
  md5-verified tar+script local==remote before deploying. Scripts archived
  to publish/v1.20.1/ (deploy-api-v1201.sh, DEPLOY-README.md).
- **CYCLE CLOSED** (2026-07-13 ~22:25): H2 verified LIVE
  (get_document_chain(QT-7) resolves full Q→SO→IV→RC across skip-DO edge).
  H1 verified by transition-exercising tests + Opus tx trace (live browser
  probe blocked by post-restart session expiry — login is Ham-only). Pre-fix
  data corrected: BN 4 (07-2026-IV-TEST-0001) flipped ISSUED→SETTLED via
  one-off SQL, matches financial reality (RC-5 covered it in full; backups on
  prod). Lesson folded into implementer template: state-transition tests
  must EXERCISE the transition, not seed the target state. H3 open-low.
  Phase: IDLE.
- For Ham (morning): (1) new MCP tools appear after the connector's next
  session/reconnect (tool list caches per session); (2) review the 19
  expense categories + account mapping I seeded for co2 (delete-me:
  `DELETE FROM sys.expense_categories WHERE company_id=2`); (3) BUTEST test
  drafts await your delete/void: PV 1, vendor-invoice 1, expense-claim 1 +
  BUTEST-EMP employee; (4) co2 still has NO bank account (bank rec unusable)
  and no real employees; (5) product decision pending: auto-seed expense
  categories at company creation + expense-category/employee CRUD UI (none
  exists — SQL was the only path tonight); (6) push the tightened
  implementer/gate-runner "poll, never turn-end-wait" rule upstream to
  minions-assemble.
- Ham RULED (2026-07-10 evening): #4 = keep as documented limitation (closed,
  no code). #7 = FE warning on >7d gap (sonnet implementing on
  feat/match-window-warning). PV SelfWithhold = investigated by Opus → NOT A
  BUG (TotalPaid conditional on payer mode, single computation site :219,
  worked examples tie out; only a stale comment at PaymentVoucher.cs:52 —
  cosmetic). Findings in specs/fix-codex-review-2026-07-10.md.
- In-flight: none. #7 warning DONE — PR #72 merged to main (CI green, Fable
  diff review pass); rides the next release, no separate deploy.
- Next: PARKED by Ham 2026-07-11 — no next cycle yet. Backlog candidates when
  ready (phase-2s): per-bank GL posting (unlocks DocReconciliationLimited),
  receipt OCR via MCP/AI, bank feed API, tax-vs-book depreciation, FA category
  master; plus the "not now" list in PLAN-feature-cycle-2026-07.md and small
  debts (PaymentVoucher.cs:52 comment, mobile-viewport smoke, PR #72 rides the
  next release).

## Recently done (2026-07-10 evening)
- v1.18.0 DEPLOYED — (1) MCP expansion v2: 14 read/draft-create tools for bank
  rec, expense claims (+list_employees, PII-slim), fixed assets; scopes in
  McpScopes.All + FE picker; no state-changing tool (test-asserted). (2) Codex
  fix round: all 10 accepted findings fixed (bank-rec report scoping per Opus
  addendum, override validations, double-match unique indexes, draft-edit 409s,
  CSV injection, parser strictness) + 24 targeted tests. Suite 957/0/8.
  API DEPLOY_OK 21/21 (incl. match_target_unique_indexes=2, total_sql_scripts=68
  prod-baseline), FE_DEPLOY_OK (api-keys route + 3 regressions), public E2E
  green (login 200, proxies 401, /mcp 401). PRs #69/#71, release #70.
  Pre-deploy dedup gate ran clean (prod had 0 matched lines).
- Codex cross-family review of v1.14.0..v1.17.0 delivered 11 findings (2
  BLOCKING) that three layers of Claude-family review missed — cross-family
  review now proven twice (Cycle B + this round).

## Recently done
- 2026-07-10 v1.17.0 DEPLOYED — Cycle C expense claims (submit/approve/pay,
  self-contained JE, no WHT) + Cycle D fixed assets (register, straight-line
  depreciation with dual-direction final-month plug, disposal/write-off,
  period-close hook). API DEPLOY_OK 21/21 probes (seeds 616-622 first try,
  fanout exp=20 fa=22, fa_accounts=10, RLS true), FE_DEPLOY_OK (3 new routes),
  public E2E green (login 200, proxy 401s, pages 307). PRs #66/#68, release #67.
- 2026-07-10 deploy false-fail lesson: total_sql_scripts probe expected repo
  file count (88) but prod ledger has 68 (pre-squash scripts baked into EF
  migrations, never individually recorded) — auto-rollback fired on a healthy
  deploy; fixed expectation, re-ran, DEPLOY_OK. → troubles-wiki.
- 2026-07-10 quota cliff mid-D-implement (session limit) — checkpoint+wakeup
  protocol worked; resumed clean at reset.

## Recently done
- 2026-07-09 v1.16.0 DEPLOYED — bank reconciliation live: bank master, KBiz CSV +
  K-Plus PDF (password) adapters, matching + inline JE, reconciliation report.
  API DEPLOY_OK 13 probes (bank_tbl=3 scripts=2 perms=5 fanout=30 rls=true; seeds
  passed FIRST TRY on prod — post-42501 RLS-safe patterns held), FE_DEPLOY_OK,
  public E2E green (proxy 401s, pages 307). PR #64 (4+1 commits), suite 882/0/8.
- 2026-07-09 review chain earned its cost AGAIN: Opus Tier-2 (B4, 2 findings fixed),
  Fable diff review caught SPEC-level tie-out sign flip, sonnet cross-review caught
  cumulative-window bug + CI-only storage-path test failure. 3 money bugs, 0 reached prod.
- 2026-07-09 v1.15.1 DEPLOYED — Cycle A (year-end closing, period close UI, ar-aging
  CSV, docType i18n) after 42501 seed-RLS hotfix.

## Blocked / waiting
- Ham to confirm: .gitignore had no other entries beyond codex-out//agy-out/ (reset
  --hard incident, restored 2026-07-09 — see PROGRESS-cycle-a retro).
- Carryover: FE browser smoke of prod (Ham login at Chrome tab) — now covers v1.16.0.

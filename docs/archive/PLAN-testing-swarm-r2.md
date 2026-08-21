# SCOPE — Testing Swarm Round 2 (2026-08-18, drafted; run on a fresh quota window)

Round 1 (2026-08-15/16) hard-tested the sales + purchase document chains and found **14 defects, all
now fixed** (`PROGRESS-local-hard-test.md`, PLAN-fix-findings-2026-08-16.md). The external review
follow-up (N1/N2/N3, `047fe95`) is also closed. Round 2 targets the modules round 1 never touched,
on the working theory that proved out last time: **finding density follows untested surface, not
suite coverage** — 1,243 green tests caught zero of round 1's five money/tax findings.

## Method — same discipline that made round 1 work
1. **Read the tables, not the screen.** Every claimed number is verified in Postgres (psql
   READ-ONLY — memory `test-data-via-ui-only`: fixtures are built through the UI/API only, never
   direct INSERT).
2. **Ledger tie-out closes every leg.** Trial balance balanced, every journal header = its own
   lines, subledgers reconcile to control accounts, no document carrying a tax code absent from its
   company's master.
3. **Walk both doors.** Create paths AND edit/re-save paths — round 1's worst regression came back
   through the edit door with the suite green.
4. **Find, don't fix.** Workers record findings with evidence (doc numbers, row values, repro
   steps); severity per round-1 scale (🔴 money/tax/security → low). No worker touches source.
5. **Go around the UI too.** Direct POSTs with valid payloads against permission/route guards
   (round 1's F5/H4 shape), malformed input against error contracts (typed 422/400, never a raw
   500 with .NET text).
6. Findings append per-leg to `PROGRESS-hard-test-r2.md` (created at kickoff from the round-1 file's
   format); Fable folds them into one fix-plan table at the end, same as PLAN-fix-findings.

## Legs

### Leg 1 — Payroll (highest priority: direct tax touch, untested)
Company: Demo Company (co1). Walk: employee master → payroll run (calc, WHT ภ.ง.ด.1, สปส.)
→ approve → pay → GL.
- ภ.ง.ด.1 / ภ.ง.ด.1ก: figures vs hand-computed progressive WHT; filing refuses/warns on missing
  identity (compare the F10 refuse precedent — payer tax ID, employee tax IDs).
- สปส.1-10: 5% employee + 5% employer, the ฿15,000 cap, rounding; employer side reaches expense,
  employee side reaches liability; batch export guards (`sso_batch.*` errors already exist — do
  they fire?).
- Deductions: `specs/payroll-deductions-o10.md` has one [~] item — verify current live behaviour
  against that spec's checklist while in there.
- GL: salary expense / WHT payable / SSO payable / net pay clearing — Dr=Cr per run, payable
  accounts clear when the payment voucher pays them.
- Edit door: reopen a draft payroll run, re-save unchanged → nothing moves.
- Period interplay: payroll posting into a closed month must refuse.

### Leg 2 — Bank reconciliation
Company: co1. Statement import (KBiz CSV adapter exists — `KBizCsvAdapterTests` names the format) →
match → adjustments → close.
- Reconciled balance vs GL cash account, to the satang.
- Unmatched/partially-matched handling; duplicate import of the same statement file (idempotent or
  duplicated?).
- Adjustment entries it creates: correct accounts, correct period, visible in the JE list.
- Malformed CSV → typed error, not 500 (F2/F4 shape).

### Leg 3 — Fixed assets + depreciation
Company: co1. Register asset (via purchase flow if wired, else asset screen) → depreciation run →
month close interplay → disposal.
- Depreciation math (straight-line per config), first/last month proration, accumulated
  depreciation account movement.
- Run depreciation twice for the same month → idempotent or double-posted?
- Disposal: gain/loss computation reaches the right account; asset stops depreciating.
- Closed-period refusal; year-close with un-run depreciation — blocked, warned, or silent?

### Leg 4 — Expense claims
Company: co1. NOTE: `specs/expense-claims.md` shows 8 open items — this leg FIRST establishes what
is actually implemented (read the routes/screens), then tests what exists and reports the spec-vs-
reality gap as its own finding. Claim → approve (SoD?) → pay → GL.
- Same-user create/approve segregation (PV has a DB CHECK for SoD — does expense claim?).
- VAT on claim lines (recoverable vs not per category), WHT if applicable.
- GL: expense account from category default, clearing through the payment.
- Permission gating on buttons vs backend 403s (F6 shape).

### Leg 5 — co2 tenant (production-shaped data) — **READ-ONLY**
⚠ Memory `co2-demo-loadbearing-pl-polluted`: ch7/8 walkthroughs tie to co2's P&L; JEs are immutable
with no void. **No document is created, edited, or posted in co2 in this round. psql read-only +
UI read-only.** If a finding needs a write to prove, record it as "needs write repro" and stop —
Ham decides (a wipe+reseed restores clean, but that is Ham's call, not a worker's).
- Full ledger tie-out on real-shaped volume (the round-1 checks, at co2 scale).
- Master integrity: tax codes on documents all present in co2's master (F13 shape), orphan
  FK sweep, numbering continuity per series (no gaps/dupes — H1 shape).
- Reports cross-check: P&L / TB / VAT registers vs direct SQL aggregation of the same period.
- ภ.พ.30 boxes vs SalesCategorizer expectations (EXEMPT vs ZERO_RATED bucketing — N1's M5 now
  matters on real data).

### Leg 6 (cheap tail, fold into any leg) — round-1 leftovers
- Company 4 ("NV-ร้านนอนแวต2"): post one sale end-to-end, check stored tax code + ใบกำกับภาษี
  refusal (~10 tool calls, round 1 left it unfinished).
- Re-verify N1 live: exempt product through the UI on co1 — screen 0% AND stored pair exempt at
  0% (the fix is committed but has never been seen live).
- Re-verify N2 live: convert the same quotation twice — second refusal `quotation.already_invoiced`.

## Ops rules
- **Stack boot:** memory `local-stack-boot-recipe` — two env overrides + seed on an EMPTY DB in ONE
  boot, else no roles. Stack is currently DOWN. Restart :3000 if it has run overnight (stale-chunk
  memory) before believing any FE bug.
- **Parallelism:** legs run through the UI on ONE shared stack + DB — that is safe for reads, but
  two legs must NEVER both touch period close / year close, and only ONE leg posts documents in a
  given company at a time. Safe pairing: Leg 5 (read-only co2) runs alongside anything; Legs 1–4
  post into co1 → run them **sequentially** (or move one to co3 if its module works non-VAT).
  No `dotnet test` runs during the swarm (same DB).
- **Workers:** sonnet per leg, findings-only dispatch (no source edits, no commits); AGY optional
  for digesting long statement/CSV fixtures. Each dispatch carries: boot state, psql read-only
  rule, the leg checklist above, the finding format, and the co2 write-ban where relevant.
- **Fable:** dispatches, reads every finding against the DB before accepting it into the plan
  (round-1 memory `subagent-misattributes-fresh-db-fail` — workers mislabel; verify company +
  assertion), owns the final consolidated fix-plan.
- **Quota:** start on a fresh 5-hour window; round 1's shape suggests ~4 worker-legs ≈ one window.
  85% → checkpoint protocol as usual.

## Out of scope this round
- Fixing anything (separate fix batch, same as round 1 → PLAN-fix-findings pattern).
- Server migration / deploy (separate project; the §N2.5 deploy pre-check note lives in
  specs/fix-review-n-findings-2026-08-17.md).
- MCP tool surface (round 1 covered it; unchanged since except the two scope fixes).
- Load/perf testing.

## Exit criteria
Every leg reports its checklist walked with DB evidence, ledger tie-out green (or its failure IS
the finding), findings table consolidated, and a fix-plan drafted in the PLAN-fix-findings format
with units, routing, and traps.

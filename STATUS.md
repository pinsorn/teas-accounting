# STATUS.md — orchestrator live board

## Now
- **✅ v2.0.0 LIVE (2026-08-13) — R2 compliance filings.** API + FE both deployed and probed.
  Major bump, not 1.29.0: WP-7 removes a public endpoint and the `feat!` is honest about it.
  - **WP-2** ภ.พ.36 declares a foreign service ONCE, at the ม.83/6 payment tax point. It used to
    union invoice rows with voucher rows and never dedup, so the ordinary chain was declared twice —
    and split across two filed periods when it straddled a month.
  - **WP-3** a company with no VAT registration can no longer preview, print, batch-export **or
    finalize** a ภ.พ.30. co7 had a `Finalized` VAT return on record with its real tax ID.
  - **WP-4** ภ.ง.ด.1 / สปส.1-10 / the on-screen schedule require a **Posted** payroll run. They used to
    render from a draft with `journalId: null` — a signable return with no ledger behind it.
  - **WP-7 (BREAKING)** `POST /billing-notes/{id}/mark-settled` deleted. A receipt is now the only
    proof of settlement.
  - **WP-1 — C4 RETRACTED, it was never a defect.** The swarm's "totals on row 5" was a
    `pdftotext -layout` misreading, reproduced on demand and refuted from the rendered page. The
    *committed field map* was the thing that was wrong.
  - **WP-5 / WP-6** (shipped in the same release): สปส.1-10 refuses a blank employer account and an
    unfilable name; ภ.ง.ด.50/51 reject a nonsense year with 422 instead of an unmapped 500.
  - Gates: suite **1170 / 0 / 14 skipped**, tsc 0, vitest 65/65, CI green on main.
  - **Tier-2 REJECTed the release once** and was right both times: an i18n entry that deleted the
    employee id and character code point from the only message carrying them, and a payroll dead end
    (bad pay date → un-postable → un-deletable → un-replaceable) that this release itself sealed shut
    by making filings refuse an unposted run. Both fixed before shipping.

- **🟡 ONE THING LEFT, AND IT NEEDS HAM: log in to https://teas.kazaki-rio.com again.**
  The API restart during deploy invalidated the browser session (`No session`, refresh 401), and I
  cannot log in myself. Once logged in I finish the Tier-4 leg — three checks, all read-only:
  1. `/invoices/3` on co2 shows **สร้างใบเสร็จ** and **no ยืนยันชำระครบแล้ว** (it was there before the release —
     baseline captured).
  2. ภ.พ.30 on a non-VAT company refuses with `pp30.non_vat_blocked`.
  3. A draft payroll run refuses to produce ภ.ง.ด.1 / สปส.1-10.
  Server-side proof already in hand: version 2.0.0 live, **zero** `mark-settled` references in the
  deployed assembly (with a control grep proving the check works), and the settled-invoice census
  unchanged on both real tenants (co2=3, co3=1) before and after.

- **🔴 STILL BLOCKED ON HAM — the RD PDF templates (E6/E8).** ภ.พ.36, ภ.ง.ด.2 and the ส่วนที่ 2
  official PDFs. No file, no form filler; nothing else in R2 depends on them.

- **⚠️ One finding shipped KNOWINGLY and needs a tax decision.** ภ.พ.36 now sources rows from posted
  payment vouchers only, which is right under ม.83/6 — but clearing a foreign vendor's payable with a
  manual journal entry (`Dr 2110 / Cr Bank`) produces no voucher, so that purchase is declared in no
  period at all. Before the change it was declared, in the wrong month. Live exposure is zero (foreign
  reverse-charge invoices exist only on co5; neither real tenant has any; ภ.พ.36 has never been
  finalized), and the old behaviour was wrong on the path that actually has data. Blocking manual JVs
  against AP would create a fresh dead end, so this is a decision, not a patch. In `troubles-wiki.md`
  and queued for R3.

- **Next up:** R3 (guards + doc numbering + the two findings above) and R4 (documents/reports + the LOW
  cluster) — neither designed yet. Then wipe+reseed co5/co7 and re-run the break-it swarm against clean
  companies.

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

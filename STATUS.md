# STATUS.md — orchestrator live board

## Now
- **✅ 2026-08-20 — ALL FOUR PENDING DECISIONS RULED BY HAM AND EXECUTED.** O2b block-on-mismatch
  (`3732f27` — billing_note.lines_not_reconciled at issue, Receipt-pattern reconcile vs linked
  TIs' applied sum) · O5 ภ.พ.36 filled-PDF export (`cc11662` — official RD template from
  docs/RD-Forms, field map render-verified by viewing, /tax-filings/pp36/pdf + FE button) ·
  R10 closed won't-fix · CLAUDE.md reconciled both directions with minions-assemble (`2e31d02`
  local: new 85/95/100 quota ladder + self-wake monitor + staging rules; `4d050cd` upstream:
  money-invariant review, shared-test-DB, orchestrator-runs-suite, 7-day stop). Post-rulings full
  suite: Domain 188/188 + Api 1324/1338 (0 failed, 14 baseline skips, zero flakes). Stack UP on
  HEAD binaries, accounting_dev CLEAN (0 documents, roles correct). Remaining external: E1 CPA
  sign-off (Ham) + everything in MIGRATION-CUTOVER-CHECKLIST.md (needs the new server).
- **✅ 2026-08-19 — CLEANUP BATCH CLOSED (Ham: "ทำทั้งหมดยกเว้น server migration").** 11 units:
  C1 PO tax-code resolver (last verbatim-id writer closed) + MCP employee scope narrowed +
  seed-640 arm test · C2 FE trio (FA modal a11y, PO/DO hardcoded taxCodeId gone, back-dated-claim
  pay note) · C3 e2e suite repaired (6 specs + pickCustomer + TenantIsolation leak) · C4/C7
  **depreciation redesigned**: day-prorated first month per ม.65ทวิ(2)/พรฎ.145 + units-indexed
  schedule (skipped months never silently absorbed; Opus design ratified + Tier-2 APPROVE) ·
  C5 lessons pushed to minions-assemble `9b3f940` · C6/C9 backlog triage: 
  48 stamped-closed, 8 obsolete, 9 alive (4 blocked-on-migration → MIGRATION-CUTOVER-CHECKLIST.md;
  O2b/O5/E1 = Ham decisions; R10 low) · C10 cutover checklist · **C11 seed-181 FORCE-RLS bug
  found+fixed** (ap_clerk/sales_staff had ZERO roles on every fresh install; 181 patched + seed 641
  reconcile). Final gates: full suite Domain 188/188 + Api 1318/1332 (0 fail, 14 baseline skips);
  fresh wipe+boot with rebuilt binaries — all user classes login 200, user_roles 30, 0 documents.
  Board: PROGRESS-cleanup-2026-08-19.md. Stack UP (API :5080 new build, FE :3000). Prod-blocked
  work lives in MIGRATION-CUTOVER-CHECKLIST.md.
- **✅ 2026-08-19 — R2 FIX BATCH CLOSED: all 11 units landed, live re-verify 6/6 PASS.**
  Full ledger: release-notes block at top of PLAN-fix-findings-r2.md. Headlines fixed & verified
  live: non-VAT companies can issue billing notes again · ภ.ง.ด.1 renders the real payer tax ID
  (seed 638) + ALL filing artifacts refuse a placeholder ID (U1+U10, 9 call sites) · sales lines
  can no longer carry another company's tax code (laundering + repair seed 639) · bank-rec closing
  balance deterministic + typed import errors + superseded-import delete · disposal-date guard ·
  ACCOUNTANT gets a name-only employee lookup (seed 640) · 24 screens show real error messages in
  Thai. accounting_dev wiped+reseeded per Ham (backup: D:\teas-backups\accounting_dev_pre_r2fix_wipe.dump);
  fresh-boot gate clean. Local stack UP (API :5080, FE :3000 fresh). NOT deployed — prod down
  (server migration project); deploy pre-probes recorded in the plan.
- **✅ 2026-08-19 — TESTING SWARM ROUND 2 COMPLETE: all 6 legs walked, 5×🔴 4×🟠 5×🟡 found,
  fix-plan drafted.** Overnight autonomous run per PLAN-testing-swarm-r2.md, browser-first
  (Playwright driving the real FE — Ham's directive; Claude-in-Chrome unreachable from Claude Code).
  Board + verified findings: `PROGRESS-hard-test-r2.md`. Fix-plan (drafted, NOT started —
  awaiting Ham's go): `PLAN-fix-findings-r2.md` — headline reds: ภ.ง.ด.1 PDF renders payer tax ID
  0000000000000 (seed 637 repaired the wrong table's twin — company_profile.tax_id still zero);
  non-VAT companies 100% blocked from creating billing notes (non-nullable TaxCodeId DTO);
  billing-note lines accept tax codes absent from company master (no FK — F13 shape returns);
  bank-rec closing balance nondeterministic on tied PeriodEnd; oversized import field → raw 500.
  N1/N2 (047fe95) verified live PASS both doors. Round-close battery: TB balanced co1/co3,
  header=lines clean, F13 sweep found 5 violation rows on round-1-era POSTED/SETTLED co3 docs
  (escalates U2). Evidence durable in `findings-r2/`. Local stack UP (API :5080 / FE :3000); co1
  full of R2 test debris (fine for dev, wipe before demos); local co2 confirmed EMPTY →
  real-volume tie-out deferred to post-migration prod-shaped data.
- **✅ 2026-08-18 — HARD-TEST ROUND CLOSED: all 14 findings fixed.** The last five landed today in
  three commits: `2b82dde` (F11 header discount rollup + F12 P&L default — plus a review-caught paper
  bug: the printed footer needed a GROSS subtotal once the discount became real), `65a5419` (F10 —
  Ham decided REFUSE: PV post throws `wht.payer_tax_id_missing` on a blank/all-zero company tax ID,
  no demo exemption; seed 637 repairs the demo company's all-zero tax ID to `0105000000012`),
  `25a9b8a` (F4 binding failures → typed 400 in both middleware branches + `create_receipt_draft`
  settlement now demands `sales.tax_invoice.read`, F5's mechanism). Final gates: suite
  **1255/0/14 skipped** + Domain 188/188, every fix RED-then-GREEN or collateral-swept
  (70/70 purchase-area classes after seed 637). Stack still DOWN — nothing deployed; release notes
  recorded at the top of PLAN-fix-findings-2026-08-16.md (two MCP key re-scopes + seed 637's
  prod effect).
- **🔵 LOCAL STACK IS UP AND HAS BEEN HARD-TESTED (2026-08-15 night).** Postgres 18 (`S:\Program Files\
  PostgreSQL\18`) + API :5080 + FE :3000, seeded fresh. Full run, evidence and repro steps:
  `PROGRESS-local-hard-test.md`; boot recipe in memory `local-stack-boot-recipe`.
  - **🔴 F5 SECURITY — an API key can mint a Tax Invoice it has no scope for.** MCP
    `create_invoice_draft` is gated only by `sales.billing_note.manage` but mints a **Tax Invoice** on a
    VAT-registered company. Exploited live with a key that lacked `sales.tax_invoice.create`. Worse than
    a stray draft: tax invoices have **no DELETE route at all**, so the draft cannot be removed, and it
    then blocks month close (`period.draft_present`) and year close. Only exit is to post it.
  - **F2 — raw 500s + leaked .NET exception text** on `/reports/pnd30`, the three VAT registers
    (`month=13`/`0`) and `/tax-filings/cit/*` (`year=9999/99999/0/-1`). Same shape WP-6 fixed for
    ภ.ง.ด.50/51; these services were missed.
  - **F1** — a fresh install that enables demo data on a *later* boot gets tenants with **no roles**;
    only super-admins can log in. Seed-ordering fragility in `510`, matters for the server migration.
  - **F6** — the five convert buttons render without checking the target-create permission (backend
    correctly 403s). **F7** — the year-close interlock is real but monthly reopen works, so H10 is not
    the deadlock it was described as; the design should start from what co5 actually hits.
  - **Held under attack:** cross-company isolation (404, no existence leak) · H4 attachment
    authorisation (auditor 403, co3 404) · posted-document immutability (405) · H1 numbering
    (branch bound to 0, unique indexes present, no gaps/dupes) · the money path (Dr/Cr balanced).
  - **✅ F5 and F2 are FIXED and committed (`4988e52`), not just reported.** Tier-2 Opus review on the
    security fix: APPROVE-WITH-NITS, nit applied. Full suite **1233/0/14 skipped** + Domain 188/188,
    and both fixes replayed live against a restarted stack — the original exploit key now gets
    `[mcp.forbidden]` and mints nothing; every listed 500 is a typed 422; `year=3000` still 200.
    Not deployed: there is no server to deploy to until the migration.
  - **Release note when this ships:** any existing MCP key on a VAT-registered company scoped
    `sales.billing_note.manage` without `sales.tax_invoice.create` will start being refused on
    `create_invoice_draft`. That is the fix working, but those keys need re-scoping.
  - **✅ UNIT A CLOSED (`1a13eb1`) — the document chain no longer loses money or mis-taxes a line.**
    F8, F8b, F13 and F14 were one root cause: `ChainLineDto` carried no `lineId`, no `discountPercent`
    and no `taxCode`, so the two convert screens invented them. Both conversions are now **server-side**
    (built from the tracked entity, like the ten paths that were already correct), the server resolves a
    **matched** `(tax_code, tax_code_id)` pair from the caller's own master and never throws, and the
    rate-only dropdown is replaced by a **real tax-code picker** over the new `GET /tax-codes` — which
    finally exposes the eight exempt categories and two zero-rated export codes every company is seeded
    with and the UI could never reach.
    - Verified live by reading the tables: an exempt line now stores `EXEMPT-BOOK` with a matching id at
      0% and a total equal to what the screen showed (was `V7` at 7%, ฿70 the customer was never quoted);
      the same sales order converts with `discount_percent 15.00` and `line_amount 2,125.00`,
      `sales_order_line_id` populated, and **`delivered_quantity` moving for the first time** — the
      over-delivery guard had never once executed.
    - **No amount moved.** Ledger re-checked after: 32,724.12 on both sides, every journal header
      agreeing with its lines, no new document carrying a code absent from its own master.
    - **Tier-2 REJECTed the first cut and was right.** The edit forms hydrated from the same thin DTO,
      so reopening a draft and saving it unchanged reset the discount and the tax code — the same bug
      through a different door, **with the suite green at 1241/0/14**, because the tests covered create
      and not edit. `ChainLineDto` was widened after all; that design decision predated both the picker
      and the conversions becoming payload-free, so the objection it rested on no longer applied.
    - Gates: suite **1243/0/14 skipped**, Domain 188/188, tsc 0.
  - **✅ F1 FIXED (`c2d9249`)** — a company seeded by raw SQL after script 510 no longer ends up with no
    roles. Prevention in the demo seeds (guarded so a fresh single boot still works, since they run
    before 510 defines the function) plus script 636 to repair an already-broken database. Proven by
    replaying the real two-boot toggle on throwaway databases and logging in as a non-super-admin.
  - **✅ F9 FIXED (`6fbad63`)** — the payment-voucher preview no longer overstates what leaves the bank.
  - **✅ F6 FIXED (`edcf9af`)** — the five convert buttons now render **disabled with a tooltip naming
    the permission**, per Ham's call (disable, not hide — the exception is recorded in
    `PermissionGate.tsx`). Browser-verified both ways: disabled with the Thai tooltip for a SALES_STAFF
    user, enabled with no tooltip for a super-admin, and a permission the same user *does* hold stays
    enabled.
  - **Still open, all small and none touching money or tax:** **F10** (a 50 ทวิ issued with an all-zero
    payer tax ID — needs Ham's call on refuse-versus-warn), **F11** (the tax-invoice header discount
    rollup stays zero), **F12** (`/reports/profit-loss` defaults to excluding untagged activity, while
    both shipped callers pass `true`), **F4** (a missing required query parameter returns 500 instead of
    400), and the note that `create_receipt_draft` reads a tax invoice under only `sales.receipt.create`.
    Fix plan with routing and traps per unit: `PLAN-fix-findings-2026-08-16.md`.
  - **Never tested this round, and worth a second swarm:** payroll (ภ.ง.ด.1, สปส.1-10, payslips), bank
    reconciliation, fixed assets and depreciation, expense claims, and co2 — the tenant with the richest
    master data. Fourteen findings came out of the sales and purchase chains alone, five of them money
    or tax, none of which the 1,243-test suite caught.
  - **The local stack is DOWN** (both background servers were stopped). Restart per memory
    `local-stack-boot-recipe` — the two env overrides matter, and the database must be seeded in one
    boot or every tenant ends up with no roles.
  - ⚠️ RLS is **not** exercised locally — both PG roles are BYPASSRLS. Give the new server a
    non-bypassing app role and re-run this pass there.

- **🔵 UI/UX + ACCOUNTING SWARM (2026-08-16) — the books tie out; the documents do not always.**
  Four sonnet agents: two walked the sales and purchase cycles through the real UI, then an auditor
  reconciled the actual ledger and a code sweep mapped every conversion path. Full evidence in
  `PROGRESS-local-hard-test.md`.
  - **✅ The accounting is correct.** All 8 posted journals balance with header matching lines; trial
    balance 32,724.12 = 32,724.12 and the API ties to an independent SQL sum; AR and AP subledgers
    reconcile to their control accounts with **difference 0.0000**; VAT rounds correctly on both
    deliberately fractional cases (69.9993→70.00, 23.3331→23.33); nothing double-posts across the
    ใบแจ้งหนี้/ใบกำกับภาษี pair; a credit note against an already-settled invoice correctly drives the
    customer to −356.66; WHT is 300.00 on the **pre-VAT** base, and AP clears to exactly zero.
  - **🔴 F8 / F8b — converting a document loses the line discount.** Sales order → delivery order
    overstated a document by ฿401.25 (confirmed in the tables), and the same defect exists on
    quotation → tax invoice, where the result is **immutable and legally numbered**. Root cause is the
    API shape: `ChainLineDto` carries no `lineId`, no `discountPercent`, no `taxCode`, so the convert
    screen has nothing truthful to send. Two further consequences: `sales_order_line_id` is NULL on every
    delivery-order line, so `delivered_quantity` never moves and the over-delivery guard **can never
    fire**; and the hardcoded tax code overrides an exempt line into standard 7%.
    Ten of twelve paths are clean, and `create_delivery_order_draft` already builds the same request
    correctly from the tracked entity — that is the pattern to standardise on.
    **The error never reached the GL** — `delivery_orders` has no `journal_entry_id` column at all.
  - **🔴 F13 — tax invoices store a tax code that does not exist.** `/tax-invoices/new` hardcodes
    `taxCode: 'V7'`; the company's master has `VAT7`. Live data confirms one orphan code, on a row whose
    `tax_code_id = 1` *is* VAT7 — the row disagrees with itself. Lookup then falls through to
    "unclassified taxable" at the standard rate, which is how an exempt line loses its exemption.
  - **F9** the payment-voucher preview overstates Grand Total and Net by exactly the WHT (one line, one
    file — the correct net is already computed on that page). **F10** a 50 ทวิ is issued with an
    all-zero payer tax ID and no warning. **F11** the tax-invoice header discount rollup stays zero.
    **F12** the profit-loss endpoint defaults to excluding untagged activity and returns all zeros,
    while both shipped callers pass true.
  - **Nothing fixed this round — deliberately.** The 7-day quota reached 82%, and F8/F8b/F13 are
    footgun-zone (money, tax, a shared DTO across the whole chain) so they need an Opus design rather
    than a quick patch. Suggested order next session: **F9** (one line, contained) → **F13** (wrong tax
    code on a legal document) → **F8/F8b** (design first) → F10 → F11/F12.
- **🔴 TEAS PROD INTENTIONALLY DOWN (2026-08-14 evening) — server crisis, migration pending.**
  The OVH VPS hit 96% disk + RAM exhaustion and crash-looped; recovered via rescue mode (freed ~21G,
  disk now 65%, RAM 4.6G free). Ham decided **TEAS moves to a new server** — teas-api + teas-web were
  removed from pm2 autostart (backup: `dump.pm2.teas-bak`) and the site returns Cloudflare 521 until
  the migration. n8n / MT5 bot / OneDrive / openclaw also disabled or deleted. Full state + restore
  steps: memory `teas-prod-disabled-server-crisis`. Next TEAS work item: **plan the server migration**.
  **Until the new server is live, ALL TEAS testing runs on LOCAL** (localhost API + FE + local PG) —
  no Tier-4 live-prod leg, no prod probes; anything that previously targeted teas.kazaki-rio.com
  points at the local stack instead.

- **✅ v2.2.0 LIVE (2026-08-14) — the duplicate document-number bug class is CLOSED, both halves.**
  Seven unique indexes moved to `(company_id, doc_no)`, so the database itself now refuses a second
  document with the same number. v2.1.0 had stopped the allocator from minting one and added detection;
  this is the structural guarantee behind it.
  - **All 11 production duplicates were renumbered, not deleted** — every document survived. The later
    of each pair moved to the next free number in its own space and its journal entry's `reference` and
    `description` moved with it, because the number is denormalised there and renaming the document
    alone would have left the ledger pointing at a number that no longer identified it. co2 Repttown's
    two receipts (฿3,000 and ฿18,000) are both intact, now `…-0001` and `…-0003`.
  - Deployed with four preconditions gating the swap. That mattered more than usual: `DbInitializer`
    runs unguarded before `app.Run()`, so a `CREATE UNIQUE INDEX` raising 23505 would not merely block
    the release — the API would never start and would restart-loop.
  - Every new index name still contains `doc_no`, which is load-bearing: the numbering retry heals a
    collision by matching the constraint name for that substring.

- **🟢 Nothing is blocked and nothing is waiting on a decision.** R3's next items are undesigned rather
  than stuck: the 500 family · conversion routes checking the wrong scope · the year-close deadlock ·
  the year=3000 bound. Two CPA questions remain open (E2, E3) and neither blocks code.
  One worth scheduling: **CI never runs vitest**, so the compliance-control test that keeps the
  number-gaps page from showing a green shield over a live breach has no automated enforcement.

- **✅ v2.1.0 LIVE (2026-08-14) — R3 round one.** API + FE deployed, 10/10 API probes and 4/4 FE
  probes, Tier-4 verified in the browser on the live site.
  - `18f6fcc` **F1** — ภ.พ.36 surfaces foreign-service payments it would otherwise miss. Clearing a
    foreign vendor's payable with a manual journal entry produced no voucher, so under v2.0.0's correct
    payment-tax-point rule that purchase was declared in **no period at all**.
  - `0381d60` **H4** — attachment download *and* delete authorize against the parent document.
    `sys.attachment.read` is granted to every role, so anyone could walk ids and pull files from
    documents they cannot see.
  - `ca820f5` **H1** — document numbers are sequenced per company, not per login channel. Branch scoped
    the counter but never appeared in the printed number, and branch is *who is logged in* (web UI = 0,
    API key / MCP = the real branch), so any company driven through both ran two counters into one
    number space. Ships with a reconcile and with detection.
  - **Tier-4 proof:** `/number-gaps` on co2 now shows a red "พบเลขเอกสารซ้ำ (1)" banner and the row
    `sales.receipts | 07-2026-RC-LAB-0001 | copies 2 | branches 0, 2` — **no green compliant shield**.
    co7, which is clean, still shows the shield.
  - Two probe bugs cost one rollback (both mine, both now in `troubles-wiki.md`): `strings -a -el` finds
    .NET string literals but NOT method names, which live in a UTF-8 heap; and `applied_sql_scripts` is
    in `sys`, not `public`, where the wrong schema yields an empty string rather than a zero. Auto-rollback
    worked and nothing was damaged — but that is the second probe-caused rollback in two releases.

- **🔻 NEXT UP, needs one word from Ham.** The duplicate cleanup is authorised and the backup is taken
  (`~/backups/h1-dupes/` — full dump + all-columns CSVs of every duplicate row). Two things to settle
  before running it, both in `PROGRESS-r3-release.md`: a posted receipt has a **journal entry** behind
  it, so deleting the row alone orphans the JE and deleting both moves the trial balance; and co2's two
  rows are two **genuinely different transactions** (฿3,000 and ฿18,000) that merely collided on a
  number — **renumbering the later one reaches the same end state without losing a posted document**.
  Once the duplicates are gone, WP-4 (the unique indexes) ships as its own release and the class of bug
  is closed structurally, not just behaviourally.

- **🟡 R3 in progress — two fixes committed on main, NOT yet released.**
  - `18f6fcc` **F1** — ภ.พ.36 now surfaces foreign-service payments it would otherwise miss entirely.
    Clearing a foreign vendor's payable with a manual journal entry produced no voucher, so under
    v2.0.0's (correct) payment-tax-point rule that purchase was declared in **no period at all**.
    Detects and requires sign-off; deliberately no blocklist on the AP account, which would have
    recreated the dead end remediated in `cb2e362`.
  - `0381d60` **H4** — attachment download AND delete now authorize against the parent document.
    `sys.attachment.read` is granted to every role, so any user could walk ids and pull files from
    documents they cannot see (403 on the list, 200 on the download). Tier-2 caught two things before
    this shipped: the first cut would have **403'd the company logo, stamp and signature for nearly
    every user** — that route is also the canonical brand-image URL — and it closed only 9 of 12
    parent types, leaving imported bank statements readable by any role.
  - **H1 — duplicate document numbers. Probe run on prod; the bug is live.** co2 Repttown holds two
    POSTED receipts sharing `07-2026-RC-LAB-0001` (฿3,000 and ฿18,000, eight days apart), and **both
    real tenants carry the split-counter condition today** — co2 mints from branches `{0,2}`, co3 from
    `{0,3}`. Branch scopes the sequence but never appears in the printed number, and branch is *who is
    logged in*: web UI = 0, API key / MCP = the real branch. The stop-the-bleeding half (WP-1/2/3/3b)
    is in flight. **The unique index (WP-4) is a separate release** — shipping it while that co2
    duplicate exists would make the release permanently un-deployable, since a failed EF migration is
    not recorded and retries on every boot.
  - Left for the next round, deliberately: the 500 family, conversion routes checking the wrong scope,
    the year-close deadlock, the year=3000 bound.

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

- **✅ Tier-4 live acceptance COMPLETE — every R2 guard verified against production, authenticated.**
  `mark-settled` 404s and left the invoice `Issued` untouched · the ยืนยันชำระครบแล้ว button is gone from
  the rendered page · a non-VAT company gets **422 `pp30.non_vat_blocked`** on the exact ภ.พ.30 PDF call
  the swarm exploited · a Draft payroll run is refused ภ.ง.ด.1, สปส.1-10 **and** the on-screen schedule with
  `payroll.not_posted_for_filing` · year 9999 gives 422 instead of a 500 · and the payroll pay-date trap
  is refused at creation. The Draft run needed for the payroll leg was made on co7 (test playground) and
  deleted after; co7 verified back to its original four POSTED runs. **No real tenant was written to.**
  R2 is verified end to end: tests → CI → Tier-2 → artifact-on-prod → live behaviour.

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

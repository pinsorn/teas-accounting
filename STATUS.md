# STATUS.md — orchestrator live board

## Now
- **🔨 R1 (ledger integrity) IN FLIGHT — 3 of 5 work packages committed, none deployed.**
  Fixing the 6 CRITs from the 17-agent break-it swarm (`VERDICT-breakit-v1271.md`). Plan:
  `PLAN-fix-breakit-v1271.md` · spec: `specs/fix-breakit-r1-ledger-integrity.md`.
  - ✅ **WP-1 `e750780`** — non-VAT invoices now accrue Dr 1130 / Cr 4000 at issue; the receipt
    settles AR instead of re-recognising revenue. Repttown is a live non-VAT tenant whose books
    had NO accounts receivable at all.
  - ✅ **WP-2 `2eb61c3`** — `/admin/nonvat-ar-backfill?mode=preview|apply` corrects the history:
    outstanding pre-fix invoices post Dr AR / Cr retained-earnings (prior FY) or Cr revenue
    (current FY). Preview output is the deliverable for the company's accountant.
  - ✅ **WP-3 `7eaa81a`** — sub-satang amounts rejected at `JournalEntry.MarkPosted`, the seam every
    posting path shares. Reject, never round.
  - 🔄 **WP-4** — expense-claim account-type rule. Round 3; Opus REJECTed twice. **The spec's own
    §3.3 authorized the bug it was meant to close** (amended in `a2e9508`).
  - 🔄 **WP-5** — payroll period + pay-date guard. In flight.
- **🔴 DEPLOY GATE — WP-6, `tools/audit-subsatang.sql`.** R1 must NOT ship until this read-only audit
  runs on prod. WP-3's guard is correct, but on a company that already holds >2dp data it turns silent
  wrongness into a **hard dead-end**: year-end close/reopen, paying an already-posted payroll run, and
  WP-2's own backfill all re-post STORED amounts and would be refused — with advice ("restate in
  satang") that is impossible on immutable history. co5/co7 are known polluted; **Repttown uses all
  four pollution paths and must be assumed polluted until measured.**
- **⏳ WAITING ON HAM — 5 questions** in `specs/doc-lifecycle-cancel-reissue-backdate.md` §6 (new scope
  from 2026-08-06: cancel+reissue posted tax documents, settable doc date, delete the "customer has
  paid" button). Feature C must ship WITH R1 — R1 turns `MarkSettledAsync` from a weak path into an
  active hole (it would mark an accrued invoice settled without crediting AR or debiting cash).
- **⏳ PARALLEL, HUMAN, CLOCK RUNNING — the Repttown tax track.** Amended ภ.ง.ด.50 for the years whose
  revenue was understated. Voluntary filing before an RD summons waives เบี้ยปรับ; เงินเพิ่ม 1.5%/month
  is statutory and accruing now. Research + citations: `specs/research-thai-prior-period-correction.md`.
  Does not wait on R1 — the WP-2 preview supplies the per-year figures.
- **⚠️ teas_test is structurally dirty.** Four unrelated test failures this session traced to years of
  accumulated state (41 poisoned fixture employees; a fresh-year pool that has drifted below 2020; a
  `pk_companies` id collision). Two were fixed at the fixture; **a full reset is the real remedy** and is
  queued before R2. Technique for proving a red test is pre-existing: run it at HEAD in a throwaway
  worktree — now in `troubles-wiki.md`.

- **✅ v1.27.1 LIVE (2026-07-30 ~22:15)** — patch: approve-banner no longer flashes
  "no permission" during permissions load; API restamped so the footer stays truthful.
  First agent-draft→human-post round-trip COMPLETED: draft #186 posted as 07-2026-JV-0060
  via claude-in-chrome (GIF delivered to Ham); GL moved exactly +123.45, Dr=Cr holds.
- **✅ v1.27.0 LIVE + smoked 6/6 (2026-07-30 ~21:00) — agents-draft/humans-post journal loop.**
  MCP `create_manual_journal_draft` (draft-only; `.post` scopes still structurally
  ungrantable, pinned by test) + the missing human half: Post CTA + ?action=approve banner on
  /journals/[id]. PostAsync now runs the full manual-path gates (accounts 3-check +
  period/fiscal — was posting ANYTHING balanced). Scope `gl.journal.create` = opt-in checkbox
  (verified unchecked-by-default on prod). Test-hardening Phase 1 landed: skip 9→8 (real RLS
  test revived), SeedConsistencyTests, FE↔BE scope-parity pin, PaperFoot mirror-fixture test.
  Suite 1073/0/8. Tier-2: opus REJECT→R1-R4 fixed→green.
  - **🎁 WAITING FOR HAM: draft JV #186 (฿123.45, co5) left UNPOSTED on purpose** —
    https://teas.kazaki-rio.com/journals/186?action=approve — click อนุมัติ & Post to complete
    the first agent-draft→human-post round-trip yourself. (Banner "no permission" flash during
    load was found+fixed on main d1264d7, ships next release.)
  - Correcting JV ฿1,000 5200→5500 posted on co7 (07-2026-JV-0008, net profit unchanged) —
    all three "ลุยให้หมด" items closed.
- **✅ v1.26.1 LIVE + Tier-4 VERIFIED 10/10 (2026-07-30 ~14:30) — doc signatures, bottom-anchored
  foot, pagination, default notes + the E2E fix set.** Proven live on co7: signature+stamp+ตำแหน่ง
  render on issued docs; Drafts stay empty; 30-line doc paginates (repeated header, atomic bottom
  group, หน้า x/x); viewer-swap holds (doc's actor signs, never the viewer — verified via new
  viewer01/Auditor); PV 3-box w/ stamp on ผู้อนุมัติ; regressions clean (PV-INTR screen now
  1,000/850; pnd2 pages fine). Review: 2 opus rounds + first tier2-review workflow run (4 confirmed
  findings incl. image-magic validation, all fixed). Suite 1054/0/9.
- Warm workers (2026-07-30 ~14:30): sonnet-signature (full feature context) · sonnet-E2E-tier4
  (co7 browser + PDF-download technique) · others expired/free.
- **PENDING HAM**: correcting JV on co7 (฿1,000 from 5200→5500, JE 07-2026-JV-0006 immutable) ·
  MCP write-side JV tools · PLAN-test-hardening.md Phase 1 go-ahead (acceptance-tester role ฯลฯ).
- **✅ SHIPPED EARLIER (2026-07-29): ภ.ง.ด.2 filing — v1.26.0 live, E2E 10/10.**
  Fixes a live compliance defect: director interest via PV was certified ภ.ง.ด.3 @1% instead of
  ภ.ง.ด.2 @15% (ม.50(2)). Zero damaged certs on prod (income_type_code='4' count = 0, verified).
  Ships: Pnd2 enum + pnd2_income_code snapshot columns, individual-only routing, positive-FormType
  partition (kills a real double-count), INT-IND 15% seed (RLS-safe), /tax-filings/pnd2 + RD
  Format กลาง batch file, Tax Summary ภ.ง.ด.2 column, forced-manual finalize (auto mode would have
  faked "Submitted" — no SubmitPnd2Async exists). 2 opus REJECT→fix rounds then APPROVE; full
  suite 1028/0/9. Deploy steps + probes: PROGRESS-pnd2-filing.md.
  - Post-deploy E2E pending on Chrome: pay real director interest on co7 → 50ทวิ shows ภ.ง.ด.2
    15% → download batch file; probe seed 632 row counts as superuser.
- **📐 SPEC READY (awaiting Fable review + Ham-approved mockup locked): doc signature stamping +
  foot layout** — `specs/doc-signature-and-foot-layout.md` (opus, 5 requirements: bottom-anchored
  notes+totals+sign group, page-2 spill w/ repeated header + หน้า x/y, per-user signature image +
  ตำแหน่ง + company stamp via attachments pipeline, per-doctype layout audit, per-doctype default
  notes jsonb in company_profile). NOT parallel-safe with pnd2 files (i18n/queries.ts). Implement
  AFTER pnd2 deploy closes.
- **✅ v1.25.0 LIVE (2026-07-29) — manual journal vouchers + chart-of-accounts management.**
  Ham asked how to record a director/shareholder loan and non-sales income; the system could do
  neither. Rather than a bespoke director-loan feature, this shipped the general capability.
  New: `/journals`, `/journals/new`, `/settings/chart-of-accounts`, and accounts **2190
  เงินกู้ยืมจากกรรมการ/ผู้ถือหุ้น · 5500 ดอกเบี้ยจ่าย · 4300 รายได้อื่น** seeded into every company.
  - **Proven live on prod (co7), the numbers that matter:** posting
    `Dr 1120 100,000.00 / Cr 2190 100,000.00` left the trial balance footing (฿215,953.07), moved bank
    by exactly +100,000.00 and 2190 by exactly +100,000.00 credit, and **left net profit unchanged at
    -฿115,953.07** — a loan is a liability, never income. The contrast case was posted deliberately:
    non-sales income of ฿5,000 moved net profit by exactly +5,000.00, which is what proves the
    "unchanged" result was real and not a stale report. 2190 shows on the Balance Sheet as a liability.
  - Also verified live: the float split entry (33.33/33.33/33.34 vs 100.00) balances with Save enabled,
    unbalanced is refused, the account picker excludes header and inactive accounts (checked by
    deactivating one and watching it vanish), future dates are refused, and a posted JV has no edit or
    delete affordance. Evidence: `swarm-findings/v1241/legF-jv-prod.md`.
  - Gates before release: Api suite **1012/0/9**, Tier-2 opus review APPROVE after 4 findings fixed,
    local click-through, deploy probes confirmed 2190/5500/4300 seeded for **5 of 5 companies**.
  - **Deferred deliberately** (phase 2): a dedicated director-loan screen, **ภ.ง.ด.2** filing (needed
    before interest can be paid to a director — 15% WHT under ม.50(2); the 50ทวิ side already supports
    it, the monthly return does not exist), share capital, and reversing-entry automation. The
    correction path for a posted journal is a manually entered reversing JV.
  - Tax note for whoever uses this: a director loan needs a written agreement and a market-rate
    interest charge — an interest-free loan lets the Revenue Department impute interest (ม.65 ทวิ (4)).
- **✅ CLOSED OUT (2026-07-29): prod = v1.24.2, all four features verified live, nothing outstanding.**
  O2b's fix is deployed and the blocked case now works: on co5, two tax invoices linked with the grid
  left empty generated two Thai lines and tied out exactly — **subtotal 6,500.00 · VAT 455.00 · total
  6,955.00**, which is the very figure the original bug reported as unbillable. All three regressions
  hold (override keeps the manual line; an empty grid with no invoice is still refused; a half-filled
  row is still caught, not silently dropped). Doc `07-2026-IV-0007`.
  co7's employee names were repaired through the UI and now render correctly in the SSO schedule and
  on the ใบแนบ ภ.ง.ด.1. Evidence: `swarm-findings/v1241/legD-o2b-reverify-and-co7-names.md`.
  - **Known and correct, worth remembering:** a payslip snapshots the employee name at posting time,
    so runs posted BEFORE the repair still print the old `???????` in the payslip summary. The SSO
    schedule reads the live employee master and shows the corrected names. Not a bug — but it means
    historical payroll runs cannot be retro-fixed by editing the employee.
  - **Small leftover:** co7's คำนำหน้า (title) field is still corrupted the same way; only ชื่อ/สกุล
    were repaired, since that is what the dispatch authorised. It shows on สปส.1-10.
  - Still unverified by a human: the SSO schedule's print preview (native print dialogs freeze browser
    automation) and narrow-viewport rendering.
- ~~⚠️ FOR HAM ON WAKING (2026-07-29): prod v1.24.1 was verified live on all three test companies.
  Two features pass outright; ONE is broken on prod and the fix is committed but NOT deployed.~~
  Full evidence in `swarm-findings/v1241/`, state in `PROGRESS-v1241-live-verify.md`.
  - ✅ **O14 (co6)** — the ledger-safety invariant HOLDS: reopening a month inside a closed fiscal
    year is refused `422 period.year_closed`. **co6, frozen until 2027, is usable again** — PV
    `07-2026-PV-MISC-0001` posted, Trial Balance Dr = Cr ฿17,640.00.
  - ✅ **O10 + O11-alt (co7)** — JE `08-2026-JV-0001` Dr 121,750.00 = Cr 121,750.00 with `Cr 2180`
    = ฿500.00 exactly; the cap refuses an over-large deduction; **ภ.ง.ด.1 still shows the
    pre-deduction figures**, so a net-pay deduction never reaches a tax filing. SSO schedule totals
    tie to ส่วนที่ 1 on two runs; the prorated joiner shows ฿32,903.23, not ฿60,000.
  - 🔴 **O2b (co5) — was unreachable on prod.** With the grid empty and invoices linked, the form
    refused client-side and no request reached the backend. Cause: the shared `LineItemsTable` always
    keeps one undeletable blank row, so the `lines.length === 0` relaxation could never fire and zod
    rejected the blank row before the submit handler ran. **Fixed in `c9e7f8a`** (blank rows stripped
    by `z.preprocess`, contained to `BillingNoteForm`), verified by driving the app locally through
    all three cases. **NOT DEPLOYED — prod still carries the broken form.** Deploying needs a new
    release tag; it is FE-only, no schema, so no DB backup is required for it.
  - 🟡 co7's employee names are corrupted Thai (`????`, one byte per character) from an old
    PowerShell-driven API write — not an app defect, but co7 cannot verify name rendering on RD/SSO
    forms. Left unrepaired (prod data write). Use co6, which holds correct Thai.
  - Not verified: the SSO schedule's print preview (a native print dialog freezes browser automation)
    and narrow-viewport rendering. Both want a human eyeball.
- **v1.24.1 IS LIVE ON PROD (2026-07-28 ~20:1x).** Everything from the 2026-07-26 backlog shipped:
  O10 payroll deductions, O14 monthly period reopen, O2b billing-note line generation, O11-alt
  on-screen สปส.1-10 ส่วนที่ 2, plus O4/O2a/G5 which had been sitting undeployed since v1.23.0.
  Gate before release: **983 passed / 0 failed / 9 skipped**; tsc + next build clean.
  - Verified through the PUBLIC topology, not just localhost: `https://teas.kazaki-rio.com/login` 200,
    `/payroll` and `/period-close` 307 (routes exist), and a **POST to the login API through the public
    host returns 401** — so https → nginx-proxy-manager → Next → API round-trips on the new build.
    Running binary reports `1.24.1.0`. Post-deploy probes: EF migration applied, the
    `payroll.payslips.other_deductions_reason` column exists, SqlScripts 630 recorded, and account
    **2180 seeded for 5 of 5 companies**; the three new routes answer 401, not 404.
  - **v1.24.0 failed first and auto-rolled back cleanly** (no stuck downtime): SqlScripts/630 seeded
    2180 with a bare cross-company `INSERT … FROM master.companies CROSS JOIN …`, which dies
    `42501: new row violates row-level security policy` under prod's NOBYPASSRLS `teas` role. The
    test suite cannot catch it — teas_test connects as a superuser and bypasses RLS. Fixed in
    `48a220d` by mirroring `621_seed_fixed_asset_accounts.sql` (DO block pinning `app.company_id`
    per company). **The spec caused this**: it told the implementer to copy `482`, which predates the
    RLS lockdown. 482 is no longer a valid template for a G1 tenant table — 621 is.
  - Backups taken before both attempts (`~/backups/teas-pre-v1.24.*` and
    `backups/teas-20260728-192313.dump`). The failed binary is kept as `api/unpacked.broken-v1240`.
- **SESSION END 2026-07-26 — main `505743e`, tree clean, nothing mid-flight.
  Read `HANDOFF-next-session.md` first.** Shipped today: **O10** (`e62102f` + `93d5ee4`),
  **O14** monthly period reopen (`d6cce40`), **O2b** billing-note line generation (`1706d72`),
  plus the O11 template finding (`4d71841`). Last gate: **979 passed / 0 failed / 9 skipped**.
  Left: **O11-alt** (spec ready, not started), **O11** (blocked below), and a **deploy of the 5
  commits sitting after tag `v1.23.0`** — that release carries SqlScripts seed 630 AND EF migration
  `20260726060403`, so **a prod DB backup is mandatory**.
  Paused at 7-day quota 94%; resets ~2026-07-28 16:00 GMT+7.
- **⛔ O11 BLOCKED — needs a file from Ham (2026-07-26, commit `4d71841`).** `sps110_main.pdf` does not
  contain ส่วนที่ 2 at all. Measured page titles: p1 = `สปส.1-10 ส่วนที่ 1` (summary only, no employee
  rows), p2 = คำชี้แจง, **p3/p4 = `สปส.1-10/1`, a different form** (branch-consolidated return + its
  continuation) whose rows are per-BRANCH. **Ham: drop the official ส่วนที่ 2 PDF into
  `backend/src/Accounting.Infrastructure/Pdf/Templates/`** and O11 resumes as specced.
  Salvaged: coordinate mapping solved (`yTop_json = 595.3 − Top_dump`, `x_json = Left_dump`, A4
  landscape, verified against `wageMonth`), and `TaxFormFillDiagnostic.Dump_sps110_positioned_words`
  (`TEAS_DIAG=1`) dumps any template's word positions — point it at the new file.
  Note: "page 2 of the PDF" ≠ "ส่วนที่ 2 of the form", and the army leg's "10 blank rows" was the
  branch table, not an employee schedule.
- **DONE (2026-07-26 ~12:5x): O10-A payroll deductions BACKEND — gate green 968/0/8, committed.**
  Codex implemented, AGY cross-family reviewed (independently re-derived the Dr=Cr identity and found
  2 real guard defects), Fable read every line and ran both full suites.
  - Shipped: account **2180** wired + `Cr 2180` conditional journal line, draft-only deduction API
    (`PUT /payroll/runs/{id}/deductions`), cap + unknown-employee guards at BOTH the DTO validator and
    the service, 2180 seeded on both CoA paths, +5 tests.
  - Also fixed a **pre-existing suite flake** (not caused by this work): `FreshJeYearAsync` called a
    year "fresh" using journal entries alone, so a `CitYearSummary.OverrideNetProfit` or `CitAdjustment`
    from another CIT test leaked in and failed 2 Pnd50 tests that pass in isolation. Now all three are
    checked. See `troubles-wiki.md`.
  - ~~Still open: the deduction REASON is not persisted~~ → **closed by O10-B.**
- **DONE (2026-07-26 ~15:5x): O10-B — reason column + FE + payslip PDF. O10 IS NOW COMPLETE.**
  `Payslip.OtherDeductionsReason` (nullable, 500) + migration `20260726060403_PayslipOtherDeductionsReason`
  (one `AddColumn`, no drift — Fable read it), written/cleared atomically with the amount, carried on the
  payslip DTO, printed on the payslip PDF as `หัก  รายการหักอื่น ๆ (<reason>)`, and editable per employee
  on a DRAFT run at `payroll/[id]` behind `payroll.run.manage` (read-only once Approved/Posted).
  - Gates: full Api suite **968/0/8**, backend build 0/0, `tsc` 0, `next build` ok.
  - Fable caught two FE defects at diff review and had them fixed: the change was casting around
    `PayslipDto` instead of adding the field to it, and it hardcoded Thai in a page that uses next-intl
    (would have broken the EN locale — same class of miss as G5 last round).
  - Two suite failures on the way were proven NOT to be this diff: a Thai assertion that fails because
    `PdfText` drops combining marks (`อื่น` → `อื น`), and a random-id `pk_companies` collision that
    passes standalone. Both now in `troubles-wiki.md`.
  - **Codex runtime was unreliable all afternoon** — 3 jobs died mid-run and 2 more never launched
    because a dead job stayed flagged `running` in the plugin's state file (a stale PID entry that
    `codex cancel` does not clear; repaired by hand). Poll loops must check log mtime AND the PID, not
    just the tracker's status.
  - **Release note: this ships a new SqlScripts seed (630) → prod DB backup is mandatory.**
  - Next: **O10-B** (FE column + payslip PDF + reason column), then **O11** — whose spec now carries a
    new blocking **D0**: the สปส.1-10 template is a FLAT pdf with no AcroForm widgets, so page-2 box
    coordinates cannot be read programmatically and must be measured from a render by a vision worker
    (AGY or Fable) before any implementer starts. Then O2b (needs a spec), then O14.
- ~~IN FLIGHT (2026-07-26 ~11:45): O10-A payroll deductions backend → Codex.~~
  Spec `specs/payroll-deductions-o10.md` corrected by Fable first: counterpart account pinned to
  **2180** (`เงินหักจากพนักงานค้างนำส่ง`, LIABILITY/CR), **both** CoA seeding paths required
  (new `630_seed_payroll_other_deductions_account.sql` + `MasterDataServices.DefaultChartOfAccounts`
  — missing the 2nd = every newly-created company 500s on payroll post), Dr=Cr invariant written as an
  invariant not as field values, Ham's tax answer recorded (deduction hits NET only; fix an overstated
  prior month by amending THAT month's ภ.ง.ด.1). O10-B (FE column + payslip PDF line) is a later dispatch.
  Routed to Codex because the Claude 7-day quota is at 90% (≥85% = no new Claude workers).
  **This release ships a new SqlScripts seed → prod DB backup is mandatory.**
  Next after O10: **O11** (`specs/sps110-part2-o11.md`), then O2b (needs a spec), then O14.
- **HANDOFF: อ่าน `HANDOFF-next-session.md` ก่อน** (เขียน 2026-07-26 ~11:5x ตอน context เต็ม) —
  (เก่า) 13/14 ข้อจาก army ปิดแล้ว · prod = **v1.23.0** · เหลือ O10 + O11 ที่มีสเปกพร้อม implement
  (`specs/payroll-deductions-o10.md`, `specs/sps110-part2-o11.md`) + O2b ที่ Ham ตอบแล้วแต่ยังไม่มีสเปก
  · O14 (reopen งวดรายเดือน) สเปกพร้อมแต่ยังไม่จัดลำดับ
- **ARMY COMPLETE — all 11 never-tested areas driven live, VAT + non-VAT + vision.
  Verdict: `swarm-findings/army/VERDICT-army-2026-07-25.md`** (read that first; per-leg reports +
  screenshots in the same folder). 10 legs + 2 vision waves + a post-deploy verify leg.
  - **v1.22.11 LIVE** (2026-07-25 ~17:3x) — 5 work packages shipped: the CRITICAL foreign-vendor
    ภ.ง.ด.54 chain (VI-linked PV never booked the self-withhold gross-up → always 422), the stuck-PV
    escape hatch + WHT-type validation, an inert PV concurrency token (race could void a POSTED
    voucher), the real-statement K-Plus PDF 500, super-admin company-update 500, plus FE nits.
    Leg V1 re-verified 10/11 items live on prod (PV #17 posts, JE balanced, ภ.ง.ด.54 = ฿3,529.41,
    PV #19 unstuck). Pre-deploy DB backup taken; applied_sql_scripts 75 → 75.
  - **co6 = บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด** now exists (id=6, non-VAT) with 3 scoped users
    (nvadmin01/nvchief01/nvtax01). Its FY2026 is **year-end CLOSED** (B2-ye's terminal state) and the
    12 monthly locks CANNOT be undone (O14 — only reopen-YEAR exists), so co6 accepts no new
    PaymentVoucher until 2027; use **co7** for non-VAT work. co5 stays the VAT playground.
  - **v1.22.12 LIVE** (2026-07-25 ~20:0x) — WP-F (PV prefill dual-flag), WP-G (non-VAT company gate
    on the PV path; Tier-2 rejected round 1 because zeroing the VAT under-paid the vendor and
    stranded AP — the bad acceptance criterion was Fable's own spec error), WP-H (RD/SSO filing PDFs
    on `Payroll.RunManage OR tax.filing.preview` — TAX_OFFICER was 403'd off its own ภ.ง.ด.1/1ก).
    Leg V2 verified 3/4 live; leg **V3b closed the 4th on a fresh co7: JE #173 = Dr 5200 1,070.00,
    no 1170 line, TotalPaid 1,070.00, Dr=Cr, TB balanced** — the vendor's VAT folds into cost and the
    vendor is paid in full. Fix arc 8/8 work packages shipped; nothing engineering-side is open.
  - **Ham-facing decision doc: `DECISIONS-army-2026-07-25.md`** (Thai, 15 items grouped: 5 unbuilt /
    7 "do we want it" / 2 go-look-yourself, plus the shipped-bug ledger). Top one:
    **O8 — payroll has no day-based proration**; a mid-month hire and a mid-month leaver both got a
    full month of salary + PIT, in the GL and in the printed ภ.ง.ด.1/1ก. Also O11/O12: สปส.1-10
    prints but is not submittable (ส่วนที่ 2 unbuilt, no employer account-number field). And **O14: no
    monthly period-reopen exists anywhere** (only reopen-year) — closing a month is irreversible, which
    is why co6 can create no PaymentVoucher until 2027; **co7** (id=7, non-VAT, periods open) is the
    replacement non-VAT playground, users nvadmin02/nvchief02.
- **v1.22.10 LIVE (2026-07-22 ~02:5x) — non-VAT F-A..F-D shipped+deployed; NEXT SESSION = army
  on untested areas, see HANDOFF-untested-army.md.** ExpenseClaim non-VAT 1170 guard (money,
  Fable-reviewed) + PO paper vendor address + control heights + VI non-VAT wording. Suite 921/0/8.
  API scripts unchanged 75, FE full, public probes 200. co5 address filled (.txt export unblocked).
- **ROUND-5 SWARM VERDICT: ALL FINDING FIXES CONFIRMED CLOSED on v1.22.9 (2026-07-22 ~00:5x).**
  10/10 agents: WP1 17/17 deny-pages clean; WP2+WP6 15 modules render real data for AUDITOR, BU-403
  spam = 0; WP3 4/4 report-UX items live; WP4 widget 200 4/4; WP5 api-keys #418 gone + users
  self-guard + VI-clobber fixed + Thai toast. CRIT regression: sales 27/27, ar 12/12, purch+appr
  approves all 2xx, TB Dr=Cr 9/9, tenant/SoD clean, zero 500/23505 everywhere. Residual LOW nits
  (file-response EN toast, statement-imports 403-vs-empty, 2 stray 403s, peer-admin guard untested
  no-data) logged in swarm-findings/round5/ for a future batch. Goal COMPLETE.
  - CRIT-1/CRIT-2: CLOSED + verified live (v1.22.7 on prod; round-4 10-agent swarm = zero
    500/23505 across every numbering path, TB Dr=Cr held, tenant/SoD clean).
  - Finding fixes committed to main, awaiting a v1.22.8 deploy:
    - WP1 `5c49234` — FE route-guard on 16 /new routes + CN/DN buttons + tax-filing/period-close/
      attachment (clean deny, backend still 403s).
    - WP2/WP4 `5c49234` — 628 seed grants AUDITOR module reads + APPROVER pending-approvals read
      (suite 912/0/8).
    - WP3/WP5 `043935c` — report date-basis labels + AP-aging tie banner + AR negatives + bank-recon
      badge; api-keys deny+#418, users self/peer-admin guard, EN-toast i18n, VI-new category clobber.
  - **RESUME (do NOT auto-start — Ham paused):** (1) WP6 read/manage split (BU/quotation/SO/DO/vendor
    — footgun auth, Opus review; Claude 7-day pool was 95%, consider Codex/wait). (2) deploy v1.22.8
    (API has 628 SqlScript → scripts +1, DB backup; FE full) + prod verify. (3) big Sonnet swarm
    round 5 to confirm every finding closed — needs Claude budget, gate on 7-day recovery.
  - Spec: specs/fix-swarm-findings-all.md (WP1/2/3/5 [x], WP6 open). Swarm evidence: swarm-findings/.
- **(prev) CRIT-1 REFIX — v1.22.7 LIVE + PROD-VERIFIED (2026-07-20 ~05:xx).** v1.22.6 fixed only the
  no-ambient-tx path (PO approve); round-3 swarm caught TI post still 500 3/3. opus-debugger found
  the REAL bug: off-by-one retry cap (`attempt < MaxAttempts` left the final-attempt doc_no collision
  uncaught → raw 500; in an ambient tx the escape rolls the seq bump back so it never climbs). Fix:
  catch every attempt + explicit savepoint after allocate/before SaveChanges + cap 5→50; tests now
  drive the REAL TaxInvoice/Receipt PostAsync (drift=8 + parallel) RED→GREEN. Fable diff-reviewed the
  one file; af5ab8a → CI green → v1.22.7 API deploy (scripts unchanged 73) → **real TI post on prod
  co5 = TI-0004 Posted** (the closing check that was skipped for v1.22.6). CRIT-2 already verified
  (round-3 ภ.พ.30 preview/PDF 200). NEXT (Ham /goal, after quota reset): swarm round 4 (concurrency
  proof) → fix ALL remaining findings (specs/fix-swarm-findings-all.md WP1-5) → swarm round 5.
- **(prev) SWARM CRIT-1/CRIT-2 FIX SHIPPED — v1.22.6 LIVE (2026-07-19 ~23:5x).** 10-role concurrent
  swarm on co5 exposed doc-number sequence drift (post/approve 500 → 23505 on *_doc_no) +
  TAX_OFFICER missing tax.filing grant. Root causes CONFIRMED from prod pm2 log + drift query.
  Fix: 626 reconcile SQL (GREATEST-only, per-company FORCE RLS) + retry-guard helper wired to
  14 alloc services + 627 tax grant. Sonnet impl → Opus Tier-2 APPROVE (5 lenses) → Fable
  reviewed 626/627/helper/GL/Receipt money paths personally → 3531052 → CI green → v1.22.6 API
  deploy: scripts 71→73 (+2), **seq drift co5 JV delta=0 (reconcile healed it)**, ภ.พ.30 alive,
  public probes 200. Suite 1053/1/8 (1 = documented Pnd50 flaky). Worker also fixed 2 rollout
  footguns (Receipt CashReceived vs 570-freeze ordering; ExpenseClaim first-attempt guard).
  NEXT: re-swarm round 3 to PROVE CRIT closed under concurrency (Ham /goal).
- **(prev) USAGE-DRIVE FIX ROUND SHIPPED — v1.22.5 LIVE + VERIFIED (2026-07-19 ~16:4x,
  Ham "แก้สิ เราต้องการ Webapp สมบูรณ์").** Sonnet impl per
  specs/fix-usage-drive-findings.md → Fable diff read → d04b290 → CI green →
  PR #91 → v1.22.5 → full deploy (API DEPLOY_OK 7/7 probes incl. new
  ar_aging_auth, DB backup, scripts 71 unchanged; FE_DEPLOY_OK no-sudo;
  public probes 200/200/401). **F-1 VERIFIED LIVE**: AR aging shows C001
  −1,070 row, table total 4,280 = control banner. **F-3 VERIFIED LIVE**:
  QT→TI prefill carries ชิ้น/งาน. F-2 = not a code bug (invalidation already
  present pre-v1.22.3; prod sighting likely stale edge-cached bundle).
  F-4 = documented CDP screenshot tooling artifact, payroll code clean.
  Worker flagged: SO has NO prefill path to TI/DO at all (pre-existing gap,
  out of scope) — backlog candidate.
- **(prev) CO5 USAGE DRIVE COMPLETE (2026-07-19 ~15:1x, Ham "ลองใช้งานซื้อ/ขาย/Payroll/
  รายงาน").** Fable drove prod v1.22.3 live via Chrome on co5: purchase
  PO-0002→VI-0003→PV-COGS-0002, sales QT-0002→TI-0003→RC-0002 (direct-TI
  shortcut), payroll 09/2026 full cycle (create→approve→post→pay KBANK) +
  08/2026 PIT 7,008.33 = hand-calc exact. Reports: ภ.พ.30 July
  910/1,050/เครดิตยกไป 140 ✓, TB Dr=Cr + **RE-TEST (c) confirmed (new COGS
  VI → 5000)**, AR aging tie ✓. Dashboard "VAT 70 ขอคืนได้" sign-bug
  hypothesis REFUTED (was genuinely a credit). 4 minor findings logged in
  PROGRESS-vat-usage-drive.md (top: AR-aging table total 5,350 vs control
  4,280 — net-credit customer C001 hidden from rows). No code changes.
- **(prev) RESIDUAL NITS SHIPPED — v1.22.3 + v1.22.4 LIVE, ALL VERIFIED (2026-07-19
  ~12:2x, Ham "แก้เลย").** N-1 (CN/DN list shows TI doc-no via server JOIN) +
  N-2 (draft-only delete, 899/8/0 suite, RBAC wiring fixed) → v1.22.3 full
  deploy; verify pass found delete-toast raw key `common.deleted` (latent on
  Quotation too) → 2-line i18n fix → v1.22.4 FE-only deploy (API stays
  1.22.3 by design) → toast "ลบแล้ว" verified; test drafts cleaned, co5 =
  CN-0001 Posted only. New wiki: never sudo the FE deploy script.
- **S13 CLOSED-FOR-NOW (2026-07-19 ~13:0x, CF dashboard checked via Ham's
  login).** Bot Fight Mode OFF; Events(host=teas, 24h) = 7 scanner blocks
  (US IPs, Managed rules) and ZERO events from our heavy automation traffic
  → H1 bot-scoring REFUTED; WAF Skip rule = no-op, NOT applied. Leading
  hypothesis by elimination: CF edge↔origin connection race (intermittent;
  no recurrence since 07-18). Recurrence playbook appended to
  specs/fix-s13-cf-edge-503.md (check CF Events SAME-DAY — 24h retention).
- **(prev) F-11/F-12 FIX ARC SHIPPED — v1.22.2 LIVE (2026-07-19 ~02:3x).** Ham
  "เอาตามที่แนะนำเลย" → spec → sonnet impl (8 files) → Fable diff read →
  3fc7619 → CI green → PR #88 → tag → deployed (MinVer 1.22.2, DB backup,
  DEPLOY_OK scripts-unchanged 71, FE_DEPLOY_OK content anchors, public
  probes). **F-11 VERIFIED LIVE** on CN-0001 (Thai reason label on legal
  doc). F-12 live via content anchors; visual eyeball optional (TEAS session
  expired post-deploy). Remaining: S13 CF-dashboard check + WAF Skip rule =
  **waiting on Ham** (CF login in different Chrome profile than extension).
- **(prev) OPEN-ITEMS ROUND COMPLETE (2026-07-19 ~01:3x) — 4/4 tracks done.**
  (1) E3 create_vendor FIXED ac048e8 — stale test fixture (taxId null vs WP1
  domestic-VAT validation 65b9b2b); suite 897/8/0; CI-green confirmation
  pending (watch bg). (2) S13 CF-edge 503 ROOT-CAUSED db29473 — origin fully
  ruled out; top hypothesis CF Bot Fight Mode blocking the automation browser;
  **NEEDS HAM: CF dashboard checklist** (specs/fix-s13-cf-edge-503.md, 8 items;
  proposed scoped WAF Skip rule drafted, not applied). (3) statement-import +
  CN→ภ.พ.30 on co5 PASS af89e0d — KBiz CSV import→match RC/PV exact→recon
  ผลต่าง 0.00; CN-0001 → ภ.พ.30 ขาย 770/เครดิตยกไป 70, TB 172,440 tie; new
  F-11 (CN reason raw enum key on UI+printed doc) + F-12 (low UX batch).
  (4) manual บท 6 posted-state 99f3a8f — Post-JE/Pay-JE/opening-YTD/dup-guard,
  facts code-cited, mkdocs clean (screenshots deferred 🚧). Next candidates:
  F-11/F-12 fix batch, TI PDF + สปส.1-10 PDF visual re-checks.
- **(prev) VAT DUMMY ROUND COMPLETE — FIX LOOP CLEAN (2026-07-19 ~00:2x).** v1.22.1 live; all
  F-findings fixed + VERIFIED-LIVE on co5 (F-3 opening-YTD 7,008.33 exact, F-6 Pay JE
  TB-exact, F-9 COGS→5000, F-8 PND1 column, F-4/7/10 UI-i18n). Re-test found ZERO new
  issues. Open items: E3 create_vendor CI-red (pre-existing), S13 CF-edge 503s,
  statement-import flow + CN + manual posted-state (PROGRESS §Next).
- **(prev) VAT DUMMY ROUND — MAIN ARC (2026-07-18 ~14:2x).** F-1 CRITICAL (company
  creation broken since RLS-600: CreateAsync missed by Family-B inventory, 42501 +
  no tx → half-created tenant) → fixed 4b92efd (tx + LOCAL company_id pin, red→green
  RLS test, Opus APPROVE) → **v1.21.6 deployed** (DEPLOY_OK 10/10, backup, scripts 69)
  → orphan co4 deleted → dummy co5 seeded FULL via UI (1/25/12/15/19/11). Then the
  never-run-before paths, all on co5: **payroll Post #1 in history** (07-2026-PR-0001,
  JE tie exact 127,500, docs ภ.ง.ด.1/สปส.1-10 txt+PDF/สลิป/50ทวิ/ภ.ง.ด.1ก, Pay,
  month-2 continuity, dup-guard 422) + **full VAT sales chain** QT→SO→DO→IV→TI→RC
  (7,490 every hop) + TI-0002 unpaid + **purchase chain** PO→VI→PV (10,700) +
  **ภ.พ.30 real data exact** (ขาย 840 / ซื้อ 700 / สุทธิ 140) + sales-summary/
  tax-summary/AR aging (1130 tie ✓)/TB (169,230 Dr=Cr)/bank-recon (diff 0.00) —
  ALL money math ties to hand-calc. Findings F-1..F-10 in REPORT-vat-dummy-test.md;
  **awaiting Ham: F-3** (PIT onboarding-year under-withholding 1,408 vs 6,075 —
  recommend opening-YTD/ยอดยกมา feature) + **F-6** (Pay = status-only, no settlement
  JE, 2170 never clears). Batchable: F-4/F-7/F-8 (tax-summary ภ.ง.ด.1 col blank)/
  F-9 (COGS→5200, no COGS acct in default CoA)/F-10. Then fix→retest loop per Ham.
- **PAYROLL+REPORTS GOAL COMPLETE (2026-07-17 ~11:3x).** Full arc done: UX test (20
  findings) → Ham "แก้ทั้งหมดเลย แก้หมดแล้วค่อยทำ manual" → fix round W1 7bb293d (error
  infra: blank-toast fix in openPdf/downloadFile, global-error boundaries w/ chunk
  auto-retry, employees modal stale-seed + silent-fail fixes, i18n common.yes/no +
  report.total, P&L dev-note removal, BE hint, destructive confirm) + W2 ce9aba1
  (zero-salary badge/banner, payslip breakdown modal + ม.50(1) explainer, CE period
  hint) + W3 c71c13b (BE dates on statements, TB/BS/P&L CSV + financial-statements PDF
  wiring, shared FE csvCell w/ OWASP formula guard ×5 exports, month/year presets +
  defaults, GL picker code-prefix resolve, bank-recon empty link, sales-summary basis
  footnote — basis confirmed posted TaxInvoices only) → **Manual ใหม่ บท 6 เงินเดือน +
  บท 8 รายงาน** 3ee4433 (hand-authored, facts verified against code; mkdocs nav wired).
  All gates green every WP (tsc, next build, ম grep); Fable read every diff.
  **v1.21.5 DEPLOYED + VERIFIED LIVE (2026-07-18, Ham "ทำเลย ๆ"):** release PR #84
  admin-merged → tag @ 2d786c8 → built from tag worktree (MinVer 1.21.5 ✓) → API
  `DEPLOY_OK` (probes 10/10 incl. new financial-statements route probe; sql_scripts 69
  unchanged; DB backup) + `FE_DEPLOY_OK` (content-check anchors P1/R1/P6/W3) → public
  E2E green → **Chrome smoke test 7/7 PASS** (common.no→ไม่, TB/BS/P&L exports, P&L
  presets+default, GL code-resolve, payroll BE hint + breakdown modal + destructive
  confirm, sales-summary footnote, outstanding-po header). Scripts archived
  publish/v1.21.5/ (ab056f8). **OPEN:** (1) R10 picker/modal first-click intermittent
  (investigate-only); (2) S13 CF log pull — Ham (evidence §Infra, two windows now);
  (3) payroll POSTED-state e2e ยังไม่เคยรัน (ต้อง test company แยก).
- **PAYROLL + REPORTS UX TEST COMPLETE (2026-07-16 ~23:1x, Chrome on prod, Repttown).**
  Goal (Ham /goal): เงินเดือน + รายงาน ละเอียด, UX-first → Finding report → Manual ถ้าไม่มีปัญหา.
  Payroll draft lifecycle tested end-to-end (create/calc/dup-guard/delete — NEVER posted, zero
  GL impact); all 14 report pages tested, cross-report numbers tie everywhere checked.
  **20 findings** → `REPORT-payroll-reports-uxtest.md`; fix spec
  `specs/fix-payroll-reports-findings-2026-07-16.md` **PENDING HAM APPROVAL**. Highlights:
  ChunkLoadError white-screens (2×, CF 503 on _next chunks, no error boundary); blank error
  toasts app-wide (openPdf statusText on HTTP/2); employee edit modal stale-cache seed =
  silent data-revert risk; i18n leaks (common.yes/no, report.total); P&L dev note on prod.
  **S13 SMOKING GUN CAPTURED**: PUT employees/2 → origin 204 / browser 503; origin log has
  ZERO 503s all day → all 5xx generated at CF edge (evidence chain in report §Infra, window
  22:10–22:40 ICT for Ham's CF log pull). **Manual ใหม่ DEFERRED** — เงื่อนไข "ไม่มีปัญหา"
  ไม่ผ่าน; ทำหลัง fix round. Cleanup: no payroll runs left; BUTEST-EMP salary now ฿30,000
  (test fixture, harmless).
- **v1.21.4 DEPLOYED TO PROD (2026-07-16 ~21:37, Ham-approved "deploy เลยลุย").** Sales
  fix round LIVE — 83e47f9 (WP-A backend), e71f3e3 (WP-B FE flow), 996d91a (WP-C polish),
  all 16 S1-S16 findings. release-please PR #83 admin-merged (checks never fire on that
  branch, per wiki) → tag v1.21.4 @ f09cfcf. Built from a fresh worktree
  `Z:\temp\claude\wt-teas-v1214-build` (real path, not subst — MinVer confirmed `1.21.4`
  in Accounting.Api.deps.json before shipping). Code-only release: no EF migration, no
  new SqlScript — `applied_sql_scripts` stayed 69. API `DEPLOY_OK` (DB backup
  `teas-pre-v1.21.4-deploy-*.sql.gz` 281820B; all probes PASS incl. new S15-B probe
  `sales_order_put_route_exists=401` for the new `PUT /sales-orders/{id}` endpoint —
  unauthenticated, route confirmed present without touching any real doc). FE
  `FE_DEPLOY_OK` (content-check on S13a proxy-timeout anchor). Public E2E through
  teas.kazaki-rio.com GREEN: login 200, /mcp 401, /.well-known 200, PUT
  /api/proxy/sales-orders/999999999 401 through the full CDN→NPM→app path. S13b
  (quotation-send double-call safety) verified by the 17 new backend tests
  (SalesUxFixesWpATests) pre-deploy, no live probe per plan. Scripts + DEPLOY-README
  archived + **committed** to publish/v1.21.4/ (aef6b91 — first time a deploy archive
  was actually git-tracked; publish/ is gitignored, prior v1.21.0-v1.21.3 archives were
  left untracked on disk despite STATUS.md claiming otherwise — fixed via `git add -f`).
  **PROD SPOT-CHECK PASSED (2026-07-16 ~21:5x, fresh Ham login, Fable via Chrome):**
  footer v1.21.4 ✓; BU columns show names on /quotations + /sales-orders +
  /delivery-orders incl. via SPA-nav (R8-lesson repro method) ✓; BU + status filters
  populated/Thai (S3/S4) ✓; QT detail BU badge (S10) ✓; S11 confirm dialog live on
  QT #8 send (warning ออกเลขทันที + totals; cancelled, no number consumed) ✓; S1 nav
  no longer flashes empty section headers ✓. Sales fix round CLOSED end-to-end.
  Remaining for Ham (unchanged): CF 5xx log pull (S13), LE cert rate-limit cleanup,
  prod test-doc cleanup (QT #5/#8 drafts), MCP connector re-auth.
- **PURCHASE-SIDE REGRESSION CHECK on v1.21.4 PASSED (2026-07-16 ~22:1x, Chrome, BU
  TEST):** /purchase-orders list BU names + Thai status filter ✓; PO #4 /edit docDate
  still locked w/ R2-B hint + new BE hint on วันที่คาดว่าจะส่ง ✓; PO detail BU badge +
  CTAs + Thai activity ✓; VI-from-PO form INTACT — อัตรา VAT percent field + chips
  still present on non-VAT co (LineItemsTable vatMode change did NOT hide purchase VAT
  fields), PO prefill + ล็อกวันนี้ + งวด ม.82/4 all ✓. No docs posted (rendering-level
  regression only; purchase backend untouched by v1.21.4). Observation: SPA
  transitions intermittently slow (5-10s) tonight — one transient Next router wedge
  when navigating away mid-load (recovered by hard nav, NOT reproducible) — likely
  S13/edge-latency family; fold into the CF log review.
- Phase: **SALES-SIDE UX TEST COMPLETE (2026-07-16 ~06:1x).** Full chain E2E on prod
  BU TEST: QT (create/edit-preserves-docDate/send/accept) → SO post → INV issue →
  RC post (confirm dialog ✓) → Settled ✓ → AR aging balanced ✓ → PDF ✓.
  FINAL report: REPORT-sales-uxtest.md (16 findings). Fix spec ready for Ham go:
  specs/fix-sales-ux-findings-2026-07-16.md. Manual ch.4 refreshed (420a4c0).
  **Top findings: S13 prod intermittently 503s on writes that still APPLY (proxy layer
  — investigate first, dup-risk); S4 QT/SO/DO list DTOs lack BusinessUnitId (BU column
  dead ×3 pages, needs API deploy); S11 no confirm dialogs on QT send (issues number!)
  / SO post / INV issue — only RC has one.** Test docs left: QT #5/#8 drafts +
  QT/SO/IV/RC-TEST-0002 chain (settled). MCP connector TEAS-Repttown still needs
  re-auth (Ham).
- **FIX ROUND DONE (2026-07-16 evening, Ham-approved "แก้ทุก finding"):** S1–S16 all
  landed on main — 83e47f9 (WP-A backend), e71f3e3 (WP-B FE flow), 996d91a (WP-C
  polish). Gates green (17 new BE tests, suite baseline+2; FE tsc + 61 vitest;
  runtime-verified local both VAT/non-VAT cos). S13 = Cloudflare edge (origin clean).
  **NEXT = Ham decisions: (1) release/deploy — backend changed, needs API deploy w/
  DB backup (no schema change); (2) CF dashboard 5xx pull 13:02-13:12 ICT 07-16;
  (3) LE cert rate-limit cleanup (npm-1/9/11/23); (4) prod test-doc cleanup.**
- Prior phase: **v1.21.2 DEPLOYED (2026-07-15 ~20:25) — R2 Option B + BU-column batch LIVE.**
  Ham decisions executed: (B) PO draft edit now PRESERVES DocDate (§10 amended,
  UpdateDraftAsync; Create/Approve pinning untouched; new integration test) + BU-column
  stale-memo fix on all remaining 8 list pages. Commits 0258260 (BU×8) + 6368451 (Option B),
  release v1.21.2 @ ec8b33c (PR #81), API DEPLOY_OK (backup taken, sql-scripts unchanged 69,
  version=1.21.2) + FE_DEPLOY_OK, public login 200. Scripts archived publish/v1.21.1/
  (deploy-*-v1212.sh). Local live-verification: edit preserved docDate 2026-06-22 on PO #8,
  BU names on 2/8 pages fresh-mount. **Prod Chrome spot-check PENDING a fresh login by Ham**
  (8h session expired mid-check): PO #4 /edit → hint "ล็อกตามวันที่สร้างเอกสาร" + docDate
  15/07; create form → "ล็อกเป็นวันนี้"; /vendor-invoices + /invoices fresh mount → BU names.
  Still open for Ham: MCP connector TEAS-Repttown re-auth; other doc types still re-pin
  docDate on draft edit (PO-only per decision scope — batch follow-up if wanted).
- **PROD SPOT-CHECK RESULT (2026-07-15 ~20:45, fresh Ham login):** footer v1.21.2 ✓;
  R2-B on prod ✓ (PO #4 /edit shows stored docDate + hint "ล็อกตามวันที่สร้างเอกสาร";
  create form shows "ล็อกเป็นวันนี้"); /vendor-invoices BU names ✓.
  **NEW FINDING R8: /invoices (billing notes) BU column STILL "#3"/"#1" on prod** —
  business-units fetch 200, cache-bust (?cb=1) did NOT help, /vendor-invoices works in the
  same session → page-specific defect, NOT the deployed-build/cache. The reviewed diff for
  invoices/page.tsx looked identical to the working pages — root cause TBD (suspect: the
  billing-note list row's BU field differs, or its buName lookup misses; diagnose locally).
  **R8 RESOLVED — v1.21.3 deployed (2026-07-15 ~23:40).** TRUE root cause (2nd bug stacked
  under R1): TanStack Table `row._valuesCache` caches the accessorFn result PERMANENTLY per
  row (invalidated only on a new data reference) — a row built before business-units
  resolved keeps "#id" forever; the R1 memo-deps fix couldn't override it. Deterministic
  repro: create→save→SPA redirect to list (3/3 fail pre-fix). Fix: BU column `cell` resolves
  from `row.original.businessUnitId` every render (accessorFn kept for faceted filter) —
  applied to ALL 9 list pages (f6a8356), release v1.21.3 @ 82ac750 (PR #82), FE-only deploy
  FE_DEPLOY_OK, prod-verified via Chrome: /invoices names ✓ + SPA-nav → /purchase-orders ✓.
  Footer shows v1.21.2 = API version (API not redeployed, FE-only — expected).
  troubles-wiki updated (R8 + next-build-vs-dev corruption footgun).
- Prior: **v1.21.1 DEPLOYED + R1-R4 PROD-VERIFIED (2026-07-15 ~18:20) — pipeline COMPLETE.**
  R1/R3/R4 (731e775) + R2-FE locked docDate (526a55b) → release v1.21.1 @ b08387e (PR #80,
  admin-merged per wiki) → FE-only deploy FE_DEPLOY_OK (deploy-fe-v1211.sh archived in
  publish/v1.21.1/, no API/DB change) → all four re-verified on prod via Chrome incl. R4
  full chain (VI-TEST-0003 321 → PV prefill 321 exact → posted → settled PAID). Details:
  PROGRESS-purchase-uxtest.md §R1-R4 FIX ROUND.
  **OPEN for Ham:** (1) backend §10 docDate re-pin on draft edit — keep (current; FE now
  shows locked-today honestly) vs preserve-on-edit (backend change + API deploy);
  (2) same stale-memo BU-column bug latent in 8 other list pages (troubles-wiki entry) —
  batch-fix candidate; (3) MCP connector TEAS-Repttown needs re-auth (token expired).
- Prior phase: **BU TEST CONFIRM ROUND on prod v1.21.0 DONE (2026-07-15 ~11:30, Claude Chrome).**
  Full purchase chain re-run on prod (PO-TEST-0003 → VI-TEST-0002 → PV-TEST-COGS-0001, all
  posted, VI settled 214/214 PAID). 24 findings CONFIRMED FIXED incl. all money/compliance
  (F15 percent-UI, F27 non-recoverable forced server-side, F20 COGS, F13 taxId, F16 8h+refresh,
  F21 single-POST). 4 new issues found: R1 PO-list BU column still #id (F3 not actually fixed
  on that page), R2 PO edit-save resets docDate to today (data bug), R3 po.reopen_blocked
  toast EN-only, R4 PV-from-VI under-settles a VAT-carrying VI of a non-VAT vendor (edge).
  Full log: PROGRESS-purchase-uxtest.md §CONFIRM ROUND. MCP connector token expired —
  Ham must re-auth the claude.ai TEAS-Repttown connector.
- Goal (2026-07-14, Ham away): purchase-side UX/UI test on PROD (BU TEST) via
  Claude Chrome + refresh outdated manual ch.5. PROGRESS-purchase-uxtest.md =
  full findings log (F1–F27; top: F15 VAT-fraction field accepts "7"→700%,
  F16 ~25-30min token TTL w/ silent-401 UX, F20 COGS no default GL on Repttown,
  F27 non-VAT company can post recoverable input VAT).
- Phase: **v1.21.0 DEPLOYED TO PROD (2026-07-15 ~10:35) — purchase-findings fix batch LIVE.**
  Tag v1.21.0 @ bf36ba3 (release-please #79). API + FE both deployed. 623 backfill ran
  under RLS on prod (expense_null_defaults 2→0), applied_sql_scripts 68→69, version=1.21.0,
  all regression + v1.20.0-migration probes PASS, DB backed up. FE build OK, teas-web online.
  Public E2E through teas.kazaki-rio.com GREEN (login 200 / mcp 401 / wellknown 200). Cookie
  TTL 15min→8h config live (effective on next login). Scripts + DEPLOY-README archived to
  publish/v1.21.0/. Build worktree removed. Ham: rotate the leaked password; the pre-existing
  McpServerSmokeTests.E3_create_vendor failure (not from this batch) still needs triage.
- Prior phase: **PURCHASE-FINDINGS FIX PIPELINE — WP1–WP4 landed on main (a86de78).**
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

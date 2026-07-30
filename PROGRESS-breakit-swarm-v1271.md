# PROGRESS — "ทำยังไงก็ได้ให้พัง" break-it swarm on v1.27.1 (2026-07-30 ~23:2x)

Ham /goal: *"ตอนนี้ Live เป็น 1.27.1 ส่งฝูง Sonnet ไปเทสซะ ทั้งบริษัท Vat/Non Vat
ไปทั้งฝูงทำยังไงก็ได้ให้พัง"* — adversarial swarm, both VAT and non-VAT, break it.

Prod = **v1.27.1** (teas.kazaki-rio.com). Not a feature round: the deliverable is
**defects**, not code. Fix arc comes after Ham sees the verdict.

## State
- Written at quota **90%** (block 95, 5h window resets ~2026-07-31 00:00 GMT+7).
  A 10-agent Sonnet fleet launched at 90% dies mid-run and loses every finding →
  **dispatch is deliberately deferred to the wakeup**, not skipped.
- ScheduleWakeup chained to the reset; on wake: verify `~/.claude/quota-guard/state.json`
  shows a fresh window, then dispatch Wave A + B from §Dispatch below.

## Targets (hard rules for every agent)
- **co5** = บริษัท ทดสอบ VAT (DUMMY) — VAT playground, litter freely.
- **co7** = non-VAT dummy (id=7, periods OPEN) — non-VAT playground.
- **co6** non-VAT: FY2026 year-end CLOSED, accepts no new PV until 2027. Read-only probes only.
- **co2 / co3 = REAL (Repttown ฯลฯ) — UNTOUCHABLE.** Verify the company badge before every write.
  co2's P&L is load-bearing for manual ch7/8.
- MCP connector `TEAS-Repttown` points at the wrong company → **forbidden** for writes.

## Swarm shape
Concurrency is safe: agents drive **prod over HTTP/browser**, no shared test DB.
**co5 accounts (REUSE, exist on prod)** — password suffix is the ROLE-SLOT CODE, NOT the role name
(this bit A5's first dispatch; corrected live). Exact map:
sales01=`UxSwarm-2026-A1` · acct01=`-A2` · appr01=`-A3` · ap01=`-A4` · ar01=`-A5` · audit01=`-A6`
· chief01=`-A7` · admin01=`-A8` · purch01=`-A9` · tax01=`-B1`. chief01 posts everything.
**co7 (non-VAT):** nvadmin02 / nvchief02 — password unknown (see blocker below).
Target host: https://teas.kazaki-rio.com
Chrome MCP is single-session → **at most ONE browser agent at a time**; the rest drive
the API through the public host (login → JWT → REST), which is how rounds 3–5 ran 10-wide.

Every agent works its module **twice**: (1) happy path end-to-end, numbers tied to hand-calc;
(2) then attack it. A module that only passes the happy path is not "tested" this round.

Waves run **sequentially**, agents inside a wave in parallel. Wave B's docs feed Wave D's PDFs.

### Wave A — sales + purchase full chains, both companies — 5 agents, API-driven
1. **A1 sales chain co5 (VAT)**: QT→SO→DO→IV→TI→RC end-to-end + partial receipt, over-receipt,
   ใบวางบิล (billing note) from 2 invoices, CN/DN against a PAID TI, partial credit,
   0% line mixed with 7%, rounding at .005. Three-way tie ภ.พ.30 vs sales-summary vs TB.
2. **A2 purchase chain co5 (VAT)**: PO→VI→PV incl. WHT 1/3/5%, partial payment, one PV
   settling 2 VIs, PO close/reopen, VI over-billing a PO, AP aging vs TB tie.
3. **A3 foreign vendor co5**: never-driven ภ.พ.36 + ภ.ง.ด.54 reverse-charge chain
   (foreign vendor → service VI → นำส่ง) vs hand-calc; the v1.22.11 fix must still hold.
4. **A4 sales+purchase co7 (non-VAT)**: same two chains in non-VAT mode. Any VAT field, VAT GL
   (1170/2151), or VAT wording surfacing = finding. VI VAT folds into cost, vendor paid in FULL
   (the 2026-07-25 spec-error class). TB Dr=Cr after every post.
5. **A5 doc-number + concurrency**: hammer concurrent post/approve on TI/RC/PV/JV across both
   companies; 23505 `*_doc_no`, gaps, reused numbers under the retry-guard (CRIT-1 family, cap 50).

### Wave B — approval / expense claims / payroll — 5 agents
6. **B1 approval + SoD attack (both cos)**: every doctype's approve/post chain — self-approval,
   approver without the scope, approve twice (double-click race), approve a doc another user is
   editing, pending-approvals widget accuracy, approve after period close, out-of-order transitions.
7. **B2 expense claims full cycle co5**: create→submit→approve→pay + attachments, VAT-carrying
   claim, claim > limit, reject-then-resubmit, GL 1170 correctness, claim paid twice.
8. **B3 expense claims co7 (non-VAT)**: same cycle; the non-VAT 1170 guard (v1.22.10 F-A) must
   hold live — JE carries no 1170, VAT folds into cost.
9. **B4 payroll full cycle co5**: create→calc→approve→post→pay + month-2 continuity, opening-YTD,
   deduction, dup-guard, ภ.ง.ด.1 / 1ก / สปส.1-10 / payslip / 50ทวิ generated, GL tie
   5400/5410/2153/2160/2170 to hand-calc.
10. **B5 payroll edge attack (co7)**: mid-month hire/leave proration (O8 known gap — confirm
    blast radius in GL + on the printed forms), negative adjustment, deduction > net, zero salary,
    two runs same period, delete a posted run, ภ.ง.ด.1 vs GL tie.

### Wave C — period / immutability / tenant / MCP — 4 agents
11. **C1 period + immutability attack**: post into a closed period, reopen month, back-date,
    future-date, edit/delete a POSTED doc via direct API (not just UI), void attempts, year-close.
12. **C2 cross-tenant + RBAC attack**: co7 user reaching co5 data by id-guessing on EVERY REST
    route (documents, reports, attachments, exports); super-admin scope boundaries; token replay.
13. **C3 MCP agent surface**: API-key scopes — try to grant/forge a `.post` scope, post a draft
    via MCP (must be structurally impossible), draft on a company the key doesn't own,
    unbalanced/garbage payloads, header+inactive accounts (v1.27.0 gates).
14. **C4 journal/JV attack**: unbalanced by 0.01, 30-line JV, float split, header/inactive
    accounts, post twice (double-click race), approve banner with a permission-less user.

### Wave D — PDF / print / exports (runs on the docs Waves A–B produced) — 3 + vision
15. **D1 PDF sweep co5 (VAT)**: download/open EVERY doctype's PDF — QT/SO/DO/IV/TI/RC/CN/DN/BN/
    PO/VI/PV/EC/payslip + ภ.พ.30/ภ.ง.ด.1/1ก/3/53/54/50ทวิ/สปส.1-10. Check: totals match the
    screen, Thai glyphs (grep ม vs Bengali ম), BE dates, doc-no, signature+stamp on issued docs
    and NOT on drafts, 30-line pagination (repeated header, atomic bottom group, หน้า x/y).
16. **D2 PDF sweep co7 (non-VAT)**: same list in non-VAT layout — no VAT columns/wording anywhere.
17. **D3 exports attack**: every CSV/txt/batch export — OWASP formula injection, TIS-620 encoding,
    empty-data crash, huge date range, blob-tab flakiness, RD Format กลาง batch files.
- **Vision pass (AGY — separate quota pool)**: screenshots/PDFs from D1–D2 compared against the
  official RD/SSO form layouts for field placement. AGY writes to its sandbox, never the repo.

Each agent returns `swarm-findings/breakit-v1271/<agent>.md`: repro steps, exact request/response,
expected vs actual, severity. **No fixes, no commits** — evidence only.

## DISPATCHED (2026-07-31 ~00:05, fresh 5h window 0%)
- **Wave A running (4 agents, co5 VAT, API-driven):** A1 sales · A2 purchase · A3 foreign-vendor
  (ภ.พ.36/ภ.ง.ด.54) · A5 doc-number concurrency. Findings → `swarm-findings/breakit-v1271/`.
  Held Wave B until A reports so A5's concurrency chaos doesn't pollute B's happy-path baselines.
- **⛔ co7 BLOCKER — need password.** nvadmin02/nvchief02 (userId 24/25, co7 non-VAT) exist but
  their passwords are NOT recorded anywhere (the army Playwright script that logged in was deleted
  per its own hard-rules). Blocks A4, B3, B5 (the 3 non-VAT agents). co5 agents unaffected.
  **ASK HAM** for co7 creds, or reset via super-admin. Everything else (approval both-via-co5,
  expense co5, payroll co5, period/JV/MCP, PDF co5) runs without it.

## FINDINGS (Fable to verify each in code before any fix arc)
- **F1 (A5) — HIGH robustness, no data loss.** Concurrent double-post of the SAME draft JV →
  `POST /journals/{id}/post` returns **raw HTTP 500 `internal_error`** to the losing racers.
  Min trigger N=3 (N=2 clean). Root cause per A5: `je.not_draft` guard is a read-check TOCTOU;
  real conflict escapes at SaveChanges (DbUpdateConcurrencyException) unmapped → generic 500.
  NEW trigger of the CRIT-1 raw-500 class (state-transition race, not bucket drift). Data integrity
  HELD: posted exactly once, Dr=Cr, unique docNo, no orphaned number on the 500 threads.
  Follow-up A5 flagged: same generic `IConcurrencyVersioned` post path → one-shot double-post race
  on `/tax-invoices/{id}/post` and `/payment-vouchers/{id}/post|approve` likely repro too.
  A5 PASS otherwise: JV seq bulletproof 20-wide (0062..0086, contiguous), mixed-type no 23505/40P01.

- **F2 (A3) — CRIT compliance, money. Fable-VERIFIED in code.** ภ.พ.36 double-counts ม.83/6
  reverse-charge VAT. `WhtFilingService.GeneratePnd36Async` (WhtFilingService.cs:257-266) does
  `viRows.Concat(pvRows)` with **no dedup**; a foreign-service VI AND its settling self-withhold PV
  both carry `RequiresPnd36ReverseCharge` with the same `SubtotalAmount`, so one ฿20,000 service is
  counted twice → service 40,000 / VAT 2,800 instead of 20,000 / 1,400. On `mode=finalize` this
  posts an **immutable JV over-remitting output VAT to the RD** (PostReverseChargeJvAsync, line 282-283).
  Proven live: VI-0007 + PV-IT-0003 both in July's ภ.พ.36; co5's pre-existing VI-0004/0005 + PVs
  already double-counted. Fix-arc design Q: flag belongs on VI-only OR PV-only, query picks one.
  A3 used PREVIEW only on prod — no bad JV posted.
- **F3 (A3) — MED.** ภ.พ.36 has NO printable form: `/tax-filings/pnd36/pdf` → 404, no
  `BuildPnd36PdfAsync`/`Pnd36FormFiller`, FE page has no print control — yet every sibling filing
  (ภ.พ.30, ภ.ง.ด.2/3/53/54, 50/51) has a PDF. The mandatory reverse-charge return can't be filed.
- A3 INFO: foreign vendor accepts a Thai Tax ID (semantically wrong, routing OK).
- A3 PASS: v1.22.11 VI-linked-PV guard HOLDS (JE 229 balanced, WHT 3,529.41 ref); ภ.ง.ด.54 does
  NOT double-count (1 cert/PV); empty period clean; posted VI/PV immutable; non-THB clean 400.

- **A2 (purchase co5) — PASS, no CRIT/HIGH.** Chain money-correct end-to-end (input VAT→1170,
  WHT→2152, vendor net = gross−WHT, AP→0), immutability 6/6 clean, parallel numbering no 23505.
  - **F4 LOW** — VI can over-bill a PO to ~200% and post with only an advisory chip; no hard block
    or approval gate on >105% over-receipt (loose matching by design, but the control is non-binding).
  - **F5 LOW** — `/reports/trial-balance` defaults as-of to `UtcNow`; `/reports/ap-aging` defaults to
    Bangkok-today → between 00:00–07:00 Bangkok the two disagree by a full day (phantom AP gap;
    gone with explicit `?asOfDate=`). TB also silently ignores an unknown query param (no 400).
  - INFO: one-PV-settling-many-VIs is NOT supported by the API (single `VendorInvoiceId`) → A2's
    "PV settling 2 VIs" attack N/A. PV SoD is permission-based (no hard creator≠approver), per prior
    Ham decision.

- **A1 (sales co5) — no 500, no Dr≠Cr, no cross-tenant; TB held 514,505.64 through every attack.**
  - **F6 (A1) — HIGH compliance. Fable-VERIFIED structural cause.** Duplicate running numbers on
    posted tax documents: 4 duplicate TI numbers (TI-0001..4 each ×2) + **live-reproduced** a
    duplicate ใบลดหนี้ (`07-2026-CN-0001` posted identical to an existing posted CN). Cause: the
    TI/CN/RC unique index is `(CompanyId, BranchId, DocNo)` (TaxInvoiceConfiguration.cs:99,
    TaxAdjustmentNoteConfiguration.cs:62, ReceiptConfiguration.cs:66) but the number allocator
    sequences per (company,type) ignoring branch → docs on a different/NULL branch reuse the same
    visible running number and the DB accepts them. RD requires unique tax-doc numbers. Fix-arc Q:
    branch-aware allocator OR company-wide unique index OR branch segment in the number.
  - **F7 (A1) — MED.** `/reports/number-gaps` reports `hasGaps:false` for the same period — it only
    detects MISSING numbers, never REUSED ones → false "compliant" on a compliance control (masks F6).
  - **F8 (A1) — MED.** Three-way tie FAILS: `sales-summary` sums only posted TaxInvoiceLines and
    EXCLUDES CN/DN → disagrees with ภ.พ.30/TB by exactly the credit-note total (+2,000 net/+140 VAT).
    PND30 and TB agree with each other; sales-summary is the odd one out.
  - **F9 (A1) — MED (O2b class).** Billing note with BOTH TaxInvoiceIds and manual Lines silently
    drops the linked TI from the total while still referencing it.
  - **F10 (A1) — HIGH security (verify at fix-arc).** RBAC leak: sales01 is denied direct
    `POST /tax-invoices` (403) but CAN mint a TI draft via `POST /billing-notes/{id}/create-tax-invoice`
    (200) — the scope check is missing on the billing-note→TI route.
  - A1 LOW: per-line VAT rounding +0.01; zero-total TI accepted; zero-value billing note accepted.
  - A1 PASS: happy path ties (2,500/175/2,675); over-receipt→422; posted-TI edit/delete→405;
    receive-draft→422; VAT7+rate0 injection normalized; mixed 0%/7% correct; concurrent TI posts unique.

## WAVE A COMPLETE (4/4). Tally: 1 CRIT (F2) · 3 HIGH (F1, F6, F10) · 4 MED (F3, F7, F8, F9) · 3 LOW.
Fable-verified in code: F2 (PND36 no-dedup), F6 (branch-scoped unique index). F1/F10 verify at fix-arc.

## Next (resume here)
1. [x] Quota window reset (0%). Wave A done.
2. [ ] Launch Wave B co5-runnable: B1 approval/SoD · B2 expense-claims · B4 payroll. (B3/B5 need co7 pw.)
2. [ ] Pull the 10 co5 creds + co7 creds (nvadmin02/nvchief02) into the dispatch prompts.
3. [ ] Dispatch **Wave A** (5 agents, one message — distinct companies/doc-types, all API-driven).
4. [ ] **Wave B** (5) after A reports.
5. [ ] **Wave C** (4) — C3 needs an API key issued from settings first.
6. [ ] **Wave D** (3) last — it consumes the documents A/B created; give D1 the browser slot.
       Vision comparison → AGY (separate pool), keeps Claude quota for the finders.
7. [ ] Consolidate → `VERDICT-breakit-v1271.md` → Ham decides the fix arc.

Re-check quota between waves; ≥85% = stop dispatching, checkpoint, wakeup, resume next window.
17 agents will not fit one 5h window — expect this to span 2+ windows, which is why the wave
boundaries are also the checkpoint boundaries.

## Rules recap
- Prod writes only on co5/co7. Verify company badge/id before every write.
- Any 500 / data-loss / cross-tenant leak = STOP that agent, report immediately (proactive push).
- Quota ≥85% → no new Claude dispatches; ≥95% → checkpoint + wakeup only.

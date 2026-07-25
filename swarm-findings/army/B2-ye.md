# B2-ye — Year-end closing, co6 (2026-07-25, prod v1.22.11) — LAST leg on co6

Company: co6 "บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด" (id=6). Users: nvchief01
(CHIEF_ACCOUNTANT — closes periods/year, has `gl.period.close`+`gl.year.close`),
nvadmin01 (COMPANY_ADMIN — used for the post-close deny probe, matches its
"creates docs" role from B2-nv/B2-pr). Driven via headless Playwright
(5 short scripts `army-B2-ye*.mjs`, all deleted after the run) against
`https://teas.kazaki-rio.com`, tall viewport 1440×2200 (B2-nv lesson).
Read `specs/year-end-closing.md` (backend design, D1–D9) +
`swarm-findings/army/B2-nv.md`+`B2-pr.md` (co6 state) first.

**co6 is fully, permanently period-locked as of this leg — by design, this
is the intended terminal state; nothing else runs on co6 after this.**

## Done (all 5 mission items)

1. **PRE-STATE captured** before any mutation: TB as-of 2026-12-31 =
   ฿415,521.24 = ฿415,521.24 (balanced); P&L for FY2026
   (2026-01-01→2026-12-31, `includeUnspecified=true`) = Revenue ฿3,000.00 /
   Expense ฿205,640.00 / NetProfit **−฿202,640.00** (a loss year — 3 employees'
   payroll from B2-pr dominates). Screenshots `B2-ye-01/02`.
2. **Period close (monthly)** — the mission's predicted `period.draft_present`
   refusal on July **did not occur**: `POST /periods/2026/7/close` returned
   **200 clean** on the first attempt. Investigated why before proceeding
   (not just accepted blind): `PeriodCloseService.CloseAsync` only checks
   Draft **TaxInvoice/PaymentVoucher/JournalEntry** — none of B2-nv's
   orphaned drafts (abandoned Quotations/SO/DO/BillingNotes, duplicate
   customers/vendors) are in that set, `TaxInvoice` can never even exist on a
   non-VAT company (`NonVatGuard`), and co6's one PaymentVoucher was already
   fully Posted. Confirmed live before closing (`GET /payment-vouchers`,
   `GET /tax-invoices?status=Draft` → empty) rather than assuming. **This is
   a PASS** (clean close, not a bug) — the mission's predicted blocker was a
   reasonable but incorrect prior; documented for the record, not filed as a
   finding. Closed all 12 months of FY2026 (Jan–Dec, including 5 "future"
   months relative to today 2026-07-25 — nothing in `PeriodCloseService`
   restricts closing a future month with no data in it). Screenshot
   `B2-ye-04` (all 12 "ปิดแล้ว").
3. **Year-end closing, FY2026** — driven via the real UI (`/period-close`,
   "ปิดบัญชีสิ้นปี" button → `AlertDialog` confirm → success toast):
   - **(a) Closing entries posted, hand-calc match EXACT.** Closing JE
     `#169` (`12-2026-JV-0001`, DocDate 2026-12-31): `Dr 4000 3,000.00 /
     Cr 5200 2,140.00 / Cr 5400 200,000.00 / Cr 5410 3,500.00 / Dr 3300
     202,640.00` — pulled directly via `GET /journals/169`, not the FE's own
     display. Total Dr=Cr=205,640.00. `netProfit` in `FiscalYearStatus` =
     **−202,640.00**, matching the pre-state P&L exactly. See hand-calc table
     below for the full derivation.
   - **(b) TB still Dr=Cr after closing.** ฿621,161.24 = ฿621,161.24
     (up from 415,521.24 by exactly the closing JE's ฿205,640.00 on both
     sides). Revenue/Expense accounts (4000/5200/5400/5410) all net to
     **exactly 0** post-close; `3300` nets to **202,640.00** (debit-side,
     correctly representing the deficit). Screenshot `B2-ye-07`.
   - **(c) P&L for the closed year vs. the balance sheet.** These behave
     *differently by design* (spec D1) and both were verified correct,
     not conflated: the **range P&L** (`/reports/profit-loss`,
     2026-01-01→2026-12-31) is **UNCHANGED** post-close — still
     Revenue 3,000 / Expense 205,640 / NetProfit −202,640 (screenshot
     `B2-ye-08`) — this is the CORE regression the spec's C1 fix exists to
     prevent (excluding closing entries from the range aggregation), and it
     held live in prod. Meanwhile the **point-in-time reports** (TB, balance
     sheet) correctly show the P&L accounts zeroed and the earnings carried
     into `3300`: balance sheet `currentPeriodEarnings` = **0** as-of
     2026-12-31, Equity section now shows `3300 = −202,640.00`, `balanced:
     true` (screenshot `B2-ye-09`).
4. **Post-close immutability**:
   - **Deny probe (clean, no 500):** `POST /vendor-invoices` (nvadmin01,
     right after period close — VI `DocDate` is server-pinned to
     `TodayInBangkok()` per the codebase's "§10" convention, and *today*
     (2026-07-25) is inside the now-closed July period, so no back-dating
     was even needed) → **422**
     `{"type":"urn:teas:error:period.closed","title":"period.closed",
     "status":422,"detail":"Period 2026-07 is CLOSED. Reopen the period or
     correct doc_date."}`. Clean Thai/English-neutral structured error, no
     stack, no 500. `EnsureOpenAsync` runs before any other validation in
     `VendorInvoiceService.CreateDraftAsync`, so this is unambiguously the
     period-closed check firing, not a side effect of a malformed body.
     UI-drive of the same form got as far as filling vendor+amount
     (screenshot `B2-ye-10`) but the Save-draft button stayed disabled
     because the script never filled the description field (client-side
     `canSave` guard) — a script gap, not a product issue; the API probe
     against the identical code path is the authoritative evidence.
   - **Reopen exists, tested, and correctly scoped.** UI offers
     "เปิดงวดบัญชีสิ้นปีอีกครั้ง" (reopen), gated by the same `gl.year.close`
     perm as close (CHIEF_ACCOUNTANT + COMPANY_ADMIN + SUPER_ADMIN — no
     separate reopen-only role). Exercised live: reopen posted reversing JE
     `#170` (`12-2026-JV-0002`, `reversalOfId:169`, description "กลับรายการ
     ปิดบัญชี 2026 / Reopen FY 2026") — an EXACT Dr/Cr swap of `#169`'s 5
     lines. Post-reopen: `3300` net back to **0** (debit 202,640 = credit
     202,640, gross both sides now reflect close+reverse), 4000/5200/5400/
     5410 all restored to their exact pre-close net balances, P&L
     unaffected throughout (still −202,640). **Confirmed the 12 monthly
     `AccountingPeriod` rows stayed Closed after reopen** (`allPeriodsClosed:
     true` in the post-reopen `year-status`) — exactly D4's documented scope
     boundary (reopen undoes the close, not the monthly locks). Screenshots
     `B2-ye-12/13/14`.
   - **Re-close after reopen** (to leave co6 in its correct final state, and
     to exercise the filtered-unique-index "slot freed" mechanic live):
     closed again via the same UI button → **new** JE `#171`
     (`12-2026-JV-0003`, `reversalOfId:null`, a fresh independent close, not
     a continuation of `#169`), netProfit recomputed = same −202,640.00
     (correct: the sweep query excludes ALL `IsClosingEntry` rows, so `#169`
     and `#170` are both invisible to it and it re-derives from the same
     underlying original activity). Final TB: **฿1,032,441.24 =
     ฿1,032,441.24**. Screenshot `B2-ye-15` (green toast "ปิดบัญชีสิ้นปี 2026
     แล้ว", badge "ปิดแล้ว", "กำไรสุทธิ: −฿202,640.00").
5. **`specs/year-end-closing.md` open item classified** — see below.

## Hand-calc table (closing entry, §C sweep math from the spec)

Pre-close TB (2026-12-31): `4000` net Cr 3,000 · `5200` net Dr 2,140 ·
`5400` net Dr 200,000 · `5410` net Dr 3,500. Per spec formula
(`rawNet = Debit − Credit`):

| Account | rawNet | Sweep line | Expected | Actual (JE #169) | Match |
|---|---|---|---|---|---|
| 4000 (Revenue) | 0−3,000 = **−3,000** | Dr (zeroes a credit bal.) | Dr 3,000.00 | Dr 3,000.00 | ✅ |
| 5200 (Expense) | 2,140−0 = **+2,140** | Cr (zeroes a debit bal.) | Cr 2,140.00 | Cr 2,140.00 | ✅ |
| 5400 (Expense) | 200,000−0 = **+200,000** | Cr | Cr 200,000.00 | Cr 200,000.00 | ✅ |
| 5410 (Expense) | 3,500−0 = **+3,500** | Cr | Cr 3,500.00 | Cr 3,500.00 | ✅ |
| totalRawNet = −3,000+2,140+200,000+3,500 = **202,640** (>0 ⇒ loss) | | 3300 plug: Dr 202,640.00 | Dr 202,640.00 | Dr 202,640.00 | ✅ |
| **NetProfit = −totalRawNet** | | **−202,640.00** | −202,640.00 | −202,640.00 (`FiscalYearStatus.netProfit`) | ✅ |
| ΣDr = 3,000+202,640 = 205,640 · ΣCr = 2,140+200,000+3,500 = 205,640 | | balanced | 205,640.00=205,640.00 | `totalDebit:205640,totalCredit:205640` | ✅ **exact** |

Retained-earnings movement: `3300` moved **0 → −202,640.00** (a deficit,
correct for a loss year) — exactly matches the pre-state P&L's NetProfit.

## Post-close report checks

| Check | Expected (spec D1/C1/C4) | Actual | Match |
|---|---|---|---|
| TB Dr=Cr after close | balanced | 621,161.24=621,161.24 | ✅ |
| 4000/5200/5400/5410 net after close | all 0 | all 0 | ✅ |
| 3300 net after close | 202,640 (debit-side) | 202,640 | ✅ |
| BS `currentPeriodEarnings` as-of FY-end | 0 | 0 | ✅ |
| BS Equity section | includes 3300 = −202,640 | `{"accountCode":"3300",...,"balance":-202640}` | ✅ |
| BS `balanced` | true | true | ✅ |
| Range P&L (FY2026) after close | UNCHANGED (excludes closing entries — the core anti-footgun fix) | Revenue 3,000/Expense 205,640/NetProfit −202,640, identical to pre-close | ✅ **regression-safe in prod** |

## Reopen / re-close verification

| Check | Expected | Actual | Match |
|---|---|---|---|
| Reversing JE | exact Dr/Cr swap of the closing JE, `reversalOfId` set | JE #170, all 5 lines swapped vs #169, `reversalOfId:169` | ✅ |
| 3300 after reopen | back to 0 | debit 202,640 = credit 202,640, net 0 | ✅ |
| Revenue/Expense accounts after reopen | restored to pre-close balances | 4000 net −3,000, 5200 net 2,140, 5400 net 200,000, 5410 net 3,500 (all match pre-close exactly) | ✅ |
| 12 monthly periods after reopen | STAY Closed (D4 scope boundary — reopen-year ≠ period-reopen) | `allPeriodsClosed: true` post-reopen | ✅ |
| Re-close after reopen | fresh independent close (filtered-unique slot freed) | JE #171, `reversalOfId:null`, new `closingJournalId:171` | ✅ |
| NetProfit on re-close | same −202,640 (re-derived from original activity, both prior closing JEs excluded from the sweep) | −202,640.00 | ✅ |
| Final TB | balanced | 1,032,441.24 = 1,032,441.24 | ✅ |

## Findings

No HIGH/CRITICAL findings. Two LOW/process notes, not filed as product bugs:

**N1 — LOW (documentation, not a bug) — the mission's predicted
`period.draft_present` refusal on July did not occur.** Root cause
understood and confirmed live (see item 2 above): the close-blocking check
is scoped to Draft TaxInvoice/PaymentVoucher/JournalEntry only, and none of
those existed on co6 in July. B2-nv's "orphaned drafts" were Quotation/SO/
DO/BillingNote chains and duplicate master data — none of which
`PeriodCloseService` inspects. Worth a note for whoever writes the next
army mission brief: don't assume a doc-type's Draft status blocks period
close unless it's specifically TI/PV/JE.

**N2 — LOW (script gap, not a product issue) — UI drive of the post-close
deny probe stalled on a disabled Save-draft button** because the driver
script filled amount but not description (client `canSave` guard). Did not
retry with the field filled since the direct API call against the exact
same `POST /vendor-invoices` endpoint already produced definitive, clean
422 evidence (`EnsureOpenAsync` runs before any body validation in
`VendorInvoiceService.CreateDraftAsync`, so the deny is unambiguously the
period check, not a validation error).

No 500s anywhere across the full run (grepped every captured API response
across all 5 script runs — zero `"status": 500`). No cross-tenant data at
any point — every screenshot's company badge stayed on co6 throughout.

## Unbuilt vs. untested vs. broken — `specs/year-end-closing.md` open item

The spec's only unchecked item was **A5 (EF migration, Fable-owned)** —
left `[ ]` not because the migration was missing, but because generating it
was explicitly out of the backend implementer's stage-1 scope. Per the
spec's own Stage 2/5 status notes, the migration (`20260708163202_
YearEndClosing.cs`) was in fact generated, applied cleanly to `teas_test`,
and (after an RLS-scoping hotfix to scripts 610/611 — a real prod incident,
already documented in `troubles-wiki.md` and the spec's stage-5 log) is
live on production. Before this leg, that liveness had only ever been
confirmed by automated tests + the stage-5 hotfix probe — **never by a full
manual year-end-closing walkthrough against a real prod company.**

**Classification: BUILT + WORKING (verified, not merely assumed).** This
leg is the first end-to-end live confirmation: 12 monthly closes → fiscal
year close → hand-calc-exact closing JE → correct TB/BS/PL treatment
(including the C1 anti-footgun regression) → clean post-close deny → reopen
with correct reversal → re-close with correct re-derivation — every design
decision D1–D9 in the spec proven live in prod, not just in
`YearEndClosingTests.cs`. The spec's checkbox has been flipped to `[x]`
with this evidence (see `specs/year-end-closing.md` A5).

Nothing in this leg's scope was found to be UNBUILT or BROKEN.

## Evidence / artifacts

- Screenshots: `swarm-findings/army/B2-ye-01..15-*.png` (pre-state TB/PL,
  period-close before/after all-months-closed, close-year confirm dialog +
  result, post-close TB/PL/BS, post-close deny-probe form, isClosed badge,
  reopen confirm + result, final re-close result).
- Raw JSON dumps: `B2-ye-recon.json` (pre-state + July close), `B2-ye-
  recon3.json` (year-close + closing JE #169 + post-close reports), `B2-ye-
  recon5.json` (reopen + JE detail + re-close + final reports).
- Temp driver scripts `frontend/army-B2-ye*.mjs` (5 files) — all deleted
  after the run.

## Blast-radius note

No new fiscal DOCUMENTS were created (0 of the ≤4-document cap used) — the
one probe (VI creation attempt) was denied at 422 before any row was
persisted. The mutating actions taken (12× monthly close, year close,
reopen, re-close) are the mission itself, not "documents," and are exactly
what B2-ye was dispatched to exercise — co6 is now permanently
period-locked for FY2026 as intended (this was the last leg on co6).

## No tenant leak

Every screenshot's sidebar/company context stayed "TEAS · บริษัท ทดสอบ
NON-VAT (DUMMY) จำกัด" (co6) throughout all 5 script runs; no co2/co3/co5
data appeared in any list, report, or detail page.

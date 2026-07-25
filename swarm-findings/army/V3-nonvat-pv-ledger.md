# V3 — non-VAT PV fold-into-cost live ledger proof, co6, prod v1.22.12 — **BLOCKED, mission not completed**

Target: `https://teas.kazaki-rio.com`, co6 = บริษัท ทดสอบ NON-VAT (DUMMY) จำกัด (id=6). Users:
nvadmin01 (COMPANY_ADMIN), nvchief01 (CHIEF_ACCOUNTANT). Driven via a temp headless Playwright
script (`frontend/army-V3.mjs` + `army-V3b.mjs`, both deleted after the run per dispatch) using
`page.request` against the BFF proxy (`/api/proxy/*`) for authenticated API calls, plus real UI
navigation for the period-close screen.

**Bottom line: the mission's step 1 (reopen co6's current month) is impossible today — not a
script problem, a real, live-confirmed product gap.** No PaymentVoucher (or any other fiscal
document) could be created on co6, so the money-invariant proof this leg exists to get (fold vs.
zero, no 1170 debit, TotalPaid=gross) **could not be re-driven live**. This is reported as
BLOCKED, not silently downgraded to a partial pass.

## Done

1. Logged in as nvchief01, opened `/period-close` for co6, confirmed July 2026 (and all FY2026
   months) show badge "ปิดแล้ว"/Closed with **no action control at all** in the row (screenshot
   `V3-01-period-close-before.png`). Read the FE source
   (`frontend/app/(dashboard)/period-close/page.tsx` L222-234): the action `<td>` only renders
   a **Close** button, gated `{open && (...)}}` — there is no `else` branch, i.e. literally no
   button exists in this UI for a closed month.
2. Confirmed via API (`GET /api/proxy/periods/2026/7/status`) → `{"open":false}`.
3. Probed for an undocumented monthly-reopen route: `POST /periods/2026/7/reopen`,
   `/periods/2026/7/open`, `/periods/2026/7/reopen-month` → **404 on all three**. Cross-checked
   against source: `backend/src/Accounting.Api/Endpoints/PeriodEndpoints.cs` maps exactly 5
   routes — `{y}/{m}/close`, `{y}/{m}/status`, `{y}/close-year`, `{y}/reopen-year`,
   `{y}/year-status`. **No monthly reopen route is mapped anywhere in the codebase.**
4. Checked the ONE reopen mechanism that does exist — `reopen-year` (`gl.year.close`, what
   B2-ye actually exercised) — against what this leg needs. `GET /periods/2026/year-status`
   (live, `yearStatusBefore` in `V3-results.json`) shows `isClosed:true` (the YEAR itself is
   ALSO currently closed, re-closed by B2-ye's own final step) and, more importantly, **every
   one of the 12 monthly rows stays `"status":"Closed"` regardless of the year's own open/closed
   state** — confirmed identically in B2-ye's own report ("Confirmed the 12 monthly
   AccountingPeriod rows stayed Closed after reopen — exactly D4's documented scope boundary")
   and in code: `IYearCloseService.cs`'s own comment calls a monthly reopen "D9.3 — future
   period-reopen feature's job." **B2-ye's "reopen works and is clean" finding is real but
   answers a different question (fiscal-year close reversal) than what this mission needs
   (a monthly period unlock) — the dispatch's premise conflated the two.**
5. Since `PaymentVoucherService.CreateDraftAsync` pins `DocDate` to
   `_clock.TodayInBangkok()` unconditionally (never client-controlled — confirmed in code,
   `backend/src/Accounting.Infrastructure/Purchase/PaymentVoucherService.cs` L179-182) and
   `EnsureOpenAsync` is the very first thing it does, there is no way to send a request that
   lands in a still-open period: today (2026-07-25) is inside July 2026, which is closed, and
   every other FY2026 month is also closed. Reconfirmed live with the **exact WP-G money-bug
   shape** (mirrors `PaymentVoucherNonVatCompanyTests` precisely): vendor id 14 "ผู้ขาย NON-VAT
   ทดสอบ B2NV" (`vatRegistered:true` — confirmed via `GET /vendors`, this is the correct
   VAT-registered-vendor-selling-to-non-VAT-company fixture), expense category id 56 CAPEX
   (`defaultIsRecoverableVat:true` — confirmed via `GET /expense-categories`), line
   `amount:1000, vatRate:0.07, isRecoverableVat:true` → **`POST /payment-vouchers` → 422
   `period.closed`**, `"detail":"Period 2026-07 is CLOSED. Reopen the period or correct
   doc_date."` (`V3-pv-create-reconfirm.json`). The request never reaches the WP-G gate logic —
   it dies at the period check, the first line of the method.
6. Confirmed the earlier attempt (arbitrary vendor/category ids resolved from live
   `GET /vendors`/`GET /expense-categories`, before I had picked the "right" fixture) hit the
   same wall (`V3-results.json` — skipped only because the array-shaped response wasn't parsed
   as `{items:[...]}` on the first pass; the corrected follow-up script above got a clean
   reproduction).
7. **0 documents created, 0 mutations made.** No period was reopened (impossible) so none needed
   reclosing. co6's final state is **byte-identical to how this leg found it** — confirmed by
   the same `year-status` read showing all 12 months + the year itself Closed, matching B2-ye's
   documented terminal state. Screenshot `V3-01-period-close-before.png` doubles as the
   before-AND-after state (nothing changed).

## Expected-vs-actual (the four assertions the dispatch asked for)

| # | Assertion | Expected (stated before looking) | Actual | Result |
|---|---|---|---|---|
| 1 | Expense debit = gross (1,000 net + 7% VAT = 1,070.00) | 1,070.00 | **No JE exists — PV could not be created** | **FAIL (blocked, not disproven)** |
| 2 | No input-VAT debit line at all | absent | **No JE exists to check** | **FAIL (blocked, not disproven)** |
| 3 | `TotalPaid` = gross (1,070.00) | 1,070.00 | **No PV exists — no `TotalPaid` to read** | **FAIL (blocked, not disproven)** |
| 4 | Dr = Cr on the posted JE | balanced | **No JE exists** | **FAIL (blocked, not disproven)** |

None of these are DISPROVEN — the code path was never reached. This is a blocked verification,
not a red test. The existing Tier-2-approved dotnet evidence
(`PaymentVoucherNonVatCompanyTests.NonVatCompany_StandalonePv_FoldsVatIntoCost_NoInputVatLine`)
remains the only proof of the fold-not-zero invariant; it is unchanged by this leg.

## JE dump

None — no PaymentVoucher, and therefore no JournalEntry, was ever created this leg.

## Findings

**HIGH (process/product gap, new — not previously filed this precisely) — no monthly
period-reopen capability exists anywhere in the application**, confirmed live and in code (see
Done #3-4). Consequence: closing a company's current month is a ONE-WAY DOOR — once done, that
company can never receive a new draft TaxInvoice/PaymentVoucher/JournalEntry again via any UI or
API path, because DocDate is always server-pinned to real today and today's period is closed.
For co6, which had ALL 12 FY2026 months closed by the B2-ye leg, this means co6 is now
**permanently unable to create any new fiscal document for the rest of calendar 2026** — matches
B2-ye's own stated intent ("nothing else runs on co6 after this") but this leg is the first to
discover that intent is now an *enforced, irreversible* product limitation rather than a
convention future legs were expected to just respect. Filed here as a troubles-wiki entry
(`troubles-wiki.md`, "period.closed 422 on every new draft...") for any future leg's awareness;
recommend Fable/Ham decide whether building the D9.3 monthly-reopen feature is worth doing, given
it has now directly blocked a MONEY-critical verification.

**No other findings** — the WP-G server-side gate logic itself was not exercised (blocked before
reaching it), so this leg neither confirms nor refutes anything new about WP-G's correctness
beyond what the existing dotnet test suite already proves.

## Final co6 period state

**Unchanged from how B2-ye left it and how this leg found it**: fiscal year 2026 Closed
(`closedAt: 2026-07-25T12:10:28Z`, `closingJournalId: 171`), all 12 monthly periods Closed
(Jan-Dec 2026), `allPeriodsClosed: true`. No reopen was performed (none is possible), so no
re-close was needed. Screenshot: `V3-01-period-close-before.png` (also stands as the after-state
— nothing was mutated). Raw evidence: `V3-results.json`, `V3-pv-create-reconfirm.json` (both
copied alongside this report); `V3-vendors.json`, `V3-expense-categories.json` (scratchpad only,
not load-bearing beyond confirming the vendor/category ids used above).

## Screenshots

- `V3-01-period-close-before.png` — co6 `/period-close`, all months "ปิดแล้ว", no action control
  on any closed row.
- `V3-02-pv-new-co6.png` — co6 `/payment-vouchers/new` as nvadmin01 (form loads fine; the actual
  submit-blocking happens server-side per the API probe, not at the FE).

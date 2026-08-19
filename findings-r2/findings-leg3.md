# R2 Leg 3 -- Fixed Assets + Depreciation -- Findings

Company under test: company_id=1 (Demo Company). Env: FE :3000, API :5080.
Executed live via Playwright against the real UI (frontend/e2e/r2-leg3-*.spec.ts).
All 11 tests across 2 spec files PASSED. Zero source edits. No commits.

Findings tally: 0 🔴 (money/tax/security) · 1 🟠 (L3-9, wrong behavior) · 1 🟡 (L3-12,
contract/UX) · 2 ⚪ (L3-2, L3-3, spec-documented design notes) · 10 pass items.

## Checklist verdicts
1. Asset registration / acquisition posting -- PASS (L3-1)
2. Depreciation math (straight-line, proration, last-month) -- PASS + 2 design notes (L3-2..L3-4)
3. Idempotency (run twice same month) -- PASS (L3-5)
4. Accum. depreciation GL movement (Dr/Cr, Dr=Cr) -- PASS (L3-6)
5. Disposal gain/loss + stop-depreciating -- PASS (L3-7)
6. Closed-period refusal (depreciation run + disposal) -- PASS (L3-8)
7. Year-close interplay with un-run depreciation -- PASS, live + code evidence (L3-10)
8. Edit door (unchanged resave / edit-after-active) -- PASS + UI gap (L3-11, L3-12)
9. Permission probe (403s) -- PASS (L3-13)
10. Malformed probes (negative cost, zero life, disposal-before-acquisition) -- PASS on 3/4, one real gap found (L3-9, L3-14)

## Findings log

### L3-1  🟢 PASS -- Acquisition posts NO GL journal entry (FA-A design confirmed live)
Repro: created + activated 6 assets (A,B,C,D,F activated; E left Draft) across the full
walkthrough. Evidence: exactly 5 journal entries total (ids 71-75) touch company 1's fixed
assets -- 2 depreciation runs + 3 disposals = 5; the 5 Activate calls contributed ZERO. UI
correctly shows the fa-no-gl-cost-warning banner (asset detail page) whenever no
VendorInvoiceId is linked (all 5 test assets). docNo assigned only at Activate
(08-2026-FA-0001..0005), Draft->Active state machine enforced.

### L3-2  ⚪ NOTE (spec-documented, not a bug) -- No first-month/mid-month proration
Asset A (acquireDate 2026-08-05, mid-month) got the FULL monthly charge (1000.00) for
August, identical to asset B (acquireDate 2026-08-01). No day-count proration exists
anywhere in GenerateDepreciationAsync -- charge = full MonthlyAmount once
DepreciationStartDate <= runDate. Matches specs/fixed-assets.md's documented full-month
convention. Flagging only because an accountant used to IFRS day-count proration could be
surprised; not a defect against this repo's own spec.

### L3-3  ⚪ NOTE (spec-documented, not a bug) -- Final-month "plug" silently absorbs skipped months
Asset C (cost 100, salvage 0, life 3mo, DepreciationStartDate backdated into an
already-closed month) was never charged for its first scheduled month (unreachable --
closed). When its computed FINAL scheduled month arrived (August, current month), the
engine charged the full REMAINING balance (66.67, not the steady-state 33.33), landing
accumulated_depreciation at exactly 100.00 in only 2 run-lines instead of 3. This is the
spec's documented final-month plug (fixed-assets.md 3.1 step 4) working as designed --
but it means "count of depreciation_run_lines for an asset" is NOT a reliable proxy for
"months since depreciation started". A report/reviewer assuming 1:1 could be misled.

### L3-4  🟢 PASS -- Straight-line math exact, verified per-asset in DB
Hand-computed vs live: A monthly=1000.00 (24000/24), B monthly=2500.00
((100000-10000)/36), C monthly=33.33 (round(100/3,2,AwayFromZero)). August aggregate run
(depreciation_run_id=2): total=3566.67 (1000+2500+66.67), assetCount=3.
fixedasset.depreciation_run_lines confirms per-asset: A=1000.00, B=2500.00, C=66.67
(accumulated_after=100.00 = exactly the depreciable base -- FA-B "never exceeds base"
holds).

### L3-5  🟢 PASS -- Idempotent re-run, both months tested
Re-POST /depreciation-runs for an already-posted (year,month) returns 200 with
alreadyExisted:true and the IDENTICAL journalEntryId/totalAmount/assetCount as the first
call -- confirmed for both July (run_id=1, JE=71, re-run returned same) and August
(run_id=2, JE=74, re-run returned same). DB confirms exactly one depreciation_runs row
per period, no duplicate JE, no double-charge to any asset's accumulated_depreciation.

### L3-6  🟢 PASS -- Accum. depreciation GL movement correct, Dr=Cr every time
July JE 71: Dr 5450 (Depreciation Expense) 33.33 / Cr 1690 (Accumulated Depreciation)
33.33. August JE 74: Dr 5450 3566.67 / Cr 1690 3566.67. Both balance exactly (verified via
psql SUM(debit_amount)=SUM(credit_amount) per journal_id). Both visible via the
depreciation page's run-history table (linked to /journals/{id}) and the asset detail
page's per-asset run-lines table.

### L3-7  🟢 PASS -- Disposal gain/loss correct, hand-verified on 3 separate disposals; stop-depreciating confirmed
- D (never depreciated, disposed BEFORE any run touched it): NBV=12000, proceeds=5000,
  VAT=350 (7%), gainLoss=5000-12000=-7000 (LOSS). JE 73: Dr Cash(1110) 5350, Dr
  Loss(5460) 7000 / Cr AssetCost(1610) 12000, Cr OutputVAT(2151) 350. Balances 12350=12350.
- B (accumDep 2500 from the August run): NBV=97500, proceeds=90000, VAT=6300 (7%),
  gainLoss=90000-97500=-7500 (LOSS). JE 75: Dr Cash 96300, Dr AccumDep(1690) 2500, Dr
  Loss(5460) 7500 / Cr AssetCost 100000, Cr OutputVAT 6300. Balances 106300=106300.
- F (never depreciated, proceeds=0): NBV=5000, gainLoss=-5000 (full write-down as loss).
  JE 72: Dr Loss 5000 / Cr AssetCost 5000 -- cash/VAT lines correctly SUPPRESSED since
  cashReceived=0 (the "drop any zero line" rule works).
Stop-depreciating, live-proven (not just inferred): D was disposed BEFORE the August run;
that run's assetCount=3 (A,B,C only) with D structurally ABSENT from
depreciation_run_lines, and D's accumulated_depreciation stayed 0.0000 permanently
(re-confirmed via psql after the run). Enforced by GenerateDepreciationAsync's
Status==Active filter -- a Disposed/WrittenOff asset cannot re-enter any future run.

### L3-8  🟢 PASS -- Closed-period refusal, both depreciation run and disposal, typed not raw
Both probed against 2026-04 (no accounting_periods row for co1, not the current month ->
CLOSED by the default rule). Depreciation run -> 422 period.closed
("Period 2026-04 is CLOSED. Reopen the period or correct doc_date."). Disposal of Active
asset A into the same closed month -> identical 422 period.closed. Verified no
depreciation_runs row was created for (2026,4) and asset A stayed Active/untouched after
the failed disposal attempt (both checks happen before any DB write in the service).

### L3-9  🟠 FINDING (wrong behavior) -- No validation that DisposalDate >= AcquireDate
DisposeFixedAssetValidator, DisposeCoreAsync, and FixedAsset.Dispose() have no check
anywhere that the disposal date is on/after the asset's acquisition date. Reproduced live
through the REAL dispose modal (not just a raw API call): asset F was acquired
2026-08-10, activated, then successfully DISPOSED on 2026-07-15 -- 26 days BEFORE its own
acquisition date -- HTTP 200, no validation error, no warning. This posted a real balanced
GL journal entry (JE 72, doc 07-2026-JV-0004, dated 2026-07-15) for an asset that, per its
own register row, would not exist yet on that date; the July period's books now contain a
disposal whose asset's own acquisition happened in a later month. Severity: wrong
behavior (data-integrity gap), not a crash -- checklist item 10 asked for "typed errors,
not raw 500s" and technically got neither: no error at all.
Repro: POST /fixed-assets {acquireDate: D, ...} -> activate -> POST .../dispose
{disposalDate: <date before D>, proceeds: 0} -> 200 OK, asset.status=Disposed.
Screenshot: l3-9-date-order-anomaly.png (asset detail page for fixed_asset_id=5, showing
Disposed status with disposal date 2026-07-15 against acquire date 2026-08-10).

### L3-10  🟢 PASS -- Year-close interplay with un-run depreciation
Live probe: POST /periods/2026/close-year (admin) -> 422 year.periods_not_closed,
listing all 12 months of FY2026 as open. Zero mutation (psql-confirmed: co1
accounting_periods rows unchanged before/after). Source-confirmed (could not be
independently live-reproduced at the FA-specific hook itself -- see caveat below):
PeriodCloseService.CloseAsync has a dedicated depreciation-due hook that throws
period.depreciation_required if any Active asset is still owed depreciation and no run
exists yet for the target (year,month); this check runs BEFORE the transaction begins
(non-mutating on failure). Since YearCloseService.CloseAsync requires ALL 12 fiscal
months to already be Closed, and month-close is itself blocked pending depreciation,
year-close with un-run depreciation is transitively blocked. YearCloseService itself has
no FA-specific code -- enforcement lives entirely at the month layer.
CAVEAT: a live repro of period.depreciation_required specifically was attempted twice
(before running any depreciation) but both attempts hit an EARLIER guard in the same
CloseAsync method instead (period.draft_present) -- co1's August window has real,
pre-existing DRAFT tax-invoice rows left by other/earlier concurrent test legs (confirmed
via psql: my first pre-flight check used the wrong status-string case status='Draft' vs
the actual stored value 'DRAFT' and false-negatived; the corrected query found genuine
drafts, and separately, live churn consistent with another leg actively posting in co1 at
the same time). Did not touch or clear that data -- out of this leg's scope.

### L3-11  🟢 PASS -- Edit door
(a) Unchanged resave of Draft asset E: GET detail, echo every field back verbatim via PUT
-> 204 No Content; before/after GET diff shows every money/config field identical, only
version incremented 0->1 (expected optimistic-concurrency housekeeping, not a financial
side effect).
(b) Attempted edit of ACTIVE asset A (cost, life, dates all changed in the payload) via
PUT -> refused cleanly, typed 422 fixed_asset.not_editable
("Cannot edit a fixed asset in status Active."); asset A's stored fields unchanged.
Because UpdateDraftAsync only permits Status==Draft, and an asset can only become Active
via Activate (before which no depreciation could possibly have run), "edit cost AFTER
depreciation has run" is a structurally unreachable state in this API -- the Draft->Active
transition is the sole/earliest edit-refusal gate, so there is no separate
recompute-vs-refuse branch to exercise.

### L3-12  🟡 FINDING (contract/UX) -- No frontend UI exists to edit a Draft fixed asset
useUpdateFixedAsset() (PUT /fixed-assets/{id}) is wired in frontend/lib/queries.ts and
works correctly (see L3-11a), but there is no /fixed-assets/[id]/edit route and the
detail page (fixed-assets/[id]/page.tsx) renders no edit form or Edit button for Draft
assets -- only Activate/Cancel. A real user cannot correct a typo in a Draft asset (name,
cost, life, dates) without going outside the UI. Contract/UX gap, not a money bug.

### L3-13  🟢 PASS -- Permission probe
rbac_sales_staff (SALES_STAFF role, co1; confirmed via DB to hold zero fixedasset.*
grants) got HTTP 403 on all three probed mutating actions: POST /fixed-assets (create),
POST /depreciation-runs (run), POST /fixed-assets/1/dispose (dispose an asset it does not
even have read access to create/run for). No rows were created from the attempted create.

### L3-14  🟢 PASS on 3/4 -- Malformed-payload probes return typed 400s, never raw 500s
- cost=-500 -> 400, fieldErrors: "'Cost' must be greater than '0'." (+ a correct cascading
  "SalvageValue must be between 0 and Cost" since 0 is not <= a negative cost -- expected
  validator interaction, not a bug).
- usefulLifeMonths=0 -> 400, "'Useful Life Months' must be greater than '0'."
- salvageValue=5000 on cost=1000 -> 400, "SalvageValue must be between 0 and Cost."
- disposalDate before acquireDate -> the ONE case that did NOT produce a typed refusal;
  see L3-9 (accepted with 200, not rejected).

### Write-off (not walked -- route+source evidence only)
POST /fixed-assets/{id}/write-off exists (FixedAssetEndpoints.cs) and is a thin variant of
Dispose (DisposeCoreAsync with proceeds=0, vat=0, a mandatory free-text Reason, gated by
the SAME fixedasset.dispose permission, posting to the SAME Loss account since proceeds=0
forces gainLoss=-NBV). Not driven through the UI this leg -- the dispose walkthrough
(L3-7, L3-9) already exercises the identical money mechanics (DisposeCoreAsync is shared
code), so the only untested surface is the Reason field itself and the WrittenOff status
label. Established as present and route-correct, not behaviorally walked.

## Test data left behind (company_id=1, all R2L3-* prefixed, safe to leave/reap)
Fixed assets (fixedasset.fixed_assets):
  id=1 R2L3-A-Laptop         08-2026-FA-0001  ACTIVE    cost 24000  accumDep 1000.00
  id=2 R2L3-B-Copier         08-2026-FA-0002  DISPOSED  cost 100000 accumDep 2500.00 (at disposal)
  id=3 R2L3-C-RoundTest      08-2026-FA-0003  ACTIVE    cost 100    accumDep 100.00 (fully depreciated)
  id=4 R2L3-D-PreDispose     08-2026-FA-0004  DISPOSED  cost 12000  accumDep 0.00
  id=5 R2L3-F-DateOrderProbe 08-2026-FA-0005  DISPOSED  cost 5000   accumDep 0.00
  id=6 R2L3-E-EditDoor       (no docNo)       DRAFT     cost 8000   never activated
Also created via the RBAC/malformed probes spec: 3 rejected create attempts (no rows
persisted, all 400/403) and 1 rejected low-priv create attempt (no row persisted, 403).
Depreciation runs: id=1 (2026-07, total 33.33, JE 71), id=2 (2026-08, total 3566.67, JE 74).
Journal entries: 71 (Jul dep), 72 (F dispose, dated Jul), 73 (D dispose), 74 (Aug dep),
75 (B dispose) -- all POSTED, all Dr=Cr balanced.
Periods: co1 2026-07 and 2026-08 both confirmed still OPEN after the run (no accidental
close from the close-block probes -- both probe attempts failed before any write, by
design).
Throwaway specs (NOT committed, left on disk for re-run/reference):
  frontend/e2e/r2-leg3-fa-lifecycle.spec.ts
  frontend/e2e/r2-leg3-fa-api-probes.spec.ts
  frontend/e2e/r2-leg3-screenshot.spec.ts
Screenshot: Z:\temp\claude\Y--ClaudePlayground-TEAS-Project\5667c374-e2c0-4998-b10c-b993b4182367\scratchpad\l3-9-date-order-anomaly.png

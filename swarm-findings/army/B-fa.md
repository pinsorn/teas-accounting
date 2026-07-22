# Wave B-fa — fixed assets full lifecycle (co5), prod v1.22.10

Agent: sonnet (Playwright headless). Target: https://teas.kazaki-rio.com, company co5
(บริษัท ทดสอบ VAT (DUMMY) จำกัด) ONLY. Login: admin01 (`UxSwarm-2026-A8`). Never-before-driven
area (0 assets existed per A1 recon). Full lifecycle driven: register → activate → depreciate
(×2, idempotence) → dispose. Blast cap: 2 assets created (≤3 cap), 2 depreciation runs (≤2 cap)
— both respected exactly.

Before driving anything live, read `backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs`
in full — this shaped what to check for and caught one finding (F-2 below) before the browser
was even opened.

## Done
- [x] Registered + activated asset 1: cost 120,000, residual 0, life 36 months (mission's exact
      example numbers). docNo `07-2026-FA-0001`.
- [x] Registered + activated asset 2 (cheap): cost 6,000, residual 0, life 12 months. docNo
      `07-2026-FA-0002`. **No numbering collision** — clean sequential allocation, no 23505.
- [x] Depreciation run #1 for current month (2026-07) — posted, JE `07-2026-JV-0051` (#155).
- [x] Depreciation run #2, SAME month — idempotence check: **no double-post** (backend correctly
      no-ops), but a real UX finding surfaced (F-1 below).
- [x] Disposed asset 2 (sale path, proceeds 3,000 < NBV → a loss, exercising the gain/loss line).
      JE `07-2026-JV-0052` (#157): accum-dep reversal + gain/loss line both present and correct.
- [x] TB (Dr=Cr) checked after every step, 6 checkpoints total (see table below) — always balanced.
- [x] Tenant-leak check on dashboard/list body text (เรปทาวน์/พงศ์สันต์/repttown): clean, no hits.
- [x] No 500s/crashes/blank pages/raw i18n keys anywhere in the run.
- [x] Temp scripts `frontend/army-B-fa.mjs` + `frontend/army-B-fa-tbcheck.mjs` (follow-up TB
      as-of check) both deleted after the run.

## Evidence
- Full console log: `swarm-findings/army/B-fa-run-log.txt`, raw numeric results:
  `swarm-findings/army/B-fa-results.json`
- Asset 1 form/draft/activate: `B-fa-A1-form-filled.png`, `B-fa-A1-draft-detail.png`,
  `B-fa-A1-activate-confirm.png`, `B-fa-A1-active-detail.png`
- Asset 2 form/draft/activate: `B-fa-A2-form-filled.png`, `B-fa-A2-draft-detail.png`,
  `B-fa-A2-activate-confirm.png`, `B-fa-A2-active-detail.png`
- List views: `B-fa-list-after-A1.png`, `B-fa-list-after-A2.png` (docNo column shows the
  sequential FA numbering)
- Depreciation run #1: `B-fa-dep-run1-before.png`, `B-fa-dep-run1-confirm.png`,
  `B-fa-dep-run1-after.png`, `B-fa-dep-run1-je-detail.png` (JE #155 screenshot, see F-3 below)
- Per-asset run-history after run #1: `B-fa-A1-after-dep-run1.png`, `B-fa-A2-after-dep-run1.png`
- Depreciation run #2 (idempotence): `B-fa-dep-run2-after.png` (**shows the misleading toast**,
  F-1's exact repro)
- Disposal: `B-fa-A2-dispose-modal-filled.png`, `B-fa-A2-disposed-detail.png`,
  `B-fa-A2-disposal-je-detail.png` (JE #157 screenshot)
- TB checkpoints (6 total): `B-fa-tb-00-baseline.png` through `B-fa-tb-05-after-disposal.png`,
  plus a follow-up `B-fa-tb-06-asof-monthend.png` (as-of 2026-07-31, see F-3)

## Hand-calc vs JE table

Straight-line: monthly = (cost − residual) / life.

| Asset | Cost | Residual | Life | Hand-calc monthly | JE line amount | Match |
|---|---|---|---|---|---|---|
| A1 (07-2026-FA-0001) | 120,000 | 0 | 36mo | 3,333.33 | 3,333.33 (in JE #155, combined) | ✓ exact |
| A2 (07-2026-FA-0002) | 6,000 | 0 | 12mo | 500.00 | 500.00 (in JE #155, combined) | ✓ exact |
| **Run #1 total (2 assets)** | | | | **3,833.33** | JE #155: Dr 5450 ค่าเสื่อมราคา ฿3,833.33 = Cr 1690 ค่าเสื่อมราคาสะสม ฿3,833.33 | ✓ exact, Dr=Cr |

Disposal hand-calc (asset 2, after 1 month depreciation):
- NBV at disposal = cost − accumDep = 6,000 − 500 = **5,500.00**
- Gain/(loss) = proceeds − NBV = 3,000 − 5,500 = **(2,500.00)** (a loss, deliberately chosen to
  exercise the loss line, not just a gain)
- JE #157 actual: Dr 1110 เงินสด 3,000.00 / Dr 1690 ค่าเสื่อมราคาสะสม 500.00 (reversal) /
  Cr 1610 อุปกรณ์และเครื่องใช้สำนักงาน 6,000.00 / Dr 5460 ขาดทุนจากการจำหน่ายสินทรัพย์ 2,500.00.
  Total Dr 6,000.00 = Total Cr 6,000.00. **Matches hand-calc exactly**, both the loss amount and
  the accum-dep reversal.

## TB (Dr=Cr) after each step

| Checkpoint | Badge | Totals (Dr / Cr) |
|---|---|---|
| Baseline (before any FA activity) | Dr = Cr ✓ | 110,870.50 / 110,870.50 |
| After A1 activate | Dr = Cr ✓ | 110,870.50 / 110,870.50 (unchanged — activation posts no JE, see F-2) |
| After A2 activate | Dr = Cr ✓ | 110,870.50 / 110,870.50 (unchanged, same reason) |
| After dep run #1 (as of TODAY 2026-07-22) | Dr = Cr ✓ | 110,870.50 / 110,870.50 (unchanged — see F-3, JE excluded by as-of date) |
| After dep run #2 (idempotence) | Dr = Cr ✓ | 110,870.50 / 110,870.50 |
| After disposal | Dr = Cr ✓ | 116,870.50 / 116,870.50 (+6,000.00, the disposal JE's own total) |
| **Follow-up: as of 2026-07-31 (month-end)** | Dr = Cr ✓ | 268,203.83 / 268,203.83 — **with the dep JE now visibly included**: row 5450 debit 3,833.33, row 1690 debit 500.00/credit 3,833.33 |

Trial balance tied (Dr=Cr) at every single checkpoint, including the follow-up check that
actually contains the depreciation JE. No imbalance anywhere.

## Idempotence result

**PASS at the data layer — no double-post.** Depreciation run #2 for the identical period
(2026-07) left the past-runs table at exactly 1 row (both before and after), and the backend's
belt-and-braces check (`FixedAssetService.GenerateDepreciationAsync`, existing-row lookup on
`(company, year, month)`) returned the SAME existing run with no new JE. Confirmed independently
by TB staying byte-identical across the retry.

## Findings

**F-1 — MEDIUM (UX correctness, not data integrity): depreciation re-run shows a false "success"
toast instead of "already posted".**
- Repro: run depreciation for a period that's already posted (exactly what I did for run #2).
  The UI shows the generic **success** toast "บันทึกค่าเสื่อมราคาสำหรับ กรกฎาคม 2026 แล้ว" (green
  checkmark, "Depreciation for July 2026 recorded") — the SAME toast as a genuinely NEW post —
  even though nothing new was posted. Screenshot: `B-fa-dep-run2-after.png`.
- Root cause (confirmed by reading `FixedAssetService.cs` + `frontend/app/(dashboard)/depreciation/page.tsx`
  + `frontend/lib/queries.ts`): the backend's idempotent-retry path returns HTTP 200 with
  `DepreciationRunResult.AlreadyExisted = true` (`FixedAssetDtos.cs`/`lib/types.ts` both carry
  `alreadyExisted: boolean`), but the frontend's `handleGenerate()` never reads that field — it
  only branches on `res.depreciationRunId == null` (→ "no assets due") vs. not-null (→ always
  shows the generic success toast). The i18n key `alreadyPosted` ("มีการคิดค่าเสื่อมราคาสำหรับ
  เดือนนี้แล้ว") and its matching `catch (e) { if (e instanceof ApiError && e.code ===
  'depreciation.already_posted') ... }` branch DO exist in the code, but that error is only ever
  thrown by a genuine CONCURRENT race (`DbUpdateConcurrencyException` / Postgres `23505` on the
  unique `(company,year,month)` index) — a sequential retry (the realistic "did I already run
  this?" scenario a user hits) never reaches that catch block at all, so that string is
  effectively dead for the common case.
- Impact: cosmetic/trust issue, not a money bug — no double-posting occurs. But an accountant
  re-running depreciation "just to be sure" gets no signal that nothing happened, which could
  mask a genuine failure to post in a different month.
- Suggested minimal fix (not applied — read-only army leg): in `handleGenerate()`, branch on
  `res.alreadyExisted` before the null check and show `t('alreadyPosted')` in that case.

**F-2 — Design note, not a defect (self-documented in code): asset acquisition posts no GL entry,
so an asset registered without a linked Vendor Invoice never appears on the balance sheet — yet
Dispose/WriteOff unconditionally credit its full registered cost anyway.**
- `FixedAssetService.cs` line ~16 and the `ActivateAsync` comment are explicit: "Acquisition
  posts NO journal entry — the linked Vendor Invoice already booked the cost" (or an
  "opening-balance JE" the accountant is expected to post separately). This is a deliberate
  module boundary (fixed-asset register = depreciation subledger; GL entry for the cost is
  someone else's job), not a bug.
- However: A1's recon already found the "ใบกำกับภาษีซื้อ" (Vendor Invoice) picker on
  `/fixed-assets/new` had nothing to link (0 invoices available) — so on a fresh company, EVERY
  asset registered through the normal UI flow will have this gap by default, with **no warning
  anywhere in the UI** that the cost is invisible on the balance sheet until a separate manual JE
  is posted.
- Consequence confirmed live: asset 2's disposal JE (#157, screenshot `B-fa-A2-disposal-je-detail.png`)
  credited account `1610 อุปกรณ์และเครื่องใช้สำนักงาน` by the full 6,000 cost — but nothing in this
  test ever debited that account for asset 2's acquisition (activation posts nothing, per design).
  In this run it didn't produce a visibly broken balance only because 1610 already carried
  unrelated pre-existing activity (11,000 debit / 6,000 credit → net 5,000 debit after my test);
  on a cleaner account this credit-with-no-matching-debit would push the account negative.
- TB Dr=Cr held throughout regardless (any set of individually-balanced JEs is tautologically
  Dr=Cr in aggregate) — this is a semantic/per-account correctness gap, not a global ledger
  imbalance, and the gate's Dr=Cr check would never catch it on its own.
- Not filing as a bug per the mission's unbuilt-vs-untested split — this is BUILT behavior,
  intentionally scoped, just under-guarded in the UI. Flagging for the fix-arc triage to decide
  whether it needs a "cost not yet on GL" warning badge on the asset detail page.

**F-3 — Testing note, not a product bug: "TB as of today" silently excludes the current month's
depreciation JE.**
- Depreciation always posts dated at month-END (`runDate = DateOnly(year, month, DaysInMonth(...))`
  — 2026-07-31 for a July run), regardless of what day it's actually run on. A trial balance
  report "as of today" (2026-07-22, the default) is correctly date-filtered and therefore does
  NOT show that JE until the calendar reaches month-end — this is why the TB totals looked
  unchanged after both depreciation runs in the table above.
- This is correct report behavior, not a defect — but it's an easy trap for any tester (or
  accountant) glancing at "TB unchanged after I just posted depreciation" and wrongly concluding
  something failed to post. Verified the JE genuinely is in the ledger by re-running the TB with
  `asOf = 2026-07-31`: the badge stays Dr=Cr ✓ and the JE's accounts (5450, 1690) now show the
  expected 3,833.33. Screenshot: `B-fa-tb-06-asof-monthend.png`.
- Flag for future army legs / manual QA: always check TB **as of the transaction's own date**,
  not just "today," when verifying a future-or-period-end-dated posting.

## Unbuilt-vs-untested classification
Everything in this leg's scope was BUILT and worked: registration, free-text category, GL
account override/default, activation + numbering, straight-line depreciation with correct
final-month plugging logic (read in code, not exercised here since neither asset reached its
final month), idempotent re-run (data-correct, UX-misleading per F-1), disposal with gain/loss +
accum-dep reversal. Nothing in this leg was found unbuilt. Write-off path (`useWriteOffFixedAsset`)
exists in code and UI but was NOT driven (only Dispose was, per the mission's "sale or write-off
path, whatever the UI offers" — sale/dispose was picked since it exercises both the VAT-notice
field and the proceeds/gain-loss math) — flag as untested-but-built if a future leg wants full
write-off coverage too.

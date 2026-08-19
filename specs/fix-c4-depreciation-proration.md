# C4 — Fixed-asset depreciation: first-period day proration + units-based final charge

<!-- Living document. Implementer updates the checklist in place; a retry uses the SAME
     file and grows the attempt log. Designer: opus-designer, 2026-08-19. -->

## 0. Headline

Ham greenlit changing the two behaviours r2 flagged as design notes
(`findings-r2/findings-leg3.md` L3-2 / L3-3):

1. **No first-month proration** — a mid-month acquisition charges a FULL month.
2. **Final-month "plug"** — the last *calendar-scheduled* month silently absorbs every
   skipped/never-run month (asset C: 2 run-lines instead of 3, the last one 66.67 instead of 33.33).

Both are fixed by ONE change of the schedule's index: **stop indexing the schedule by the
CALENDAR and index it by MONTHS-OF-LIFE ALREADY CHARGED ("units")**, then prorate the first
unit by days held.

The single most important discovery: **the exactness plug and the proration are the same
mechanism.** Today's plug fires on "is this the calendar month `start + L - 1`?" — that test is
what absorbs skipped months. Replace it with "did this charge take the asset's cumulative units
to `L`?" and (a) the sum-to-exact invariant is preserved *by construction*, (b) skipped months
can no longer be absorbed anywhere, (c) a prorated first unit falls out for free, and
(d) **the two existing exact-value tests and the year-end test still pass with ZERO edits**
(they all start on day 1 → fraction = 1.0000 → identical arithmetic to today). If the
implementer has to touch `Depreciation_full_life_ties_out_to_the_satang`,
`Depreciation_undershoot_plug_closes_life_at_exactly_24_months`, or
`YearEnd_close_sweeps_5450_...`, the implementation is WRONG — stop and re-read §3.

Second discovery, load-bearing for the migration: `fixedasset.fixed_assets` is
`FORCE ROW LEVEL SECURITY` with `USING (company_id = NULLIF(current_setting('app.company_id',
true), '')::INT)`. At API startup no GUC is set → `company_id = NULL` → **an UPDATE backfill in
the migration would silently touch ZERO rows in prod while passing on superuser test DBs**.
The design therefore carries **no migration DML at all** (§3.4).

---

## 1. Facts established in code (VERIFIED — file:line)

### 1.1 The engine as it stands
- `backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs:309-429`
  `GenerateDepreciationAsync(year, month)`:
  - `:312` `runDate = last day of (year, month)`; `:314` `period.EnsureOpenAsync(runDate)` — FA-D
    closed-period refusal (**r2-verified green, do not touch**).
  - `:317-321` idempotent early return on an existing `DepreciationRuns` row for (year, month)
    → returns the SAME `JournalEntryId` with `AlreadyExisted: true` (**r2-verified green, do not
    touch**).
  - `:331-336` eligible assets = `Status == Active && DepreciationStartDate <= runDate &&
    AccumulatedDepreciation < DepreciableBase`, ordered by id.
  - `:345-353` **the code being replaced**:
    ```csharp
    var remaining = asset.DepreciableBase - asset.AccumulatedDepreciation;
    var (finalYear, finalMonth) = AddMonths(asset.DepreciationStartDate, asset.UsefulLifeMonths - 1);
    var isFinalScheduledMonth = year > finalYear || (year == finalYear && month >= finalMonth);
    var charge = isFinalScheduledMonth ? remaining : Math.Min(asset.MonthlyAmount, remaining);
    if (charge <= 0m) continue;
    ```
    `isFinalScheduledMonth` is the *calendar* test = L3-3's silent absorber. `>= finalMonth`
    means every month at-or-after the scheduled end plugs the whole remaining balance.
  - `:356-367` `AccumulatedDepreciation += charge`, `Version++`, one `DepreciationRunLine`
    (`Amount`, `AccumulatedAfter`), Dr/Cr totals bucketed per account.
  - `:370-379` zero lines → **no run row, no JE**, returns `AssetCount: 0` (see the trap analysis
    in §3.2 — this is why the design must never produce a zero charge for an eligible asset).
  - `:405-425` posts via `gl.PostManualEntryAsync` (reference `DEP-{yyyy}{MM}`); the unique
    `(company_id, period_year, period_month)` index + the optimistic `Version` check are the race
    backstops (**r2-verified green, do not touch**).
  - `:303-307` `private static AddMonths(...)` — its only caller is the line being deleted.
- `Math.Round(x, 2, MidpointRounding.AwayFromZero)` is the house rounding rule
  (`FixedAssetService.cs:134,172`).

### 1.2 Entity / persistence
- `backend/src/Accounting.Domain/Entities/FixedAsset/FixedAsset.cs:41` `MonthlyAmount` =
  `round(DepreciableBase / UsefulLifeMonths, 2, Away)`, frozen at Activate; its XML comment
  (`:38-40`) documents the calendar plug — **must be updated**.
- `FixedAsset.cs:80-100` `Activate(...)` carries the period-close deadlock guard
  `if (MonthlyAmount == 0m && DepreciableBase > 0m) throw fixed_asset.monthly_amount_zero`.
  **Keep exactly as-is** (§3.3 explains why the new rule does NOT need a second guard here).
- `backend/src/Accounting.Domain/Entities/FixedAsset/DepreciationRunLine.cs:18-19` comment cites
  the plug — **must be updated**.
- `backend/src/Accounting.Infrastructure/Persistence/Configurations/FixedAsset/FixedAssetConfiguration.cs:20-37`
  — `ToTable("fixed_assets", "fixedasset")`, money columns `HasPrecision(19,4)`; snake_case column
  names come from a global convention (no `HasColumnName` anywhere in this file).

### 1.3 The period-close hook (the thing a bad design turns into a trap)
- `backend/src/Accounting.Infrastructure/Ledger/PeriodCloseService.cs:70-84`:
  ```csharp
  var depreciationDue = await _db.FixedAssets.AnyAsync(a =>
      a.Status == FixedAssetStatus.Active
      && a.DepreciationStartDate <= to
      && a.AccumulatedDepreciation < a.DepreciableBase, ct);
  if (depreciationDue) { var runPosted = await _db.DepreciationRuns.AnyAsync(r =>
        r.PeriodYear == year && r.PeriodMonth == month && r.Status == Posted, ct);
    if (!runPosted) throw new DomainException("period.depreciation_required", ...); }
  ```
  **A month with any not-fully-depreciated active asset CANNOT be closed until a run row exists
  for that month.** A run row only exists if at least one asset produced a charge > 0
  (`FixedAssetService.cs:370-379`). This is the guard whose exit the design must protect.
  `PeriodCloseService.cs` is **NOT edited by this work package**.

### 1.4 RLS (footgun — verified, not inferred)
- `backend/src/Accounting.Infrastructure/Migrations/SqlScripts/619_fixed_assets_rls.sql:7-14`:
  `ENABLE` + **`FORCE ROW LEVEL SECURITY`** on `fixedasset.fixed_assets` (same for
  `depreciation_runs`, `depreciation_run_lines`), policy
  `USING (company_id = NULLIF(current_setting('app.company_id', true), '')::INT)`, **no bypass
  arm** (G1 table).
- Consequence, pinned: at API startup `DbInitializer` runs EF migrations with **no
  `app.company_id` GUC set** → the policy predicate evaluates to NULL → a migration `UPDATE`/
  `SELECT` over `fixed_assets` sees **zero rows**, silently, and `FORCE` means even the table
  owner is subject. Prod's app role is NOBYPASSRLS (memory: *RLS masked by superuser tests*);
  `teas_test`/`accounting_dev` connect as a **superuser**, which bypasses RLS entirely → a
  backfill would look green in tests and no-op in prod. **This design writes no migration DML**
  (§3.4). The runtime engine's reads/writes are fine: they run inside the request/tenant session
  where `app.company_id` IS set.
- No seed script or dev-tool inserts fixed assets (`grep -rln "fixed_assets|depreciation_run"
  SqlScripts/ db/ dev-tools/ scripts/` → only `619_fixed_assets_rls.sql` plus three
  number-sequence/view scripts). Nothing else creates run lines.

### 1.5 troubles-wiki.md entries that apply to THIS task (do not rediscover)
- **`troubles-wiki.md:210-232` — "Regenerating an already-applied EF migration (ef remove + add)
  leaves teas_test stuck: relation already exists"**: once `dotnet test` has applied your new
  migration to the shared `teas_test`, do **NOT** `ef migrations remove` + `add` again.
  Hand-edit the generated migration file instead. If you already did it, recovery is
  `DROP SCHEMA ... CASCADE` + `DELETE FROM sys.__ef_migrations WHERE migration_id LIKE '%_<Name>'`.
- **`troubles-wiki.md:768-772`** — this repo's migrations-history table is **`sys.__ef_migrations`**
  (snake_case columns), NOT `__EFMigrationsHistory`. Any deploy probe must query `sys.__ef_migrations`.
- **Memory: migration squash + teas_test reset** — `PostgresFixture` owns the migrations history;
  the test DB must be left EMPTY (never `dotnet ef database update` it by hand).
- **Memory: `TEAS_TEST_PG` is per-shell** — set it in the SAME PowerShell call as `dotnet test`,
  and check the skip count: a suite that "passes" with everything skipped proves nothing
  (`Skip.If(_fx.SkipReason ...)` is on every test in this file).
- **FOOTGUN 5 (documented at `FixedAssetServiceTests.cs:24-26`)** — test dates are ALWAYS
  today/future; `PostgresFixture`'s seed closes the previous month relative to `CURRENT_DATE`.

### 1.6 Existing tests (exact-value assertions — enumerated, not left to discovery)
`backend/tests/Accounting.Api.Tests/FixedAsset/FixedAssetServiceTests.cs`

| line | test | asserts money? | start date | effect of this change |
|---|---|---|---|---|
| 84 | `Depreciation_full_life_ties_out_to_the_satang` | YES — `1388.89` ×35 then `1388.85`; accum `50000.00`; month 37 `AssetCount 0` | day **1** of current month | **PASSES UNCHANGED** (f = 1.0000) — this is the regression proof. Only the `"...final-scheduled-month plug..."` *because*-string may be reworded. |
| 131 | `Depreciation_undershoot_plug_closes_life_at_exactly_24_months` | YES — `2083.33` ×23 then `2083.41`; accum `50000.00`; month 25 `AssetCount 0` | day **1** | **PASSES UNCHANGED** (f = 1.0000). |
| 172 | `Activate_posts_no_journal_entry` | no | `Today` | unaffected |
| 195 | `Activate_throws_when_monthly_amount_rounds_to_zero` | no (asserts code `fixed_asset.monthly_amount_zero`) | `Today` | unaffected — guard untouched |
| 214 | `GenerateDepreciation_called_twice_is_idempotent` | no (asserts run/JE counts = 1) | `Today` | unaffected; charge stays > 0 (M = 2000 → worst case f = 1/31 → 64.60) |
| 242 | `GenerateDepreciation_concurrent_calls_post_exactly_one_run` | no | `Today` | unaffected (M = 833.33 → worst case 26.88) |
| 277 | `PeriodClose_hook_blocks_then_allows_close_around_the_depreciation_run` | no | `Today` | unaffected (M = 1041.67 → worst case 33.60 > 0 → run row created) |
| 300 | `PeriodClose_hook_allows_close_when_no_assets_are_due` | no | — | unaffected |
| 328 / 367 / 401 | `Dispose_gain_…` / `Dispose_loss_…` / `WriteOff_…` worked examples | YES (disposal JEs) | accum patched directly by `SeedDisposableAssetAsync:307-320` | unaffected — no depreciation run happens after the patch |
| 438 / 460 / 476 | disposal date-order guards | no | — | unaffected |
| 493 | `YearEnd_close_sweeps_5450_but_ProfitLoss_still_shows_the_depreciation_expense` | YES — `NetProfit -12000.00`, `Expense 12000.00` (12 × 1000.00) | **Jan 1** of last year | **PASSES UNCHANGED** (f = 1.0000) |
| 548-698 | tenant scope / account-type / vendor-invoice / concurrency | no | — | unaffected |
| — | `FixedAssetPermissionTests.cs` | no | — | unaffected |

**No other suite and no live Playwright spec touches depreciation amounts** — the only e2e hits
are `findings-r2/artifacts/r2-leg3-fa-*.spec.ts`, archived r2 throwaways that are not part of the
suite (do not edit them). No API-contract/OpenAPI snapshot test pins `FixedAssetDetail`.

---

## 2. Consumer sweep — the seam is "how a month's charge is computed"

| consumer (file:line) | what it does with the seam | disposition |
|---|---|---|
| `FixedAssetService.cs:345-353` (engine) | computes charge from the calendar plug | **REPLACE** (§3.2) |
| `FixedAssetService.cs:303-307` `AddMonths` | only caller is the line above | **DELETE** (dead after the change) |
| `FixedAssetService.cs:370-379` zero-line early return | no run row when nothing was charged | **KEEP** + protected by the ≥ 0.01 floor invariant I4 |
| `PeriodCloseService.cs:70-84` hook | "any active asset with accum < base ⇒ a run row must exist for this month" | **UNCHANGED** — design must keep every eligible asset producing a line (I4) |
| `FixedAsset.Activate` guard `FixedAsset.cs:92-94` | blocks `MonthlyAmount == 0` | **UNCHANGED** (§3.3) |
| `FixedAsset.MonthlyAmount` XML `FixedAsset.cs:38-40` | says the final month is a calendar plug | **EDIT** (doc) |
| `DepreciationRunLine.Amount` XML `:18-19` | same stale claim | **EDIT** (doc) |
| `FixedAssetService.GetDetailAsync:478-499` → `FixedAssetDetail` | asset detail incl. run-line history | **EXTEND** — expose `MonthsDepreciated` (answers L3-3: run-line count is no longer needed as a proxy) |
| `FixedAssetService.GetRegisterReportAsync:504-533` | sums run-line `Amount` as-of a date | **NO CHANGE** — still sums real lines |
| `FixedAssetService.GetAccumulatedDepreciationReportAsync:535-552` | per-asset monthly charges for a calendar year | **NO CHANGE** — a prorated first month simply shows a smaller figure; an `L+1`-month schedule just puts one more charge in the final year |
| `FixedAssetService.GetDepreciationRunAsync:441-455` | run detail lines | **NO CHANGE** |
| `FixedAssetService.CreateDraftAsync:134` / `UpdateDraftAsync:172` | compute `MonthlyAmount` | **NO CHANGE** — `MonthlyAmount` keeps its meaning (steady-state month) |
| `TeasMcpTools.cs:1983-2052` (`create_fixed_asset_draft`, `update_fixed_asset_draft`, `get_fixed_asset`, register + accumulated-depreciation reports, `list_depreciation_runs`) | read-only or draft-only; `GenerateDepreciationAsync` deliberately NOT exposed (`:1976-1978`) | **NO CHANGE** — the added DTO field is additive |
| `frontend/lib/types.ts:1663-1667` `FixedAssetDetail` | hand-written TS interface | **DEFER** (structural typing ignores an extra JSON field; no runtime break) → §8 |
| `frontend/app/(dashboard)/fixed-assets/[id]/page.tsx:157` | shows `monthlyAmount` + run-line table | **DEFER** — displaying "months depreciated" / a prorated-first-month hint is a FE follow-up |
| `frontend/components/forms/FixedAssetForm.tsx:64,182` | client-side `monthlyAmountPreview` = base/life | **DEFER** — still correct as the *steady-state* preview; it does not claim to be month 1 |
| `frontend/app/(dashboard)/depreciation/page.tsx` | run trigger + history table (totals only) | **NO CHANGE** |
| `frontend/app/(dashboard)/tax-filings/cit/page.tsx:115,171` | `s.depreciation` comes from the P&L summary, not FA internals | **NO CHANGE** |
| `findings-r2/artifacts/r2-leg3-fa-*.spec.ts` | archived r2 throwaway probes | **DO NOT EDIT** |

---

## 3. Design

### 3.1 The binding rule (Thai practice) — DECIDED

> **First-period depreciation is prorated by DAYS, expressed as a fraction of the calendar month
> in which depreciation starts, counting the start day itself.**
> `f = round(daysHeldInStartMonth / daysInStartMonth, 4, AwayFromZero)`, where
> `daysHeldInStartMonth = DaysInMonth(start) - start.Day + 1`.
> A full month of *use* is one **unit**; an asset receives exactly `UsefulLifeMonths` units of
> depreciation in total, so a mid-month acquisition spans `L + 1` calendar months
> (a partial first month + a partial last month = one whole month).

**Legal grounding.** ประมวลรัษฎากร **มาตรา 65 ทวิ (2)** requires ค่าสึกหรอและค่าเสื่อมราคา to be computed
ตามหลักเกณฑ์ของ **พระราชกฤษฎีกา (ฉบับที่ 145) พ.ศ. 2527**, whose มาตรา 4 requires that an asset acquired
*during* a period be depreciated **"ตามส่วนเฉลี่ยแห่งระยะเวลาที่ได้ทรัพย์สินนั้นมา"** — in the Revenue
Department's own worked examples and in universal Thai SME practice this is applied **by days**
(`ราคาทุน × อัตรา × จำนวนวันที่ได้ทรัพย์สินมา / 365`). Day-count is therefore the Thai-correct convention;
the Thai SME packages our users come from (Express, FlowAccount, PEAK) all prorate the acquisition
period by days.

**Rejected alternatives (do not relitigate):**
- *Half-month / mid-month convention* — a US MACRS construct with **no basis in Thai law**; it
  produces a number no Thai auditor can tie back to พ.ร.ฎ.145.
- *`days / 365` against an annual rate* — that is the shape of the **annual tax computation**;
  this app's useful life is expressed in **months** (`UsefulLifeMonths`), so a /365 denominator
  would introduce a second, conflicting definition of "a month of life". The month-fraction form
  is day-exact within the month, keeps `MonthlyAmount` meaningful, and differs from the /365
  figure by at most ~1.5 days of expense — an ordinary บัญชี-vs-ภาษี reconciling item, not an error.
- *Full daily accrual across the whole life* (`base / totalDays`) — would change EVERY month's
  amount, void `MonthlyAmount`, and blow the blast radius. Rejected.

### 3.2 The engine — units-indexed schedule (REPLACES the calendar plug)

Replace `FixedAssetService.cs:345-353` with exactly this shape (this fragment is the money
formula; type it as written):

```csharp
var remaining = asset.DepreciableBase - asset.AccumulatedDepreciation;
var life = (decimal)asset.UsefulLifeMonths;

// Units of LIFE already charged to this asset. NULL = a row that predates this feature (or
// predates its own first charge) -> derive from its posted run-line count, where every legacy
// line is one full month by definition (no proration existed). See §3.4 — this is why the
// migration needs no DML (RLS would have silently no-op'd a backfill in prod).
var unitsBefore = asset.MonthsDepreciated ?? priorLineCounts.GetValueOrDefault(asset.FixedAssetId, 0m);

// First charge is day-prorated (§3.1); every later charge is one whole month; the last one is
// trimmed so the total is EXACTLY `life` units — that trim is what replaces the old calendar
// plug, and it can never absorb a skipped month because it is bounded by one unit.
var delta = Math.Min(unitsBefore == 0m ? FirstMonthFraction(asset.DepreciationStartDate) : 1m,
                     life - unitsBefore);
var unitsAfter = unitsBefore + delta;

// The FINAL charge is the remaining balance -> sum-to-exact holds by construction, in BOTH
// rounding directions, with no dribble month and no multi-month lump.
var charge = unitsAfter >= life
    ? remaining
    : Math.Round(asset.MonthlyAmount * delta, 2, MidpointRounding.AwayFromZero);

// I4 — an eligible asset must never produce a 0.00 charge: a run with no lines creates no run
// row, and PeriodCloseService's hook would then refuse to close that month forever (no in-app
// exit). Self-correcting: the final charge absorbs the satang.
if (charge < 0.01m && remaining >= 0.01m) charge = 0.01m;
charge = Math.Min(charge, remaining);
if (charge <= 0m) continue;

asset.MonthsDepreciated = unitsAfter;
```
…then the existing `:356-367` block (accumulate, `Version++`, run line, account buckets) is
unchanged.

`FirstMonthFraction` — add as a `public static` on the entity (`FixedAsset.cs`) so the domain owns
the rule:
```csharp
/// <summary>§3.1 — ป.รัษฎากร ม.65 ทวิ(2) + พ.ร.ฎ.145 ม.4: the acquisition period is depreciated
/// ตามส่วนเฉลี่ยแห่งระยะเวลาที่ได้ทรัพย์สินนั้นมา, by DAYS, counting the start day itself.
/// 4 dp because months_depreciated is numeric(9,4) — the stored units and the charged units must
/// be the SAME number, or the schedule and the ledger drift.</summary>
public static decimal FirstMonthFraction(DateOnly start) =>
    Math.Round((decimal)(DateTime.DaysInMonth(start.Year, start.Month) - start.Day + 1)
               / DateTime.DaysInMonth(start.Year, start.Month), 4, MidpointRounding.AwayFromZero);
```

`priorLineCounts` — one grouped query, immediately after the eligible-asset load
(`FixedAssetService.cs:336`), inside the same transaction and tenant session (so RLS and the
global query filters are satisfied):
```csharp
var legacyIds = assets.Where(a => a.MonthsDepreciated is null).Select(a => a.FixedAssetId).ToList();
var priorLineCounts = legacyIds.Count == 0
    ? new Dictionary<long, decimal>()
    : await db.DepreciationRunLines.AsNoTracking()
        .Where(l => legacyIds.Contains(l.FixedAssetId))
        .GroupBy(l => l.FixedAssetId)
        .Select(g => new { g.Key, N = (decimal)g.Count() })
        .ToDictionaryAsync(x => x.Key, x => x.N, ct);
```
Self-healing: after an asset's first post-deploy charge its `MonthsDepreciated` is non-null, so
`legacyIds` empties out and the query stops running.

**Why "sliding" (one unit per run) and NOT calendar catch-up — do not "improve" this back.**
A calendar-anchored schedule (charge up to `f + monthsSinceStart` units) was designed and
**rejected**: run September before August (both open) and August's target is already met → zero
charge → **no run row for August** → `PeriodCloseService`'s `period.depreciation_required`
refuses to close August **forever, with no in-app exit** (you cannot force a run row from the UI).
That is a guard whose state has no exit — the exact failure class the payroll trap taught us.
The units-indexed schedule is order-independent: every run charges one unit to each eligible
asset, so a late run always produces a line and the month always closes.

**The trade-off, stated honestly (this is the one real cost):** a month that is never run is
never retroactively absorbed — the schedule *slides*, so the asset's final charge lands one
calendar month later per skipped month, i.e. expense recognition moves later. The in-app remedy
is to **run the missed month itself** (`POST /depreciation-runs {year, month}` works for any OPEN
period, and the amounts do not change with ordering). Gaps are structurally rare: the
period-close hook already refuses to close a month with un-run depreciation, so the only way to
get one is a `DepreciationStartDate` backdated into an ALREADY-closed period (exactly r2's asset C).

**What L3-3's asset C does now** (cost 100, salvage 0, L = 3, start backdated into a closed month):
`[33.33, 33.33, 33.34]` over three runs, ending one month later — instead of today's
`[33.33, 66.67]`. Run-line count now equals units charged, and `MonthsDepreciated` states it
outright.

### 3.3 Why no new refusal is added (guard/exit discipline)

A prorated first charge can round to `0.00` for an absurdly small asset (`MonthlyAmount` 0.01,
`f` 0.3871 → 0.0039). Today that is impossible (`min(M, remaining)` with the Activate guard
`M != 0`), and a zero charge would remove the asset's line → possibly no run row → the
period-close hook traps the month. Two fixes were considered:
- *Extend the `Activate` guard to reject a prorated-zero first charge* — **rejected**: it does
  nothing for rows already Active before deploy (the guard only runs at Activate), so the trap
  would still exist in prod, and it adds a refusal.
- **CHOSEN: the ≥ 0.01 floor in the engine (I4).** Covers legacy and new rows in one place, adds
  no refusal, no error code, no FE/i18n work, and cannot break exactness (the final charge is
  `remaining`, which absorbs the satang). **`FixedAsset.Activate`'s existing
  `monthly_amount_zero` guard stays exactly as it is.**

### 3.4 Schema — one nullable column, DDL only, NO DML

`fixedasset.fixed_assets` += `months_depreciated numeric(9,4) NULL`.

- Entity `FixedAsset.cs`: `public decimal? MonthsDepreciated { get; set; }` with an XML comment:
  units (months of useful life) already charged; fractional when the first month was prorated;
  **NULL = not yet recorded → derived from posted run lines at run time**; never edited by hand.
- `FixedAssetConfiguration.cs`: `b.Property(x => x.MonthsDepreciated).HasPrecision(9, 4);`
  (no `HasDefaultValue` — NULL is meaningful, and a DB default would destroy the legacy
  discriminator).
- Migration name: **`FixedAssetMonthsDepreciated`**. Body: `AddColumn<decimal>` / `DropColumn`
  only. **Hand-verify the generated `Up()` contains no `Sql(...)` / `UpdateData`.**
- **RLS pin (§1.4):** at startup the migration runs with **no `app.company_id`**, against a
  `FORCE ROW LEVEL SECURITY` table with **no bypass arm**, as a **NOBYPASSRLS** role in prod →
  any `UPDATE`/`SELECT` here would silently affect **0 rows** while passing on superuser test DBs.
  `ALTER TABLE ... ADD COLUMN` is DDL and is unaffected by RLS, which is precisely why the legacy
  value is resolved at *run time* (inside a tenant session where the GUC is set) instead of by a
  backfill. **Any implementer instinct to "just backfill it in the migration" is a REJECT.**
- `Down()`: `DropColumn` — safe, since no posted amount depends on the column (it is recomputable
  from run lines).

**Deploy probe (row counts, not exit codes).** Run as a superuser/BYPASSRLS psql session (or
`SET app.company_id` first) — a probe returning 0 under the app role proves RLS filtering, not
absence:
```sql
-- 1 expected: the migration is recorded (custom history table! never __EFMigrationsHistory)
SELECT count(*) FROM sys.__ef_migrations WHERE migration_id LIKE '%FixedAssetMonthsDepreciated';
-- 1 expected: the column exists and is NULLABLE
SELECT count(*) FROM information_schema.columns
 WHERE table_schema='fixedasset' AND table_name='fixed_assets'
   AND column_name='months_depreciated' AND is_nullable='YES';
-- baseline BEFORE the first post-deploy run: legacy_rows = every active asset, migrated_rows = 0
SELECT count(*) FILTER (WHERE months_depreciated IS NULL)     AS legacy_rows,
       count(*) FILTER (WHERE months_depreciated IS NOT NULL) AS migrated_rows
  FROM fixedasset.fixed_assets WHERE status = 'ACTIVE';
-- AFTER the first post-deploy depreciation run: whole-unit rows must equal their run-line count.
-- IN-PROGRESS rows only: a COMPLETED prorated asset legitimately lands on whole units = L with
-- L+1 lines, so without the accumulated < base filter this probe false-alarms on a correct system.
SELECT count(*) AS mismatched FROM fixedasset.fixed_assets a
  WHERE a.months_depreciated IS NOT NULL
    AND a.accumulated_depreciation < a.depreciable_base      -- still depreciating
    AND a.months_depreciated = trunc(a.months_depreciated)   -- whole-unit (legacy) rows only
    AND a.months_depreciated <> (SELECT count(*) FROM fixedasset.depreciation_run_lines l
                                  WHERE l.fixed_asset_id = a.fixed_asset_id);   -- expect 0
```
Prod deploy is manual over plink and new SqlScripts run at API startup → **DB backup before
deploy is mandatory** (memory: *TEAS prod deploy via plink*).

### 3.5 Worked examples (hand-computed, satang-exact — these ARE the test expectations)

**(a) Mid-month acquisition, 31-day month.** cost 50,000.00, salvage 0, L = 36, start **day 5**
of a 31-day month.
`M = round(50000/36, 2, Away) = 1388.89`; `daysHeld = 31-5+1 = 27`;
`f = round(27/31, 4, Away) = 0.8710`.

| charge # | delta | units after | amount |
|---|---|---|---|
| 1 | 0.8710 | 0.8710 | `round(1388.89 × 0.8710, 2) =` **1209.72** |
| 2–36 (35×) | 1 | 35.8710 | **1388.89** each (= 48,611.15) |
| 37 (final) | 0.1290 → units 36.0000 ≥ 36 | 36.0000 | `remaining = 50000 − 1209.72 − 48611.15 =` **179.13** |

Total **50,000.00 exactly**, 37 lines. (Cross-check: `0.1290 × 1388.89 = 179.17`; the 0.04
difference is the `MonthlyAmount` rounding residue, absorbed by the final charge — the same job
the old plug did, now bounded by one unit instead of unbounded.)

**(b) Mid-month + undershoot rounding, 30-day month.** cost 50,000.00, salvage 0, L = 24, start
**day 16** of a 30-day month. `M = round(50000/24, 2, Away) = 2083.33`; `f = round(15/30, 4) = 0.5000`.
Charge 1 = `round(2083.33 × 0.5, 2, Away)` = **1041.67**; charges 2–24 = 2083.33 × 23 = 47,916.59
(units 23.5000); charge 25: `delta = 0.5000` → units 24.0000 → final → `50000 − 1041.67 −
47916.59 =` **1041.74**. Total **50,000.00 exactly**, 25 lines. (The undershoot drift that
`Depreciation_undershoot_plug_...` pins at month 24 is now absorbed by the *units*-final charge.)

**(c) Skipped month (L3-3 asset C shape).** cost 100.00, salvage 0, L = 3, start day 1
(f = 1.0000), **the start month's run never happens**. Runs in months 2, 3, 4 charge
**33.33 / 33.33 / 33.34** (units 1 → 2 → 3; the third is the units-final charge).
Total **100.00 exactly**, **3** lines. Today the same asset yields **2** lines `33.33 / 66.67` —
month 3 is `start + L − 1`, so the calendar plug dumps the whole remaining balance. This pair of
numbers is the RED→GREEN discriminator for T3.

**(d) Regression, day-1 start (f = 1.0000).** 50,000 / 36 → `1388.89 × 35` + **1388.85**;
50,000 / 24 → `2083.33 × 23` + **2083.41**; 12,000 / 12 → `1000.00 × 12`. **Identical to today** —
which is why the three existing money tests must pass untouched.

---

## 4. Invariants (each → its proving test)

- **I1 — Sum-to-exact.** Over an asset's life, Σ(run-line amounts) = `Cost − SalvageValue`
  **exactly** (satang-exact; house rounding is `Math.Round(x, 2, MidpointRounding.AwayFromZero)`),
  in both rounding directions and whether or not months were skipped. Guaranteed structurally:
  the charge that takes cumulative units to `UsefulLifeMonths` **is** `remaining`.
  → **T1, T2, T3, T5**
- **I2 — Nothing posted is recomputed.** No posted depreciation JE, no `journal_lines` row, and no
  existing `AccumulatedDepreciation` / `DepreciationRunLine.Amount` value is altered by this work.
  The new rule applies to **runs executed after deploy only**. Assets mid-life at deploy keep
  charging whole months (their `MonthsDepreciated` resolves from their posted line count) and
  finish on their existing trajectory — **there is no retro-recompute and no migration DML**.
  Proration applies only where cumulative units resolve to 0, i.e. **no depreciation has ever been
  posted for that asset**. → **T4**
- **I3 — `AccumulatedDepreciation ≤ DepreciableBase` always** (FA-B), preserved by
  `charge = Math.Min(charge, remaining)`. → **T1, T2, T5**
- **I4 — Every asset the run selects produces a charge ≥ 0.01**, so a month that owes depreciation
  always yields a run row and `period.depreciation_required` always has an exit. → **T5**
- **I5 — Untouched guards.** `EnsureOpenAsync`'s closed-period refusal, the idempotent early return
  (same `JournalEntryId`, `AlreadyExisted: true`), the unique
  `(company_id, period_year, period_month)` index and the `Version` optimistic check behave exactly
  as before; `PeriodCloseService` and `FixedAsset.Activate` are not modified.
  → **existing tests at lines 214, 242, 277, 195 (must stay green, unedited)**
- **I6 — Steady state still equals `MonthlyAmount`.** Every non-first, non-final charge is exactly
  the stored `MonthlyAmount`, so the FE preview and the asset detail stay honest.
  → **T1, plus the two existing money tests passing unchanged**

---

## 5. Requirements checklist

### WP-1 — schema (do FIRST; everything else compiles against it)
- [x] `backend/src/Accounting.Domain/Entities/FixedAsset/FixedAsset.cs` — added
      `public decimal? MonthsDepreciated { get; set; }` + XML comment per §3.4; added
      `public static decimal FirstMonthFraction(DateOnly start)` per §3.2. `Activate` untouched.
      Done: builds clean (isolated `-o` build, 0 warnings/0 errors — see gate evidence below).
- [x] `backend/src/Accounting.Infrastructure/Persistence/Configurations/FixedAsset/FixedAssetConfiguration.cs`
      — `b.Property(x => x.MonthsDepreciated).HasPrecision(9, 4);` added (no default value).
- [x] Migration `FixedAssetMonthsDepreciated` — live API on :5080 locks `Accounting.Api`'s bin,
      so generated via `dotnet ef migrations add FixedAssetMonthsDepreciated -p
      backend/src/Accounting.Infrastructure -s backend/src/Accounting.Infrastructure` (the
      repo's `AccountingDbContextFactory : IDesignTimeDbContextFactory` was built for exactly
      this — bypasses the Api host build entirely). Verified `Up()` is ONE
      `AddColumn<decimal>("months_depreciated", schema:"fixedasset", table:"fixed_assets",
      type:"numeric(9,4)", nullable:true)`, `Down()` is one `DropColumn` — no `Sql(`/`UpdateData`
      anywhere. Snapshot regenerated (`AccountingDbContextModelSnapshot.cs` now carries
      `MonthsDepreciated` / `months_depreciated`). Files:
      `20260819125540_FixedAssetMonthsDepreciated.cs` /
      `20260819125540_FixedAssetMonthsDepreciated.Designer.cs`.

### WP-2 — engine (depends on WP-1)
- [x] `backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs` — added the
      `priorLineCounts` grouped query after the eligible-asset load; replaced the calendar-plug
      block with the §3.2 fragment verbatim; `asset.MonthsDepreciated = unitsAfter` set before the
      run line is added; **deleted `AddMonths`** (had no other callers). `grep -n
      "isFinalScheduledMonth\|AddMonths"` on the file → zero matches.
- [x] Same file — `GetDetailAsync`'s projection now passes `asset.MonthsDepreciated` as the trailing
      positional arg into `FixedAssetDetail`.
- [x] `backend/src/Accounting.Application/FixedAsset/FixedAssetDtos.cs` — appended
      `decimal? MonthsDepreciated = null` as the LAST positional member of `FixedAssetDetail`.
      Isolated build (0 errors/0 warnings) confirms `GetDetailAsync` is the only construction site
      and no other caller broke.

### WP-3 — docs (same PR, no code impact)
- [x] `FixedAsset.cs:38-40` `MonthlyAmount` comment + `DepreciationRunLine.cs:18-19` `Amount`
      comment — replaced the "final scheduled month plug" wording with the units-final rule.
- [x] `specs/fixed-assets.md` §3.1 step 4 — restated the algorithm (day proration on the first
      unit, units-final charge, no calendar plug; old text kept as marked-superseded history),
      and recorded that a mid-month acquisition spans `L + 1` calendar months.

### WP-4 — tests (RED first: write T1–T5, watch them fail, then implement — or, if implementing
first, `git stash` the src change once and prove they go RED)
- [ ] T1–T5 added to `backend/tests/Accounting.Api.Tests/FixedAsset/FixedAssetServiceTests.cs`
      (§6). Done when: the 5 new tests pass AND lines 84 / 131 / 493 pass **with no edits to their
      assertions** (a reworded `because:` string is the only permitted change).

---

## 6. Test list (all in `FixedAssetServiceTests.cs`, `[SkippableFact]` + `Skip.If(_fx.SkipReason…)`)

Determinism helper (add near `MonthRange`) — FOOTGUN 5 keeps dates today/future, but the worked
examples need a KNOWN month length, so pick the first qualifying future month:
```csharp
private static DateOnly FirstFutureMonthWithDays(int days, int day)
{
    var d = new DateOnly(Today.Year, Today.Month, 1);
    while (DateTime.DaysInMonth(d.Year, d.Month) != days) d = d.AddMonths(1);
    return new DateOnly(d.Year, d.Month, day);
}
```
(31-day months are ≤ 2 months away, 30-day ≤ 3 — always inside the opened period range; open
periods from the START month, not from today.)

- **T1 — `Depreciation_prorates_the_first_month_by_days_and_still_ties_out_to_the_satang`**
  (I1, I3, I6): §3.5(a). `start = FirstFutureMonthWithDays(31, 5)`, cost 50000, salvage 0, L = 36;
  open 38 periods from `start`'s month; run 37 months. Assert charge #1 = **1209.72**, charges
  2–36 = **1388.89**, charge #37 = **179.13**, `AccumulatedDepreciation == 50000.00m`, **37** run
  lines, and the 38th run returns `AssetCount 0` / `DepreciationRunId null`. Assert Dr == Cr on
  every JE.
- **T2 — `Prorated_asset_with_rounded_down_monthly_closes_exactly_on_the_last_unit`** (I1):
  §3.5(b). `start = FirstFutureMonthWithDays(30, 16)`, cost 50000, L = 24. Assert **1041.67**,
  23 × **2083.33**, final **1041.74**, accum `50000.00`, **25** lines.
- **T3 — `Skipped_month_is_not_absorbed_by_the_final_charge`** (I1 — the L3-3 fix): §3.5(c).
  Asset cost 100, salvage 0, L = 3, `start = day 1 of the current month` (f = 1.0000);
  **skip the start month's run**; run months 2, 3, 4. Assert amounts **[33.33, 33.33, 33.34]**,
  exactly **3** run lines, accum **100.00**, and explicitly `.Should().NotBe(66.67m)` on the
  month-3 charge with a because-string naming L3-3. Month 5 → `AssetCount 0`.
- **T4 — `Legacy_asset_with_null_months_depreciated_resolves_units_from_its_posted_lines`** (I2):
  asset cost 100, L = 3, start day 1; run months 1 and 2 (2 lines, accum 66.66); then set
  `months_depreciated = NULL` directly via the DbContext (simulating a row created before this
  feature) **without touching `AccumulatedDepreciation`**; run month 3. Assert the charge is
  **33.34** (units resolved to 2 from the line count → this is the final unit), accum **100.00**
  exactly, **3** lines, and that the asset's `MonthsDepreciated` is now `3`. This is the
  no-backfill / grandfather proof.
- **T5 — `Tiny_asset_whose_prorated_first_charge_rounds_to_zero_still_produces_a_line`**
  (I1, I3, I4): cost **0.36**, salvage 0, L = 36 → `MonthlyAmount = 0.01` (Activate passes);
  `start = FirstFutureMonthWithDays(31, 20)` → `f = 0.3871`, `0.01 × 0.3871 = 0.0039` → floored.
  Assert the run posts, the asset's line amount is **0.01**, `AssetCount 1`,
  `DepreciationRunId != null`, and `PeriodCloseService.CloseAsync(y, m, …)` then **succeeds**
  (the I4 exit).

Not automated / reported honestly: nothing in this WP. (A live Tier-4 leg on a mid-month asset is
recommended at release but is not part of this work package.)

---

## 7. Verification gates

Worker runs, in order, and pastes output into the attempt log:
1. `dotnet build backend/TEAS.sln -c Debug` → **0 errors** (warnings unchanged from baseline).
2. Filtered suite (ONE PowerShell call — `TEAS_TEST_PG` does not survive between calls):
   ```powershell
   $env:TEAS_TEST_PG='<connection string>'; dotnet test backend/tests/Accounting.Api.Tests/Accounting.Api.Tests.csproj --filter "FullyQualifiedName~FixedAsset" -v minimal
   ```
   → **all FixedAsset tests pass, skipped count = 0**. A run where tests SKIP is NOT a pass
   (memory: *TEAS_TEST_PG per-shell*). Poll the run in-turn; never end the turn waiting on it.
3. `git diff --stat` → **≤ 10 files**, and `git status --porcelain | grep '^??'` → the new
   migration files are the ONLY untracked source (add them explicitly; `git add -u` misses new
   files).
4. Evidence line required in the report: the three untouched tests
   (`Depreciation_full_life_ties_out_to_the_satang`,
   `Depreciation_undershoot_plug_closes_life_at_exactly_24_months`,
   `YearEnd_close_sweeps_5450_...`) **passed without assertion edits**.

**Fable (orchestrator) runs**, not the worker: the full `dotnet test` suite (~13 min, one
backgrounded call) + the personal diff review + the commit. The worker reports code-complete with
gates 1–4.

---

## 8. Out of scope (scope creep here is a reviewable defect)

- **FE**: `frontend/lib/types.ts` `FixedAssetDetail`, the asset-detail page's display of "months
  depreciated", and a prorated-first-month hint in `FixedAssetForm`'s preview → FE follow-up
  ticket; the API field ships now so the FE can pick it up later. TS structural typing means the
  extra JSON field breaks nothing today.
- `PeriodCloseService.cs` — not modified (I5).
- `FixedAsset.Activate`'s `monthly_amount_zero` guard — not modified (§3.3).
- Any change to `EnsureOpenAsync`, the idempotent early return, the unique index, or the `Version`
  concurrency path (r2-verified green).
- Retro-recompute / re-posting of any existing depreciation JE — **forbidden** (I2).
- L3-9 (disposal date ≥ acquire date) — handled under `specs/fix-r2-u5-disposal-date.md`; do not
  touch. L3-12 (no Draft-edit UI) — separate ticket.
- Revaluation / impairment, non-straight-line methods, per-day accrual across the whole life.
- Note for future test authors: `SeedDisposableAssetAsync` patches `AccumulatedDepreciation`
  directly; any FUTURE test that patches accum **and then runs depreciation** must patch
  `MonthsDepreciated` too, or the engine will resolve units from run lines and disagree.

---

## 9. Blast-radius cap

**Max 10 files**, of which 3 are EF-generated:
1. `backend/src/Accounting.Domain/Entities/FixedAsset/FixedAsset.cs`
2. `backend/src/Accounting.Domain/Entities/FixedAsset/DepreciationRunLine.cs` (comment only)
3. `backend/src/Accounting.Infrastructure/Persistence/Configurations/FixedAsset/FixedAssetConfiguration.cs`
4. `backend/src/Accounting.Infrastructure/Migrations/<ts>_FixedAssetMonthsDepreciated.cs`
5. `backend/src/Accounting.Infrastructure/Migrations/<ts>_FixedAssetMonthsDepreciated.Designer.cs` (generated)
6. `backend/src/Accounting.Infrastructure/Migrations/AccountingDbContextModelSnapshot.cs` (generated)
7. `backend/src/Accounting.Infrastructure/FixedAsset/FixedAssetService.cs`
8. `backend/src/Accounting.Application/FixedAsset/FixedAssetDtos.cs`
9. `backend/tests/Accounting.Api.Tests/FixedAsset/FixedAssetServiceTests.cs`
10. `specs/fixed-assets.md`

**Public API**: additive only — one nullable field appended to `FixedAssetDetail`. No endpoint,
route, request DTO, error code or i18n key may change.

**Stop-and-re-spec triggers** (stop, report, do not improvise):
- Any migration `Up()` that wants DML, or any urge to backfill `months_depreciated` in SQL.
- Any need to edit `PeriodCloseService.cs`, `GlPostingService`, or `FixedAsset.Activate`.
- Any need to change an assertion in the three regression tests (§1.6 lines 84 / 131 / 493).
- Any FE file, any new error code, or an 11th file.

---

## Attempt log
<!-- - <date> <worker>: <result / failure summary / evidence pasted> -->
- 2026-08-19 opus-designer: spec written. Design decided — day-count first-unit proration
  (ม.65 ทวิ(2) + พ.ร.ฎ.145 ม.4) + units-indexed schedule replacing the calendar plug + nullable
  `months_depreciated` with a run-line fallback (no migration DML: `619_fixed_assets_rls.sql`
  FORCE RLS would silently no-op a backfill in prod). Calendar catch-up rejected — it makes an
  out-of-order month unclosable via `period.depreciation_required` with no in-app exit.

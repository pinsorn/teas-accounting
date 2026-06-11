# ภ.ง.ด.50 v2 (default) + CIT Filing Dashboard — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the ภ.ง.ด.50 filler always render the รายการที่ 2 ladder (p3) and balance sheet (p6) from real data (v2 = the only behaviour), and add `GET /tax-filings/pnd50/preview` + a CIT filing dashboard on `/tax-filings/cit` that shows every figure before generation.

**Architecture:** A new internal `Pnd50Composition` (all figures + refusal-reason list) is built once by `Pnd50FilingService.ComposeAsync` and consumed by BOTH the PDF path (throws on refusals) and the preview path (reports them) — single source, no duplicated math. Two new pure static builders (`BuildLadder`, `MapBalanceSheet`) are unit-tested without a DB, mirroring the existing `BuildSheet` idiom.

**Tech Stack:** .NET 10 / EF Core 10 (no schema change), existing `RdAcroFormFiller`, Next.js 15 + React Query for the dashboard.

**Spec:** `docs/superpowers/specs/pnd50-v2-dashboard.md` · Recon: `docs/RD-Forms/pnd50/pnd50_radiomap.md` (p3/p6 sections, cont.89) + regenerated `pnd50_cells.json` (already embedded).

---

## Recon facts this plan is built on (cont.89, render-confirmed)

- p3 ladder + รายการที่ 3 each row has **3 columns** — ① กิจการยกเว้นภาษี ② กิจการเสียภาษี ③ รวม.
  Form rubric (confirmed from raster): **กรณีทั่วไป/ลดอัตรา = fill column ③ ONLY.**
- p3 col③ field names (margin №): รายการที่ 2 rows 1..21 =
  `Text17.4`(63), `Text17.7`(64), `Text17.10`(65-66), `Text17.13`(67), `Text17.16`,
  `Text17.19`(68), `Text17.22`, `Text17.25`(69), `Text17.28`(70-71), `Text17.31`(72),
  `Text17.34`(73), `Text17.37`, `Text17.40`(74), `Text17.43`, `Text20`(75), `Text23`,
  `Text26`(75.1), `Text29`(76), `Text32`(77), `Text35.1`(78), `Text35.2`(21. กำไรสุทธิที่ต้องเสียภาษี).
  รายการที่ 3 (ต้นทุนขาย) rows 1..9 = `Text35.5`(79), `Text35.8`(80), `Text35.11`(81),
  `Text35.14`(82), `Text35.17`(83), `Text35.20`, `Text35.23`, `Text35.26`(84), `Text35.29`(85).
- p3 radios: `Group100` `'0'`=กำไรขั้นต้น/`'1'`=ขาดทุนขั้นต้น · `Group101` `'0'`=กำไรสุทธิ/`'1'`=ขาดทุนสุทธิ
  (⚠️ raw `'0'`/`'1'` on-states, not ChoiceN) · `Group9` `Choice1`=กำไรสุทธิที่ต้องเสียภาษี/`Choice2`=ขาดทุนสุทธิ.
- p6 fields: 140=`Text35.210` 141=`.211` 142=`.212` 143=`.213` 144=`.214` 145=`.215` 146=`.216`
  147=`.217` 148=`.218` รวมสินทรัพย์=`.219` 149=`.220` 150=`.221` 151=`.222` 152=`.223` 153=`.224`
  154=`.225` รวมหนี้สิน=`.226` 156=`.2241` 157=`.2251` 158-159=`.2261` 160=`.2242` 161=`.2252`.
  ทุนจดทะเบียน(155)=`Text35.227` is a **plain box (no comb)** — excluded from cells.json; we leave it
  BLANK (no registered-capital field in TEAS; PaidUpCapital ≠ ทุนจดทะเบียน).
- p6 radios: `Group91` `Choice1`=กำไรสะสม/`Choice2`=ขาดทุนสะสม. `Group92`/`Group93` (auditor opinion)
  + attachment-count boxes 162.x = NOT filled (system cannot know them).
- All p3/p6 amount combs are 13 cells (11 baht + 2 satang) — same `Amt` rendering as p2.

## Data-source decisions (locked)

1. **Fix latent double-count:** v1 fed `summary.EffectiveNetProfit` (= P&L net **+ adjustments**, per
   `ComputeYearAsync`) into `CitCalculator.Compute` as *accounting* profit and added
   `profile.AdjustmentsTotal` again. Masked by the v1 adjustments==0 guard. v2 uses
   `profile.AccountingNetProfit` (pure P&L FY net). `OverrideNetProfit`, when set and ≠ computed
   TaxableBeforeLoss, becomes a **refusal** (`pnd50.override_breaks_ladder`) — an override cannot be
   rendered in a ladder whose rows must foot from the books.
2. **Ladder rows 1–8 from flat P&L** (`ProfitLossAsync` has no COGS/GP split — R-Q1a): row 1 =
   Revenue, row 2 ต้นทุน = 0 (TEAS keeps no inventory/COGS; the books genuinely contain no ต้นทุนขาย),
   row 4 รายได้อื่น = 0, row 6 รายจ่ายอื่น = 0, row 8 ขายและบริหาร = total Expense. Row 9 then foots
   to `AccountingNetProfit` exactly (structural invariant, asserted). รายการที่ 3 rows 1–9 print
   explicit zeros (consistent with row 2 = 0).
3. **Adjustments routing:** amount > 0 → row 11 (รายจ่ายที่ไม่ให้ถือเป็นรายจ่าย), amount < 0 → row 13
   (รายได้ยกเว้น/รายจ่ายหักเพิ่ม), abs. Rows 10/17/18/19 = 0 in v2 (no per-code classification yet —
   dashboard shows each adjustment and its destination line so the filer sees the routing).
4. **Sign-flip refusal:** boxes print absolute values; the only sign indicators are the row-3/9/21
   checkboxes. Refuse (`pnd50.ladder_sign_flip`) when the running chain flips sign mid-ladder:
   `s9 ≥ 0 && s14 < 0` or `s9 < 0 && (s12 > 0 || s14 > 0)`.
5. **p6 classifier by account-code convention** (4-digit codes, `1xxxx ASSET…` comment in
   `ChartOfAccount.cs` is stale — seeded codes are 4-digit): cash 1110–1129→140, 1130–1139→141,
   1140–1149→142, other 1000–1499→143, 1500–1999→148; 2110→150, other 2000–2499→152, 2500–2999→154;
   3100–3199→156, 3200–3299→retained earnings(158-159), other 3xxx→157. `CurrentPeriodEarnings`
   always adds into retained earnings. Boxes 144/145/146/147/149/151/153 stay 0 (no classified
   accounts exist; anything else lands in the honest "อื่น (นอกจากที่ระบุ)" lines). Unparseable codes →
   the section's "อื่น" bucket. Invariant: mapped totals == `BalanceSheetReport` section totals.
6. **Attestation narrows:** `attestBlankSchedules` now means p4–p5 (ต้นทุน/ขายบริหาร detail) + p7
   (แบบแจ้งกรรมการ) + ใบแนบ stay blank. Wire param names unchanged. v1 refusals DROPPED: adjustments≠0,
   loss≠0. KEPT: not-attested, surcharge-with-overpaid. NEW: override-mismatch, ladder sign-flip.
7. **Preview** never throws for refusals — it returns them in `refusals[]`; PDF throws
   `pnd50.not_renderable` listing the same codes.

---

### Task 1: `Pnd50Ladder` + `BuildLadder` (pure, TDD)

**Files:**
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs` (records live next to `Pnd50Sheet`)
- Modify: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs` (static `BuildLadder`)
- Test: `backend/tests/Accounting.Api.Tests/Tax/Pnd50FilingServiceTests.cs` (existing file — add a `BuildLadder` region)

- [ ] **Step 1: Add the record** (in `Pnd50FormFiller.cs`, above `Pnd50Sheet`):

```csharp
/// <summary>
/// p3 รายการที่ 2 ladder — SIGNED values internally; the filler prints absolute values and sets the
/// three sign radios (Group100 row 3, Group101 row 9, Group9 row 21). Rows are the form's printed
/// lines; the arithmetic invariants are asserted by BuildLadder, which also enforces the
/// sign-flip renderability rule (boxes have no sign of their own).
/// </summary>
public sealed record Pnd50Ladder(
    decimal DirectRevenue,         // 1  (63)
    decimal CostOfSales,           // 2  (64) = รายการที่ 3 row 9 (0 — TEAS keeps no COGS/inventory)
    decimal GrossProfit,           // 3  (65-66) signed → Group100
    decimal OtherIncome,           // 4  (67) 0 in v2
    decimal Total5,                // 5  = 3.+4. (signed)
    decimal OtherExpenses,         // 6  (68) 0 in v2
    decimal Total7,                // 7  = 5.−6. (signed)
    decimal SellingAdminExpenses,  // 8  (69) = FY total expense
    decimal AccountingNetProfit,   // 9  (70-71) signed → Group101
    decimal IncomeAdditions,       // 10 (72) 0 in v2
    decimal DisallowedExpenses,    // 11 (73) = Σ positive adjustments
    decimal Total12,               // 12 signed = 9.+10.+11.
    decimal ExemptDeductions,      // 13 (74) = |Σ negative adjustments|
    decimal Total14,               // 14 signed = 12.−13. == CitComputation.TaxableBeforeLoss
    decimal LossCarryForward,      // 15 (75) = CitComputation.LossApplied
    decimal Total16,               // 16 signed = 14.−15.
    decimal Excess10Pct,           // 17 (75.1) 0 in v2
    decimal CharityExcess,         // 18 (76) 0 in v2
    decimal EducationExcess,       // 19 (77) 0 in v2
    decimal Total20,               // 20 (78) signed = 16.+17.+18.+19.
    decimal TaxableNetProfit);     // 21 signed → Group9 (== TaxableProfit, or TaxableBeforeLoss when loss)
```

- [ ] **Step 2: Write failing tests** (zero-adjustment pass-through, positive+negative adjustments, loss applied, loss year, sign-flip refusal, invariant-vs-CitComputation):

```csharp
// region BuildLadder (pure — ม.65ทวิ/ตรี ladder, spec pnd50-v2-dashboard.md §3)
[Fact]
public void BuildLadder_zero_adjustments_prints_pass_through_zeros()
{
    var cit = CitCalculator.Compute(100_000m, 0m, 0m, 0m, 0m, CitRateSchedule.General());
    var l = Pnd50FilingService.BuildLadder(
        revenue: 500_000m, expenses: 400_000m,
        positiveAdjustments: 0m, negativeAdjustments: 0m, cit);
    l.DirectRevenue.Should().Be(500_000m);
    l.CostOfSales.Should().Be(0m);
    l.GrossProfit.Should().Be(500_000m);
    l.SellingAdminExpenses.Should().Be(400_000m);
    l.AccountingNetProfit.Should().Be(100_000m);
    l.DisallowedExpenses.Should().Be(0m);
    l.Total14.Should().Be(cit.TaxableBeforeLoss);
    l.TaxableNetProfit.Should().Be(cit.TaxableProfit);
}

[Fact]
public void BuildLadder_routes_signed_adjustments_to_rows_11_and_13()
{
    var cit = CitCalculator.Compute(100_000m, 30_000m, 0m, 0m, 0m, CitRateSchedule.General());
    var l = Pnd50FilingService.BuildLadder(500_000m, 400_000m,
        positiveAdjustments: 50_000m, negativeAdjustments: -20_000m, cit);
    l.DisallowedExpenses.Should().Be(50_000m);
    l.ExemptDeductions.Should().Be(20_000m);
    l.Total12.Should().Be(150_000m);
    l.Total14.Should().Be(130_000m);
    l.Total14.Should().Be(cit.TaxableBeforeLoss);
}

[Fact]
public void BuildLadder_applies_loss_carry_forward_row_15()
{
    var cit = CitCalculator.Compute(100_000m, 0m, 40_000m, 0m, 0m, CitRateSchedule.General());
    var l = Pnd50FilingService.BuildLadder(500_000m, 400_000m, 0m, 0m, cit);
    l.LossCarryForward.Should().Be(40_000m);
    l.Total16.Should().Be(60_000m);
    l.TaxableNetProfit.Should().Be(cit.TaxableProfit);
}

[Fact]
public void BuildLadder_loss_year_keeps_negative_chain()
{
    var cit = CitCalculator.Compute(-80_000m, 0m, 0m, 0m, 0m, CitRateSchedule.General());
    var l = Pnd50FilingService.BuildLadder(300_000m, 380_000m, 0m, 0m, cit);
    l.AccountingNetProfit.Should().Be(-80_000m);
    l.Total14.Should().Be(-80_000m);
    l.LossCarryForward.Should().Be(0m);
    l.TaxableNetProfit.Should().Be(-80_000m);
}

[Fact]
public void BuildLadder_refuses_sign_flip_mid_chain()
{
    // profit books, huge negative adjustment → s14 < 0 while s9 ≥ 0: unrenderable (no sign box on row 14)
    var cit = CitCalculator.Compute(100_000m, -150_000m, 0m, 0m, 0m, CitRateSchedule.General());
    var act = () => Pnd50FilingService.BuildLadder(500_000m, 400_000m, 0m, -150_000m, cit);
    act.Should().Throw<DomainException>().Which.Code.Should().Be("pnd50.ladder_sign_flip");
}

[Fact]
public void BuildLadder_revenue_minus_expenses_must_equal_cit_accounting_profit()
{
    var cit = CitCalculator.Compute(100_000m, 0m, 0m, 0m, 0m, CitRateSchedule.General());
    var act = () => Pnd50FilingService.BuildLadder(500_000m, 350_000m, 0m, 0m, cit); // 150k ≠ 100k
    act.Should().Throw<InvalidOperationException>();
}
// endregion
```

- [ ] **Step 3: Run to verify failure**: from `W:`:
  `dotnet build W:\Accounting.sln` then
  `dotnet test tests\Accounting.Api.Tests --filter "BuildLadder" --no-build` → FAIL (method missing).
  (kill :5080 first if a full build is needed)

- [ ] **Step 4: Implement** in `Pnd50FilingService`:

```csharp
/// <summary>
/// p3 รายการที่ 2 ladder from the FY P&L + signed adjustment sums + the CitComputation.
/// Pure; throws DomainException("pnd50.ladder_sign_flip") when the chain cannot be honestly
/// rendered (boxes are unsigned; only rows 3/9/21 carry sign radios), and
/// InvalidOperationException when caller figures do not reproduce the CitComputation
/// (caller bug, not a tax condition — same posture as BuildSheet's credits check).
/// </summary>
public static Pnd50Ladder BuildLadder(
    decimal revenue, decimal expenses,
    decimal positiveAdjustments, decimal negativeAdjustments, CitComputation cit)
{
    if (positiveAdjustments < 0m || negativeAdjustments > 0m)
        throw new InvalidOperationException("BuildLadder adjustment sums must be pre-split by sign.");
    if (revenue - expenses != cit.AccountingProfit)
        throw new InvalidOperationException("BuildLadder P&L must reproduce CitComputation.AccountingProfit.");
    if (positiveAdjustments + negativeAdjustments != cit.AdjustmentsTotal)
        throw new InvalidOperationException("BuildLadder adjustments must reproduce CitComputation.AdjustmentsTotal.");

    var gross = revenue;                    // row 2 ต้นทุน = 0 (no COGS/inventory in TEAS books)
    var s9  = cit.AccountingProfit;
    var s12 = s9 + positiveAdjustments;     // rows 10 (0) + 11
    var s14 = s12 + negativeAdjustments;    // − row 13
    if (s14 != cit.TaxableBeforeLoss)
        throw new InvalidOperationException("Ladder row 14 must equal CitComputation.TaxableBeforeLoss.");
    if ((s9 >= 0m && s14 < 0m) || (s9 < 0m && (s12 > 0m || s14 > 0m)))
        throw new DomainException("pnd50.ladder_sign_flip",
            "Adjustments flip the sign of the รายการที่ 2 running total mid-ladder; the form's "
          + "unsigned boxes cannot honestly render that — file this year manually.");

    var s16 = s14 - cit.LossApplied;
    return new Pnd50Ladder(
        DirectRevenue: revenue, CostOfSales: 0m, GrossProfit: gross,
        OtherIncome: 0m, Total5: gross, OtherExpenses: 0m, Total7: gross,
        SellingAdminExpenses: expenses, AccountingNetProfit: s9,
        IncomeAdditions: 0m, DisallowedExpenses: positiveAdjustments, Total12: s12,
        ExemptDeductions: Math.Abs(negativeAdjustments), Total14: s14,
        LossCarryForward: cit.LossApplied, Total16: s16,
        Excess10Pct: 0m, CharityExcess: 0m, EducationExcess: 0m,
        Total20: s16, TaxableNetProfit: s16);
}
```

(note: `TaxableNetProfit = s16` — equals `cit.TaxableProfit` on the profit path and the negative
`TaxableBeforeLoss` on the loss path, since `LossApplied` is capped at the positive base.)

- [ ] **Step 5: Build + run** `--filter "BuildLadder"` → all PASS. Run **2×**.

### Task 2: `Pnd50BalanceSheetBoxes` + `MapBalanceSheet` (pure, TDD)

**Files:** same trio as Task 1.

- [ ] **Step 1: Record** (in `Pnd50FormFiller.cs`):

```csharp
/// <summary>
/// p6 งบแสดงฐานะการเงิน boxes, classified from BalanceSheetReport rows by the TEAS account-code
/// convention (4-digit): 1110-1129→140, 1130-1139→141, 1140-1149→142, other 1000-1499→143,
/// 1500-1999→148 · 2110→150, other 2000-2499→152, 2500-2999→154 · 3100-3199→156,
/// 3200-3299→158-159 (+CurrentPeriodEarnings), other 3xxx→157. Boxes 144-147/149/151/153 have no
/// classified source accounts and print 0. Unparseable codes land in the section's "อื่น" line.
/// RetainedEarnings is SIGNED → Group91. Totals must reproduce the report's (asserted).
/// </summary>
public sealed record Pnd50BalanceSheetBoxes(
    decimal CashAndEquivalents, decimal TradeReceivables, decimal Inventory,
    decimal OtherCurrentAssets, decimal OtherNonCurrentAssets, decimal TotalAssets,
    decimal TradePayables, decimal OtherCurrentLiabilities, decimal OtherNonCurrentLiabilities,
    decimal TotalLiabilities,
    decimal PaidUpShareCapital, decimal OtherEquity, decimal RetainedEarnings,
    decimal TotalEquity, decimal TotalLiabilitiesAndEquity);
```

- [ ] **Step 2: Failing tests** (classification, catch-alls, retained-earnings sign, foot invariant):

```csharp
// region MapBalanceSheet (pure — p6 งบฐานะ classifier)
private static BalanceSheetReport Bs(
    (string Code, decimal Bal)[] assets, (string Code, decimal Bal)[] liabs,
    (string Code, decimal Bal)[] equity, decimal currentEarnings)
{
    BalanceSheetSection Sec((string Code, decimal Bal)[] rows) => new(
        rows.Select(r => new BalanceSheetRow(r.Code, "x", r.Bal)).ToList(), rows.Sum(r => r.Bal));
    var a = Sec(assets); var l = Sec(liabs); var e = Sec(equity);
    return new BalanceSheetReport(new DateOnly(2026, 12, 31), 1, a, l, e,
        currentEarnings, l.Total + e.Total + currentEarnings,
        a.Total == l.Total + e.Total + currentEarnings, "");
}

[Fact]
public void MapBalanceSheet_classifies_by_account_code_convention()
{
    var bs = Bs(
        assets:  [("1110", 10m), ("1120", 20m), ("1130", 30m), ("1145", 5m), ("1170", 7m), ("1510", 100m)],
        liabs:   [("2110", 40m), ("2151", 9m), ("2510", 50m)],
        equity:  [("3100", 60m), ("3200", 11m), ("3900", 2m)],
        currentEarnings: 0m);
    var b = Pnd50FilingService.MapBalanceSheet(bs);
    b.CashAndEquivalents.Should().Be(30m);
    b.TradeReceivables.Should().Be(30m);
    b.Inventory.Should().Be(5m);
    b.OtherCurrentAssets.Should().Be(7m);
    b.OtherNonCurrentAssets.Should().Be(100m);
    b.TotalAssets.Should().Be(172m);
    b.TradePayables.Should().Be(40m);
    b.OtherCurrentLiabilities.Should().Be(9m);
    b.OtherNonCurrentLiabilities.Should().Be(50m);
    b.PaidUpShareCapital.Should().Be(60m);
    b.RetainedEarnings.Should().Be(11m);
    b.OtherEquity.Should().Be(2m);
    b.TotalEquity.Should().Be(73m);
    b.TotalLiabilitiesAndEquity.Should().Be(bs.LiabilitiesAndEquityTotal);
}

[Fact]
public void MapBalanceSheet_adds_current_period_earnings_into_retained()
{
    var bs = Bs([("1110", 100m)], [], [("3200", 30m)], currentEarnings: 70m);
    var b = Pnd50FilingService.MapBalanceSheet(bs);
    b.RetainedEarnings.Should().Be(100m);
    b.TotalEquity.Should().Be(100m);
}

[Fact]
public void MapBalanceSheet_negative_retained_is_signed_for_group91()
{
    var bs = Bs([("1110", 10m)], [], [("3200", -40m)], currentEarnings: 25m);
    Pnd50FilingService.MapBalanceSheet(bs).RetainedEarnings.Should().Be(-15m);
}

[Fact]
public void MapBalanceSheet_unparseable_code_lands_in_other_bucket()
{
    var bs = Bs([("1110", 1m), ("ABC", 5m)], [], [], 0m);
    Pnd50FilingService.MapBalanceSheet(bs).OtherCurrentAssets.Should().Be(5m);
}
// endregion
```

- [ ] **Step 3: Run → FAIL.** **Step 4: Implement:**

```csharp
public static Pnd50BalanceSheetBoxes MapBalanceSheet(BalanceSheetReport bs)
{
    static int CodeOf(BalanceSheetRow r) =>
        int.TryParse(r.AccountCode, out var n) && n is >= 1000 and <= 9999 ? n : -1;

    decimal cash = 0, ar = 0, inv = 0, curA = 0, nonA = 0;
    foreach (var r in bs.Assets.Rows)
        switch (CodeOf(r))
        {
            case >= 1110 and <= 1129: cash += r.Balance; break;
            case >= 1130 and <= 1139: ar   += r.Balance; break;
            case >= 1140 and <= 1149: inv  += r.Balance; break;
            case >= 1500 and <= 1999: nonA += r.Balance; break;
            default:                  curA += r.Balance; break;   // incl. unparseable
        }

    decimal ap = 0, curL = 0, nonL = 0;
    foreach (var r in bs.Liabilities.Rows)
        switch (CodeOf(r))
        {
            case 2110:                ap   += r.Balance; break;
            case >= 2500 and <= 2999: nonL += r.Balance; break;
            default:                  curL += r.Balance; break;
        }

    decimal capital = 0, retained = bs.CurrentPeriodEarnings, otherEq = 0;
    foreach (var r in bs.Equity.Rows)
        switch (CodeOf(r))
        {
            case >= 3100 and <= 3199: capital  += r.Balance; break;
            case >= 3200 and <= 3299: retained += r.Balance; break;
            default:                  otherEq  += r.Balance; break;
        }

    var totalEq = capital + otherEq + retained;
    var boxes = new Pnd50BalanceSheetBoxes(
        cash, ar, inv, curA, nonA, bs.Assets.Total,
        ap, curL, nonL, bs.Liabilities.Total,
        capital, otherEq, retained, totalEq, bs.LiabilitiesAndEquityTotal);
    if (cash + ar + inv + curA + nonA != bs.Assets.Total
        || ap + curL + nonL != bs.Liabilities.Total
        || totalEq != bs.Equity.Total + bs.CurrentPeriodEarnings)
        throw new InvalidOperationException("MapBalanceSheet must reproduce the report totals.");
    return boxes;
}
```

- [ ] **Step 5: Build + `--filter "MapBalanceSheet"` PASS ×2.**

### Task 3: Composition + BuildSheet v2 (guards shift) + service rewire

**Files:**
- Modify: `backend/src/Accounting.Application/Tax/IPnd50FilingService.cs` (add `PreviewAsync` + DTOs)
- Modify: `backend/src/Accounting.Infrastructure/Tax/Pnd50FilingService.cs`
- Modify: `backend/src/Accounting.Infrastructure/Pdf/Pnd50FormFiller.cs` (`Pnd50Model` gains `Ladder` + `BalanceSheet`)
- Test: `Pnd50FilingServiceTests.cs` — revise the 11 existing BuildSheet tests + add v2 cases

Sub-steps:
- [ ] `BuildSheet` v2: **drop** the `AdjustmentsTotal != 0 || LossApplied != 0` refusal block; keep the
  attestation check (message reworded: "ภ.ง.ด.50 prints p4–p5 + p7 + ใบแนบ blank — attest firstFiling +
  acceptBlankSchedules") and the surcharge+overpaid refusal. `BaseAmount`/`IsLoss` now come from the
  ladder row 21 (`TaxableNetProfit` sign), passed in — signature becomes
  `BuildSheet(CitComputation cit, decimal whtSuffered, decimal pnd51Prepaid, decimal surcharge, bool isSme, Pnd50Attestation? attest)`
  (unchanged) but the body's `isLoss` already matches `TaxableBeforeLoss < 0` — keep.
- [ ] New internal composition record (Infrastructure, internal):

```csharp
internal sealed record Pnd50Composition(
    int Year, DateOnly PeriodStart, DateOnly PeriodEnd,
    bool IsSme, decimal? PaidUpCapital, decimal Revenue, decimal Expenses,
    CitComputation Cit, Pnd50Ladder? Ladder, Pnd50BalanceSheetBoxes BalanceSheet,
    decimal WhtCredit, decimal Pnd51Prepaid, decimal? Pnd51Estimate, decimal Surcharge,
    IReadOnlyList<WhtReceivableRegisterRow> WhtRows,
    IReadOnlyList<CitAdjustmentDto> Adjustments,
    IReadOnlyList<string> Refusals);
```

- [ ] `ComposeAsync(year, isSme, ct)`: company+profile fetch (as today), `profile = ProfileAsync`,
  `summary` for estimate/prepaid, `whtReg = GetRegisterAsync(periodStart, periodEnd)`,
  `expenses = profile.RevenueFullYear − profile.AccountingNetProfit`,
  `cit = CitCalculator.Compute(profile.AccountingNetProfit, profile.AdjustmentsTotal, profile.LossCarryIn, prepaid, whtFy, schedule)`
  (**accounting NP, not EffectiveNetProfit** — double-count fix), surcharge as today.
  Refusal collection: override mismatch (`summary.OverrideNetProfit is { } o && o != cit.TaxableBeforeLoss`
  → `pnd50.override_breaks_ladder`); `BuildLadder` caught `DomainException` → add its code, Ladder=null;
  surcharge+overpaid → `pnd50.surcharge_with_overpaid`. Balance sheet:
  `MapBalanceSheet(await financialReport.BalanceSheetAsync(periodEnd, ct))` (inject `IFinancialReportService`).
- [ ] `BuildPnd50Async`: `var comp = await ComposeAsync(...)`; if `comp.Refusals.Count > 0` throw
  `DomainException("pnd50.not_renderable", string.Join("; ", comp.Refusals))`; then BuildSheet (attest
  check lives there) and fill with `Sheet + comp.Ladder! + comp.BalanceSheet`.
- [ ] Update the existing BuildSheet unit tests: delete/replace the two that asserted the
  adjustments/loss refusals; keep credits-mismatch, attestation, surcharge-overpaid, figure tests.
- [ ] Build + `--filter "Pnd50"` PASS ×2 on `teas_test`.

### Task 4: Filler renders p3 + p6

**Files:** `Pnd50FormFiller.cs` + structural tests.

- [ ] `Pnd50Model` gains `Pnd50Ladder Ladder` + `Pnd50BalanceSheetBoxes BalanceSheet` (required params — v2 always renders).
- [ ] In `Fill`, after the p2 block, append p3 (`A = abs`, every row printed including zeros):

```csharp
// p3 รายการที่ 2 — column ③ (รวม) only: กรณีทั่วไป/ลดอัตรา per the form rubric (recon cont.89).
var L = m.Ladder;
void Lad(string name, decimal v) => Amt(name, Math.Abs(v));
Lad("Text17.4",  L.DirectRevenue);   Lad("Text17.7",  L.CostOfSales);
Lad("Text17.10", L.GrossProfit);     Lad("Text17.13", L.OtherIncome);
Lad("Text17.16", L.Total5);          Lad("Text17.19", L.OtherExpenses);
Lad("Text17.22", L.Total7);          Lad("Text17.25", L.SellingAdminExpenses);
Lad("Text17.28", L.AccountingNetProfit);
Lad("Text17.31", L.IncomeAdditions); Lad("Text17.34", L.DisallowedExpenses);
Lad("Text17.37", L.Total12);         Lad("Text17.40", L.ExemptDeductions);
Lad("Text17.43", L.Total14);         Lad("Text20",    L.LossCarryForward);
Lad("Text23",    L.Total16);         Lad("Text26",    L.Excess10Pct);
Lad("Text29",    L.CharityExcess);   Lad("Text32",    L.EducationExcess);
Lad("Text35.1",  L.Total20);         Lad("Text35.2",  L.TaxableNetProfit);
// p3 รายการที่ 3 ต้นทุนขาย — all zero (row 2 ↑ = 0; books carry no inventory/COGS)
foreach (var n in new[] { "Text35.5", "Text35.8", "Text35.11", "Text35.14", "Text35.17",
                          "Text35.20", "Text35.23", "Text35.26", "Text35.29" })
    Amt(n, 0m);

// p6 งบแสดงฐานะการเงิน (155 ทุนจดทะเบียน + 162.x attachment counts + Group92/93 left for the filer)
var B = m.BalanceSheet;
Amt("Text35.210", B.CashAndEquivalents); Amt("Text35.211", B.TradeReceivables);
Amt("Text35.212", B.Inventory);          Amt("Text35.213", B.OtherCurrentAssets);
Amt("Text35.214", 0m); Amt("Text35.215", 0m); Amt("Text35.216", 0m); Amt("Text35.217", 0m);
Amt("Text35.218", B.OtherNonCurrentAssets); Amt("Text35.219", B.TotalAssets);
Amt("Text35.220", 0m); Amt("Text35.221", B.TradePayables); Amt("Text35.222", 0m);
Amt("Text35.223", B.OtherCurrentLiabilities); Amt("Text35.224", 0m);
Amt("Text35.225", B.OtherNonCurrentLiabilities); Amt("Text35.226", B.TotalLiabilities);
Amt("Text35.2241", B.PaidUpShareCapital); Amt("Text35.2251", B.OtherEquity);
Amt("Text35.2261", Math.Abs(B.RetainedEarnings));
Amt("Text35.2242", B.TotalEquity); Amt("Text35.2252", B.TotalLiabilitiesAndEquity);
```

- [ ] Radios appended (⚠️ p3 Group100/101 on-states are `'0'`/`'1'`):

```csharp
radios.Add(new("Group100", L.GrossProfit         < 0m ? "1" : "0"));
radios.Add(new("Group101", L.AccountingNetProfit < 0m ? "1" : "0"));
radios.Add(new("Group9",   L.TaxableNetProfit    < 0m ? "Choice2" : "Choice1"));
radios.Add(new("Group91",  B.RetainedEarnings    < 0m ? "Choice2" : "Choice1"));
```

- [ ] Structural tests: `Fill_renders_page3_and_page6_content` (raster pages 3/6 of the produced PDF
  have more ink than the blank template — mirror `Fill_with_worksheet_draws_more_on_page2_than_without`
  from pnd51 tests) + `Cells` map contains every p3/p6 comb name used above (geometry presence test).
- [ ] Build + `--filter "Pnd50"` PASS ×2.

### Task 5: VISUAL GATE (main agent reads rasters — compliance mandatory)

- [ ] Console/test harness fills a distinctive case (profit, adjustments +50k/−20k, loss c/f 40k,
  SME, retained earnings negative) → save PDF → `crop.py` bands of p3 (ladder rows, radio rows) and
  p6 (all sections) → READ every crop: numbers in the right boxes/columns (③ only!), radio ticks on
  the intended options, satang cells = `00`, no off-by-one row. Crops saved to `_review/pnd50v2/` and
  sent to Ham (SendUserFile).
- [ ] A loss-year case (Group101=1, Group9=Choice2, Group91=Choice2) re-rastered + read.

### Task 6: Preview endpoint (single-source)

**Files:** `IPnd50FilingService.cs` (+DTOs), `Pnd50FilingService.cs`, `TaxFilingEndpoints.cs`, integration tests.

- [ ] Public DTOs (Application layer):

```csharp
public sealed record Pnd50LadderDto(
    decimal DirectRevenue, decimal CostOfSales, decimal GrossProfit, decimal OtherIncome,
    decimal Total5, decimal OtherExpenses, decimal Total7, decimal SellingAdminExpenses,
    decimal AccountingNetProfit, decimal IncomeAdditions, decimal DisallowedExpenses,
    decimal Total12, decimal ExemptDeductions, decimal Total14, decimal LossCarryForward,
    decimal Total16, decimal Total20, decimal TaxableNetProfit);

public sealed record Pnd50BalanceSheetDto(
    decimal CashAndEquivalents, decimal TradeReceivables, decimal Inventory,
    decimal OtherCurrentAssets, decimal OtherNonCurrentAssets, decimal TotalAssets,
    decimal TradePayables, decimal OtherCurrentLiabilities, decimal OtherNonCurrentLiabilities,
    decimal TotalLiabilities, decimal PaidUpShareCapital, decimal OtherEquity,
    decimal RetainedEarnings, decimal TotalEquity, decimal TotalLiabilitiesAndEquity, bool Balanced);

public sealed record Pnd50WhtCertDto(
    string DocNo, DateOnly DocDate, string CustomerName, string? CustomerTaxId,
    decimal WhtAmount, string? CustomerWhtCertNo);

public sealed record Pnd50PreviewDto(
    int Year, DateOnly PeriodStart, DateOnly PeriodEnd, bool IsSme, decimal? PaidUpCapital,
    decimal Revenue, decimal Expenses,
    decimal? Pnd51EstimatedProfit, decimal Pnd51Prepaid,
    decimal WhtCreditTotal, IReadOnlyList<Pnd50WhtCertDto> WhtCertificates,
    Pnd50LadderDto? Ladder, IReadOnlyList<CitAdjustmentDto> Adjustments,
    decimal TaxBeforeCredits, decimal CreditsTotal, decimal NetPayable, bool PayMore,
    decimal Surcharge, decimal TotalDue,
    Pnd50BalanceSheetDto BalanceSheet,
    IReadOnlyList<string> Refusals);
```

- [ ] `PreviewAsync(year, isSme, ct)` on the interface; implementation = `ComposeAsync` → map (no throw).
- [ ] Endpoint `GET /tax-filings/pnd50/preview?year&isSme` (FilingPreview policy, tag TaxFilings).
- [ ] Integration tests (`teas_test`, `TestIds.FutureFiscalYear()`): seed a year via existing cit
  endpoints idiom (see `Pnd50FilingServiceTests` v1 setup) → (a) preview returns ladder + refusals
  empty + figures foot (`Total14 == AccountingNetProfit + DisallowedExpenses − ExemptDeductions`);
  (b) with an adjustment row → PDF generation **succeeds** now (v1 refusal dropped) and preview
  `Ladder.DisallowedExpenses` equals the adjustment; (c) preview with override ≠ computed → refusal
  `pnd50.override_breaks_ladder` present AND pdf endpoint → 422 `pnd50.not_renderable`.
- [ ] Build + `--filter "Pnd50"` ×2.

### Task 7: FE — CIT filing dashboard (subagent OK, sequential after Task 6)

**Files:**
- Modify: `frontend/app/(dashboard)/tax-filings/cit/page.tsx` (+ existing components under it)
- Modify: `frontend/messages/th.json` + `frontend/messages/en.json`
- (follow the page's existing data-fetch idiom — React Query + fetch helpers already used there)

Sections (spec §5): ① ภ.ง.ด.51 filed card (estimate/prepaid from preview) · ② ladder card (all rows,
Thai labels matching the form lines, SME/rate badge) · ③ WHT-cert table (per-cert rows + total)
· ④ adjustments table (exists — show routed line 11/13 per row sign) + loss c/f used · ⑤ balance-sheet
mini card (totals + `Balanced` badge) · ⑥ generate buttons (move/merge the v1 pnd50 card; attest
wording updated to p4-5/p7; show `refusals[]` as warning list, disable generate while non-empty).
`tsc --noEmit` = 0. i18n keys th+en. (e2e: extend the cit page spec if one exists; otherwise smoke
via dev server.)

### Task 8: openapi + docs + final gates + commit

- [ ] `docs/api/openapi.yaml`: add `/tax-filings/pnd50/preview` + `Pnd50Preview*` schemas; update
  `/tax-filings/pnd50/pdf` description (v2 semantics — ladder+BS always rendered; 422 codes list).
  **Flag delta for Sana.**
- [ ] Update `docs/superpowers/specs/pnd50-fieldmap-recon.md` v1-scope note → v2 (pointer to this plan).
- [ ] Gates: kill :5080 → `dotnet build W:\Accounting.sln` 0/0 → Domain 137+ → Api full suite ×2 on
  `teas_test` → FE `tsc --noEmit` 0 → `grep -rln "ম"` clean (Thai ม glyph check).
- [ ] `progress.md` prepend (cont.89) + `plan.md` tick + NEXT-SESSION.md update.
- [ ] Commit (Ham's standing policy: push after every commit) — git commit only at the consolidated
  gate, by the main agent.

## Self-review notes

- Spec coverage: §1 semantics shift → Tasks 3/4 · §2 recon → done pre-plan (cont.89) · §3 ladder
  mapping → Task 1/3 · §4 p6 → Task 2/4 · §5 dashboard+preview → Tasks 6/7 · §6 tests → embedded
  per task + visual gate Task 5.
- รายการที่ 3 minimal fill (spec §3 last bullet) = zeros, consistent with ladder row 2 = 0 (decision
  #2) — the dashboard's ladder card shows CostOfSales 0 so the filer sees it pre-generation.
- Type names consistent across tasks: `Pnd50Ladder`, `Pnd50BalanceSheetBoxes`, `Pnd50Composition`,
  `Pnd50PreviewDto`; `BuildLadder`/`MapBalanceSheet` static on `Pnd50FilingService`.

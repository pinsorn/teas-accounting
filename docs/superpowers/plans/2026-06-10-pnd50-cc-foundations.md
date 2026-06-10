# ภ.ง.ด.50 Phase C-C Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended)
> or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Build the C-C data foundations: loss-carry-forward engine + per-FY store, `Company.PaidUpCapital`
(+ auto-SME classify), manual ม.65ตรี adjustment entries, real `BalanceSheetAsync`, and ภ.ง.ด.51
estimate persistence (ম.67ตรี) — everything `Pnd50FormFiller` (next plan) will consume.

**Architecture:** Clean Architecture, mirrors shipped patterns exactly: pure Domain calculator
(`CitLossCarryForward` ~ `CitCalculator`), EF entities in schema `tax` with RLS scripts
(~ `TaxFiling`/payroll), Application contract + Infrastructure service (~ `Pnd51FilingService`),
Minimal-API endpoints gated on existing tax permissions, FE pages in the existing `useState`+fetch idiom.

**Tech Stack:** .NET 10 / EF Core 10 / PostgreSQL (RLS), Next.js 15 + next-intl.

**Scope note (locked decisions, Ham 2026-06-01):** manual adjustment UI (#1) · real BalanceSheetAsync (#3) ·
`Company` paid-up capital + auto-detect SME (#4) · per-year override-able loss store (#5).
`Pnd50FormFiller` + service + ใบแนบ = NEXT plan (C-C form fill / C-D) — NOT here.

---

## Environment briefing (MANDATORY for every executor — §6 CLAUDE.md, updated for Y:)

- Repo root: `Y:\ClaudePlayground\TEAS-Project` (NO subst needed; `dotnet` works from Y: directly — proven).
- Backend dir: `Y:\ClaudePlayground\TEAS-Project\backend`. Build: `dotnet build Y:\ClaudePlayground\TEAS-Project\backend\Accounting.sln` → must be **0 warn / 0 err**.
- **Kill :5080 before a full build** (exe lock): `Get-NetTCPConnection -LocalPort 5080 -State Listen` → `Stop-Process -Force`. (FE+BE may have been left running.)
- **EF migrations: NEVER `--no-build`.** Build solution first, then from `backend\`:
  `dotnet ef migrations add <Name> --project src\Accounting.Infrastructure --startup-project src\Accounting.Api`.
  **Migrations are run by the MAIN agent only** — subagents stop at "entity+config written, build green" and report back.
- Integration tests (real PG, shared DB — must pass **2× consecutively**):
  ```powershell
  $env:TEAS_TEST_PG='Host=localhost;Port=5432;Database=teas_test;Username=accounting;Password=accounting_dev_password;Include Error Detail=true'
  dotnet test Y:\ClaudePlayground\TEAS-Project\backend\tests\Accounting.Api.Tests --filter "<Filter>"
  ```
- Test data discipline: anything UNIQUE-constrained must use `Accounting.TestKit.TestIds.*` (this plan adds `FutureFiscalYear()`).
- FE: `cd Y:\ClaudePlayground\TEAS-Project\frontend`; gate = `node node_modules\typescript\bin\tsc --noEmit` → 0. Never `next build` while dev runs. `next dev` must run from the real Y: path.
- **Do NOT `git commit`** — main agent commits after the consolidated gate.

---

## File structure (all paths relative to `backend/` unless noted)

| File | Responsibility |
|---|---|
| `src/Accounting.Domain/Tax/CitLossCarryForward.cs` (new) | Pure ม.65ตรี(12) 5-year loss walk |
| `tests/Accounting.Domain.Tests/Tax/CitLossCarryForwardTests.cs` (new) | Golden tests |
| `src/Accounting.Domain/Entities/Master/Company.cs` (mod) | + `PaidUpCapital` |
| `src/Accounting.Application/Master/CompanyDtos.cs` (mod) | + field in Create/Update/Dto + validator |
| `src/Accounting.Domain/Entities/Tax/CitYearSummary.cs` (new) | Per-FY P/L store + 51 estimate |
| `src/Accounting.Domain/Entities/Tax/CitAdjustment.cs` (new) | ม.65ตรี adjustment line |
| `src/Accounting.Infrastructure/Persistence/Configurations/Tax/CitYearSummaryConfiguration.cs` + `CitAdjustmentConfiguration.cs` (new) | EF config |
| `src/Accounting.Infrastructure/Migrations/SqlScripts/500_cit_rls.sql` (new) | RLS (auto-run, lexical) |
| `src/Accounting.Application/Tax/ICitYearDataService.cs` (new) | Contract + DTOs |
| `src/Accounting.Infrastructure/Tax/CitYearDataService.cs` (new) | Impl |
| `src/Accounting.Api/Endpoints/CitEndpoints.cs` (new) | `/tax-filings/cit/*` |
| `src/Accounting.Application/Reports/FinancialReportDtos.cs` (mod) + `Infrastructure/Reports/FinancialReportService.cs` (mod) + `Api/Endpoints/ReportEndpoints.cs` (mod) | Balance sheet |
| `tests/Accounting.Api.Tests/Tax/CitYearDataServiceTests.cs`, `tests/Accounting.Api.Tests/Reports/BalanceSheetTests.cs` (new) | Integration tests |
| `frontend/app/(dashboard)/settings/company/…` (mod), `frontend/app/(dashboard)/tax-filings/cit/page.tsx` (new), `frontend/app/(dashboard)/tax-filings/pnd51/page.tsx` (mod), `messages/{th,en}.json` (mod) | FE |
| `docs/api/openapi.yaml` (mod) | New endpoints (Sana delta) |

---

### Task 1: Pure loss-carry-forward engine (Domain, TDD)

**Files:** Create `src/Accounting.Domain/Tax/CitLossCarryForward.cs` ·
Create `tests/Accounting.Domain.Tests/Tax/CitLossCarryForwardTests.cs` ·
Modify `tests/Accounting.TestKit/TestIds.cs`

- [ ] **Step 1: add `TestIds.FutureFiscalYear()`** (used by Tasks 4–7 integration tests; unique-constraint discipline):

```csharp
    /// <summary>A fiscal-year label far in the future with a wide random spread —
    /// cit_year_summaries / cit_adjustments are unique per (company, fiscal_year)
    /// on the shared teas_test DB. Callers that upsert-then-assert should still
    /// tolerate (or delete) a pre-existing row for the chosen year.</summary>
    public static int FutureFiscalYear() => 2200 + Random.Shared.Next(0, 5000);
```

- [ ] **Step 2: write golden tests (RED).** ม.65ตรี(12): a loss of FY `Y` is usable in `Y+1 … Y+5`;
profit years consume the OLDEST non-expired loss first; the carry-in for `targetYear` is what survives.

```csharp
using Accounting.Domain.Tax;
using FluentAssertions;
using Xunit;

namespace Accounting.Domain.Tests.Tax;

/// <summary>ม.65ตรี(12) — loss carry-forward, 5 accounting periods, oldest-first.</summary>
public class CitLossCarryForwardTests
{
    private static (int, decimal) Y(int year, decimal pl) => (year, pl);

    [Fact] public void No_history_carry_in_is_zero() =>
        CitLossCarryForward.CarryInFor(2026, []).Should().Be(0m);

    [Fact] public void Single_loss_within_window_carries_in_full() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2024, -100_000m)]).Should().Be(100_000m);

    [Fact] public void Loss_expires_after_5_periods()
    {
        CitLossCarryForward.CarryInFor(2025, [Y(2020, -100_000m)]).Should().Be(100_000m); // Y+5 ok
        CitLossCarryForward.CarryInFor(2026, [Y(2020, -100_000m)]).Should().Be(0m);       // Y+6 gone
    }

    [Fact] public void Intervening_profit_consumes_loss() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2023, -100_000m), Y(2024, 60_000m)])
            .Should().Be(40_000m);

    [Fact] public void Profit_consumes_oldest_loss_first_so_newer_survives() =>
        // 2019 loss is eaten by the 2021 profit; the 2021-expiry then doesn't matter.
        CitLossCarryForward.CarryInFor(2026, [Y(2019, -50_000m), Y(2021, 50_000m), Y(2024, -80_000m)])
            .Should().Be(80_000m);

    [Fact] public void Profit_cannot_consume_an_already_expired_loss() =>
        // 2018 loss expired before the 2026-window AND before the 2025 profit could… 2018+5=2023 < 2025.
        CitLossCarryForward.CarryInFor(2026, [Y(2018, -100_000m), Y(2025, 70_000m), Y(2024, -30_000m)])
            .Should().Be(30_000m); // 2025 profit eats nothing expired; 2024 loss survives intact? NO —
                                   // 2025 profit consumes the 2024 loss: 30k − 30k = 0 … see Step 3 note.

    [Fact] public void Multiple_losses_accumulate() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2023, -100_000m), Y(2024, -50_000m)])
            .Should().Be(150_000m);

    [Fact] public void Only_years_before_target_count() =>
        CitLossCarryForward.CarryInFor(2026, [Y(2026, -999m), Y(2027, -999m), Y(2024, -10_000m)])
            .Should().Be(10_000m);
}
```

⚠️ **Fix the `Profit_cannot_consume_an_already_expired_loss` expectation while writing:** with history
`2018:-100k, 2024:-30k, 2025:+70k` the 2025 profit consumes the non-expired 2024 loss (30k) fully and the
expired 2018 loss not at all → carry-in for 2026 = **0**. Write the test with that corrected expectation
(`.Should().Be(0m)`) and a comment explaining both effects (expiry blocks the old loss; consumption kills the new one).

- [ ] **Step 3: run → RED** (`dotnet test …Accounting.Domain.Tests --filter CitLossCarryForward` — type missing).

- [ ] **Step 4: implement**

```csharp
namespace Accounting.Domain.Tax;

/// <summary>
/// Pure ม.65ตรี(12) loss carry-forward: a tax loss of fiscal year Y may offset taxable profit in the
/// FIVE following accounting periods (Y+1 … Y+5). Profits consume the OLDEST non-expired loss first.
/// Input = effective net TAXABLE profit/loss per FY (after ম.65ทวิ/ตรี adjustments, signed; loss &lt; 0).
/// Output = the loss available to carry INTO <paramref name="targetYear"/>. No DB, golden-tested.
/// </summary>
public static class CitLossCarryForward
{
    public static decimal CarryInFor(
        int targetYear, IReadOnlyList<(int Year, decimal NetTaxableProfit)> history)
    {
        var pool = new List<(int Year, decimal Remaining)>();  // open losses, oldest first
        foreach (var (year, pl) in history
                     .Where(h => h.Year < targetYear)
                     .OrderBy(h => h.Year))
        {
            if (pl < 0m) { pool.Add((year, -pl)); continue; }
            var profit = pl;
            for (var i = 0; i < pool.Count && profit > 0m; i++)
            {
                if (pool[i].Remaining <= 0m || pool[i].Year + 5 < year) continue; // spent / expired
                var use = Math.Min(pool[i].Remaining, profit);
                pool[i] = (pool[i].Year, pool[i].Remaining - use);
                profit -= use;
            }
        }
        return pool.Where(p => p.Year + 5 >= targetYear).Sum(p => p.Remaining);
    }
}
```

- [ ] **Step 5: run → GREEN.** Also run the full `Accounting.Domain.Tests` (baseline ≥ 89). Report counts.

---

### Task 2: `Company.PaidUpCapital` (entity → DTO → validator → service → config) — migration by MAIN agent

**Files:** Modify `src/Accounting.Domain/Entities/Master/Company.cs` ·
`src/Accounting.Application/Master/CompanyDtos.cs` ·
`src/Accounting.Infrastructure/Persistence/Configurations/Master/CompanyConfiguration.cs` ·
the `ICompanyService` impl (find: `Grep "class CompanyService" src/Accounting.Infrastructure`).

- [ ] **Step 1: entity** — add to `Company` after `ReportingStandard`:

```csharp
    /// <summary>Paid-up / registered capital (฿). Drives auto-SME CIT classification
    /// (SME = paid-up ≤ ฿5M AND revenue ≤ ฿30M — ภ.ง.ด.50 rate schedule, plan §4.6).
    /// Null = unknown → classified as General (never silently SME).</summary>
    public decimal? PaidUpCapital { get; set; }
```

- [ ] **Step 2: DTOs** — `CreateCompanyRequest` + `UpdateCompanyRequest` + `CompanyDto` each gain
`decimal? PaidUpCapital` (append at the end of the record's parameter list; update the service's
Create/Update/List mappings accordingly). Validator (both create + a new update rule if an
`UpdateCompanyValidator` exists; if not, only create):

```csharp
        RuleFor(x => x.PaidUpCapital).GreaterThanOrEqualTo(0m).When(x => x.PaidUpCapital.HasValue);
```

- [ ] **Step 3: EF config** — in `CompanyConfiguration` add:

```csharp
        b.Property(c => c.PaidUpCapital).HasPrecision(19, 4);
```

- [ ] **Step 4: build 0/0** + run existing `--filter Company` tests (no regression). **STOP — do not run `dotnet ef`.**

- [ ] **Step 5 (MAIN AGENT): migration** — kill :5080 if listening, build solution, then from `backend\`:
`dotnet ef migrations add AddCompanyPaidUpCapital --project src\Accounting.Infrastructure --startup-project src\Accounting.Api`
→ review the diff is exactly one nullable numeric column → `dotnet ef database update …` (same flags) on `accounting_dev`.

---

### Task 3: `CitYearSummary` + `CitAdjustment` entities + RLS — migration by MAIN agent

**Files:** Create the two entities, two configurations, `500_cit_rls.sql`; modify `AccountingDbContext`
(add `DbSet`s — mirror how `TaxFilings` is declared).

- [ ] **Step 1: entities**

```csharp
using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Phase C-C — per-fiscal-year CIT summary (locked decision #5): the loss-carry-forward store
/// (computed at year-end, override-able) + the ภ.ง.ด.51 estimate (ম.67ตรี under-estimate check).
/// FiscalYear = the CE year the FY STARTS in (matches Pnd51FilingService's `year`).
/// NetProfit figures are TAXABLE (accounting P&amp;L net + ম.65ทวิ/ตรี adjustments), signed; loss &lt; 0.
/// </summary>
public class CitYearSummary : ITenantOwned, IAuditable
{
    public long CitYearSummaryId { get; set; }
    public int CompanyId { get; set; }
    public int FiscalYear { get; set; }

    /// <summary>Auto snapshot: P&amp;L FY net profit + Σ adjustments (POST …/compute).</summary>
    public decimal? ComputedNetProfit { get; set; }
    /// <summary>Manual override (locked decision #5) — wins over Computed when set.</summary>
    public decimal? OverrideNetProfit { get; set; }

    /// <summary>ภ.ง.ด.51 method-A full-year estimate as filed (ম.67ตรี penalty input).</summary>
    public decimal? Pnd51EstimatedProfit { get; set; }
    /// <summary>ภ.ง.ด.51 amount prepaid (ম.67ทวิ) — the ภ.ง.ด.50 credit line.</summary>
    public decimal? Pnd51Prepaid { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }

    public decimal? EffectiveNetProfit => OverrideNetProfit ?? ComputedNetProfit;
}
```

```csharp
using Accounting.Domain.Common;

namespace Accounting.Domain.Entities.Tax;

/// <summary>
/// Phase C-C — one manual CIT tax-adjustment line (locked decision #1): ম.65ทวิ/65ตรี differences
/// between accounting and taxable profit. SIGNED: add-backs (non-deductibles) &gt; 0,
/// exempt income / extra deductions &lt; 0. Layered onto the auto P&amp;L net profit.
/// </summary>
public class CitAdjustment : ITenantOwned, IAuditable
{
    public long CitAdjustmentId { get; set; }
    public int CompanyId { get; set; }
    public int FiscalYear { get; set; }

    /// <summary>Legal reference, e.g. "ม.65ตรี(3)" / "ম.65ทวิ(2)".</summary>
    public required string LegalRefCode { get; set; }
    public required string Label { get; set; }
    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public long? CreatedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long? UpdatedBy { get; set; }
}
```

(If `ITenantOwned`/`IAuditable` member sets differ, match the interfaces — copy `TaxFiling`'s shape.)

- [ ] **Step 2: configurations** (schema `tax`, snake_case tables, mirror `TaxFilingConfiguration`):

```csharp
internal sealed class CitYearSummaryConfiguration : IEntityTypeConfiguration<CitYearSummary>
{
    public void Configure(EntityTypeBuilder<CitYearSummary> b)
    {
        b.ToTable("cit_year_summaries", "tax");
        b.HasKey(x => x.CitYearSummaryId);
        b.Property(x => x.ComputedNetProfit).HasPrecision(19, 4);
        b.Property(x => x.OverrideNetProfit).HasPrecision(19, 4);
        b.Property(x => x.Pnd51EstimatedProfit).HasPrecision(19, 4);
        b.Property(x => x.Pnd51Prepaid).HasPrecision(19, 4);
        b.Property(x => x.Note).HasMaxLength(500);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.Ignore(x => x.EffectiveNetProfit);
        b.HasIndex(x => new { x.CompanyId, x.FiscalYear }).IsUnique();
    }
}
```

```csharp
internal sealed class CitAdjustmentConfiguration : IEntityTypeConfiguration<CitAdjustment>
{
    public void Configure(EntityTypeBuilder<CitAdjustment> b)
    {
        b.ToTable("cit_adjustments", "tax");
        b.HasKey(x => x.CitAdjustmentId);
        b.Property(x => x.LegalRefCode).HasMaxLength(50).IsRequired();
        b.Property(x => x.Label).HasMaxLength(255).IsRequired();
        b.Property(x => x.Amount).HasPrecision(19, 4);
        b.Property(x => x.CreatedAt).HasColumnType("timestamptz(3)");
        b.Property(x => x.UpdatedAt).HasColumnType("timestamptz(3)");
        b.HasIndex(x => new { x.CompanyId, x.FiscalYear });
    }
}
```

- [ ] **Step 3: DbSets** in `AccountingDbContext` (next to `TaxFilings`):
`public DbSet<CitYearSummary> CitYearSummaries => Set<CitYearSummary>();` and
`public DbSet<CitAdjustment> CitAdjustments => Set<CitAdjustment>();`
**Check the tenant global-query-filter mechanism**: if filters are applied by convention over `ITenantOwned`,
nothing more; if each entity is listed explicitly, add both (grep `HasQueryFilter`).

- [ ] **Step 4: `500_cit_rls.sql`** (auto-run lexically by DbInitializer; idempotent — mirror `480_payroll_rls.sql`):

```sql
-- Phase C-C — RLS on tax.cit_year_summaries + tax.cit_adjustments (multi-tenant; mirror 010/480).
-- Table schema is owned by the EF migration AddCitYearStores (runs first). Idempotent.

ALTER TABLE tax.cit_year_summaries ENABLE ROW LEVEL SECURITY;
ALTER TABLE tax.cit_year_summaries FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON tax.cit_year_summaries;
CREATE POLICY company_isolation ON tax.cit_year_summaries
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );

ALTER TABLE tax.cit_adjustments ENABLE ROW LEVEL SECURITY;
ALTER TABLE tax.cit_adjustments FORCE  ROW LEVEL SECURITY;
DROP POLICY IF EXISTS company_isolation ON tax.cit_adjustments;
CREATE POLICY company_isolation ON tax.cit_adjustments
    USING (
        company_id = NULLIF(current_setting('app.company_id', true), '')::INT
        OR COALESCE(NULLIF(current_setting('app.is_super_admin', true), '')::BOOLEAN, FALSE)
    );
```

- [ ] **Step 5: build 0/0. STOP — no `dotnet ef`.**

- [ ] **Step 6 (MAIN AGENT): migration `AddCitYearStores`** — same ritual as Task 2 Step 5; review diff =
two new tables only; `database update` on dev.

---

### Task 4: `ICitYearDataService` + impl + integration tests

**Files:** Create `src/Accounting.Application/Tax/ICitYearDataService.cs` ·
`src/Accounting.Infrastructure/Tax/CitYearDataService.cs` ·
`tests/Accounting.Api.Tests/Tax/CitYearDataServiceTests.cs`.
DI: register where `IPnd51FilingService` is registered (grep it; same lifetime, Scoped).

- [ ] **Step 1: contract + DTOs**

```csharp
using Accounting.Domain.Tax;

namespace Accounting.Application.Tax;

public sealed record CitYearSummaryDto(
    int FiscalYear, decimal? ComputedNetProfit, decimal? OverrideNetProfit,
    decimal? EffectiveNetProfit, decimal? Pnd51EstimatedProfit, decimal? Pnd51Prepaid, string? Note);

public sealed record UpsertCitYearRequest(decimal? OverrideNetProfit, string? Note);

public sealed record CitAdjustmentDto(
    long CitAdjustmentId, int FiscalYear, string LegalRefCode, string Label, decimal Amount);

public sealed record UpsertCitAdjustmentRequest(string LegalRefCode, string Label, decimal Amount);

/// <summary>Auto-SME + ภ.ง.ด.50 inputs for a fiscal year (plan §4.6: SME = paid-up ≤5M ∧ revenue ≤30M).</summary>
public sealed record CitProfileDto(
    int FiscalYear, decimal? PaidUpCapital, decimal RevenueFullYear, bool IsSme,
    decimal AdjustmentsTotal, decimal LossCarryIn, decimal AccountingNetProfit);

public interface ICitYearDataService
{
    Task<IReadOnlyList<CitYearSummaryDto>> ListYearsAsync(CancellationToken ct);
    Task<CitYearSummaryDto> UpsertYearAsync(int fiscalYear, UpsertCitYearRequest req, CancellationToken ct);
    /// <summary>Snapshots ComputedNetProfit = P&amp;L FY net profit + Σ adjustments(FY).</summary>
    Task<CitYearSummaryDto> ComputeYearAsync(int fiscalYear, CancellationToken ct);
    /// <summary>Persist the ภ.ง.ด.51 method-A estimate + prepaid for the ম.67ตรี year-end check.</summary>
    Task<CitYearSummaryDto> RecordPnd51EstimateAsync(
        int fiscalYear, decimal estimatedProfit, decimal whtH1, bool isSme, CancellationToken ct);

    Task<IReadOnlyList<CitAdjustmentDto>> ListAdjustmentsAsync(int fiscalYear, CancellationToken ct);
    Task<CitAdjustmentDto> CreateAdjustmentAsync(int fiscalYear, UpsertCitAdjustmentRequest req, CancellationToken ct);
    Task<CitAdjustmentDto> UpdateAdjustmentAsync(long id, UpsertCitAdjustmentRequest req, CancellationToken ct);
    Task DeleteAdjustmentAsync(long id, CancellationToken ct);

    Task<CitProfileDto> ProfileAsync(int fiscalYear, CancellationToken ct);
}
```

- [ ] **Step 2: implementation sketch** (`CitYearDataService(AccountingDbContext db, ITenantContext tenant, IFinancialReportService financialReport)`,
mirror `Pnd51FilingService` style — `EnsureAuth` via `tenant.IsAuthenticated`, tenant filter is the global
query filter but ALWAYS set `CompanyId = tenant.CompanyId` on inserts):
  - FY bounds: copy the `Company.FiscalYearStartMonth` logic from `Pnd51FilingService` (`periodStart = new DateOnly(fiscalYear, startMonth, 1)`, `periodEnd = +12mo −1d`).
  - `UpsertYearAsync` / `RecordPnd51EstimateAsync` / `ComputeYearAsync`: find-or-create the `(company, fiscalYear)` row, set fields, `UpdatedAt = DateTimeOffset.UtcNow`, save, return DTO.
  - `ComputeYearAsync`: `ProfitLossAsync(periodStart, periodEnd, null, includeUnspecified: true)` →
    `ComputedNetProfit = pl.Totals.NetProfit + adjustmentsSum(fiscalYear)`.
  - `RecordPnd51EstimateAsync`: `Pnd51Prepaid = CitCalculator.HalfYearPrepayment(estimatedProfit, whtH1, isSme ? CitRateSchedule.Sme() : CitRateSchedule.General())`; store `Pnd51EstimatedProfit = estimatedProfit`.
  - Adjustments CRUD: validate `LegalRefCode`/`Label` non-empty (throw `DomainException("cit.adjustment_invalid", …)`); Update/Delete must 404 (`DomainException("cit.adjustment_not_found", …)`) when the row isn't found **within the tenant**.
  - `ProfileAsync`: company → `PaidUpCapital`; P&L FY → `RevenueFullYear` + `AccountingNetProfit`;
    `IsSme = PaidUpCapital is { } cap && cap <= 5_000_000m && RevenueFullYear <= 30_000_000m` (null capital → General — never silently SME);
    `AdjustmentsTotal = Σ adjustments(fiscalYear)`;
    `LossCarryIn = CitLossCarryForward.CarryInFor(fiscalYear, history)` where history = all `CitYearSummary`
    rows of the company with `EffectiveNetProfit != null`, projected to `(FiscalYear, EffectiveNetProfit.Value)`.

- [ ] **Step 3: integration tests (RED first where cheap; these hit teas_test).** Mirror an existing Api.Tests
service test for fixture/auth setup (look at `Pnd51FilingServiceTests` — `PostgresFixture`, tenant login).
Each test takes its own `var year = TestIds.FutureFiscalYear();`. Cover at minimum:
  1. `UpsertYearAsync` creates then updates (override + note round-trip; `EffectiveNetProfit` prefers override).
  2. `CreateAdjustmentAsync` + `ListAdjustmentsAsync` + `UpdateAdjustmentAsync` + `DeleteAdjustmentAsync` round-trip; delete of unknown id throws `cit.adjustment_not_found`.
  3. `RecordPnd51EstimateAsync` stores estimate and `Pnd51Prepaid == CitCalculator.HalfYearPrepayment(est, wht, General())` (cite `// ম.67ตรี`).
  4. `ProfileAsync` SME classification: seed company PaidUpCapital via direct db update inside the test
     (≤5M → with small revenue ⇒ `IsSme=true`; set 6M ⇒ false; null ⇒ false). Restore the original value in a `finally`.
  5. `ProfileAsync` loss carry-in: upsert overrides for `year-1 = -100k`, `year-2 = +20k` (use the SAME random base year) → expect carry-in `100k` at `year` (the +20k is AFTER the loss year so consumes… order: 2 years history `[y-2:+20k, y-1:-100k]` → profit precedes loss → nothing consumed → 100k).
  6. Cross-tenant: adjustments created under company 1 are invisible/unreachable when the service runs as another company (mirror how existing tenant-isolation service tests do it; if no cheap pattern exists, assert the query path filters by `tenant.CompanyId` and rely on RLS tests).

- [ ] **Step 4: run filter `CitYearData` → GREEN ×2 consecutively** on teas_test. Build 0/0.

---

### Task 5: `/tax-filings/cit/*` endpoints

**Files:** Create `src/Accounting.Api/Endpoints/CitEndpoints.cs`; modify `Program.cs` (add `app.MapCitEndpoints();`
next to `MapTaxFilingEndpoints`).

- [ ] **Step 1: endpoints** — reads gated `Permissions.Tax.FilingPreview`, writes gated `Permissions.Tax.FilingFinalize`
(no new permission rows v1 — note for Sana/openapi):

```csharp
using Accounting.Api.Authorization;
using Accounting.Application.Identity;
using Accounting.Application.Tax;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Endpoints;

/// <summary>Phase C-C — CIT year data (loss c/f store, ম.65ตรี adjustments, SME profile).</summary>
public static class CitEndpoints
{
    public static IEndpointRouteBuilder MapCitEndpoints(this IEndpointRouteBuilder app)
    {
        var read  = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingPreview;
        var write = PermissionPolicyProvider.PolicyPrefix + Permissions.Tax.FilingFinalize;
        var g = app.MapGroup("/tax-filings/cit").WithTags("TaxFilings");

        g.MapGet("/years", (ICitYearDataService svc, CancellationToken ct) =>
                svc.ListYearsAsync(ct)).RequireAuthorization(read);

        g.MapPut("/years/{year:int}", async (int year, [FromBody] UpsertCitYearRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.UpsertYearAsync(year, req, ct)))
            .RequireAuthorization(write);

        g.MapPost("/years/{year:int}/compute", async (int year,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.ComputeYearAsync(year, ct)))
            .RequireAuthorization(write);

        g.MapGet("/adjustments", (int year, ICitYearDataService svc, CancellationToken ct) =>
                svc.ListAdjustmentsAsync(year, ct)).RequireAuthorization(read);

        g.MapPost("/adjustments", async (int year, [FromBody] UpsertCitAdjustmentRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.CreateAdjustmentAsync(year, req, ct)))
            .RequireAuthorization(write);

        g.MapPut("/adjustments/{id:long}", async (long id, [FromBody] UpsertCitAdjustmentRequest req,
                ICitYearDataService svc, CancellationToken ct) =>
                    Results.Ok(await svc.UpdateAdjustmentAsync(id, req, ct)))
            .RequireAuthorization(write);

        g.MapDelete("/adjustments/{id:long}", async (long id,
                ICitYearDataService svc, CancellationToken ct) =>
                { await svc.DeleteAdjustmentAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(write);

        g.MapGet("/profile", (int year, ICitYearDataService svc, CancellationToken ct) =>
                svc.ProfileAsync(year, ct)).RequireAuthorization(read);

        return app;
    }
}
```

(Adapt: if other endpoint files wrap awaited results in `Results.Ok(…)` for GETs too, match the house style.)

- [ ] **Step 2: ภ.ง.ด.51 estimate endpoint** — in `TaxFilingEndpoints` next to the existing pnd51 GET:

```csharp
        // C-C — persist the method-A estimate at filing time (ม.67ตรี year-end check).
        app.MapPost("/tax-filings/pnd51/estimate", async (
            [FromQuery] int year, [FromQuery] decimal estimatedProfit,
            [FromQuery] decimal? whtH1, [FromQuery] bool? isSme,
            ICitYearDataService svc, CancellationToken ct) =>
                Results.Ok(await svc.RecordPnd51EstimateAsync(
                    year, estimatedProfit, whtH1 ?? 0m, isSme ?? false, ct)))
        .RequireAuthorization(preview);
```

- [ ] **Step 3: build 0/0**; smoke integration test optional (service already covered) — but DO add one endpoint
test if the repo has a cheap authenticated-HTTP fixture (mirror how the pnd51 pdf endpoint is tested in
`Pnd51WorksheetTests`/`Pnd51FilingServiceTests`; if those are service-level only, skip HTTP tests).

---

### Task 6: real Balance Sheet (locked decision #3)

**Files:** Modify `src/Accounting.Application/Reports/FinancialReportDtos.cs` ·
`src/Accounting.Infrastructure/Reports/FinancialReportService.cs` ·
`src/Accounting.Api/Endpoints/ReportEndpoints.cs` ·
Create `tests/Accounting.Api.Tests/Reports/BalanceSheetTests.cs`.

- [ ] **Step 1: DTOs** (append to `FinancialReportDtos.cs`):

```csharp
// ── C-C Balance Sheet (งบแสดงฐานะการเงิน) — assets / liabilities / equity as-of date.
public sealed record BalanceSheetRow(string AccountCode, string AccountNameTh, decimal Balance);

public sealed record BalanceSheetSection(IReadOnlyList<BalanceSheetRow> Rows, decimal Total);

public sealed record BalanceSheetReport(
    DateOnly AsOfDate, int CompanyId,
    BalanceSheetSection Assets, BalanceSheetSection Liabilities, BalanceSheetSection Equity,
    decimal CurrentPeriodEarnings,          // cumulative un-closed Revenue − Expense up to as-of
    decimal LiabilitiesAndEquityTotal,      // Liabilities.Total + Equity.Total + CurrentPeriodEarnings
    bool Balanced,                          // Assets.Total == LiabilitiesAndEquityTotal
    string Note);
```

and add to `IFinancialReportService`:

```csharp
    Task<BalanceSheetReport> BalanceSheetAsync(DateOnly asOfDate, CancellationToken ct);
```

- [ ] **Step 2: failing integration test** — seed nothing new; on teas_test the existing posted journals are
balanced by the GL invariant, so assert the structural invariant + classification:

```csharp
[Fact]
public async Task Balance_sheet_balances_and_classifies_by_account_type()
{
    var svc = /* resolve IFinancialReportService exactly like the TrialBalance tests do */;
    var bs = await svc.BalanceSheetAsync(new DateOnly(2100, 12, 31), CancellationToken.None);

    bs.Balanced.Should().BeTrue();   // Σ assets == Σ liabilities + equity + current earnings
    bs.LiabilitiesAndEquityTotal.Should()
        .Be(bs.Liabilities.Total + bs.Equity.Total + bs.CurrentPeriodEarnings);
    bs.Assets.Rows.Should().OnlyContain(r => r.Balance != 0m);  // zero-balance accounts hidden
}
```

Add a cross-check test: `BalanceSheetAsync` vs `TrialBalanceAsync` at the same date —
assets total must equal Σ(TB net of Asset accounts).

- [ ] **Step 3: implement** in `FinancialReportService` (same query shape as `TrialBalanceAsync`):
sum `JournalLines` joined to Posted `JournalEntries` with `DocDate <= asOfDate`, joined to
`ChartOfAccounts`, grouped per account. Balance per account: `AccountType.Asset → Dr−Cr`,
`Liability/Equity → Cr−Dr`. `CurrentPeriodEarnings = Σ Revenue(Cr−Dr) − Σ Expense(Dr−Cr)` over the same
rows (cumulative — period-close journals already in GL net themselves out, so this is correct whether or
not closes ran). Drop zero-balance rows, order by `AccountCode`.
`Note`: state that earnings appear as a single computed line, not per-account, until period-close maturity (Phase 2).

- [ ] **Step 4: endpoint** in `ReportEndpoints` (perm = TrialBalance — same data surface):

```csharp
        // C-C — งบแสดงฐานะการเงิน (feeds ภ.ง.ด.50 + DBD; locked decision #3).
        group.MapGet("/balance-sheet", async (
            [FromQuery] DateOnly? asOfDate, IFinancialReportService svc, CancellationToken ct) =>
                Results.Ok(await svc.BalanceSheetAsync(
                    asOfDate ?? DateOnly.FromDateTime(DateTime.UtcNow), ct)))
        .RequireAuthorization(PermissionPolicyProvider.PolicyPrefix + Permissions.Report.TrialBalance);
```

- [ ] **Step 5: run `--filter BalanceSheet` → GREEN ×2** + full `--filter "Pdf|Pnd51|FinancialReport|TrialBalance"` regression. Build 0/0.

---

### Task 7: FE — paid-up capital + CIT year-data page + pnd51 estimate save

**Files:** Modify `frontend/app/(dashboard)/settings/company/` (the edit form — read it first) ·
Create `frontend/app/(dashboard)/tax-filings/cit/page.tsx` ·
Modify `frontend/app/(dashboard)/tax-filings/pnd51/page.tsx` ·
Modify `frontend/messages/th.json` + `frontend/messages/en.json` (BOTH — TH primary).

- [ ] **Step 1: company settings** — add a `paidUpCapital` number input to the company edit form +
include in the PUT body. Label th: `ทุนจดทะเบียนชำระแล้ว (บาท)` / en: `Paid-up capital (THB)`; helper text th:
`ใช้จัดประเภท SME สำหรับภาษีเงินได้นิติบุคคล (ทุน ≤ 5 ล้าน และรายได้ ≤ 30 ล้าน)`.

- [ ] **Step 2: CIT page** `tax-filings/cit/page.tsx` — **mirror the pnd51 page idiom exactly**
(`'use client'`, `useState`, fetch via the same api helper the pnd51 page uses; do NOT introduce RHF/Zod here):
  - Year selector (default current CE year).
  - Profile card: GET `/tax-filings/cit/profile?year=` → paid-up capital, revenue, SME badge, loss carry-in, adjustments total.
  - Year summary card: computed/override/effective net profit + ภ.ง.ด.51 estimate/prepaid; "คำนวณจากบัญชี" button → POST `/years/{year}/compute`; override input + save → PUT `/years/{year}`.
  - Adjustments table (ম.65ตรี): list + add/edit/delete rows (`LegalRefCode`, `Label`, `Amount` signed,
    hint th: `บวกกลับ = จำนวนบวก · หักออก/ยกเว้น = จำนวนลบ`).
  - Link from the tax-filings index page (add a card/row where pnd51 is listed).
- [ ] **Step 3: pnd51 page** — after a successful PDF download, show a `บันทึกประมาณการ (ม.67ตรี)` button
that POSTs `/tax-filings/pnd51/estimate?year=&estimatedProfit=&whtH1=&isSme=` with the form's current
values; success toast th: `บันทึกประมาณการสำหรับตรวจ ম.67ตรี ปลายปีแล้ว`. Disabled until a value for
estimatedProfit exists (the stored estimate must equal what was filed).
- [ ] **Step 4: i18n** — every new string in BOTH `th.json` and `en.json` (mirror the pnd51 key structure under
a new `cit` namespace). No hardcoded Thai in TSX beyond what the surrounding page already does.
- [ ] **Step 5: gate** — `node node_modules\typescript\bin\tsc --noEmit` → 0 errors.

---

### Task 8 (MAIN agent): openapi + consolidated gate + records

- [ ] **Step 1: openapi.yaml** — add `/tax-filings/cit/years` (GET/PUT/compute), `/tax-filings/cit/adjustments`
(GET/POST/PUT/DELETE), `/tax-filings/cit/profile`, `/tax-filings/pnd51/estimate`, `/reports/balance-sheet`,
and the `paidUpCapital` field on company schemas. **Flag the delta for Sana** in progress.md.
- [ ] **Step 2: consolidated gate** — kill :5080 → full solution build 0/0 → Domain.Tests (≥ 89 + new CitLossCarryForward) →
Api.Tests filters `CitYearData|BalanceSheet|Pnd51` ×2 consecutive on teas_test → FE `tsc --noEmit` 0.
- [ ] **Step 3: commit** (Ham-authorized for this plan's scope only) — code + migrations + SqlScripts + FE + openapi together.
Conventional message, e.g. `feat(tax): CIT Phase C-C foundations — loss c/f store, adjustments, PaidUpCapital, balance sheet`.
- [ ] **Step 4: prepend `progress.md`** entry + tick `plan.md` C-C foundation items (note: form filler still ☐).

---

## Out of scope (NEXT plan — do not start here)

- `Pnd50FormFiller` + field map of `pnd50_050369.pdf` (192 widgets, needs visual gate + Ham iteration).
- `Pnd50FilingService` + endpoint + FE, ใบแนบ 1–5, disclosure ม.71ทวิ (Phase C-D).
- SME % radio on ภ.ง.ด.51 page 2 (needs Ham), Method B, ชำระไว้เกิน.
- Wiring auto-SME INTO `Pnd51FilingService` defaults (FE still blocks SME worksheet; revisit with the radio).

## Self-review notes

- Spec coverage: locked decisions #1 (Task 3+4+5+7 adjustments), #3 (Task 6), #4 (Task 2 + ProfileAsync), #5 (Task 1+3+4); gap-list items 4, 5, 6 of the kickoff spec covered; items 1–2 partially shipped earlier (C-A/C-B); item 3 = Task 6.
- Type consistency: `CitLossCarryForward.CarryInFor` used in Task 4 ProfileAsync; `UpsertCitYearRequest`/`UpsertCitAdjustmentRequest` names match between Tasks 4 and 5; `Pnd51Prepaid` via `CitCalculator.HalfYearPrepayment` (existing API, verified).
- Known judgment calls baked in: write-perm = `tax.filing.finalize` (no new permission seed v1); adjustment amounts signed (form filler splits +/− later); `FiscalYear` = CE year the FY starts in (matches `Pnd51FilingService.year`).

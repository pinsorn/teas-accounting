using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.FixedAsset;
using Accounting.Application.Ledger;
using Accounting.Application.Reports;
using Accounting.Domain.Common;
using Accounting.Domain.Entities.Ledger;
using Accounting.Domain.Entities.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.FixedAsset;

/// <summary>
/// Cycle D (specs/fixed-assets.md §10) — depreciation math invariant (§3, worked examples
/// verbatim incl. the Fable-review undershoot-plug case 10.1b), acquisition double-book guard
/// (FA-A), idempotency + race (FA-E), period-close hook (§4), disposal/write-off gain/loss
/// worked examples (§5), year-end interplay (FA-C), tenant scope (FOOTGUN 6 — labelled
/// query-filter, not RLS). Dates are ALWAYS today/future (IClock) per FOOTGUN 5 — the year-end
/// test is the one exception, mirroring YearEndClosingTests' `Today.Year - 1` target-year
/// convention with explicit Closed period rows (not IsOpenAsync's implicit rule).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class FixedAssetServiceTests
{
    private readonly PostgresFixture _fx;
    public FixedAssetServiceTests(PostgresFixture fx) => _fx = fx;

    private static DateOnly Today => new SystemClock().TodayInBangkok();

    private async Task<(TestCompanyFactory.SeededCompany Co, ServiceProvider Sp)> SetupAsync()
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, co.BranchId);
        return (co, sp);
    }

    private static IEnumerable<(int Year, int Month)> MonthRange(DateOnly start, int count) =>
        Enumerable.Range(0, count).Select(i => start.AddMonths(i)).Select(d => (d.Year, d.Month));

    /// <summary>C4 (specs/fix-c4-depreciation-proration.md §6) — FOOTGUN 5 keeps dates
    /// today/future, but the day-proration worked examples need a KNOWN month length; pick the
    /// first qualifying future month with that day count.</summary>
    private static DateOnly FirstFutureMonthWithDays(int days, int day)
    {
        var d = new DateOnly(Today.Year, Today.Month, 1);
        while (DateTime.DaysInMonth(d.Year, d.Month) != days) d = d.AddMonths(1);
        return new DateOnly(d.Year, d.Month, day);
    }

    /// <summary>Direct-seed Open AccountingPeriod rows (mirrors YearEndClosingTests /
    /// Sprint6VatRegisterTests) — PeriodCloseService.IsOpenAsync's default-fallback rule only
    /// treats the CURRENT real month as open when no row exists, so multi-month depreciation
    /// tests must open every target month explicitly.</summary>
    private static async Task OpenPeriodsAsync(ServiceProvider sp, int companyId, IEnumerable<(int Year, int Month)> months)
    {
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        foreach (var (y, m) in months)
            db.AccountingPeriods.Add(new AccountingPeriod
            {
                CompanyId = companyId, Year = y, Month = (short)m, Status = PeriodStatus.Open,
            });
        await db.SaveChangesAsync();
    }

    private static async Task<long> AccountIdByCode(AccountingDbContext db, int companyId, string code) =>
        await db.ChartOfAccounts.Where(a => a.CompanyId == companyId && a.AccountCode == code)
            .Select(a => a.AccountId).FirstAsync();

    private static CreateFixedAssetRequest Req(
        string name, DateOnly acquireDate, decimal cost, decimal salvage, int lifeMonths, DateOnly? startDate = null) =>
        new(name, "EQUIPMENT", acquireDate, null, cost, salvage, lifeMonths, startDate, null, null, null, null);

    private static async Task<long> CreateActiveAssetAsync(
        ServiceProvider sp, string name, DateOnly acquireDate, decimal cost, decimal salvage, int lifeMonths,
        DateOnly? startDate = null)
    {
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var id = await svc.CreateDraftAsync(Req(name, acquireDate, cost, salvage, lifeMonths, startDate), default);
        await svc.ActivateAsync(id, default);
        return id;
    }

    // ── 10.1 — the invariant: full life ties out to the satang (overshoot plug) ────

    [SkippableFact]
    public async Task Depreciation_full_life_ties_out_to_the_satang()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var allMonths = MonthRange(start, 37).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร A", start, 50000.00m, 0m, 36, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");

        for (var i = 0; i < 36; i++)
        {
            var (y, m) = allMonths[i];
            var result = await svc.GenerateDepreciationAsync(y, m, default);
            result.AssetCount.Should().Be(1);
            result.JournalEntryId.Should().NotBeNull();

            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .FirstAsync(j => j.JournalId == result.JournalEntryId);
            je.IsClosingEntry.Should().BeFalse("FA-C — depreciation JEs are never closing entries");
            var dr = je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount;
            var cr = je.Lines.Single(l => l.AccountId == accumAcct).CreditAmount;
            dr.Should().Be(cr);

            if (i < 35) dr.Should().Be(1388.89m, $"month {i + 1} is a normal steady-state charge");
            else dr.Should().Be(1388.85m, "month 36 is the final-scheduled-month plug absorbing the rounding drift");
        }

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(50000.00m, "FA-B — accumulated never exceeds, and here exactly equals, the depreciable base");

        // Month 37 — fully depreciated, no eligible line, no run created.
        var (y37, m37) = allMonths[36];
        var afterFull = await svc.GenerateDepreciationAsync(y37, m37, default);
        afterFull.AssetCount.Should().Be(0);
        afterFull.DepreciationRunId.Should().BeNull();
    }

    // ── 10.1b — Fable review add: undershoot plug (rounded-down monthly) ───────────

    [SkippableFact]
    public async Task Depreciation_undershoot_plug_closes_life_at_exactly_24_months()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var allMonths = MonthRange(start, 25).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร B", start, 50000.00m, 0m, 24, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");

        for (var i = 0; i < 24; i++)
        {
            var (y, m) = allMonths[i];
            var result = await svc.GenerateDepreciationAsync(y, m, default);
            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .FirstAsync(j => j.JournalId == result.JournalEntryId);
            var dr = je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount;
            je.Lines.Single(l => l.AccountId == accumAcct).CreditAmount.Should().Be(dr);

            if (i < 23) dr.Should().Be(2083.33m, $"month {i + 1} is the rounded-DOWN steady-state charge");
            else dr.Should().Be(2083.41m, "month 24 (final scheduled month) is the reverse plug closing the undershoot");
        }

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(50000.00m, "life never exceeds useful_life_months in the undershoot direction either");

        var (y25, m25) = allMonths[24];
        var afterFull = await svc.GenerateDepreciationAsync(y25, m25, default);
        afterFull.AssetCount.Should().Be(0);
        afterFull.DepreciationRunId.Should().BeNull();
    }

    // ── 10.1c-g — C4 (specs/fix-c4-depreciation-proration.md §6): units-indexed schedule +
    // day-prorated first charge, replacing the calendar-plug (T1–T5) ──────────────────

    [SkippableFact]
    public async Task Depreciation_prorates_the_first_month_by_days_and_still_ties_out_to_the_satang()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = FirstFutureMonthWithDays(31, 5); // §3.5(a): daysHeld = 31-5+1 = 27, f = 0.8710
        var allMonths = MonthRange(start, 38).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร T1", start, 50000.00m, 0m, 36, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");

        for (var i = 0; i < 37; i++)
        {
            var (y, m) = allMonths[i];
            var result = await svc.GenerateDepreciationAsync(y, m, default);
            result.AssetCount.Should().Be(1);
            result.JournalEntryId.Should().NotBeNull();

            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .FirstAsync(j => j.JournalId == result.JournalEntryId);
            var dr = je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount;
            var cr = je.Lines.Single(l => l.AccountId == accumAcct).CreditAmount;
            dr.Should().Be(cr);

            if (i == 0) dr.Should().Be(1209.72m, "charge #1 is the day-prorated first unit (27/31 days, §3.5a)");
            else if (i < 36) dr.Should().Be(1388.89m, $"charge {i + 1} is a normal steady-state whole unit");
            else dr.Should().Be(179.13m, "charge #37 is the units-final charge absorbing the rounding drift");
        }

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(50000.00m, "I1 — sum-to-exact with a day-prorated first unit");

        var lineCount = await db.DepreciationRunLines.AsNoTracking().CountAsync(l => l.FixedAssetId == assetId);
        lineCount.Should().Be(37, "one line per run: the prorated first unit + 35 steady units + the units-final charge");

        var (y38, m38) = allMonths[37];
        var afterFull = await svc.GenerateDepreciationAsync(y38, m38, default);
        afterFull.AssetCount.Should().Be(0);
        afterFull.DepreciationRunId.Should().BeNull();
    }

    [SkippableFact]
    public async Task Prorated_asset_with_rounded_down_monthly_closes_exactly_on_the_last_unit()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = FirstFutureMonthWithDays(30, 16); // §3.5(b): daysHeld = 30-16+1 = 15, f = 0.5000
        var allMonths = MonthRange(start, 25).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร T2", start, 50000.00m, 0m, 24, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");

        for (var i = 0; i < 25; i++)
        {
            var (y, m) = allMonths[i];
            var result = await svc.GenerateDepreciationAsync(y, m, default);
            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .FirstAsync(j => j.JournalId == result.JournalEntryId);
            var dr = je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount;
            je.Lines.Single(l => l.AccountId == accumAcct).CreditAmount.Should().Be(dr);

            if (i == 0) dr.Should().Be(1041.67m, "charge #1 is the day-prorated first unit (15/30 days, §3.5b)");
            else if (i < 24) dr.Should().Be(2083.33m, $"charge {i + 1} is the rounded-DOWN steady-state whole unit");
            else dr.Should().Be(1041.74m, "charge #25 is the units-final charge closing the undershoot exactly");
        }

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(50000.00m, "I1 — sum-to-exact in the undershoot direction too");

        var lineCount = await db.DepreciationRunLines.AsNoTracking().CountAsync(l => l.FixedAssetId == assetId);
        lineCount.Should().Be(25);
    }

    [SkippableFact]
    public async Task Skipped_month_is_not_absorbed_by_the_final_charge()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1); // day 1 -> f = 1.0000, §3.5(c)
        var allMonths = MonthRange(start, 5).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร T3", start, 100.00m, 0m, 3, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");

        // Month 1 (the start month) is deliberately NEVER run — the L3-3 shape (a start month
        // backdated into an already-closed period, whose run then never happens).
        var expected = new[] { 33.33m, 33.33m, 33.34m };
        for (var i = 0; i < 3; i++)
        {
            var (y, m) = allMonths[i + 1]; // runs 2, 3, 4
            var result = await svc.GenerateDepreciationAsync(y, m, default);
            var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
                .FirstAsync(j => j.JournalId == result.JournalEntryId);
            var dr = je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount;
            je.Lines.Single(l => l.AccountId == accumAcct).CreditAmount.Should().Be(dr);
            dr.Should().Be(expected[i]);
            if (i == 2)
                dr.Should().NotBe(66.67m,
                    "L3-3 — a skipped month must never be absorbed into the final charge; the " +
                    "units-indexed schedule slides one calendar month later instead");
        }

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(100.00m, "I1 — sum-to-exact even with a skipped month");

        var lineCount = await db.DepreciationRunLines.AsNoTracking().CountAsync(l => l.FixedAssetId == assetId);
        lineCount.Should().Be(3, "one line per actual run — the skipped month produced none and absorbed none");

        var (y5, m5) = allMonths[4];
        var afterFull = await svc.GenerateDepreciationAsync(y5, m5, default);
        afterFull.AssetCount.Should().Be(0);
        afterFull.DepreciationRunId.Should().BeNull();
    }

    [SkippableFact]
    public async Task Legacy_asset_with_null_months_depreciated_resolves_units_from_its_posted_lines()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = new DateOnly(Today.Year, Today.Month, 1);
        var allMonths = MonthRange(start, 3).ToList();
        await OpenPeriodsAsync(sp, co.CompanyId, allMonths);

        var assetId = await CreateActiveAssetAsync(sp, "เครื่องจักร T4", start, 100.00m, 0m, 3, start);

        await using (var s1 = sp.CreateAsyncScope())
        {
            var svc1 = s1.ServiceProvider.GetRequiredService<IFixedAssetService>();
            await svc1.GenerateDepreciationAsync(allMonths[0].Year, allMonths[0].Month, default);
            await svc1.GenerateDepreciationAsync(allMonths[1].Year, allMonths[1].Month, default);
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
            var asset = await db2.FixedAssets.FirstAsync(a => a.FixedAssetId == assetId);
            asset.AccumulatedDepreciation.Should().Be(66.66m, "2 months at 33.33 each");
            asset.MonthsDepreciated = null; // simulate a row created before this feature (grandfather case)
            await db2.SaveChangesAsync();
        }

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");

        var result = await svc.GenerateDepreciationAsync(allMonths[2].Year, allMonths[2].Month, default);
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == result.JournalEntryId);
        je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount.Should().Be(33.34m,
            "units resolved to 2 from the posted-line count -> this charge is the final unit");

        var final = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        final.AccumulatedDepreciation.Should().Be(100.00m, "I2 — grandfathering never breaks sum-to-exact");
        final.MonthsDepreciated.Should().Be(3m, "the engine re-derives and re-stores units once it resolves them");

        var lineCount = await db.DepreciationRunLines.AsNoTracking().CountAsync(l => l.FixedAssetId == assetId);
        lineCount.Should().Be(3);
    }

    [SkippableFact]
    public async Task Tiny_asset_whose_prorated_first_charge_rounds_to_zero_still_produces_a_line()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var start = FirstFutureMonthWithDays(31, 20); // §3.5: f = 12/31 = 0.3871, 0.01 x 0.3871 = 0.0039 -> floors to 0
        await OpenPeriodsAsync(sp, co.CompanyId, new[] { (start.Year, start.Month) });

        var assetId = await CreateActiveAssetAsync(sp, "สินทรัพย์จิ๋ว T5", start, 0.36m, 0m, 36, start);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");

        var result = await svc.GenerateDepreciationAsync(start.Year, start.Month, default);
        result.AssetCount.Should().Be(1, "I4 — a floored-to-0.01 charge still yields a run row");
        result.DepreciationRunId.Should().NotBeNull();

        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .FirstAsync(j => j.JournalId == result.JournalEntryId);
        je.Lines.Single(l => l.AccountId == depExpenseAcct).DebitAmount.Should().Be(0.01m,
            "the true prorated charge (0.0039) rounds to 0.00 and is floored to the satang (I4)");

        var periodSvc = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
        var closeResult = await periodSvc.CloseAsync(start.Year, start.Month, "T5 close", default);
        closeResult.Year.Should().Be(start.Year, "I4's exit: the floored charge produced a run row, so period-close is never trapped");
    }

    // ── 10.2 — FA-A double-book guard ───────────────────────────────────────────────

    [SkippableFact]
    public async Task Activate_posts_no_journal_entry()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var before = await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId);

        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var id = await svc.CreateDraftAsync(Req("โต๊ะทำงาน", Today, 10000m, 0m, 12), default);
        await svc.ActivateAsync(id, default);

        var after = await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId);
        after.Should().Be(before, "FA-A — the linked cost is already in the GL; activation posts nothing");

        var detail = await svc.GetDetailAsync(id, default);
        detail!.Status.Should().Be(nameof(FixedAssetStatus.Active));
        detail.DocNo.Should().Contain("FA");
    }

    // ── Tier-2 review fix (2026-07-10) — period-close deadlock guard ────────────────

    [SkippableFact]
    public async Task Activate_throws_when_monthly_amount_rounds_to_zero()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();

        // DepreciableBase = 0.10, MonthlyAmount = round(0.10/36, 2) = 0.00 — would otherwise
        // never accrue a run line and permanently block period-close (PeriodCloseService's
        // due-check keeps seeing accumulated(0) < base(0.10) forever).
        var id = await svc.CreateDraftAsync(Req("สินทรัพย์ฐานเล็กมาก", Today, 100.10m, 100.00m, 36), default);

        (await Assert.ThrowsAsync<DomainException>(() => svc.ActivateAsync(id, default)))
            .Code.Should().Be("fixed_asset.monthly_amount_zero");
    }

    // ── 10.3 — idempotency ──────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task GenerateDepreciation_called_twice_is_idempotent()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var (y, m) = (Today.Year, Today.Month);
        await OpenPeriodsAsync(sp, co.CompanyId, [(y, m)]);
        await CreateActiveAssetAsync(sp, "รถยนต์", Today, 120000m, 0m, 60, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var r1 = await svc.GenerateDepreciationAsync(y, m, default);
        var r2 = await svc.GenerateDepreciationAsync(y, m, default);

        r1.AlreadyExisted.Should().BeFalse();
        r2.AlreadyExisted.Should().BeTrue();
        r2.DepreciationRunId.Should().Be(r1.DepreciationRunId);
        r2.JournalEntryId.Should().Be(r1.JournalEntryId);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.DepreciationRuns.CountAsync(r => r.CompanyId == co.CompanyId && r.PeriodYear == y && r.PeriodMonth == m))
            .Should().Be(1);
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId && j.Reference == $"DEP-{y}{m:D2}"))
            .Should().Be(1);
    }

    // ── 10.4 — race (genuine concurrency, unique-index backstop) ────────────────────

    [SkippableFact]
    public async Task GenerateDepreciation_concurrent_calls_post_exactly_one_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var (y, m) = (Today.Year, Today.Month);
        await OpenPeriodsAsync(sp, co.CompanyId, [(y, m)]);
        await CreateActiveAssetAsync(sp, "เครื่องปรับอากาศ", Today, 30000m, 0m, 36, Today);

        async Task<bool> TryGenerate()
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
            try { await svc.GenerateDepreciationAsync(y, m, default); return true; }
            catch (DomainException ex) when (ex.Code == "depreciation.already_posted") { return false; }
        }

        // FA-E — the loser either observes the winner's commit via the idempotent pre-check
        // (returns AlreadyExisted=true, no exception) or hits the unique-index backstop and
        // throws depreciation.already_posted; both are correct. The invariant that matters is
        // the DATA below: never two runs, never two journal entries (mirrors expense-claims'
        // Double_pay_race test — "success" of both callers is a valid outcome here).
        var results = await Task.WhenAll(Task.Run(TryGenerate), Task.Run(TryGenerate));
        results.Should().Contain(true, "at least one concurrent call must succeed");

        await using var s2 = sp.CreateAsyncScope();
        var db = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await db.DepreciationRuns.CountAsync(r => r.CompanyId == co.CompanyId && r.PeriodYear == y && r.PeriodMonth == m))
            .Should().Be(1, "a concurrent double-generate must not post two runs");
        (await db.JournalEntries.CountAsync(j => j.CompanyId == co.CompanyId && j.Reference == $"DEP-{y}{m:D2}"))
            .Should().Be(1, "a concurrent double-generate must not post two journal entries");
    }

    // ── 10.5 — period-close hook ─────────────────────────────────────────────────────

    [SkippableFact]
    public async Task PeriodClose_hook_blocks_then_allows_close_around_the_depreciation_run()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var (y, m) = (Today.Year, Today.Month);
        await CreateActiveAssetAsync(sp, "แล็ปท็อป", Today, 25000m, 0m, 24, Today);

        await using var s = sp.CreateAsyncScope();
        var periodSvc = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();

        // (a) active asset due depreciation, no run yet -> blocked.
        (await Assert.ThrowsAsync<DomainException>(() => periodSvc.CloseAsync(y, m, null, default)))
            .Code.Should().Be("period.depreciation_required");

        // (b) after the run is posted -> close succeeds.
        var faSvc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        await faSvc.GenerateDepreciationAsync(y, m, default);
        var result = await periodSvc.CloseAsync(y, m, "closed after depreciation", default);
        result.Year.Should().Be(y);
        result.Month.Should().Be(m);
    }

    [SkippableFact]
    public async Task PeriodClose_hook_allows_close_when_no_assets_are_due()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var (y, m) = (Today.Year, Today.Month);

        await using var s = sp.CreateAsyncScope();
        var periodSvc = s.ServiceProvider.GetRequiredService<IPeriodCloseService>();
        var result = await periodSvc.CloseAsync(y, m, null, default);
        result.Year.Should().Be(y);
    }

    // ── 10.6 / 10.7 — disposal gain/loss + write-off worked examples verbatim ──────

    private static async Task<long> SeedDisposableAssetAsync(ServiceProvider sp, int companyId, decimal accumulated)
    {
        var id = await CreateActiveAssetAsync(sp, "รถบรรทุก " + Guid.NewGuid().ToString("N")[..6], Today, 50000.00m, 0m, 60, Today);
        await using var s0 = sp.CreateAsyncScope();
        var db0 = s0.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var a = await db0.FixedAssets.FirstAsync(x => x.FixedAssetId == id);
        // Direct DB patch — test setup only, not exercising the depreciation engine (already
        // covered by 10.1/10.1b); this just needs a pre-existing accumulated balance in place.
        a.AccumulatedDepreciation = accumulated;
        await db0.SaveChangesAsync();
        return id;
    }

    [SkippableFact]
    public async Task Dispose_gain_reproduces_the_worked_example_JE_exactly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var assetId = await SeedDisposableAssetAsync(sp, co.CompanyId, 30000.00m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var result = await svc.DisposeAsync(assetId,
            new DisposeFixedAssetRequest(Today, 25000.00m, 1750.00m, "บริษัท ผู้ซื้อ จำกัด"), default);

        result.Nbv.Should().Be(20000.00m);
        result.GainLoss.Should().Be(5000.00m);
        result.Status.Should().Be(nameof(FixedAssetStatus.Disposed));

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines).FirstAsync(j => j.JournalId == result.JournalEntryId);
        je.TotalDebit.Should().Be(56750.00m);
        je.TotalCredit.Should().Be(56750.00m);
        je.Lines.Should().HaveCount(5);

        var cashAcct = await AccountIdByCode(db, co.CompanyId, "1110");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");
        var costAcct = await AccountIdByCode(db, co.CompanyId, "1610");
        var vatAcct = await AccountIdByCode(db, co.CompanyId, "2151");
        var gainAcct = await AccountIdByCode(db, co.CompanyId, "4200");

        je.Lines.Should().ContainSingle(l => l.AccountId == cashAcct).Which.DebitAmount.Should().Be(26750.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == accumAcct).Which.DebitAmount.Should().Be(30000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == costAcct).Which.CreditAmount.Should().Be(50000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == vatAcct).Which.CreditAmount.Should().Be(1750.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == gainAcct).Which.CreditAmount.Should().Be(5000.00m);

        var asset = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        asset.Status.Should().Be(FixedAssetStatus.Disposed);
        asset.AccumulatedDepreciation.Should().Be(30000.00m, "disposal clears the asset via the JE, not by zeroing this audit field");
    }

    [SkippableFact]
    public async Task Dispose_loss_reproduces_the_worked_example_JE_exactly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var assetId = await SeedDisposableAssetAsync(sp, co.CompanyId, 30000.00m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var result = await svc.DisposeAsync(assetId,
            new DisposeFixedAssetRequest(Today, 15000.00m, 1050.00m, null), default);

        result.Nbv.Should().Be(20000.00m);
        result.GainLoss.Should().Be(-5000.00m);

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines).FirstAsync(j => j.JournalId == result.JournalEntryId);
        je.TotalDebit.Should().Be(51050.00m);
        je.TotalCredit.Should().Be(51050.00m);
        je.Lines.Should().HaveCount(5, "cash + accum-dep + cost + VAT + loss");

        var cashAcct = await AccountIdByCode(db, co.CompanyId, "1110");
        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");
        var costAcct = await AccountIdByCode(db, co.CompanyId, "1610");
        var vatAcct = await AccountIdByCode(db, co.CompanyId, "2151");
        var lossAcct = await AccountIdByCode(db, co.CompanyId, "5460");

        je.Lines.Should().ContainSingle(l => l.AccountId == cashAcct).Which.DebitAmount.Should().Be(16050.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == accumAcct).Which.DebitAmount.Should().Be(30000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == lossAcct).Which.DebitAmount.Should().Be(5000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == costAcct).Which.CreditAmount.Should().Be(50000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == vatAcct).Which.CreditAmount.Should().Be(1050.00m);
    }

    [SkippableFact]
    public async Task WriteOff_reproduces_the_worked_example_JE_exactly()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var assetId = await SeedDisposableAssetAsync(sp, co.CompanyId, 30000.00m);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var result = await svc.WriteOffAsync(assetId, new WriteOffFixedAssetRequest(Today, "เสียหายจากอุบัติเหตุ"), default);

        result.Nbv.Should().Be(20000.00m);
        result.GainLoss.Should().Be(-20000.00m);
        result.Status.Should().Be(nameof(FixedAssetStatus.WrittenOff));

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var je = await db.JournalEntries.AsNoTracking().Include(j => j.Lines).FirstAsync(j => j.JournalId == result.JournalEntryId);
        je.TotalDebit.Should().Be(50000.00m);
        je.TotalCredit.Should().Be(50000.00m);
        je.Lines.Should().HaveCount(3, "no cash, no VAT line — write-off has neither");

        var accumAcct = await AccountIdByCode(db, co.CompanyId, "1690");
        var costAcct = await AccountIdByCode(db, co.CompanyId, "1610");
        var lossAcct = await AccountIdByCode(db, co.CompanyId, "5460");

        je.Lines.Should().ContainSingle(l => l.AccountId == accumAcct).Which.DebitAmount.Should().Be(30000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == lossAcct).Which.DebitAmount.Should().Be(20000.00m);
        je.Lines.Should().ContainSingle(l => l.AccountId == costAcct).Which.CreditAmount.Should().Be(50000.00m);

        var asset = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        asset.Status.Should().Be(FixedAssetStatus.WrittenOff);
        asset.WriteoffReason.Should().Be("เสียหายจากอุบัติเหตุ");
    }

    // ── L3-9 (fix-r2 PLAN-fix-findings-r2.md U5) — disposal date must not precede
    // acquire/depreciation-start (findings-r2/findings-leg3.md) ────────────────────────

    [SkippableFact]
    public async Task Dispose_refuses_when_disposal_date_is_before_acquire_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        // Acquired NEXT month so "a disposal date before acquisition" still lands in the
        // OPEN current-real-month period (period.EnsureOpenAsync's implicit default-open
        // rule) — exercises the date-order guard without touching period state at all.
        var acquireDate = Today.AddMonths(1);
        var assetId = await CreateActiveAssetAsync(sp, "รถยนต์ L3-9a", acquireDate, 50000.00m, 0m, 60, acquireDate);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var act = () => svc.DisposeAsync(assetId, new DisposeFixedAssetRequest(Today, 0m, null, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.disposal_date_invalid");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var asset = await db.FixedAssets.AsNoTracking().FirstAsync(a => a.FixedAssetId == assetId);
        asset.Status.Should().Be(FixedAssetStatus.Active, "a refused disposal must not mutate the asset (L3-9 — the bug let it post 200 + a real JE)");
    }

    [SkippableFact]
    public async Task Dispose_refuses_when_disposal_date_is_before_depreciation_start_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var acquireDate = Today;
        var depStart = Today.AddMonths(1);   // AcquireDate OK, but before DepreciationStartDate.
        var assetId = await CreateActiveAssetAsync(sp, "รถยนต์ L3-9b", acquireDate, 50000.00m, 0m, 60, depStart);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var act = () => svc.DisposeAsync(assetId, new DisposeFixedAssetRequest(Today, 0m, null, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.disposal_date_invalid");
    }

    [SkippableFact]
    public async Task Dispose_succeeds_when_disposal_date_equals_acquire_date()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var assetId = await CreateActiveAssetAsync(sp, "รถยนต์ L3-9c", Today, 50000.00m, 0m, 60, Today);

        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var result = await svc.DisposeAsync(assetId, new DisposeFixedAssetRequest(Today, 0m, null, null), default);

        result.Status.Should().Be(nameof(FixedAssetStatus.Disposed),
            "disposal_date == acquire_date is the inclusive boundary, not refused");
    }

    // ── 10.8 — year-end interplay (FA-C) ────────────────────────────────────────────

    [SkippableFact]
    public async Task YearEnd_close_sweeps_5450_but_ProfitLoss_still_shows_the_depreciation_expense()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        var fy = Today.Year - 1;
        var fyStart = new DateOnly(fy, 1, 1);
        var fiscalEnd = new DateOnly(fy, 12, 31);
        var months = Enumerable.Range(0, 12).Select(i => fyStart.AddMonths(i)).Select(d => (d.Year, d.Month)).ToList();

        await OpenPeriodsAsync(sp, co.CompanyId, months);
        await CreateActiveAssetAsync(sp, "เครื่องพิมพ์", fyStart, 12000.00m, 0m, 12, fyStart);

        await using (var s0 = sp.CreateAsyncScope())
        {
            var faSvc0 = s0.ServiceProvider.GetRequiredService<IFixedAssetService>();
            foreach (var (y, m) in months)
                await faSvc0.GenerateDepreciationAsync(y, m, default);
        }

        // Flip all 12 months to Closed directly (mirrors YearEndClosingTests D6 — year-end
        // close requires explicit Closed rows, not IsOpenAsync's implicit current-month rule).
        await using (var s1 = sp.CreateAsyncScope())
        {
            var db1 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
            foreach (var (y, m) in months)
            {
                var p = await db1.AccountingPeriods.FirstAsync(x => x.CompanyId == co.CompanyId && x.Year == y && x.Month == (short)m);
                p.Status = PeriodStatus.Closed;
            }
            await db1.SaveChangesAsync();
        }

        await using var s = sp.CreateAsyncScope();
        var yearSvc = s.ServiceProvider.GetRequiredService<IYearCloseService>();
        var closeResult = await yearSvc.CloseAsync(fy, "test close with depreciation", default);
        closeResult.NetProfit.Should().Be(-12000.00m, "12 months x 1,000.00 depreciation, no revenue");

        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var depExpenseAcct = await AccountIdByCode(db, co.CompanyId, "5450");
        var jesAsOfEnd = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .Where(j => j.CompanyId == co.CompanyId && j.Status == DocumentStatus.Posted && j.DocDate <= fiscalEnd)
            .ToListAsync();
        var depBalanceAsOfEnd = jesAsOfEnd.SelectMany(j => j.Lines)
            .Where(l => l.AccountId == depExpenseAcct).Sum(l => l.DebitAmount - l.CreditAmount);
        depBalanceAsOfEnd.Should().Be(0m, "the year-end closing JE sweeps 5450 to zero as-of fiscalEnd (point-in-time view includes it)");

        var reportSvc = s.ServiceProvider.GetRequiredService<IFinancialReportService>();
        var pl = await reportSvc.ProfitLossAsync(fyStart, fiscalEnd, null, true, default);
        pl.Totals.Expense.Should().Be(12000.00m,
            "the dep JEs (IsClosingEntry=false) stay in the range P&L even after year-end close — no year-end code edit needed");
    }

    // ── 10.9 — tenant scope (labelled query-filter, FOOTGUN 6) ──────────────────────

    [SkippableFact]
    public async Task Asset_from_company_A_is_invisible_to_company_B_via_query_filter()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (coA, spA) = await SetupAsync();
        long id;
        await using (var sA = spA.CreateAsyncScope())
        {
            var svcA = sA.ServiceProvider.GetRequiredService<IFixedAssetService>();
            id = await svcA.CreateDraftAsync(Req("สินทรัพย์ A", Today, 1000m, 0m, 12), default);
        }

        var (coB, _) = await SetupAsync();
        var spB = TestCompanyFactory.BuildProvider(_fx.ConnectionString, coB.CompanyId, coB.BranchId);
        await using var sB = spB.CreateAsyncScope();
        var svcB = sB.ServiceProvider.GetRequiredService<IFixedAssetService>();

        // EF global query filter (ITenantOwned -> CompanyId), NOT a DB-level RLS assertion —
        // teas_test connects as superuser (RLS bypassed), see FOOTGUN 6.
        (await svcB.GetDetailAsync(id, default)).Should().BeNull();
        var dbB = sB.ServiceProvider.GetRequiredService<AccountingDbContext>();
        (await dbB.FixedAssets.AnyAsync(a => a.FixedAssetId == id)).Should().BeFalse();
    }

    // ── Codex review finding #5 (2026-07-10) — account OVERRIDE type/status validation ──

    [SkippableFact]
    public async Task DepExpenseAccountId_override_of_the_wrong_type_Asset_not_Expense_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        // 1130 (AR) is a seeded Asset account — wrong type for the dep-expense slot (Expense).
        var wrongTypeAcctId = await AccountIdByCode(db, co.CompanyId, "1130");

        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var act = () => svc.CreateDraftAsync(
            new CreateFixedAssetRequest("โต๊ะ", "EQUIPMENT", Today, null, 1000m, 0m, 12, null,
                null, null, wrongTypeAcctId, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.account_invalid");
    }

    [SkippableFact]
    public async Task AssetCostAccountId_override_pointing_at_a_header_account_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var headerAcct = new ChartOfAccount
        {
            CompanyId = co.CompanyId, AccountCode = "1611", AccountNameTh = "บัญชีหลักสินทรัพย์",
            AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
            IsHeader = true, IsActive = true, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.ChartOfAccounts.Add(headerAcct);
        await db.SaveChangesAsync();

        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();
        var act = () => svc.CreateDraftAsync(
            new CreateFixedAssetRequest("โต๊ะ", "EQUIPMENT", Today, null, 1000m, 0m, 12, null,
                headerAcct.AccountId, null, null, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.account_invalid");
    }

    // ── Codex review finding #5b (2026-07-10) — VendorInvoiceId tenant-scoped existence ──

    [SkippableFact]
    public async Task VendorInvoiceId_pointing_at_a_nonexistent_row_is_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IFixedAssetService>();

        var act = () => svc.CreateDraftAsync(
            new CreateFixedAssetRequest("โต๊ะ", "EQUIPMENT", Today, 999_999_999L, 1000m, 0m, 12, null,
                null, null, null, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.vendor_invoice_invalid");
    }

    [SkippableFact]
    public async Task VendorInvoiceId_from_another_company_is_rejected_tenant_scoped()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (coA, spA) = await SetupAsync();
        await using var sA = spA.CreateAsyncScope();
        var dbA = sA.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var viA = new Accounting.Domain.Entities.Purchase.VendorInvoice
        {
            CompanyId = coA.CompanyId, BranchId = coA.BranchId, VendorId = 1,
            VendorTaxInvoiceNo = "TEST-" + Guid.NewGuid().ToString("N")[..8],
            VendorTaxInvoiceDate = Today, VatClaimPeriod = Today.Year * 100 + Today.Month,
            DocDate = Today, VendorName = "ผู้ขาย A", VendorType = CustomerType.Corporate,
            SubtotalAmount = 100m, VatAmount = 0m, TotalAmount = 100m, TotalAmountThb = 100m,
        };
        dbA.VendorInvoices.Add(viA);
        await dbA.SaveChangesAsync();

        var (coB, spB) = await SetupAsync();
        await using var sB = spB.CreateAsyncScope();
        var svcB = sB.ServiceProvider.GetRequiredService<IFixedAssetService>();

        var act = () => svcB.CreateDraftAsync(
            new CreateFixedAssetRequest("โต๊ะ", "EQUIPMENT", Today, viA.VendorInvoiceId, 1000m, 0m, 12, null,
                null, null, null, null), default);

        (await Assert.ThrowsAsync<DomainException>(act)).Code.Should().Be("fixed_asset.vendor_invoice_invalid");
    }

    // ── Codex review finding #6 (2026-07-10) — Draft-edit concurrency ──────────────────

    /// <summary>Before the fix, UpdateDraftAsync mutated fields and saved via a bare
    /// db.SaveChangesAsync with NO Version++, so a Draft edit racing another writer never
    /// conflict-detected at all (silent clobber) instead of 409-ing. Same deterministic
    /// two-preloaded-context technique as ExpenseClaimServiceTests' concurrency proofs.</summary>
    [SkippableFact]
    public async Task Concurrent_UpdateDraft_second_stale_save_throws_DbUpdateConcurrencyException()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (co, sp) = await SetupAsync();

        long id;
        await using (var s0 = sp.CreateAsyncScope())
        {
            var svc0 = s0.ServiceProvider.GetRequiredService<IFixedAssetService>();
            id = await svc0.CreateDraftAsync(Req("สินทรัพย์แข่งขัน", Today, 1000m, 0m, 12), default);
        }

        await using var s1 = sp.CreateAsyncScope();
        var db1 = s1.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var asset1 = await db1.FixedAssets.FirstAsync(a => a.FixedAssetId == id);

        await using var s2 = sp.CreateAsyncScope();
        var db2 = s2.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var asset2 = await db2.FixedAssets.FirstAsync(a => a.FixedAssetId == id);

        // Both contexts loaded the SAME original Version=0 — simulates two concurrent
        // Draft-edit callers each having read the row before either wrote.
        asset1.Name = "edit A"; asset1.Version++;
        asset2.Name = "edit B"; asset2.Version++;

        await db1.SaveChangesAsync();   // winner — Version 0 -> 1

        var loser = async () => await db2.SaveChangesAsync();
        await loser.Should().ThrowAsync<DbUpdateConcurrencyException>(
            "db2's tracked original Version no longer matches the row db1 just committed — before " +
            "the fix, UpdateDraftAsync never incremented Version, so this stale write would have " +
            "silently succeeded and clobbered db1's edit instead of conflicting.");
    }
}

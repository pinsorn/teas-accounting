using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Domain.Tax;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Tax;

/// <summary>
/// Phase C-C — CIT year-data store: per-FY effective net profit (override wins),
/// ม.65ทวิ/65ตรี adjustment lines, the persisted ภ.ง.ด.51 estimate (ม.67ตรี), the
/// auto-SME profile (§4.6) and the ม.65ตรี(12) loss carry-in.
/// Every unique-constrained fiscal year comes from <see cref="TestIds.FutureFiscalYear"/>;
/// tests tolerate (or delete) pre-existing rows for their chosen years (shared teas_test DB).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CitYearDataServiceTests
{
    private readonly PostgresFixture _fx;
    public CitYearDataServiceTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Upsert_year_creates_then_updates_and_effective_prefers_override()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = TestIds.FutureFiscalYear();

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
            var dto = await svc.UpsertYearAsync(
                year, new UpsertCitYearRequest(-100_000m, "loss year"), default);
            dto.FiscalYear.Should().Be(year);
            dto.OverrideNetProfit.Should().Be(-100_000m);
            dto.Note.Should().Be("loss year");
            dto.EffectiveNetProfit.Should().Be(-100_000m, "override wins over computed");
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
            var dto = await svc.UpsertYearAsync(
                year, new UpsertCitYearRequest(50_000m, "amended"), default);
            dto.OverrideNetProfit.Should().Be(50_000m);
            dto.Note.Should().Be("amended");
            dto.EffectiveNetProfit.Should().Be(50_000m);

            var rows = await svc.ListYearsAsync(default);
            rows.Where(r => r.FiscalYear == year).Should().ContainSingle(
                "upsert must update the existing (company, fiscal_year) row, not insert a second");
        }
    }

    [SkippableFact]
    public async Task Adjustments_crud_round_trip_and_unknown_id_throws_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = TestIds.FutureFiscalYear();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();

        // ม.65ตรี add-back (> 0) + ม.65ทวิ exempt income (< 0) — signed amounts.
        var addBack = await svc.CreateAdjustmentAsync(year,
            new UpsertCitAdjustmentRequest("ม.65ตรี(3)", "ค่ารับรองเกินเกณฑ์", 5_000m), default);
        var exempt = await svc.CreateAdjustmentAsync(year,
            new UpsertCitAdjustmentRequest("ม.65ทวิ(10)", "เงินปันผลยกเว้น", -2_000m), default);

        var list = await svc.ListAdjustmentsAsync(year, default);
        list.Select(a => a.CitAdjustmentId).Should().Contain([addBack.CitAdjustmentId, exempt.CitAdjustmentId]);

        var updated = await svc.UpdateAdjustmentAsync(addBack.CitAdjustmentId,
            new UpsertCitAdjustmentRequest("ม.65ตรี(4)", "รายจ่ายส่วนตัว", 7_500m), default);
        updated.LegalRefCode.Should().Be("ม.65ตรี(4)");
        updated.Label.Should().Be("รายจ่ายส่วนตัว");
        updated.Amount.Should().Be(7_500m);

        await svc.DeleteAdjustmentAsync(exempt.CitAdjustmentId, default);
        (await svc.ListAdjustmentsAsync(year, default))
            .Should().NotContain(a => a.CitAdjustmentId == exempt.CitAdjustmentId);

        var delUnknown = async () => await svc.DeleteAdjustmentAsync(long.MaxValue, default);
        (await delUnknown.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("cit.adjustment_not_found");

        var createInvalid = async () => await svc.CreateAdjustmentAsync(year,
            new UpsertCitAdjustmentRequest("", "no ref", 1m), default);
        (await createInvalid.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("cit.adjustment_invalid");
    }

    [SkippableFact]
    public async Task RecordPnd51Estimate_stores_estimate_and_prepaid_per_calculator() // ม.67ตรี
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = TestIds.FutureFiscalYear();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();

        // ม.67ตรี — the stored estimate must equal what was filed; prepaid = the ม.67ทวิ
        // half-year prepayment from the same calculator the ภ.ง.ด.51 PDF used.
        var dto = await svc.RecordPnd51EstimateAsync(
            year, estimatedProfit: 1_000_000m, whtH1: 10_000m, isSme: false, default);

        dto.FiscalYear.Should().Be(year);
        dto.Pnd51EstimatedProfit.Should().Be(1_000_000m);
        dto.Pnd51Prepaid.Should().Be(CitCalculator.HalfYearPrepayment(
            1_000_000m, 10_000m, CitRateSchedule.General()));
        dto.Pnd51Prepaid.Should().Be(90_000m); // 1M × 20% / 2 − 10k WHT
    }

    [SkippableFact]
    public async Task Profile_classifies_sme_by_paid_up_capital_and_never_silently_sme()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = TestIds.FutureFiscalYear(); // far-future FY ⇒ revenue 0 ≤ 30M

        decimal? original;
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            original = await db.Companies.AsNoTracking()
                .Where(c => c.CompanyId == 1).Select(c => c.PaidUpCapital).FirstAsync();
        }

        async Task SetCapital(decimal? cap)
        {
            await using var s = sp.CreateAsyncScope();
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await db.Companies.Where(c => c.CompanyId == 1)
                .ExecuteUpdateAsync(u => u.SetProperty(c => c.PaidUpCapital, cap));
        }

        async Task<CitProfileDto> ProfileAsync()
        {
            await using var s = sp.CreateAsyncScope();
            var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
            return await svc.ProfileAsync(year, default);
        }

        try
        {
            await SetCapital(4_000_000m); // §4.6 — paid-up ≤ 5M ∧ revenue ≤ 30M ⇒ SME
            var sme = await ProfileAsync();
            sme.PaidUpCapital.Should().Be(4_000_000m);
            sme.RevenueFullYear.Should().BeLessThanOrEqualTo(30_000_000m);
            sme.IsSme.Should().BeTrue();

            await SetCapital(6_000_000m); // > 5M ⇒ General
            (await ProfileAsync()).IsSme.Should().BeFalse();

            await SetCapital(null);       // unknown capital ⇒ General — never silently SME
            (await ProfileAsync()).IsSme.Should().BeFalse();
        }
        finally
        {
            await SetCapital(original);
        }
    }

    [SkippableFact]
    public async Task Profile_loss_carry_in_walks_effective_history() // ม.65ตรี(12)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = TestIds.FutureFiscalYear();

        // Deterministic window: drop any leftover rows whose losses could still feed
        // `year` (losses of year-5 … year-1 are non-expired) from earlier runs.
        await using (var s = sp.CreateAsyncScope())
        {
            var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
            await db.CitYearSummaries
                .Where(r => r.FiscalYear >= year - 6 && r.FiscalYear <= year)
                .ExecuteDeleteAsync();
        }

        await using (var s = sp.CreateAsyncScope())
        {
            var svc = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
            // Profit BEFORE the loss year consumes nothing of the later loss (oldest-first walk);
            // the year-1 loss of 100k therefore carries into `year` in full.
            await svc.UpsertYearAsync(year - 2, new UpsertCitYearRequest(20_000m, "profit"), default);
            await svc.UpsertYearAsync(year - 1, new UpsertCitYearRequest(-100_000m, "loss"), default);
        }

        await using (var s2 = sp.CreateAsyncScope())
        {
            var svc = s2.ServiceProvider.GetRequiredService<ICitYearDataService>();
            var profile = await svc.ProfileAsync(year, default);
            profile.LossCarryIn.Should().Be(100_000m); // ม.65ตรี(12) — 5-period window, oldest first
        }
    }

    [SkippableFact]
    public async Task Adjustments_are_invisible_to_another_tenant()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var year = TestIds.FutureFiscalYear();

        long id;
        await using (var sp1 = Provider(companyId: 1))
        await using (var s1 = sp1.CreateAsyncScope())
        {
            var svc = s1.ServiceProvider.GetRequiredService<ICitYearDataService>();
            id = (await svc.CreateAdjustmentAsync(year,
                new UpsertCitAdjustmentRequest("ม.65ตรี(5)", "tenant-1 only", 123m), default))
                .CitAdjustmentId;
        }

        await using var sp2 = Provider(companyId: 2);
        await using var s2 = sp2.CreateAsyncScope();
        var svc2 = s2.ServiceProvider.GetRequiredService<ICitYearDataService>();

        (await svc2.ListAdjustmentsAsync(year, default))
            .Should().NotContain(a => a.CitAdjustmentId == id, "company_id isolation (§4.7)");

        var update = async () => await svc2.UpdateAdjustmentAsync(id,
            new UpsertCitAdjustmentRequest("ม.65ตรี(5)", "hijack", 1m), default);
        (await update.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("cit.adjustment_not_found");
    }
}

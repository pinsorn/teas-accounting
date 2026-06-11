using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Tax;
using Accounting.Domain.Common;
using Accounting.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// Phase C-C — ภ.ง.ด.50 v2 service tests on real PG (PDF + preview from the SAME composition).
/// Far-future fiscal years keep the shared teas_test DB collision-free (§8); tests that write
/// year rows/adjustments use their own random far-future year and clean up after.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pnd50FilingServiceTests
{
    private readonly PostgresFixture _fx;
    public Pnd50FilingServiceTests(PostgresFixture fx) => _fx = fx;

    private static readonly Pnd50Attestation Ok = new(FirstFiling: true, AcceptBlankSchedules: true);

    private ServiceProvider Provider()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        s.AddSingleton<IConfiguration>(cfg);
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Pnd50_attested_clean_year_renders_valid_pdf()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        // Far-future year: empty P&L → zero ladder → renderable PayMore=0 case.
        var pdf = await svc.BuildPnd50Async(
            year: 3098, isSme: false, hasRelatedPartyOver200M: false, attest: Ok, ct: default);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [SkippableFact]
    public async Task Pnd50_without_attestation_refuses()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        var act = () => svc.BuildPnd50Async(3098, false, false, attest: null, ct: default);
        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("pnd50.not_attestable");
    }

    [SkippableFact]
    public async Task Pnd50_with_nonzero_adjustments_renders_the_ladder_in_v2()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        // Own far-future year per run — adjustments are unique to (year, row); clean up after.
        var year = 2500 + Random.Shared.Next(400);

        await using var s = sp.CreateAsyncScope();
        var citData = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
        var adj = await citData.CreateAdjustmentAsync(
            year, new UpsertCitAdjustmentRequest("ม.65ตรี(3)", "test add-back", 1_000m), default);
        try
        {
            var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

            // v1 refused this year; v2 renders the p3 ladder instead.
            var pdf = await svc.BuildPnd50Async(year, false, false, attest: Ok, ct: default);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");

            // Preview derives from the SAME composition: the adjustment shows on row 11.
            var preview = await svc.PreviewAsync(year, isSme: false, ct: default);
            preview.Refusals.Should().BeEmpty();
            preview.Ladder.Should().NotBeNull();
            preview.Ladder!.DisallowedExpenses.Should().Be(1_000m);
            preview.Ladder.Total14.Should().Be(
                preview.Ladder.AccountingNetProfit + 1_000m);
            preview.Adjustments.Should().ContainSingle(a => a.Amount == 1_000m);
        }
        finally
        {
            await citData.DeleteAdjustmentAsync(adj.CitAdjustmentId, default);
        }
    }

    [SkippableFact]
    public async Task Pnd50_preview_reports_figures_and_balance_sheet()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        var p = await svc.PreviewAsync(3098, isSme: null, ct: default);

        p.Refusals.Should().BeEmpty();
        p.Ladder.Should().NotBeNull();
        // Ladder foots: 1−2=3 · 3+4=5 · 5−6=7 · 7−8=9 · 9+10+11=12 · 12−13=14 · 14−15=16
        var l = p.Ladder!;
        (l.DirectRevenue - l.CostOfSales).Should().Be(l.GrossProfit);
        (l.Total7 - l.SellingAdminExpenses).Should().Be(l.AccountingNetProfit);
        (l.AccountingNetProfit + l.IncomeAdditions + l.DisallowedExpenses).Should().Be(l.Total12);
        (l.Total12 - l.ExemptDeductions).Should().Be(l.Total14);
        (l.Total14 - l.LossCarryForward).Should().Be(l.Total16);
        l.Total20.Should().Be(l.Total16);
        // Balance sheet foots against itself (mapper asserts vs the report internally).
        var b = p.BalanceSheet;
        (b.CashAndEquivalents + b.TradeReceivables + b.Inventory
         + b.OtherCurrentAssets + b.OtherNonCurrentAssets).Should().Be(b.TotalAssets);
        (b.PaidUpShareCapital + b.OtherEquity + b.RetainedEarnings).Should().Be(b.TotalEquity);
        // p2 figures present and self-consistent.
        p.TotalDue.Should().Be(p.NetPayable + p.Surcharge);
    }

    [SkippableFact]
    public async Task Pnd50_override_mismatch_is_a_refusal_and_blocks_the_pdf()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        var year = 2500 + Random.Shared.Next(400);

        await using var s = sp.CreateAsyncScope();
        var citData = s.ServiceProvider.GetRequiredService<ICitYearDataService>();
        // Override that disagrees with the books (empty far-future P&L ⇒ computed 0).
        await citData.UpsertYearAsync(year, new UpsertCitYearRequest(123_456m, "test override"), default);
        try
        {
            var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

            var preview = await svc.PreviewAsync(year, isSme: false, ct: default);
            preview.Refusals.Should().Contain("pnd50.override_breaks_ladder");

            var act = () => svc.BuildPnd50Async(year, false, false, attest: Ok, ct: default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pnd50.not_renderable");
        }
        finally
        {
            await citData.UpsertYearAsync(year, new UpsertCitYearRequest(null, null), default);
        }
    }
}

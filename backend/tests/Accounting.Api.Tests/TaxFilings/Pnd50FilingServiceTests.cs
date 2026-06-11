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
/// Phase C-C — ภ.ง.ด.50 v1 service smoke tests on real PG. Far-future fiscal years keep the
/// shared teas_test DB collision-free (§8); the refusal test creates + deletes its own adjustment.
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
    public async Task Pnd50_with_nonzero_adjustments_refuses()
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
            var act = () => svc.BuildPnd50Async(year, false, false, attest: Ok, ct: default);
            (await act.Should().ThrowAsync<DomainException>())
                .Which.Code.Should().Be("pnd50.not_attestable");
        }
        finally
        {
            await citData.DeleteAdjustmentAsync(adj.CitAdjustmentId, default);
        }
    }
}

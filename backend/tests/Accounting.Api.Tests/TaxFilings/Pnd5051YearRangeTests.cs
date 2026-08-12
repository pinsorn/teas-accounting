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
/// R2/WP-6 (T26) — pnd50/pnd51 threw an unmapped ArgumentOutOfRangeException (raw 500) for
/// year&lt;=0 or year&gt;=9999. TaxFilingPeriod.EnsureYear (ProportionalInputVatService.cs, next
/// to the sibling MonthRange) now guards Pnd50FilingService.BuildPnd50Async, .PreviewAsync and
/// Pnd51FilingService.BuildPnd51Async as their FIRST statement, so a bad year is a clean 422
/// (tax_filing.bad_year) instead. Bad-year cases pass attest:null / defaults for the other
/// parameters — the guard must fire before any of that is ever consulted, which the assertion
/// (Code == "tax_filing.bad_year", not e.g. pnd50.not_attestable) pins directly.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pnd5051YearRangeTests
{
    private readonly PostgresFixture _fx;
    public Pnd5051YearRangeTests(PostgresFixture fx) => _fx = fx;

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

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(-1)]
    public async Task Pnd50_pdf_bad_year_returns_422_not_500(int year)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        var act = () => svc.BuildPnd50Async(year, isSme: null, hasRelatedPartyOver200M: false,
            attest: null, ct: default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("tax_filing.bad_year");
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(-1)]
    public async Task Pnd50_preview_bad_year_returns_422_not_500(int year)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        var act = () => svc.PreviewAsync(year, isSme: null, ct: default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("tax_filing.bad_year");
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(9999)]
    [InlineData(-1)]
    public async Task Pnd51_pdf_bad_year_returns_422_not_500(int year)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd51FilingService>();

        var act = () => svc.BuildPnd51Async(year, estimatedAnnualProfit: 1_000_000m,
            whtSufferedH1: 0m, isSme: false, fillWorksheet: false, attest: null, ct: default);

        (await act.Should().ThrowAsync<DomainException>())
            .Which.Code.Should().Be("tax_filing.bad_year");
    }

    [SkippableFact]
    public async Task Pnd50_pdf_year_2026_is_not_blocked_by_the_year_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        // year=2026 must clear EnsureYear. Whatever real company-1 2026 data does or does not
        // trigger downstream (a different refusal, or a clean render) is out of scope for this
        // guard — the only thing pinned here is that it is never "tax_filing.bad_year".
        try
        {
            await svc.BuildPnd50Async(2026, isSme: null, hasRelatedPartyOver200M: false,
                attest: Ok, ct: default);
        }
        catch (DomainException ex)
        {
            ex.Code.Should().NotBe("tax_filing.bad_year");
        }
    }

    [SkippableFact]
    public async Task Pnd50_preview_year_2026_is_not_blocked_by_the_year_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd50FilingService>();

        try
        {
            await svc.PreviewAsync(2026, isSme: null, ct: default);
        }
        catch (DomainException ex)
        {
            ex.Code.Should().NotBe("tax_filing.bad_year");
        }
    }

    [SkippableFact]
    public async Task Pnd51_pdf_year_2026_is_not_blocked_by_the_year_guard()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd51FilingService>();

        try
        {
            await svc.BuildPnd51Async(2026, estimatedAnnualProfit: 1_000_000m,
                whtSufferedH1: 0m, isSme: false, fillWorksheet: false, attest: null, ct: default);
        }
        catch (DomainException ex)
        {
            ex.Code.Should().NotBe("tax_filing.bad_year");
        }
    }
}

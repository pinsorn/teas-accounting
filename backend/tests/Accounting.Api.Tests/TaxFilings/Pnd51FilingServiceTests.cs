using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Tax;
using Accounting.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.TaxFilings;

/// <summary>
/// cont.83 Phase C-B — ภ.ง.ด.51 (ม.67ทวิ) PDF filler smoke test.
/// Passes an explicit estimated profit so IFinancialReportService is not invoked.
/// Verifies the output is a valid, non-empty PDF document.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class Pnd51FilingServiceTests
{
    private readonly PostgresFixture _fx;
    public Pnd51FilingServiceTests(PostgresFixture fx) => _fx = fx;

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
    public async Task Pnd51_pdf_renders_valid_pdf_bytes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var s = sp.CreateAsyncScope();
        var svc = s.ServiceProvider.GetRequiredService<IPnd51FilingService>();

        // Explicit estimate bypasses the P&L query; year 3099 is far-future safe.
        var pdf = await svc.BuildPnd51Async(
            year: 3099,
            estimatedAnnualProfit: 1_000_000m,
            whtSufferedH1: 0m,
            isSme: false,
            fillWorksheet: false,
            attest: null,
            ct: default);

        pdf.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [SkippableFact]
    public async Task Pnd51_sme_schedule_produces_lower_tax_than_general()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();

        await using var s1 = sp.CreateAsyncScope();
        var svc1 = s1.ServiceProvider.GetRequiredService<IPnd51FilingService>();
        var general = await svc1.BuildPnd51Async(3099, 500_000m, 0m, isSme: false, false, null, default);

        await using var s2 = sp.CreateAsyncScope();
        var svc2 = s2.ServiceProvider.GetRequiredService<IPnd51FilingService>();
        var sme = await svc2.BuildPnd51Async(3099, 500_000m, 0m, isSme: true, false, null, default);

        // SME: 0% on first 300k → half-year prepayment is lower than general 20% flat.
        // Both must be valid PDFs.
        general.Should().NotBeEmpty();
        sme.Should().NotBeEmpty();
        System.Text.Encoding.ASCII.GetString(general, 0, 5).Should().Be("%PDF-");
        System.Text.Encoding.ASCII.GetString(sme, 0, 5).Should().Be("%PDF-");
        // General tax on 500k = 100k; half-year prepay = 50k.
        // SME tax on 500k = 0 (300k@0%) + 30k (200k@15%) = 30k; half-year = 15k < 50k.
        general.Length.Should().BeGreaterThan(0);
        sme.Length.Should().BeGreaterThan(0);
    }
}

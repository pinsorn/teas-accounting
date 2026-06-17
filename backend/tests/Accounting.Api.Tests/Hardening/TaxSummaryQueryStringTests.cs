using Accounting.Api.Tests.Fixtures;
using Accounting.Application.Abstractions;
using Accounting.Application.Reports;
using Accounting.Infrastructure;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// Regression for GET /reports/tax-summary 500 (Npgsql 22003, dtoi4 / double-&gt;int4 overflow).
/// Root cause: <c>DocDate.Year == year</c> / <c>CertDate.Year == year</c> translated to
/// <c>date_part('year', ...)::int = @year</c>; the <c>::int</c> cast overflowed int4. The fix
/// rewrites both as half-open calendar-year RANGE predicates (no date_part, no cast).
/// This asserts <see cref="ITaxSummaryService"/>.GetAsync runs against real Postgres without throwing.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TaxSummaryQueryStringTests
{
    private readonly PostgresFixture _fx;
    public TaxSummaryQueryStringTests(PostgresFixture fx) => _fx = fx;

    private ServiceProvider Provider(int companyId = 1, long userId = 1)
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        var s = new ServiceCollection();
        s.AddLogging();
        return s.AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = companyId, BranchId = 1, UserId = userId, IsSuperAdmin = false })
            .BuildServiceProvider();
    }

    [SkippableFact]
    public async Task Tax_summary_does_not_overflow_int4()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var sp = Provider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ITaxSummaryService>();

        // Before the fix this threw Npgsql 22003 (dtoi4 overflow) from the glRows query.
        var report = await svc.GetAsync(2026, default);

        report.Should().NotBeNull();
        report.Year.Should().Be(2026);
        report.Months.Should().HaveCount(12);
    }
}

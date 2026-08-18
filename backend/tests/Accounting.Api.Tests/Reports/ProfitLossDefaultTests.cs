using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Sales;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// F12 (PLAN-fix-findings-2026-08-16.md Unit E) — GET /reports/profit-loss without the
/// includeUnspecified query param must include untagged (no-business-unit) revenue/expense,
/// matching its own shipped callers (frontend reports/profit-loss/page.tsx defaults the toggle
/// to true; the MCP get_profit_loss tool documents/defaults to true). Before the fix the endpoint
/// defaulted the query-string binding to false, so a bare GET silently zeroed out a company that
/// never business-unit-tags its documents. HTTP-level (RbacApiFactory), mirroring
/// GeneralLedgerEndpointTests — shared company 1, so assertions are RELATIVE (presence/absence of
/// the untagged group), never absolute totals.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ProfitLossDefaultTests
{
    private readonly PostgresFixture _fx;
    public ProfitLossDefaultTests(PostgresFixture fx) => _fx = fx;

    private static string Token() => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "pl-default-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: true, Roles: ["test"], Permissions: [])).Token;

    private static HttpRequestMessage Get(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    /// <summary>Seeds one posted, untagged (BusinessUnitId=null) tax invoice for company 1 —
    /// same Provider/PostTi shape as Sprint9FinancialReportTests. TI dates are server-pinned to
    /// today's Bangkok date regardless of the DateOnly passed in, so the report window below
    /// queries the current Bangkok month.</summary>
    private async Task SeedUntaggedRevenue()
    {
        var cfg = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build();
        await using var sp = new ServiceCollection().AddLogging()
            .AddInfrastructure(cfg)
            .AddSingleton<ITenantContext>(new StubTenant
            { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();

        await using var s = sp.CreateAsyncScope();
        var db = s.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var custId = await db.Customers.Where(c => c.CustomerCode == "C-DEMO-001")
            .Select(c => c.CustomerId).FirstAsync();

        var svc = s.ServiceProvider.GetRequiredService<ITaxInvoiceService>();
        var id = await svc.CreateDraftAsync(new CreateTaxInvoiceRequest(
            new DateOnly(2026, 5, 16), custId, false, "THB", 1m, null, null, null,
            [new TaxInvoiceLineInput(null, null, "pl-default-tests svc", 1m, 1, "ชิ้น", 4321m, 0m, 1, "VAT7", 0.07m)],
            null), default);
        await svc.PostAsync(id, default);
    }

    private static (string from, string to) CurrentBangkokMonth()
    {
        var today = new SystemClock().TodayInBangkok();
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        return (from.ToString("yyyy-MM-dd"), to.ToString("yyyy-MM-dd"));
    }

    private static bool HasUnspecifiedGroup(JsonElement root) =>
        root.GetProperty("groups").EnumerateArray()
            .Any(g => g.GetProperty("businessUnitId").ValueKind == JsonValueKind.Null);

    [SkippableFact]
    public async Task Bare_get_includes_untagged_revenue_by_default()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await SeedUntaggedRevenue();
        var (from, to) = CurrentBangkokMonth();

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token();

        using var resp = await client.SendAsync(
            Get($"/reports/profit-loss?from={from}&to={to}", token));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        HasUnspecifiedGroup(doc.RootElement).Should().BeTrue(
            "a bare GET (no includeUnspecified param) must default to true and include the " +
            "untagged (ไม่ระบุ BU) group — matching the FE page and the MCP tool's own default");
    }

    [SkippableFact]
    public async Task Explicit_false_still_excludes_untagged_revenue()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await SeedUntaggedRevenue();
        var (from, to) = CurrentBangkokMonth();

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token();

        using var resp = await client.SendAsync(
            Get($"/reports/profit-loss?from={from}&to={to}&includeUnspecified=false", token));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        HasUnspecifiedGroup(doc.RootElement).Should().BeFalse(
            "explicitly passing includeUnspecified=false must still exclude the untagged group");
    }
}

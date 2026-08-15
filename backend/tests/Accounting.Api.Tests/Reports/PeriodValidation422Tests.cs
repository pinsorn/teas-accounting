using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// WP-2 (specs/fix-local-hard-test-findings.md) — out-of-range year/month used to construct a
/// DateOnly straight from request input and leak a raw 500 (ArgumentOutOfRangeException) instead
/// of a typed 422. Covers the two affected route families: VAT reports (VatReportService.MonthRange)
/// and CIT year endpoints (CitYearDataService via TaxFilingPeriod.EnsureYear) — plus the trap-1
/// regression guard that year=3000 still succeeds (the accepted window must stay loose).
/// HTTP-level (real pipeline, RbacApiFactory), mirroring GeneralLedgerEndpointTests.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class PeriodValidation422Tests
{
    private readonly PostgresFixture _fx;
    public PeriodValidation422Tests(PostgresFixture fx) => _fx = fx;

    private static string SuperAdminToken() => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "period-validation-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: true, Roles: ["test"], Permissions: [])).Token;

    private static HttpRequestMessage Get(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static HttpRequestMessage Post(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    /// <summary>Root/BFF surface uses RFC-7807; DomainExceptionMiddleware sets title = ex.Code.</summary>
    private static async Task<string> ErrorCodeOf(HttpResponseMessage resp)
    {
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("title").GetString()!;
    }

    [SkippableFact]
    public async Task Vat_report_family_bad_month_returns_422_tax_filing_bad_period()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = SuperAdminToken();

        // pnd30 — the exact repro from the spec (month=13).
        using var pnd30 = await client.SendAsync(Get("/reports/pnd30?year=2026&month=13", token));
        pnd30.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(pnd30)).Should().Be("tax_filing.bad_period");

        // month=0 — the other repro from the spec, same family/service.
        using var pnd30Zero = await client.SendAsync(Get("/reports/pnd30?year=2026&month=0", token));
        pnd30Zero.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(pnd30Zero)).Should().Be("tax_filing.bad_period");

        // vat-register — same MonthRange helper, different endpoint on the same service.
        using var vatReg = await client.SendAsync(Get("/reports/vat-register?year=2026&month=13", token));
        vatReg.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(vatReg)).Should().Be("tax_filing.bad_period");
    }

    [SkippableFact]
    public async Task Vat_report_out_of_band_month_does_not_alias_to_a_different_valid_period()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = SuperAdminToken();

        // month=-88 would decode as (year*100-88) = 202512, i.e. December 2025 — a DIFFERENT,
        // in-range period. Round-trip check must catch this instead of silently returning it.
        using var resp = await client.SendAsync(Get("/reports/pnd30?year=2026&month=-88", token));
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(resp)).Should().Be("tax_filing.bad_period");
    }

    [SkippableFact]
    public async Task Cit_year_family_bad_year_returns_422_tax_filing_bad_year_on_both_entry_points()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = SuperAdminToken();

        // GET /tax-filings/cit/profile?year= — query-parameter entry point.
        using var profile9999 = await client.SendAsync(Get("/tax-filings/cit/profile?year=9999", token));
        profile9999.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(profile9999)).Should().Be("tax_filing.bad_year");

        using var profileZero = await client.SendAsync(Get("/tax-filings/cit/profile?year=0", token));
        profileZero.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(profileZero)).Should().Be("tax_filing.bad_year");

        // POST /tax-filings/cit/years/{year}/compute — route-value entry point.
        using var compute = await client.SendAsync(Post("/tax-filings/cit/years/99999/compute", token));
        compute.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await ErrorCodeOf(compute)).Should().Be("tax_filing.bad_year");
    }

    [SkippableFact]
    public async Task Cit_year_3000_still_returns_200_the_window_stays_loose() // trap 1 regression guard
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = SuperAdminToken();

        using var resp = await client.SendAsync(Get("/tax-filings/cit/profile?year=3000", token));
        resp.StatusCode.Should().Be(HttpStatusCode.OK,
            "EnsureYear's ceiling is deliberately loose — sentinel far-future fiscal years must keep working");
    }
}

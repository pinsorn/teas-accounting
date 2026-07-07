using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.Reports;

/// <summary>
/// HTTP-level (real pipeline, RbacApiFactory — mirrors PaperEndpointTests) coverage for the
/// General Ledger report + export endpoints and the JE detail endpoint: RBAC gating, the
/// fromDate/toDate 400, 404 mapping, and the export file shapes (CSV BOM, PDF magic). Business
/// balance-math correctness lives in GeneralLedgerReportTests (TestCompanyFactory-isolated);
/// these tests only assert structural/wire behaviour, so running against the shared company 1
/// (whatever data other tests have accumulated) is safe — row counts are checked RELATIVE to
/// the JSON report, never as an absolute figure.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class GeneralLedgerEndpointTests
{
    private readonly PostgresFixture _fx;
    public GeneralLedgerEndpointTests(PostgresFixture fx) => _fx = fx;

    private static string Token(bool isSuperAdmin) => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "gl-endpoint-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: isSuperAdmin, Roles: ["test"], Permissions: [])).Token;

    private static HttpRequestMessage Get(string path, string token)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    private static async Task<long> FirstAccountId(HttpClient client, string token)
    {
        using var resp = await client.SendAsync(Get("/reports/general-ledger/accounts", token));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement[0].GetProperty("accountId").GetInt64();
    }

    [SkippableFact]
    public async Task No_perm_user_gets_403_on_both_endpoints()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: false);

        using var glResp = await client.SendAsync(Get(
            "/reports/general-ledger?accountId=1&fromDate=2026-01-01&toDate=2026-01-31", token));
        glResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no report.general_ledger.read claim");

        using var jeResp = await client.SendAsync(Get("/journals/1", token));
        jeResp.StatusCode.Should().Be(HttpStatusCode.Forbidden, "no gl.journal.read claim");
    }

    [SkippableFact]
    public async Task From_date_after_to_date_returns_400()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: true);

        using var resp = await client.SendAsync(Get(
            "/reports/general-ledger?accountId=1&fromDate=2026-02-01&toDate=2026-01-01", token));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SkippableFact]
    public async Task Unknown_account_id_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: true);

        using var resp = await client.SendAsync(Get(
            "/reports/general-ledger?accountId=999999999&fromDate=2026-01-01&toDate=2026-01-31", token));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Unknown_journal_id_returns_404()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: true);

        using var resp = await client.SendAsync(Get("/journals/999999999", token));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [SkippableFact]
    public async Task Csv_export_returns_200_with_bom_and_row_count_matches_report()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: true);

        var accountId = await FirstAccountId(client, token);
        const string from = "2000-01-01"; const string to = "2100-12-31";

        using var reportResp = await client.SendAsync(Get(
            $"/reports/general-ledger?accountId={accountId}&fromDate={from}&toDate={to}", token));
        reportResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var reportJson = JsonDocument.Parse(await reportResp.Content.ReadAsStringAsync());
        var expectedRows = reportJson.RootElement.GetProperty("rows").GetArrayLength();

        using var csvResp = await client.SendAsync(Get(
            $"/reports/general-ledger/export?accountId={accountId}&fromDate={from}&toDate={to}&format=csv", token));
        csvResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await csvResp.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal([0xEF, 0xBB, 0xBF], "UTF-8 BOM so Thai text opens correctly in Excel");

        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // header + opening row + N movement rows + closing row.
        lines.Should().HaveCount(expectedRows + 3);
    }

    [SkippableFact]
    public async Task Pdf_export_returns_200_non_empty_with_pdf_magic()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();
        var token = Token(isSuperAdmin: true);

        var accountId = await FirstAccountId(client, token);

        using var pdfResp = await client.SendAsync(Get(
            $"/reports/general-ledger/export?accountId={accountId}&fromDate=2000-01-01&toDate=2100-12-31&format=pdf", token));
        pdfResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var bytes = await pdfResp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(500);
        Encoding.ASCII.GetString(bytes, 0, 5).Should().Be("%PDF-");
    }
}

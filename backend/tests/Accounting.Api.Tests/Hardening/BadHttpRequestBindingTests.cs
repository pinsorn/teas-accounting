using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Accounting.Api.Tests.Hardening;

/// <summary>
/// F4 (PLAN-fix-findings-2026-08-16.md Unit F) — minimal-API model binding failures (a missing
/// required query parameter, or one present but unparsable) throw BadHttpRequestException BEFORE
/// any handler runs. DomainExceptionMiddleware had no catch for it, so every such request fell
/// through to the generic 500 branch — a client mistake reported as a server failure. HTTP-level
/// (real pipeline via RbacApiFactory), one BFF endpoint (RFC-7807) + one /api/v1 endpoint
/// (ErrorEnvelope), mirroring PeriodValidation422Tests / RbacCartesianTests' API-key pattern.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BadHttpRequestBindingTests
{
    private readonly PostgresFixture _fx;
    public BadHttpRequestBindingTests(PostgresFixture fx) => _fx = fx;

    private static string SuperAdminToken() => new JwtTokenIssuer(
        new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        }))
        .Issue(new TokenClaims(
            UserId: 1, Username: "bad-http-binding-tests", CompanyId: 1, BranchId: 1,
            IsSuperAdmin: true, Roles: ["test"], Permissions: [])).Token;

    private static HttpRequestMessage Get(string path, string? bearerToken = null, string? apiKey = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (bearerToken is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        if (apiKey is not null) req.Headers.Add("X-Api-Key", apiKey);
        return req;
    }

    private async Task<string> MintApiKeyAsync(int companyId, int branchId, IReadOnlyList<string> scopes)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId, branchId);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var created = await svc.CreateAsync(new CreateApiKeyRequest(
            Accounting.TestKit.TestIds.Name("v1-binding"), scopes), default);
        return created.Plaintext;
    }

    private static void BodyHasNoDotNetInternals(string body) =>
        body.Should().NotContain("Int32").And.NotContain("Int64").And.NotContain("BadHttpRequestException")
            .And.NotContain("Microsoft.AspNetCore").And.NotContain("System.");

    // ── BFF (root) — /reports/vat-register: DomainExceptionMiddleware's non-v1 (RFC-7807) branch ──

    [SkippableFact]
    public async Task Bff_missing_required_query_param_returns_400_not_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // month omitted — a required (non-nullable) `int month` [FromQuery] parameter.
        using var resp = await client.SendAsync(Get("/reports/vat-register?year=2026", SuperAdminToken()));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a missing required query parameter is a client error (400), never a server error (500)");

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("validation_error");
        doc.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        BodyHasNoDotNetInternals(body);
    }

    [SkippableFact]
    public async Task Bff_malformed_query_param_returns_400_not_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // month=abc fails int binding — distinct from month=13, which BINDS fine and only then
        // fails domain validation with tax_filing.bad_period (PeriodValidation422Tests).
        using var resp = await client.SendAsync(Get("/reports/vat-register?year=2026&month=abc", SuperAdminToken()));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("title").GetString().Should().Be("validation_error");
        BodyHasNoDotNetInternals(body);
    }

    // ── /api/v1 — GET /api/v1/customers: DomainExceptionMiddleware's v1 (ErrorEnvelope) branch ──

    [SkippableFact]
    public async Task V1_malformed_query_param_returns_400_error_envelope_not_500()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintApiKeyAsync(1, 1, ["master.customer.read"]);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // `page` is `int?` — nullable, so MISSING binds fine (null), but a present-and-unparsable
        // value still fails binding regardless of nullability.
        using var resp = await client.SendAsync(Get("/api/v1/customers?page=abc", apiKey: key));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var error = doc.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("validation_error");
        BodyHasNoDotNetInternals(body);
    }
}

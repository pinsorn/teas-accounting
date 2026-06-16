using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// Onboarding-switcher spec (2026-06-16) — end-to-end HTTP tests for <c>GET /me</c> and
/// <c>POST /auth/switch-company/{id}</c> over the full pipeline (RbacApiFactory + teas_test).
/// Tokens are minted with the SAME Jwt config the factory validates, so an authenticated-but-
/// forbidden request returns 403, never 401.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompanySwitchTests
{
    private readonly PostgresFixture _fx;
    public CompanySwitchTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer() => new(Options.Create(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static string Token(long userId, string username, int companyId, bool isSuper) =>
        Issuer().Issue(new TokenClaims(
            UserId: userId, Username: username, CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: isSuper, Roles: [], Permissions: [])).Token;

    private static HttpRequestMessage Authed(HttpMethod method, string path, string token)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    [SkippableFact]
    public async Task Get_me_as_super_admin_lists_more_than_one_active_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        // Guarantee ≥2 active companies (company 1 seeded + one fresh).
        await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Get, "/me", Token(1, "admin", companyId: 1, isSuper: true));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("isSuperAdmin").GetBoolean().Should().BeTrue();
        root.GetProperty("companyId").GetInt32().Should().Be(1);
        var allowed = root.GetProperty("allowedCompanies");
        allowed.GetArrayLength().Should().BeGreaterThan(1,
            "a super-admin sees every active company");
        // §10 — the allowed-company projection must NOT leak tax fields.
        var first = allowed[0];
        first.TryGetProperty("id", out _).Should().BeTrue();
        first.TryGetProperty("vatRate", out _).Should().BeFalse();
        first.TryGetProperty("pnd30SubmissionMode", out _).Should().BeFalse();
    }

    [SkippableFact]
    public async Task Get_me_as_normal_user_lists_only_own_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Get, "/me", Token(42, "user42", companyId: 1, isSuper: false));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("isSuperAdmin").GetBoolean().Should().BeFalse();
        root.GetProperty("allowedCompanies").GetArrayLength().Should().Be(1,
            "a normal user is scoped to exactly their own company — never a cross-tenant list");
        root.GetProperty("allowedCompanies")[0].GetProperty("id").GetInt32().Should().Be(1);
    }

    [SkippableFact]
    public async Task Switch_company_as_super_admin_reissues_token_scoped_to_target()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var target = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Post, $"/auth/switch-company/{target.CompanyId}",
            Token(1, "admin", companyId: 1, isSuper: true));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Claims.First(c => c.Type == "company_id").Value
            .Should().Be(target.CompanyId.ToString(), "the re-issued token must scope to the target");
        jwt.Claims.First(c => c.Type == "is_super_admin").Value
            .Should().Be("true", "switching never drops super-admin");

        // §4.8 — the privileged switch MUST leave an audit trail (action company_switch,
        // scoped to the target company, actor = the super-admin user).
        await using var sp = _fx.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var auditRows = (await db.Database.SqlQueryRaw<int>(
                "SELECT COUNT(*)::int AS \"Value\" FROM audit.activity_log " +
                "WHERE activity_type = 'company_switch' AND company_id = {0} AND user_id = 1",
                target.CompanyId)
            .ToListAsync()).Single();
        auditRows.Should().BeGreaterThan(0,
            "the company switch must be recorded in audit.activity_log");
    }

    [SkippableFact]
    public async Task Switch_company_as_non_super_admin_is_forbidden()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Post, "/auth/switch-company/1",
            Token(42, "user42", companyId: 1, isSuper: false));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "only a super-admin may switch company (authenticated-but-forbidden → 403)");
    }

    [SkippableFact]
    public async Task Switch_company_to_nonexistent_company_is_not_found()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Post, "/auth/switch-company/1900000000",
            Token(1, "admin", companyId: 1, isSuper: true));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a missing/inactive target company → 404");
    }
}

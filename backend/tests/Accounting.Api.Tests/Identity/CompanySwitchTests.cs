using System.IdentityModel.Tokens.Jwt;
using System.Linq;
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
using Npgsql;
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

    private static JwtTokenIssuer Issuer() => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = 60,
    }));

    private static string Token(
        long userId, string username, int companyId, bool isSuper, DateTimeOffset? authTime = null) =>
        Issuer().Issue(new TokenClaims(
            UserId: userId, Username: username, CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: isSuper, Roles: [], Permissions: [], AuthTime: authTime)).Token;

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

    /// <summary>F-A (2026-07 review fix) — a switch attempted past the absolute session cap must
    /// be rejected, exactly like /auth/refresh (AuthRefreshTests.Refresh_PastAbsoluteCap_403),
    /// not silently reset the cap clock to "now" by minting a fresh token.</summary>
    [SkippableFact]
    public async Task Switch_company_past_absolute_cap_is_forbidden()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var target = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // auth_time 13h old — past the 10h default AbsoluteSessionCapHours (RbacApiFactory does
        // not override it, so the appsettings.Development.json default applies — same setup
        // AuthRefreshTests.Refresh_PastAbsoluteCap_403 relies on).
        using var req = Authed(HttpMethod.Post, $"/auth/switch-company/{target.CompanyId}",
            Token(1, "admin", companyId: 1, isSuper: true, authTime: DateTimeOffset.UtcNow.AddHours(-13)));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a session past its absolute lifetime must not be extendable by switching company");

        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("auth.session_absolute_cap_exceeded");
    }

    /// <summary>F-A — the core regression this fix closes: before it, CompanySwitchService never
    /// passed AuthTime into TokenClaims, so JwtTokenIssuer stamped a fresh "now" on every switch,
    /// letting repeated switching slide a session past the cap forever. The re-issued token's
    /// auth_time claim must equal the ORIGINAL login time, not "now".</summary>
    [SkippableFact]
    public async Task Switch_company_within_cap_carries_original_auth_time_forward()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var target = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // 3h into the 10h cap — comfortably within, but far enough from "now" that a reset-to-now
        // regression is unambiguously detectable from the returned token's claim.
        var original = DateTimeOffset.UtcNow.AddHours(-3);
        using var req = Authed(HttpMethod.Post, $"/auth/switch-company/{target.CompanyId}",
            Token(1, "admin", companyId: 1, isSuper: true, authTime: original));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK, "a within-cap switch must still succeed");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var authTimeClaim = long.Parse(jwt.Claims.First(c => c.Type == "auth_time").Value);
        authTimeClaim.Should().Be(original.ToUnixTimeSeconds(),
            "the switched token must carry the ORIGINAL auth_time forward, never reset it to now");
    }

    /// <summary>F-A happy path — a within-cap switch is otherwise unaffected: still re-issues a
    /// token correctly scoped to the target company (mirrors
    /// Switch_company_as_super_admin_reissues_token_scoped_to_target, with an explicit non-null
    /// auth_time to prove the new cap check doesn't interfere with the ordinary case).</summary>
    [SkippableFact]
    public async Task Switch_company_within_cap_still_reissues_token_for_target_company()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var target = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var req = Authed(HttpMethod.Post, $"/auth/switch-company/{target.CompanyId}",
            Token(1, "admin", companyId: 1, isSuper: true, authTime: DateTimeOffset.UtcNow.AddHours(-1)));
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var accessToken = doc.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        jwt.Claims.First(c => c.Type == "company_id").Value
            .Should().Be(target.CompanyId.ToString(), "the re-issued token must scope to the target");
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

    /// <summary>
    /// D6 integration gate (specs/superadmin-tenant-scope.md) — end-to-end proof that a super
    /// admin's data is ALWAYS scoped to the currently switched-into company, over the real HTTP
    /// pipeline (TenantMiddleware + the EF global query filter), not just at the RLS layer.
    /// Reproduces the exact prod symptom (Repttown vs พงศ์สันต์ quotations identical regardless
    /// of the switcher) and its "letterhead" corollary (cross-company detail fetch → 404, never
    /// renders under the wrong company).
    /// </summary>
    [SkippableFact]
    public async Task Quotations_are_scoped_to_the_switched_into_company_over_http()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var a = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var b = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);

        long aQuotationId, bQuotationId;
        await using (var seed = new NpgsqlConnection(_fx.ConnectionString))
        {
            await seed.OpenAsync();
            aQuotationId = await InsertQuotationAsync(seed, a.CompanyId, a.BranchId, a.CustomerId);
            bQuotationId = await InsertQuotationAsync(seed, b.CompanyId, b.BranchId, b.CustomerId);
        }

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Switch a super admin (starting at A) into B — mirrors Switch_company_as_super_admin_
        // reissues_token_scoped_to_target above.
        using var switchReq = Authed(HttpMethod.Post, $"/auth/switch-company/{b.CompanyId}",
            Token(1, "admin", companyId: a.CompanyId, isSuper: true));
        using var switchResp = await client.SendAsync(switchReq);
        switchResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var switchDoc = JsonDocument.Parse(await switchResp.Content.ReadAsStringAsync());
        var bScopedToken = switchDoc.RootElement.GetProperty("access_token").GetString();
        bScopedToken.Should().NotBeNullOrEmpty();

        // GET /quotations while switched to B must return ONLY B's quotation — the exact prod bug
        // was seeing the UNION of every company regardless of the switcher.
        using var listReq = Authed(HttpMethod.Get, "/quotations", bScopedToken!);
        using var listResp = await client.SendAsync(listReq);
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var listDoc = JsonDocument.Parse(await listResp.Content.ReadAsStringAsync());
        var ids = listDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("quotationId").GetInt64()).ToArray();
        ids.Should().Contain(bQuotationId, "company B's own quotation must be visible while switched to B");
        ids.Should().NotContain(aQuotationId,
            "a super admin switched to B must NOT see company A's quotation (the reported leak)");

        // Letterhead symptom (D5): fetching A's quotation by id while switched to B → 404, never
        // rendered under B's letterhead.
        using var detailReq = Authed(HttpMethod.Get, $"/quotations/{aQuotationId}", bScopedToken!);
        using var detailResp = await client.SendAsync(detailReq);
        detailResp.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "company A's quotation must 404 while switched to B, never render under B's letterhead");

        // Unswitched super admin (companyId=0, per LoginService's no-assignment default) sees ZERO
        // quotations — matches the FE onboarding auto-switch precondition documented in the spec.
        using var unswitchedReq = Authed(HttpMethod.Get, "/quotations",
            Token(1, "admin", companyId: 0, isSuper: true));
        using var unswitchedResp = await client.SendAsync(unswitchedReq);
        unswitchedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var unswitchedDoc = JsonDocument.Parse(await unswitchedResp.Content.ReadAsStringAsync());
        unswitchedDoc.RootElement.GetArrayLength().Should().Be(0,
            "an unswitched super admin (companyId=0) must see no quotations from any real company");
    }

    /// <summary>Minimal DRAFT quotation insert on a bypass connection — same shape as
    /// SuperAdminTenantScopeRlsTests' helper (private per-class, not shared across test classes).</summary>
    private static async Task<long> InsertQuotationAsync(
        NpgsqlConnection c, int companyId, int branchId, long customerId)
    {
        var docNo = "CST-" + Guid.NewGuid().ToString("N")[..10];
        var today = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        await using var cmd = new NpgsqlCommand($@"
            INSERT INTO sales.quotations
                (company_id, branch_id, customer_id, doc_no, doc_date, valid_until_date, status,
                 customer_name, customer_type, currency_code, exchange_rate,
                 subtotal_amount, vat_amount, total_amount, show_wht_note,
                 created_at, updated_at, version, print_count)
            VALUES ({companyId},{branchId},{customerId},'{docNo}','{today}','{today}','DRAFT',
                 'ลูกค้า','CORPORATE','THB',1, 100,7,107, false, now(), now(), 0, 0)
            RETURNING quotation_id", c);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }
}

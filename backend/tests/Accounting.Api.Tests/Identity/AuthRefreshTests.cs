using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Infrastructure.Identity;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;
using Xunit;

namespace Accounting.Api.Tests.Identity;

/// <summary>
/// WP2.1 (F16, D6 sliding re-issue) — end-to-end HTTP tests for <c>POST /auth/refresh</c>
/// over the full pipeline (RbacApiFactory + teas_test), mirroring CompanySwitchTests'
/// token-minting pattern.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AuthRefreshTests
{
    private readonly PostgresFixture _fx;
    public AuthRefreshTests(PostgresFixture fx) => _fx = fx;

    private static JwtTokenIssuer Issuer(int accessTokenMinutes = 60) => new(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
    {
        Issuer = RbacApiFactory.JwtIssuer,
        Audience = RbacApiFactory.JwtAudience,
        SigningKey = RbacApiFactory.JwtSigningKey,
        AccessTokenMinutes = accessTokenMinutes,
    }));

    private static string Token(
        long userId, string username, int companyId, bool isSuper,
        DateTimeOffset? authTime = null, int accessTokenMinutes = 60) =>
        Issuer(accessTokenMinutes).Issue(new TokenClaims(
            UserId: userId, Username: username, CompanyId: companyId, BranchId: 1,
            IsSuperAdmin: isSuper, Roles: [], Permissions: [], AuthTime: authTime)).Token;

    /// <summary>Hand-builds an ALREADY-EXPIRED token (both notBefore and expires in the past —
    /// JwtSecurityToken's ctor requires expires > notBefore, so JwtTokenIssuer.Issue's normal
    /// "now + AccessTokenMinutes" shape can't express a negative lifetime). Mirrors
    /// JwtTokenIssuer.Issue's claim set exactly.</summary>
    private static string ExpiredToken(long userId, string username, int companyId, bool isSuper)
    {
        var keyBytes = Encoding.UTF8.GetBytes(RbacApiFactory.JwtSigningKey);
        var creds = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var notBefore = DateTime.UtcNow.AddMinutes(-10);
        var expires = DateTime.UtcNow.AddMinutes(-5);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Name, username),
            new(TenantClaims.CompanyId, companyId.ToString()),
            new(TenantClaims.BranchId, "1"),
            new(TenantClaims.IsSuperAdmin, isSuper ? "true" : "false"),
            new("auth_time", new DateTimeOffset(notBefore, TimeSpan.Zero).ToUnixTimeSeconds().ToString()),
        };
        var jwt = new JwtSecurityToken(
            issuer: RbacApiFactory.JwtIssuer, audience: RbacApiFactory.JwtAudience,
            claims: claims, notBefore: notBefore, expires: expires, signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static HttpRequestMessage Authed(HttpMethod method, string path, string token)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return req;
    }

    /// <summary>Inserts a standalone test user (sys.users carries no RLS — cross-tenant by
    /// design, see User.cs doc comment — so a plain insert on the superuser test connection
    /// is representative of prod behaviour too, no GUC dance needed).</summary>
    private static async Task<long> InsertUserAsync(
        string connectionString, bool isActive, DateTimeOffset? lockedUntil)
    {
        await using var c = new NpgsqlConnection(connectionString);
        await c.OpenAsync();
        await using var cmd = new NpgsqlCommand(@"
            INSERT INTO sys.users
                (username, email, password_hash, full_name, is_active, is_super_admin,
                 failed_login_count, must_change_password, locked_until, created_at, updated_at, version)
            VALUES
                (@username, @email, 'x', 'WP2.1 Test User', @is_active, false,
                 0, false, @locked_until, now(), now(), 0)
            RETURNING user_id", c);
        cmd.Parameters.AddWithValue("username", $"wp21-{TestIds.Suffix()}");
        cmd.Parameters.AddWithValue("email", TestIds.Email("wp21"));
        cmd.Parameters.AddWithValue("is_active", isActive);
        cmd.Parameters.AddWithValue("locked_until", (object?)lockedUntil ?? DBNull.Value);
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    [SkippableFact]
    public async Task Refresh_WithValidToken_IssuesNewExpiry()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Seeded admin (user 1, company 1) — same fixture user CompanySwitchTests relies on.
        var original = Token(1, "admin", companyId: 1, isSuper: true, authTime: DateTimeOffset.UtcNow);
        using var req = Authed(HttpMethod.Post, "/auth/refresh", original);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
        var expiresAt = root.GetProperty("expires_at").GetDateTimeOffset();
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow, "the re-issued token must carry a fresh future expiry");
    }

    [SkippableFact]
    public async Task Refresh_WithExpiredToken_401()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Both notBefore and expires are already in the past; the JWT bearer handler's
        // ValidateLifetime rejects it before the /auth/refresh handler ever runs — an expired
        // session can never resurrect itself via sliding, only a fresh /auth/login can.
        var expired = ExpiredToken(1, "admin", companyId: 1, isSuper: true);
        using var req = Authed(HttpMethod.Post, "/auth/refresh", expired);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "an expired token must not be refreshable — sliding only extends an ACTIVE session");
    }

    [SkippableFact]
    public async Task Refresh_PastAbsoluteCap_403()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        // Still cryptographically valid (60-min token, just minted) but auth_time is 13h old —
        // past the 10h default AbsoluteSessionCapHours (RbacApiFactory does not override it, so
        // the appsettings.Development.json default applies). Must force full re-login.
        var pastCap = Token(1, "admin", companyId: 1, isSuper: true,
            authTime: DateTimeOffset.UtcNow.AddHours(-13));
        using var req = Authed(HttpMethod.Post, "/auth/refresh", pastCap);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a session past its absolute lifetime must be rejected regardless of token validity");
    }

    [SkippableFact]
    public async Task Refresh_LockedUser_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var lockedUserId = await InsertUserAsync(
            _fx.ConnectionString, isActive: true, lockedUntil: DateTimeOffset.UtcNow.AddMinutes(15));

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Token(lockedUserId, "wp21-locked", companyId: 1, isSuper: false,
            authTime: DateTimeOffset.UtcNow);
        using var req = Authed(HttpMethod.Post, "/auth/refresh", token);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "refresh must re-validate the user server-side, never blindly re-sign a locked account");
    }

    [SkippableFact]
    public async Task Refresh_InactiveUser_rejected()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        var inactiveUserId = await InsertUserAsync(
            _fx.ConnectionString, isActive: false, lockedUntil: null);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        var token = Token(inactiveUserId, "wp21-inactive", companyId: 1, isSuper: false,
            authTime: DateTimeOffset.UtcNow);
        using var req = Authed(HttpMethod.Post, "/auth/refresh", token);
        using var resp = await client.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a since-disabled account must not be re-signed just because its old token still parses");
    }

    [SkippableFact]
    public async Task Refresh_WithoutToken_401()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient();

        using var resp = await client.PostAsync("/auth/refresh", null);
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Identity;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using Accounting.Infrastructure.Persistence;
using Accounting.TestKit;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Xunit;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// H4 — the OAuth consent grant must never exceed the consenting user's effective RBAC for the
/// consented company (a super-admin is the sanctioned exception — they hold no explicit permission
/// rows anywhere, so they get the full valid set unfiltered). M11 — a refresh-token grant must
/// re-validate the subject user (active + still a member) and re-derive scopes against CURRENT RBAC,
/// so a disabled/off-boarded/de-permissioned user cannot keep MCP access for the refresh token's
/// 8h lifetime. Both share `McpConsentScopes.FilterToRbac` (unit-proved in McpConsentScopesTests).
///
/// Access tokens are OpenIddict-encrypted JWEs (ephemeral encryption key per test host) — they
/// cannot be decoded client-side, so scope reach is proved BEHAVIORALLY via real <c>/mcp</c> tool
/// calls, mirroring the established pattern in McpServerSmokeTests
/// (Mcp_key_with_read_only_scopes_hides_create_tools): a missing scope hides the tool from
/// ListToolsAsync AND makes an explicit call throw — PermissionHandler gates OAuth Bearer the same
/// way it gates X-Api-Key (both ride the is_api_key=true + CSV `scopes` claim).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class OAuthScopeRevalidationTests
{
    private const string ClientId = "teas-mcp";
    private const string RedirectUri = "http://localhost:8765/callback";
    private readonly PostgresFixture _fx;
    public OAuthScopeRevalidationTests(PostgresFixture fx) => _fx = fx;

    private static string SessionJwt(long userId, string username, int companyId, bool isSuper) =>
        new JwtTokenIssuer(new StaticOptionsMonitor<JwtOptions>(new JwtOptions
        {
            Issuer = RbacApiFactory.JwtIssuer,
            Audience = RbacApiFactory.JwtAudience,
            SigningKey = RbacApiFactory.JwtSigningKey,
            AccessTokenMinutes = 60,
        })).Issue(new TokenClaims(userId, username, companyId, 1, isSuper, [], [])).Token;

    private static string B64Url(byte[] b) =>
        Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static async Task<McpClient> ConnectAsync(HttpClient http) =>
        await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(http.BaseAddress!, "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
            }, http, loggerFactory: null, ownsHttpClient: false));

    // Drives authorize(accept) → code → token for the given principal + scope string.
    private static async Task<(string access, string? refresh)> AuthorizeAsync(
        HttpClient client, long userId, string username, int companyId, bool isSuper,
        string scope, bool offlineAccess)
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var fullScope = offlineAccess ? $"{scope} offline_access" : scope;

        using var acceptReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/authorize")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["scope"] = fullScope,
                ["state"] = "xyz",
                ["resource"] = "http://localhost:3000/mcp",
                ["company_id"] = companyId.ToString(),
                ["approve"] = "true",
            }),
        };
        acceptReq.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", SessionJwt(userId, username, companyId, isSuper));
        using var acceptResp = await client.SendAsync(acceptReq);
        var acceptBody = await acceptResp.Content.ReadAsStringAsync();
        acceptResp.StatusCode.Should().Be(HttpStatusCode.Found,
            $"consent accept → 302 to redirect_uri?code; got {(int)acceptResp.StatusCode}: {acceptBody}");
        var code = QueryHelpers.ParseQuery(new Uri(acceptResp.Headers.Location!.ToString()).Query)["code"].ToString();
        code.Should().NotBeNullOrEmpty();

        using var tokenResp = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier,
            }));
        tokenResp.StatusCode.Should().Be(HttpStatusCode.OK, await tokenResp.Content.ReadAsStringAsync());
        using var doc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var access = doc.RootElement.GetProperty("access_token").GetString()!;
        var refresh = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
        return (access, refresh);
    }

    // Seeds a fresh company + a regular (non-super) user whose ONE role grants exactly the given
    // permission codes. Returns the user + role ids so a test can later flip IsActive / revoke perms.
    private async Task<(int CompanyId, long UserId, int RoleId, string Username)> SeedRestrictedUserAsync(
        params string[] permissionCodes)
    {
        var co = await TestCompanyFactory.CreateAsync(_fx.ConnectionString, vatRegistered: true);
        var username = "oauthu" + TestIds.Suffix();

        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, co.CompanyId, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRbacAdminService>();

        var roleId = await svc.CreateRoleAsync(new CreateRoleRequest(
            "ROLE" + TestIds.Suffix(), "OAuth RBAC test role", null, null), default);
        await svc.SetRolePermissionsAsync(roleId, new SetRolePermissionsRequest(permissionCodes), default);
        var userId = await svc.CreateUserAsync(new CreateUserRequest(
            username, "Str0ng#Password!123", "OAuth RBAC test user", null,
            IsActive: true, new[] { roleId }, null), default);

        return (co.CompanyId, userId, roleId, username);
    }

    private async Task SetUserActiveAsync(long userId, bool isActive)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var user = await db.Users.FirstAsync(u => u.UserId == userId);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    private async Task RevokeRolePermissionAsync(int roleId, string permissionCode)
    {
        await using var sp = TestCompanyFactory.BuildProvider(_fx.ConnectionString, companyId: 1, branchId: 1);
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccountingDbContext>();
        var permissionId = await db.Permissions
            .Where(p => p.PermissionCode == permissionCode)
            .Select(p => p.PermissionId).FirstAsync();
        var rp = await db.RolePermissions
            .FirstAsync(x => x.RoleId == roleId && x.PermissionId == permissionId);
        db.RolePermissions.Remove(rp);
        await db.SaveChangesAsync();
    }

    // ── H4 ───────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task H4_consent_grant_is_capped_to_the_users_effective_rbac()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, userId, _, username) = await SeedRestrictedUserAsync("sales.tax_invoice.read");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (access, _) = await AuthorizeAsync(client, userId, username, companyId, isSuper: false,
            scope: "sales.tax_invoice.read sales.tax_invoice.create", offlineAccess: false);

        using var mcpHttp = factory.CreateClient();
        mcpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        await using var mcp = await ConnectAsync(mcpHttp);

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().Contain("list_tax_invoices", "the retained *.read scope must still work");
        names.Should().NotContain("create_tax_invoice_draft",
            "a user holding only *.read must never receive *.create in the issued token");

        var read = await mcp.CallToolAsync("list_tax_invoices", new Dictionary<string, object?>());
        read.IsError.Should().NotBe(true, "the retained read scope must actually resolve a tool call");

        var act = async () => await mcp.CallToolAsync(
            "create_tax_invoice_draft", new Dictionary<string, object?> { ["request"] = new { } });
        await act.Should().ThrowAsync<Exception>("the dropped create scope must reject an explicit call too");
    }

    [SkippableFact]
    public async Task H4_super_admin_consent_is_not_rbac_filtered()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Company 1's seeded admin (super-admin) — holds no explicit permission rows anywhere, so
        // the RBAC intersection would zero their grant; the super-admin bypass must keep both scopes.
        var (access, _) = await AuthorizeAsync(client, userId: 1, username: "admin", companyId: 1, isSuper: true,
            scope: "sales.tax_invoice.read sales.tax_invoice.create", offlineAccess: false);

        using var mcpHttp = factory.CreateClient();
        mcpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        await using var mcp = await ConnectAsync(mcpHttp);

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().Contain("create_tax_invoice_draft",
            "a super-admin's MCP consent is granted the full valid set, unfiltered by RBAC");
    }

    // ── M11 ──────────────────────────────────────────────────────────────────

    [SkippableFact]
    public async Task M11_refresh_is_rejected_when_the_user_is_deactivated()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, userId, _, username) =
            await SeedRestrictedUserAsync("sales.tax_invoice.read", "sales.tax_invoice.create");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, refresh) = await AuthorizeAsync(client, userId, username, companyId, isSuper: false,
            scope: "sales.tax_invoice.read sales.tax_invoice.create", offlineAccess: true);
        refresh.Should().NotBeNullOrEmpty();

        await SetUserActiveAsync(userId, isActive: false);

        using var refreshResp = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = refresh!, ["client_id"] = ClientId,
            }));
        refreshResp.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a refresh grant for a deactivated user must be rejected (invalid_grant), not reissued");
    }

    [SkippableFact]
    public async Task M11_refresh_re_derives_scopes_when_a_permission_is_revoked()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var (companyId, userId, roleId, username) =
            await SeedRestrictedUserAsync("sales.tax_invoice.read", "sales.tax_invoice.create");

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var (_, refresh) = await AuthorizeAsync(client, userId, username, companyId, isSuper: false,
            scope: "sales.tax_invoice.read sales.tax_invoice.create", offlineAccess: true);
        refresh.Should().NotBeNullOrEmpty();

        await RevokeRolePermissionAsync(roleId, "sales.tax_invoice.create");

        using var refreshResp = await client.PostAsync("/oauth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token", ["refresh_token"] = refresh!, ["client_id"] = ClientId,
            }));
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK,
            await refreshResp.Content.ReadAsStringAsync());
        var newAccess = JsonDocument.Parse(await refreshResp.Content.ReadAsStringAsync())
            .RootElement.GetProperty("access_token").GetString()!;

        using var mcpHttp = factory.CreateClient();
        mcpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        await using var mcp = await ConnectAsync(mcpHttp);

        var names = (await mcp.ListToolsAsync()).Select(t => t.Name).ToHashSet();
        names.Should().Contain("list_tax_invoices", "the still-held read permission must survive the refresh");
        names.Should().NotContain("create_tax_invoice_draft",
            "the refreshed token must re-derive scopes against CURRENT RBAC, not the grant baked at consent");
    }
}

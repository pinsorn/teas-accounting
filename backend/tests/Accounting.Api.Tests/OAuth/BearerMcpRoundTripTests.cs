using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Mcp;
using Accounting.Api.Tests.Rbac;
using Accounting.Application.Abstractions;
using Accounting.Application.Master;
using Accounting.Domain.Enums;
using Accounting.Infrastructure;
using Accounting.Infrastructure.Identity;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// P3 — the full OAuth round-trip proves the load-bearing insight: an OAuth Bearer, once validated
/// on /mcp, yields the SAME claims the X-Api-Key handler emits → is_api_key → CSV scopes → RLS →
/// mcpperm:* tool gate, for READS and WRITES. Also gates the regression (X-Api-Key still works),
/// the /mcp 401 WWW-Authenticate, and the XOR-credential guard.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class BearerMcpRoundTripTests
{
    private const string ClientId = "teas-mcp";
    private const string RedirectUri = "http://localhost:8765/callback";
    private readonly PostgresFixture _fx;
    public BearerMcpRoundTripTests(PostgresFixture fx) => _fx = fx;

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

    // Drive authorize (super-admin session grants for company 1) → code → token. Returns the Bearer.
    // Asserts the 302→code→200-token happy path along the way.
    private static async Task<string> AcquireOauthTokenAsync(HttpClient noRedirectClient)
    {
        var verifier = B64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        using var acceptReq = new HttpRequestMessage(HttpMethod.Post, "/oauth/authorize")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["scope"] = string.Join(' ', McpScopes.All),
                ["state"] = "xyz",
                ["resource"] = "http://localhost:3000/mcp",
                ["company_id"] = "1",
                ["approve"] = "true",
            }),
        };
        acceptReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", SessionJwt(1, "admin", 1, true));
        using var acceptResp = await noRedirectClient.SendAsync(acceptReq);
        var acceptBody = await acceptResp.Content.ReadAsStringAsync();
        acceptResp.StatusCode.Should().Be(HttpStatusCode.Found,
            $"consent accept → 302 to redirect_uri?code; got {(int)acceptResp.StatusCode}: {acceptBody}");
        var location = acceptResp.Headers.Location!.ToString();
        location.Should().StartWith(RedirectUri);
        var code = QueryHelpers.ParseQuery(new Uri(location).Query)["code"].ToString();
        code.Should().NotBeNullOrEmpty();

        using var tokenResp = await noRedirectClient.PostAsync("/oauth/token", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier,
            }));
        tokenResp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var tokenDoc = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
        var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();
        return accessToken!;
    }

    // (a) THE round-trip: session login → authorize/accept → code → token → /mcp Bearer READ tool call.
    [SkippableFact]
    public async Task Oauth_bearer_round_trip_calls_a_read_tool()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var accessToken = await AcquireOauthTokenAsync(client);

        using var mcpHttp = factory.CreateClient();
        mcpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await using var mcp = await ConnectAsync(mcpHttp);
        var result = await mcp.CallToolAsync("list_customers", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true, "an OAuth Bearer must resolve scopes + tenant like an mcp X-Api-Key");
    }

    // (e) WRITE — an OAuth agent creates a draft AND can poll its OWN drafts (first-class provenance:
    // CreatedViaApiKeyName = the oauth actor, so the E5 filter finds it and never collides with humans).
    [SkippableFact]
    public async Task Oauth_bearer_creates_a_draft_and_finds_it_in_pending_approvals()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var customerId = await SeedCustomerAsync();
        var productId = await SeedProductAsync();

        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var accessToken = await AcquireOauthTokenAsync(client);

        using var mcpHttp = factory.CreateClient();
        mcpHttp.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        await using var mcp = await ConnectAsync(mcpHttp);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var create = await mcp.CallToolAsync("create_quotation_draft", new Dictionary<string, object?>
        {
            ["request"] = new
            {
                docDate = today,
                validUntilDate = today.AddDays(30),
                customerId,
                businessUnitId = (int?)null,
                currencyCode = "THB",
                exchangeRate = 1m,
                notes = (string?)null,
                internalNotes = (string?)null,
                lines = new[]
                {
                    new { productId, descriptionTh = "OAuth draft", quantity = 1m, uomText = "ครั้ง",
                          unitPrice = 1000m, discountPercent = 0m,
                          taxCodeId = 0, taxCode = "NONE", taxRate = 0m, productType = (string?)null },
                },
            },
        });
        create.IsError.Should().NotBe(true, "an OAuth agent must be able to CREATE drafts, not only read");
        var draftId = JsonDocument.Parse(create.Content.OfType<TextContentBlock>().Single().Text)
            .RootElement.GetProperty("id").GetInt64();
        draftId.Should().BeGreaterThan(0);

        // E5: the same OAuth token polls its own pending drafts → the new draft is listed (proves the
        // CreatedViaApiKeyName provenance + own-drafts filter work for OAuth, not just X-Api-Key).
        var pending = await mcp.CallToolAsync("list_pending_approvals", new Dictionary<string, object?>());
        pending.IsError.Should().NotBe(true);
        pending.Content.OfType<TextContentBlock>().Single().Text
            .Should().Contain(draftId.ToString(), "the OAuth agent must find its own draft in pending approvals");
    }

    // (b) REGRESSION — an X-Api-Key mcp key still authorizes the SAME /mcp tool call (mcpperm: refactor safe).
    [SkippableFact]
    public async Task Xapikey_mcp_key_still_calls_a_tool_after_the_refactor()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        var key = await MintMcpKeyAsync();
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        await using var mcp = await ConnectAsync(http);
        var result = await mcp.CallToolAsync("list_customers", new Dictionary<string, object?>());
        result.IsError.Should().NotBe(true, "the X-Api-Key /mcp path (PR #29) must be unbroken");
    }

    // (c) No credential → 401 carrying WWW-Authenticate: Bearer resource_metadata (RFC 9728 discovery).
    [SkippableFact]
    public async Task Mcp_without_credential_returns_401_with_www_authenticate_bearer()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json, text/event-stream");
        var resp = await http.PostAsync("/mcp", new StringContent(
            """{"jsonrpc":"2.0","id":1,"method":"tools/list"}""", Encoding.UTF8, "application/json"));
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        resp.Headers.WwwAuthenticate.ToString().Should()
            .Contain("Bearer").And.Contain("oauth-protected-resource");
    }

    // (d) BOTH credentials → 400 (credential-confusion guard).
    [SkippableFact]
    public async Task Mcp_with_both_credentials_returns_400()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new RbacApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("X-Api-Key", "key_dummy0000000000");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "dummy.token");
        var resp = await http.SendAsync(req);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── seed helpers (company 1, via a stub-tenant SP — mirrors McpServerSmokeTests) ──
    private ServiceProvider Sp() =>
        (ServiceProvider)new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["ConnectionStrings:Postgres"] = _fx.ConnectionString }).Build())
            .AddSingleton<ITenantContext>(new StubTenant { CompanyId = 1, BranchId = 1, UserId = 1, IsSuperAdmin = false })
            .BuildServiceProvider();

    private async Task<long> SeedCustomerAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<ICustomerService>();
        return await svc.CreateAsync(new CreateCustomerRequest(
            Accounting.TestKit.TestIds.CustomerCode(), CustomerType.Corporate, "ลูกค้า OAuth", null,
            null, null, null, VatRegistered: false, null, null, null, null,
            CreditLimit: 0m, PaymentTermDays: 30, DefaultCurrency: "THB"), default);
    }

    private async Task<long> SeedProductAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProductService>();
        return await svc.CreateAsync(new CreateProductRequest(
            Accounting.TestKit.TestIds.ProductCode(), "บริการ OAuth", null, "SERVICE",
            "ครั้ง", DefaultUnitPrice: 9999m, null, null, null, null, null, IsSaleable: true), default);
    }

    private async Task<string> MintMcpKeyAsync()
    {
        await using var sp = Sp();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<Accounting.Application.Identity.IApiKeyService>();
        var created = await svc.CreateAsync(new Accounting.Application.Identity.CreateApiKeyRequest(
            Accounting.TestKit.TestIds.Name("mcp-rt"),
            McpScopes.All.ToArray(),
            Kind: Accounting.Domain.Entities.Identity.ApiKeyKinds.Mcp), default);
        return created.Plaintext;
    }
}

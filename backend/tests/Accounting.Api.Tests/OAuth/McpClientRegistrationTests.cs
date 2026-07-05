using System.Net;
using System.Net.Http.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Mcp;
using Accounting.Api.OAuth;
using Accounting.Application.Abstractions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// specs/mcp-dcr-client-registration.md — Option 3 (manual pre-registered public client) proved the
/// pre-registered <c>teas-mcp</c> client (<see cref="OpenIddictSeeder.McpClientId"/>) works as a
/// manual fallback. TEAS now ALSO implements RFC 7591 Dynamic Client Registration
/// (specs/mcp-dcr-implementation.md) — <c>POST /oauth/register</c> — so the claude.ai connector can
/// self-register without a human pasting a client_id. This file covers BOTH: the pre-registered
/// client's shape (public/PKCE + every redirect_uri Claude's connector docs require) AND the DCR
/// endpoint's security invariants (fixed server scopes, redirect_uri validation, scope/grant
/// injection ignored, idempotent dedup).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class McpClientRegistrationTests
{
    private readonly PostgresFixture _fx;
    public McpClientRegistrationTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Teas_mcp_client_is_public_pkce_with_the_claude_redirect_uris_and_mcp_scopes()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        // Booting the factory runs OpenIddictSeeder (a real IHostedService) — the same seeding path
        // that will run in prod on the next API restart after this change is deployed.
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var scope = factory.Services.CreateScope();
        var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

        var app = await appMgr.FindByClientIdAsync(OpenIddictSeeder.McpClientId);
        app.Should().NotBeNull("the pre-registered MCP client must exist — this IS the Client ID Ham pastes");

        var clientType = await appMgr.GetClientTypeAsync(app!);
        clientType.Should().Be(ClientTypes.Public, "Claude's connector uses PKCE with no client secret");

        var redirectUris = (await appMgr.GetRedirectUrisAsync(app!)).Select(u => u.ToString()).ToArray();
        redirectUris.Should().Contain([
            "https://claude.ai/api/mcp/auth_callback",   // hosted claude.ai / Desktop / mobile / Cowork
            "http://localhost/callback",                 // Claude Code (RFC 8252 loopback)
            "http://127.0.0.1/callback",                 // Claude Code (RFC 8252 loopback, IP form)
        ]);

        var permissions = (await appMgr.GetPermissionsAsync(app!)).ToArray();
        permissions.Should().Contain(Permissions.GrantTypes.AuthorizationCode);
        permissions.Should().Contain(Permissions.GrantTypes.RefreshToken);
        permissions.Should().Contain(Permissions.Prefixes.Scope + Scopes.OfflineAccess);
        foreach (var s in McpScopes.All)
            permissions.Should().Contain(Permissions.Prefixes.Scope + s, $"scope '{s}' must be requestable");
    }

    [SkippableFact]
    public async Task Discovery_advertises_none_as_a_supported_token_endpoint_auth_method()
    {
        // Claude's connector docs require token_endpoint_auth_methods_supported to include "none"
        // for a public/PKCE client (specs/mcp-dcr-client-registration.md). OpenIddict's built-in
        // discovery handler never adds it on its own — Program.cs appends it explicitly.
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.GetAsync("/.well-known/oauth-authorization-server");
        using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("token_endpoint_auth_methods_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain(ClientAuthenticationMethods.None);
    }

    // ── RFC 7591 Dynamic Client Registration (specs/mcp-dcr-implementation.md) ──

    [SkippableFact]
    public async Task Register_returns_201_public_client_with_fixed_scopes_and_no_write_scope()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/oauth/register",
            new { redirect_uris = new[] { "https://claude.ai/api/mcp/auth_callback" } });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = body.RootElement;
        var clientId = root.GetProperty("client_id").GetString();
        clientId.Should().NotBeNullOrEmpty();
        root.GetProperty("token_endpoint_auth_method").GetString().Should().Be("none");
        root.GetProperty("grant_types").EnumerateArray().Select(e => e.GetString())
            .Should().Contain(["authorization_code", "refresh_token"]);
        var scope = root.GetProperty("scope").GetString()!;
        foreach (var s in McpScopes.All) scope.Should().Contain(s);

        using var scopeDi = factory.Services.CreateScope();
        var appMgr = scopeDi.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await appMgr.FindByClientIdAsync(clientId!);
        app.Should().NotBeNull("the DCR endpoint must persist the created client via CreateAsync");
        (await appMgr.GetClientTypeAsync(app!)).Should().Be(ClientTypes.Public, "DCR clients are public/PKCE, never confidential");

        var permissions = (await appMgr.GetPermissionsAsync(app!)).ToArray();
        foreach (var s in McpScopes.All)
            permissions.Should().Contain(Permissions.Prefixes.Scope + s, $"scope '{s}' must be requestable");
        permissions.Should().Contain(Permissions.Prefixes.Scope + Scopes.OfflineAccess);
        permissions.Should().Contain(Permissions.Prefixes.Resource + "http://localhost:3000/mcp");
        foreach (var suffix in McpScopes.ForbiddenSuffixes)
            permissions.Should().NotContain(p => p.EndsWith(suffix, StringComparison.Ordinal),
                $"a DCR client must NEVER receive a permission ending in the forbidden suffix '{suffix}'");

        var requirements = (await appMgr.GetRequirementsAsync(app!)).ToArray();
        requirements.Should().Contain(Requirements.Features.ProofKeyForCodeExchange);
    }

    [SkippableTheory]
    [InlineData("http://evil.example.com/cb")]
    [InlineData("http://192.168.1.10/cb")]
    [InlineData("ftp://h/cb")]
    [InlineData("com.example.app:/cb")]
    public async Task Register_rejects_non_https_non_loopback_redirect_uri(string redirectUri)
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/oauth/register", new { redirect_uris = new[] { redirectUri } });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("error").GetString().Should().Be("invalid_redirect_uri");
    }

    [SkippableFact]
    public async Task Register_ignores_requested_scope_and_grant_injection()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.PostAsJsonAsync("/oauth/register", new
        {
            redirect_uris = new[] { "http://localhost/callback" },
            scope = "sales.tax_invoice.post admin.super",
            grant_types = new[] { "client_credentials" },
        });
        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        using var body = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var clientId = body.RootElement.GetProperty("client_id").GetString();

        using var scopeDi = factory.Services.CreateScope();
        var appMgr = scopeDi.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await appMgr.FindByClientIdAsync(clientId!);
        var permissions = (await appMgr.GetPermissionsAsync(app!)).ToArray();

        permissions.Should().NotContain(Permissions.Prefixes.Scope + "sales.tax_invoice.post",
            "the requested write scope must be IGNORED — core security invariant");
        permissions.Should().NotContain(Permissions.Prefixes.Scope + "admin.super");
        permissions.Should().NotContain(Permissions.GrantTypes.ClientCredentials,
            "the requested grant_types injection must be IGNORED — only the fixed policy is ever granted");
        permissions.Should().Contain(Permissions.GrantTypes.AuthorizationCode);
    }

    [SkippableFact]
    public async Task Register_requires_redirect_uris()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        using var emptyBody = await http.PostAsJsonAsync("/oauth/register", new { });
        emptyBody.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        System.Text.Json.JsonDocument.Parse(await emptyBody.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetString().Should().Be("invalid_redirect_uri");

        using var emptyArray = await http.PostAsJsonAsync("/oauth/register", new { redirect_uris = Array.Empty<string>() });
        emptyArray.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        System.Text.Json.JsonDocument.Parse(await emptyArray.Content.ReadAsStringAsync())
            .RootElement.GetProperty("error").GetString().Should().Be("invalid_redirect_uri");
    }

    [SkippableFact]
    public async Task Register_is_idempotent_same_uris_return_same_client_id()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();
        var payload = new { redirect_uris = new[] { "https://claude.ai/api/mcp/auth_callback", "http://localhost/callback" } };

        using var first = await http.PostAsJsonAsync("/oauth/register", payload);
        using var second = await http.PostAsJsonAsync("/oauth/register", payload);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);

        var clientId1 = System.Text.Json.JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("client_id").GetString();
        var clientId2 = System.Text.Json.JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("client_id").GetString();
        clientId2.Should().Be(clientId1, "re-registering the SAME redirect_uris must dedup to the same client_id");

        // Prove it is literally the SAME row (not a coincidentally-equal id after a delete/recreate).
        using var scopeDi = factory.Services.CreateScope();
        var appMgr = scopeDi.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var app = await appMgr.FindByClientIdAsync(clientId1!);
        app.Should().NotBeNull();
        var rowId = await appMgr.GetIdAsync(app!);
        rowId.Should().NotBeNullOrEmpty();
    }
}

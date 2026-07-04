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
/// specs/mcp-dcr-client-registration.md — Option 3 (manual pre-registered public client): TEAS has
/// no Dynamic Client Registration, so Claude's connector falls back to "add an OAuth Client ID".
/// This proves the pre-registered <c>teas-mcp</c> client (<see cref="OpenIddictSeeder.McpClientId"/>)
/// — the id Ham pastes into the connector — is public/PKCE and carries every redirect_uri Claude's
/// connector docs require: the hosted claude.ai callback + both RFC 8252 loopback forms (Claude Code).
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
}

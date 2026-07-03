using System.Net;
using System.Text.Json;
using Accounting.Api.Tests.Fixtures;
using Accounting.Api.Tests.Mcp;
using FluentAssertions;
using Xunit;

namespace Accounting.Api.Tests.OAuth;

/// <summary>
/// P1 smoke — the OAuth discovery documents resolve anonymously so an MCP native connector can
/// bootstrap: RFC 9728 protected-resource metadata → RFC 8414 authorization-server metadata.
/// Boots the real API against teas_test (reuses <see cref="McpApiFactory"/>).
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DiscoveryEndpointsTests
{
    private readonly PostgresFixture _fx;
    public DiscoveryEndpointsTests(PostgresFixture fx) => _fx = fx;

    [SkippableFact]
    public async Task Protected_resource_metadata_is_anonymous_and_points_at_mcp()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.GetAsync("/.well-known/oauth-protected-resource");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("resource").GetString().Should().EndWith("/mcp");
        root.GetProperty("authorization_servers").GetArrayLength().Should().BeGreaterThan(0);
        root.GetProperty("scopes_supported").GetArrayLength().Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public async Task Authorization_server_metadata_advertises_pkce_and_grants()
    {
        Skip.If(_fx.SkipReason is not null, _fx.SkipReason);
        await using var factory = new McpApiFactory(_fx.ConnectionString);
        using var http = factory.CreateClient();

        var resp = await http.GetAsync("/.well-known/oauth-authorization-server");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        root.GetProperty("token_endpoint").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("authorization_endpoint").GetString().Should().NotBeNullOrEmpty();
        root.GetProperty("code_challenge_methods_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain("S256");
        root.GetProperty("grant_types_supported").EnumerateArray()
            .Select(e => e.GetString()).Should().Contain(["authorization_code", "refresh_token"]);
    }
}

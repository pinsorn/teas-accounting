using Accounting.Api.Mcp;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// Seeds the OAuth scopes (<see cref="McpScopes.All"/>) and a pre-registered public MCP client
/// (<c>teas-mcp</c>) whose permissions are FIXED server-side to the read+create+manage scope set —
/// never a *.post scope, so a client can't self-grant write authority. Idempotent; runs once at
/// startup. Clients that can't do DCR use this pre-registered client (spec §6 manual fallback).
/// </summary>
public sealed class OpenIddictSeeder(IServiceProvider services) : IHostedService
{
    public const string McpClientId = "teas-mcp";

    // Loopback callback for local/dev + the integration round-trip, PLUS the Claude connector's
    // documented callbacks (mcp-dcr-client-registration.md, Option 3 — manual pre-registered public
    // client; claude.com/docs/connectors/building/authentication): the hosted claude.ai/Desktop/
    // mobile/Cowork callback, and the two RFC 8252 native-app loopback forms Claude Code uses.
    // Additional per-client redirect URIs (Codex / Gemini) are supplied via config
    // `Oauth:RedirectUris` (env Oauth__RedirectUris__N) and unioned in — re-asserted each startup,
    // so adding a client's callback is a config change + restart, not a redeploy
    // (docs/mcp-oauth-deploy-gates.md).
    private static readonly string[] DefaultRedirectUris =
    [
        "http://localhost:8765/callback",
        "https://claude.ai/api/mcp/auth_callback",
        "http://localhost/callback",
        "http://127.0.0.1/callback",
    ];

    public async Task StartAsync(CancellationToken ct)
    {
        using var scope = services.CreateScope();

        // RFC 8707: the MCP resource each scope grants access to. Registering it on the scopes lets
        // OpenIddict accept the client's `resource` param and encode it in the token aud. Env-specific
        // (public base URL) → re-asserted each startup so a base-URL change (or a stale teas_test row)
        // is reconciled. Must equal what McpPrincipalFactory sets + what the resource server checks.
        var mcpResource = $"{scope.ServiceProvider.GetRequiredService<IOptions<AppOptions>>().Value.BaseUrl.TrimEnd('/')}/mcp";

        var scopeMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in McpScopes.All)
        {
            var scopeDescriptor = new OpenIddictScopeDescriptor { Name = name, Resources = { mcpResource } };
            var existingScope = await scopeMgr.FindByNameAsync(name, ct);
            if (existingScope is null) await scopeMgr.CreateAsync(scopeDescriptor, ct);
            else await scopeMgr.UpdateAsync(existingScope, scopeDescriptor, ct);   // reconcile the resource
        }

        var configured = scope.ServiceProvider.GetRequiredService<IConfiguration>()
            .GetSection("Oauth:RedirectUris").Get<string[]>() ?? [];
        var redirectUris = DefaultRedirectUris.Union(configured, StringComparer.OrdinalIgnoreCase)
                                              .Select(u => new Uri(u));
        var descriptor = McpClientDescriptorFactory.Build(
            McpClientId, "TEAS Connect (MCP)", mcpResource, redirectUris);

        var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var existing = await appMgr.FindByClientIdAsync(McpClientId, ct);
        if (existing is null) await appMgr.CreateAsync(descriptor, ct);
        else await appMgr.UpdateAsync(existing, descriptor, ct);   // re-assert server-fixed permissions
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

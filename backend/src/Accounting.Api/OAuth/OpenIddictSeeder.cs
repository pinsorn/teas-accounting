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

    // Loopback callback for local/dev + the integration round-trip. Real per-client redirect URIs
    // (Claude / Codex / Gemini) are added at deploy time (docs/mcp-oauth-deploy-gates.md).
    private static readonly string[] RedirectUris = ["http://localhost:8765/callback"];

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

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = McpClientId,
            ClientType = ClientTypes.Public,        // native connectors — PKCE, no secret
            ConsentType = ConsentTypes.Explicit,    // our /oauth/consent page approves each grant
            DisplayName = "TEAS Connect (MCP)",
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,   // long-running agents: refresh
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in RedirectUris) descriptor.RedirectUris.Add(new Uri(uri));
        // Grant ONLY the fixed MCP scope set (server-authoritative — clients can never add *.post).
        foreach (var s in McpScopes.All) descriptor.Permissions.Add(Permissions.Prefixes.Scope + s);
        // Permit the client to target the MCP resource (RFC 8707) — else invalid_request on `resource`.
        descriptor.Permissions.Add(Permissions.Prefixes.Resource + mcpResource);

        var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var existing = await appMgr.FindByClientIdAsync(McpClientId, ct);
        if (existing is null) await appMgr.CreateAsync(descriptor, ct);
        else await appMgr.UpdateAsync(existing, descriptor, ct);   // re-assert server-fixed permissions
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

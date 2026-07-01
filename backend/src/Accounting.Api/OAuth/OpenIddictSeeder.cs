using Accounting.Application.Abstractions;
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

        var scopeMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
        foreach (var name in McpScopes.All)
        {
            if (await scopeMgr.FindByNameAsync(name, ct) is not null) continue;
            await scopeMgr.CreateAsync(new OpenIddictScopeDescriptor { Name = name }, ct);
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
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in RedirectUris) descriptor.RedirectUris.Add(new Uri(uri));
        // Grant ONLY the fixed MCP scope set (server-authoritative — clients can never add *.post).
        foreach (var s in McpScopes.All) descriptor.Permissions.Add(Permissions.Prefixes.Scope + s);

        var appMgr = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var existing = await appMgr.FindByClientIdAsync(McpClientId, ct);
        if (existing is null) await appMgr.CreateAsync(descriptor, ct);
        else await appMgr.UpdateAsync(existing, descriptor, ct);   // re-assert server-fixed permissions
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}

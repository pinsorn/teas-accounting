using Accounting.Application.Abstractions;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Accounting.Api.OAuth;

/// <summary>
/// The server-authoritative descriptor for an MCP public/PKCE client. The scope/permission policy is
/// FIXED here (McpScopes.All + offline_access + the MCP resource) and lives ONLY here — both
/// OpenIddictSeeder (teas-mcp) and the DCR endpoint call this, so a client can NEVER self-grant a
/// *.post write scope. Requested scopes are never an input (spec §3, core security invariant).
/// </summary>
public static class McpClientDescriptorFactory
{
    public static OpenIddictApplicationDescriptor Build(
        string clientId, string displayName, string mcpResource, IEnumerable<Uri> redirectUris)
    {
        var d = new OpenIddictApplicationDescriptor
        {
            ClientId    = clientId,
            ClientType  = ClientTypes.Public,       // PKCE, no secret
            ConsentType = ConsentTypes.Explicit,    // /oauth/consent gates every grant + company pick
            DisplayName = displayName,
            Permissions =
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + Scopes.OfflineAccess,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var s in McpScopes.All) d.Permissions.Add(Permissions.Prefixes.Scope + s);
        d.Permissions.Add(Permissions.Prefixes.Resource + mcpResource);
        foreach (var u in redirectUris) d.RedirectUris.Add(u);   // store the parsed Uri AS-IS (mirror seeder)
        return d;
    }
}

using Accounting.Api.Mcp;
using Accounting.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Accounting.Api.OAuth;

public static class OAuthEndpoints
{
    /// <summary>
    /// RFC 9728 protected-resource metadata — ANONYMOUS. An MCP client that gets a 401 with
    /// <c>WWW-Authenticate: Bearer resource_metadata="…"</c> fetches this to discover the AS + scopes.
    /// </summary>
    public static IEndpointRouteBuilder MapOAuthMetadata(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/oauth-protected-resource", (IOptions<AppOptions> opt) =>
        {
            var baseUrl = opt.Value.BaseUrl.TrimEnd('/');
            return Results.Json(new
            {
                resource = $"{baseUrl}/mcp",
                authorization_servers = new[] { baseUrl },
                scopes_supported = McpScopes.All,
                bearer_methods_supported = new[] { "header" },
            });
        })
        .AllowAnonymous()
        .WithName("OAuthProtectedResource");

        return app;
    }
}

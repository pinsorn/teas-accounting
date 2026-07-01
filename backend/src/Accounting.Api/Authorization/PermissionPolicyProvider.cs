using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using OpenIddict.Validation.AspNetCore;

namespace Accounting.Api.Authorization;

/// <summary>
/// Auto-generates an AuthorizationPolicy for any policy name starting with "perm:".
/// Lets endpoints declare permissions as strings: <c>.RequireAuthorization("perm:sales.tax_invoice.post")</c>.
/// </summary>
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";
    /// <summary>Sprint 14 — pins the ApiKey scheme (so a scheme-less default
    /// JWT can't clobber the API-key principal) + the scope requirement.
    /// Root keeps <see cref="PolicyPrefix"/> (JWT-default) → auth isolation.
    /// Used by the /api/v1 external surface (ApiKey ONLY — never Bearer).</summary>
    public const string ApiKeyPolicyPrefix = "apiperm:";

    /// <summary>MCP OAuth (2026-07-01) — like <see cref="ApiKeyPolicyPrefix"/> but ALSO accepts the
    /// OpenIddict OAuth Bearer (used only on /mcp). Both schemes emit is_api_key=true → PermissionHandler
    /// reads the CSV scopes. Kept SEPARATE from apiperm: so /api/v1 stays ApiKey-only (ASP.NET unions
    /// a policy's schemes, so a shared prefix would silently open /api/v1 to Bearer).</summary>
    public const string McpPolicyPrefix = "mcpperm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()  => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // MCP OAuth: ApiKey OR OpenIddict Bearer (checked before apiperm: — "mcpperm:" also
        // starts with neither of the others, but order-independent since prefixes are distinct).
        if (policyName.StartsWith(McpPolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[McpPolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(
                    ApiKeyAuthenticationHandler.SchemeName,
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        if (policyName.StartsWith(ApiKeyPolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[ApiKeyPolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)   // /api/v1 — ApiKey ONLY
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[PolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();
            return Task.FromResult<AuthorizationPolicy?>(policy);
        }
        return _fallback.GetPolicyAsync(policyName);
    }
}

public static class PermissionAuthorizationExtensions
{
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddAuthorization();
        return services;
    }
}

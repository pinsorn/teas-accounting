using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

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
    /// Root keeps <see cref="PolicyPrefix"/> (JWT-default) → auth isolation.</summary>
    public const string ApiKeyPolicyPrefix = "apiperm:";

    private readonly DefaultAuthorizationPolicyProvider _fallback;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) =>
        _fallback = new DefaultAuthorizationPolicyProvider(options);

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()  => _fallback.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _fallback.GetFallbackPolicyAsync();

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(ApiKeyPolicyPrefix, StringComparison.Ordinal))
        {
            var permission = policyName[ApiKeyPolicyPrefix.Length..];
            var policy = new AuthorizationPolicyBuilder()
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
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

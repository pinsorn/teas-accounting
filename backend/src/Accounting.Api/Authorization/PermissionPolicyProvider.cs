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

    /// <summary>fix-c1-backend-cleanup item 2 — NAMED policy (not the dynamic McpPolicyPrefix
    /// above, which is single-permission-exact-match only, see PermissionHandler). Back-compat
    /// fallback for the master.employee.manage → master.employee.lookup MCP scope narrowing:
    /// succeeds for EITHER permission, so an already-issued API key still holding the OLD
    /// broader "manage" grant keeps calling list_employees without a 403, while a NEW key only
    /// ever needs to request the narrower "lookup" scope.</summary>
    public const string McpEmployeeLookupOrManagePolicy = "mcp.employee.lookup_or_manage";

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
    // RequireAssertion (not a second IAuthorizationRequirement/Handler pair) because this is a
    // one-off two-permission OR, not a general mechanism — the McpScopes.cs comment on
    // report.read notes deliberately NOT building an OR-of-perms system for the catalog; this
    // reuses PermissionHandler's own exact-match rule via its static HasPermission, evaluated twice.
    public static IServiceCollection AddPermissionAuthorization(this IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
        services.AddAuthorization(options => options.AddPolicy(
            PermissionPolicyProvider.McpEmployeeLookupOrManagePolicy,
            policy => policy
                .AddAuthenticationSchemes(
                    ApiKeyAuthenticationHandler.SchemeName,
                    OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                    PermissionHandler.HasPermission(ctx.User, Permissions.Master.EmployeeLookup) ||
                    PermissionHandler.HasPermission(ctx.User, Permissions.Master.EmployeeManage))));
        return services;
    }
}

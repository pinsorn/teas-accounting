using Accounting.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;

namespace Accounting.Api.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public PermissionRequirement(string permission) => Permission = permission;
}

public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement requirement)
    {
        // Sprint 14 P6 — external API key: authorize against the key's
        // ScopesJson (a CSV claim), NOT role-permission claims. A key never
        // gets super-admin bypass.
        if (string.Equals(ctx.User.FindFirst(TenantClaims.IsApiKey)?.Value, "true",
                StringComparison.OrdinalIgnoreCase))
        {
            var scopes = ctx.User.FindFirst(TenantClaims.Scopes)?.Value ?? "";
            var has = scopes.Split(',', StringSplitOptions.RemoveEmptyEntries
                                       | StringSplitOptions.TrimEntries)
                .Contains(requirement.Permission, StringComparer.Ordinal);
            if (has) ctx.Succeed(requirement);
            return Task.CompletedTask;
        }

        // JWT user — super admins bypass per-permission checks (CLAUDE.md §4.1).
        var isSuperAdmin = string.Equals(
            ctx.User.FindFirst(TenantClaims.IsSuperAdmin)?.Value, "true",
            StringComparison.OrdinalIgnoreCase);

        if (isSuperAdmin ||
            ctx.User.HasClaim(c => c.Type == TenantClaims.Permission && c.Value == requirement.Permission))
        {
            ctx.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
